using System.Collections.Generic;
using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.AppServer.Models;

public sealed class ConfigReadResult {
    public ConfigReadResult(JsonObject config, IReadOnlyDictionary<string, ConfigLayerMetadata> origins,
        IReadOnlyList<ConfigLayer> layers, JsonObject raw, JsonObject? additional) {
        Config = config;
        Origins = origins;
        Layers = layers;
        Raw = raw;
        Additional = additional;
    }

    public JsonObject Config { get; }
    public IReadOnlyDictionary<string, ConfigLayerMetadata> Origins { get; }
    public IReadOnlyList<ConfigLayer> Layers { get; }
    public JsonObject Raw { get; }
    public JsonObject? Additional { get; }

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

public sealed class ConfigLayerMetadata {
    public ConfigLayerMetadata(ConfigLayerSourceInfo source, string? version, JsonObject raw, JsonObject? additional) {
        Source = source;
        Version = version;
        Raw = raw;
        Additional = additional;
    }

    public ConfigLayerSourceInfo Source { get; }
    public string? Version { get; }
    public JsonObject Raw { get; }
    public JsonObject? Additional { get; }

    public static ConfigLayerMetadata FromJson(JsonObject obj) {
        var source = ConfigLayerSourceInfo.FromJson(obj.TryGetValue("name", out var value) ? value : null);
        var version = obj.GetString("version");
        var additional = obj.ExtractAdditional("name", "version");
        return new ConfigLayerMetadata(source, version, obj, additional);
    }
}

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

    public ConfigLayerSourceInfo Source { get; }
    public string? Version { get; }
    public JsonValue? Config { get; }
    public string? DisabledReason { get; }
    public JsonObject Raw { get; }
    public JsonObject? Additional { get; }

    public static ConfigLayer FromJson(JsonObject obj) {
        var source = ConfigLayerSourceInfo.FromJson(obj.TryGetValue("name", out var value) ? value : null);
        var version = obj.GetString("version");
        obj.TryGetValue("config", out var config);
        var disabledReason = obj.GetString("disabledReason") ?? obj.GetString("disabled_reason");
        var additional = obj.ExtractAdditional("name", "version", "config", "disabledReason", "disabled_reason");
        return new ConfigLayer(source, version, config, disabledReason, obj, additional);
    }
}

public sealed class ConfigLayerSourceInfo {
    public ConfigLayerSourceInfo(string? type, JsonValue? raw, JsonObject? additional) {
        Type = type;
        Raw = raw;
        Additional = additional;
    }

    public string? Type { get; }
    public JsonValue? Raw { get; }
    public JsonObject? Additional { get; }

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
