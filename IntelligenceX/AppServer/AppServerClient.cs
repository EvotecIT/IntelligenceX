using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.AppServer.Models;
using IntelligenceX.Json;
using IntelligenceX.Rpc;
using IntelligenceX.Utils;

namespace IntelligenceX.AppServer;

public sealed class AppServerClient : IDisposable {
    private readonly Process _process;
    private readonly StreamWriter _stdin;
    private readonly StreamReader _stdout;
    private readonly StreamReader? _stderr;
    private readonly CancellationTokenSource _cts = new();
    private readonly JsonRpcClient _rpc;
    private readonly Task _readerTask;
    private readonly Task? _stderrTask;
    private bool _disposed;

    private AppServerClient(Process process, StreamWriter stdin, StreamReader stdout, StreamReader? stderr) {
        _process = process;
        _stdin = stdin;
        _stdout = stdout;
        _stderr = stderr;
        _rpc = new JsonRpcClient(SendLineAsync);
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

    public static Task<AppServerClient> StartAsync(AppServerOptions? options = null, CancellationToken cancellationToken = default) {
        options ??= new AppServerOptions();
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

        return Task.FromResult(new AppServerClient(process, stdin, stdout, stderr));
    }

    public async Task InitializeAsync(ClientInfo clientInfo, CancellationToken cancellationToken = default) {
        Guard.NotNull(clientInfo, nameof(clientInfo));

        var parameters = new JsonObject()
            .Add("clientInfo", new JsonObject()
                .Add("name", clientInfo.Name)
                .Add("title", clientInfo.Title)
                .Add("version", clientInfo.Version));

        await _rpc.CallAsync("initialize", parameters, cancellationToken).ConfigureAwait(false);
        await _rpc.NotifyAsync("initialized", null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ChatGptLoginStart> StartChatGptLoginAsync(CancellationToken cancellationToken = default) {
        var parameters = new JsonObject().Add("type", "chatgpt");
        var result = await _rpc.CallAsync("account/login/start", parameters, cancellationToken).ConfigureAwait(false);
        var obj = result?.AsObject();
        if (obj is null) {
            throw new InvalidOperationException("Unexpected login response.");
        }
        var loginId = obj.GetString("loginId") ?? string.Empty;
        var authUrl = obj.GetString("authUrl") ?? string.Empty;
        return new ChatGptLoginStart(loginId, authUrl);
    }

    public Task LoginWithApiKeyAsync(string apiKey, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(apiKey, nameof(apiKey));
        var parameters = new JsonObject()
            .Add("type", "apiKey")
            .Add("apiKey", apiKey);
        return _rpc.CallAsync("account/login/start", parameters, cancellationToken);
    }

    public async Task<AccountInfo> ReadAccountAsync(CancellationToken cancellationToken = default) {
        var result = await _rpc.CallAsync("account/read", null, cancellationToken).ConfigureAwait(false);
        var obj = result?.AsObject();
        if (obj is null) {
            throw new InvalidOperationException("Unexpected account response.");
        }
        var email = obj.GetString("email");
        var plan = obj.GetString("planType");
        var id = obj.GetString("id");
        return new AccountInfo(email, plan, id);
    }

    public Task LogoutAsync(CancellationToken cancellationToken = default) => _rpc.CallAsync("account/logout", null, cancellationToken);

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

        var result = await _rpc.CallAsync("thread/start", parameters, cancellationToken).ConfigureAwait(false);
        var obj = result?.AsObject();
        var threadObj = obj?.GetObject("thread");
        if (threadObj is null) {
            throw new InvalidOperationException("Unexpected thread response.");
        }
        return ThreadInfo.FromJson(threadObj);
    }

    public async Task<TurnInfo> StartTurnAsync(string threadId, string text, CancellationToken cancellationToken = default) {
        return await StartTurnAsync(threadId, text, null, null, null, null, cancellationToken).ConfigureAwait(false);
    }

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

        var result = await _rpc.CallAsync("turn/start", parameters, cancellationToken).ConfigureAwait(false);
        var obj = result?.AsObject();
        var turnObj = obj?.GetObject("turn");
        if (turnObj is null) {
            throw new InvalidOperationException("Unexpected turn response.");
        }
        return TurnInfo.FromJson(turnObj);
    }

    public async Task<ThreadInfo> ResumeThreadAsync(string threadId, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(threadId, nameof(threadId));
        var parameters = new JsonObject().Add("threadId", threadId);
        var result = await _rpc.CallAsync("thread/resume", parameters, cancellationToken).ConfigureAwait(false);
        var obj = result?.AsObject();
        var threadObj = obj?.GetObject("thread");
        if (threadObj is null) {
            throw new InvalidOperationException("Unexpected thread response.");
        }
        return ThreadInfo.FromJson(threadObj);
    }

    public async Task<ThreadInfo> ForkThreadAsync(string threadId, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(threadId, nameof(threadId));
        var parameters = new JsonObject().Add("threadId", threadId);
        var result = await _rpc.CallAsync("thread/fork", parameters, cancellationToken).ConfigureAwait(false);
        var obj = result?.AsObject();
        var threadObj = obj?.GetObject("thread");
        if (threadObj is null) {
            throw new InvalidOperationException("Unexpected thread response.");
        }
        return ThreadInfo.FromJson(threadObj);
    }

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

        var result = await _rpc.CallAsync("thread/list", parameters, cancellationToken).ConfigureAwait(false);
        var obj = result?.AsObject() ?? new JsonObject();
        var dataArray = obj.GetArray("data");
        var items = new List<ThreadInfo>();
        if (dataArray is not null) {
            foreach (var entry in dataArray) {
                var threadObj = entry.AsObject();
                if (threadObj is not null) {
                    items.Add(ThreadInfo.FromJson(threadObj));
                }
            }
        }
        var nextCursor = obj.GetString("nextCursor");
        return new ThreadListResult(items, nextCursor);
    }

