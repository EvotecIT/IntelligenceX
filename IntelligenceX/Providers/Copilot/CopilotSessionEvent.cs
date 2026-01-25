using System;
using IntelligenceX.Json;

namespace IntelligenceX.Copilot;

public sealed class CopilotSessionEvent {
    public CopilotSessionEvent(string type, JsonObject? data, JsonObject raw, JsonObject? additional) {
        Type = type;
        Data = data;
        Raw = raw;
        Additional = additional;
    }

    public string Type { get; }
    public JsonObject? Data { get; }
    public JsonObject Raw { get; }
    public JsonObject? Additional { get; }
    public string? Content { get; init; }
    public string? DeltaContent { get; init; }
    public string? MessageId { get; init; }
    public string? ErrorMessage { get; init; }
    public string? ErrorStack { get; init; }

    public bool IsIdle => string.Equals(Type, "session.idle", StringComparison.Ordinal);

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
