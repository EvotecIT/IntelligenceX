using System;
using System.Collections.Generic;
using System.Globalization;

namespace IntelligenceX.Chat.App.Markdown;

/// <summary>
/// Builds transcript markdown documents from timestamped role messages.
/// </summary>
internal static class TranscriptMarkdownDocumentBuilder {
    public static string BuildPreparedTranscript(
        IEnumerable<(string Role, string Text, DateTime Time, string? Model)> messages,
        string timestampFormat) {
        return Build(messages, timestampFormat, static (role, messageText) => TranscriptMarkdownPreparation.PrepareMessageBodyForDisplay(role, messageText));
    }

    public static string BuildRawTranscript(
        IEnumerable<(string Role, string Text, DateTime Time, string? Model)> messages,
        string timestampFormat) {
        return Build(messages, timestampFormat, static (_, messageText) => messageText ?? string.Empty);
    }

    private static string Build(
        IEnumerable<(string Role, string Text, DateTime Time, string? Model)> messages,
        string timestampFormat,
        Func<string, string?, string> bodySelector) {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(bodySelector);

        var format = string.IsNullOrWhiteSpace(timestampFormat) ? "HH:mm:ss" : timestampFormat.Trim();
        var markdown = new MarkdownComposer();

        foreach (var message in messages) {
            var body = bodySelector(message.Role, message.Text);
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
