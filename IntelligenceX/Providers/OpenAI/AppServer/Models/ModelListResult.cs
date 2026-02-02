using System.Collections.Generic;
using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.AppServer.Models;

/// <summary>
/// Represents a paged list of models from the app-server.
/// </summary>
/// <example>
/// <code>
/// var models = await client.ListModelsAsync();
/// foreach (var model in models.Models) {
///     Console.WriteLine(model.Model);
/// }
/// </code>
/// </example>
public sealed class ModelListResult {
    public ModelListResult(IReadOnlyList<ModelInfo> models, string? nextCursor, JsonObject raw, JsonObject? additional) {
        Models = models;
        NextCursor = nextCursor;
        Raw = raw;
        Additional = additional;
    }

    /// <summary>Models returned by the service.</summary>
    public IReadOnlyList<ModelInfo> Models { get; }
    /// <summary>Cursor for the next page, if any.</summary>
    public string? NextCursor { get; }
    /// <summary>Raw JSON payload from the service.</summary>
    public JsonObject Raw { get; }
    /// <summary>Additional unmapped fields from the payload.</summary>
    public JsonObject? Additional { get; }

    /// <summary>Parses a model list from JSON.</summary>
    public static ModelListResult FromJson(JsonObject obj) {
        var models = new List<ModelInfo>();
        var items = obj.GetArray("items") ?? obj.GetArray("data") ?? obj.GetArray("models");
        if (items is not null) {
            foreach (var item in items) {
                var modelObj = item.AsObject();
                if (modelObj is null) {
                    continue;
                }
                models.Add(ModelInfo.FromJson(modelObj));
            }
        }

        var nextCursor = GetString(obj, "nextCursor", "next_cursor");
        var additional = obj.ExtractAdditional("items", "data", "models", "nextCursor", "next_cursor");
        return new ModelListResult(models, nextCursor, obj, additional);
    }

    private static string? GetString(JsonObject obj, string primary, string fallback) {
        return obj.GetString(primary) ?? obj.GetString(fallback);
    }
}

/// <summary>
/// Describes a model returned by the app-server.
/// </summary>
/// <example>
/// <code>
/// var info = models.Models[0];
/// Console.WriteLine(info.DisplayName);
/// </code>
/// </example>
public sealed class ModelInfo {
    public ModelInfo(string id, string model, string displayName, string description,
        IReadOnlyList<ReasoningEffortOption> supportedReasoningEfforts, string? defaultReasoningEffort, bool isDefault,
        JsonObject raw, JsonObject? additional) {
        Id = id;
        Model = model;
        DisplayName = displayName;
        Description = description;
        SupportedReasoningEfforts = supportedReasoningEfforts;
        DefaultReasoningEffort = defaultReasoningEffort;
        IsDefault = isDefault;
        Raw = raw;
        Additional = additional;
    }

    /// <summary>Model identifier.</summary>
    public string Id { get; }
    /// <summary>Model name.</summary>
    public string Model { get; }
    /// <summary>Human-friendly display name.</summary>
    public string DisplayName { get; }
    /// <summary>Model description.</summary>
    public string Description { get; }
    /// <summary>Supported reasoning effort options.</summary>
    public IReadOnlyList<ReasoningEffortOption> SupportedReasoningEfforts { get; }
    /// <summary>Default reasoning effort (if provided).</summary>
    public string? DefaultReasoningEffort { get; }
    /// <summary>True when this model is the default.</summary>
    public bool IsDefault { get; }
    /// <summary>Raw JSON payload from the service.</summary>
    public JsonObject Raw { get; }
    /// <summary>Additional unmapped fields from the payload.</summary>
    public JsonObject? Additional { get; }

    /// <summary>Parses a model descriptor from JSON.</summary>
    public static ModelInfo FromJson(JsonObject obj) {
        var displayName = GetString(obj, "displayName", "display_name");
        var id = GetString(obj, "id")
                 ?? GetString(obj, "model")
                 ?? GetString(obj, "slug")
                 ?? GetString(obj, "name")
                 ?? displayName
                 ?? string.Empty;
        var model = GetString(obj, "model") ?? id;
        displayName ??= model;
        var description = GetString(obj, "description") ?? string.Empty;

        var efforts = new List<ReasoningEffortOption>();
        var effortArray = obj.GetArray("supportedReasoningEfforts") ?? obj.GetArray("supported_reasoning_efforts");
        if (effortArray is not null) {
            foreach (var item in effortArray) {
                var effortObj = item.AsObject();
                if (effortObj is null) {
                    continue;
                }
                efforts.Add(ReasoningEffortOption.FromJson(effortObj));
            }
        }

        var defaultReasoningEffort = GetString(obj, "defaultReasoningEffort", "default_reasoning_effort");
        var isDefault = obj.GetBoolean("isDefault");
        var additional = obj.ExtractAdditional(
            "id", "model", "displayName", "display_name", "description",
            "supportedReasoningEfforts", "supported_reasoning_efforts",
            "defaultReasoningEffort", "default_reasoning_effort", "isDefault");
        return new ModelInfo(id, model, displayName, description, efforts, defaultReasoningEffort, isDefault, obj, additional);
    }

    private static string? GetString(JsonObject obj, string key) => obj.GetString(key);
    private static string? GetString(JsonObject obj, string primary, string fallback) => obj.GetString(primary) ?? obj.GetString(fallback);
}

/// <summary>
/// Describes a supported reasoning effort option for a model.
/// </summary>
public sealed class ReasoningEffortOption {
    public ReasoningEffortOption(string reasoningEffort, string description, JsonObject raw, JsonObject? additional) {
        ReasoningEffort = reasoningEffort;
        Description = description;
        Raw = raw;
        Additional = additional;
    }

    /// <summary>Reasoning effort value.</summary>
    public string ReasoningEffort { get; }
    /// <summary>Human-friendly description.</summary>
    public string Description { get; }
    /// <summary>Raw JSON payload from the service.</summary>
    public JsonObject Raw { get; }
    /// <summary>Additional unmapped fields from the payload.</summary>
    public JsonObject? Additional { get; }

    /// <summary>Parses a reasoning effort option from JSON.</summary>
    public static ReasoningEffortOption FromJson(JsonObject obj) {
        var effort = obj.GetString("reasoningEffort") ?? obj.GetString("reasoning_effort") ?? string.Empty;
        var description = obj.GetString("description") ?? string.Empty;
        var additional = obj.ExtractAdditional("reasoningEffort", "reasoning_effort", "description");
        return new ReasoningEffortOption(effort, description, obj, additional);
    }
}
