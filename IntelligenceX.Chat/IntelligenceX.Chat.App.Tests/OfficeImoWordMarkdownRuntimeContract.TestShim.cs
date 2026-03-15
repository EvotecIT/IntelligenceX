using System.Collections.Generic;
using OfficeIMO.Word.Markdown;

namespace IntelligenceX.Chat.ExportArtifacts;

internal static class OfficeImoWordMarkdownRuntimeContract {
    public static MarkdownToWordOptions CreateTranscriptMarkdownToWordOptions(
        IReadOnlyList<string>? allowedImageDirectories,
        int? docxVisualMaxWidthPx) {
        return MarkdownToWordPresets.CreateIntelligenceXTranscript(allowedImageDirectories, docxVisualMaxWidthPx);
    }

    public static bool PreservesGroupedDefinitionLikeParagraphs() {
        return MarkdownToWordCapabilities.PreservesNarrativeSingleLineDefinitionsAsSeparateParagraphs();
    }

    public static string DescribeWordMarkdownContract() {
        var assembly = typeof(MarkdownToWordOptions).Assembly;
        var version = assembly.GetName().Version?.ToString() ?? "unknown";
        var path = string.IsNullOrWhiteSpace(assembly.Location) ? "(dynamic)" : System.IO.Path.GetFullPath(assembly.Location);
        return $"OfficeIMO.Word.Markdown expected>=1.0.8 feature=transcript markdown-to-word conversion loaded={version} status=ok path={path}";
    }
}
