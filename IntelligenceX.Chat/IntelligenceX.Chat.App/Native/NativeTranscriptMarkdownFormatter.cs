using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace IntelligenceX.Chat.App.Native;

/// <summary>
/// Formats native transcript items into portable Markdown for local export.
/// </summary>
internal static class NativeTranscriptMarkdownFormatter {
    public static string Format(IEnumerable<NativeChatTranscriptItem> items) {
        if (items == null) throw new ArgumentNullException(nameof(items));

        var builder = new StringBuilder();
        foreach (var item in items) {
            if (item == null || string.IsNullOrWhiteSpace(item.Text)) {
                continue;
            }

            if (builder.Length > 0) {
                builder.AppendLine();
                builder.AppendLine();
            }

            builder.Append("### ");
            builder.Append(FormatRole(item.Role));
            builder.Append(" - ");
            builder.Append(item.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture));
            builder.AppendLine();
            builder.AppendLine();
            builder.AppendLine(item.Text.Trim());
        }

        return builder.ToString();
    }

    private static string FormatRole(string role) {
        var normalized = string.IsNullOrWhiteSpace(role) ? "system" : role.Trim();
        return normalized.Length == 1
            ? normalized.ToUpperInvariant()
            : char.ToUpperInvariant(normalized[0]) + normalized[1..];
    }
}
