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

    /// <summary>
    /// Ensures structured tool failure fields are rendered in debug markdown.
    /// </summary>
    [Fact]
    public void Format_RendersFailureDescriptorAndHints() {
        var tools = new ToolRunDto {
            Calls = new[] {
                new ToolCallDto {
                    CallId = "c2",
                    Name = "ad_replication_check"
                }
            },
            Outputs = new[] {
                new ToolOutputDto {
                    CallId = "c2",
                    Output = "{}",
                    Ok = false,
                    ErrorCode = "tool_timeout",
                    Error = "Tool timed out after 60s.",
                    IsTransient = true,
                    Hints = new[] { "Narrow scope to one DC.", "Retry with a longer timeout." }
                }
            }
        };

        var markdown = ToolRunMarkdownFormatter.Format(tools, _ => "AD Replication Check");

        Assert.Contains("#### AD Replication Check", markdown);
        Assert.Contains("failure descriptor: code: `tool_timeout` | retryable: yes", markdown);
        Assert.Contains("error: Tool timed out after 60s.", markdown);
        Assert.Contains("- Narrow scope to one DC.", markdown);
        Assert.Contains("- Retry with a longer timeout.", markdown);
    }

    /// <summary>
    /// Ensures summary normalization preserves pipe-only lines when they are inside fenced code blocks.
    /// </summary>
    [Fact]
    public void Format_PreservesPipeOnlyLinesInsideFencedCode() {
        var tools = new ToolRunDto {
            Calls = new[] {
                new ToolCallDto {
                    CallId = "c3",
                    Name = "diag"
                }
            },
            Outputs = new[] {
                new ToolOutputDto {
                    CallId = "c3",
                    Output = "{}",
                    SummaryMarkdown = "```text\n|---|\n```"
                }
            }
        };

        var markdown = ToolRunMarkdownFormatter.Format(tools, _ => "Diagnostic Tool");

        Assert.Contains("```text", markdown);
        Assert.Contains("|---|", markdown);
        Assert.Contains("```", markdown);
    }
}
