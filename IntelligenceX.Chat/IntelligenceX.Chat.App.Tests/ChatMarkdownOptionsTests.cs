using IntelligenceX.Chat.App;
using OfficeIMO.MarkdownRenderer;
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
        Assert.True(options.Chart.Enabled);
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
    /// Ensures newly available OfficeIMO normalization flags are enabled when the referenced renderer exposes them.
    /// </summary>
    [Fact]
    public void Create_EnablesOptionalOfficeImoNormalizationFlags_WhenAvailable() {
        var options = ChatMarkdownOptions.Create();

        AssertOptionalBooleanPropertyEnabled(options, "NormalizeTightArrowStrongBoundaries");
        AssertOptionalBooleanPropertyEnabled(options, "NormalizeTightColonSpacing");
    }

    /// <summary>
    /// Ensures optional OfficeIMO network support is enabled when the referenced renderer exposes it.
    /// </summary>
    [Fact]
    public void Create_EnablesOptionalOfficeImoNetworkSupport_WhenAvailable() {
        var options = ChatMarkdownOptions.Create();
        var networkProperty = typeof(MarkdownRendererOptions).GetProperty("Network", BindingFlags.Instance | BindingFlags.Public);
        if (networkProperty is null) {
            return;
        }

        var networkOptions = networkProperty.GetValue(options);
        Assert.NotNull(networkOptions);
        AssertOptionalBooleanPropertyEnabled(networkOptions!, "Enabled");
    }

    private static void AssertOptionalBooleanPropertyEnabled(object target, string propertyName) {
        var property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        if (property?.PropertyType != typeof(bool)) {
            return;
        }

        Assert.Equal(true, property.GetValue(target));
    }
}
