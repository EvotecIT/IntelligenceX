using System.Collections.Generic;
using IntelligenceX.Json;

namespace IntelligenceX.Copilot;

/// <summary>
/// Represents a Copilot model entry.
/// </summary>
public sealed class CopilotModelInfo {
    /// <summary>
    /// Initializes a new model info instance.
    /// </summary>
    public CopilotModelInfo(string id, string? name, JsonObject raw, JsonObject? additional) {
        Id = id;
        Name = name;
        Raw = raw;
        Additional = additional;
    }

    /// <summary>
    /// Gets the model id.
    /// </summary>
    public string Id { get; }
    /// <summary>
    /// Gets the model name.
    /// </summary>
    public string? Name { get; }
    /// <summary>
    /// Gets the metadata dictionary.
    /// </summary>
    public Dictionary<string, object?> Metadata { get; } = new();
    /// <summary>
    /// Gets the raw JSON object.
    /// </summary>
    public JsonObject Raw { get; }
    /// <summary>
    /// Gets unrecognized fields from the payload.
    /// </summary>
    public JsonObject? Additional { get; }

    /// <summary>
    /// Parses model info from JSON.
    /// </summary>
    /// <param name="obj">Source JSON object.</param>
    /// <returns>The parsed model info.</returns>
    public static CopilotModelInfo FromJson(JsonObject obj) {
        var id = obj.GetString("id") ?? string.Empty;
        var name = obj.GetString("name");
        var additional = obj.ExtractAdditional("id", "name");
        return new CopilotModelInfo(id, name, obj, additional);
    }
}
