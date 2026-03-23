using OfficeIMO.MarkdownRenderer;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Verifies markdown renderer option defaults owned by the OfficeIMO runtime contract.
/// </summary>
public sealed class ChatMarkdownOptionsTests {
    /// <summary>
    /// Ensures strict minimal safety defaults remain in place while Mermaid is enabled.
    /// </summary>
    [Fact]
    public void CreateTranscriptRendererOptions_EnablesVisualsAndKeepsMinimalStrictGuards() {
        var options = OfficeImoMarkdownRuntimeContract.CreateTranscriptRendererOptions();

        Assert.True(options.Mermaid.Enabled);
        Assert.True(options.Chart.Enabled);
        Assert.False(options.Math.Enabled);
        Assert.False(options.EnableCodeCopyButtons);
        Assert.False(options.EnableTableCopyButtons);
    }

    /// <summary>
    /// Ensures the central contract still produces a chat-scoped surface with IntelligenceX alias support.
    /// </summary>
    [Fact]
    public void CreateTranscriptRendererOptions_ComposesChatPresentationAndIntelligenceXAliases() {
        var options = OfficeImoMarkdownRuntimeContract.CreateTranscriptRendererOptions();
        options.Chart.Enabled = true;

        var styleValue = options.HtmlOptions.Style.ToString();
        Assert.Equal("ChatAuto", styleValue);
        Assert.Equal("#omdRoot article.markdown-body", options.HtmlOptions.CssScopeSelector);

        var html = MarkdownRenderer.RenderBodyHtml("""
```ix-chart
{"type":"bar","data":{"labels":["A"],"datasets":[{"label":"Count","data":[1]}]}}
```
""", options);

        Assert.Contains("data-omd-visual-kind=\"chart\"", html, StringComparison.Ordinal);
        Assert.Contains("data-omd-visual-contract=\"v1\"", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the central transcript contract maps IntelligenceX dataview aliases onto the native OfficeIMO visual contract.
    /// </summary>
    [Fact]
    public void CreateTranscriptRendererOptions_ComposesIntelligenceXDataviewAliasesOntoNativeVisualContract() {
        var options = OfficeImoMarkdownRuntimeContract.CreateTranscriptRendererOptions();

        var html = MarkdownRenderer.RenderBodyHtml("""
```ix-dataview
{"headers":["Name","Count"],"items":[{"Name":"A","Count":1}]}
```
""", options);

        Assert.Contains("data-omd-visual-kind=\"dataview\"", html, StringComparison.Ordinal);
        Assert.Contains("data-omd-visual-contract=\"v1\"", html, StringComparison.Ordinal);
        Assert.Contains("data-omd-config-encoding=\"base64-utf8\"", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures the same renderer contract also accepts the portable generic dataview fence emitted by markdown artifact export.
    /// </summary>
    [Fact]
    public void CreateTranscriptRendererOptions_ComposesGenericDataviewFenceOntoNativeVisualContract() {
        var options = OfficeImoMarkdownRuntimeContract.CreateTranscriptRendererOptions();

        var html = MarkdownRenderer.RenderBodyHtml("""
```dataview
{"kind":"ix_tool_dataview_v1","rows":[["Name","Count"],["A","1"]]}
```
""", options);

        Assert.Contains("data-omd-visual-kind=\"dataview\"", html, StringComparison.Ordinal);
        Assert.Contains("data-omd-visual-contract=\"v1\"", html, StringComparison.Ordinal);
        Assert.Contains("data-omd-config-encoding=\"base64-utf8\"", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures each options instance is independent so per-call mutations cannot leak across callers.
    /// </summary>
    [Fact]
    public void CreateTranscriptRendererOptions_ReturnsIndependentOptionInstances() {
        var first = OfficeImoMarkdownRuntimeContract.CreateTranscriptRendererOptions();
        var second = OfficeImoMarkdownRuntimeContract.CreateTranscriptRendererOptions();

        first.Mermaid.Enabled = false;

        Assert.True(second.Mermaid.Enabled);
    }

    /// <summary>
    /// Ensures the published OfficeIMO transcript contract enables the required network visual path.
    /// </summary>
    [Fact]
    public void CreateTranscriptRendererOptions_EnablesRequiredNetworkSupport() {
        var options = OfficeImoMarkdownRuntimeContract.CreateTranscriptRendererOptions();
        Assert.True(options.Network.Enabled);
    }
}
