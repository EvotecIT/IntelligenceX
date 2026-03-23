using System;
using IntelligenceX.Chat.ExportArtifacts;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Verifies the shared transcript markdown contract used across render and export paths.
/// </summary>
public sealed class TranscriptMarkdownContractTests {
    /// <summary>
    /// Ensures export preparation removes cached-evidence transport markers while preserving content
    /// and collapsing the blank-line gap they leave behind.
    /// </summary>
    [Fact]
    public void PrepareTranscriptMarkdownForExport_RemovesCachedEvidenceMarkers() {
        const string markdown = """
            # Transcript

            [Cached evidence fallback]
            ix:cached-tool-evidence:v1


            ### Result
            """;

        var normalized = TranscriptMarkdownContract.PrepareTranscriptMarkdownForExport(markdown)
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.DoesNotContain("ix:cached-tool-evidence:v1", normalized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Cached evidence fallback", normalized, StringComparison.Ordinal);
        Assert.Contains("### Result", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("\n\n\n", normalized, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures portable markdown export normalizes legacy IX visual aliases onto generic semantic fence languages.
    /// </summary>
    [Fact]
    public void PrepareTranscriptMarkdownForPortableExport_UsesGenericVisualFenceLanguages() {
        const string markdown = """
            ix:cached-tool-evidence:v1

            ```json
            {"type":"bar","data":{"labels":["A"],"datasets":[{"label":"Count","data":[1]}]}}
            ```

            ```json
            {"nodes":[{"id":"A","label":"Forest: ad.evotec.xyz"}],"edges":[{"source":"A","target":"B","label":"contains"}]}
            ```

            ```json
            {"kind":"ix_tool_dataview_v1","rows":[["Server","Fails"],["AD0","0"]]}
            ```
            """;

        var normalized = TranscriptMarkdownContract.PrepareTranscriptMarkdownForPortableExport(markdown)
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.DoesNotContain("ix:cached-tool-evidence:v1", normalized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("```chart", normalized, StringComparison.Ordinal);
        Assert.Contains("```network", normalized, StringComparison.Ordinal);
        Assert.Contains("```dataview", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("```ix-chart", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("```ix-network", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("```ix-dataview", normalized, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures portable markdown export also normalizes the legacy visnetwork alias onto the generic network fence.
    /// </summary>
    [Fact]
    public void PrepareTranscriptMarkdownForPortableExport_NormalizesVisnetworkAliasToNetwork() {
        const string markdown = """
            ```visnetwork
            {"nodes":[{"id":"A","label":"User"}],"edges":[]}
            ```
            """;

        var normalized = TranscriptMarkdownContract.PrepareTranscriptMarkdownForPortableExport(markdown)
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains("```network", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("```visnetwork", normalized, StringComparison.Ordinal);
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
