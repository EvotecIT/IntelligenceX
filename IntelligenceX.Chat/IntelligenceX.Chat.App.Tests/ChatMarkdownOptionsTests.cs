using System.Reflection;
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

        var styleValue = options.HtmlOptions.GetType().GetProperty("Style", BindingFlags.Instance | BindingFlags.Public)?.GetValue(options.HtmlOptions)?.ToString();
        Assert.Equal("ChatAuto", styleValue);
        Assert.Equal("#omdRoot article.markdown-body", options.HtmlOptions.CssScopeSelector);

        var html = MarkdownRenderer.RenderBodyHtml("""
```ix-chart
{"type":"bar","data":{"labels":["A"],"datasets":[{"label":"Count","data":[1]}]}}
```
""", options);

        Assert.True(
            html.Contains("data-omd-visual-kind=\"chart\"", StringComparison.Ordinal)
            || html.Contains("language-ix-chart", StringComparison.OrdinalIgnoreCase),
            "Expected the composed transcript contract to support ix-chart fences through native visuals or fenced-block fallback.");
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
    /// Ensures optional vis-network support is enabled when the referenced OfficeIMO renderer exposes it.
    /// </summary>
    [Fact]
    public void CreateTranscriptRendererOptions_EnablesOptionalNetworkSupport_WhenSupported() {
        var options = OfficeImoMarkdownRuntimeContract.CreateTranscriptRendererOptions();
        var property = options.GetType().GetProperty("Network", BindingFlags.Instance | BindingFlags.Public);
        var networkOptions = property?.GetValue(options);
        var enabledProperty = networkOptions?.GetType().GetProperty("Enabled", BindingFlags.Instance | BindingFlags.Public);
        if (enabledProperty?.PropertyType != typeof(bool)) {
            return;
        }

        Assert.True((bool)(enabledProperty.GetValue(networkOptions) ?? false));
    }
}
