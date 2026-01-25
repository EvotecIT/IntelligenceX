using IntelligenceX.Json;

namespace IntelligenceX.Copilot;

public sealed class CopilotStatus {
    public CopilotStatus(string version, int protocolVersion, JsonObject raw, JsonObject? additional) {
        Version = version;
        ProtocolVersion = protocolVersion;
        Raw = raw;
        Additional = additional;
    }

    public string Version { get; }
    public int ProtocolVersion { get; }
    public JsonObject Raw { get; }
    public JsonObject? Additional { get; }

    public static CopilotStatus FromJson(JsonObject obj) {
        var version = obj.GetString("version") ?? string.Empty;
        var protocol = (int)(obj.GetInt64("protocolVersion") ?? 0);
        var additional = obj.ExtractAdditional("version", "protocolVersion");
        return new CopilotStatus(version, protocol, obj, additional);
    }
}
