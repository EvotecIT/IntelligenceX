using IntelligenceX.OpenAI.Chat;

namespace IntelligenceX.OpenAI;

/// <summary>
/// Per-request overrides for <see cref="EasySession"/> chat calls.
/// </summary>
/// <example>
/// <code>
/// var options = new EasyChatOptions {
///     Model = "gpt-5.2-codex",
///     ReasoningEffort = ReasoningEffort.Medium,
///     Temperature = 0.2,
///     NewThread = true
/// };
/// var result = await session.AskAsync("Summarize the PR", options);
/// </code>
/// </example>
public sealed class EasyChatOptions {
    /// <summary>
    /// Model override for the request.
    /// </summary>
    public string? Model { get; set; }
    /// <summary>
    /// System instructions override.
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
    /// Workspace root for file access.
    /// </summary>
    public string? Workspace { get; set; }
    /// <summary>
    /// Whether network access is allowed for tools.
    /// </summary>
    public bool AllowNetwork { get; set; }
    /// <summary>
    /// Whether to force a new thread for the request.
    /// </summary>
    public bool NewThread { get; set; }
    /// <summary>
    /// Maximum image size (bytes) for local image inputs.
    /// </summary>
    public long? MaxImageBytes { get; set; }
    /// <summary>
    /// Overrides whether a workspace is required for file access.
    /// </summary>
    public bool? RequireWorkspaceForFileAccess { get; set; }
}
