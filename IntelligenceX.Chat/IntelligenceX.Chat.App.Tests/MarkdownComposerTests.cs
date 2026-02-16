using IntelligenceX.Chat.App.Markdown;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Tests for typed markdown composition helpers.
/// </summary>
public sealed class MarkdownComposerTests {
    /// <summary>
    /// Ensures typed nodes render stable markdown output.
    /// </summary>
    [Fact]
    public void Build_RendersHeadingQuoteBulletAndCodeFence() {
        var markdown = new MarkdownComposer()
            .Heading("Section", 3)
            .Paragraph("Body line")
            .Quote("error: test")
            .Bullet("done")
            .BlankLine()
            .CodeFence("json", "{\"ok\":true}")
            .Build();

        Assert.Contains("### Section", markdown);
        Assert.Contains("Body line", markdown);
        Assert.Contains("> error: test", markdown);
        Assert.Contains("- done", markdown);
        Assert.Contains("```json", markdown);
        Assert.Contains("{\"ok\":true}", markdown);
        Assert.EndsWith("```", markdown);
    }

    /// <summary>
    /// Ensures code fence rendering chooses a fence that does not collide with content runs.
    /// </summary>
    [Fact]
    public void Build_CodeFenceChoosesSafeFenceWhenContentContainsBackticksAndTildes() {
        var markdown = new MarkdownComposer()
            .CodeFence("text", "```\n~~~~")
            .Build();

        Assert.StartsWith("````text", markdown, System.StringComparison.Ordinal);
        Assert.EndsWith("````", markdown, System.StringComparison.Ordinal);
        Assert.Contains("~~~~", markdown, System.StringComparison.Ordinal);
    }
}
