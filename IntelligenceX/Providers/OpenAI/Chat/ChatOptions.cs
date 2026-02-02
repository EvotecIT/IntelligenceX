using IntelligenceX.OpenAI.AppServer;

namespace IntelligenceX.OpenAI.Chat;

/// <summary>
/// Options for chat requests.
/// </summary>
public sealed class ChatOptions {
    /// <summary>
    /// Model name override.
    /// </summary>
    public string? Model { get; set; }
    /// <summary>
    /// System instructions.
    /// </summary>
    public string? Instructions { get; set; }
    /// <summary>
    /// Reasoning effort hint.
    /// </summary>
    public ReasoningEffort? ReasoningEffort { get; set; }
    /// <summary>
    /// Reasoning summary hint.
    /// </summary>
    public ReasoningSummary? ReasoningSummary { get; set; }
    /// <summary>
    /// Text verbosity hint.
    /// </summary>
    public TextVerbosity? TextVerbosity { get; set; }
    /// <summary>
    /// Sampling temperature.
    /// </summary>
    public double? Temperature { get; set; }
    /// <summary>
    /// Working directory for file operations.
    /// </summary>
    public string? WorkingDirectory { get; set; }
    /// <summary>
    /// Workspace path for tool access.
    /// </summary>
    public string? Workspace { get; set; }
    /// <summary>
    /// Whether network access is allowed.
    /// </summary>
    public bool AllowNetwork { get; set; }
    /// <summary>
    /// Approval policy string passed to the app-server.
    /// </summary>
    public string? ApprovalPolicy { get; set; }
    /// <summary>
    /// Sandbox policy for the request.
    /// </summary>
    public SandboxPolicy? SandboxPolicy { get; set; }
    /// <summary>
    /// Whether to force a new thread.
    /// </summary>
    public bool NewThread { get; set; }
    /// <summary>
    /// Maximum allowed image size in bytes.
    /// </summary>
    public long? MaxImageBytes { get; set; }
    /// <summary>
    /// Whether a workspace is required for file access.
    /// </summary>
    public bool RequireWorkspaceForFileAccess { get; set; }
}
