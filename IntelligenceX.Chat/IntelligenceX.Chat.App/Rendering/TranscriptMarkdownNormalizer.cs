using OfficeIMO.Markdown;

namespace IntelligenceX.Chat.App.Rendering;

/// <summary>
/// Normalizes common LLM markdown artifacts before UI rendering.
/// </summary>
internal static class TranscriptMarkdownNormalizer {
    public static string NormalizeForRendering(string? text) {
        var normalized = text ?? string.Empty;
        if (normalized.Length == 0) {
            return string.Empty;
        }

        normalized = OfficeImoMarkdownRuntimeContract.ApplyTranscriptMarkdownPreProcessors(normalized);

        return MarkdownFence.ApplyTransformOutsideFencedCodeBlocks(normalized, static segment =>
            MarkdownInlineCode.ApplyTransformPreservingInlineCodeSpans(segment, static protectedInlineCode => {
                return OfficeImoMarkdownInputNormalizationRuntimeContract.NormalizeForTranscriptCleanup(protectedInlineCode);
        }));
    }

    /// <summary>
    /// Lightweight sanitizer for partial streaming text before render.
    /// Keeps edits conservative to avoid reshaping incomplete markdown while still removing
    /// the most common visually-breaking artifacts seen in deltas.
    /// </summary>
    public static string NormalizeForStreamingPreview(string? text) {
        var normalized = text ?? string.Empty;
        if (normalized.Length == 0) {
            return string.Empty;
        }

        normalized = OfficeImoMarkdownRuntimeContract.ApplyTranscriptMarkdownPreProcessors(normalized);
        return MarkdownStreamingPreviewNormalizer.NormalizeIntelligenceXTranscript(normalized);
    }

    public static bool TryRepairLegacyTranscript(string? text, out string normalized) {
        normalized = text ?? string.Empty;
        if (normalized.Length == 0) {
            return false;
        }

        var repaired = NormalizeForRendering(normalized);
        if (repaired == normalized) {
            return false;
        }

        normalized = repaired;
        return true;
    }

}
