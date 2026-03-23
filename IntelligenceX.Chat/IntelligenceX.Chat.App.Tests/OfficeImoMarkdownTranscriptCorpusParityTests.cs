using System;
using System.IO;
using IntelligenceX.Chat.App;
using IntelligenceX.Chat.ExportArtifacts;
using OfficeIMO.Markdown;
using OfficeIMO.MarkdownRenderer;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Verifies the IX runtime/render and portable-export seams stay aligned with the explicit OfficeIMO corpus fixtures.
/// </summary>
public sealed class OfficeImoMarkdownTranscriptCorpusParityTests {
    /// <summary>
    /// Verifies IX transcript pre-processing matches the explicit OfficeIMO transcript preset
    /// for the IX compatibility mixed visual transcript corpus.
    /// </summary>
    [Fact]
    public void IxCompatibilityTranscriptVisualPack_PreProcessorsMatchExplicitOfficeImoTranscriptPreset() {
        string markdown = LoadOfficeImoCorpusFixture("ix-compat-transcript-visual-pack.md");

        var explicitOptions = TryCreateExplicitOfficeImoTranscriptMinimal();
        if (explicitOptions == null) {
            return;
        }

        var actual = OfficeImoMarkdownRuntimeContract.ApplyTranscriptMarkdownPreProcessors(markdown);
        var expected = ApplyExplicitTranscriptPreparation(markdown, explicitOptions);

        Assert.Equal(expected, actual);
        Assert.Contains("```ix-chart", actual, StringComparison.Ordinal);
        Assert.Contains("```mermaid", actual, StringComparison.Ordinal);
        Assert.Contains("\"label\": \"Broken\"", actual, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies IX transcript rendering matches the explicit OfficeIMO transcript preset
    /// for the IX compatibility mixed visual transcript corpus.
    /// </summary>
    [Fact]
    public void IxCompatibilityTranscriptVisualPack_RenderingMatchesExplicitOfficeImoTranscriptPreset() {
        string markdown = LoadOfficeImoCorpusFixture("ix-compat-transcript-visual-pack.md");
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
    /// for the IX compatibility chart-heavy transcript corpus.
    /// </summary>
    [Fact]
    public void IxCompatibilityTranscriptChartSuite_RenderingMatchesExplicitOfficeImoTranscriptPreset() {
        string markdown = LoadOfficeImoCorpusFixture("ix-compat-transcript-chart-suite.md");
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

    /// <summary>
    /// Verifies portable markdown export stays aligned with the explicit OfficeIMO generic semantic-fence preparation.
    /// </summary>
    [Fact]
    public void SourceDerivedCachedEvidenceVisualCorpus_PortableExportMatchesExplicitOfficeImoGenericPreparation() {
        string markdown = LoadOfficeImoCorpusFixture("ix-source-derived-cached-evidence-visuals.md");
        var actual = TranscriptMarkdownContract.PrepareTranscriptMarkdownForPortableExport(markdown);
        var expected = ApplyExplicitPortableTranscriptPreparation(markdown);

        Assert.Equal(expected, actual);
        Assert.Contains("```chart", actual, StringComparison.Ordinal);
        Assert.Contains("```dataview", actual, StringComparison.Ordinal);
        Assert.DoesNotContain("```ix-chart", actual, StringComparison.Ordinal);
        Assert.DoesNotContain("```ix-dataview", actual, StringComparison.Ordinal);
        Assert.Contains("\"label\": \"Count\"", actual, StringComparison.Ordinal);
    }

    private static string ApplyExplicitTranscriptPreparation(string markdown, MarkdownRendererOptions options) {
        return MarkdownTranscriptPreparation.PrepareIntelligenceXTranscriptForExport(markdown);
    }

    private static string ApplyExplicitPortableTranscriptPreparation(string markdown) {
        return MarkdownTranscriptPreparation.PrepareIntelligenceXTranscriptForExport(
            markdown,
            MarkdownVisualFenceLanguageMode.GenericSemanticFence);
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
        return InvokeOptionalBaseHrefFactory("CreateIntelligenceXTranscriptMinimal");
    }

    private static MarkdownRendererOptions? TryCreateExplicitOfficeImoTranscriptDesktopShell() {
        return InvokeOptionalBaseHrefFactory("CreateIntelligenceXTranscriptDesktopShell");
    }

    private static string LoadOfficeImoCorpusFixture(string name) {
        var path = Path.Combine(GetAppTestsProjectRoot(), "Fixtures", "Compatibility", name);
        return File.ReadAllText(path);
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

    private static MarkdownRendererOptions? InvokeOptionalBaseHrefFactory(string methodName) {
        var methods = typeof(MarkdownRendererPresets).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        for (var i = 0; i < methods.Length; i++) {
            var method = methods[i];
            if (!string.Equals(method.Name, methodName, StringComparison.Ordinal)
                || !typeof(MarkdownRendererOptions).IsAssignableFrom(method.ReturnType)) {
                continue;
            }

            var parameters = method.GetParameters();
            if (parameters.Length == 0) {
                return method.Invoke(null, null) as MarkdownRendererOptions;
            }

            if (parameters.Length == 1
                && parameters[0].ParameterType == typeof(string)
                && parameters[0].IsOptional) {
                return method.Invoke(null, [null]) as MarkdownRendererOptions;
            }
        }

        return null;
    }

    private static string GetAppTestsProjectRoot() {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null) {
            string candidate = Path.Combine(dir.FullName, "IntelligenceX.Chat.App.Tests.csproj");
            if (File.Exists(candidate)) {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate IntelligenceX.Chat.App.Tests project root from test runtime base directory.");
    }
}
