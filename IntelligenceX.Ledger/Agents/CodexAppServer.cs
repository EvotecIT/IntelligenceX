using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace IntelligenceX.Ledger.Agents;

/// <summary>A JSON-RPC connection to the Codex app server through the official CLI. When the Codex app runs, the
/// connection goes to its daemon with <c>codex app-server proxy</c> (WebSocket frames over the proxy's stdio), so
/// threads and turns are the app's own. Without the app, a private <c>codex app-server</c> serves the same protocol
/// over JSON lines. Server-initiated requests (approvals) are left to the app's own clients and never answered here.</summary>
internal sealed class CodexAppServer : IAsyncDisposable {
    private const int MaximumMessageBytes = 16 * 1024 * 1024;
    private readonly Process _process;
    private readonly WebSocket? _socket;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly CancellationTokenSource _closing = new();
    private readonly Task _reader;
    private long _next;
    private Exception? _failure;

    private CodexAppServer(Process process, WebSocket? socket) {
        _process = process;
        _socket = socket;
        _reader = Task.Run(ReadLoopAsync);
    }

    /// <summary>True when connected to the Codex app's daemon rather than a private server.</summary>
    public bool Live => _socket is not null;
    public bool Closed => _failure is not null;
    /// <summary>Completes when the connection ends, whether closed here or lost (the server exited).</summary>
    public Task Ended => _reader;
    /// <summary>Raised for every server notification, on the reader thread.</summary>
    public event Action<string, JsonElement>? Notification;
    /// <summary>Raised when the server sends a request to clients (for example an approval).</summary>
    public event Action<string, JsonElement>? ServerRequest;

    /// <summary>The Codex app's control socket, from CODEX_HOME or ~/.codex.</summary>
    internal static string DefaultControlSocket() {
        var home = Environment.GetEnvironmentVariable("CODEX_HOME") is { Length: > 0 } configured ? configured
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        return Path.Combine(home, "app-server-control", "app-server-control.sock");
    }

    /// <summary>Connects to the Codex app's daemon when its control socket answers, otherwise starts a private server.
    /// On Windows the app serves its threads over stdio and its control socket is usually stale, so the private
    /// server is the normal route there.</summary>
    public static async Task<CodexAppServer> OpenAsync(string codex, string? controlSocket, CancellationToken token) {
        var socketPath = controlSocket ?? DefaultControlSocket();
        if (TryLive(socketPath, out var socketStamp)) {
            try { return await OpenAsync(codex, socketPath, live: true, token).ConfigureAwait(false); }
            catch (Exception error) when (error is IOException or TimeoutException or WebSocketException or OperationCanceledException &&
                !token.IsCancellationRequested) {
                MarkDead(socketPath, socketStamp);
            }
        }
        return await OpenAsync(codex, socketPath, live: false, token).ConfigureAwait(false);
    }

    /// <summary>Control sockets that didn't answer, with the socket file's write time then. A daemon that died leaves
    /// its socket file behind; trying it costs every connection the handshake and initialize timeouts first, so it is
    /// skipped until the file changes (a daemon that starts again recreates it).</summary>
    private static readonly ConcurrentDictionary<string, DateTime> DeadSockets = new(StringComparer.Ordinal);

    internal static bool TryLive(string socketPath, out DateTime stamp) {
        stamp = default;
        if (!File.Exists(socketPath)) return false;
        stamp = File.GetLastWriteTimeUtc(socketPath);
        if (DeadSockets.TryGetValue(socketPath, out var dead) && dead == stamp) return false;
        DeadSockets.TryRemove(socketPath, out _);
        return true;
    }

    internal static void MarkDead(string socketPath, DateTime stamp) => DeadSockets[socketPath] = stamp;

