using IntelligenceX.OpenAI.AppServer;

namespace IntelligenceX.OpenAI;

public sealed class IntelligenceXClientOptions {
    public AppServerOptions AppServerOptions { get; } = new();
    public ClientInfo ClientInfo { get; set; } = new("IntelligenceX", "IntelligenceX", "0.1.0");
    public bool AutoInitialize { get; set; } = true;
    public string DefaultModel { get; set; } = "gpt-5.1-codex";
    public string? DefaultWorkingDirectory { get; set; }
    public string? DefaultApprovalPolicy { get; set; }
    public SandboxPolicy? DefaultSandboxPolicy { get; set; }
}
