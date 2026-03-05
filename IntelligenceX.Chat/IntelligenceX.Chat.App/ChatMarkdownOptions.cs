using OfficeIMO.MarkdownRenderer;
using System.Reflection;

namespace IntelligenceX.Chat.App;

/// <summary>
/// Centralized markdown renderer options used by the desktop chat shell.
/// </summary>
internal static class ChatMarkdownOptions {
    private static readonly string[] OptionalStrictNormalizationFlags = {
        "NormalizeTightArrowStrongBoundaries",
        "NormalizeTightColonSpacing"
    };

    /// <summary>
    /// Creates strict markdown options with Mermaid enabled for transcript visualization.
    /// </summary>
    public static MarkdownRendererOptions Create() {
        // Preset factory returns a fresh options object per call; this mutation is call-local.
        var options = MarkdownRendererPresets.CreateChatStrictMinimal();
        options.Mermaid.Enabled = true;
        options.Chart.Enabled = true;
        EnableOptionalStrictNormalizations(options);
        EnableOptionalNetworkSupport(options);
        return options;
    }

    private static void EnableOptionalStrictNormalizations(MarkdownRendererOptions options) {
        for (var i = 0; i < OptionalStrictNormalizationFlags.Length; i++) {
            var property = typeof(MarkdownRendererOptions).GetProperty(
                OptionalStrictNormalizationFlags[i],
                BindingFlags.Instance | BindingFlags.Public);
            if (property?.CanWrite != true || property.PropertyType != typeof(bool)) {
                continue;
            }

            property.SetValue(options, true);
        }
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
