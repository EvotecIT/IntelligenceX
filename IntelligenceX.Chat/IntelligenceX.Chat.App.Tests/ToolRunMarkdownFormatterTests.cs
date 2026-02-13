using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.App.Markdown;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Tests for tool-run markdown formatting.
/// </summary>
public sealed class ToolRunMarkdownFormatterTests {
    /// <summary>
    /// Ensures tool summaries render with headings and without list-heading corruption.
    /// </summary>
    [Fact]
    public void Format_UsesTypedHeadingsAndKeepsSummaryMarkdown() {
        var tools = new ToolRunDto {
            Calls = new[] {
                new ToolCallDto {
                    CallId = "c1",
                    Name = "ad_domain_info"
                }
            },
            Outputs = new[] {
                new ToolOutputDto {
                    CallId = "c1",
                    Output = "{}",
                    SummaryMarkdown = "### Active Directory: Domain Info\n\n|Field|Value|\n|---|---|\n|Domain|ad.evotec.xyz|"
                }
            }
        };

        var markdown = ToolRunMarkdownFormatter.Format(tools, _ => "AD Domain Info");

        Assert.Contains("**Tool outputs:**", markdown);
        Assert.Contains("#### AD Domain Info", markdown);
        Assert.Contains("### Active Directory: Domain Info", markdown);
        Assert.DoesNotContain("- **AD Domain Info**", markdown);
        Assert.Contains("|Field|Value|", markdown);
    }
}
