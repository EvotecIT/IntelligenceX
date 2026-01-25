using System.Collections.Generic;
using IntelligenceX.Json;

namespace IntelligenceX.Copilot;

public sealed class CopilotModelInfo {
    public CopilotModelInfo(string id, string? name, JsonObject raw, JsonObject? additional) {
        Id = id;
        Name = name;
        Raw = raw;
        Additional = additional;
    }

    public string Id { get; }
    public string? Name { get; }
    public Dictionary<string, object?> Metadata { get; } = new();
    public JsonObject Raw { get; }
    public JsonObject? Additional { get; }

    public static CopilotModelInfo FromJson(JsonObject obj) {
        var id = obj.GetString("id") ?? string.Empty;
        var name = obj.GetString("name");
        var additional = obj.ExtractAdditional("id", "name");
        return new CopilotModelInfo(id, name, obj, additional);
    }
}
