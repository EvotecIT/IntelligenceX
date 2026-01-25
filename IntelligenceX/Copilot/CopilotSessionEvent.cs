using System;
using IntelligenceX.Json;

namespace IntelligenceX.Copilot;

public sealed class CopilotSessionEvent {
    public CopilotSessionEvent(string type, JsonObject? data) {
        Type = type;
        Data = data;
    }

    public string Type { get; }
    public JsonObject? Data { get; }
    public string? Content { get; init; }
    public string? DeltaContent { get; init; }
    public string? MessageId { get; init; }
    public string? ErrorMessage { get; init; }
    public string? ErrorStack { get; init; }

    public bool IsIdle => string.Equals(Type, "session.idle", StringComparison.Ordinal);

    public static CopilotSessionEvent FromJson(JsonObject obj) {
        var type = obj.GetString("type") ?? "unknown";
        var data = obj.GetObject("data");
        var evt = new CopilotSessionEvent(type, data);

        if (data is not null) {
            if (string.Equals(type, "assistant.message", StringComparison.Ordinal)) {
                return new CopilotSessionEvent(type, data) {
                    Content = data.GetString("content"),
                    MessageId = data.GetString("messageId")
                };
            }
            if (string.Equals(type, "assistant.message_delta", StringComparison.Ordinal)) {
                return new CopilotSessionEvent(type, data) {
                    DeltaContent = data.GetString("deltaContent"),
                    MessageId = data.GetString("messageId")
                };
            }
            if (string.Equals(type, "session.error", StringComparison.Ordinal)) {
                return new CopilotSessionEvent(type, data) {
                    ErrorMessage = data.GetString("message"),
                    ErrorStack = data.GetString("stack")
                };
            }
        }

        return evt;
    }
}
