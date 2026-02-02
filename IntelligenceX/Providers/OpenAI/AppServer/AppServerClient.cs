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
/// JSON-RPC client for the Codex app-server process.
/// </summary>
/// <example>
/// <code>
/// var client = await AppServerClient.StartAsync();
/// var models = await client.ListModelsAsync();
/// </code>
/// </example>
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

    public event EventHandler<JsonRpcNotificationEventArgs>? NotificationReceived;
    public event EventHandler<JsonRpcRequestEventArgs>? RequestReceived;
    public event EventHandler<Exception>? ProtocolError;
    public event EventHandler<string>? StandardErrorReceived;
    public event EventHandler<string>? ProtocolLineReceived;
    public event EventHandler<RpcCallStartedEventArgs>? RpcCallStarted;
    public event EventHandler<RpcCallCompletedEventArgs>? RpcCallCompleted;
    public event EventHandler<LoginEventArgs>? LoginStarted;
    public event EventHandler<LoginEventArgs>? LoginCompleted;

    /// <summary>
    /// Starts the app-server process with the provided options.
    /// </summary>
    /// <example>
    /// <code>
    /// var client = await AppServerClient.StartAsync();
    /// await client.InitializeAsync(new ClientInfo("IntelligenceX", "Demo", "1.0"));
    /// </code>
    /// </example>
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
    /// Initializes the app-server session and sends client metadata.
    /// </summary>
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
    /// Executes a health check RPC call.
    /// </summary>
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
    /// Starts a ChatGPT login flow through the app-server.
    /// </summary>
    /// <example>
    /// <code>
    /// var login = await client.StartChatGptLoginAsync();
    /// Console.WriteLine(login.AuthUrl);
    /// await client.WaitForLoginCompletionAsync(login.LoginId);
    /// </code>
    /// </example>
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
    /// Logs in using an API key through the app-server.
    /// </summary>
    /// <example>
    /// <code>
    /// await client.LoginWithApiKeyAsync("sk-...");
    /// </code>
    /// </example>
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
    /// Reads the account information from the app-server.
    /// </summary>
    /// <example>
    /// <code>
    /// var account = await client.ReadAccountAsync();
    /// Console.WriteLine(account.Email);
    /// </code>
    /// </example>
    public async Task<AccountInfo> ReadAccountAsync(CancellationToken cancellationToken = default) {
        var result = await CallWithRetryAsync("account/read", (JsonObject?)null, true, cancellationToken).ConfigureAwait(false);
        var obj = result?.AsObject();
        if (obj is null) {
            throw new InvalidOperationException("Unexpected account response.");
        }
        return AccountInfo.FromJson(obj);
    }

    /// <summary>
    /// Logs out the current account session.
    /// </summary>
    /// <example>
    /// <code>
    /// await client.LogoutAsync();
    /// </code>
    /// </example>
    public Task LogoutAsync(CancellationToken cancellationToken = default)
        => CallWithRetryAsync("account/logout", (JsonObject?)null, false, cancellationToken);

    /// <summary>
    /// Starts a new thread with the specified model and optional settings.
    /// </summary>
    /// <example>
    /// <code>
    /// var thread = await client.StartThreadAsync("gpt-5.2-codex", currentDirectory: "C:\\repo");
    /// </code>
    /// </example>
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
    /// Starts a new turn by sending a text prompt.
    /// </summary>
    /// <example>
    /// <code>
    /// var turn = await client.StartTurnAsync(thread.Id, "Summarize the PR");
    /// </code>
    /// </example>
    public async Task<TurnInfo> StartTurnAsync(string threadId, string text, CancellationToken cancellationToken = default) {
        return await StartTurnAsync(threadId, text, null, null, null, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Starts a new turn with optional overrides.
    /// </summary>
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
    /// Starts a new turn with a prebuilt input payload.
    /// </summary>
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
    /// Resumes an existing thread by id.
    /// </summary>
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
    /// Forks an existing thread into a new one.
    /// </summary>
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
    /// Lists threads for the current account.
    /// </summary>
    /// <example>
    /// <code>
    /// var list = await client.ListThreadsAsync(limit: 10);
    /// foreach (var thread in list.Data) {
    ///     Console.WriteLine(thread.Id);
    /// }
    /// </code>
    /// </example>
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
    /// Lists thread ids currently loaded in memory.
    /// </summary>
    public async Task<ThreadIdListResult> ListLoadedThreadsAsync(CancellationToken cancellationToken = default) {
        var result = await CallWithRetryAsync("thread/loaded/list", (JsonObject?)null, true, cancellationToken).ConfigureAwait(false);
        var obj = result?.AsObject();
        if (obj is null) {
            throw new InvalidOperationException("Unexpected loaded thread response.");
        }
        return ThreadIdListResult.FromJson(obj);
    }

    /// <summary>
    /// Archives a thread.
    /// </summary>
    public Task ArchiveThreadAsync(string threadId, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(threadId, nameof(threadId));
        var parameters = new JsonObject().Add("threadId", threadId);
        return CallWithRetryAsync("thread/archive", parameters, false, cancellationToken);
    }

    /// <summary>
    /// Rolls back the last N turns in a thread.
    /// </summary>
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
    /// Interrupts a running turn.
    /// </summary>
    public Task InterruptTurnAsync(string threadId, string turnId, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(threadId, nameof(threadId));
        Guard.NotNullOrWhiteSpace(turnId, nameof(turnId));
        var parameters = new JsonObject()
            .Add("threadId", threadId)
            .Add("turnId", turnId);
        return CallWithRetryAsync("turn/interrupt", parameters, false, cancellationToken);
    }

    /// <summary>
    /// Starts a review request for the given target.
    /// </summary>
    /// <example>
    /// <code>
    /// var result = await client.StartReviewAsync(thread.Id, "inline", ReviewTarget.BaseBranch("main"));
    /// Console.WriteLine(result.ReviewThreadId);
    /// </code>
    /// </example>
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
    /// <example>
    /// <code>
    /// var request = new CommandExecRequest(new[] { "dotnet", "--info" });
    /// var result = await client.ExecuteCommandAsync(request);
    /// Console.WriteLine(result.ExitCode);
    /// </code>
    /// </example>
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
    /// <example>
    /// <code>
    /// var models = await client.ListModelsAsync();
    /// Console.WriteLine(models.Models.Count);
    /// </code>
    /// </example>
    public async Task<ModelListResult> ListModelsAsync(CancellationToken cancellationToken = default) {
        var result = await CallWithRetryAsync("model/list", (JsonObject?)null, true, cancellationToken).ConfigureAwait(false);
        var obj = result?.AsObject();
        if (obj is null) {
            throw new InvalidOperationException("Unexpected model list response.");
        }
        return ModelListResult.FromJson(obj);
    }

    /// <summary>
    /// Lists collaboration modes supported by the app-server.
    /// </summary>
    /// <example>
    /// <code>
    /// var modes = await client.ListCollaborationModesAsync();
    /// foreach (var mode in modes.Modes) {
    ///     Console.WriteLine(mode.Name);
    /// }
    /// </code>
    /// </example>
    public async Task<CollaborationModeListResult> ListCollaborationModesAsync(CancellationToken cancellationToken = default) {
        var result = await CallWithRetryAsync("collaborationMode/list", (JsonObject?)null, true, cancellationToken).ConfigureAwait(false);
        var obj = result?.AsObject();
        if (obj is null) {
            throw new InvalidOperationException("Unexpected collaboration mode response.");
        }
        return CollaborationModeListResult.FromJson(obj);
    }

    /// <summary>
    /// Lists skills available to the app-server.
    /// </summary>
    /// <example>
    /// <code>
    /// var skills = await client.ListSkillsAsync();
    /// Console.WriteLine(skills.Groups.Count);
    /// </code>
    /// </example>
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
    /// Enables or disables a skill config entry.
    /// </summary>
    public Task WriteSkillConfigAsync(string path, bool enabled, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(path, nameof(path));
        var parameters = new JsonObject()
            .Add("path", path)
            .Add("enabled", enabled);
        return CallWithRetryAsync("skills/config/write", parameters, false, cancellationToken);
    }

    /// <summary>
    /// Reads the current app-server configuration.
    /// </summary>
    /// <example>
    /// <code>
    /// var config = await client.ReadConfigAsync();
    /// Console.WriteLine(config.Config.GetString("model"));
    /// </code>
    /// </example>
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
    public Task WriteConfigValueAsync(string key, JsonValue value, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(key, nameof(key));
        Guard.NotNull(value, nameof(value));
        var parameters = new JsonObject()
            .Add("key", key)
            .Add("value", value);
        return CallWithRetryAsync("config/value/write", parameters, false, cancellationToken);
    }

    /// <summary>
    /// Writes multiple configuration values.
    /// </summary>
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
    /// Reads allowed configuration requirements.
    /// </summary>
    /// <example>
    /// <code>
    /// var req = await client.ReadConfigRequirementsAsync();
    /// Console.WriteLine(req.Requirements?.AllowedSandboxModes?.Count);
    /// </code>
    /// </example>
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
    /// <example>
    /// <code>
    /// var login = await client.StartMcpOauthLoginAsync(serverName: "my-mcp");
    /// Console.WriteLine(login.AuthUrl);
    /// </code>
    /// </example>
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
    /// Lists MCP server status information.
    /// </summary>
    /// <example>
    /// <code>
    /// var status = await client.ListMcpServerStatusAsync(limit: 5);
    /// Console.WriteLine(status.Servers.Count);
    /// </code>
    /// </example>
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
    public async Task ReloadMcpServerConfigAsync(CancellationToken cancellationToken = default) {
        await CallWithRetryAsync("config/mcpServer/reload", (JsonObject?)null, false, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Requests user input for the provided questions.
    /// </summary>
    /// <example>
    /// <code>
    /// var response = await client.RequestUserInputAsync(new[] { "Continue?" });
    /// Console.WriteLine(response.Answers.Count);
    /// </code>
    /// </example>
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
    /// Uploads feedback content to the app-server.
    /// </summary>
    public Task UploadFeedbackAsync(string content, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(content, nameof(content));
        var parameters = new JsonObject().Add("content", content);
        return CallWithRetryAsync("feedback/upload", parameters, false, cancellationToken);
    }

    /// <summary>
    /// Calls an arbitrary JSON-RPC method.
    /// </summary>
    public Task<JsonValue?> CallAsync(string method, JsonObject? parameters, CancellationToken cancellationToken = default) {
        return _rpc.CallAsync(method, parameters, cancellationToken);
    }

    /// <summary>
    /// Sends a JSON-RPC notification.
    /// </summary>
    public Task NotifyAsync(string method, JsonObject? parameters, CancellationToken cancellationToken = default) {
        return _rpc.NotifyAsync(method, parameters, cancellationToken);
    }

    /// <summary>
    /// Waits for a login completion notification.
    /// </summary>
    /// <example>
    /// <code>
    /// var login = await client.StartChatGptLoginAsync();
    /// await client.WaitForLoginCompletionAsync(login.LoginId);
    /// </code>
    /// </example>
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
