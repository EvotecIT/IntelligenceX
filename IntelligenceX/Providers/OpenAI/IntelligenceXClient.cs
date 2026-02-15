using System;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.OpenAI.CompatibleHttp;
using IntelligenceX.OpenAI.Native;
using IntelligenceX.OpenAI.Transport;
using IntelligenceX.Rpc;
using IntelligenceX.Telemetry;
using IntelligenceX.Utils;

namespace IntelligenceX.OpenAI;

/// <summary>
/// Main client for OpenAI app-server and native transports.
/// </summary>
public sealed class IntelligenceXClient : IDisposable
#if NET5_0_OR_GREATER || NETSTANDARD2_1_OR_GREATER
    , IAsyncDisposable
#endif
{
    private readonly IOpenAITransport _transport;
    private string? _currentThreadId;
    private string _defaultModel;
    private string? _defaultWorkingDirectory;
    private string? _defaultApprovalPolicy;
    private SandboxPolicy? _defaultSandboxPolicy;

    private IntelligenceXClient(IOpenAITransport transport, string defaultModel, string? workingDirectory, string? approvalPolicy, SandboxPolicy? sandboxPolicy) {
        _transport = transport;
        _defaultModel = defaultModel;
        _defaultWorkingDirectory = workingDirectory;
        _defaultApprovalPolicy = approvalPolicy;
        _defaultSandboxPolicy = sandboxPolicy;
        _transport.DeltaReceived += OnDeltaReceived;
        _transport.RpcCallStarted += OnRpcCallStarted;
        _transport.RpcCallCompleted += OnRpcCallCompleted;
        _transport.LoginStarted += OnLoginStarted;
        _transport.LoginCompleted += OnLoginCompleted;
        _transport.ProtocolLineReceived += OnProtocolLineReceived;
        _transport.StandardErrorReceived += OnStandardErrorReceived;
    }

    /// <summary>
    /// Raised when streaming deltas are received.
    /// </summary>
    public event EventHandler<string>? DeltaReceived;
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
    /// Raised when a protocol line is received from the transport.
    /// </summary>
    public event EventHandler<string>? ProtocolLineReceived;
    /// <summary>
    /// Raised when the transport writes to standard error.
    /// </summary>
    public event EventHandler<string>? StandardErrorReceived;

    /// <summary>
    /// Gets the active transport kind.
    /// </summary>
    public OpenAITransportKind TransportKind => _transport.Kind;

    /// <summary>
    /// Gets the underlying app-server client when using app-server transport.
    /// </summary>
    public AppServerClient RawClient => RequireAppServer();

    /// <summary>
    /// Returns the underlying app-server client or throws if not active.
    /// </summary>
    public AppServerClient RequireAppServer() {
        var client = _transport.RawAppServerClient;
        if (client is null) {
            throw new InvalidOperationException("App-server transport is not active. Use TransportKind=AppServer.");
        }
        return client;
    }

    /// <summary>
    /// Connects to the configured transport and returns a ready client.
    /// </summary>
    /// <param name="options">Optional client options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task<IntelligenceXClient> ConnectAsync(IntelligenceXClientOptions? options = null, CancellationToken cancellationToken = default) {
        options ??= new IntelligenceXClientOptions();
        options.Validate();

        IOpenAITransport transport;
        switch (options.TransportKind) {
            case OpenAITransportKind.AppServer: {
                    var client = await AppServerClient.StartAsync(options.AppServerOptions, cancellationToken).ConfigureAwait(false);
                    transport = new AppServerTransport(client);
                    break;
                }
            case OpenAITransportKind.CompatibleHttp:
                transport = new OpenAICompatibleHttpTransport(options.CompatibleHttpOptions);
                break;
            default:
                transport = new OpenAINativeTransport(options.NativeOptions);
                break;
        }
        var wrapper = new IntelligenceXClient(transport, options.DefaultModel, options.DefaultWorkingDirectory, options.DefaultApprovalPolicy, options.DefaultSandboxPolicy);
        if (options.AutoInitialize) {
            await wrapper.InitializeAsync(options.ClientInfo, cancellationToken).ConfigureAwait(false);
        }
        return wrapper;
    }

    /// <summary>
    /// Initializes the transport with client metadata.
    /// </summary>
    /// <param name="clientInfo">Client identity information.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task InitializeAsync(ClientInfo clientInfo, CancellationToken cancellationToken = default) {
        return _transport.InitializeAsync(clientInfo, cancellationToken);
    }

    /// <summary>
    /// Executes a health check call.
    /// </summary>
    /// <param name="method">Optional method name to call.</param>
    /// <param name="timeout">Optional timeout.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<HealthCheckResult> HealthCheckAsync(string? method = null, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) {
        return _transport.HealthCheckAsync(method, timeout, cancellationToken);
    }

    /// <summary>
    /// Retrieves account information.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<AccountInfo> GetAccountAsync(CancellationToken cancellationToken = default) {
        return _transport.GetAccountAsync(cancellationToken);
    }

    /// <summary>
    /// Logs out of the current session.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task LogoutAsync(CancellationToken cancellationToken = default) {
        return _transport.LogoutAsync(cancellationToken);
    }

    /// <summary>
    /// Lists available models.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<ModelListResult> ListModelsAsync(CancellationToken cancellationToken = default) {
        return _transport.ListModelsAsync(cancellationToken);
    }

    /// <summary>
    /// Starts a ChatGPT login flow.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<ChatGptLoginStart> LoginChatGptAsync(CancellationToken cancellationToken = default) {
        return LoginChatGptAsync(null, null, true, null, cancellationToken);
    }

    /// <summary>
    /// Starts a ChatGPT login flow with callbacks and options.
    /// </summary>
    /// <param name="onUrl">Callback for the login URL.</param>
    /// <param name="onPrompt">Callback for interactive prompts.</param>
    /// <param name="useLocalListener">Whether to use a local listener.</param>
    /// <param name="timeout">Optional timeout.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<ChatGptLoginStart> LoginChatGptAsync(Action<string>? onUrl, Func<string, Task<string>>? onPrompt,
        bool useLocalListener = true, TimeSpan? timeout = null, CancellationToken cancellationToken = default) {
        var resolvedTimeout = timeout ?? TimeSpan.FromMinutes(3);
        return _transport.LoginChatGptAsync(onUrl, onPrompt, useLocalListener, resolvedTimeout, cancellationToken);
    }

    /// <summary>
    /// Starts a ChatGPT login flow and waits for completion.
    /// </summary>
    /// <param name="onUrl">Callback for the login URL.</param>
    /// <param name="onPrompt">Callback for interactive prompts.</param>
    /// <param name="useLocalListener">Whether to use a local listener.</param>
    /// <param name="timeout">Optional timeout.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task LoginChatGptAndWaitAsync(Action<string>? onUrl = null, Func<string, Task<string>>? onPrompt = null,
        bool useLocalListener = true, TimeSpan? timeout = null, CancellationToken cancellationToken = default) {
        await LoginChatGptAsync(onUrl, onPrompt, useLocalListener, timeout, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Ensures a valid ChatGPT login is available by using cached credentials when possible and falling back to the OAuth flow.
    /// </summary>
    /// <param name="forceLogin">When true, always runs the login flow even if cached credentials appear valid.</param>
    /// <param name="onUrl">Callback for the login URL.</param>
    /// <param name="onPrompt">Callback for interactive prompts.</param>
    /// <param name="useLocalListener">Whether to use a local listener.</param>
    /// <param name="timeout">Optional timeout.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task EnsureChatGptLoginAsync(bool forceLogin = false, Action<string>? onUrl = null, Func<string, Task<string>>? onPrompt = null,
        bool useLocalListener = true, TimeSpan? timeout = null, CancellationToken cancellationToken = default) {
        if (!forceLogin) {
            try {
                _ = await GetAccountAsync(cancellationToken).ConfigureAwait(false);
                return;
            } catch (OperationCanceledException) {
                throw;
            } catch (OpenAIAuthenticationRequiredException) {
                // Not logged in (or token expired). Fall back to login flow.
            }
        }

        await LoginChatGptAndWaitAsync(onUrl, onPrompt, useLocalListener, timeout, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Logs in using an API key (app-server transport only).
    /// </summary>
    /// <param name="apiKey">API key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task LoginApiKeyAsync(string apiKey, CancellationToken cancellationToken = default) {
        return _transport.LoginApiKeyAsync(apiKey, cancellationToken);
    }

    /// <summary>
    /// Starts a new thread and sets it as the current thread.
    /// </summary>
    /// <param name="model">Optional model override.</param>
    /// <param name="currentDirectory">Optional working directory.</param>
    /// <param name="approvalPolicy">Optional approval policy.</param>
    /// <param name="sandbox">Optional sandbox policy name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<ThreadInfo> StartNewThreadAsync(string? model = null, string? currentDirectory = null, string? approvalPolicy = null,
        string? sandbox = null, CancellationToken cancellationToken = default) {
        var thread = await _transport.StartThreadAsync(model ?? _defaultModel, currentDirectory, approvalPolicy, sandbox, cancellationToken)
            .ConfigureAwait(false);
        _currentThreadId = thread.Id;
        return thread;
    }

    /// <summary>
    /// Resumes an existing thread and sets it as the current thread.
    /// </summary>
    /// <param name="threadId">Thread id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<ThreadInfo> UseThreadAsync(string threadId, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(threadId, nameof(threadId));
        _currentThreadId = threadId;
        return _transport.ResumeThreadAsync(threadId, cancellationToken);
    }

    /// <summary>
    /// Sends a text-only chat request.
    /// </summary>
    /// <param name="text">Prompt text.</param>
    /// <param name="model">Optional model override.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<TurnInfo> ChatAsync(string text, string? model = null, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(text, nameof(text));
        var input = Chat.ChatInput.FromText(text);
        return await ChatAsync(input, new Chat.ChatOptions { Model = model }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a chat request with a local image.
    /// </summary>
    /// <param name="text">Prompt text.</param>
    /// <param name="imagePath">Local image path.</param>
    /// <param name="options">Chat options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<TurnInfo> ChatWithImagePathAsync(string text, string imagePath, Chat.ChatOptions? options = null,
        CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(text, nameof(text));
        Guard.NotNullOrWhiteSpace(imagePath, nameof(imagePath));
        var input = Chat.ChatInput.FromTextWithImagePath(text, imagePath);
        return await ChatAsync(input, options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a chat request with an image URL.
    /// </summary>
    /// <param name="text">Prompt text.</param>
    /// <param name="imageUrl">Image URL.</param>
    /// <param name="options">Chat options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<TurnInfo> ChatWithImageUrlAsync(string text, string imageUrl, Chat.ChatOptions? options = null,
        CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(text, nameof(text));
        Guard.NotNullOrWhiteSpace(imageUrl, nameof(imageUrl));
        var input = Chat.ChatInput.FromTextWithImageUrl(text, imageUrl);
        return await ChatAsync(input, options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a chat request with a structured input.
    /// </summary>
    /// <param name="input">Chat input.</param>
    /// <param name="options">Chat options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<TurnInfo> ChatAsync(Chat.ChatInput input, Chat.ChatOptions? options = null, CancellationToken cancellationToken = default) {
        Guard.NotNull(input, nameof(input));
        options ??= new Chat.ChatOptions();
        if (options.NewThread) {
            _currentThreadId = null;
        }
        EnsureFileSafety(input, options);

        var workspace = options.Workspace;
        var model = options.Model ?? _defaultModel;
        options.Model ??= model;
        await EnsureThreadAsync(model, cancellationToken).ConfigureAwait(false);
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

        return await _transport.StartTurnAsync(_currentThreadId!, input, options, cwd, approval, sandbox, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Configures defaults for workspace-based tool access.
    /// </summary>
    /// <param name="workingDirectory">Workspace path.</param>
    /// <param name="allowNetwork">Whether network access is allowed.</param>
    /// <returns>The current client instance.</returns>
    public IntelligenceXClient ConfigureWorkspace(string workingDirectory, bool allowNetwork = false) {
        _defaultWorkingDirectory = workingDirectory;
        _defaultApprovalPolicy = _defaultApprovalPolicy ?? "auto";
        _defaultSandboxPolicy = new SandboxPolicy("workspace", allowNetwork, new[] { workingDirectory });
        return this;
    }

    /// <summary>
    /// Configures default model and execution settings.
    /// </summary>
    /// <param name="model">Default model override.</param>
    /// <param name="workingDirectory">Default working directory.</param>
    /// <param name="approvalPolicy">Default approval policy.</param>
    /// <param name="sandboxPolicy">Default sandbox policy.</param>
    /// <returns>The current client instance.</returns>
    public IntelligenceXClient ConfigureDefaults(string? model = null, string? workingDirectory = null, string? approvalPolicy = null,
        SandboxPolicy? sandboxPolicy = null) {
        if (!string.IsNullOrWhiteSpace(model)) {
            _defaultModel = model!;
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

    /// <summary>
    /// Subscribes to streaming text deltas.
    /// </summary>
    /// <param name="onDelta">Callback invoked for each delta.</param>
    /// <returns>A subscription token that should be disposed to unsubscribe.</returns>
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

    private void OnRpcCallStarted(object? sender, RpcCallStartedEventArgs args) => RpcCallStarted?.Invoke(this, args);
    private void OnRpcCallCompleted(object? sender, RpcCallCompletedEventArgs args) => RpcCallCompleted?.Invoke(this, args);
    private void OnLoginStarted(object? sender, LoginEventArgs args) => LoginStarted?.Invoke(this, args);
    private void OnLoginCompleted(object? sender, LoginEventArgs args) => LoginCompleted?.Invoke(this, args);
    private void OnProtocolLineReceived(object? sender, string line) => ProtocolLineReceived?.Invoke(this, line);
    private void OnStandardErrorReceived(object? sender, string line) => StandardErrorReceived?.Invoke(this, line);
    private void OnDeltaReceived(object? sender, string text) => DeltaReceived?.Invoke(this, text);

    private void EnsureFileSafety(Chat.ChatInput input, Chat.ChatOptions options) {
        var paths = input.GetImagePaths();
        if (paths.Length == 0) {
            return;
        }

        var maxImageBytes = options.MaxImageBytes ?? 0;
        var requireWorkspace = options.RequireWorkspaceForFileAccess;
        var workspace = options.Workspace ?? options.WorkingDirectory ?? _defaultWorkingDirectory;

        foreach (var path in paths) {
            PathSafety.EnsureFileExists(path);
            PathSafety.EnsureMaxFileSize(path, maxImageBytes);
            if (requireWorkspace) {
                if (string.IsNullOrWhiteSpace(workspace)) {
                    throw new InvalidOperationException("Workspace is required for file access.");
                }
                PathSafety.EnsureUnderRoot(path, workspace!);
            }
        }
    }

    /// <summary>
    /// Disposes the client and underlying transport.
    /// </summary>
    public void Dispose() {
        _transport.DeltaReceived -= OnDeltaReceived;
        _transport.RpcCallStarted -= OnRpcCallStarted;
        _transport.RpcCallCompleted -= OnRpcCallCompleted;
        _transport.LoginStarted -= OnLoginStarted;
        _transport.LoginCompleted -= OnLoginCompleted;
        _transport.ProtocolLineReceived -= OnProtocolLineReceived;
        _transport.StandardErrorReceived -= OnStandardErrorReceived;
        _transport.Dispose();
    }

#if NET5_0_OR_GREATER || NETSTANDARD2_1_OR_GREATER
    /// <summary>
    /// Disposes the client and underlying transport asynchronously.
    /// </summary>
    public ValueTask DisposeAsync() {
        _transport.DeltaReceived -= OnDeltaReceived;
        _transport.RpcCallStarted -= OnRpcCallStarted;
        _transport.RpcCallCompleted -= OnRpcCallCompleted;
        _transport.LoginStarted -= OnLoginStarted;
        _transport.LoginCompleted -= OnLoginCompleted;
        _transport.ProtocolLineReceived -= OnProtocolLineReceived;
        _transport.StandardErrorReceived -= OnStandardErrorReceived;
        _transport.Dispose();
        return ValueTask.CompletedTask;
    }
#else
    /// <summary>
    /// Disposes the client and underlying transport asynchronously.
    /// </summary>
    public Task DisposeAsync() {
        _transport.DeltaReceived -= OnDeltaReceived;
        _transport.RpcCallStarted -= OnRpcCallStarted;
        _transport.RpcCallCompleted -= OnRpcCallCompleted;
        _transport.LoginStarted -= OnLoginStarted;
        _transport.LoginCompleted -= OnLoginCompleted;
        _transport.ProtocolLineReceived -= OnProtocolLineReceived;
        _transport.StandardErrorReceived -= OnStandardErrorReceived;
        _transport.Dispose();
        return Task.CompletedTask;
    }
#endif

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
