using System.Collections.Generic;
using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.AppServer.Models;

/// <summary>
/// Represents the current configuration state returned by the app-server.
/// </summary>
/// <example>
/// <code>
/// var config = await client.ReadConfigAsync();
/// Console.WriteLine(config.Config.GetString("model"));
/// </code>
/// </example>
public sealed class ConfigReadResult {
    public ConfigReadResult(JsonObject config, IReadOnlyDictionary<string, ConfigLayerMetadata> origins,
        IReadOnlyList<ConfigLayer> layers, JsonObject raw, JsonObject? additional) {
        Config = config;
        Origins = origins;
        Layers = layers;
        Raw = raw;
        Additional = additional;
    }

    /// <summary>Resolved configuration object.</summary>
    public JsonObject Config { get; }
    /// <summary>Origin metadata by layer name.</summary>
    public IReadOnlyDictionary<string, ConfigLayerMetadata> Origins { get; }
    /// <summary>Raw config layers used to build the final config.</summary>
    public IReadOnlyList<ConfigLayer> Layers { get; }
    /// <summary>Raw JSON payload from the service.</summary>
    public JsonObject Raw { get; }
    /// <summary>Additional unmapped fields from the payload.</summary>
    public JsonObject? Additional { get; }

    /// <summary>Parses a config response from JSON.</summary>
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
/// Metadata describing a configuration layer origin.
/// </summary>
public sealed class ConfigLayerMetadata {
    public ConfigLayerMetadata(ConfigLayerSourceInfo source, string? version, JsonObject raw, JsonObject? additional) {
        Source = source;
        Version = version;
        Raw = raw;
        Additional = additional;
    }

    /// <summary>Source information for the layer.</summary>
    public ConfigLayerSourceInfo Source { get; }
    /// <summary>Config layer version (if provided).</summary>
    public string? Version { get; }
    /// <summary>Raw JSON payload from the service.</summary>
    public JsonObject Raw { get; }
    /// <summary>Additional unmapped fields from the payload.</summary>
    public JsonObject? Additional { get; }

    /// <summary>Parses layer metadata from JSON.</summary>
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
    public ConfigLayer(ConfigLayerSourceInfo source, string? version, JsonValue? config, string? disabledReason,
        JsonObject raw, JsonObject? additional) {
        Source = source;
        Version = version;
        Config = config;
        DisabledReason = disabledReason;
        Raw = raw;
        Additional = additional;
    }

    /// <summary>Source information for the layer.</summary>
    public ConfigLayerSourceInfo Source { get; }
    /// <summary>Layer version (if provided).</summary>
    public string? Version { get; }
    /// <summary>Layer configuration payload.</summary>
    public JsonValue? Config { get; }
    /// <summary>Reason the layer was disabled (if any).</summary>
    public string? DisabledReason { get; }
    /// <summary>Raw JSON payload from the service.</summary>
    public JsonObject Raw { get; }
    /// <summary>Additional unmapped fields from the payload.</summary>
    public JsonObject? Additional { get; }

    /// <summary>Parses a config layer from JSON.</summary>
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
/// Describes the origin of a configuration layer.
/// </summary>
public sealed class ConfigLayerSourceInfo {
    public ConfigLayerSourceInfo(string? type, JsonValue? raw, JsonObject? additional) {
        Type = type;
        Raw = raw;
        Additional = additional;
    }

    /// <summary>Source type name (if available).</summary>
    public string? Type { get; }
    /// <summary>Raw JSON payload for the source.</summary>
    public JsonValue? Raw { get; }
    /// <summary>Additional unmapped fields from the payload.</summary>
    public JsonObject? Additional { get; }

    /// <summary>Parses a source descriptor from JSON.</summary>
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
