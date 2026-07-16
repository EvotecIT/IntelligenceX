using System;
using System.Collections.Generic;
using System.Linq;
using ChartForgeX.Markup;
using IntelligenceX.Chat.App.Native.Rendering;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Tests the native rendering adapter seams without taking ownership of Markdown parsing.
/// </summary>
public sealed class NativeRenderingProjectionTests {
    /// <summary>
    /// Ensures supported fences use the ChartForgeX scanner's canonical kind contract.
    /// </summary>
    [Theory]
    [InlineData("mermaid", nameof(VisualMarkupKind.Mermaid), "mermaid")]
    [InlineData("chartforgex topology v1", nameof(VisualMarkupKind.Topology), "chartforgex topology")]
    [InlineData("chartforgex flow v1", nameof(VisualMarkupKind.Flow), "chartforgex flow")]
    [InlineData("chartforgex table v1", nameof(VisualMarkupKind.Table), "chartforgex table")]
    [InlineData("chartforgex chart v1", nameof(VisualMarkupKind.Chart), "chartforgex chart")]
    [InlineData("chartforgex timeline v1", nameof(VisualMarkupKind.Timeline), "chartforgex timeline")]
    [InlineData("chartforgex sequence v1", nameof(VisualMarkupKind.Sequence), "chartforgex sequence")]
    public void Project_RoutesVisualsThroughCanonicalChartForgeXContract(
        string infoString,
        string expectedKindName,
        string expectedFenceName) {
        var markdown = "```" + infoString + "\ninvalid test payload\n```";

        var content = NativeMarkdownProjection.Project(markdown);

        var visual = Assert.Single(content, item => item.Kind == NativeTranscriptContentKind.Visual);
        Assert.Equal(Enum.Parse<VisualMarkupKind>(expectedKindName), visual.Visual?.Kind);
        Assert.Equal(expectedFenceName, visual.Visual?.FenceName);
    }

    /// <summary>
    /// Ensures legacy IX aliases are not silently treated as ChartForgeX-native artifacts.
    /// </summary>
    [Theory]
    [InlineData("dataview")]
    [InlineData("ix-dataview")]
    [InlineData("ix-network")]
    [InlineData("cfx-flow")]
    [InlineData("chartforgex-flow")]
    public void Project_DoesNotInventVisualAliasesOutsideChartForgeX(string value) {
        var markdown = "```" + value + "\nA --> B\n```";

        var content = NativeMarkdownProjection.Project(markdown);

        Assert.DoesNotContain(content, item => item.Kind == NativeTranscriptContentKind.Visual);
    }

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
    /// <summary>
    /// Ensures the shared ChartForgeX scanner owns visual attributes and schema validation.
    /// </summary>
    [Fact]
    public void Project_PreservesCanonicalChartForgeXFenceMetadata() {
        const string markdown = """
            ```mermaid {#diagram-1 .wide .accent title="Replication" interactive=true}
            graph TD
              A --> B
            ```
            """;

        var content = NativeMarkdownProjection.Project(markdown);

        var attributes = Assert.Single(content, item => item.Kind == NativeTranscriptContentKind.Visual).Visual!.Attributes;

        Assert.Equal("diagram-1", attributes["id"]);
        Assert.Equal("wide accent", attributes["class"]);
        Assert.Equal("Replication", attributes["title"]);
        Assert.Equal("true", attributes["interactive"]);
    }

    /// <summary>
    /// Ensures incomplete ChartForgeX fences surface the shared scanner's schema diagnostic.
    /// </summary>
    [Fact]
    public void Project_MissingChartForgeXSchemaVersion_ReturnsDiagnostic() {
        const string markdown = """
            ```chartforgex topology
            node api "API"
            ```
            """;

        var content = NativeMarkdownProjection.Project(markdown);

        Assert.Contains(content, item => item.Kind == NativeTranscriptContentKind.Diagnostic
            && item.Text.Contains("must declare schema version v1", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(content, item => item.Kind == NativeTranscriptContentKind.Visual);
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

    /// <summary>
    /// Ensures the OfficeIMO to ChartForgeX path returns a native preview for supported Mermaid diagrams.
    /// </summary>
    [Fact]
    public void Project_ProducesMermaidVisualPreview() {
        const string markdown = """
            ```mermaid
            graph TD
              A[Start] --> B[Done]
            ```
            """;

        var content = NativeMarkdownProjection.Project(markdown);

        var visual = Assert.Single(content, item => item.Kind == NativeTranscriptContentKind.Visual);
        Assert.NotNull(visual.Visual);
        Assert.NotNull(visual.Visual.Artifact);
        Assert.NotNull(visual.Visual.Preview);
        Assert.True(visual.Visual.Preview.HasPng);
    }
}
