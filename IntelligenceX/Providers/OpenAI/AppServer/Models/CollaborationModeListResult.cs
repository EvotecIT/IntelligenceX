using System.Collections.Generic;
using IntelligenceX.Json;
using IntelligenceX.Utils;

namespace IntelligenceX.OpenAI.AppServer.Models;

/// <summary>
/// Represents collaboration modes returned by the app-server.
/// </summary>
/// <example>
/// <code>
/// var modes = await client.ListCollaborationModesAsync();
/// foreach (var mode in modes.Modes) {
///     Console.WriteLine(mode.Name);
/// }
/// </code>
/// </example>
public sealed class CollaborationModeListResult {
    public CollaborationModeListResult(IReadOnlyList<CollaborationModeMask> modes, JsonObject raw, JsonObject? additional) {
        Modes = modes;
        Raw = raw;
        Additional = additional;
    }

    /// <summary>Collaboration modes returned by the service.</summary>
    public IReadOnlyList<CollaborationModeMask> Modes { get; }
    /// <summary>Raw JSON payload from the service.</summary>
    public JsonObject Raw { get; }
    /// <summary>Additional unmapped fields from the payload.</summary>
    public JsonObject? Additional { get; }

    /// <summary>Parses collaboration modes from JSON.</summary>
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

/// <summary>
/// Represents a collaboration mode mask.
/// </summary>
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

    /// <summary>Mode name.</summary>
    public string Name { get; }
    /// <summary>Mode identifier (if provided).</summary>
    public string? Mode { get; }
    /// <summary>Model override (if provided).</summary>
    public string? Model { get; }
    /// <summary>Optional reasoning effort override.</summary>
    public OptionalValue<string?> ReasoningEffort { get; }
    /// <summary>Optional developer instructions override.</summary>
    public OptionalValue<string?> DeveloperInstructions { get; }
    /// <summary>Raw JSON payload from the service.</summary>
    public JsonObject Raw { get; }
    /// <summary>Additional unmapped fields from the payload.</summary>
    public JsonObject? Additional { get; }

    /// <summary>Parses a mode mask from JSON.</summary>
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
