using IntelligenceX.Chat.App;
using System.Collections;
using System.Reflection;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Verifies markdown renderer option defaults used by the desktop chat host.
/// </summary>
public sealed class ChatMarkdownOptionsTests {
    /// <summary>
    /// Ensures strict minimal safety defaults remain in place while Mermaid is enabled.
    /// </summary>
    [Fact]
    public void Create_EnablesMermaidAndKeepsMinimalStrictGuards() {
        var options = ChatMarkdownOptions.Create();

        Assert.True(options.Mermaid.Enabled);
        Assert.False(options.Chart.Enabled);
        Assert.False(options.Math.Enabled);
        Assert.False(options.EnableCodeCopyButtons);
        Assert.False(options.EnableTableCopyButtons);
    }

    /// <summary>
    /// Ensures each options instance is independent so per-call mutations cannot leak across callers.
    /// </summary>
    [Fact]
    public void Create_ReturnsIndependentOptionInstances() {
        var first = ChatMarkdownOptions.Create();
        var second = ChatMarkdownOptions.Create();

        first.Mermaid.Enabled = false;

        Assert.True(second.Mermaid.Enabled);
    }

    /// <summary>
    /// Ensures OfficeIMO fenced code block extensions are registered when the referenced renderer exposes the API.
    /// </summary>
    [Fact]
    public void Create_RegistersIxFenceExtensions_WhenSupported() {
        var options = ChatMarkdownOptions.Create();
        var property = options.GetType().GetProperty("FencedCodeBlockRenderers", BindingFlags.Instance | BindingFlags.Public);
        if (property?.GetValue(options) is not IEnumerable renderers) {
            return;
        }

        var names = renderers
            .Cast<object>()
            .Select(renderer => renderer.GetType().GetProperty("Name", BindingFlags.Instance | BindingFlags.Public)?.GetValue(renderer) as string)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();

        Assert.Contains("IX chart", names);
        Assert.Contains("IX network", names);
    }
}
