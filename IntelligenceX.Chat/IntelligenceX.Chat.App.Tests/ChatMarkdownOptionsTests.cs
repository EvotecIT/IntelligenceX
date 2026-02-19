using IntelligenceX.Chat.App;
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
}
