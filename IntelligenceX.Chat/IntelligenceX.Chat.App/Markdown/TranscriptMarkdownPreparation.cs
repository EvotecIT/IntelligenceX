using IntelligenceX.Chat.ExportArtifacts;
using IntelligenceX.Chat.App.Rendering;

namespace IntelligenceX.Chat.App.Markdown;

/// <summary>
/// Applies App-specific transcript normalization before delegating to the shared transcript markdown contract.
/// </summary>
internal static class TranscriptMarkdownPreparation {
    public static string NormalizePersistedTranscriptText(string? role, string text, out bool repaired) {
        ArgumentNullException.ThrowIfNull(text);

        if (!TranscriptMarkdownNormalizer.TryRepairLegacyTranscript(text, out var normalized)) {
            normalized = text;
        } else {
            repaired = true;
            return normalized;
        }

        if (string.Equals(role, "User", StringComparison.OrdinalIgnoreCase)) {
            repaired = false;
            return text;
        }

        var fullyNormalized = NormalizeMessageBodyCore(text);
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
        PrepareMessageBody(text).Trim();

    public static string PrepareTranscriptMarkdownForExport(string? markdown) =>
        TranscriptMarkdownContract.PrepareTranscriptMarkdownForExport(NormalizeMessageBodyCore(markdown));

    public static string PrepareStreamingPreview(string? text) =>
        TranscriptMarkdownNormalizer.NormalizeForStreamingPreview(text);

    private static string NormalizeMessageBodyCore(string? text) {
        var value = text ?? string.Empty;
        return TranscriptMarkdownNormalizer.TryRepairLegacyTranscript(value, out var repaired)
            ? repaired
            : TranscriptMarkdownNormalizer.NormalizeForRendering(value);
    }
}
