namespace IntelligenceX.Copilot;

public sealed class CopilotSessionOptions {
    public string? Model { get; set; }
    public string? SessionId { get; set; }
    public string? SystemMessage { get; set; }
    public bool? Streaming { get; set; }
}
