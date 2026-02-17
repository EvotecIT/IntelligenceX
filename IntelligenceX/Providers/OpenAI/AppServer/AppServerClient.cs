using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.Json;
using IntelligenceX.Rpc;
using IntelligenceX.Telemetry;
using IntelligenceX.Utils;

namespace IntelligenceX.OpenAI.AppServer;

/// <summary>
/// Low-level client for the OpenAI app-server JSON-RPC protocol.
/// </summary>
public sealed partial class AppServerClient : IDisposable {
    private readonly Process _process;
    private readonly StreamWriter _stdin;
    private readonly StreamReader _stdout;
    private readonly StreamReader? _stderr;
    private readonly CancellationTokenSource _cts = new();
    private readonly JsonRpcClient _rpc;
    private readonly Task _readerTask;
    private readonly Task? _stderrTask;
    private readonly TimeSpan _shutdownTimeout;
    private readonly RpcRetryOptions _rpcRetry;
    private bool _disposed;

    private AppServerClient(Process process, StreamWriter stdin, StreamReader stdout, StreamReader? stderr, TimeSpan shutdownTimeout,
        RpcRetryOptions rpcRetry) {
        _process = process;
        _stdin = stdin;
        _stdout = stdout;
        _stderr = stderr;
        _shutdownTimeout = shutdownTimeout;
        _rpcRetry = rpcRetry;
        _rpc = new JsonRpcClient(SendLineAsync);
        _rpc.CallStarted += (_, args) => RpcCallStarted?.Invoke(this, args);
        _rpc.CallCompleted += (_, args) => RpcCallCompleted?.Invoke(this, args);
        _rpc.NotificationReceived += (_, args) => NotificationReceived?.Invoke(this, args);
        _rpc.RequestReceived += (_, args) => RequestReceived?.Invoke(this, args);
        _rpc.ProtocolError += (_, args) => ProtocolError?.Invoke(this, args);
        _readerTask = Task.Run(ReadLoopAsync, _cts.Token);
        if (_stderr is not null) {
            _stderrTask = Task.Run(ReadErrorLoopAsync, _cts.Token);
        }
    }

    /// <summary>
    /// Raised when a JSON-RPC notification is received.
    /// </summary>
    public event EventHandler<JsonRpcNotificationEventArgs>? NotificationReceived;
    /// <summary>
    /// Raised when an inbound request is received.
    /// </summary>
    public event EventHandler<JsonRpcRequestEventArgs>? RequestReceived;
    /// <summary>
    /// Raised when a protocol parsing error occurs.
    /// </summary>
    public event EventHandler<Exception>? ProtocolError;
    /// <summary>
    /// Raised when a line is received on standard error.
    /// </summary>
    public event EventHandler<string>? StandardErrorReceived;
    /// <summary>
    /// Raised when a protocol line is received.
    /// </summary>
    public event EventHandler<string>? ProtocolLineReceived;
    /// <summary>
    /// Raised when an RPC call starts.
    /// </summary>
    public event EventHandler<RpcCallStartedEventArgs>? RpcCallStarted;
    /// <summary>
    /// Raised when an RPC call completes.
    /// </summary>
    public event EventHandler<RpcCallCompletedEventArgs>? RpcCallCompleted;
    /// <summary>
    /// Raised when a login flow starts.
    /// </summary>
    public event EventHandler<LoginEventArgs>? LoginStarted;
    /// <summary>
    /// Raised when a login flow completes.
    /// </summary>
    public event EventHandler<LoginEventArgs>? LoginCompleted;

