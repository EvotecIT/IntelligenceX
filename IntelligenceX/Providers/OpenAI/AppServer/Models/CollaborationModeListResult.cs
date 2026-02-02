using System.Collections.Generic;
using IntelligenceX.Json;
using IntelligenceX.Utils;

namespace IntelligenceX.OpenAI.AppServer.Models;

/// <summary>
/// Represents a list of collaboration mode masks.
/// </summary>
public sealed class CollaborationModeListResult {
    /// <summary>
    /// Initializes a new list result.
    /// </summary>
    public CollaborationModeListResult(IReadOnlyList<CollaborationModeMask> modes, JsonObject raw, JsonObject? additional) {
        Modes = modes;
        Raw = raw;
        Additional = additional;
    }

    /// <summary>
    /// Gets the available collaboration modes.
    /// </summary>
    public IReadOnlyList<CollaborationModeMask> Modes { get; }
    /// <summary>
    /// Gets the raw JSON object.
    /// </summary>
    public JsonObject Raw { get; }
    /// <summary>
    /// Gets unrecognized fields from the payload.
    /// </summary>
    public JsonObject? Additional { get; }

    /// <summary>
    /// Parses a collaboration mode list from JSON.
    /// </summary>
    /// <param name="obj">Source JSON object.</param>
    /// <returns>The parsed result.</returns>
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
/// Describes a collaboration mode mask.
/// </summary>
public sealed class CollaborationModeMask {
    /// <summary>
    /// Initializes a new collaboration mode mask.
    /// </summary>
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

    /// <summary>
    /// Gets the mask name.
    /// </summary>
    public string Name { get; }
    /// <summary>
    /// Gets the mode identifier.
    /// </summary>
    public string? Mode { get; }
    /// <summary>
    /// Gets the model name.
    /// </summary>
    public string? Model { get; }
    /// <summary>
    /// Gets the optional reasoning effort setting.
    /// </summary>
    public OptionalValue<string?> ReasoningEffort { get; }
    /// <summary>
    /// Gets the optional developer instructions.
    /// </summary>
    public OptionalValue<string?> DeveloperInstructions { get; }
    /// <summary>
    /// Gets the raw JSON object.
    /// </summary>
    public JsonObject Raw { get; }
    /// <summary>
    /// Gets unrecognized fields from the payload.
    /// </summary>
    public JsonObject? Additional { get; }

    /// <summary>
    /// Parses a collaboration mode mask from JSON.
    /// </summary>
    /// <param name="obj">Source JSON object.</param>
    /// <returns>The parsed mask.</returns>
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
