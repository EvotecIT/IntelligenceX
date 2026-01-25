using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.AppServer.Models;

public sealed class ConfigEntry {
    public ConfigEntry(string key, JsonValue value) {
        Key = key;
        Value = value;
    }

    public string Key { get; }
    public JsonValue Value { get; }
}
