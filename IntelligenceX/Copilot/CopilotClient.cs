using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.Rpc;

namespace IntelligenceX.Copilot;

public sealed class CopilotClient : IDisposable, IAsyncDisposable {
    private readonly CopilotClientOptions _options;
    private readonly ConcurrentDictionary<string, CopilotSession> _sessions = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _cts = new();
    private Process? _process;
    private TcpClient? _tcpClient;
    private NetworkStream? _networkStream;
    private HeaderDelimitedMessageTransport? _transport;
    private JsonRpcClient? _rpc;
    private Task? _readerTask;
    private Task? _stderrTask;
    private bool _disposed;

    private CopilotClient(CopilotClientOptions options) {
        _options = options;
    }

    public event EventHandler<string>? StandardErrorReceived;

    public static async Task<CopilotClient> StartAsync(CopilotClientOptions? options = null, CancellationToken cancellationToken = default) {
        var client = new CopilotClient(options ?? new CopilotClientOptions());
        await client.StartCoreAsync(cancellationToken).ConfigureAwait(false);
        return client;
    }

    public async Task<CopilotStatus> GetStatusAsync(CancellationToken cancellationToken = default) {
        var result = await CallAsync("status.get", JsonValue.From(new JsonArray()), cancellationToken).ConfigureAwait(false);
        var obj = result?.AsObject();
        if (obj is null) {
            return new CopilotStatus();
        }
        return new CopilotStatus {
            Version = obj.GetString("version") ?? string.Empty,
            ProtocolVersion = (int)(obj.GetInt64("protocolVersion") ?? 0)
        };
    }

    public async Task<CopilotAuthStatus> GetAuthStatusAsync(CancellationToken cancellationToken = default) {
        var result = await CallAsync("auth.getStatus", JsonValue.From(new JsonArray()), cancellationToken).ConfigureAwait(false);
        var obj = result?.AsObject();
        if (obj is null) {
            return new CopilotAuthStatus();
        }
        return new CopilotAuthStatus {
            IsAuthenticated = obj.GetBoolean("isAuthenticated"),
            AuthType = obj.GetString("authType"),
            Host = obj.GetString("host"),
            Login = obj.GetString("login"),
            StatusMessage = obj.GetString("statusMessage")
        };
    }

