using IntelligenceX.Chat.Abstractions.Protocol;

namespace IntelligenceX.Chat.Client;

/// <summary>
/// Request and event surface used by higher-level chat client coordinators.
/// </summary>
public interface IChatServiceClient {
    /// <summary>
    /// Raised for every protocol message received from the service.
    /// </summary>
    event Action<ChatServiceMessage>? MessageReceived;

    /// <summary>
    /// Sends a request and waits for its correlated response.
    /// </summary>
    Task<TResponse> RequestAsync<TResponse>(ChatServiceRequest request, CancellationToken cancellationToken)
        where TResponse : ChatServiceMessage;
}
