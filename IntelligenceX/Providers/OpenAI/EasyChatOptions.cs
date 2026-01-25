namespace IntelligenceX.OpenAI;

public sealed class EasyChatOptions {
    public string? Model { get; set; }
    public string? Workspace { get; set; }
    public bool AllowNetwork { get; set; }
    public bool NewThread { get; set; }
}
