using System;
using IntelligenceX.Json;

namespace IntelligenceX.Copilot;

/// <summary>
/// Represents an event emitted by a Copilot session.
/// </summary>
public sealed class CopilotSessionEvent {
    /// <summary>
    /// Initializes a new Copilot session event.
    /// </summary>
    public CopilotSessionEvent(string type, JsonObject? data, JsonObject raw, JsonObject? additional) {
        Type = type;
        Data = data;
        Raw = raw;
        Additional = additional;
    }

    /// <summary>
    /// Gets the event type.
    /// </summary>
    public string Type { get; }
    /// <summary>
    /// Gets the event data payload.
    /// </summary>
    public JsonObject? Data { get; }
    /// <summary>
    /// Gets the raw JSON object.
    /// </summary>
    public JsonObject Raw { get; }
    /// <summary>
    /// Gets unrecognized fields from the payload.
    /// </summary>
    public JsonObject? Additional { get; }
    /// <summary>
    /// Gets the full content for message events.
    /// </summary>
    public string? Content { get; private set; }
    /// <summary>
    /// Gets the delta content for streaming events.
    /// </summary>
    public string? DeltaContent { get; private set; }
    /// <summary>
    /// Gets the message id when present.
    /// </summary>
    public string? MessageId { get; private set; }
    /// <summary>
    /// Gets the error message when present.
    /// </summary>
    public string? ErrorMessage { get; private set; }
    /// <summary>
    /// Gets the error stack when present.
    /// </summary>
    public string? ErrorStack { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the session is idle.
    /// </summary>
    public bool IsIdle => string.Equals(Type, "session.idle", StringComparison.Ordinal);

    /// <summary>
    /// Parses a session event from JSON.
    /// </summary>
    /// <param name="obj">Source JSON object.</param>
    /// <returns>The parsed event.</returns>
    public static CopilotSessionEvent FromJson(JsonObject obj) {
        var type = obj.GetString("type") ?? "unknown";
        var data = obj.GetObject("data");
        var additional = obj.ExtractAdditional("type", "data");
        var evt = new CopilotSessionEvent(type, data, obj, additional);

        if (data is not null) {
            if (string.Equals(type, "assistant.message", StringComparison.Ordinal)) {
                return new CopilotSessionEvent(type, data, obj, additional) {
                    Content = data.GetString("content"),
                    MessageId = data.GetString("messageId")
                };
            }
            if (string.Equals(type, "assistant.message_delta", StringComparison.Ordinal)) {
                return new CopilotSessionEvent(type, data, obj, additional) {
                    DeltaContent = data.GetString("deltaContent"),
                    MessageId = data.GetString("messageId")
                };
            }
            if (string.Equals(type, "session.error", StringComparison.Ordinal)) {
                return new CopilotSessionEvent(type, data, obj, additional) {
                    ErrorMessage = data.GetString("message"),
                    ErrorStack = data.GetString("stack")
                };
            }
        }

        return evt;
    }
}
