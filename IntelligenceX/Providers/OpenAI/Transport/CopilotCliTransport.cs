using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Copilot;
using IntelligenceX.Json;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.Telemetry;
using IntelligenceX.Utils;

namespace IntelligenceX.OpenAI.Transport;

internal sealed class CopilotCliTransport : IOpenAITransport {
    private readonly CopilotClientOptions _options;
    private readonly Func<CopilotSession, CopilotMessageOptions, TimeSpan?, CancellationToken, Task<string?>> _sendAndWaitAsync;
    private readonly SemaphoreSlim _clientGate = new(1, 1);
    private readonly object _threadsLock = new();
    private readonly Dictionary<string, CopilotThreadState> _threads = new(StringComparer.Ordinal);
    private CopilotClient? _client;
    private int _disposeState;

    private sealed class CopilotThreadState {
        public CopilotThreadState(string model, CopilotSession session) {
            Model = model;
            Session = session;
            CreatedAtUtc = DateTimeOffset.UtcNow;
        }

        public string Model { get; set; }
        public CopilotSession Session { get; set; }
        public DateTimeOffset CreatedAtUtc { get; }
        public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    }

    public CopilotCliTransport(CopilotClientOptions options) : this(options, null) { }

    internal CopilotCliTransport(CopilotClientOptions options,
        Func<CopilotSession, CopilotMessageOptions, TimeSpan?, CancellationToken, Task<string?>>? sendAndWaitAsync) {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _sendAndWaitAsync = sendAndWaitAsync ?? ((session, message, timeout, cancellationToken) =>
            session.SendAndWaitAsync(message, timeout, cancellationToken));
        if (!_options.AutoInstallCli) {
            // Subscription runtime should "just work" for app callers, without manual CLI install.
            _options.AutoInstallCli = true;
        }
    }

    public OpenAITransportKind Kind => OpenAITransportKind.CopilotCli;
    public AppServerClient? RawAppServerClient => null;
    internal bool IsDisposedForDiagnostics => Volatile.Read(ref _disposeState) != 0;

    public event EventHandler<string>? DeltaReceived;
    public event EventHandler<LoginEventArgs>? LoginStarted;
    public event EventHandler<LoginEventArgs>? LoginCompleted;
    public event EventHandler<string>? ProtocolLineReceived;
    public event EventHandler<string>? StandardErrorReceived;
    public event EventHandler<RpcCallStartedEventArgs>? RpcCallStarted;
    public event EventHandler<RpcCallCompletedEventArgs>? RpcCallCompleted;

