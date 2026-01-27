using System;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.OpenAI.AppServer;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.Telemetry;
using IntelligenceX.Utils;

namespace IntelligenceX.OpenAI.Transport;

internal interface IOpenAITransport : IDisposable {
    OpenAITransportKind Kind { get; }
    AppServerClient? RawAppServerClient { get; }

    event EventHandler<string>? DeltaReceived;
    event EventHandler<LoginEventArgs>? LoginStarted;
    event EventHandler<LoginEventArgs>? LoginCompleted;
    event EventHandler<string>? ProtocolLineReceived;
    event EventHandler<string>? StandardErrorReceived;
    event EventHandler<RpcCallStartedEventArgs>? RpcCallStarted;
    event EventHandler<RpcCallCompletedEventArgs>? RpcCallCompleted;

    Task InitializeAsync(ClientInfo clientInfo, CancellationToken cancellationToken);
    Task<HealthCheckResult> HealthCheckAsync(string? method, TimeSpan? timeout, CancellationToken cancellationToken);
    Task<AccountInfo> GetAccountAsync(CancellationToken cancellationToken);
    Task LogoutAsync(CancellationToken cancellationToken);
    Task<ModelListResult> ListModelsAsync(CancellationToken cancellationToken);

    Task<ChatGptLoginStart> LoginChatGptAsync(Action<string>? onUrl, Func<string, Task<string>>? onPrompt,
        bool useLocalListener, TimeSpan timeout, CancellationToken cancellationToken);

    Task LoginApiKeyAsync(string apiKey, CancellationToken cancellationToken);

    Task<ThreadInfo> StartThreadAsync(string model, string? currentDirectory, string? approvalPolicy,
        string? sandbox, CancellationToken cancellationToken);

    Task<ThreadInfo> ResumeThreadAsync(string threadId, CancellationToken cancellationToken);

    Task<TurnInfo> StartTurnAsync(string threadId, ChatInput input, ChatOptions? options, string? currentDirectory,
        string? approvalPolicy, SandboxPolicy? sandboxPolicy, CancellationToken cancellationToken);
}
