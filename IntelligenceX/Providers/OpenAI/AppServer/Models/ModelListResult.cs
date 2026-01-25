using System.Collections.Generic;
using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.AppServer.Models;

public sealed class ModelListResult {
    public ModelListResult(IReadOnlyList<ModelInfo> models, string? nextCursor, JsonObject raw, JsonObject? additional) {
        Models = models;
        NextCursor = nextCursor;
        Raw = raw;
        Additional = additional;
    }

    public IReadOnlyList<ModelInfo> Models { get; }
    public string? NextCursor { get; }
    public JsonObject Raw { get; }
    public JsonObject? Additional { get; }

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

    public string Id { get; }
    public string Model { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public IReadOnlyList<ReasoningEffortOption> SupportedReasoningEfforts { get; }
    public string? DefaultReasoningEffort { get; }
    public bool IsDefault { get; }
    public JsonObject Raw { get; }
    public JsonObject? Additional { get; }

    public static ModelInfo FromJson(JsonObject obj) {
        var id = GetString(obj, "id") ?? string.Empty;
        var model = GetString(obj, "model") ?? id;
        var displayName = GetString(obj, "displayName", "display_name") ?? model;
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

public sealed class ReasoningEffortOption {
    public ReasoningEffortOption(string reasoningEffort, string description, JsonObject raw, JsonObject? additional) {
        ReasoningEffort = reasoningEffort;
        Description = description;
        Raw = raw;
        Additional = additional;
    }

    public string ReasoningEffort { get; }
    public string Description { get; }
    public JsonObject Raw { get; }
    public JsonObject? Additional { get; }

    public static ReasoningEffortOption FromJson(JsonObject obj) {
        var effort = obj.GetString("reasoningEffort") ?? obj.GetString("reasoning_effort") ?? string.Empty;
        var description = obj.GetString("description") ?? string.Empty;
        var additional = obj.ExtractAdditional("reasoningEffort", "reasoning_effort", "description");
        return new ReasoningEffortOption(effort, description, obj, additional);
    }
}
