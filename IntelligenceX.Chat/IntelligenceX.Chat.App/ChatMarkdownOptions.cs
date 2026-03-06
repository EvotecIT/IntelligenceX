using System.Reflection;
using OfficeIMO.MarkdownRenderer;

namespace IntelligenceX.Chat.App;

/// <summary>
/// Centralized markdown renderer options used by the desktop chat shell.
/// </summary>
internal static class ChatMarkdownOptions {
    /// <summary>
    /// Creates strict markdown options with visual runtime support enabled for transcript rendering.
    /// </summary>
    public static MarkdownRendererOptions Create() {
        // Preset factory returns a fresh options object per call; this mutation is call-local.
        var options = MarkdownRendererPresets.CreateChatStrictMinimal();
        options.Mermaid.Enabled = true;
        options.Chart.Enabled = true;
        EnableOptionalNetworkSupport(options);
        return options;
    }

    private static void EnableOptionalNetworkSupport(MarkdownRendererOptions options) {
        var networkProperty = typeof(MarkdownRendererOptions).GetProperty(
            "Network",
            BindingFlags.Instance | BindingFlags.Public);
        var networkOptions = networkProperty?.GetValue(options);
        var enabledProperty = networkOptions?.GetType().GetProperty(
            "Enabled",
            BindingFlags.Instance | BindingFlags.Public);
        if (enabledProperty?.CanWrite == true && enabledProperty.PropertyType == typeof(bool)) {
            enabledProperty.SetValue(networkOptions, true);
        }
    }
}
