using OfficeIMO.MarkdownRenderer;

namespace IntelligenceX.Chat.App;

/// <summary>
/// Centralized markdown renderer options used by the desktop chat shell.
/// </summary>
internal static class ChatMarkdownOptions {
    /// <summary>
    /// Creates strict markdown options with Mermaid enabled for transcript visualization.
    /// </summary>
    public static MarkdownRendererOptions Create() {
        // Preset factory returns a fresh options object per call; this mutation is call-local.
        var options = MarkdownRendererPresets.CreateChatStrictMinimal();
        options.Mermaid.Enabled = true;
        return options;
    }
}
