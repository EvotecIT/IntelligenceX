using System;
using System.Collections.Generic;
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
        return TranscriptMarkdownDocumentBuilder.BuildPreparedTranscript(messages, timestampFormat);
    }

    private static IEnumerable<(string Role, string Text, DateTime Time, string? Model)> ProjectLegacyMessages(IEnumerable<(string Role, string Text, DateTime Time)> messages) {
        foreach (var message in messages) {
            yield return (message.Role, message.Text, message.Time, null);
        }
    }
}
