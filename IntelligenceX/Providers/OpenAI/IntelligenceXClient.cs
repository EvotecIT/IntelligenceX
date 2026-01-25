using System;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.Json;
using IntelligenceX.Rpc;
using IntelligenceX.Utils;

namespace IntelligenceX.OpenAI;

public sealed class IntelligenceXClient : IAsyncDisposable {
    private readonly AppServerClient _client;
    private string? _currentThreadId;
    private string _defaultModel;
    private string? _defaultWorkingDirectory;
    private string? _defaultApprovalPolicy;
    private SandboxPolicy? _defaultSandboxPolicy;

    private IntelligenceXClient(AppServerClient client, string defaultModel, string? workingDirectory, string? approvalPolicy, SandboxPolicy? sandboxPolicy) {
        _client = client;
        _defaultModel = defaultModel;
        _defaultWorkingDirectory = workingDirectory;
        _defaultApprovalPolicy = approvalPolicy;
        _defaultSandboxPolicy = sandboxPolicy;
        _client.NotificationReceived += OnNotificationReceived;
    }

    public event EventHandler<string>? DeltaReceived;

    public AppServerClient RawClient => _client;

    public static async Task<IntelligenceXClient> ConnectAsync(IntelligenceXClientOptions? options = null, CancellationToken cancellationToken = default) {
        options ??= new IntelligenceXClientOptions();
        Guard.NotNull(options.ClientInfo, nameof(options.ClientInfo));
        Guard.NotNull(options.AppServerOptions, nameof(options.AppServerOptions));

        var client = await AppServerClient.StartAsync(options.AppServerOptions, cancellationToken).ConfigureAwait(false);
        var wrapper = new IntelligenceXClient(client, options.DefaultModel, options.DefaultWorkingDirectory, options.DefaultApprovalPolicy, options.DefaultSandboxPolicy);
        if (options.AutoInitialize) {
            await wrapper.InitializeAsync(options.ClientInfo, cancellationToken).ConfigureAwait(false);
        }
        return wrapper;
    }

    public Task InitializeAsync(ClientInfo clientInfo, CancellationToken cancellationToken = default) {
        return _client.InitializeAsync(clientInfo, cancellationToken);
    }

    public Task<AccountInfo> GetAccountAsync(CancellationToken cancellationToken = default) {
        return _client.ReadAccountAsync(cancellationToken);
    }

    public Task LogoutAsync(CancellationToken cancellationToken = default) {
        return _client.LogoutAsync(cancellationToken);
    }

