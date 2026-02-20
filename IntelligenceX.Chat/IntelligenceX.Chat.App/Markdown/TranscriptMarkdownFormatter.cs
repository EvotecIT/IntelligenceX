using System;
using System.Collections.Generic;
using System.Globalization;
using IntelligenceX.Chat.App.Rendering;

namespace IntelligenceX.Chat.App.Markdown;

/// <summary>
/// Formats transcript entries into markdown export text.
/// </summary>
internal static class TranscriptMarkdownFormatter {
    /// <summary>
    /// Backward-compatible overload for transcript entries without model metadata.
    /// </summary>
    public static string Format(IEnumerable<(string Role, string Text, DateTime Time)> messages, string timestampFormat) {
        ArgumentNullException.ThrowIfNull(messages);
        return Format(ProjectLegacyMessages(messages), timestampFormat);
    }

    /// <summary>
    /// Builds transcript markdown from timestamped role messages.
    /// </summary>
    /// <param name="messages">Role/text/time message stream.</param>
    /// <param name="timestampFormat">Timestamp format.</param>
    /// <returns>Markdown transcript.</returns>
    public static string Format(IEnumerable<(string Role, string Text, DateTime Time, string? Model)> messages, string timestampFormat) {
        ArgumentNullException.ThrowIfNull(messages);
        var format = string.IsNullOrWhiteSpace(timestampFormat) ? "HH:mm:ss" : timestampFormat;
        var markdown = new MarkdownComposer();

        foreach (var message in messages) {
            var normalizedText = TranscriptMarkdownNormalizer.NormalizeForRendering(message.Text);
            if (string.IsNullOrWhiteSpace(normalizedText)) {
                continue;
            }

            var time = message.Time.ToString(format, CultureInfo.InvariantCulture);
            markdown
                .Heading($"{message.Role} ({time})", 3);

            var modelComment = BuildModelComment(message.Role, message.Model);
            if (!string.IsNullOrWhiteSpace(modelComment)) {
                markdown.Raw(modelComment);
            }

            markdown
                .Raw(normalizedText)
                .BlankLine();
        }

        return markdown.Build();
    }

    private static IEnumerable<(string Role, string Text, DateTime Time, string? Model)> ProjectLegacyMessages(IEnumerable<(string Role, string Text, DateTime Time)> messages) {
        foreach (var message in messages) {
            yield return (message.Role, message.Text, message.Time, null);
        }
    }

    private static string BuildModelComment(string role, string? model) {
        var normalizedRole = (role ?? string.Empty).Trim();
        if (!normalizedRole.Equals("Assistant", StringComparison.OrdinalIgnoreCase)
            && !normalizedRole.Equals("Tools", StringComparison.OrdinalIgnoreCase)) {
            return string.Empty;
        }

        var normalizedModel = (model ?? string.Empty).Trim();
        if (normalizedModel.Length == 0) {
            return string.Empty;
        }

        var safeModel = normalizedModel.Replace("--", "- -", StringComparison.Ordinal);
        return "<!-- ix:model: " + safeModel + " -->";
    }
}
