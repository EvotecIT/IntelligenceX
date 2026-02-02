using IntelligenceX.OpenAI.AppServer;

namespace IntelligenceX.OpenAI.Chat;

/// <summary>
/// Options for a chat request.
/// </summary>
public sealed class ChatOptions {
    /// <summary>
    /// Model name for the request.
    /// </summary>
    public string? Model { get; set; }
    /// <summary>
    /// System instructions applied to the request.
    /// </summary>
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
    /// <summary>
    /// Whether to start a new thread for this request.
    /// </summary>
    public bool NewThread { get; set; }
    public long? MaxImageBytes { get; set; }
    public bool RequireWorkspaceForFileAccess { get; set; }
}
