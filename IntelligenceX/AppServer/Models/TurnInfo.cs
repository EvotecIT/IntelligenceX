namespace IntelligenceX.AppServer.Models;

public sealed class TurnInfo {
    public TurnInfo(string id, string? status) {
        Id = id;
        Status = status;
    }

    public string Id { get; }
    public string? Status { get; }
}
