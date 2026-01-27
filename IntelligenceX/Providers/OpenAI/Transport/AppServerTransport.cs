using System;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.Rpc;
using IntelligenceX.Telemetry;
using IntelligenceX.Utils;

namespace IntelligenceX.OpenAI.Transport;

internal sealed class AppServerTransport : IOpenAITransport {
    private readonly AppServerClient _client;

    public AppServerTransport(AppServerClient client) {
        _client = client;
        _client.NotificationReceived += OnNotificationReceived;
        _client.LoginStarted += OnLoginStarted;
        _client.LoginCompleted += OnLoginCompleted;
        _client.ProtocolLineReceived += OnProtocolLineReceived;
        _client.StandardErrorReceived += OnStandardErrorReceived;
        _client.RpcCallStarted += OnRpcCallStarted;
        _client.RpcCallCompleted += OnRpcCallCompleted;
    }

    public OpenAITransportKind Kind => OpenAITransportKind.AppServer;
    public AppServerClient? RawAppServerClient => _client;

    public event EventHandler<string>? DeltaReceived;
    public event EventHandler<LoginEventArgs>? LoginStarted;
    public event EventHandler<LoginEventArgs>? LoginCompleted;
    public event EventHandler<string>? ProtocolLineReceived;
    public event EventHandler<string>? StandardErrorReceived;
    public event EventHandler<RpcCallStartedEventArgs>? RpcCallStarted;
    public event EventHandler<RpcCallCompletedEventArgs>? RpcCallCompleted;

    public Task InitializeAsync(ClientInfo clientInfo, CancellationToken cancellationToken) {
        return _client.InitializeAsync(clientInfo, cancellationToken);
    }

    public Task<HealthCheckResult> HealthCheckAsync(string? method, TimeSpan? timeout, CancellationToken cancellationToken) {
        return _client.HealthCheckAsync(method, timeout, cancellationToken);
    }

    public Task<AccountInfo> GetAccountAsync(CancellationToken cancellationToken) {
        return _client.ReadAccountAsync(cancellationToken);
    }

    public Task LogoutAsync(CancellationToken cancellationToken) {
        return _client.LogoutAsync(cancellationToken);
    }

    public Task<ModelListResult> ListModelsAsync(CancellationToken cancellationToken) {
        return _client.ListModelsAsync(cancellationToken);
    }

    public async Task<ChatGptLoginStart> LoginChatGptAsync(Action<string>? onUrl, Func<string, Task<string>>? onPrompt,
        bool useLocalListener, TimeSpan timeout, CancellationToken cancellationToken) {
        var login = await _client.StartChatGptLoginAsync(cancellationToken).ConfigureAwait(false);
        onUrl?.Invoke(login.AuthUrl);
        await _client.WaitForLoginCompletionAsync(login.LoginId, cancellationToken).ConfigureAwait(false);
        return login;
    }

    public Task LoginApiKeyAsync(string apiKey, CancellationToken cancellationToken) {
        return _client.LoginWithApiKeyAsync(apiKey, cancellationToken);
    }

    public Task<ThreadInfo> StartThreadAsync(string model, string? currentDirectory, string? approvalPolicy,
        string? sandbox, CancellationToken cancellationToken) {
        return _client.StartThreadAsync(NormalizeModel(model), currentDirectory, approvalPolicy, sandbox, cancellationToken);
    }

    public Task<ThreadInfo> ResumeThreadAsync(string threadId, CancellationToken cancellationToken) {
        return _client.ResumeThreadAsync(threadId, cancellationToken);
    }

    public Task<TurnInfo> StartTurnAsync(string threadId, ChatInput input, ChatOptions? options, string? currentDirectory,
        string? approvalPolicy, SandboxPolicy? sandboxPolicy, CancellationToken cancellationToken) {
        var model = options?.Model;
        return _client.StartTurnAsync(threadId, input.ToJson(), NormalizeModel(model), currentDirectory, approvalPolicy, sandboxPolicy, cancellationToken);
    }

    private void OnNotificationReceived(object? sender, JsonRpcNotificationEventArgs args) {
        var delta = TryExtractDelta(args.Params);
        if (!string.IsNullOrWhiteSpace(delta)) {
            DeltaReceived?.Invoke(this, delta!);
        }
    }

    private void OnLoginStarted(object? sender, LoginEventArgs args) => LoginStarted?.Invoke(this, args);
    private void OnLoginCompleted(object? sender, LoginEventArgs args) => LoginCompleted?.Invoke(this, args);
    private void OnProtocolLineReceived(object? sender, string line) => ProtocolLineReceived?.Invoke(this, line);
    private void OnStandardErrorReceived(object? sender, string line) => StandardErrorReceived?.Invoke(this, line);
    private void OnRpcCallStarted(object? sender, RpcCallStartedEventArgs args) => RpcCallStarted?.Invoke(this, args);
    private void OnRpcCallCompleted(object? sender, RpcCallCompletedEventArgs args) => RpcCallCompleted?.Invoke(this, args);

    private static string? TryExtractDelta(JsonValue? value) {
        return value?.AsObject()?.GetObject("delta")?.GetString("text");
    }

    private static string NormalizeModel(string? model) {
        if (string.IsNullOrWhiteSpace(model)) {
            return string.Empty;
        }
        var slash = model!.IndexOf('/');
        return slash > -1 && slash + 1 < model.Length ? model.Substring(slash + 1) : model;
    }

    public void Dispose() {
        _client.NotificationReceived -= OnNotificationReceived;
        _client.LoginStarted -= OnLoginStarted;
        _client.LoginCompleted -= OnLoginCompleted;
        _client.ProtocolLineReceived -= OnProtocolLineReceived;
        _client.StandardErrorReceived -= OnStandardErrorReceived;
        _client.RpcCallStarted -= OnRpcCallStarted;
        _client.RpcCallCompleted -= OnRpcCallCompleted;
        _client.Dispose();
    }
}
