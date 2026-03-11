using OfficeIMO.MarkdownRenderer;

namespace IntelligenceX.Chat.App;

/// <summary>
/// Centralized markdown renderer options used by the desktop chat shell.
/// </summary>
internal static class ChatMarkdownOptions {
    /// <summary>
    /// Creates strict markdown options with visual runtime support enabled for transcript rendering.
    /// </summary>
    public static MarkdownRendererOptions Create() => OfficeImoMarkdownRuntimeContract.CreateTranscriptRendererOptions();
}