    private static async Task<CodexAppServer> OpenAsync(string codex, string socketPath, bool live, CancellationToken token) {
        var start = new ProcessStartInfo(codex) {
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true
        };
        start.ArgumentList.Add("app-server");
        if (live) { start.ArgumentList.Add("proxy"); start.ArgumentList.Add("--sock"); start.ArgumentList.Add(socketPath); }
        var process = Process.Start(start) ?? throw new IOException("codex app-server did not start.");
        _ = process.StandardError.BaseStream.CopyToAsync(Stream.Null, CancellationToken.None);
        CodexAppServer? server = null;
        try {
            WebSocket? socket = null;
            if (live) {
                var stream = new ProcessDuplexStream(process);
                await HandshakeAsync(stream, token).ConfigureAwait(false);
                socket = WebSocket.CreateFromStream(stream, new WebSocketCreationOptions { IsServer = false, KeepAliveInterval = TimeSpan.Zero });
            }
            server = new CodexAppServer(process, socket);
            await server.CallAsync("initialize", new JsonObject {
                ["clientInfo"] = new JsonObject { ["name"] = "intelligencex-ledger-agent", ["version"] = "1.0.0" },
                ["capabilities"] = new JsonObject { ["experimentalApi"] = true }
            }, TimeSpan.FromSeconds(20), token).ConfigureAwait(false);
            await server.SendAsync(new JsonObject { ["method"] = "initialized", ["params"] = new JsonObject() }, token).ConfigureAwait(false);
            return server;
        } catch {
            if (server is not null) await server.DisposeAsync().ConfigureAwait(false);
            else Stop(process);
            throw;
        }
    }

    private static async Task HandshakeAsync(Stream stream, CancellationToken token) {
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        var request = "GET / HTTP/1.1\r\nHost: localhost\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n" +
            "Sec-WebSocket-Key: " + key + "\r\nSec-WebSocket-Version: 13\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(request), token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);
        // Read the response header byte by byte so no WebSocket frame is consumed with it.
        var header = new List<byte>();
        var one = new byte[1];
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        while (header.Count < 8192) {
            if (await stream.ReadAsync(one, deadline.Token).ConfigureAwait(false) == 0) throw new IOException("Codex control proxy closed during the handshake.");
            header.Add(one[0]);
            if (header.Count >= 4 && header[^4] == '\r' && header[^3] == '\n' && header[^2] == '\r' && header[^1] == '\n') break;
        }
        var text = Encoding.ASCII.GetString(header.ToArray());
        if (!text.StartsWith("HTTP/1.1 101", StringComparison.Ordinal)) throw new IOException("Codex control proxy refused the WebSocket upgrade.");
        var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
        if (!text.Contains("Sec-WebSocket-Accept: " + accept, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Codex control proxy returned an invalid WebSocket accept key.");
    }

    public async Task<JsonElement> CallAsync(string method, JsonObject? parameters, TimeSpan timeout, CancellationToken token) {
        if (_failure is not null) throw new IOException("Codex app server connection closed.", _failure);
        var id = Interlocked.Increment(ref _next);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = completion;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeout);
        try {
            await SendAsync(Request(id, method, parameters), deadline.Token).ConfigureAwait(false);
            return await completion.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
        } catch (OperationCanceledException) when (!token.IsCancellationRequested) {
            throw new TimeoutException("Codex app server did not answer " + method + ".");
        } finally { _pending.TryRemove(id, out _); }
    }

    /// <summary>One request message. It carries a copy of the parameters, so a caller can send the same parameters again,
    /// as the fork of an archived thread does after unarchiving it.</summary>
    internal static JsonObject Request(long id, string method, JsonObject? parameters) =>
        new() { ["id"] = id, ["method"] = method, ["params"] = parameters?.DeepClone() ?? new JsonObject() };

    private async Task SendAsync(JsonObject message, CancellationToken token) {
        var bytes = Encoding.UTF8.GetBytes(message.ToJsonString());
        await _writeGate.WaitAsync(token).ConfigureAwait(false);
        try {
            if (_socket is not null) await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, token).ConfigureAwait(false);
            else {
                var stdin = _process.StandardInput.BaseStream;
                await stdin.WriteAsync(bytes, token).ConfigureAwait(false);
                await stdin.WriteAsync("\n"u8.ToArray(), token).ConfigureAwait(false);
                await stdin.FlushAsync(token).ConfigureAwait(false);
            }
        } finally { _writeGate.Release(); }
    }

