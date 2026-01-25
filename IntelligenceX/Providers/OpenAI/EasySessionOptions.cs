using System;
using IntelligenceX.OpenAI.AppServer;

namespace IntelligenceX.OpenAI;

public enum EasyLoginMode {
    ChatGpt,
    ApiKey,
    None
}

public sealed class EasySessionOptions {
    public AppServerOptions AppServerOptions { get; } = new();
    public ClientInfo ClientInfo { get; set; } = new("IntelligenceX", "IntelligenceX Easy", "0.1.0");
    public EasyLoginMode Login { get; set; } = EasyLoginMode.ChatGpt;
    public string? ApiKey { get; set; }
    public Action<string>? OnLoginUrl { get; set; }
    public bool AutoInitialize { get; set; } = true;
    public bool AutoLogin { get; set; } = true;
    public string DefaultModel { get; set; } = "gpt-5.1-codex";
    public string? WorkingDirectory { get; set; }
    public string? Workspace { get; set; }
    public bool AllowNetwork { get; set; }
    public string? ApprovalPolicy { get; set; }
}