    public async Task<ThreadIdListResult> ListLoadedThreadsAsync(CancellationToken cancellationToken = default) {
        var result = await _rpc.CallAsync("thread/loaded/list", null, cancellationToken).ConfigureAwait(false);
        var obj = result?.AsObject() ?? new JsonObject();
        var dataArray = obj.GetArray("data");
        var items = new List<string>();
        if (dataArray is not null) {
            foreach (var entry in dataArray) {
                var value = entry.AsString();
                if (!string.IsNullOrWhiteSpace(value)) {
                    items.Add(value);
                }
            }
        }
        return new ThreadIdListResult(items);
    }

    public Task ArchiveThreadAsync(string threadId, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(threadId, nameof(threadId));
        var parameters = new JsonObject().Add("threadId", threadId);
        return _rpc.CallAsync("thread/archive", parameters, cancellationToken);
    }

    public async Task<ThreadInfo> RollbackThreadAsync(string threadId, int turns, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(threadId, nameof(threadId));
        var parameters = new JsonObject()
            .Add("threadId", threadId)
            .Add("turns", turns);
        var result = await _rpc.CallAsync("thread/rollback", parameters, cancellationToken).ConfigureAwait(false);
        var obj = result?.AsObject();
        var threadObj = obj?.GetObject("thread");
        if (threadObj is null) {
            throw new InvalidOperationException("Unexpected thread response.");
        }
        return ThreadInfo.FromJson(threadObj);
    }

    public Task InterruptTurnAsync(string threadId, string turnId, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(threadId, nameof(threadId));
        Guard.NotNullOrWhiteSpace(turnId, nameof(turnId));
        var parameters = new JsonObject()
            .Add("threadId", threadId)
            .Add("turnId", turnId);
        return _rpc.CallAsync("turn/interrupt", parameters, cancellationToken);
    }

    public async Task<ReviewStartResult> StartReviewAsync(string threadId, string delivery, ReviewTarget target, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(threadId, nameof(threadId));
        Guard.NotNullOrWhiteSpace(delivery, nameof(delivery));
        Guard.NotNull(target, nameof(target));

        var parameters = new JsonObject()
            .Add("threadId", threadId)
            .Add("delivery", delivery)
            .Add("target", target.Payload);

        var result = await _rpc.CallAsync("review/start", parameters, cancellationToken).ConfigureAwait(false);
        var obj = result?.AsObject();
        var turnObj = obj?.GetObject("turn");
        if (turnObj is null) {
            throw new InvalidOperationException("Unexpected review response.");
        }
        var reviewThreadId = obj?.GetString("reviewThreadId");
        return new ReviewStartResult(TurnInfo.FromJson(turnObj), reviewThreadId);
    }

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