    private async Task ReadLoopAsync() {
        try {
            if (_socket is not null) {
                var buffer = new byte[64 * 1024];
                using var message = new MemoryStream();
                while (!_closing.IsCancellationRequested) {
                    var received = await _socket.ReceiveAsync(buffer, _closing.Token).ConfigureAwait(false);
                    if (received.MessageType == WebSocketMessageType.Close) throw new IOException("Codex app server closed the connection.");
                    message.Write(buffer, 0, received.Count);
                    if (message.Length > MaximumMessageBytes) throw new InvalidDataException("Codex message exceeds its bound.");
                    if (!received.EndOfMessage) continue;
                    Dispatch(message.ToArray());
                    message.SetLength(0);
                }
            } else {
                var reader = _process.StandardOutput;
                while (!_closing.IsCancellationRequested) {
                    var line = await reader.ReadLineAsync(_closing.Token).ConfigureAwait(false) ?? throw new IOException("Codex app server exited.");
                    if (line.Length > MaximumMessageBytes) throw new InvalidDataException("Codex message exceeds its bound.");
                    if (line.Length > 0) Dispatch(Encoding.UTF8.GetBytes(line));
                }
            }
        } catch (Exception error) {
            _failure = error;
            foreach (var pending in _pending.Values) pending.TrySetException(new IOException("Codex app server connection closed.", error));
        }
    }

    private void Dispatch(byte[] bytes) {
        JsonElement root;
        try { root = JsonDocument.Parse(bytes).RootElement.Clone(); } catch (JsonException) { return; }
        if (root.ValueKind != JsonValueKind.Object) return;
        var hasId = root.TryGetProperty("id", out var id);
        var hasMethod = root.TryGetProperty("method", out var method) && method.ValueKind == JsonValueKind.String;
        if (hasId && !hasMethod) {
            if (id.ValueKind != JsonValueKind.Number || !_pending.TryGetValue(id.GetInt64(), out var pending)) return;
            if (root.TryGetProperty("error", out var error))
                pending.TrySetException(new CodexRpcException(error.TryGetProperty("message", out var text) ? text.GetString() ?? "" : "",
                    error.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.Number ? code.GetInt32() : 0));
            else pending.TrySetResult(root.TryGetProperty("result", out var result) ? result : default);
            return;
        }
        if (!hasMethod) return;
        var parameters = root.TryGetProperty("params", out var value) ? value : default;
        try {
            if (hasId) ServerRequest?.Invoke(method.GetString()!, parameters);
            else Notification?.Invoke(method.GetString()!, parameters);
        } catch (Exception) { /* Observers own their failures; the connection keeps reading. */ }
    }

    private int _disposed;

    public async ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        _closing.Cancel();
        try {
            if (_socket is { State: WebSocketState.Open }) {
                using var close = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, close.Token).ConfigureAwait(false);
            }
        } catch (Exception error) when (error is WebSocketException or IOException or OperationCanceledException or ObjectDisposedException) { }
        _socket?.Dispose();
        Stop(_process);
        try { await _reader.ConfigureAwait(false); } catch (Exception) { }
        _closing.Dispose();
        _writeGate.Dispose();
    }

    private static void Stop(Process process) {
        try { process.StandardInput.Close(); } catch (Exception error) when (error is IOException or InvalidOperationException) { }
        try { if (!process.WaitForExit(3000)) process.Kill(entireProcessTree: true); }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        process.Dispose();
    }

    /// <summary>The proxy's stdout and stdin as one stream for the WebSocket client.</summary>
    private sealed class ProcessDuplexStream(Process process) : Stream {
        private readonly Stream _input = process.StandardOutput.BaseStream;
        private readonly Stream _output = process.StandardInput.BaseStream;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => _output.Flush();
        public override Task FlushAsync(CancellationToken token) => _output.FlushAsync(token);
        public override int Read(byte[] buffer, int offset, int count) => _input.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) => _input.ReadAsync(buffer, token);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken token) => _input.ReadAsync(buffer, offset, count, token);
        public override void Write(byte[] buffer, int offset, int count) { _output.Write(buffer, offset, count); _output.Flush(); }
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token = default) {
            await _output.WriteAsync(buffer, token).ConfigureAwait(false);
            await _output.FlushAsync(token).ConfigureAwait(false);
        }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken token) =>
            WriteAsync(buffer.AsMemory(offset, count), token).AsTask();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}

internal sealed class CodexRpcException(string message, int code) : IOException(message) {
    public int Code { get; } = code;
}
