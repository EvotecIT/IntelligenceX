using IntelligenceX.OpenAI.AppServer;

namespace IntelligenceX.OpenAI.Chat;

public sealed class ChatOptions {
    public string? Model { get; set; }
    public string? Instructions { get; set; }
    public ReasoningEffort? ReasoningEffort { get; set; }
    public ReasoningSummary? ReasoningSummary { get; set; }
    public TextVerbosity? TextVerbosity { get; set; }
    public double? Temperature { get; set; }
    public string? WorkingDirectory { get; set; }
    public string? Workspace { get; set; }
    public bool AllowNetwork { get; set; }
    public string? ApprovalPolicy { get; set; }
    public SandboxPolicy? SandboxPolicy { get; set; }
    public bool NewThread { get; set; }
    public long? MaxImageBytes { get; set; }
    public bool RequireWorkspaceForFileAccess { get; set; }
}
