using System;
using System.IO;
using IntelligenceX.Chat.App;
using OfficeIMO.MarkdownRenderer;
using Xunit;

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
        if (markdown.Length == 0) {
            return;
        }

        var explicitOptions = TryCreateExplicitOfficeImoTranscriptMinimal();
        if (explicitOptions == null) {
            return;
        }

        var actual = OfficeImoMarkdownRuntimeContract.ApplyTranscriptMarkdownPreProcessors(markdown);
        var expected = ApplyMarkdownPreProcessors(markdown, explicitOptions);

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
        if (markdown.Length == 0) {
            return;
        }

        var expectedOptions = TryCreateExplicitOfficeImoTranscriptDesktopShell();
        if (expectedOptions == null) {
            return;
        }

        var actual = MarkdownRenderer.RenderBodyHtml(markdown, OfficeImoMarkdownRuntimeContract.CreateTranscriptRendererOptions());
        var expected = MarkdownRenderer.RenderBodyHtml(markdown, expectedOptions);

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
        if (markdown.Length == 0) {
            return;
        }

        var expectedOptions = TryCreateExplicitOfficeImoTranscriptDesktopShell();
        if (expectedOptions == null) {
            return;
        }

        var actual = MarkdownRenderer.RenderBodyHtml(markdown, OfficeImoMarkdownRuntimeContract.CreateTranscriptRendererOptions());
        var expected = MarkdownRenderer.RenderBodyHtml(markdown, expectedOptions);

        Assert.Equal(NormalizeHtml(expected), NormalizeHtml(actual));
        Assert.Contains("data-omd-fence-language=\"ix-chart\"", actual, StringComparison.Ordinal);
        Assert.Contains("class=\"mermaid\"", actual, StringComparison.Ordinal);
        Assert.Contains("omd-visual omd-chart", actual, StringComparison.Ordinal);
    }

    private static string ApplyMarkdownPreProcessors(string markdown, MarkdownRendererOptions options) {
        var pipelineType = Type.GetType(
            "OfficeIMO.MarkdownRenderer.MarkdownRendererPreProcessorPipeline, OfficeIMO.MarkdownRenderer",
            throwOnError: false);
        var method = pipelineType?.GetMethod(
            "Apply",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
            binder: null,
            types: [typeof(string), typeof(MarkdownRendererOptions)],
            modifiers: null);
        if (method == null) {
            return markdown;
        }

        return (string)(method.Invoke(null, [markdown, options]) ?? markdown);
    }

    private static MarkdownRendererOptions? TryCreateExplicitOfficeImoTranscriptMinimal() {
        var method = typeof(MarkdownRendererPresets).GetMethod(
            "CreateIntelligenceXTranscriptMinimal",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);
        return method?.Invoke(null, null) as MarkdownRendererOptions;
    }

    private static MarkdownRendererOptions? TryCreateExplicitOfficeImoTranscriptDesktopShell() {
        var method = typeof(MarkdownRendererPresets).GetMethod(
            "CreateIntelligenceXTranscriptDesktopShell",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);
        return method?.Invoke(null, null) as MarkdownRendererOptions;
    }

    private static string LoadOfficeImoCompatibilityFixture(string name) {
        var path = TryFindOfficeImoCompatibilityFixture(name);
        if (path == null) {
            return string.Empty;
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
