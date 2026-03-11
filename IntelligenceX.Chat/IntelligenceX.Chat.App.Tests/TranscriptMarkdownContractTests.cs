using System;
using IntelligenceX.Chat.ExportArtifacts;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Verifies the shared transcript markdown contract used across render and export paths.
/// </summary>
public sealed class TranscriptMarkdownContractTests {
    /// <summary>
    /// Ensures shared message-body preparation repairs adjacent ordered list items.
    /// </summary>
    [Fact]
    public void PrepareMessageBody_InsertsBlankLineBetweenAdjacentOrderedItems() {
        const string markdown = """
            1. First check
            2. Second check
            """;

        var normalized = TranscriptMarkdownContract.PrepareMessageBody(markdown)
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains("1. First check\n\n2. Second check", normalized, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures export preparation removes cached-evidence transport markers while preserving content.
    /// </summary>
    [Fact]
    public void PrepareTranscriptMarkdownForExport_RemovesCachedEvidenceMarkers() {
        const string markdown = """
            # Transcript

            [Cached evidence fallback]
            ix:cached-tool-evidence:v1

            ### Result
            """;

        var normalized = TranscriptMarkdownContract.PrepareTranscriptMarkdownForExport(markdown);

        Assert.DoesNotContain("ix:cached-tool-evidence:v1", normalized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[Cached evidence fallback]", normalized, StringComparison.Ordinal);
        Assert.Contains("### Result", normalized, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures DOCX preparation keeps legacy grouped definition repair isolated behind an explicit capability flag.
    /// </summary>
    [Fact]
    public void PrepareTranscriptMarkdownForDocx_OptionallySeparatesGroupedDefinitionLikeParagraphs() {
        const string markdown = """
            # Transcript

            Status: healthy
            Impact: none
            """;

        var preserved = TranscriptMarkdownContract.PrepareTranscriptMarkdownForDocx(markdown, preservesGroupedDefinitionLikeParagraphs: true)
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        var repaired = TranscriptMarkdownContract.PrepareTranscriptMarkdownForDocx(markdown, preservesGroupedDefinitionLikeParagraphs: false)
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains("Status: healthy\nImpact: none", preserved, StringComparison.Ordinal);
        Assert.Contains("Status: healthy\n\nImpact: none", repaired, StringComparison.Ordinal);
    }
}
