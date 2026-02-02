using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.AppServer.Models;

/// <summary>
/// Represents a key/value configuration entry.
/// </summary>
public sealed class ConfigEntry {
    /// <summary>
    /// Initializes a new configuration entry.
    /// </summary>
    /// <param name="key">Configuration key.</param>
    /// <param name="value">Configuration value.</param>
    public ConfigEntry(string key, JsonValue value) {
        Key = key;
        Value = value;
    }

    /// <summary>
    /// Gets the configuration key.
    /// </summary>
    public string Key { get; }
    /// <summary>
    /// Gets the configuration value.
    /// </summary>
    public JsonValue Value { get; }
}
