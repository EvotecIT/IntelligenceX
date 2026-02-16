using IntelligenceX.Tools.Common;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public class ToolMarkdownTests {
    [Fact]
    public void SummaryFacts_ShouldBuildHeadingBulletsAndCodeBlock_WhenCodeProvided() {
        var markdown = ToolMarkdown.SummaryFacts(
            title: "Sample",
            facts: new[] {
                ("Path", @"C:\Temp\a.txt"),
                ("Truncated", "no")
            },
            codeLanguage: "text",
            codeContent: "hello");

        Assert.Contains("### Sample", markdown);
        Assert.Contains("- Path:", markdown);
        Assert.Contains("- Truncated:", markdown);
        Assert.Contains("```text", markdown);
        Assert.Contains("hello", markdown);
    }

    [Fact]
    public void SummaryFacts_ShouldBuildHeadingAndBulletsOnly_WhenCodeMissing() {
        var markdown = ToolMarkdown.SummaryFacts(
            title: "Sample",
            facts: new[] {
                ("Mode", "dry-run")
            });

        Assert.Contains("### Sample", markdown);
        Assert.Contains("- Mode:", markdown);
        Assert.DoesNotContain("```", markdown);
    }

    [Fact]
    public void SummaryText_ShouldBuildHeadingAndParagraphs() {
        var markdown = ToolMarkdown.SummaryText(
            title: "Status",
            "First paragraph.",
            "Second paragraph.");

        Assert.Contains("### Status", markdown);
        Assert.Contains("First paragraph.", markdown);
        Assert.Contains("Second paragraph.", markdown);
    }

    [Fact]
    public void CodeBlock_ShouldChooseFenceLongerThanContentRuns() {
        var markdown = ToolMarkdown.CodeBlock("text", "```\n~~~~\n`");

        Assert.StartsWith("````text", markdown, global::System.StringComparison.Ordinal);
        Assert.EndsWith("````", markdown, global::System.StringComparison.Ordinal);
        Assert.Contains("~~~~", markdown, global::System.StringComparison.Ordinal);
    }
}
