using System;
using IntelligenceX.Chat.App;
using IntelligenceX.Chat.App.Markdown;
using IntelligenceX.Chat.App.Rendering;
using IntelligenceX.Chat.ExportArtifacts;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Verifies the shared transcript markdown contract stays aligned across render, export, and DOCX seams.
/// </summary>
public sealed class TranscriptMarkdownContractIntegrationTests {
    /// <summary>
    /// Ensures render-body and markdown-export preparation agree on the shared normalization contract when no export-only cleanup is required.
    /// </summary>
    [Fact]
    public void RenderAndExportPreparation_ShareCoreNormalization_WhenNoExportOnlyCleanupIsNeeded() {
        const string markdown = """
            1. First check
            2. Second check

            - Signal **Only total count checked, not origin split -> **Why it matters:**external/custom rules can drift ->**Next action:**break down origins.**
            - TestimoX rules available ****359****
            """;

        var preparedForRender = TranscriptMarkdownPreparation.PrepareMessageBody(markdown)
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        var preparedForExport = LocalExportArtifactWriter.NormalizeTranscriptMarkdownForExport(markdown)
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Equal(preparedForRender, preparedForExport);
        Assert.Contains("1. First check\n\n2. Second check", preparedForRender, StringComparison.Ordinal);
        Assert.Contains("**Only total count checked, not origin split** -> **Why it matters:** external/custom rules can drift", preparedForRender, StringComparison.Ordinal);
        Assert.Contains("**359**", preparedForRender, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures export-only cleanup removes transport markers while leaving the shared normalized content intact for DOCX preparation.
    /// </summary>
    [Fact]
    public void ExportAndDocxPreparation_KeepSharedContentWhileApplyingExplicitDocxDifferences() {
        const string markdown = """
            # Transcript

            [Cached evidence fallback]
            ix:cached-tool-evidence:v1

            Status: healthy
            Impact: none
            """;

        var preparedForExport = LocalExportArtifactWriter.NormalizeTranscriptMarkdownForExport(markdown)
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        var preparedForDocx = OfficeImoArtifactWriter.NormalizeTranscriptMarkdownForDocx(preparedForExport)
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.DoesNotContain("ix:cached-tool-evidence:v1", preparedForExport, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[Cached evidence fallback]", preparedForExport, StringComparison.Ordinal);
        Assert.Contains("Status: healthy", preparedForExport, StringComparison.Ordinal);
        Assert.Contains("Impact: none", preparedForExport, StringComparison.Ordinal);

        Assert.Contains("[Cached evidence fallback]", preparedForDocx, StringComparison.Ordinal);
        Assert.Contains("Status: healthy", preparedForDocx, StringComparison.Ordinal);
        Assert.Contains("Impact: none", preparedForDocx, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures transcript HTML rendering reflects the same shared normalization now owned by the transcript markdown contract.
    /// </summary>
    [Fact]
    public void HtmlRendering_ReflectsSharedTranscriptMarkdownContract() {
        const string markdown = """
            1. First check
            2. Second check

            - TestimoX rules available ****359****
            """;

        var html = TranscriptHtmlFormatter.FormatSingleMessageForExport(
            "Assistant",
            markdown,
            OfficeImoMarkdownRuntimeContract.CreateTranscriptRendererOptions());

        Assert.Contains("<li>First check</li>", html, StringComparison.Ordinal);
        Assert.Contains("<li>Second check</li>", html, StringComparison.Ordinal);
        Assert.Contains("<strong>359</strong>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("****359****", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures cached-evidence outcome bodies use the same shared preparation seam as normal transcript content.
    /// </summary>
    [Fact]
    public void OutcomeDetailPreparation_ReusesSharedTranscriptPreparationContract() {
        const string detail = """
            ix:cached-tool-evidence:v1

            Recent evidence:
            - eventlog_top_events: ### Top 30 recent events (preview)
            """;

        var prepared = TranscriptMarkdownPreparation.PrepareOutcomeDetailBody(detail)
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.DoesNotContain("ix:cached-tool-evidence:v1", prepared, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("- eventlog_top_events:", prepared, StringComparison.Ordinal);
        Assert.Contains("### Top 30 recent events (preview)", prepared, StringComparison.Ordinal);
    }
}
