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
public sealed class AppServerClient : IDisposable {
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

    /// <summary>
    /// Starts a ChatGPT login flow.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ChatGptLoginStart> StartChatGptLoginAsync(CancellationToken cancellationToken = default) {
        var parameters = new JsonObject().Add("type", "chatgpt");
        var result = await CallWithRetryAsync("account/login/start", parameters, false, cancellationToken).ConfigureAwait(false);
        var obj = result?.AsObject();
        if (obj is null) {
            throw new InvalidOperationException("Unexpected login response.");
        }
        var login = ChatGptLoginStart.FromJson(obj);
        LoginStarted?.Invoke(this, new LoginEventArgs("chatgpt", login.LoginId, login.AuthUrl));
        return login;
    }

    /// <summary>
    /// Logs in using an API key.
    /// </summary>
    /// <param name="apiKey">API key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task LoginWithApiKeyAsync(string apiKey, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(apiKey, nameof(apiKey));
        LoginStarted?.Invoke(this, new LoginEventArgs("apikey"));
        var parameters = new JsonObject()
            .Add("type", "apiKey")
            .Add("apiKey", apiKey);
        return CallWithRetryAsync("account/login/start", parameters, false, cancellationToken)
            .ContinueWith(task => {
                if (IsTaskSuccessful(task)) {
                    LoginCompleted?.Invoke(this, new LoginEventArgs("apikey"));
                }
                return task;
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default).Unwrap();
    }

    /// <summary>
    /// Reads account information.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<AccountInfo> ReadAccountAsync(CancellationToken cancellationToken = default) {
        var result = await CallWithRetryAsync("account/read", (JsonObject?)null, true, cancellationToken).ConfigureAwait(false);
        var obj = result?.AsObject();
        if (obj is null) {
            throw new InvalidOperationException("Unexpected account response.");
        }
        return AccountInfo.FromJson(obj);
    }

    /// <summary>
    /// Logs out of the current session.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task LogoutAsync(CancellationToken cancellationToken = default)
        => CallWithRetryAsync("account/logout", (JsonObject?)null, false, cancellationToken);

    /// <summary>
    /// Starts a new chat thread.
    /// </summary>
    /// <param name="model">Model name.</param>
    /// <param name="currentDirectory">Optional working directory.</param>
    /// <param name="approvalPolicy">Optional approval policy.</param>
    /// <param name="sandbox">Optional sandbox mode.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ThreadInfo> StartThreadAsync(string model, string? currentDirectory = null, string? approvalPolicy = null,
        string? sandbox = null, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(model, nameof(model));

        var parameters = new JsonObject()
            .Add("model", model);
        if (!string.IsNullOrWhiteSpace(currentDirectory)) {
            parameters.Add("cwd", currentDirectory);
        }
        if (!string.IsNullOrWhiteSpace(approvalPolicy)) {
            parameters.Add("approvalPolicy", approvalPolicy);
        }
        if (!string.IsNullOrWhiteSpace(sandbox)) {
            parameters.Add("sandbox", sandbox);
        }

        var result = await CallWithRetryAsync("thread/start", parameters, false, cancellationToken).ConfigureAwait(false);
        var obj = result?.AsObject();
        var threadObj = obj?.GetObject("thread");
        if (threadObj is null) {
            throw new InvalidOperationException("Unexpected thread response.");
        }
        return ThreadInfo.FromJson(threadObj);
    }

    /// <summary>
    /// Starts a turn with a text-only input.
    /// </summary>
    /// <param name="threadId">Thread id.</param>
    /// <param name="text">Prompt text.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<TurnInfo> StartTurnAsync(string threadId, string text, CancellationToken cancellationToken = default) {
        return await StartTurnAsync(threadId, text, null, null, null, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Starts a turn with a text-only input and optional overrides.
    /// </summary>
    /// <param name="threadId">Thread id.</param>
    /// <param name="text">Prompt text.</param>
    /// <param name="model">Optional model override.</param>
    /// <param name="currentDirectory">Optional working directory.</param>
    /// <param name="approvalPolicy">Optional approval policy.</param>
    /// <param name="sandboxPolicy">Optional sandbox policy.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<TurnInfo> StartTurnAsync(string threadId, string text, string? model, string? currentDirectory,
        string? approvalPolicy, SandboxPolicy? sandboxPolicy, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(threadId, nameof(threadId));
        Guard.NotNullOrWhiteSpace(text, nameof(text));

        var input = new JsonArray().Add(new JsonObject()
            .Add("type", "text")
            .Add("text", text));

        var parameters = new JsonObject()
            .Add("threadId", threadId)
            .Add("input", input);
        return await StartTurnAsync(parameters, model, currentDirectory, approvalPolicy, sandboxPolicy, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Starts a turn with a structured input payload.
    /// </summary>
    /// <param name="threadId">Thread id.</param>
    /// <param name="input">Input items.</param>
    /// <param name="model">Optional model override.</param>
    /// <param name="currentDirectory">Optional working directory.</param>
    /// <param name="approvalPolicy">Optional approval policy.</param>
    /// <param name="sandboxPolicy">Optional sandbox policy.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<TurnInfo> StartTurnAsync(string threadId, JsonArray input, string? model, string? currentDirectory,
        string? approvalPolicy, SandboxPolicy? sandboxPolicy, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(threadId, nameof(threadId));
        Guard.NotNull(input, nameof(input));

        var parameters = new JsonObject()
            .Add("threadId", threadId)
            .Add("input", input);
        return await StartTurnAsync(parameters, model, currentDirectory, approvalPolicy, sandboxPolicy, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TurnInfo> StartTurnAsync(JsonObject parameters, string? model, string? currentDirectory,
        string? approvalPolicy, SandboxPolicy? sandboxPolicy, CancellationToken cancellationToken = default) {
        if (!string.IsNullOrWhiteSpace(model)) {
            parameters.Add("model", model);
        }
        if (!string.IsNullOrWhiteSpace(currentDirectory)) {
            parameters.Add("cwd", currentDirectory);
        }
        if (!string.IsNullOrWhiteSpace(approvalPolicy)) {
            parameters.Add("approvalPolicy", approvalPolicy);
        }
        if (sandboxPolicy is not null) {
            parameters.Add("sandboxPolicy", sandboxPolicy.ToJson());
        }

        var result = await CallWithRetryAsync("turn/start", parameters, false, cancellationToken).ConfigureAwait(false);
        var obj = result?.AsObject();
        var turnObj = obj?.GetObject("turn");
        if (turnObj is null) {
            throw new InvalidOperationException("Unexpected turn response.");
        }
        return TurnInfo.FromJson(turnObj);
    }

    /// <summary>
    /// Resumes a thread by id.
    /// </summary>
    /// <param name="threadId">Thread id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ThreadInfo> ResumeThreadAsync(string threadId, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(threadId, nameof(threadId));
        var parameters = new JsonObject().Add("threadId", threadId);
        var result = await CallWithRetryAsync("thread/resume", parameters, false, cancellationToken).ConfigureAwait(false);
        var obj = result?.AsObject();
        var threadObj = obj?.GetObject("thread");
        if (threadObj is null) {
            throw new InvalidOperationException("Unexpected thread response.");
        }
        return ThreadInfo.FromJson(threadObj);
    }

    /// <summary>
    /// Forks a thread by id.
    /// </summary>
    /// <param name="threadId">Thread id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ThreadInfo> ForkThreadAsync(string threadId, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(threadId, nameof(threadId));
        var parameters = new JsonObject().Add("threadId", threadId);
        var result = await CallWithRetryAsync("thread/fork", parameters, false, cancellationToken).ConfigureAwait(false);
        var obj = result?.AsObject();
        var threadObj = obj?.GetObject("thread");
        if (threadObj is null) {
            throw new InvalidOperationException("Unexpected thread response.");
        }
        return ThreadInfo.FromJson(threadObj);
    }

    /// <summary>
    /// Lists threads with optional filters.
    /// </summary>
    /// <param name="cursor">Pagination cursor.</param>
    /// <param name="limit">Maximum number of items.</param>
    /// <param name="sortKey">Sort key.</param>
    /// <param name="modelProviders">Optional model provider filter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ThreadListResult> ListThreadsAsync(string? cursor = null, int? limit = null, string? sortKey = null,
        IReadOnlyList<string>? modelProviders = null, CancellationToken cancellationToken = default) {
        var parameters = new JsonObject();
        if (!string.IsNullOrWhiteSpace(cursor)) {
            parameters.Add("cursor", cursor);
        }
        if (limit.HasValue) {
            parameters.Add("limit", limit.Value);
        }
        if (!string.IsNullOrWhiteSpace(sortKey)) {
            parameters.Add("sortKey", sortKey);
        }
        if (modelProviders is not null && modelProviders.Count > 0) {
            var providers = new JsonArray();
            foreach (var provider in modelProviders) {
                providers.Add(provider);
            }
            parameters.Add("modelProviders", providers);
        }

        var result = await CallWithRetryAsync("thread/list", parameters, true, cancellationToken).ConfigureAwait(false);
        var obj = result?.AsObject();
        if (obj is null) {
            throw new InvalidOperationException("Unexpected thread list response.");
        }
        return ThreadListResult.FromJson(obj);
    }

    /// <summary>
    /// Lists currently loaded thread ids.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ThreadIdListResult> ListLoadedThreadsAsync(CancellationToken cancellationToken = default) {
        var result = await CallWithRetryAsync("thread/loaded/list", (JsonObject?)null, true, cancellationToken).ConfigureAwait(false);
        var obj = result?.AsObject();
        if (obj is null) {
            throw new InvalidOperationException("Unexpected loaded thread response.");
        }
        return ThreadIdListResult.FromJson(obj);
    }

    /// <summary>
    /// Archives a thread by id.
    /// </summary>
    /// <param name="threadId">Thread id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task ArchiveThreadAsync(string threadId, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(threadId, nameof(threadId));
        var parameters = new JsonObject().Add("threadId", threadId);
        return CallWithRetryAsync("thread/archive", parameters, false, cancellationToken);
    }

    /// <summary>
    /// Rolls back a thread by a number of turns.
    /// </summary>
    /// <param name="threadId">Thread id.</param>
    /// <param name="turns">Number of turns to roll back.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ThreadInfo> RollbackThreadAsync(string threadId, int turns, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(threadId, nameof(threadId));
        var parameters = new JsonObject()
            .Add("threadId", threadId)
            .Add("turns", turns);
        var result = await CallWithRetryAsync("thread/rollback", parameters, false, cancellationToken).ConfigureAwait(false);
        var obj = result?.AsObject();
        var threadObj = obj?.GetObject("thread");
        if (threadObj is null) {
            throw new InvalidOperationException("Unexpected thread response.");
        }
        return ThreadInfo.FromJson(threadObj);
    }

    /// <summary>
    /// Interrupts an in-flight turn.
    /// </summary>
    /// <param name="threadId">Thread id.</param>
    /// <param name="turnId">Turn id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task InterruptTurnAsync(string threadId, string turnId, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(threadId, nameof(threadId));
        Guard.NotNullOrWhiteSpace(turnId, nameof(turnId));
        var parameters = new JsonObject()
            .Add("threadId", threadId)
            .Add("turnId", turnId);
        return CallWithRetryAsync("turn/interrupt", parameters, false, cancellationToken);
    }

    /// <summary>
    /// Starts a review for a thread.
    /// </summary>
    /// <param name="threadId">Thread id.</param>
    /// <param name="delivery">Delivery channel.</param>
    /// <param name="target">Review target.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ReviewStartResult> StartReviewAsync(string threadId, string delivery, ReviewTarget target, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(threadId, nameof(threadId));
        Guard.NotNullOrWhiteSpace(delivery, nameof(delivery));
        Guard.NotNull(target, nameof(target));

        var parameters = new JsonObject()
            .Add("threadId", threadId)
            .Add("delivery", delivery)
            .Add("target", target.Payload);

        var result = await CallWithRetryAsync("review/start", parameters, false, cancellationToken).ConfigureAwait(false);
        var obj = result?.AsObject();
        if (obj is null) {
            throw new InvalidOperationException("Unexpected review response.");
        }
        return ReviewStartResult.FromJson(obj);
    }

    /// <summary>
    /// Executes a command through the app-server.
    /// </summary>
    /// <param name="request">Command execution request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<CommandExecResult> ExecuteCommandAsync(CommandExecRequest request, CancellationToken cancellationToken = default) {
        Guard.NotNull(request, nameof(request));

        var commandArray = new JsonArray();
        foreach (var item in request.Command) {
            commandArray.Add(item);
        }

        var parameters = new JsonObject()
            .Add("command", commandArray);
        if (!string.IsNullOrWhiteSpace(request.WorkingDirectory)) {
            parameters.Add("cwd", request.WorkingDirectory);
        }
        if (request.SandboxPolicy is not null) {
            parameters.Add("sandboxPolicy", request.SandboxPolicy.ToJson());
        }
        if (request.TimeoutMs.HasValue) {
            parameters.Add("timeoutMs", request.TimeoutMs.Value);
        }

        var result = await CallWithRetryAsync("command/exec", parameters, false, cancellationToken).ConfigureAwait(false);
        var obj = result?.AsObject();
        if (obj is null) {
            throw new InvalidOperationException("Unexpected command response.");
        }
        return CommandExecResult.FromJson(obj);
    }

    /// <summary>
    /// Lists available models.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ModelListResult> ListModelsAsync(CancellationToken cancellationToken = default) {
        var result = await CallWithRetryAsync("model/list", (JsonObject?)null, true, cancellationToken).ConfigureAwait(false);
        var obj = result?.AsObject();
        if (obj is null) {
            throw new InvalidOperationException("Unexpected model list response.");
        }
        return ModelListResult.FromJson(obj);
    }

    /// <summary>
    /// Lists available collaboration modes.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<CollaborationModeListResult> ListCollaborationModesAsync(CancellationToken cancellationToken = default) {
        var result = await CallWithRetryAsync("collaborationMode/list", (JsonObject?)null, true, cancellationToken).ConfigureAwait(false);
        var obj = result?.AsObject();
        if (obj is null) {
            throw new InvalidOperationException("Unexpected collaboration mode response.");
        }
        return CollaborationModeListResult.FromJson(obj);
    }

    /// <summary>
    /// Lists available skills.
    /// </summary>
    /// <param name="cwds">Optional working directories to query.</param>
    /// <param name="forceReload">Whether to force reload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<SkillListResult> ListSkillsAsync(IReadOnlyList<string>? cwds = null, bool? forceReload = null,
        CancellationToken cancellationToken = default) {
        var parameters = new JsonObject();
        if (cwds is not null && cwds.Count > 0) {
            var array = new JsonArray();
            foreach (var cwd in cwds) {
                array.Add(cwd);
            }
            parameters.Add("cwds", array);
        }
        if (forceReload.HasValue) {
            parameters.Add("forceReload", forceReload.Value);
        }
        var result = await CallWithRetryAsync("skills/list", parameters, true, cancellationToken).ConfigureAwait(false);
        var obj = result?.AsObject();
        if (obj is null) {
            throw new InvalidOperationException("Unexpected skills list response.");
        }
        return SkillListResult.FromJson(obj);
    }

    /// <summary>
    /// Writes a skill configuration entry.
    /// </summary>
    /// <param name="path">Skill path.</param>
    /// <param name="enabled">Whether the skill is enabled.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task WriteSkillConfigAsync(string path, bool enabled, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(path, nameof(path));
        var parameters = new JsonObject()
            .Add("path", path)
            .Add("enabled", enabled);
        return CallWithRetryAsync("skills/config/write", parameters, false, cancellationToken);
    }

    /// <summary>
    /// Reads the current configuration.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ConfigReadResult> ReadConfigAsync(CancellationToken cancellationToken = default) {
        var result = await CallWithRetryAsync("config/read", (JsonObject?)null, true, cancellationToken).ConfigureAwait(false);
        var obj = result?.AsObject();
        if (obj is null) {
            throw new InvalidOperationException("Unexpected config response.");
        }
        return ConfigReadResult.FromJson(obj);
    }

    /// <summary>
    /// Writes a single configuration value.
    /// </summary>
    /// <param name="key">Configuration key.</param>
    /// <param name="value">Configuration value.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task WriteConfigValueAsync(string key, JsonValue value, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(key, nameof(key));
        Guard.NotNull(value, nameof(value));
        var parameters = new JsonObject()
            .Add("key", key)
            .Add("value", value);
        return CallWithRetryAsync("config/value/write", parameters, false, cancellationToken);
    }

    /// <summary>
    /// Writes a batch of configuration values.
    /// </summary>
    /// <param name="entries">Entries to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task WriteConfigBatchAsync(IReadOnlyList<ConfigEntry> entries, CancellationToken cancellationToken = default) {
        Guard.NotNull(entries, nameof(entries));
        var items = new JsonArray();
        foreach (var entry in entries) {
            items.Add(new JsonObject()
                .Add("key", entry.Key)
                .Add("value", entry.Value));
        }
        var parameters = new JsonObject().Add("items", items);
        return CallWithRetryAsync("config/batchWrite", parameters, false, cancellationToken);
    }

    /// <summary>
    /// Reads configuration requirements.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ConfigRequirementsReadResult> ReadConfigRequirementsAsync(CancellationToken cancellationToken = default) {
        var result = await CallWithRetryAsync("configRequirements/read", (JsonObject?)null, true, cancellationToken).ConfigureAwait(false);
        var obj = result?.AsObject();
        if (obj is null) {
            throw new InvalidOperationException("Unexpected config requirements response.");
        }
        return ConfigRequirementsReadResult.FromJson(obj);
    }

    /// <summary>
    /// Starts an MCP OAuth login flow.
    /// </summary>
    /// <param name="serverId">Optional server id.</param>
    /// <param name="serverName">Optional server name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<McpOauthLoginStart> StartMcpOauthLoginAsync(string? serverId, string? serverName = null,
        CancellationToken cancellationToken = default) {
        var parameters = new JsonObject();
        if (!string.IsNullOrWhiteSpace(serverId)) {
            parameters.Add("serverId", serverId);
        }
        if (!string.IsNullOrWhiteSpace(serverName)) {
            parameters.Add("serverName", serverName);
        }
        var result = await CallWithRetryAsync("mcpServer/oauth/login", parameters, false, cancellationToken).ConfigureAwait(false);
        var obj = result?.AsObject();
        if (obj is null) {
            throw new InvalidOperationException("Unexpected MCP OAuth response.");
        }
        return McpOauthLoginStart.FromJson(obj);
    }

    /// <summary>
    /// Lists MCP server status entries.
    /// </summary>
    /// <param name="cursor">Pagination cursor.</param>
    /// <param name="limit">Maximum number of items.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<McpServerStatusListResult> ListMcpServerStatusAsync(string? cursor = null, int? limit = null,
        CancellationToken cancellationToken = default) {
        var parameters = new JsonObject();
        if (!string.IsNullOrWhiteSpace(cursor)) {
            parameters.Add("cursor", cursor);
        }
        if (limit.HasValue) {
            parameters.Add("limit", limit.Value);
        }
        var result = await CallWithRetryAsync("mcpServerStatus/list", parameters, true, cancellationToken).ConfigureAwait(false);
        var obj = result?.AsObject();
        if (obj is null) {
            throw new InvalidOperationException("Unexpected MCP server status response.");
        }
        return McpServerStatusListResult.FromJson(obj);
    }

    /// <summary>
    /// Reloads MCP server configuration.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ReloadMcpServerConfigAsync(CancellationToken cancellationToken = default) {
        await CallWithRetryAsync("config/mcpServer/reload", (JsonObject?)null, false, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Requests user input responses.
    /// </summary>
    /// <param name="questions">Questions to prompt.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<UserInputResponse> RequestUserInputAsync(IReadOnlyList<string> questions, CancellationToken cancellationToken = default) {
        Guard.NotNull(questions, nameof(questions));
        var array = new JsonArray();
        foreach (var question in questions) {
            array.Add(question);
        }
        var parameters = new JsonObject().Add("questions", array);
        var result = await CallWithRetryAsync("tool/requestUserInput", parameters, false, cancellationToken).ConfigureAwait(false);
        var obj = result?.AsObject();
        if (obj is null) {
            throw new InvalidOperationException("Unexpected user input response.");
        }
        return UserInputResponse.FromJson(obj);
    }

    /// <summary>
    /// Uploads feedback content.
    /// </summary>
    /// <param name="content">Feedback content.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task UploadFeedbackAsync(string content, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(content, nameof(content));
        var parameters = new JsonObject().Add("content", content);
        return CallWithRetryAsync("feedback/upload", parameters, false, cancellationToken);
    }

    /// <summary>
    /// Executes a raw JSON-RPC call.
    /// </summary>
    /// <param name="method">Method name.</param>
    /// <param name="parameters">Optional parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<JsonValue?> CallAsync(string method, JsonObject? parameters, CancellationToken cancellationToken = default) {
        return _rpc.CallAsync(method, parameters, cancellationToken);
    }

    /// <summary>
    /// Sends a JSON-RPC notification.
    /// </summary>
    /// <param name="method">Method name.</param>
    /// <param name="parameters">Optional parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task NotifyAsync(string method, JsonObject? parameters, CancellationToken cancellationToken = default) {
        return _rpc.NotifyAsync(method, parameters, cancellationToken);
    }

    /// <summary>
    /// Waits for a login completion notification.
    /// </summary>
    /// <param name="loginId">Optional login id to match.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task WaitForLoginCompletionAsync(string? loginId = null, CancellationToken cancellationToken = default) {
        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        void Handler(object? sender, JsonRpcNotificationEventArgs args) {
            if (!string.Equals(args.Method, "account/login/completed", StringComparison.Ordinal)) {
                return;
            }
            if (loginId is null) {
                tcs.TrySetResult(null);
                return;
            }
            var id = args.Params?.AsObject()?.GetString("loginId");
            if (string.Equals(id, loginId, StringComparison.Ordinal)) {
                tcs.TrySetResult(null);
            }
        }

        NotificationReceived += Handler;
        if (cancellationToken.CanBeCanceled) {
            cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        }

        return tcs.Task.ContinueWith(task => {
            NotificationReceived -= Handler;
            if (IsTaskSuccessful(task)) {
                LoginCompleted?.Invoke(this, new LoginEventArgs("chatgpt", loginId));
            }
            return task;
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default).Unwrap();
    }

    private async Task SendLineAsync(string line) {
        await _stdin.WriteLineAsync(line).ConfigureAwait(false);
    }

    private async Task ReadLoopAsync() {
        try {
            while (!_cts.IsCancellationRequested) {
                var line = await _stdout.ReadLineAsync().ConfigureAwait(false);
                if (line is null) {
                    break;
                }
                ProtocolLineReceived?.Invoke(this, line);
                _rpc.HandleLine(line);
            }
        } catch (Exception ex) {
            ProtocolError?.Invoke(this, ex);
        }
    }

    private async Task ReadErrorLoopAsync() {
        try {
            while (!_cts.IsCancellationRequested && _stderr is not null) {
                var line = await _stderr.ReadLineAsync().ConfigureAwait(false);
                if (line is null) {
                    break;
                }
                StandardErrorReceived?.Invoke(this, line);
            }
        } catch (Exception ex) {
            ProtocolError?.Invoke(this, ex);
        }
    }

    private static TimeSpan NextDelay(TimeSpan current, TimeSpan max) {
        if (current <= TimeSpan.Zero) {
            return TimeSpan.Zero;
        }
        var next = TimeSpan.FromMilliseconds(current.TotalMilliseconds * 2);
        return next > max ? max : next;
    }

    private static CancellationToken CreateTimeoutToken(TimeSpan? timeout, CancellationToken cancellationToken, out CancellationTokenSource? cts) {
        if (timeout.HasValue && timeout.Value > TimeSpan.Zero) {
            cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout.Value);
            return cts.Token;
        }
        cts = null;
        return cancellationToken;
    }

    private static void TryWait(Task? task, TimeSpan timeout) {
        if (task is null) {
            return;
        }
        try {
            task.Wait(timeout);
        } catch {
            // Ignore shutdown wait failures.
        }
    }

    private static bool IsTaskSuccessful(Task task) {
#if NETSTANDARD2_0 || NET472
        return task.Status == TaskStatus.RanToCompletion;
#else
        return task.IsCompletedSuccessfully;
#endif
    }

    /// <summary>
    /// Disposes the app-server client and underlying process.
    /// </summary>
    public void Dispose() {
        if (_disposed) {
            return;
        }
        _disposed = true;

        _cts.Cancel();
        TryWait(_readerTask, _shutdownTimeout);
        TryWait(_stderrTask, _shutdownTimeout);
        _rpc.Dispose();

        try {
            if (!_process.HasExited) {
                _process.Kill();
            }
        } catch {
            // Ignore process shutdown errors.
        }

        _process.Dispose();
        _cts.Dispose();
        _stdin.Dispose();
        _stdout.Dispose();
        _stderr?.Dispose();
    }
}
