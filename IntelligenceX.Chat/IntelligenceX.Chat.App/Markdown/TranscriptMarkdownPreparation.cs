using IntelligenceX.Chat.App.Rendering;
using OfficeIMO.Markdown;

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
        MarkdownTranscriptPreparation.PrepareIntelligenceXTranscriptBody(NormalizeMessageBodyCore(text));

    public static string PrepareOutcomeDetailBody(string? text) =>
        PrepareMessageBody(text).Trim();

    public static string PrepareTranscriptMarkdownForExport(string? markdown) {
        var normalized = NormalizeMessageBodyCore(markdown);
        var withoutMarkers = MarkdownTranscriptTransportMarkers.StripIntelligenceXCachedEvidenceTransportMarkers(normalized);
        return MarkdownTranscriptPreparation.PrepareIntelligenceXTranscriptForExport(withoutMarkers);
    }

    public static string PrepareStreamingPreview(string? text) =>
        TranscriptMarkdownNormalizer.NormalizeForStreamingPreview(text);

    private static string NormalizeMessageBodyCore(string? text) {
        var value = text ?? string.Empty;
        return TranscriptMarkdownNormalizer.NormalizeForRendering(value);
    }
}
