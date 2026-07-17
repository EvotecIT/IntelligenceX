using System;
using System.Collections.Generic;
using System.Linq;
using ChartForgeX.Markup;
using ChartForgeX.VisualArtifacts;
using IntelligenceX.Chat.App.Native.Rendering;
using IntelligenceX.Chat.App.Native;
using Microsoft.UI.Xaml;
using OfficeIMO.Markdown;
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
    /// Ensures projection queues supported Mermaid diagrams without rendering them on the caller thread.
    /// </summary>
    [Fact]
    public void Project_QueuesMermaidVisualForLazyPreview() {
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
        Assert.Null(visual.Visual.Preview);
    }

    /// <summary>
    /// Ensures the deferred ChartForgeX renderer still produces preview bytes for a supported diagram.
    /// </summary>
    [Fact]
    public void TryRender_ValidMermaidArtifact_ProducesPreview() {
        const string markdown = """
            ```mermaid
            graph TD
              A[Start] --> B[Done]
            ```
            """;
        var projected = NativeMarkdownProjection.Project(markdown);
        var artifact = Assert.Single(projected, item => item.Kind == NativeTranscriptContentKind.Visual).Visual!.Artifact;

        var preview = NativeVisualPreviewRenderer.TryRender(artifact, out var error);

        Assert.Null(error);
        Assert.NotNull(preview);
        Assert.True(preview.HasPng);
    }

    /// <summary>
    /// Ensures an invalid persisted visual cannot terminate native conversation loading.
    /// </summary>
    [Fact]
    public void TryRender_InvalidArtifact_ReturnsDiagnosticInsteadOfThrowing() {
        var artifact = VisualArtifact.Create("invalid", VisualArtifactKind.Topology, new object());

        var preview = NativeVisualPreviewRenderer.TryRender(artifact, out var error);

        Assert.Null(preview);
        Assert.Contains("does not expose a supported SVG render model", error, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures the WinUI adapter keeps the rich OfficeIMO document structure instead of flattening it to plain paragraphs.
    /// </summary>
    [Fact]
    public void Project_PreservesRichOfficeImoDocumentSemantics() {
        const string markdown = """
            ## Evidence summary

            **Healthy** replication with *two* sites, `15m` lag, and [details](https://example.test/evidence).

            - First site
            - [x] Second site checked

            > Quoted operator context

            ---
            """;

        var content = NativeMarkdownProjection.Project(markdown);

        var heading = Assert.Single(content, item => item.Kind == NativeTranscriptContentKind.Heading);
        Assert.Equal(2, heading.HeadingLevel);
        var paragraph = Assert.Single(content, item => item.Kind == NativeTranscriptContentKind.Paragraph);
        Assert.Contains(paragraph.Inlines, inline => inline.Kind == MarkdownNativeInlineKind.Strong);
        Assert.Contains(paragraph.Inlines, inline => inline.Kind == MarkdownNativeInlineKind.Emphasis);
        Assert.Contains(paragraph.Inlines, inline => inline.Kind == MarkdownNativeInlineKind.Code);
        Assert.Contains(paragraph.Inlines, inline => inline.Kind == MarkdownNativeInlineKind.Link
            && inline.Target == "https://example.test/evidence");
        var list = Assert.Single(content, item => item.Kind == NativeTranscriptContentKind.List).List;
        Assert.NotNull(list);
        Assert.Equal(2, list.Items.Count);
        Assert.True(list.Items[1].IsTask);
        Assert.True(list.Items[1].IsChecked);
        Assert.Contains(content, item => item.Kind == NativeTranscriptContentKind.Quote);
        Assert.Contains(content, item => item.Kind == NativeTranscriptContentKind.Divider);
    }

    /// <summary>
    /// Ensures Markdown images remain native preview artifacts rather than degrading to plain links.
    /// </summary>
    [Fact]
    public void Project_PreservesMarkdownImagePreviewMetadata() {
        const string markdown = "![Replication topology](https://example.test/topology.png \"Current topology\")";

        var content = NativeMarkdownProjection.Project(markdown);

        var image = Assert.Single(content, item => item.Kind == NativeTranscriptContentKind.Image).Image;
        Assert.NotNull(image);
        Assert.Equal("https://example.test/topology.png", image.Source);
        Assert.Equal("Replication topology", image.AlternateText);
        Assert.Equal("Current topology", image.Title);
    }

    /// <summary>
    /// Ensures transcript rendering never fetches model-provided remote images without an explicit operator action.
    /// </summary>
    [Theory]
    [InlineData("https://example.test/evidence.png", true)]
    [InlineData("http://127.0.0.1/private.png", true)]
    [InlineData("ms-appx:///Assets/evidence.png", false)]
    [InlineData("C:\\Evidence\\topology.png", false)]
    public void RemoteImagePolicy_RequiresExplicitConsentOnlyForHttpSources(string source, bool expected) {
        Assert.Equal(expected, NativeTranscriptImageControl.RequiresExplicitRemoteLoad(source));
    }

    /// <summary>
    /// Ensures preview decoding preserves aspect ratio while bounding both wide and tall source images.
    /// </summary>
    [Theory]
    [InlineData(3200u, 1200u, 1493, 560)]
    [InlineData(1200u, 3200u, 210, 560)]
    [InlineData(800u, 400u, 800, 400)]
    public void ImageDecodePolicy_BoundsPixelsWithoutUpscaling(
        uint sourceWidth,
        uint sourceHeight,
        int expectedWidth,
        int expectedHeight) {
        var dimensions = NativeTranscriptImageControl.CalculateDecodeDimensions(sourceWidth, sourceHeight);

        Assert.Equal(expectedWidth, dimensions.Width);
        Assert.Equal(expectedHeight, dimensions.Height);
    }

    /// <summary>
    /// Ensures OfficeIMO callouts remain semantic warning cards for the native shell.
    /// </summary>
    [Fact]
    public void Project_PreservesOfficeImoWarningCallout() {
        const string markdown = """
            > [!WARNING] Replication lag
            > Site B is more than **15 minutes** behind.
            """;

        var content = NativeMarkdownProjection.Project(markdown);

        var callout = Assert.Single(content, item => item.Kind == NativeTranscriptContentKind.Callout).Container;
        Assert.NotNull(callout);
        Assert.Equal("warning", callout.Kind, ignoreCase: true);
        Assert.Equal("Replication lag", callout.Title);
        Assert.NotEmpty(callout.Children);
    }

    /// <summary>
    /// Ensures legacy transcript outcome markers use the same semantic parser as the HTML shell.
    /// </summary>
    [Fact]
    public void Project_ConvertsOutcomeMarkerToNativeCallout() {
        var content = NativeMarkdownProjection.Project(
            "System",
            "[warning] Tool health checks need attention\n\n- LDAP probe failed\n- Graph probe is delayed");

        var callout = Assert.Single(content).Container;
        Assert.NotNull(callout);
        Assert.Equal("warning", callout.Kind);
        Assert.Equal("Tool health checks need attention", callout.Title);
        Assert.Equal("Warning", callout.Badge);
        Assert.Contains(callout.Children, item => item.Kind == NativeTranscriptContentKind.List);
    }

    /// <summary>
    /// Ensures HTML details preserve their initial disclosure state for the native Expander.
    /// </summary>
    [Theory]
    [InlineData("<details>\n<summary>More evidence</summary>\n\nHidden\n\n</details>", false)]
    [InlineData("<details open>\n<summary>More evidence</summary>\n\nVisible\n\n</details>", true)]
    public void Project_PreservesDetailsDisclosureState(string markdown, bool expectedExpanded) {
        var content = NativeMarkdownProjection.Project(markdown);

        var details = Assert.Single(content, item => item.Kind == NativeTranscriptContentKind.Details).Container;
        Assert.NotNull(details);
        Assert.Equal("More evidence", details.Title);
        Assert.Equal("Details", details.Badge);
        Assert.Equal(expectedExpanded, details.IsExpanded);
        Assert.NotEmpty(details.Children);
    }

    /// <summary>
    /// Ensures the WinUI text builder maps raised and lowered Markdown runs to distinct typography variants.
    /// </summary>
    [Fact]
    public void RichTextBuilder_MapsSuperscriptAndSubscriptToDistinctFontVariants() {
        Assert.Equal(FontVariants.Superscript, NativeTranscriptRichTextBuilder.ResolveFontVariant(MarkdownNativeInlineKind.Superscript));
        Assert.Equal(FontVariants.Subscript, NativeTranscriptRichTextBuilder.ResolveFontVariant(MarkdownNativeInlineKind.Subscript));
        Assert.Equal(FontVariants.Superscript, NativeTranscriptRichTextBuilder.ResolveFontVariant(MarkdownNativeInlineKind.FootnoteRef));
    }
}
