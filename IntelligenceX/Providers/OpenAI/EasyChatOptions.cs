using IntelligenceX.OpenAI.Chat;

namespace IntelligenceX.OpenAI;

/// <summary>
/// Per-request overrides for <see cref="EasySession"/> chat calls.
/// </summary>
public sealed class EasyChatOptions {
    /// <summary>
    /// Model override for the request.
    /// </summary>
    public string? Model { get; set; }
    /// <summary>
    /// System instructions override.
    /// </summary>
    public string? Instructions { get; set; }
    public ReasoningEffort? ReasoningEffort { get; set; }
    public ReasoningSummary? ReasoningSummary { get; set; }
    public TextVerbosity? TextVerbosity { get; set; }
    public double? Temperature { get; set; }
    public string? Workspace { get; set; }
    public bool AllowNetwork { get; set; }
    /// <summary>
    /// Whether to force a new thread for the request.
    /// </summary>
    public bool NewThread { get; set; }
    public long? MaxImageBytes { get; set; }
    public bool? RequireWorkspaceForFileAccess { get; set; }
}