    public async Task<ChatGptLoginStart> LoginChatGptAsync(CancellationToken cancellationToken = default) {
        return await _client.StartChatGptLoginAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task LoginChatGptAndWaitAsync(Action<string>? onUrl = null, CancellationToken cancellationToken = default) {
        var login = await _client.StartChatGptLoginAsync(cancellationToken).ConfigureAwait(false);
        onUrl?.Invoke(login.AuthUrl);
        await _client.WaitForLoginCompletionAsync(login.LoginId, cancellationToken).ConfigureAwait(false);
    }

    public Task LoginApiKeyAsync(string apiKey, CancellationToken cancellationToken = default) {
        return _client.LoginWithApiKeyAsync(apiKey, cancellationToken);
    }

    public async Task<ThreadInfo> StartNewThreadAsync(string? model = null, string? currentDirectory = null, string? approvalPolicy = null,
        string? sandbox = null, CancellationToken cancellationToken = default) {
        var thread = await _client.StartThreadAsync(model ?? _defaultModel, currentDirectory, approvalPolicy, sandbox, cancellationToken)
            .ConfigureAwait(false);
        _currentThreadId = thread.Id;
        return thread;
    }

    public Task<ThreadInfo> UseThreadAsync(string threadId, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(threadId, nameof(threadId));
        _currentThreadId = threadId;
        return _client.ResumeThreadAsync(threadId, cancellationToken);
    }

    public async Task<TurnInfo> ChatAsync(string text, string? model = null, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(text, nameof(text));
        var input = Chat.ChatInput.FromText(text);
        return await ChatAsync(input, new Chat.ChatOptions { Model = model }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TurnInfo> ChatWithImagePathAsync(string text, string imagePath, Chat.ChatOptions? options = null,
        CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(text, nameof(text));
        Guard.NotNullOrWhiteSpace(imagePath, nameof(imagePath));
        var input = Chat.ChatInput.FromTextWithImagePath(text, imagePath);
        return await ChatAsync(input, options, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TurnInfo> ChatWithImageUrlAsync(string text, string imageUrl, Chat.ChatOptions? options = null,
        CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(text, nameof(text));
        Guard.NotNullOrWhiteSpace(imageUrl, nameof(imageUrl));
        var input = Chat.ChatInput.FromTextWithImageUrl(text, imageUrl);
        return await ChatAsync(input, options, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TurnInfo> ChatAsync(Chat.ChatInput input, Chat.ChatOptions? options = null, CancellationToken cancellationToken = default) {
        Guard.NotNull(input, nameof(input));
        options ??= new Chat.ChatOptions();
        if (options.NewThread) {
            _currentThreadId = null;
        }
        await EnsureThreadAsync(options.Model, cancellationToken).ConfigureAwait(false);

        var workspace = options.Workspace;
        var model = options.Model ?? _defaultModel;
        var cwd = options.WorkingDirectory ?? _defaultWorkingDirectory;
        var approval = options.ApprovalPolicy ?? _defaultApprovalPolicy;
        var sandbox = options.SandboxPolicy ?? _defaultSandboxPolicy;

        if (!string.IsNullOrWhiteSpace(workspace)) {
            if (string.IsNullOrWhiteSpace(cwd)) {
                cwd = workspace;
            }
            if (options.SandboxPolicy is null) {
                sandbox = new SandboxPolicy("workspace", options.AllowNetwork, new[] { workspace! });
            }
            if (string.IsNullOrWhiteSpace(approval)) {
                approval = "auto";
            }
        }

        return await _client.StartTurnAsync(_currentThreadId!, input.ToJson(), model, cwd, approval, sandbox, cancellationToken)
            .ConfigureAwait(false);
    }

    public IntelligenceXClient ConfigureWorkspace(string workingDirectory, bool allowNetwork = false) {
        _defaultWorkingDirectory = workingDirectory;
        _defaultApprovalPolicy = _defaultApprovalPolicy ?? "auto";
        _defaultSandboxPolicy = new SandboxPolicy("workspace", allowNetwork, new[] { workingDirectory });
        return this;
    }

    public IntelligenceXClient ConfigureDefaults(string? model = null, string? workingDirectory = null, string? approvalPolicy = null,
        SandboxPolicy? sandboxPolicy = null) {
        if (!string.IsNullOrWhiteSpace(model)) {
            _defaultModel = model;
        }
        if (!string.IsNullOrWhiteSpace(workingDirectory)) {
            _defaultWorkingDirectory = workingDirectory;
        }
        if (!string.IsNullOrWhiteSpace(approvalPolicy)) {
            _defaultApprovalPolicy = approvalPolicy;
        }
        if (sandboxPolicy is not null) {
            _defaultSandboxPolicy = sandboxPolicy;
        }
        return this;
    }

    public IDisposable SubscribeDelta(Action<string> onDelta) {
        Guard.NotNull(onDelta, nameof(onDelta));
        void Handler(object? sender, string text) => onDelta(text);
        DeltaReceived += Handler;
        return new Subscription(() => DeltaReceived -= Handler);
    }

    private async Task EnsureThreadAsync(string? model, CancellationToken cancellationToken) {
        if (!string.IsNullOrWhiteSpace(_currentThreadId)) {
            return;
        }
        await StartNewThreadAsync(model, _defaultWorkingDirectory, _defaultApprovalPolicy, null, cancellationToken).ConfigureAwait(false);
    }

    private void OnNotificationReceived(object? sender, JsonRpcNotificationEventArgs args) {
        var delta = TryExtractDelta(args.Params);
        if (!string.IsNullOrWhiteSpace(delta)) {
            DeltaReceived?.Invoke(this, delta);
        }
    }

    private static string? TryExtractDelta(JsonValue? value) {
        return value?.AsObject()?.GetObject("delta")?.GetString("text");
    }

    public ValueTask DisposeAsync() {
        _client.NotificationReceived -= OnNotificationReceived;
        _client.Dispose();
        return ValueTask.CompletedTask;
    }

    private sealed class Subscription : IDisposable {
        private readonly Action _onDispose;
        private bool _disposed;

        public Subscription(Action onDispose) {
            _onDispose = onDispose;
        }

        public void Dispose() {
            if (_disposed) {
                return;
            }
            _disposed = true;
            _onDispose();
        }
    }
}
