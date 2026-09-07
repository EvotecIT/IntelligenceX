namespace IntelligenceX.Copilot;

/// <summary>
/// Options for Copilot chat sessions.
/// </summary>
public sealed class CopilotSessionOptions {
    /// <summary>Disables tools, config discovery, skills, memory, hooks and session persistence for inline treatment requests.</summary>
    public bool Restricted { get; set; }
    /// <summary>
    /// Model name override.
    /// </summary>
    public string? Model { get; set; }
    /// <summary>
    /// Optional session id.
    /// </summary>
    public string? SessionId { get; set; }
    /// <summary>
    /// Optional system message.
    /// </summary>
    public string? SystemMessage { get; set; }
    /// <summary>
    /// Whether to enable streaming.
    /// </summary>
    public bool? Streaming { get; set; }
}
