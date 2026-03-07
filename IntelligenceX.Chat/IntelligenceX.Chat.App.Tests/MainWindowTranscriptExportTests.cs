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
            @"Transcript export failed during markdown fallback write: disk full (attempted fallback path: C:\exports\transcript.md).",
            text);
    }

    /// <summary>
    /// Ensures retrying DOCX without materialized visuals promotes the retry failure stage instead of leaving it as a generic DOCX write.
    /// </summary>
    [Fact]
    public void ResolveTranscriptExportResultAfterMaterializedDocxRetry_PromotesRetryFailureStage() {
        var materializedFailure = TranscriptExportResult.Failed(
            ExportPreferencesContract.FormatDocx,
            @"C:\exports\transcript.docx",
            new TranscriptExportFailure(TranscriptExportStage.DocxWrite, "materialized write failed"));
        var retryFailure = TranscriptExportResult.Failed(
            ExportPreferencesContract.FormatDocx,
            @"C:\exports\transcript.docx",
            new TranscriptExportFailure(TranscriptExportStage.DocxWrite, "retry write failed"));

        var result = MainWindow.ResolveTranscriptExportResultAfterMaterializedDocxRetry(materializedFailure, retryFailure);

        Assert.False(result.Succeeded);
        Assert.Equal(TranscriptExportStage.DocxWriteWithoutMaterializedVisuals, result.Failure?.Stage);
        Assert.Contains("retry write failed", result.Failure?.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures markdown fallback after the retry attributes its cause to the non-materialized DOCX retry, not the original materialized attempt.
    /// </summary>
    [Fact]
    public void ResolveTranscriptExportResultAfterMaterializedDocxRetry_PromotesRetryFallbackCauseStage() {
        var materializedFailure = TranscriptExportResult.Failed(
            ExportPreferencesContract.FormatDocx,
            @"C:\exports\transcript.docx",
            new TranscriptExportFailure(TranscriptExportStage.DocxWrite, "materialized write failed"));
        var retrySuccessWithMarkdownFallback = TranscriptExportResult.SuccessWithFallback(
            ExportPreferencesContract.FormatDocx,
            ExportPreferencesContract.FormatMarkdown,
            @"C:\exports\transcript.md",
            new TranscriptExportFallback(
                TranscriptExportFallbackKind.Markdown,
                @"C:\exports\transcript.md",
                new TranscriptExportFailure(TranscriptExportStage.DocxWrite, "retry write failed")));

        var result = MainWindow.ResolveTranscriptExportResultAfterMaterializedDocxRetry(materializedFailure, retrySuccessWithMarkdownFallback);

        Assert.True(result.Succeeded);
        Assert.Equal(TranscriptExportFallbackKind.Markdown, result.Fallback?.Kind);
        Assert.Equal(TranscriptExportStage.DocxWriteWithoutMaterializedVisuals, result.Fallback?.Cause.Stage);
        Assert.Contains("retry write failed", result.Fallback?.Cause.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the final failed result preserves the retry fallback path while promoting both retry stages consistently.
    /// </summary>
    [Fact]
    public void ResolveTranscriptExportResultAfterMaterializedDocxRetry_PromotesFailedRetryWithFallbackStages() {
        var materializedFailure = TranscriptExportResult.Failed(
            ExportPreferencesContract.FormatDocx,
            @"C:\exports\transcript.docx",
            new TranscriptExportFailure(TranscriptExportStage.DocxWrite, "materialized write failed"));
        var failedRetryWithFallback = TranscriptExportResult.Failed(
            ExportPreferencesContract.FormatDocx,
            @"C:\exports\transcript.docx",
            new TranscriptExportFailure(TranscriptExportStage.MarkdownFallbackWrite, "markdown fallback failed"),
            new TranscriptExportFallback(
                TranscriptExportFallbackKind.Markdown,
                @"C:\exports\transcript.md",
                new TranscriptExportFailure(TranscriptExportStage.DocxWrite, "retry write failed")));

        var result = MainWindow.ResolveTranscriptExportResultAfterMaterializedDocxRetry(materializedFailure, failedRetryWithFallback);

        Assert.False(result.Succeeded);
        Assert.Equal(TranscriptExportStage.MarkdownFallbackWrite, result.Failure?.Stage);
        Assert.Equal(TranscriptExportFallbackKind.Markdown, result.Fallback?.Kind);
        Assert.Equal(@"C:\exports\transcript.md", result.Fallback?.OutputPath);
        Assert.Equal(TranscriptExportStage.DocxWriteWithoutMaterializedVisuals, result.Fallback?.Cause.Stage);
        Assert.Contains("retry write failed", result.Fallback?.Cause.Message, StringComparison.Ordinal);
    }
}