        var result = await _rpc.CallAsync("command/exec", parameters, cancellationToken).ConfigureAwait(false);
        var obj = result?.AsObject();
        var exitCode = obj?.GetInt64("exitCode");
        var stdout = obj?.GetString("stdout");
        var stderr = obj?.GetString("stderr");
        return new CommandExecResult(exitCode is null ? null : (int?)exitCode.Value, stdout, stderr);
    }

    public Task<JsonValue?> ListModelsAsync(CancellationToken cancellationToken = default) {
        return _rpc.CallAsync("model/list", null, cancellationToken);
    }

    public Task<JsonValue?> ListCollaborationModesAsync(CancellationToken cancellationToken = default) {
        return _rpc.CallAsync("collaborationMode/list", null, cancellationToken);
    }

    public Task<JsonValue?> ListSkillsAsync(IReadOnlyList<string>? cwds = null, bool? forceReload = null,
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
        return _rpc.CallAsync("skills/list", parameters, cancellationToken);
    }

    public Task WriteSkillConfigAsync(string path, bool enabled, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(path, nameof(path));
        var parameters = new JsonObject()
            .Add("path", path)
            .Add("enabled", enabled);
        return _rpc.CallAsync("skills/config/write", parameters, cancellationToken);
    }

    public Task<JsonValue?> ReadConfigAsync(CancellationToken cancellationToken = default) {
        return _rpc.CallAsync("config/read", null, cancellationToken);
    }

    public Task WriteConfigValueAsync(string key, JsonValue value, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(key, nameof(key));
        Guard.NotNull(value, nameof(value));
        var parameters = new JsonObject()
            .Add("key", key)
            .Add("value", value);
        return _rpc.CallAsync("config/value/write", parameters, cancellationToken);
    }

    public Task WriteConfigBatchAsync(IReadOnlyList<ConfigEntry> entries, CancellationToken cancellationToken = default) {
        Guard.NotNull(entries, nameof(entries));
        var items = new JsonArray();
        foreach (var entry in entries) {
            items.Add(new JsonObject()
                .Add("key", entry.Key)
                .Add("value", entry.Value));
        }
        var parameters = new JsonObject().Add("items", items);
        return _rpc.CallAsync("config/batchWrite", parameters, cancellationToken);
    }

    public Task<JsonValue?> ReadConfigRequirementsAsync(CancellationToken cancellationToken = default) {
        return _rpc.CallAsync("configRequirements/read", null, cancellationToken);
    }

    public async Task<McpOauthLoginStart> StartMcpOauthLoginAsync(string? serverId, string? serverName = null,
        CancellationToken cancellationToken = default) {
        var parameters = new JsonObject();
        if (!string.IsNullOrWhiteSpace(serverId)) {
            parameters.Add("serverId", serverId);
        }
        if (!string.IsNullOrWhiteSpace(serverName)) {
            parameters.Add("serverName", serverName);
        }
        var result = await _rpc.CallAsync("mcpServer/oauth/login", parameters, cancellationToken).ConfigureAwait(false);
        var obj = result?.AsObject();
        var loginId = obj?.GetString("loginId");
        var authUrl = obj?.GetString("authUrl");
        return new McpOauthLoginStart(loginId, authUrl);
    }

    public Task<JsonValue?> ListMcpServerStatusAsync(string? cursor = null, int? limit = null, CancellationToken cancellationToken = default) {
        var parameters = new JsonObject();
        if (!string.IsNullOrWhiteSpace(cursor)) {
            parameters.Add("cursor", cursor);
        }
        if (limit.HasValue) {
            parameters.Add("limit", limit.Value);
        }
        return _rpc.CallAsync("mcpServerStatus/list", parameters, cancellationToken);
    }

    public Task<JsonValue?> ReloadMcpServerConfigAsync(CancellationToken cancellationToken = default) {
        return _rpc.CallAsync("config/mcpServer/reload", null, cancellationToken);
    }

    public Task<JsonValue?> RequestUserInputAsync(IReadOnlyList<string> questions, CancellationToken cancellationToken = default) {
        Guard.NotNull(questions, nameof(questions));
        var array = new JsonArray();
        foreach (var question in questions) {
            array.Add(question);
        }
        var parameters = new JsonObject().Add("questions", array);
        return _rpc.CallAsync("tool/requestUserInput", parameters, cancellationToken);
    }

    public Task UploadFeedbackAsync(string content, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(content, nameof(content));
        var parameters = new JsonObject().Add("content", content);
        return _rpc.CallAsync("feedback/upload", parameters, cancellationToken);
    }

    public Task<JsonValue?> CallAsync(string method, JsonObject? parameters, CancellationToken cancellationToken = default) {
        return _rpc.CallAsync(method, parameters, cancellationToken);
    }

    public Task NotifyAsync(string method, JsonObject? parameters, CancellationToken cancellationToken = default) {
        return _rpc.NotifyAsync(method, parameters, cancellationToken);
    }

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

    public void Dispose() {
        if (_disposed) {
            return;
        }
        _disposed = true;

        _cts.Cancel();
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
