using System.Collections.Generic;

namespace IntelligenceX.Copilot;

/// <summary>
/// Options for sending a Copilot message.
/// </summary>
public sealed class CopilotMessageOptions {
    /// <summary>Maximum accumulated response characters; exceeding the limit fails the operation.</summary>
    public int MaxResponseCharacters { get; set; } = 1_000_000;
    /// <summary>
    /// Prompt text.
    /// </summary>
    public string Prompt { get; set; } = string.Empty;
    /// <summary>
    /// Optional attachments.
    /// </summary>
    public List<CopilotAttachment>? Attachments { get; set; }
    /// <summary>
    /// Optional mode identifier.
    /// </summary>
    public string? Mode { get; set; }
}

/// <summary>
/// Attachment metadata for Copilot messages.
/// </summary>
public sealed class CopilotAttachment {
    /// <summary>
    /// Attachment type (for example, "file").
    /// </summary>
    public string Type { get; set; } = "file";
    /// <summary>
    /// Attachment path.
    /// </summary>
    public string Path { get; set; } = string.Empty;
    /// <summary>
    /// Optional display name.
    /// </summary>
    public string? DisplayName { get; set; }
}
