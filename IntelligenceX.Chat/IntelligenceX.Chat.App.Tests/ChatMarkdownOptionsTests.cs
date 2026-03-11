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
    public void Create_EnablesVisualsAndKeepsMinimalStrictGuards() {
        var options = ChatMarkdownOptions.Create();

        Assert.True(options.Mermaid.Enabled);
        Assert.True(options.Chart.Enabled);
        Assert.False(options.Math.Enabled);
        Assert.False(options.EnableCodeCopyButtons);
        Assert.False(options.EnableTableCopyButtons);
    }

    /// <summary>
    /// Ensures the chat host adopts Markdig-compatible reader behavior whenever the loaded OfficeIMO runtime exposes it.
    /// </summary>
    [Fact]
    public void Create_UsesMarkdigCompatibleReaderBehavior_WhenRuntimeSupportsIt() {
        var options = ChatMarkdownOptions.Create();
        var usedCapability = InvokeMarkdigCompatibleFactory();

        if (!usedCapability) {
            return;
        }

        Assert.False(options.ReaderOptions.Callouts);
        Assert.False(options.ReaderOptions.TaskLists);
        Assert.False(options.ReaderOptions.AutolinkUrls);
        Assert.False(options.ReaderOptions.AutolinkWwwUrls);
        Assert.False(options.ReaderOptions.AutolinkEmails);
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
    /// Ensures optional vis-network support is enabled when the referenced OfficeIMO renderer exposes it.
    /// </summary>
    [Fact]
    public void Create_EnablesOptionalNetworkSupport_WhenSupported() {
        var options = ChatMarkdownOptions.Create();
        var property = options.GetType().GetProperty("Network", BindingFlags.Instance | BindingFlags.Public);
        var networkOptions = property?.GetValue(options);
        var enabledProperty = networkOptions?.GetType().GetProperty("Enabled", BindingFlags.Instance | BindingFlags.Public);
        if (enabledProperty?.PropertyType != typeof(bool)) {
            return;
        }

        Assert.True((bool)(enabledProperty.GetValue(networkOptions) ?? false));
    }

    private static bool InvokeMarkdigCompatibleFactory() {
        var contractType = typeof(ChatMarkdownOptions).Assembly.GetType("IntelligenceX.Chat.App.OfficeImoMarkdownRuntimeContract", throwOnError: true);
        var method = contractType!.GetMethod(
            "TryCreateMarkdigCompatibleChatStrictMinimal",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        var args = new object?[] { null };
        var enabled = (bool)(method!.Invoke(null, args) ?? false);
        var created = Assert.IsType<MarkdownRendererOptions>(args[0]);
        Assert.NotNull(created);
        return enabled;
    }
}
