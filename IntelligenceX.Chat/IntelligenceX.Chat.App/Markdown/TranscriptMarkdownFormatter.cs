using System;
using System.Collections.Generic;
using System.Globalization;

namespace IntelligenceX.Chat.App.Markdown;

/// <summary>
/// Formats transcript entries into markdown export text.
/// </summary>
internal static class TranscriptMarkdownFormatter {
    /// <summary>
    /// Builds transcript markdown from timestamped role messages.
    /// </summary>
    /// <param name="messages">Role/text/time message stream.</param>
    /// <param name="timestampFormat">Timestamp format.</param>
    /// <returns>Markdown transcript.</returns>
    public static string Format(IEnumerable<(string Role, string Text, DateTime Time)> messages, string timestampFormat) {
        ArgumentNullException.ThrowIfNull(messages);
        var format = string.IsNullOrWhiteSpace(timestampFormat) ? "HH:mm:ss" : timestampFormat;
        var markdown = new MarkdownComposer();

        foreach (var message in messages) {
            if (string.IsNullOrWhiteSpace(message.Text)) {
                continue;
            }

            var time = message.Time.ToString(format, CultureInfo.InvariantCulture);
            markdown
                .Heading($"{message.Role} ({time})", 3)
                .Raw(message.Text)
                .BlankLine();
        }

        return markdown.Build();
    }
}
