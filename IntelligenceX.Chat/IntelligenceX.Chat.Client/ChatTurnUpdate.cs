using IntelligenceX.Chat.Abstractions.Protocol;

namespace IntelligenceX.Chat.Client;

/// <summary>
/// A request-scoped update emitted while a chat turn is running.
/// </summary>
public abstract record ChatTurnUpdate(ChatServiceMessage Message) {
    /// <summary>
    /// Creates the typed turn update represented by a protocol message.
    /// </summary>
    public static ChatTurnUpdate? FromMessage(ChatServiceMessage message) {
        ArgumentNullException.ThrowIfNull(message);

        return message switch {
            ChatStatusMessage status => new ChatTurnStatusUpdate(status),
            ChatDeltaMessage delta => new ChatTurnDeltaUpdate(delta),
            ChatAssistantProvisionalMessage provisional => new ChatTurnProvisionalUpdate(provisional),
            ChatInterimResultMessage interim => new ChatTurnInterimUpdate(interim),
            ChatMetricsMessage metrics => new ChatTurnMetricsUpdate(metrics),
            ChatResultMessage result => new ChatTurnCompletedUpdate(result),
            ErrorMessage error => new ChatTurnErrorUpdate(error),
            _ => null
        };
    }

    /// <summary>
    /// Returns whether a protocol message belongs to the shared turn update contract.
    /// </summary>
    public static bool IsTurnMessage(ChatServiceMessage message) {
        ArgumentNullException.ThrowIfNull(message);
        return message is ChatStatusMessage
            or ChatDeltaMessage
            or ChatAssistantProvisionalMessage
            or ChatInterimResultMessage
            or ChatMetricsMessage
            or ChatResultMessage
            or ErrorMessage;
    }
}

/// <summary>Progress or tool status emitted by the service.</summary>
public sealed record ChatTurnStatusUpdate(ChatStatusMessage Status) : ChatTurnUpdate(Status);

/// <summary>Incremental assistant text emitted by the service.</summary>
public sealed record ChatTurnDeltaUpdate(ChatDeltaMessage Delta) : ChatTurnUpdate(Delta);

/// <summary>Provisional assistant text emitted during reviewed turns.</summary>
public sealed record ChatTurnProvisionalUpdate(ChatAssistantProvisionalMessage Provisional) : ChatTurnUpdate(Provisional);

/// <summary>Interim assistant snapshot emitted before final synthesis.</summary>
public sealed record ChatTurnInterimUpdate(ChatInterimResultMessage Interim) : ChatTurnUpdate(Interim);

/// <summary>Metrics and autonomy telemetry for the completed turn.</summary>
public sealed record ChatTurnMetricsUpdate(ChatMetricsMessage Metrics) : ChatTurnUpdate(Metrics);

/// <summary>Final assistant response, including tools and timeline metadata.</summary>
public sealed record ChatTurnCompletedUpdate(ChatResultMessage Result) : ChatTurnUpdate(Result);

/// <summary>Terminal error returned for the turn.</summary>
public sealed record ChatTurnErrorUpdate(ErrorMessage Error) : ChatTurnUpdate(Error);

/// <summary>
/// Complete request result with the metrics event observed before the terminal response.
/// </summary>
public sealed record ChatTurnRunResult(ChatResultMessage Response, ChatMetricsMessage? Metrics);
