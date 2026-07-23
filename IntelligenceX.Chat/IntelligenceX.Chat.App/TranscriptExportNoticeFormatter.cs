using IntelligenceX.Chat.App.Conversation;

namespace IntelligenceX.Chat.App;

/// <summary>
/// Formats truthful transcript export status for every desktop shell.
/// </summary>
internal static class TranscriptExportNoticeFormatter {
    public static string FormatSuccess(TranscriptExportResult result) {
        if (!result.Succeeded) {
            return FormatFailure(result);
        }

        if (!result.UsedFallback || result.Fallback is not { } fallback) {
            return SystemNoticeFormatter.Format(SystemNotice.TranscriptExported(result.OutputPath));
        }

        return fallback.Kind switch {
            TranscriptExportFallbackKind.DocxWithoutMaterializedVisuals =>
                "Exported transcript: " + result.OutputPath + " (DOCX export retried without materialized visuals.)",
            TranscriptExportFallbackKind.Markdown =>
                "Exported transcript: " + result.OutputPath + " (DOCX export fell back to Markdown.)",
            _ => SystemNoticeFormatter.Format(SystemNotice.TranscriptExported(result.OutputPath))
        };
    }

    public static string FormatFailure(TranscriptExportResult result) {
        var failure = result.Failure;
        var stage = DescribeStage(failure?.Stage ?? TranscriptExportStage.None);
        var message = (failure?.Message ?? "Unknown error.").Trim();
        if (message.Length == 0) {
            message = "Unknown error.";
        }

        if (result.Fallback is { } fallback) {
            return "Transcript export failed during "
                   + stage
                   + ": "
                   + message
                   + " (attempted fallback path: "
                   + fallback.OutputPath
                   + ").";
        }

        return "Transcript export failed during " + stage + ": " + message;
    }

    private static string DescribeStage(TranscriptExportStage stage) => stage switch {
        TranscriptExportStage.MarkdownWrite => "markdown write",
        TranscriptExportStage.MarkdownFallbackWrite => "markdown fallback write",
        TranscriptExportStage.DocxWrite => "DOCX write",
        TranscriptExportStage.DocxWriteWithMaterializedVisuals => "DOCX write with materialized visuals",
        TranscriptExportStage.DocxWriteWithoutMaterializedVisuals => "DOCX retry without materialized visuals",
        _ => "export"
    };
}
