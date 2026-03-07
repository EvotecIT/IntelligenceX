using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Tests for app-side transcript export outcome handling.
/// </summary>
public sealed class MainWindowTranscriptExportTests {
    /// <summary>
    /// Ensures a successful DOCX retry after materialized-visual failure is surfaced as a typed DOCX fallback, not a hard failure.
    /// </summary>
    [Fact]
    public void ResolveTranscriptExportResultAfterMaterializedDocxRetry_PromotesSuccessfulRetryToDocxFallback() {
        var materializedFailure = TranscriptExportResult.Failed(
            ExportPreferencesContract.FormatDocx,
            @"C:\exports\transcript.docx",
            new TranscriptExportFailure(TranscriptExportStage.DocxWrite, "primary write failed"));
        var retryResult = TranscriptExportResult.Success(
            ExportPreferencesContract.FormatDocx,
            ExportPreferencesContract.FormatDocx,
            @"C:\exports\transcript.docx");

        var result = MainWindow.ResolveTranscriptExportResultAfterMaterializedDocxRetry(materializedFailure, retryResult);

        Assert.True(result.Succeeded);
        Assert.Equal(TranscriptExportOutcomeKind.SucceededWithFallback, result.OutcomeKind);
        Assert.Equal(TranscriptExportFallbackKind.DocxWithoutMaterializedVisuals, result.Fallback?.Kind);
        Assert.Equal(TranscriptExportStage.DocxWriteWithMaterializedVisuals, result.Fallback?.Cause.Stage);
        Assert.Equal(@"C:\exports\transcript.docx", result.OutputPath);
    }

    /// <summary>
    /// Ensures fallback success notices describe the actual saved format when DOCX degrades to markdown.
    /// </summary>
    [Fact]
    public void BuildTranscriptExportSuccessNoticeText_DescribesMarkdownFallback() {
        var result = TranscriptExportResult.SuccessWithFallback(
            ExportPreferencesContract.FormatDocx,
            ExportPreferencesContract.FormatMarkdown,
            @"C:\exports\transcript.md",
            new TranscriptExportFallback(
                TranscriptExportFallbackKind.Markdown,
                @"C:\exports\transcript.md",
                new TranscriptExportFailure(TranscriptExportStage.DocxWriteWithMaterializedVisuals, "primary write failed")));

        var text = MainWindow.BuildTranscriptExportSuccessNoticeText(result);

        Assert.Equal(
            @"Exported transcript: C:\exports\transcript.md (DOCX export fell back to Markdown.)",
            text);
    }

    /// <summary>
    /// Ensures failure notices expose the failing stage and the fallback path for diagnostics.
    /// </summary>
    [Fact]
    public void BuildTranscriptExportFailureNoticeText_IncludesStageAndFallbackPath() {
        var result = TranscriptExportResult.Failed(
            ExportPreferencesContract.FormatDocx,
            @"C:\exports\transcript.docx",
            new TranscriptExportFailure(TranscriptExportStage.MarkdownFallbackWrite, "disk full"),
            new TranscriptExportFallback(
                TranscriptExportFallbackKind.Markdown,
                @"C:\exports\transcript.md",
                new TranscriptExportFailure(TranscriptExportStage.DocxWriteWithMaterializedVisuals, "primary write failed")));

        var text = MainWindow.BuildTranscriptExportFailureNoticeText(result);

        Assert.Equal(
            @"Transcript export failed during markdown fallback write: disk full (fallback path: C:\exports\transcript.md).",
            text);
    }
}
