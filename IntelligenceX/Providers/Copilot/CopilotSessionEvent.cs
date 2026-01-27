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
    public string? Content { get; private set; }
    public string? DeltaContent { get; private set; }
    public string? MessageId { get; private set; }
    public string? ErrorMessage { get; private set; }
    public string? ErrorStack { get; private set; }

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
