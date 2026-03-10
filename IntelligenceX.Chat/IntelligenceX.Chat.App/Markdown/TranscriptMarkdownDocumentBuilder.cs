using System;
using System.Collections.Generic;
using System.Globalization;

namespace IntelligenceX.Chat.App.Markdown;

/// <summary>
/// Builds transcript markdown documents from timestamped role messages.
/// </summary>
internal static class TranscriptMarkdownDocumentBuilder {
    public static string Build(
        IEnumerable<(string Role, string Text, DateTime Time, string? Model)> messages,
        string timestampFormat,
        bool prepareMessageBodies) {
        ArgumentNullException.ThrowIfNull(messages);

        var format = string.IsNullOrWhiteSpace(timestampFormat) ? "HH:mm:ss" : timestampFormat.Trim();
        var markdown = new MarkdownComposer();

        foreach (var message in messages) {
            var body = prepareMessageBodies
                ? TranscriptMarkdownPreparation.PrepareMessageBody(message.Text)
                : message.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(body)) {
                continue;
            }

            var time = message.Time.ToString(format, CultureInfo.InvariantCulture);
            markdown.Heading($"{message.Role} ({time})", 3);

            var modelComment = BuildModelComment(message.Role, message.Model);
            if (!string.IsNullOrWhiteSpace(modelComment)) {
                markdown.Raw(modelComment);
            }

            markdown.Raw(body).BlankLine();
        }

        return markdown.Build();
    }

    internal static string BuildModelComment(string role, string? model) {
        var normalizedRole = (role ?? string.Empty).Trim();
        if (!normalizedRole.Equals("Assistant", StringComparison.OrdinalIgnoreCase)
            && !normalizedRole.Equals("Tools", StringComparison.OrdinalIgnoreCase)) {
            return string.Empty;
        }

        var normalizedModel = (model ?? string.Empty).Trim();
        if (normalizedModel.Length == 0) {
            return string.Empty;
        }

        var safeModel = normalizedModel
            .Replace("--", "- -", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
        return "<!-- ix:model: " + safeModel + " -->";
    }
}
