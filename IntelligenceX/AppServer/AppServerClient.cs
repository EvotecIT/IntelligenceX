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
        var id = threadObj.GetString("id") ?? string.Empty;
        var preview = threadObj.GetString("preview");
        var modelValue = threadObj.GetString("model") ?? threadObj.GetString("modelProvider") ?? model;
        var createdAt = threadObj.GetInt64("createdAt");
        DateTimeOffset? createdAtValue = createdAt is null ? null : DateTimeOffset.FromUnixTimeSeconds(createdAt.Value);
        return new ThreadInfo(id, preview, modelValue, createdAtValue);
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
        var id = turnObj.GetString("id") ?? string.Empty;
        var status = turnObj.GetString("status");
        return new TurnInfo(id, status);
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
