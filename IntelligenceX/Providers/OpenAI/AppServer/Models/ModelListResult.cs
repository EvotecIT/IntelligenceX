using System.Collections.Generic;
using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.AppServer.Models;

/// <summary>
/// Represents the result of a model list request.
/// </summary>
public sealed class ModelListResult {
    /// <summary>
    /// Initializes a new model list result.
    /// </summary>
    public ModelListResult(IReadOnlyList<ModelInfo> models, string? nextCursor, JsonObject raw, JsonObject? additional) {
        Models = models;
        NextCursor = nextCursor;
        Raw = raw;
        Additional = additional;
    }

    /// <summary>
    /// Gets the list of models.
    /// </summary>
    public IReadOnlyList<ModelInfo> Models { get; }
    /// <summary>
    /// Gets the pagination cursor for the next page.
    /// </summary>
    public string? NextCursor { get; }
    /// <summary>
    /// Gets the raw JSON object.
    /// </summary>
    public JsonObject Raw { get; }
    /// <summary>
    /// Gets unrecognized fields from the payload.
    /// </summary>
    public JsonObject? Additional { get; }

    /// <summary>
    /// Parses a model list result from JSON.
    /// </summary>
    /// <param name="obj">Source JSON object.</param>
    /// <returns>The parsed model list result.</returns>
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
/// Represents a single model entry.
/// </summary>
public sealed class ModelInfo {
    /// <summary>
    /// Initializes a new model info entry.
    /// </summary>
    public ModelInfo(string id, string model, string displayName, string description,
        IReadOnlyList<ReasoningEffortOption> supportedReasoningEfforts, string? defaultReasoningEffort, bool isDefault,
        JsonObject raw, JsonObject? additional, string? ownedBy = null, string? publisher = null, string? architecture = null,
        string? quantization = null, string? compatibilityType = null, string? runtimeState = null, string? modelType = null,
        long? maxContextLength = null, long? loadedContextLength = null, IReadOnlyList<string>? capabilities = null) {
        Id = id;
        Model = model;
        DisplayName = displayName;
        Description = description;
        SupportedReasoningEfforts = supportedReasoningEfforts;
        DefaultReasoningEffort = defaultReasoningEffort;
        IsDefault = isDefault;
        Raw = raw;
        Additional = additional;
        OwnedBy = ownedBy;
        Publisher = publisher;
        Architecture = architecture;
        Quantization = quantization;
        CompatibilityType = compatibilityType;
        RuntimeState = runtimeState;
        ModelType = modelType;
        MaxContextLength = maxContextLength;
        LoadedContextLength = loadedContextLength;
        Capabilities = capabilities ?? System.Array.Empty<string>();
    }

    /// <summary>
    /// Gets the model id.
    /// </summary>
    public string Id { get; }
    /// <summary>
    /// Gets the model name.
    /// </summary>
    public string Model { get; }
    /// <summary>
    /// Gets the display name.
    /// </summary>
    public string DisplayName { get; }
    /// <summary>
    /// Gets the model description.
    /// </summary>
    public string Description { get; }
    /// <summary>
    /// Gets the supported reasoning effort options.
    /// </summary>
    public IReadOnlyList<ReasoningEffortOption> SupportedReasoningEfforts { get; }
    /// <summary>
    /// Gets the default reasoning effort value.
    /// </summary>
    public string? DefaultReasoningEffort { get; }
    /// <summary>
    /// Gets a value indicating whether this is the default model.
    /// </summary>
    public bool IsDefault { get; }
    /// <summary>
    /// Gets the provider owner identifier when exposed (for example <c>owned_by</c>).
    /// </summary>
    public string? OwnedBy { get; }
    /// <summary>
    /// Gets the model publisher when exposed by the runtime.
    /// </summary>
    public string? Publisher { get; }
    /// <summary>
    /// Gets the model architecture identifier when known.
    /// </summary>
    public string? Architecture { get; }
    /// <summary>
    /// Gets the quantization identifier when known.
    /// </summary>
    public string? Quantization { get; }
    /// <summary>
    /// Gets the compatibility format (for example gguf) when known.
    /// </summary>
    public string? CompatibilityType { get; }
    /// <summary>
    /// Gets the runtime load state (for example loaded/not-loaded) when known.
    /// </summary>
    public string? RuntimeState { get; }
    /// <summary>
    /// Gets the model type/category when known.
    /// </summary>
    public string? ModelType { get; }
    /// <summary>
    /// Gets the maximum context length when exposed by the runtime.
    /// </summary>
    public long? MaxContextLength { get; }
    /// <summary>
    /// Gets the currently loaded context length when exposed by the runtime.
    /// </summary>
    public long? LoadedContextLength { get; }
    /// <summary>
    /// Gets capability tags exposed by the runtime.
    /// </summary>
    public IReadOnlyList<string> Capabilities { get; }
    /// <summary>
    /// Gets the raw JSON object.
    /// </summary>
    public JsonObject Raw { get; }
    /// <summary>
    /// Gets unrecognized fields from the payload.
    /// </summary>
    public JsonObject? Additional { get; }

