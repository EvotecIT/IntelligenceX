using System;
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

    /// <summary>
    /// Ensures tool table render hints emit an ix-dataview payload with full rows.
    /// </summary>
    [Fact]
    public void Format_EmbedsDataViewPayloadFenceForTableRenderHints() {
        var tools = new ToolRunDto {
            Calls = new[] {
                new ToolCallDto {
                    CallId = "c4",
                    Name = "testimox_rules_list"
                }
            },
            Outputs = new[] {
                new ToolOutputDto {
                    CallId = "c4",
                    Output = "{\"ok\":true,\"rules_view\":[{\"rule_name\":\"rule_a\",\"enabled\":true},{\"rule_name\":\"rule_b\",\"enabled\":false}]}",
                    RenderJson = "{\"kind\":\"table\",\"rows_path\":\"rules_view\",\"columns\":[{\"key\":\"rule_name\",\"label\":\"Rule\"},{\"key\":\"enabled\",\"label\":\"Enabled\"}]}",
                    SummaryMarkdown = "### TestimoX rules (preview)\n\n| Rule | Enabled |\n| --- | --- |\n| rule_a | true |"
                }
            }
        };

        var markdown = ToolRunMarkdownFormatter.Format(tools, _ => "TestimoX Rules List");

        Assert.Contains("```ix-dataview", markdown);
        Assert.Contains("\"kind\":\"ix_tool_dataview_v1\"", markdown);
        Assert.Contains("\"rows\":[[\"Rule\",\"Enabled\"],[\"rule_a\",\"true\"],[\"rule_b\",\"false\"]]", markdown);
        Assert.Contains("### TestimoX rules (preview)", markdown);
    }

    /// <summary>
    /// Ensures render arrays can emit first-party visual fences and map visnetwork to ix-network.
    /// </summary>
    [Fact]
    public void Format_EmitsVisualFencesFromRenderArrayAndSkipsCompletedFallback() {
        var tools = new ToolRunDto {
            Calls = new[] {
                new ToolCallDto {
                    CallId = "c5",
                    Name = "visual_pack_report"
                }
            },
            Outputs = new[] {
                new ToolOutputDto {
                    CallId = "c5",
                    Output =
                        "{\"ok\":true,\"chart_payload\":{\"type\":\"bar\",\"data\":{\"labels\":[\"A\"],\"datasets\":[{\"data\":[1]}]}},\"network_payload\":{\"nodes\":[{\"id\":1,\"label\":\"A\"}],\"edges\":[]}}",
                    RenderJson =
                        "[{\"kind\":\"code\",\"language\":\"chart\",\"content_path\":\"chart_payload\"},{\"kind\":\"code\",\"language\":\"visnetwork\",\"content_path\":\"network_payload\"}]"
                }
            }
        };

        var markdown = ToolRunMarkdownFormatter.Format(tools, _ => "Visual Pack Report");

        Assert.Contains("```ix-chart", markdown);
        Assert.Contains("\"type\":\"bar\"", markdown);
        Assert.Contains("```ix-network", markdown);
        Assert.Contains("\"nodes\":[{\"id\":1,\"label\":\"A\"}]", markdown);
        Assert.DoesNotContain("\ncompleted\n", markdown, StringComparison.OrdinalIgnoreCase);
    }
}
