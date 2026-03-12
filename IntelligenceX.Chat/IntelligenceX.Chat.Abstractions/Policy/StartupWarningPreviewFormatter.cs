using System;
using System.Collections.Generic;
using System.Globalization;

namespace IntelligenceX.Chat.Abstractions.Policy;

/// <summary>
/// Shared preview formatting for operator-facing startup warning sections.
/// </summary>
public static class StartupWarningPreviewFormatter {
    /// <summary>
    /// Default number of warning items to show before truncating with a summary line.
    /// </summary>
    public const int DefaultMaxShown = 4;

    /// <summary>
    /// Builds a compact warning preview with a heading, count line, optional truncation marker, and footer.
    /// </summary>
    public static string[] BuildLines<TItem>(
        IReadOnlyList<TItem>? items,
        Func<TItem, string?> formatItem,
        string heading,
        string countLineFormat,
        string? footer = null,
        int maxShown = DefaultMaxShown) {
        ArgumentNullException.ThrowIfNull(formatItem);

        if (items is not { Count: > 0 }) {
            return Array.Empty<string>();
        }

        var normalizedHeading = (heading ?? string.Empty).Trim();
        var normalizedCountLineFormat = (countLineFormat ?? string.Empty).Trim();
        if (normalizedHeading.Length == 0 || normalizedCountLineFormat.Length == 0) {
            return Array.Empty<string>();
        }

        if (maxShown <= 0) {
            maxShown = DefaultMaxShown;
        }

        var shownCount = Math.Min(items.Count, maxShown);
        var lines = new List<string>(shownCount + 6) {
            normalizedHeading,
            string.Empty,
            string.Format(CultureInfo.InvariantCulture, normalizedCountLineFormat, items.Count)
        };

        for (var i = 0; i < shownCount; i++) {
            var content = (formatItem(items[i]) ?? string.Empty).Trim();
            if (content.Length == 0) {
                continue;
            }

            lines.Add("- " + content);
        }

        if (items.Count > shownCount) {
            lines.Add(string.Format(CultureInfo.InvariantCulture, "- +{0} more", items.Count - shownCount));
        }

        var normalizedFooter = (footer ?? string.Empty).Trim();
        if (normalizedFooter.Length > 0) {
            lines.Add(string.Empty);
            lines.Add(normalizedFooter);
        }

        return lines.ToArray();
    }
}
