using IntelligenceX.Chat.ExportArtifacts;
using IntelligenceX.Chat.App.Rendering;

namespace IntelligenceX.Chat.App.Markdown;

/// <summary>
/// Applies App-specific transcript normalization before delegating to the shared transcript markdown contract.
/// </summary>
internal static class TranscriptMarkdownPreparation {
    public static string PrepareMessageBody(string? text) =>
        TranscriptMarkdownContract.PrepareMessageBody(TranscriptMarkdownNormalizer.NormalizeForRendering(text));

    public static string PrepareTranscriptMarkdownForExport(string? markdown) =>
        TranscriptMarkdownContract.PrepareTranscriptMarkdownForExport(TranscriptMarkdownNormalizer.NormalizeForRendering(markdown));
}
