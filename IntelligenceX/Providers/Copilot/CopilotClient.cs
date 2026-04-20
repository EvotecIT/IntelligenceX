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
using IntelligenceX.Telemetry;
using IntelligenceX.Utils;

namespace IntelligenceX.Copilot;

/// <summary>
/// Client for the GitHub Copilot CLI protocol.
/// </summary>
public sealed class CopilotClient : IDisposable
#if NET5_0_OR_GREATER || NETSTANDARD2_1_OR_GREATER
    , IAsyncDisposable
#endif
{
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
    private readonly TimeSpan _shutdownTimeout;
    private readonly RpcRetryOptions _rpcRetry;
    private bool _disposed;

    private CopilotClient(CopilotClientOptions options) {
        _options = options;
        _shutdownTimeout = options.ShutdownTimeout;
        _rpcRetry = options.RpcRetry;
    }

    /// <summary>
    /// Raised when standard error output is received.
    /// </summary>
    public event EventHandler<string>? StandardErrorReceived;
    /// <summary>
    /// Raised when standard output is received.
    /// </summary>
    public event EventHandler<string>? StandardOutputReceived;
    /// <summary>
    /// Raised when a protocol message is received.
    /// </summary>
    public event EventHandler<string>? ProtocolMessageReceived;
    /// <summary>
    /// Raised when a protocol message is sent.
    /// </summary>
    public event EventHandler<string>? ProtocolMessageSent;
    /// <summary>
    /// Raised when an RPC call starts.
    /// </summary>
    public event EventHandler<RpcCallStartedEventArgs>? RpcCallStarted;
    /// <summary>
    /// Raised when an RPC call completes.
    /// </summary>
    public event EventHandler<RpcCallCompletedEventArgs>? RpcCallCompleted;

    /// <summary>
    /// Starts the Copilot client.
    /// </summary>
    /// <param name="options">Optional client options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<CopilotClient> StartAsync(CopilotClientOptions? options = null, CancellationToken cancellationToken = default) {
        options ??= new CopilotClientOptions();
        options.Validate();
        var client = new CopilotClient(options);
        await client.StartWithRetryAsync(cancellationToken).ConfigureAwait(false);
        return client;
    }

    /// <summary>
    /// Retrieves Copilot CLI status information.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<CopilotStatus> GetStatusAsync(CancellationToken cancellationToken = default) {
        var result = await CallWithRetryAsync("status.get", JsonValue.From(new JsonArray()), true, cancellationToken).ConfigureAwait(false);
        var obj = result?.AsObject();
        if (obj is null) {
            return new CopilotStatus(string.Empty, 0, new JsonObject(), null);
        }
        return CopilotStatus.FromJson(obj);
    }

    /// <summary>
    /// Retrieves Copilot authentication status.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<CopilotAuthStatus> GetAuthStatusAsync(CancellationToken cancellationToken = default) {
        var result = await CallWithRetryAsync("auth.getStatus", JsonValue.From(new JsonArray()), true, cancellationToken).ConfigureAwait(false);
        var obj = result?.AsObject();
        if (obj is null) {
            return new CopilotAuthStatus(false, null, null, null, null, new JsonObject(), null);
        }
        return CopilotAuthStatus.FromJson(obj);
    }

    /// <summary>
    /// Lists available Copilot models.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<CopilotModelInfo>> ListModelsAsync(CancellationToken cancellationToken = default) {
        var result = await CallWithRetryAsync("models.list", JsonValue.From(new JsonArray()), true, cancellationToken).ConfigureAwait(false);
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
            var info = CopilotModelInfo.FromJson(model);
            list.Add(info);
        }
        return list;
    }

    /// <summary>
    /// Creates a new Copilot session.
    /// </summary>
    /// <param name="options">Session options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
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

    /// <summary>
    /// Deletes a Copilot session.
    /// </summary>
    /// <param name="sessionId">Session id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
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

    private Task<JsonValue?> CallWithRetryAsync(string method, JsonValue? parameters, bool idempotent, CancellationToken cancellationToken) {
        EnsureConnected();
        return RpcRetryHelper.ExecuteAsync(token => _rpc!.CallAsync(method, parameters, token), _rpcRetry, idempotent, cancellationToken);
    }

    private void EnsureConnected() {
        if (_rpc is null) {
            throw new InvalidOperationException("Copilot client is not connected.");
        }
    }

    private async Task StartWithRetryAsync(CancellationToken cancellationToken) {
        var retries = _options.ConnectRetryCount;
        var delay = _options.ConnectRetryInitialDelay;
        Exception? lastError = null;

        for (var attempt = 0; attempt <= retries; attempt++) {
            cancellationToken.ThrowIfCancellationRequested();
            try {
                await StartCoreAsync(cancellationToken).ConfigureAwait(false);
                return;
            } catch (Exception ex) {
                lastError = ex;
                if (attempt >= retries) {
                    throw;
                }
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                delay = NextDelay(delay, _options.ConnectRetryMaxDelay);
            }
        }

        throw lastError ?? new InvalidOperationException("Failed to start Copilot client.");
    }

    private async Task StartCoreAsync(CancellationToken cancellationToken) {
        if (_rpc is not null) {
            return;
        }

        if (!string.IsNullOrWhiteSpace(_options.CliUrl)) {
            var (host, port) = ParseCliUrl(_options.CliUrl!);
            _tcpClient = new TcpClient();
            var connectToken = CreateTimeoutToken(_options.ConnectTimeout, cancellationToken, out var cts);
            try {
                await ConnectAsync(_tcpClient, host, port, connectToken).ConfigureAwait(false);
            } finally {
                cts?.Dispose();
            }
            _networkStream = _tcpClient.GetStream();
            InitializeTransport(_networkStream, _networkStream);
            return;
        }

        if (!_options.AutoStart) {
            throw new InvalidOperationException("Copilot AutoStart is disabled and no CliUrl was provided.");
        }

        var resolvedPath = await ResolveCliPathOrInstallAsync(_options, cancellationToken).ConfigureAwait(false);
        var startInfo = BuildStartInfo(_options, resolvedPath);
        _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        try {
            if (!_process.Start()) {
                throw new InvalidOperationException("Failed to start Copilot CLI process.");
            }
        } catch (Exception ex) {
            var message = "Copilot CLI not found or failed to start. Install it and log in, set CopilotClientOptions.CliPath, " +
                          "or enable CopilotClientOptions.AutoInstallCli.";
            throw new InvalidOperationException(message, ex);
        }

        if (_options.UseStdio) {
            InitializeTransport(_process.StandardOutput.BaseStream, _process.StandardInput.BaseStream);
        } else {
            var port = _options.Port;
            if (port <= 0) {
                var portToken = CreateTimeoutToken(_options.ConnectTimeout, cancellationToken, out var cts);
                try {
                    port = await DetectPortAsync(_process, portToken).ConfigureAwait(false);
                } finally {
                    cts?.Dispose();
                }
            }
            _tcpClient = new TcpClient();
            var localToken = CreateTimeoutToken(_options.ConnectTimeout, cancellationToken, out var cts2);
            try {
                await ConnectAsync(_tcpClient, "localhost", port, localToken).ConfigureAwait(false);
            } finally {
                cts2?.Dispose();
            }
            _networkStream = _tcpClient.GetStream();
            InitializeTransport(_networkStream, _networkStream);
        }

        _stderrTask = Task.Run(() => ReadErrorLoopAsync(_process, _cts.Token), _cts.Token);
    }

    private void InitializeTransport(Stream input, Stream output) {
        _transport = new HeaderDelimitedMessageTransport(input, output);
        _rpc = new JsonRpcClient(message => _transport.SendAsync(message, _cts.Token));
        _rpc.CallStarted += (_, args) => RpcCallStarted?.Invoke(this, args);
        _rpc.CallCompleted += (_, args) => RpcCallCompleted?.Invoke(this, args);
        _transport.MessageReceived += (_, message) => ProtocolMessageReceived?.Invoke(this, message);
        _transport.MessageSent += (_, message) => ProtocolMessageSent?.Invoke(this, message);
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
        if (!_sessions.TryGetValue(sessionId!, out var session)) {
            return null;
        }
        var evt = CopilotSessionEvent.FromJson(evtObj);
        session.Dispatch(evt);
        return evt;
    }

    private static ProcessStartInfo BuildStartInfo(CopilotClientOptions options, string cliPath) {
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

        if (!options.InheritEnvironment) {
            startInfo.Environment.Clear();
        }

        if (options.Environment.Count > 0) {
            foreach (var entry in options.Environment) {
                startInfo.Environment[entry.Key] = entry.Value;
            }
        }

        startInfo.Environment.Remove("NODE_DEBUG");
        return startInfo;
    }

    private static async Task<string> ResolveCliPathOrInstallAsync(CopilotClientOptions options, CancellationToken cancellationToken) {
        try {
            return ResolveCliPath(options.CliPath ?? "copilot");
        } catch (InvalidOperationException ex) {
            if (!options.AutoInstallCli) {
                throw;
            }
            var command = CopilotCliInstall.GetCommand(options.AutoInstallMethod, options.AutoInstallPrerelease);
            var exitCode = await CopilotCliInstall.InstallAsync(command, cancellationToken).ConfigureAwait(false);
            if (exitCode != 0) {
                throw new InvalidOperationException($"Copilot CLI install failed with exit code {exitCode}.", ex);
            }
            return ResolveCliPath(options.CliPath ?? "copilot");
        }
    }

    private static string ResolveCliPath(string cliPath) {
        if (string.IsNullOrWhiteSpace(cliPath)) {
            return "copilot";
        }
        if (Path.IsPathRooted(cliPath) || cliPath.Contains(Path.DirectorySeparatorChar) || cliPath.Contains(Path.AltDirectorySeparatorChar)) {
            return cliPath;
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathEnv)) {
            throw new InvalidOperationException("Copilot CLI not found on PATH.\n" + CopilotCliInstall.GetInstallInstructions());
        }

        var exts = new List<string> { string.Empty };
        if (IsWindows()) {
            var pathExt = Environment.GetEnvironmentVariable("PATHEXT");
            if (!string.IsNullOrWhiteSpace(pathExt)) {
                exts = new List<string>(pathExt.Split(';'));
            } else {
                exts = new List<string> { ".exe", ".cmd", ".bat" };
            }
        }

        foreach (var dir in pathEnv.Split(Path.PathSeparator)) {
            if (string.IsNullOrWhiteSpace(dir)) {
                continue;
            }
            foreach (var ext in exts) {
                var candidate = Path.Combine(dir.Trim(), cliPath + ext);
                if (File.Exists(candidate)) {
                    return candidate;
                }
            }
        }

        var installed = CopilotCliInstall.TryResolveInstalledCliPath(cliPath);
        if (!string.IsNullOrWhiteSpace(installed)) {
            return installed!;
        }

        throw new InvalidOperationException("Copilot CLI not found on PATH.\n" + CopilotCliInstall.GetInstallInstructions());
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
            StandardOutputReceived?.Invoke(this, line);
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

    /// <summary>
    /// Executes a health check call.
    /// </summary>
    /// <param name="timeout">Optional timeout.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<HealthCheckResult> HealthCheckAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default) {
        var sw = Stopwatch.StartNew();
        var token = CreateTimeoutToken(timeout ?? _options.ConnectTimeout, cancellationToken, out var cts);
        try {
            await GetStatusAsync(token).ConfigureAwait(false);
            return new HealthCheckResult(true, "status.get", null, sw.Elapsed);
        } catch (Exception ex) {
            return new HealthCheckResult(false, "status.get", ex, sw.Elapsed);
        } finally {
            cts?.Dispose();
        }
    }

    private static TimeSpan NextDelay(TimeSpan current, TimeSpan max) {
        if (current <= TimeSpan.Zero) {
            return TimeSpan.Zero;
        }
        var next = TimeSpan.FromMilliseconds(current.TotalMilliseconds * 2);
        return next > max ? max : next;
    }

    private static CancellationToken CreateTimeoutToken(TimeSpan timeout, CancellationToken cancellationToken, out CancellationTokenSource? cts) {
        if (timeout > TimeSpan.Zero) {
            cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);
            return cts.Token;
        }
        cts = null;
        return cancellationToken;
    }

    private static async Task ConnectAsync(TcpClient client, string host, int port, CancellationToken cancellationToken) {
        var connectTask = client.ConnectAsync(host, port);
        var completed = await Task.WhenAny(connectTask, Task.Delay(Timeout.Infinite, cancellationToken)).ConfigureAwait(false);
        if (completed != connectTask) {
            throw new OperationCanceledException(cancellationToken);
        }
        await connectTask.ConfigureAwait(false);
    }

    private static async Task WaitWithTimeoutAsync(Task? task, TimeSpan timeout) {
        if (task is null) {
            return;
        }
        try {
            var completed = await Task.WhenAny(task, Task.Delay(timeout)).ConfigureAwait(false);
            if (completed != task) {
                return;
            }
            await task.ConfigureAwait(false);
        } catch {
            // Ignore shutdown wait failures.
        }
    }

    /// <summary>
    /// Disposes the client synchronously.
    /// </summary>
    public void Dispose() {
#if NET5_0_OR_GREATER || NETSTANDARD2_1_OR_GREATER
        DisposeAsync().AsTask().GetAwaiter().GetResult();
#else
        DisposeAsync().GetAwaiter().GetResult();
#endif
    }

#if NET5_0_OR_GREATER || NETSTANDARD2_1_OR_GREATER
    /// <summary>
    /// Disposes the client asynchronously.
    /// </summary>
    public async ValueTask DisposeAsync() {
#else
    /// <summary>
    /// Disposes the client asynchronously.
    /// </summary>
    public async Task DisposeAsync() {
#endif
        if (_disposed) {
            return;
        }
        _disposed = true;
        _cts.Cancel();
        await WaitWithTimeoutAsync(_readerTask, _shutdownTimeout).ConfigureAwait(false);
        await WaitWithTimeoutAsync(_stderrTask, _shutdownTimeout).ConfigureAwait(false);
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
