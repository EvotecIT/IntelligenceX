using IntelligenceX.Json;

namespace IntelligenceX.Copilot;

/// <summary>
/// Represents Copilot CLI status information.
/// </summary>
public sealed class CopilotStatus {
    /// <summary>
    /// Initializes a new Copilot status instance.
    /// </summary>
    public CopilotStatus(string version, int protocolVersion, JsonObject raw, JsonObject? additional) {
        Version = version;
        ProtocolVersion = protocolVersion;
        Raw = raw;
        Additional = additional;
    }

    /// <summary>
    /// Gets the CLI version.
    /// </summary>
    public string Version { get; }
    /// <summary>
    /// Gets the protocol version.
    /// </summary>
    public int ProtocolVersion { get; }
    /// <summary>
    /// Gets the raw JSON object.
    /// </summary>
    public JsonObject Raw { get; }
    /// <summary>
    /// Gets unrecognized fields from the payload.
    /// </summary>
    public JsonObject? Additional { get; }

    /// <summary>
    /// Parses status information from JSON.
    /// </summary>
    /// <param name="obj">Source JSON object.</param>
    /// <returns>The parsed status.</returns>
    public static CopilotStatus FromJson(JsonObject obj) {
        var version = obj.GetString("version") ?? string.Empty;
        var protocol = (int)(obj.GetInt64("protocolVersion") ?? 0);
        var additional = obj.ExtractAdditional("version", "protocolVersion");
        return new CopilotStatus(version, protocol, obj, additional);
    }
}