    /// <summary>
    /// Starts the app-server process and returns a connected client.
    /// </summary>
    /// <param name="options">Optional app-server options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that resolves to AppServerClient.</returns>
    public static async Task<AppServerClient> StartAsync(AppServerOptions? options = null, CancellationToken cancellationToken = default) {
        options ??= new AppServerOptions();
        options.Validate();

        var retries = options.ConnectRetryCount;
        var delay = options.ConnectRetryInitialDelay;
        Exception? lastError = null;

        for (var attempt = 0; attempt <= retries; attempt++) {
            cancellationToken.ThrowIfCancellationRequested();
            AppServerClient? client = null;
            try {
                client = await StartOnceAsync(options, cancellationToken).ConfigureAwait(false);
                if (options.HealthCheckOnStart) {
                    var healthToken = CreateTimeoutToken(options.StartTimeout, cancellationToken, out var cts);
                    try {
                        var check = await client.HealthCheckAsync(options.HealthCheckMethod, options.HealthCheckTimeout, healthToken)
                            .ConfigureAwait(false);
                        if (!check.Ok) {
                            throw check.Error ?? new InvalidOperationException(check.Message ?? "App-server health check failed.");
                        }
                    } finally {
                        cts?.Dispose();
                    }
                }
                return client;
            } catch (Exception ex) {
                lastError = ex;
                client?.Dispose();
                if (attempt >= retries) {
                    throw;
                }
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                delay = NextDelay(delay, options.ConnectRetryMaxDelay);
            }
        }

        throw lastError ?? new InvalidOperationException("Failed to start Codex app-server process.");
    }

    private static Task<AppServerClient> StartOnceAsync(AppServerOptions options, CancellationToken cancellationToken) {
        Guard.NotNullOrWhiteSpace(options.ExecutablePath, nameof(options.ExecutablePath));
        Guard.NotNullOrWhiteSpace(options.Arguments, nameof(options.Arguments));

        var startInfo = new ProcessStartInfo {
            FileName = options.ExecutablePath,
            Arguments = options.Arguments,
            WorkingDirectory = options.WorkingDirectory ?? Environment.CurrentDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = options.RedirectStandardError,
            UseShellExecute = false,
            CreateNoWindow = true
        };

#if NETFRAMEWORK
        foreach (var entry in options.Environment) {
            startInfo.EnvironmentVariables[entry.Key] = entry.Value;
        }
#else
        foreach (var entry in options.Environment) {
            startInfo.Environment[entry.Key] = entry.Value;
        }
#endif

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        if (!process.Start()) {
            throw new InvalidOperationException("Failed to start Codex app-server process.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var stdin = process.StandardInput;
        stdin.AutoFlush = true;
        var stdout = process.StandardOutput;
        var stderr = options.RedirectStandardError ? process.StandardError : null;

        return Task.FromResult(new AppServerClient(process, stdin, stdout, stderr, options.ShutdownTimeout, options.RpcRetry));
    }

    /// <summary>
    /// Initializes the app-server with client metadata.
    /// </summary>
    /// <param name="clientInfo">Client identity information.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the operation finishes.</returns>
    public async Task InitializeAsync(ClientInfo clientInfo, CancellationToken cancellationToken = default) {
        Guard.NotNull(clientInfo, nameof(clientInfo));

        var parameters = new JsonObject()
            .Add("clientInfo", new JsonObject()
                .Add("name", clientInfo.Name)
                .Add("title", clientInfo.Title)
                .Add("version", clientInfo.Version));

        await _rpc.CallAsync("initialize", parameters, cancellationToken).ConfigureAwait(false);
        await _rpc.NotifyAsync("initialized", (JsonObject?)null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes a health check call.
    /// </summary>
    /// <param name="method">Optional method override.</param>
    /// <param name="timeout">Optional timeout.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that resolves to HealthCheckResult.</returns>
    public async Task<HealthCheckResult> HealthCheckAsync(string? method = null, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) {
        var sw = Stopwatch.StartNew();
        var target = string.IsNullOrWhiteSpace(method) ? "config/read" : method!;
        var token = CreateTimeoutToken(timeout, cancellationToken, out var cts);
        try {
            await CallWithRetryAsync(target, (JsonObject?)null, true, token).ConfigureAwait(false);
            return new HealthCheckResult(true, target, null, sw.Elapsed);
        } catch (Exception ex) {
            return new HealthCheckResult(false, target, ex, sw.Elapsed);
        } finally {
            cts?.Dispose();
        }
    }

    private Task<JsonValue?> CallWithRetryAsync(string method, JsonObject? parameters, bool idempotent, CancellationToken cancellationToken) {
        return RpcRetryHelper.ExecuteAsync(token => _rpc.CallAsync(method, parameters, token), _rpcRetry, idempotent, cancellationToken);
    }
}
