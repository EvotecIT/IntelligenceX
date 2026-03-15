using System;
using System.IO;
using IntelligenceX.Chat.App;
using OfficeIMO.MarkdownRenderer;
using Xunit;
using Xunit.Sdk;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Verifies the IX runtime contract stays aligned with the explicit OfficeIMO transcript corpus
/// when the sibling OfficeIMO repository is available in the workspace.
/// </summary>
public sealed class OfficeImoMarkdownTranscriptCorpusParityTests {
    /// <summary>
    /// Verifies IX transcript pre-processing matches the explicit OfficeIMO transcript preset
    /// for the exported mixed visual transcript corpus.
    /// </summary>
    [Fact]
    public void ExportedTranscriptVisualPack_PreProcessorsMatchExplicitOfficeImoTranscriptPreset() {
        string markdown = LoadOfficeImoCompatibilityFixture("ix-exported-transcript-visual-pack.md");

        var actual = OfficeImoMarkdownRuntimeContract.ApplyTranscriptMarkdownPreProcessors(markdown);
        var expected = ApplyMarkdownPreProcessors(markdown, MarkdownRendererPresets.CreateIntelligenceXTranscriptMinimal());

        Assert.Equal(expected, actual);
        Assert.Contains("```ix-chart", actual, StringComparison.Ordinal);
        Assert.Contains("```mermaid", actual, StringComparison.Ordinal);
        Assert.Contains("\"label\": \"Broken\"", actual, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies IX transcript rendering matches the explicit OfficeIMO transcript preset
    /// for the exported mixed visual transcript corpus.
    /// </summary>
    [Fact]
    public void ExportedTranscriptVisualPack_RenderingMatchesExplicitOfficeImoTranscriptPreset() {
        string markdown = LoadOfficeImoCompatibilityFixture("ix-exported-transcript-visual-pack.md");

        var actual = MarkdownRenderer.RenderBodyHtml(markdown, OfficeImoMarkdownRuntimeContract.CreateTranscriptRendererOptions());
        var expected = MarkdownRenderer.RenderBodyHtml(markdown, CreateOfficeImoTranscriptRendererOptions());

        Assert.Equal(NormalizeHtml(expected), NormalizeHtml(actual));
        Assert.Contains("data-omd-fence-language=\"ix-chart\"", actual, StringComparison.Ordinal);
        Assert.Contains("class=\"mermaid\"", actual, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies IX transcript rendering matches the explicit OfficeIMO transcript preset
    /// for the exported chart-heavy transcript corpus.
    /// </summary>
    [Fact]
    public void ExportedTranscriptChartSuite_RenderingMatchesExplicitOfficeImoTranscriptPreset() {
        string markdown = LoadOfficeImoCompatibilityFixture("ix-exported-transcript-chart-suite.md");

        var actual = MarkdownRenderer.RenderBodyHtml(markdown, OfficeImoMarkdownRuntimeContract.CreateTranscriptRendererOptions());
        var expected = MarkdownRenderer.RenderBodyHtml(markdown, CreateOfficeImoTranscriptRendererOptions());

        Assert.Equal(NormalizeHtml(expected), NormalizeHtml(actual));
        Assert.Contains("data-omd-fence-language=\"ix-chart\"", actual, StringComparison.Ordinal);
        Assert.Contains("class=\"mermaid\"", actual, StringComparison.Ordinal);
        Assert.Contains("omd-visual omd-chart", actual, StringComparison.Ordinal);
    }

    private static MarkdownRendererOptions CreateOfficeImoTranscriptRendererOptions() {
        return MarkdownRendererPresets.CreateIntelligenceXTranscriptDesktopShell();
    }

    private static string ApplyMarkdownPreProcessors(string markdown, MarkdownRendererOptions options) {
        return MarkdownRendererPreProcessorPipeline.Apply(markdown, options);
    }

    private static string LoadOfficeImoCompatibilityFixture(string name) {
        var path = TryFindOfficeImoCompatibilityFixture(name);
        if (path == null) {
            throw SkipException.ForSkip("Sibling OfficeIMO transcript corpus fixture not found; parity test requires both repositories checked out side-by-side.");
        }

        return File.ReadAllText(path);
    }

    private static string? TryFindOfficeImoCompatibilityFixture(string name) {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current != null; current = current.Parent) {
            var candidate = Path.Combine(
                current.FullName,
                "OfficeIMO",
                "OfficeIMO.Tests",
                "Markdown",
                "Fixtures",
                "Compatibility",
                name);

            if (File.Exists(candidate)) {
                return candidate;
            }
        }

        return null;
    }

    private static string NormalizeHtml(string html) {
        if (string.IsNullOrWhiteSpace(html)) {
            return string.Empty;
        }

        return html
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Trim();
    }
}
