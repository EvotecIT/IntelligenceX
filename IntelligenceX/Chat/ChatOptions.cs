using IntelligenceX.AppServer;

namespace IntelligenceX.Chat;

public sealed class ChatOptions {
    public string? Model { get; set; }
    public string? WorkingDirectory { get; set; }
    public string? Workspace { get; set; }
    public bool AllowNetwork { get; set; }
    public string? ApprovalPolicy { get; set; }
    public SandboxPolicy? SandboxPolicy { get; set; }
    public bool NewThread { get; set; }
}
