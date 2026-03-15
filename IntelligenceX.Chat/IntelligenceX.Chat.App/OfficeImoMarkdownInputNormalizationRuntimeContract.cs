using OfficeIMO.Markdown;

namespace IntelligenceX.Chat.App;

/// <summary>
/// Centralizes the OfficeIMO transcript input-normalization contract used during transcript cleanup.
/// </summary>
internal static class OfficeImoMarkdownInputNormalizationRuntimeContract {
    public static string NormalizeForTranscriptCleanup(string text) {
        if (string.IsNullOrEmpty(text)) {
            return text;
        }

        var normalized = MarkdownInputNormalizer.Normalize(
            text,
            MarkdownInputNormalizationPresets.CreateIntelligenceXTranscript());

        return normalized;
    }
}
