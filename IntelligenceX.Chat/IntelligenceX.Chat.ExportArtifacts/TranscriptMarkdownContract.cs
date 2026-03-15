using System;
using OfficeIMO.Markdown;

namespace IntelligenceX.Chat.ExportArtifacts;

/// <summary>
/// Shared transcript markdown preparation contract used by export and DOCX flows.
/// </summary>
public static class TranscriptMarkdownContract {
    /// <summary>
    /// Prepares transcript markdown for markdown export by applying shared normalization, removing transport markers,
    /// and collapsing duplicate blank lines.
    /// </summary>
    public static string PrepareTranscriptMarkdownForExport(string? markdown) {
        var withoutMarkers = MarkdownTranscriptTransportMarkers.StripIntelligenceXCachedEvidenceTransportMarkers(markdown);
        if (string.IsNullOrEmpty(withoutMarkers)) {
            return string.Empty;
        }

        return MarkdownTranscriptPreparation.PrepareIntelligenceXTranscriptForExport(withoutMarkers);
    }

    /// <summary>
    /// Applies the DOCX-specific normalization contract after the transcript markdown has already been prepared for export.
    /// </summary>
    public static string PrepareTranscriptMarkdownForDocx(string markdown, bool preservesGroupedDefinitionLikeParagraphs) {
        return MarkdownTranscriptPreparation.PrepareIntelligenceXTranscriptForDocx(
            markdown,
            preservesGroupedDefinitionLikeParagraphs);
    }

}
