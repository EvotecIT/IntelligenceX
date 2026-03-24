using System;
using OfficeIMO.Markdown;
using OfficeIMO.MarkdownRenderer;

namespace IntelligenceX.Chat.App.Rendering;

/// <summary>
/// Normalizes common LLM markdown artifacts before UI rendering.
/// </summary>
internal static class TranscriptMarkdownNormalizer {
    private static readonly MarkdownRendererOptions TranscriptRendererOptions = MarkdownRendererPresets.CreateIntelligenceXTranscriptMinimal();
    private static readonly MarkdownInputNormalizationOptions TranscriptInputNormalizationOptions = MarkdownInputNormalizationPresets.CreateIntelligenceXTranscript();

    public static string NormalizeForRendering(string? text) {
        var normalized = text ?? string.Empty;
        if (normalized.Length == 0) {
            return string.Empty;
        }

        return NormalizeProseOnly(
            normalized,
            static value => MarkdownInputNormalizer.Normalize(
                MarkdownRendererPreProcessorPipeline.Apply(value, TranscriptRendererOptions),
                TranscriptInputNormalizationOptions));
    }

    /// <summary>
    /// Lightweight sanitizer for partial streaming text before render.
    /// </summary>
    public static string NormalizeForStreamingPreview(string? text) {
        var normalized = text ?? string.Empty;
        if (normalized.Length == 0) {
            return string.Empty;
        }

        return NormalizeProseOnly(
            normalized,
            static value => MarkdownStreamingPreviewNormalizer.NormalizeIntelligenceXTranscript(
                MarkdownRendererPreProcessorPipeline.Apply(value, TranscriptRendererOptions)));
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

    private static string NormalizeProseOnly(string text, Func<string, string> transform) {
        return MarkdownFence.ApplyTransformOutsideFencedCodeBlocks(
            text,
            segment => MarkdownInlineCode.ApplyTransformPreservingInlineCodeSpans(segment, transform));
    }
}
