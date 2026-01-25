namespace IntelligenceX.Copilot;

public sealed class CopilotAuthStatus {
    public bool IsAuthenticated { get; set; }
    public string? AuthType { get; set; }
    public string? Host { get; set; }
    public string? Login { get; set; }
    public string? StatusMessage { get; set; }
}