    /// <summary>
    /// Parses a model entry from JSON.
    /// </summary>
    /// <param name="obj">Source JSON object.</param>
    /// <returns>The parsed model entry.</returns>
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
        var ownedBy = GetString(obj, "ownedBy", "owned_by");
        var publisher = GetString(obj, "publisher");
        var architecture = GetString(obj, "arch", "architecture");
        var quantization = GetString(obj, "quantization");
        var compatibilityType = GetString(obj, "compatibilityType", "compatibility_type");
        var runtimeState = GetString(obj, "state");
        var modelType = GetString(obj, "type");
        var maxContextLength = obj.GetInt64("maxContextLength") ?? obj.GetInt64("max_context_length");
        var loadedContextLength = obj.GetInt64("loadedContextLength") ?? obj.GetInt64("loaded_context_length");
        var capabilities = ParseStringArray(obj.GetArray("capabilities"));
        var additional = obj.ExtractAdditional(
            "id", "model", "displayName", "display_name", "description",
            "supportedReasoningEfforts", "supported_reasoning_efforts",
            "defaultReasoningEffort", "default_reasoning_effort", "isDefault",
            "ownedBy", "owned_by", "publisher", "arch", "architecture",
            "quantization", "compatibilityType", "compatibility_type",
            "state", "type", "maxContextLength", "max_context_length",
            "loadedContextLength", "loaded_context_length", "capabilities");
        return new ModelInfo(
            id,
            model,
            displayName,
            description,
            efforts,
            defaultReasoningEffort,
            isDefault,
            obj,
            additional,
            ownedBy,
            publisher,
            architecture,
            quantization,
            compatibilityType,
            runtimeState,
            modelType,
            maxContextLength,
            loadedContextLength,
            capabilities);
    }

    private static string? GetString(JsonObject obj, string key) => obj.GetString(key);
    private static string? GetString(JsonObject obj, string primary, string fallback) => obj.GetString(primary) ?? obj.GetString(fallback);

    private static IReadOnlyList<string> ParseStringArray(JsonArray? source) {
        if (source is null || source.Count == 0) {
            return System.Array.Empty<string>();
        }

        var list = new List<string>(source.Count);
        for (var i = 0; i < source.Count; i++) {
            var value = source[i].AsString();
            if (value is null) {
                continue;
            }

            var normalized = value.Trim();
            if (normalized.Length == 0) {
                continue;
            }

            list.Add(normalized);
        }

        return list.Count == 0 ? System.Array.Empty<string>() : list;
    }
}

/// <summary>
/// Describes a supported reasoning effort option for a model.
/// </summary>
public sealed class ReasoningEffortOption {
    /// <summary>
    /// Initializes a new reasoning effort option.
    /// </summary>
    public ReasoningEffortOption(string reasoningEffort, string description, JsonObject raw, JsonObject? additional) {
        ReasoningEffort = reasoningEffort;
        Description = description;
        Raw = raw;
        Additional = additional;
    }

    /// <summary>
    /// Gets the reasoning effort identifier.
    /// </summary>
    public string ReasoningEffort { get; }
    /// <summary>
    /// Gets the description of the effort level.
    /// </summary>
    public string Description { get; }
    /// <summary>
    /// Gets the raw JSON object.
    /// </summary>
    public JsonObject Raw { get; }
    /// <summary>
    /// Gets unrecognized fields from the payload.
    /// </summary>
    public JsonObject? Additional { get; }

    /// <summary>
    /// Parses a reasoning effort option from JSON.
    /// </summary>
    /// <param name="obj">Source JSON object.</param>
    /// <returns>The parsed option.</returns>
    public static ReasoningEffortOption FromJson(JsonObject obj) {
        var effort = obj.GetString("reasoningEffort") ?? obj.GetString("reasoning_effort") ?? string.Empty;
        var description = obj.GetString("description") ?? string.Empty;
        var additional = obj.ExtractAdditional("reasoningEffort", "reasoning_effort", "description");
        return new ReasoningEffortOption(effort, description, obj, additional);
    }
}
