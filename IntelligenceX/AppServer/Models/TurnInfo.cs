using IntelligenceX.Json;

namespace IntelligenceX.AppServer.Models;

public sealed class TurnInfo {
    public TurnInfo(string id, string? status) {
        Id = id;
        Status = status;
    }

    public string Id { get; }
    public string? Status { get; }

    public static TurnInfo FromJson(JsonObject turnObj) {
        var id = turnObj.GetString("id") ?? string.Empty;
        var status = turnObj.GetString("status");
        return new TurnInfo(id, status);
    }
}
