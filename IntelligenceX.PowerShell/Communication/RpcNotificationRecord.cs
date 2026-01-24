using IntelligenceX.Json;

namespace IntelligenceX.PowerShell;

public sealed class RpcNotificationRecord {
    public RpcNotificationRecord(string method, JsonValue? @params) {
        Method = method;
        Params = @params;
    }

    public string Method { get; }
    public JsonValue? Params { get; }
}
