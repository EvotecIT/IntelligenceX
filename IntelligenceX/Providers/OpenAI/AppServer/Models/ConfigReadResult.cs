using System.Collections.Generic;
using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.AppServer.Models;

/// <summary>
/// Represents the configuration read response.
/// </summary>
public sealed class ConfigReadResult {
    /// <summary>
    /// Initializes a new configuration read result.
    /// </summary>
    public ConfigReadResult(JsonObject config, IReadOnlyDictionary<string, ConfigLayerMetadata> origins,
        IReadOnlyList<ConfigLayer> layers, JsonObject raw, JsonObject? additional) {
        Config = config;
        Origins = origins;
        Layers = layers;
        Raw = raw;
        Additional = additional;
    }

    /// <summary>
    /// Gets the merged configuration.
    /// </summary>
    public JsonObject Config { get; }
    /// <summary>
    /// Gets metadata about configuration origins.
    /// </summary>
    public IReadOnlyDictionary<string, ConfigLayerMetadata> Origins { get; }
    /// <summary>
    /// Gets the configuration layers.
    /// </summary>
    public IReadOnlyList<ConfigLayer> Layers { get; }
    /// <summary>
    /// Gets the raw JSON object.
    /// </summary>
    public JsonObject Raw { get; }
    /// <summary>
    /// Gets unrecognized fields from the payload.
    /// </summary>
    public JsonObject? Additional { get; }

    /// <summary>
    /// Parses a configuration read result from JSON.
    /// </summary>
    /// <param name="obj">Source JSON object.</param>
    /// <returns>The parsed result.</returns>
    public static ConfigReadResult FromJson(JsonObject obj) {
        var config = obj.GetObject("config") ?? new JsonObject();
        var origins = new Dictionary<string, ConfigLayerMetadata>();
        var originsObj = obj.GetObject("origins");
        if (originsObj is not null) {
            foreach (var entry in originsObj) {
                var metaObj = entry.Value?.AsObject();
                if (metaObj is null) {
                    continue;
                }
                origins[entry.Key] = ConfigLayerMetadata.FromJson(metaObj);
            }
        }

        var layers = new List<ConfigLayer>();
        var layersArray = obj.GetArray("layers");
        if (layersArray is not null) {
            foreach (var item in layersArray) {
                var layerObj = item.AsObject();
                if (layerObj is null) {
                    continue;
                }
                layers.Add(ConfigLayer.FromJson(layerObj));
            }
        }

        var additional = obj.ExtractAdditional("config", "origins", "layers");
        return new ConfigReadResult(config, origins, layers, obj, additional);
    }
}

/// <summary>
/// Metadata about a configuration layer.
/// </summary>
public sealed class ConfigLayerMetadata {
    /// <summary>
    /// Initializes a new layer metadata instance.
    /// </summary>
    public ConfigLayerMetadata(ConfigLayerSourceInfo source, string? version, JsonObject raw, JsonObject? additional) {
        Source = source;
        Version = version;
        Raw = raw;
        Additional = additional;
    }

    /// <summary>
    /// Gets the source information.
    /// </summary>
    public ConfigLayerSourceInfo Source { get; }
    /// <summary>
    /// Gets the configuration version.
    /// </summary>
    public string? Version { get; }
    /// <summary>
    /// Gets the raw JSON object.
    /// </summary>
    public JsonObject Raw { get; }
    /// <summary>
    /// Gets unrecognized fields from the payload.
    /// </summary>
    public JsonObject? Additional { get; }

    /// <summary>
    /// Parses layer metadata from JSON.
    /// </summary>
    /// <param name="obj">Source JSON object.</param>
    /// <returns>The parsed metadata.</returns>
    public static ConfigLayerMetadata FromJson(JsonObject obj) {
        var source = ConfigLayerSourceInfo.FromJson(obj.TryGetValue("name", out var value) ? value : null);
        var version = obj.GetString("version");
        var additional = obj.ExtractAdditional("name", "version");
        return new ConfigLayerMetadata(source, version, obj, additional);
    }
}

/// <summary>
/// Represents a single configuration layer.
/// </summary>
public sealed class ConfigLayer {
    /// <summary>
    /// Initializes a new configuration layer.
    /// </summary>
    public ConfigLayer(ConfigLayerSourceInfo source, string? version, JsonValue? config, string? disabledReason,
        JsonObject raw, JsonObject? additional) {
        Source = source;
        Version = version;
        Config = config;
        DisabledReason = disabledReason;
        Raw = raw;
        Additional = additional;
    }

    /// <summary>
    /// Gets the source information.
    /// </summary>
    public ConfigLayerSourceInfo Source { get; }
    /// <summary>
    /// Gets the configuration version.
    /// </summary>
    public string? Version { get; }
    /// <summary>
    /// Gets the configuration payload.
    /// </summary>
    public JsonValue? Config { get; }
    /// <summary>
    /// Gets the disabled reason when the layer is not active.
    /// </summary>
    public string? DisabledReason { get; }
    /// <summary>
    /// Gets the raw JSON object.
    /// </summary>
    public JsonObject Raw { get; }
    /// <summary>
    /// Gets unrecognized fields from the payload.
    /// </summary>
    public JsonObject? Additional { get; }

    /// <summary>
    /// Parses a configuration layer from JSON.
    /// </summary>
    /// <param name="obj">Source JSON object.</param>
    /// <returns>The parsed layer.</returns>
    public static ConfigLayer FromJson(JsonObject obj) {
        var source = ConfigLayerSourceInfo.FromJson(obj.TryGetValue("name", out var value) ? value : null);
        var version = obj.GetString("version");
        obj.TryGetValue("config", out var config);
        var disabledReason = obj.GetString("disabledReason") ?? obj.GetString("disabled_reason");
        var additional = obj.ExtractAdditional("name", "version", "config", "disabledReason", "disabled_reason");
        return new ConfigLayer(source, version, config, disabledReason, obj, additional);
    }
}

/// <summary>
/// Represents the source descriptor for a configuration layer.
/// </summary>
public sealed class ConfigLayerSourceInfo {
    /// <summary>
    /// Initializes a new source info instance.
    /// </summary>
    public ConfigLayerSourceInfo(string? type, JsonValue? raw, JsonObject? additional) {
        Type = type;
        Raw = raw;
        Additional = additional;
    }

    /// <summary>
    /// Gets the source type.
    /// </summary>
    public string? Type { get; }
    /// <summary>
    /// Gets the raw source payload.
    /// </summary>
    public JsonValue? Raw { get; }
    /// <summary>
    /// Gets unrecognized fields from the payload.
    /// </summary>
    public JsonObject? Additional { get; }

    /// <summary>
    /// Parses a source info value from JSON.
    /// </summary>
    /// <param name="value">Source JSON value.</param>
    /// <returns>The parsed source info.</returns>
    public static ConfigLayerSourceInfo FromJson(JsonValue? value) {
        if (value is null) {
            return new ConfigLayerSourceInfo(null, null, null);
        }
        var obj = value.AsObject();
        if (obj is not null) {
            var type = obj.GetString("type");
            var additional = obj.ExtractAdditional("type");
            return new ConfigLayerSourceInfo(type, value, additional);
        }
        var typeText = value.AsString();
        return new ConfigLayerSourceInfo(typeText, value, null);
    }
}
