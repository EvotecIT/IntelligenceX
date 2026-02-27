using System;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.App.Markdown;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Tests for tool-run markdown formatting.
/// </summary>
public sealed class ToolRunMarkdownFormatterTests {
    private static int CountOccurrences(string text, string token) {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(token)) {
            return 0;
        }

        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(token, index, StringComparison.Ordinal)) >= 0) {
            count++;
            index += token.Length;
        }

        return count;
    }

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

    /// <summary>
    /// Ensures malformed output payload JSON does not block inline render-hint code fences.
    /// </summary>
    [Fact]
    public void Format_UsesInlineRenderContentWhenOutputPayloadIsMalformedJson() {
        var tools = new ToolRunDto {
            Calls = new[] {
                new ToolCallDto {
                    CallId = "c6",
                    Name = "inline_renderer"
                }
            },
            Outputs = new[] {
                new ToolOutputDto {
                    CallId = "c6",
                    Output = "not-json",
                    RenderJson = "{\"kind\":\"code\",\"language\":\"text\",\"content\":\"inline-render-content\"}"
                }
            }
        };

        var markdown = ToolRunMarkdownFormatter.Format(tools, _ => "Inline Renderer");

        Assert.Contains("```text", markdown);
        Assert.Contains("inline-render-content", markdown);
    }

    /// <summary>
    /// Ensures code render-hint content preserves leading/trailing whitespace from source payload strings.
    /// </summary>
    [Fact]
    public void Format_PreservesWhitespaceForCodeRenderHintContentPath() {
        var tools = new ToolRunDto {
            Calls = new[] {
                new ToolCallDto {
                    CallId = "c7",
                    Name = "whitespace_renderer"
                }
            },
            Outputs = new[] {
                new ToolOutputDto {
                    CallId = "c7",
                    Output = "{\"snippet\":\"  keep-leading\\nkeep-trailing  \"}",
                    RenderJson = "{\"kind\":\"code\",\"language\":\"text\",\"content_path\":\"snippet\"}"
                }
            }
        };

        var markdown = ToolRunMarkdownFormatter.Format(tools, _ => "Whitespace Renderer");

        Assert.Contains("```text", markdown);
        Assert.Contains("  keep-leading", markdown, StringComparison.Ordinal);
        Assert.Contains("keep-trailing  ", markdown, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures duplicate render-hint entries do not emit duplicate first-party visual fences.
    /// </summary>
    [Fact]
    public void Format_DeduplicatesDuplicateRenderHintEntries() {
        var tools = new ToolRunDto {
            Calls = new[] {
                new ToolCallDto {
                    CallId = "c8",
                    Name = "duplicate_visuals"
                }
            },
            Outputs = new[] {
                new ToolOutputDto {
                    CallId = "c8",
                    Output = "{\"chart_payload\":{\"type\":\"bar\",\"data\":{\"labels\":[\"A\"],\"datasets\":[{\"data\":[1]}]}}}",
                    RenderJson = "[{\"kind\":\"code\",\"language\":\"chart\",\"content_path\":\"chart_payload\"},{\"kind\":\"code\",\"language\":\"chart\",\"content_path\":\"chart_payload\"}]"
                }
            }
        };

        var markdown = ToolRunMarkdownFormatter.Format(tools, _ => "Duplicate Visuals");

        Assert.Equal(1, CountOccurrences(markdown, "```ix-chart"));
    }

    /// <summary>
    /// Ensures visual-only formatting emits first-party visual fences and excludes debug diagnostics.
    /// </summary>
    [Fact]
    public void FormatVisualsOnly_EmitsFirstPartyVisualsWithoutDebugDiagnostics() {
        var tools = new ToolRunDto {
            Calls = new[] {
                new ToolCallDto {
                    CallId = "c9",
                    Name = "visuals_only_report"
                }
            },
            Outputs = new[] {
                new ToolOutputDto {
                    CallId = "c9",
                    Output = "{\"chart\":{\"type\":\"bar\",\"data\":{\"labels\":[\"A\"],\"datasets\":[{\"data\":[1]}]}},\"rows\":[{\"name\":\"A\",\"score\":1}]}",
                    RenderJson =
                        "[{\"kind\":\"code\",\"language\":\"ix-chart\",\"content_path\":\"chart\"},{\"kind\":\"table\",\"rows_path\":\"rows\",\"columns\":[{\"key\":\"name\",\"label\":\"Name\"},{\"key\":\"score\",\"label\":\"Score\"}]},{\"kind\":\"code\",\"language\":\"text\",\"content\":\"hidden\"}]",
                    Ok = false,
                    ErrorCode = "tool_timeout",
                    Error = "debug-only error",
                    Hints = new[] { "debug-only hint" },
                    SummaryMarkdown = "### Debug Summary\n\nshould-not-leak"
                }
            }
        };

        var markdown = ToolRunMarkdownFormatter.FormatVisualsOnly(tools, _ => "Visuals Only Report");

        Assert.Contains("**Tool visuals:**", markdown);
        Assert.Contains("#### Visuals Only Report", markdown);
        Assert.Contains("```ix-chart", markdown);
        Assert.Contains("```ix-dataview", markdown);
        Assert.DoesNotContain("```text", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("failure descriptor", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("debug-only error", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("debug-only hint", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Debug Summary", markdown, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures visual-only formatting returns empty markdown when no first-party visuals are present.
    /// </summary>
    [Fact]
    public void FormatVisualsOnly_ReturnsEmptyWhenNoFirstPartyVisualsPresent() {
        var tools = new ToolRunDto {
            Calls = new[] {
                new ToolCallDto {
                    CallId = "c10",
                    Name = "text_renderer"
                }
            },
            Outputs = new[] {
                new ToolOutputDto {
                    CallId = "c10",
                    Output = "{\"snippet\":\"hello\"}",
                    RenderJson = "{\"kind\":\"code\",\"language\":\"text\",\"content_path\":\"snippet\"}",
                    SummaryMarkdown = "### Text summary"
                }
            }
        };

        var markdown = ToolRunMarkdownFormatter.FormatVisualsOnly(tools, _ => "Text Renderer");

        Assert.Equal(string.Empty, markdown);
    }

    /// <summary>
    /// Ensures visual-only formatting groups repeated call outputs under a single heading.
    /// </summary>
    [Fact]
    public void FormatVisualsOnly_GroupsRepeatedCallOutputsUnderSingleHeading() {
        var tools = new ToolRunDto {
            Calls = new[] {
                new ToolCallDto {
                    CallId = "c11",
                    Name = "visual_pack_report"
                }
            },
            Outputs = new[] {
                new ToolOutputDto {
                    CallId = "c11",
                    Output = "{\"chart_a\":{\"type\":\"bar\",\"data\":{\"labels\":[\"A\"],\"datasets\":[{\"data\":[1]}]}}}",
                    RenderJson = "{\"kind\":\"code\",\"language\":\"chart\",\"content_path\":\"chart_a\"}"
                },
                new ToolOutputDto {
                    CallId = "c11",
                    Output = "{\"chart_b\":{\"type\":\"line\",\"data\":{\"labels\":[\"B\"],\"datasets\":[{\"data\":[2]}]}}}",
                    RenderJson = "{\"kind\":\"code\",\"language\":\"chart\",\"content_path\":\"chart_b\"}"
                }
            }
        };

        var markdown = ToolRunMarkdownFormatter.FormatVisualsOnly(tools, _ => "Visual Pack Report");

        Assert.Contains("```ix-chart", markdown);
        Assert.Contains("\"type\":\"bar\"", markdown);
        Assert.Contains("\"type\":\"line\"", markdown);
        Assert.Equal(1, CountOccurrences(markdown, "#### Visual Pack Report"));
    }
}
