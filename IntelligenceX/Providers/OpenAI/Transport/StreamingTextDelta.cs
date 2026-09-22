namespace IntelligenceX.OpenAI.Transport;

/// <summary>
/// Defines whether a streamed model-text fragment carries content.
/// </summary>
internal static class StreamingTextDelta {
    /// <summary>
    /// Returns <see langword="true"/> for every non-empty fragment, including
    /// whitespace-only fragments that preserve Markdown and word boundaries.
    /// </summary>
    internal static bool HasContent(string? value) => !string.IsNullOrEmpty(value);
}
