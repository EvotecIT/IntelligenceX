using IntelligenceX.OpenAI.AppServer;

namespace IntelligenceX.OpenAI.Chat;

/// <summary>
/// Options for a chat request.
/// </summary>
/// <example>
/// <code>
/// var options = new ChatOptions {
///     Model = "gpt-5.2-codex",
///     Instructions = "Be concise.",
///     AllowNetwork = false
/// };
/// </code>
/// </example>
public sealed class ChatOptions {
    /// <summary>
    /// Model name for the request.
    /// </summary>
    public string? Model { get; set; }
    /// <summary>
    /// System instructions applied to the request.
    /// </summary>
    public string? Instructions { get; set; }
    /// <summary>
    /// Reasoning effort hint (if supported by the model).
    /// </summary>
    public ReasoningEffort? ReasoningEffort { get; set; }
    /// <summary>
    /// Reasoning summary verbosity hint.
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
    /// Working directory used for file operations.
    /// </summary>
    public string? WorkingDirectory { get; set; }
    /// <summary>
    /// Workspace root for file access.
    /// </summary>
    public string? Workspace { get; set; }
    /// <summary>
    /// Whether network access is allowed for tools.
    /// </summary>
    public bool AllowNetwork { get; set; }
    /// <summary>
    /// Approval policy hint used by tool execution.
    /// </summary>
    public string? ApprovalPolicy { get; set; }
    /// <summary>
    /// Sandbox policy used when launching tools.
    /// </summary>
    public SandboxPolicy? SandboxPolicy { get; set; }
    /// <summary>
    /// Whether to start a new thread for this request.
    /// </summary>
    public bool NewThread { get; set; }
    /// <summary>
    /// Maximum image size (bytes) for local image inputs.
    /// </summary>
    public long? MaxImageBytes { get; set; }
    /// <summary>
    /// Whether file access requires a workspace.
    /// </summary>
    public bool RequireWorkspaceForFileAccess { get; set; }
}