    public async Task<IReadOnlyList<CopilotModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default) {
        var result = await CallAsync("models.list", JsonValue.From(new JsonArray()), cancellationToken).ConfigureAwait(false);
        var obj = result?.AsObject();
        var modelsObj = obj?.GetArray("models");
        var list = new List<CopilotModelInfo>();
        if (modelsObj is null) {
            return list;
        }
        foreach (var modelValue in modelsObj) {
            var model = modelValue.AsObject();
            if (model is null) {
                continue;
            }
            var id = model.GetString("id") ?? string.Empty;
            var name = model.GetString("name");
            var info = new CopilotModelInfo(id, name);
            list.Add(info);
        }
        return list;
    }

    public async Task<CopilotSession> CreateSessionAsync(CopilotSessionOptions? options = null, CancellationToken cancellationToken = default) {
        options ??= new CopilotSessionOptions();
        var request = new JsonObject();
        if (!string.IsNullOrWhiteSpace(options.Model)) {
            request.Add("model", options.Model);
        }
        if (!string.IsNullOrWhiteSpace(options.SessionId)) {
            request.Add("sessionId", options.SessionId);
        }
        if (!string.IsNullOrWhiteSpace(options.SystemMessage)) {
            request.Add("systemMessage", new JsonObject().Add("content", options.SystemMessage));
        }
        if (options.Streaming.HasValue) {
            request.Add("streaming", options.Streaming.Value);
        }

        var parameters = new JsonArray().Add(request);
        var result = await CallAsync("session.create", JsonValue.From(parameters), cancellationToken).ConfigureAwait(false);
        var obj = result?.AsObject();
        var sessionId = obj?.GetString("sessionId") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(sessionId)) {
            throw new InvalidOperationException("Copilot session id missing from response.");
        }
        var session = new CopilotSession(sessionId, this);
        if (!_sessions.TryAdd(sessionId, session)) {
            throw new InvalidOperationException($"Session {sessionId} already exists.");
        }
        return session;
    }

    public async Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default) {
        var request = new JsonObject().Add("sessionId", sessionId);
        var parameters = new JsonArray().Add(request);
        await CallAsync("session.delete", JsonValue.From(parameters), cancellationToken).ConfigureAwait(false);
        if (_sessions.TryRemove(sessionId, out var session)) {
            session.Dispose();
        }
    }

    internal Task<JsonValue?> CallAsync(string method, JsonValue? parameters, CancellationToken cancellationToken) {
        EnsureConnected();
        return _rpc!.CallAsync(method, parameters, cancellationToken);
    }

    private void EnsureConnected() {
        if (_rpc is null) {
            throw new InvalidOperationException("Copilot client is not connected.");
        }
    }

    private async Task StartCoreAsync(CancellationToken cancellationToken) {
        if (_rpc is not null) {
            return;
        }

        if (!string.IsNullOrWhiteSpace(_options.CliUrl)) {
            var (host, port) = ParseCliUrl(_options.CliUrl!);
            _tcpClient = new TcpClient();
            await _tcpClient.ConnectAsync(host, port).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            _networkStream = _tcpClient.GetStream();
            InitializeTransport(_networkStream, _networkStream);
            return;
        }

        var startInfo = BuildStartInfo(_options);
        _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        if (!_process.Start()) {
            throw new InvalidOperationException("Failed to start Copilot CLI process.");
        }

        if (_options.UseStdio) {
            InitializeTransport(_process.StandardOutput.BaseStream, _process.StandardInput.BaseStream);
        } else {
            var port = _options.Port;
            if (port <= 0) {
                port = await DetectPortAsync(_process, cancellationToken).ConfigureAwait(false);
            }
            _tcpClient = new TcpClient();
            await _tcpClient.ConnectAsync("localhost", port).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            _networkStream = _tcpClient.GetStream();
            InitializeTransport(_networkStream, _networkStream);
        }

        _stderrTask = Task.Run(() => ReadErrorLoopAsync(_process, _cts.Token), _cts.Token);
    }

    private void InitializeTransport(Stream input, Stream output) {
        _transport = new HeaderDelimitedMessageTransport(input, output);
        _rpc = new JsonRpcClient(message => _transport.SendAsync(message, _cts.Token));
        _rpc.RequestReceived += OnRequestReceived;
        _rpc.NotificationReceived += OnNotificationReceived;
        _readerTask = Task.Run(() => _transport.ReadLoopAsync(_rpc.HandleLine, _cts.Token), _cts.Token);
    }

    private void OnNotificationReceived(object? sender, JsonRpcNotificationEventArgs e) {
        // No-op for now
    }

    private void OnRequestReceived(object? sender, JsonRpcRequestEventArgs e) {
        _ = HandleRequestAsync(e.Request);
    }

    private async Task HandleRequestAsync(JsonRpcRequest request) {
        if (string.Equals(request.Method, "session.event", StringComparison.Ordinal)) {
            var evt = TryParseSessionEvent(request.Params);
            if (evt is not null) {
                await request.RespondAsync(JsonValue.Null).ConfigureAwait(false);
                return;
            }
            await request.RespondErrorAsync(-32602, "Invalid session.event payload").ConfigureAwait(false);
            return;
        }

        await request.RespondErrorAsync(-32601, $"Method not found: {request.Method}").ConfigureAwait(false);
    }

    private CopilotSessionEvent? TryParseSessionEvent(JsonValue? parameters) {
        var array = parameters?.AsArray();
        if (array is null || array.Count < 2) {
            return null;
        }
        var sessionId = array[0].AsString();
        var evtObj = array[1].AsObject();
        if (string.IsNullOrWhiteSpace(sessionId) || evtObj is null) {
            return null;
        }
        if (!_sessions.TryGetValue(sessionId, out var session)) {
            return null;
        }
        var evt = CopilotSessionEvent.FromJson(evtObj);
        session.Dispatch(evt);
        return evt;
    }

    private static ProcessStartInfo BuildStartInfo(CopilotClientOptions options) {
        var cliPath = options.CliPath ?? "copilot";
        var args = new List<string>();
        if (options.CliArgs.Count > 0) {
            args.AddRange(options.CliArgs);
        }
        args.Add("--server");
        args.Add("--log-level");
        args.Add(options.LogLevel);
        if (options.UseStdio) {
            args.Add("--stdio");
        } else if (options.Port > 0) {
            args.Add("--port");
            args.Add(options.Port.ToString());
        }

        var (fileName, processArgs) = ResolveCliCommand(cliPath, args);
        var startInfo = new ProcessStartInfo {
            FileName = fileName,
            Arguments = string.Join(" ", EscapeArgs(processArgs)),
            UseShellExecute = false,
            RedirectStandardInput = options.UseStdio,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = options.WorkingDirectory ?? Environment.CurrentDirectory,
            CreateNoWindow = true
        };

        if (options.Environment.Count > 0) {
            startInfo.Environment.Clear();
            foreach (var entry in options.Environment) {
                startInfo.Environment[entry.Key] = entry.Value;
            }
        }

        startInfo.Environment.Remove("NODE_DEBUG");
        return startInfo;
    }

    private static (string FileName, IEnumerable<string> Args) ResolveCliCommand(string cliPath, IEnumerable<string> args) {
        if (cliPath.EndsWith(".js", StringComparison.OrdinalIgnoreCase)) {
            return ("node", Prepend(cliPath, args));
        }
        if (IsWindows() && !Path.IsPathRooted(cliPath)) {
            return ("cmd", Prepend("/c", Prepend(cliPath, args)));
        }
        return (cliPath, args);
    }

    private static IEnumerable<string> Prepend(string value, IEnumerable<string> args) {
        yield return value;
        foreach (var arg in args) {
            yield return arg;
        }
    }

    private static IEnumerable<string> EscapeArgs(IEnumerable<string> args) {
        foreach (var arg in args) {
            yield return EscapeArg(arg);
        }
    }

    private static bool IsWindows() {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    }

    private static string EscapeArg(string arg) {
        if (string.IsNullOrEmpty(arg)) {
            return "\"\"";
        }
        if (!arg.Contains(' ') && !arg.Contains('"')) {
            return arg;
        }
        return "\"" + arg.Replace("\"", "\\\"") + "\"";
    }

    private static (string Host, int Port) ParseCliUrl(string url) {
        if (int.TryParse(url, out var portOnly)) {
            return ("localhost", portOnly);
        }
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) {
            url = "http://" + url;
        }
        var uri = new Uri(url);
        return (uri.Host, uri.Port);
    }

    private async Task<int> DetectPortAsync(Process process, CancellationToken cancellationToken) {
        var regex = new Regex(@"listening on port (\d+)", RegexOptions.IgnoreCase);
        using var reader = process.StandardOutput;
        while (!cancellationToken.IsCancellationRequested) {
            var line = await ReadLineWithCancellationAsync(reader, cancellationToken).ConfigureAwait(false);
            if (line is null) {
                throw new InvalidOperationException("Copilot CLI exited before reporting a port.");
            }
            var match = regex.Match(line);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var port)) {
                return port;
            }
        }
        throw new OperationCanceledException();
    }

    private async Task ReadErrorLoopAsync(Process process, CancellationToken cancellationToken) {
        while (!cancellationToken.IsCancellationRequested && !process.HasExited) {
            var line = await ReadLineWithCancellationAsync(process.StandardError, cancellationToken).ConfigureAwait(false);
            if (line is null) {
                break;
            }
            StandardErrorReceived?.Invoke(this, line);
        }
    }

    private static async Task<string?> ReadLineWithCancellationAsync(StreamReader reader, CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();
        var readTask = reader.ReadLineAsync();
        if (readTask.IsCompleted) {
            return await readTask.ConfigureAwait(false);
        }
        var cancelTask = Task.Delay(Timeout.Infinite, cancellationToken);
        var completed = await Task.WhenAny(readTask, cancelTask).ConfigureAwait(false);
        if (completed == cancelTask) {
            throw new OperationCanceledException(cancellationToken);
        }
        return await readTask.ConfigureAwait(false);
    }

    public void Dispose() {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync() {
        if (_disposed) {
            return;
        }
        _disposed = true;
        _cts.Cancel();
        if (_readerTask is not null) {
            await _readerTask.ConfigureAwait(false);
        }
        if (_stderrTask is not null) {
            await _stderrTask.ConfigureAwait(false);
        }
        _rpc?.Dispose();
        _transport?.Dispose();
        _networkStream?.Dispose();
        _tcpClient?.Close();
        if (_process is not null && !_process.HasExited) {
#if NET5_0_OR_GREATER
            _process.Kill(entireProcessTree: true);
#else
            _process.Kill();
#endif
        }
        _process?.Dispose();
        _cts.Dispose();
    }
}