    public async Task InitializeAsync(ClientInfo clientInfo, CancellationToken cancellationToken) {
        ThrowIfDisposed();
        _ = clientInfo; // Copilot CLI transport does not consume ClientInfo metadata.
        _ = await EnsureClientAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<HealthCheckResult> HealthCheckAsync(string? method, TimeSpan? timeout, CancellationToken cancellationToken) {
        ThrowIfDisposed();
        _ = method;
        var sw = Stopwatch.StartNew();
        using var timeoutCts = timeout.HasValue
            ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
            : null;
        if (timeoutCts is not null) {
            timeoutCts.CancelAfter(timeout ?? TimeSpan.FromSeconds(10));
        }
        var token = timeoutCts?.Token ?? cancellationToken;

        try {
            var client = await EnsureClientAsync(token).ConfigureAwait(false);
            _ = await client.GetStatusAsync(token).ConfigureAwait(false);
            return new HealthCheckResult(true, "copilot-cli/status", null, sw.Elapsed);
        } catch (Exception ex) {
            return new HealthCheckResult(false, "copilot-cli/status", ex, sw.Elapsed);
        }
    }

    public async Task<AccountInfo> GetAccountAsync(CancellationToken cancellationToken) {
        ThrowIfDisposed();
        var client = await EnsureClientAsync(cancellationToken).ConfigureAwait(false);
        var auth = await client.GetAuthStatusAsync(cancellationToken).ConfigureAwait(false);
        if (!auth.IsAuthenticated) {
            throw new OpenAIAuthenticationRequiredException(
                string.IsNullOrWhiteSpace(auth.StatusMessage)
                    ? "Copilot subscription login required. Run Sign In to complete GitHub Copilot authentication."
                    : auth.StatusMessage!);
        }

        var accountId = string.IsNullOrWhiteSpace(auth.Login) ? "copilot" : auth.Login!.Trim();
        var obj = new JsonObject()
            .Add("id", accountId)
            .Add("email", accountId)
            .Add("planType", "github-copilot");
        return AccountInfo.FromJson(obj);
    }

    public Task LogoutAsync(CancellationToken cancellationToken) {
        ThrowIfDisposed();
        _ = cancellationToken;
        // Copilot auth lifecycle is managed by Copilot CLI; explicit logout is not available in this transport.
        return Task.CompletedTask;
    }

    public async Task<ModelListResult> ListModelsAsync(CancellationToken cancellationToken) {
        ThrowIfDisposed();
        var client = await EnsureClientAsync(cancellationToken).ConfigureAwait(false);
        var models = await client.ListModelsAsync(cancellationToken).ConfigureAwait(false);

        var list = new List<ModelInfo>(models.Count);
        for (var i = 0; i < models.Count; i++) {
            var model = models[i];
            var id = string.IsNullOrWhiteSpace(model.Id) ? string.Empty : model.Id.Trim();
            if (id.Length == 0) {
                continue;
            }
            var displayName = string.IsNullOrWhiteSpace(model.Name) ? id : model.Name!.Trim();
            list.Add(new ModelInfo(
                id: id,
                model: id,
                displayName: displayName,
                description: string.Empty,
                supportedReasoningEfforts: Array.Empty<ReasoningEffortOption>(),
                defaultReasoningEffort: null,
                isDefault: i == 0,
                raw: model.Raw,
                additional: model.Additional));
        }

        var raw = new JsonObject().Add("models", new JsonArray());
        return new ModelListResult(list, nextCursor: null, raw, additional: null);
    }

    public async Task<ChatGptLoginStart> LoginChatGptAsync(Action<string>? onUrl, Func<string, Task<string>>? onPrompt,
        bool useLocalListener, TimeSpan timeout, CancellationToken cancellationToken) {
        ThrowIfDisposed();
        _ = onUrl;
        _ = onPrompt;
        _ = useLocalListener;

        var loginId = Guid.NewGuid().ToString("N");
        LoginStarted?.Invoke(this, new LoginEventArgs("copilot", loginId));
        try {
            var client = await EnsureClientAsync(cancellationToken).ConfigureAwait(false);
            var auth = await client.GetAuthStatusAsync(cancellationToken).ConfigureAwait(false);
            if (!auth.IsAuthenticated) {
                LaunchCopilotLogin();
                var authenticated = await WaitForCopilotAuthAsync(client, timeout, cancellationToken).ConfigureAwait(false);
                if (!authenticated) {
                    throw new OpenAIAuthenticationRequiredException(
                        "Copilot sign-in did not complete in time. Finish the GitHub Copilot browser flow and retry.");
                }
            }

            var raw = new JsonObject()
                .Add("loginId", loginId)
                .Add("authUrl", string.Empty);
            LoginCompleted?.Invoke(this, new LoginEventArgs("copilot", loginId));
            return new ChatGptLoginStart(loginId, string.Empty, raw, null);
        } catch {
            LoginCompleted?.Invoke(this, new LoginEventArgs("copilot", loginId));
            throw;
        }
    }

    public Task LoginApiKeyAsync(string apiKey, CancellationToken cancellationToken) {
        ThrowIfDisposed();
        _ = apiKey;
        _ = cancellationToken;
        throw new NotSupportedException("API key login is not supported with Copilot CLI transport.");
    }

    public async Task<ThreadInfo> StartThreadAsync(string model, string? currentDirectory, string? approvalPolicy,
        string? sandbox, CancellationToken cancellationToken) {
        ThrowIfDisposed();
        _ = currentDirectory;
        _ = approvalPolicy;
        _ = sandbox;

        var client = await EnsureClientAsync(cancellationToken).ConfigureAwait(false);
        await EnsureCopilotAuthenticatedAsync(client, cancellationToken).ConfigureAwait(false);

        var effectiveModel = string.IsNullOrWhiteSpace(model) ? string.Empty : model.Trim();
        var session = await client.CreateSessionAsync(new CopilotSessionOptions {
            Model = effectiveModel
        }, cancellationToken).ConfigureAwait(false);

        var threadId = session.SessionId;
        var state = new CopilotThreadState(effectiveModel, session);
        lock (_threadsLock) {
            _threads[threadId] = state;
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var raw = new JsonObject()
            .Add("id", threadId)
            .Add("modelProvider", "copilot-cli")
            .Add("createdAt", now)
            .Add("updatedAt", now);
        return ThreadInfo.FromJson(raw);
    }

    public Task<ThreadInfo> ResumeThreadAsync(string threadId, CancellationToken cancellationToken) {
        ThrowIfDisposed();
        _ = cancellationToken;
        if (string.IsNullOrWhiteSpace(threadId)) {
            throw new ArgumentException("Thread id cannot be empty.", nameof(threadId));
        }

        CopilotThreadState? state;
        lock (_threadsLock) {
            _threads.TryGetValue(threadId.Trim(), out state);
        }
        if (state is null) {
            throw new InvalidOperationException($"Thread '{threadId}' was not found in Copilot CLI transport.");
        }

        state.UpdatedAtUtc = DateTimeOffset.UtcNow;
        var raw = new JsonObject()
            .Add("id", threadId.Trim())
            .Add("modelProvider", "copilot-cli")
            .Add("createdAt", state.CreatedAtUtc.ToUnixTimeSeconds())
            .Add("updatedAt", state.UpdatedAtUtc.ToUnixTimeSeconds());
        return Task.FromResult(ThreadInfo.FromJson(raw));
    }

    public async Task<TurnInfo> StartTurnAsync(string threadId, ChatInput input, ChatOptions? options, string? currentDirectory,
        string? approvalPolicy, SandboxPolicy? sandboxPolicy, CancellationToken cancellationToken) {
        ThrowIfDisposed();
        _ = currentDirectory;
        _ = approvalPolicy;
        _ = sandboxPolicy;

        if (string.IsNullOrWhiteSpace(threadId)) {
            throw new ArgumentException("Thread id cannot be empty.", nameof(threadId));
        }
        if (input is null) {
            throw new ArgumentNullException(nameof(input));
        }

        CopilotThreadState? state;
        lock (_threadsLock) {
            _threads.TryGetValue(threadId.Trim(), out state);
        }
        if (state is null) {
            throw new InvalidOperationException($"Thread '{threadId}' was not found in Copilot CLI transport.");
        }

        var prompt = BuildPromptText(input);
        if (string.IsNullOrWhiteSpace(prompt)) {
            throw new InvalidOperationException("Chat input produced no text content for Copilot runtime.");
        }

        var requestedModel = options?.Model;
        if (requestedModel is not null) {
            requestedModel = requestedModel.Trim();
        }

        if (!string.IsNullOrEmpty(requestedModel)) {
            var requestedModelValue = requestedModel!;
            if (!string.Equals(state.Model, requestedModelValue, StringComparison.OrdinalIgnoreCase)) {
                var clientForModelSwitch = await EnsureClientAsync(cancellationToken).ConfigureAwait(false);
                var switched = await clientForModelSwitch.CreateSessionAsync(new CopilotSessionOptions {
                    Model = requestedModelValue
                }, cancellationToken).ConfigureAwait(false);
                state.Session.Dispose();
                state.Session = switched;
                state.Model = requestedModelValue;
            }
        }

        using var streamSubscription = state.Session.OnEvent(evt => {
            if (!string.IsNullOrWhiteSpace(evt.DeltaContent)) {
                DeltaReceived?.Invoke(this, evt.DeltaContent!);
            }
        });

        var response = await _sendAndWaitAsync(state.Session, new CopilotMessageOptions {
            Prompt = prompt
        }, Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);

        state.UpdatedAtUtc = DateTimeOffset.UtcNow;
        var text = response ?? string.Empty;
        var turnId = Guid.NewGuid().ToString("N");
        var output = new JsonArray().Add(new JsonObject()
            .Add("type", "text")
            .Add("text", text));
        var raw = new JsonObject()
            .Add("id", turnId)
            .Add("status", "completed")
            .Add("output", output);
        return TurnInfo.FromJson(raw);
    }

    public void Dispose() {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0) {
            return;
        }

        var gateEntered = false;
        try {
            _clientGate.Wait();
            gateEntered = true;

            lock (_threadsLock) {
                foreach (var state in _threads.Values) {
                    try {
                        state.Session.Dispose();
                    } catch {
                        // Ignore session cleanup failures.
                    }
                }
                _threads.Clear();
            }

            var client = _client;
            _client = null;
            if (client is null) {
                return;
            }

            DetachClientEvents(client);
            client.Dispose();
        } finally {
            if (gateEntered) {
                _clientGate.Dispose();
            }
        }
    }

    private async Task<CopilotClient> EnsureClientAsync(CancellationToken cancellationToken) {
        ThrowIfDisposed();

        var existing = _client;
        if (existing is not null) {
            ThrowIfDisposed();
            return existing;
        }

        var gateEntered = false;
        try {
            try {
                await _clientGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                gateEntered = true;
            } catch (ObjectDisposedException) {
                throw CreateDisposedException();
            }

            ThrowIfDisposed();
            if (_client is null) {
                var client = await CopilotClient.StartAsync(_options, cancellationToken).ConfigureAwait(false);
                if (Volatile.Read(ref _disposeState) != 0) {
                    client.Dispose();
                    throw CreateDisposedException();
                }
                _client = client;
                AttachClientEvents(client);
            }
            var active = _client;
            if (active is null) {
                throw new InvalidOperationException("Copilot client initialization failed.");
            }
            ThrowIfDisposed();
            return active;
        } finally {
            if (gateEntered) {
                _clientGate.Release();
            }
        }
    }

    private void ThrowIfDisposed() {
        if (Volatile.Read(ref _disposeState) != 0) {
            throw CreateDisposedException();
        }
    }

    private static ObjectDisposedException CreateDisposedException() {
        return new ObjectDisposedException(nameof(CopilotCliTransport), "Copilot CLI transport has been disposed.");
    }

    private void AttachClientEvents(CopilotClient client) {
        client.ProtocolMessageReceived += OnProtocolMessageReceived;
        client.StandardErrorReceived += OnStandardErrorReceived;
        client.RpcCallStarted += OnRpcCallStarted;
        client.RpcCallCompleted += OnRpcCallCompleted;
    }

    private void DetachClientEvents(CopilotClient client) {
        client.ProtocolMessageReceived -= OnProtocolMessageReceived;
        client.StandardErrorReceived -= OnStandardErrorReceived;
        client.RpcCallStarted -= OnRpcCallStarted;
        client.RpcCallCompleted -= OnRpcCallCompleted;
    }

    private void OnProtocolMessageReceived(object? sender, string line) => ProtocolLineReceived?.Invoke(this, line);
    private void OnStandardErrorReceived(object? sender, string line) => StandardErrorReceived?.Invoke(this, line);
    private void OnRpcCallStarted(object? sender, RpcCallStartedEventArgs args) => RpcCallStarted?.Invoke(this, args);
    private void OnRpcCallCompleted(object? sender, RpcCallCompletedEventArgs args) => RpcCallCompleted?.Invoke(this, args);

    private static string BuildPromptText(ChatInput input) {
        var json = input.ToJson();
        if (json.Count == 0) {
            return string.Empty;
        }

        var parts = new List<string>(json.Count);
        for (var i = 0; i < json.Count; i++) {
            var item = json[i].AsObject();
            if (item is null) {
                continue;
            }

            var type = item.GetString("type") ?? string.Empty;
            if (string.Equals(type, "text", StringComparison.OrdinalIgnoreCase)) {
                var text = item.GetString("text");
                if (!string.IsNullOrWhiteSpace(text)) {
                    parts.Add(text!.Trim());
                }
                continue;
            }

            if (string.Equals(type, "custom_tool_call_output", StringComparison.OrdinalIgnoreCase)) {
                var output = item.GetString("output");
                if (!string.IsNullOrWhiteSpace(output)) {
                    parts.Add(output!.Trim());
                }
                continue;
            }

            if (string.Equals(type, "image", StringComparison.OrdinalIgnoreCase)) {
                var path = item.GetString("path");
                var url = item.GetString("url");
                if (!string.IsNullOrWhiteSpace(path)) {
                    parts.Add("[image path: " + path!.Trim() + "]");
                } else if (!string.IsNullOrWhiteSpace(url)) {
                    parts.Add("[image url: " + url!.Trim() + "]");
                }
            }
        }

        if (parts.Count == 0) {
            return string.Empty;
        }

        var builder = new StringBuilder();
        for (var i = 0; i < parts.Count; i++) {
            if (builder.Length > 0) {
                builder.AppendLine();
                builder.AppendLine();
            }
            builder.Append(parts[i]);
        }
        return builder.ToString();
    }

    private static async Task EnsureCopilotAuthenticatedAsync(CopilotClient client, CancellationToken cancellationToken) {
        var auth = await client.GetAuthStatusAsync(cancellationToken).ConfigureAwait(false);
        if (auth.IsAuthenticated) {
            return;
        }

        throw new OpenAIAuthenticationRequiredException(
            string.IsNullOrWhiteSpace(auth.StatusMessage)
                ? "Copilot subscription login required. Click Sign In to authenticate."
                : auth.StatusMessage!);
    }

    private static async Task<bool> WaitForCopilotAuthAsync(CopilotClient client, TimeSpan timeout, CancellationToken cancellationToken) {
        var effectiveTimeout = timeout <= TimeSpan.Zero ? TimeSpan.FromMinutes(3) : timeout;
        var started = DateTime.UtcNow;
        while (DateTime.UtcNow - started < effectiveTimeout) {
            cancellationToken.ThrowIfCancellationRequested();
            var status = await client.GetAuthStatusAsync(cancellationToken).ConfigureAwait(false);
            if (status.IsAuthenticated) {
                return true;
            }
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
        }
        return false;
    }

    private void LaunchCopilotLogin() {
        var cliPath = string.IsNullOrWhiteSpace(_options.CliPath) ? "copilot" : _options.CliPath!.Trim();
        try {
            var psi = new ProcessStartInfo {
                FileName = cliPath,
                Arguments = "auth login",
                UseShellExecute = true
            };
            var process = Process.Start(psi);
            process?.Dispose();
        } catch (Exception ex) {
            throw new InvalidOperationException(
                "Unable to launch Copilot sign-in automatically. Run `copilot auth login` and retry.", ex);
        }
    }
}
