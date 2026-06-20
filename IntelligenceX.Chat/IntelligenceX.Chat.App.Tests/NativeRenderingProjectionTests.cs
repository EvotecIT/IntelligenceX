using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.Chat.App.Native.Rendering;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Tests the native rendering adapter seams without taking ownership of Markdown parsing.
/// </summary>
public sealed class NativeRenderingProjectionTests {
    /// <summary>
    /// Ensures Mermaid fences route to the portable Mermaid visual engine path.
    /// </summary>
    [Theory]
    [InlineData("mermaid", "", "")]
    [InlineData("", "mermaid", "")]
    [InlineData("", "", "mermaid title=\"Flow\"")]
    public void TryClassify_RoutesMermaidFences(string semanticKind, string language, string infoString) {
        var classified = NativeVisualFenceClassifier.TryClassify(
            semanticKind,
            language,
            infoString,
            out var kind,
            out var fenceName);

        Assert.True(classified);
        Assert.Equal(NativeVisualFenceKind.Mermaid, kind);
        Assert.Equal("mermaid", fenceName);
    }

    /// <summary>
    /// Ensures explicit ChartForgeX fences route to product-neutral artifact kinds.
    /// </summary>
    [Theory]
    [InlineData("chartforgex topology", nameof(NativeVisualFenceKind.Topology), "chartforgex topology")]
    [InlineData("chartforgex table", nameof(NativeVisualFenceKind.Table), "chartforgex table")]
    [InlineData("chartforgex chart title=\"Summary\"", nameof(NativeVisualFenceKind.Chart), "chartforgex chart")]
    [InlineData("cfx-flow", nameof(NativeVisualFenceKind.Flow), "chartforgex flow")]
    [InlineData("cfx timeline", nameof(NativeVisualFenceKind.Timeline), "chartforgex timeline")]
    public void TryClassify_RoutesChartForgeXFences(string infoString, string expectedKindName, string expectedFenceName) {
        var expectedKind = Enum.Parse<NativeVisualFenceKind>(expectedKindName);
        var classified = NativeVisualFenceClassifier.TryClassify(
            semanticKind: null,
            language: null,
            infoString: infoString,
            out var kind,
            out var fenceName);

        Assert.True(classified);
        Assert.Equal(expectedKind, kind);
        Assert.Equal(expectedFenceName, fenceName);
    }

    /// <summary>
    /// Ensures legacy IX aliases are not silently treated as ChartForgeX-native artifacts.
    /// </summary>
    [Theory]
    [InlineData("dataview")]
    [InlineData("ix-dataview")]
    [InlineData("ix-network")]
    public void TryClassify_DoesNotPretendLegacyAliasesArePortableArtifacts(string value) {
        var classified = NativeVisualFenceClassifier.TryClassify(
            semanticKind: value,
            language: value,
            infoString: value,
            out var kind,
            out var fenceName);

        Assert.False(classified);
        Assert.Equal(NativeVisualFenceKind.Unsupported, kind);
        Assert.Equal(string.Empty, fenceName);
    }

#if IXCHAT_NATIVE_MARKDOWN_ENGINES
    /// <summary>
    /// Ensures unsupported visual diagnostics use the best available OfficeIMO fence identifier.
    /// </summary>
    [Theory]
    [InlineData("custom-visual", "", "", "custom-visual")]
    [InlineData("custom-visual", "legacy-network", "", "legacy-network")]
    [InlineData("custom-visual", "legacy-network", "dataview title=\"Rows\"", "dataview title=\"Rows\"")]
    [InlineData("", "", "", "unknown")]
    public void ResolveVisualFenceIdentifier_UsesBestAvailableFenceMetadata(
        string semanticKind,
        string language,
        string infoString,
        string expected) {
        var resolved = NativeMarkdownProjection.ResolveVisualFenceIdentifier(semanticKind, language, infoString);

        Assert.Equal(expected, resolved);
    }
#endif

    /// <summary>
    /// Ensures OfficeIMO fence metadata can be passed through to ChartForgeX without Markdown rescanning.
    /// </summary>
    [Fact]
    public void BuildAttributes_PreservesStructuredFenceMetadata() {
        var attributes = NativeVisualFenceClassifier.BuildAttributes(
            "diagram-1",
            new[] { "wide", "accent" },
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) {
                ["title"] = "Replication",
                ["interactive"] = null
            });

        Assert.Equal("diagram-1", attributes["id"]);
        Assert.Equal("wide accent", attributes["class"]);
        Assert.Equal("Replication", attributes["title"]);
        Assert.Equal("true", attributes["interactive"]);
    }

    /// <summary>
    /// Ensures projection produces native paragraph content without returning HTML fragments.
    /// </summary>
    [Fact]
    public void Project_ReturnsNativeParagraphProjection() {
        var content = NativeMarkdownProjection.Project("**hello**");

        var item = Assert.Single(content);
        Assert.Equal(NativeTranscriptContentKind.Paragraph, item.Kind);
        Assert.Contains("hello", item.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<strong", item.Text, StringComparison.OrdinalIgnoreCase);
    }

#if IXCHAT_NATIVE_MARKDOWN_ENGINES
    /// <summary>
    /// Ensures the OfficeIMO to ChartForgeX path returns a native preview for supported Mermaid diagrams.
    /// </summary>
    [Fact]
    public void Project_WithLocalNativeEngines_ProducesMermaidVisualPreview() {
        const string markdown = """
            ```mermaid
            graph TD
              A[Start] --> B[Done]
            ```
            """;

        var content = NativeMarkdownProjection.Project(markdown);

        var visual = Assert.Single(content.Where(item => item.Kind == NativeTranscriptContentKind.Visual));
        Assert.NotNull(visual.Visual);
        Assert.NotNull(visual.Visual.Artifact);
        Assert.NotNull(visual.Visual.Preview);
        Assert.True(visual.Visual.Preview.HasPng);
    }
#endif
}
