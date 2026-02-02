using IntelligenceX.OpenAI.Chat;

namespace IntelligenceX.OpenAI;

/// <summary>
/// Configuration for an easy chat request.
/// </summary>
public sealed class EasyChatOptions {
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
    /// Workspace path for tool access.
    /// </summary>
    public string? Workspace { get; set; }
    /// <summary>
    /// Whether network access is allowed.
    /// </summary>
    public bool AllowNetwork { get; set; }
    /// <summary>
    /// Whether to force a new thread.
    /// </summary>
    public bool NewThread { get; set; }
    /// <summary>
    /// Maximum image size in bytes.
    /// </summary>
    public long? MaxImageBytes { get; set; }
    /// <summary>
    /// Require workspace for file access when set.
    /// </summary>
    public bool? RequireWorkspaceForFileAccess { get; set; }
}
