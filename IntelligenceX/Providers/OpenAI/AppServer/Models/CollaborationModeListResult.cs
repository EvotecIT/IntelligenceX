using System.Collections.Generic;
using IntelligenceX.Json;
using IntelligenceX.Utils;

namespace IntelligenceX.OpenAI.AppServer.Models;

public sealed class CollaborationModeListResult {
    public CollaborationModeListResult(IReadOnlyList<CollaborationModeMask> modes, JsonObject raw, JsonObject? additional) {
        Modes = modes;
        Raw = raw;
        Additional = additional;
    }

    public IReadOnlyList<CollaborationModeMask> Modes { get; }
    public JsonObject Raw { get; }
    public JsonObject? Additional { get; }

    public static CollaborationModeListResult FromJson(JsonObject obj) {
        var modes = new List<CollaborationModeMask>();
        var data = obj.GetArray("data") ?? obj.GetArray("items");
        if (data is not null) {
            foreach (var item in data) {
                var modeObj = item.AsObject();
                if (modeObj is null) {
                    continue;
                }
                modes.Add(CollaborationModeMask.FromJson(modeObj));
            }
        }
        var additional = obj.ExtractAdditional("data", "items");
        return new CollaborationModeListResult(modes, obj, additional);
    }
}

public sealed class CollaborationModeMask {
    public CollaborationModeMask(string name, string? mode, string? model,
        OptionalValue<string?> reasoningEffort, OptionalValue<string?> developerInstructions,
        JsonObject raw, JsonObject? additional) {
        Name = name;
        Mode = mode;
        Model = model;
        ReasoningEffort = reasoningEffort;
        DeveloperInstructions = developerInstructions;
        Raw = raw;
        Additional = additional;
    }

    public string Name { get; }
    public string? Mode { get; }
    public string? Model { get; }
    public OptionalValue<string?> ReasoningEffort { get; }
    public OptionalValue<string?> DeveloperInstructions { get; }
    public JsonObject Raw { get; }
    public JsonObject? Additional { get; }

    public static CollaborationModeMask FromJson(JsonObject obj) {
        var name = obj.GetString("name") ?? string.Empty;
        var mode = obj.GetString("mode");
        var model = obj.GetString("model");

        var reasoningEffort = OptionalValue<string?>.Unspecified;
        if (TryGetValue(obj, "reasoningEffort", "reasoning_effort", out var reasoningValue)) {
            reasoningEffort = OptionalValue<string?>.FromValue(reasoningValue?.AsString());
        }

        var developerInstructions = OptionalValue<string?>.Unspecified;
        if (TryGetValue(obj, "developerInstructions", "developer_instructions", out var developerValue)) {
            developerInstructions = OptionalValue<string?>.FromValue(developerValue?.AsString());
        }

        var additional = obj.ExtractAdditional(
            "name", "mode", "model",
            "reasoningEffort", "reasoning_effort",
            "developerInstructions", "developer_instructions");
        return new CollaborationModeMask(name, mode, model, reasoningEffort, developerInstructions, obj, additional);
    }

    private static bool TryGetValue(JsonObject obj, string primary, string fallback, out JsonValue? value) {
        if (obj.TryGetValue(primary, out value)) {
            return true;
        }
        return obj.TryGetValue(fallback, out value);
    }
}
