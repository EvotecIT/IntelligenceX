using IntelligenceX.Chat.App.Rendering;
using IntelligenceX.Chat.ExportArtifacts;

namespace IntelligenceX.Chat.App.Markdown;

/// <summary>
/// Applies App-specific timing and legacy repair before delegating to explicit OfficeIMO transcript-preparation contracts.
/// </summary>
internal static class TranscriptMarkdownPreparation {
    public static string NormalizePersistedTranscriptText(string? role, string text, out bool repaired) {
        ArgumentNullException.ThrowIfNull(text);

        if (string.Equals(role, "User", StringComparison.OrdinalIgnoreCase)) {
            repaired = false;
            return text;
        }

        if (!TranscriptMarkdownNormalizer.TryRepairLegacyTranscript(text, out var normalized)) {
            normalized = text;
        } else {
            repaired = true;
            return normalized;
        }

        var fullyNormalized = PrepareMessageBody(text);
        if (!string.Equals(fullyNormalized, text, StringComparison.Ordinal)) {
            repaired = true;
            return fullyNormalized;
        }

        repaired = false;
        return text;
    }

    public static string PrepareMessageBody(string? text) =>
        TranscriptMarkdownContract.PrepareMessageBody(NormalizeMessageBodyCore(text));

    public static string PrepareOutcomeDetailBody(string? text) =>
        TranscriptMarkdownContract.PrepareTranscriptMarkdownForExport(NormalizeMessageBodyCore(text)).Trim();

    public static string PrepareTranscriptMarkdownForExport(string? markdown) {
        return TranscriptMarkdownContract.PrepareTranscriptMarkdownForExport(NormalizeMessageBodyCore(markdown));
    }

    public static string PrepareTranscriptMarkdownForPortableExport(string? markdown) {
        return TranscriptMarkdownContract.PrepareTranscriptMarkdownForPortableExport(NormalizeMessageBodyCore(markdown));
    }

    public static string PrepareStreamingPreview(string? text) =>
        TranscriptMarkdownNormalizer.NormalizeForStreamingPreview(text);

    private static string NormalizeMessageBodyCore(string? text) {
        var value = text ?? string.Empty;
        return TranscriptMarkdownNormalizer.NormalizeForRendering(value);
    }
}
