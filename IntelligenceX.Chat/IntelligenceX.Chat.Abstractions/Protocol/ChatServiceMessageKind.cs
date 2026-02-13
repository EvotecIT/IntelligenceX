using System.Text.Json.Serialization;

namespace IntelligenceX.Chat.Abstractions.Protocol;

/// <summary>
/// Indicates whether a message is a response to a request or an out-of-band event.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ChatServiceMessageKind>))]
public enum ChatServiceMessageKind {
    /// <summary>
    /// Response to a request (correlated by <c>requestId</c>).
    /// </summary>
    Response,
    /// <summary>
    /// Out-of-band event (may still include a <c>requestId</c> for correlation).
    /// </summary>
    Event
}
