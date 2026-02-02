using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.AppServer.Models;

/// <summary>
/// Represents a single configuration key/value pair.
/// </summary>
/// <example>
/// <code>
/// var entry = new ConfigEntry("model", JsonValue.From("gpt-5.2-codex"));
/// </code>
/// </example>
public sealed class ConfigEntry {
    public ConfigEntry(string key, JsonValue value) {
        Key = key;
        Value = value;
    }

    /// <summary>Config key.</summary>
    public string Key { get; }
    /// <summary>Config value.</summary>
    public JsonValue Value { get; }
}
