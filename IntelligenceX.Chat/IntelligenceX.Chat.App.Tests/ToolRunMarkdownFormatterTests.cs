using System;
using System.Security.Cryptography;
using System.Text;
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
    /// Ensures tool table render hints emit a generic dataview payload fence with full rows.
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

        Assert.Contains("```dataview", markdown);
        Assert.Contains("\"kind\":\"ix_tool_dataview_v1\"", markdown);
        Assert.Contains("\"rows\":[[\"Rule\",\"Enabled\"],[\"rule_a\",\"true\"],[\"rule_b\",\"false\"]]", markdown);
        Assert.Contains("### TestimoX rules (preview)", markdown);
    }

    /// <summary>
    /// Ensures table render columns use case-insensitive row lookup and preserve first-match semantics when casing differs.
    /// </summary>
    [Fact]
    public void Format_TableRenderHint_UsesCaseInsensitiveRowLookupWithStableFirstMatchSemantics() {
        var tools = new ToolRunDto {
            Calls = new[] {
                new ToolCallDto {
                    CallId = "c4b",
                    Name = "testimox_rules_list"
                }
            },
            Outputs = new[] {
                new ToolOutputDto {
                    CallId = "c4b",
                    Output = "{\"rules_view\":[{\"Name\":\"First\",\"name\":\"Second\",\"STATUS\":\"OK\"}]}",
                    RenderJson = "{\"kind\":\"table\",\"rows_path\":\"rules_view\",\"columns\":[{\"key\":\"NaMe\",\"label\":\"Name\"},{\"key\":\"status\",\"label\":\"Status\"}]}"
                }
            }
        };

        var markdown = ToolRunMarkdownFormatter.Format(tools, _ => "TestimoX Rules List");

        Assert.Contains("```dataview", markdown);
        Assert.Contains("\"rows\":[[\"Name\",\"Status\"],[\"First\",\"OK\"]]", markdown);
    }

    /// <summary>
    /// Ensures table matrix fallback behavior remains stable for non-object row nodes.
    /// </summary>
    [Fact]
    public void Format_TableRenderHint_PreservesFallbackForNonObjectRows() {
        var tools = new ToolRunDto {
            Calls = new[] {
                new ToolCallDto {
                    CallId = "c4c",
                    Name = "testimox_rules_list"
                }
            },
            Outputs = new[] {
                new ToolOutputDto {
                    CallId = "c4c",
                    Output = "{\"rules_view\":[\"scalar\",[\"array-name\",\"array-status\"],{\"name\":\"ObjectName\",\"status\":\"ObjectStatus\"}]}",
                    RenderJson = "{\"kind\":\"table\",\"rows_path\":\"rules_view\",\"columns\":[{\"key\":\"name\",\"label\":\"Name\"},{\"key\":\"status\",\"label\":\"Status\"}]}"
                }
            }
        };

        var markdown = ToolRunMarkdownFormatter.Format(tools, _ => "TestimoX Rules List");

        Assert.Contains("```dataview", markdown);
        Assert.Contains("\"rows\":[[\"Name\",\"Status\"],[\"scalar\",\"\"],[\"array-name\",\"array-status\"],[\"ObjectName\",\"ObjectStatus\"]]", markdown);
    }

    /// <summary>
    /// Ensures render arrays can emit first-party visual fences and map visnetwork to generic network output.
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

        Assert.Contains("```chart", markdown);
        Assert.Contains("\"type\":\"bar\"", markdown);
        Assert.Contains("```network", markdown);
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

        Assert.Equal(1, CountOccurrences(markdown, "```chart"));
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
        Assert.Contains("```chart", markdown);
        Assert.Contains("```dataview", markdown);
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

        Assert.Contains("```chart", markdown);
        Assert.Contains("\"type\":\"bar\"", markdown);
        Assert.Contains("\"type\":\"line\"", markdown);
        Assert.Equal(1, CountOccurrences(markdown, "#### Visual Pack Report"));
    }

    /// <summary>
    /// Ensures fallback labels for missing call IDs remain backward compatible in debug formatting.
    /// </summary>
    [Fact]
    public void Format_UsesLegacyFallbackLabelWhenCallIdMissing() {
        var tools = new ToolRunDto {
            Calls = Array.Empty<ToolCallDto>(),
            Outputs = new[] {
                new ToolOutputDto {
                    CallId = string.Empty,
                    Output = "{}",
                    SummaryMarkdown = "done"
                }
            }
        };

        var markdown = ToolRunMarkdownFormatter.Format(tools, _ => "ignored");

        Assert.Contains("#### Call", markdown);
        Assert.DoesNotContain("<unknown>", markdown, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures fallback labels for missing call IDs remain backward compatible in visual-only formatting.
    /// </summary>
    [Fact]
    public void FormatVisualsOnly_UsesLegacyFallbackLabelWhenCallIdMissing() {
        var tools = new ToolRunDto {
            Calls = Array.Empty<ToolCallDto>(),
            Outputs = new[] {
                new ToolOutputDto {
                    CallId = string.Empty,
                    Output = "{\"chart\":{\"type\":\"bar\",\"data\":{\"labels\":[\"A\"],\"datasets\":[{\"data\":[1]}]}}}",
                    RenderJson = "{\"kind\":\"code\",\"language\":\"chart\",\"content_path\":\"chart\"}"
                }
            }
        };

        var markdown = ToolRunMarkdownFormatter.FormatVisualsOnly(tools, _ => "ignored");

        Assert.Contains("#### Call", markdown);
        Assert.DoesNotContain("<unknown>", markdown, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures visual-only formatting preserves first-seen call ordering across grouped headings.
    /// </summary>
    [Fact]
    public void FormatVisualsOnly_PreservesFirstSeenCallOrderAcrossGroups() {
        var tools = new ToolRunDto {
            Calls = new[] {
                new ToolCallDto {
                    CallId = "c20",
                    Name = "first_tool"
                },
                new ToolCallDto {
                    CallId = "c21",
                    Name = "second_tool"
                }
            },
            Outputs = new[] {
                new ToolOutputDto {
                    CallId = "c21",
                    Output = "{\"chart\":{\"type\":\"bar\",\"data\":{\"labels\":[\"B\"],\"datasets\":[{\"data\":[2]}]}}}",
                    RenderJson = "{\"kind\":\"code\",\"language\":\"chart\",\"content_path\":\"chart\"}"
                },
                new ToolOutputDto {
                    CallId = "c20",
                    Output = "{\"chart\":{\"type\":\"line\",\"data\":{\"labels\":[\"A\"],\"datasets\":[{\"data\":[1]}]}}}",
                    RenderJson = "{\"kind\":\"code\",\"language\":\"chart\",\"content_path\":\"chart\"}"
                }
            }
        };

        var markdown = ToolRunMarkdownFormatter.FormatVisualsOnly(
            tools,
            name => string.Equals(name, "second_tool", StringComparison.Ordinal) ? "Second Tool" : "First Tool");
        var secondIndex = markdown.IndexOf("#### Second Tool", StringComparison.Ordinal);
        var firstIndex = markdown.IndexOf("#### First Tool", StringComparison.Ordinal);

        Assert.True(secondIndex >= 0);
        Assert.True(firstIndex > secondIndex);
    }

    /// <summary>
    /// Ensures visual-only formatting deduplicates repeated visual fences across outputs within the same call group.
    /// </summary>
    [Fact]
    public void FormatVisualsOnly_DeduplicatesRepeatedFencesAcrossOutputsForSameCall() {
        var tools = new ToolRunDto {
            Calls = new[] {
                new ToolCallDto {
                    CallId = "c22",
                    Name = "dup_tool"
                }
            },
            Outputs = new[] {
                new ToolOutputDto {
                    CallId = "c22",
                    Output = "{\"chart\":{\"type\":\"bar\",\"data\":{\"labels\":[\"A\"],\"datasets\":[{\"data\":[1]}]}}}",
                    RenderJson = "{\"kind\":\"code\",\"language\":\"chart\",\"content_path\":\"chart\"}"
                },
                new ToolOutputDto {
                    CallId = "c22",
                    Output = "{\"chart\":{\"type\":\"bar\",\"data\":{\"labels\":[\"A\"],\"datasets\":[{\"data\":[1]}]}}}",
                    RenderJson = "{\"kind\":\"code\",\"language\":\"chart\",\"content_path\":\"chart\"}"
                }
            }
        };

        var markdown = ToolRunMarkdownFormatter.FormatVisualsOnly(tools, _ => "unused");

        Assert.Equal(1, CountOccurrences(markdown, "```chart"));
    }

    /// <summary>
    /// Ensures debug formatting falls back to call-based labels when resolved display names are blank.
    /// </summary>
    [Fact]
    public void Format_FallsBackToCallLabelWhenResolvedDisplayNameIsBlank() {
        var tools = new ToolRunDto {
            Calls = new[] {
                new ToolCallDto {
                    CallId = "c23",
                    Name = "blank_name_tool"
                }
            },
            Outputs = new[] {
                new ToolOutputDto {
                    CallId = "c23",
                    Output = "{}",
                    SummaryMarkdown = "done"
                }
            }
        };

        var markdown = ToolRunMarkdownFormatter.Format(tools, _ => "   ");

        Assert.Contains("#### Call c23", markdown);
    }

    /// <summary>
    /// Ensures duplicate large render-hint payloads are still deduplicated in debug formatting.
    /// </summary>
    [Fact]
    public void Format_DeduplicatesLargeRenderHintPayloads() {
        var largeSnippet = new string('x', 12000);
        var tools = new ToolRunDto {
            Calls = new[] {
                new ToolCallDto {
                    CallId = "c24",
                    Name = "large_payload_tool"
                }
            },
            Outputs = new[] {
                new ToolOutputDto {
                    CallId = "c24",
                    Output = "{\"snippet\":\"" + largeSnippet + "\"}",
                    RenderJson =
                        "[{\"kind\":\"code\",\"language\":\"text\",\"content_path\":\"snippet\"},{\"kind\":\"code\",\"language\":\"text\",\"content_path\":\"snippet\"}]"
                }
            }
        };

        var markdown = ToolRunMarkdownFormatter.Format(tools, _ => "Large Payload Tool");

        Assert.Equal(1, CountOccurrences(markdown, "```text"));
        Assert.Contains(largeSnippet, markdown, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures chunked UTF-8 hashing remains equivalent to canonical UTF-8 SHA-256 for surrogate-boundary content.
    /// </summary>
    [Fact]
    public void ComputeUtf8Sha256Hex_MatchesCanonicalHashForSurrogateBoundaryContent() {
        var value = new string('a', 1023) + "😀" + new string('b', 1024);
        var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
        var actual = ToolRunMarkdownFormatter.ComputeUtf8Sha256Hex(value);

        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Ensures chunked UTF-8 hashing matches canonical SHA-256 for empty input.
    /// </summary>
    [Fact]
    public void ComputeUtf8Sha256Hex_MatchesCanonicalHashForEmptyInput() {
        var expected = Convert.ToHexString(SHA256.HashData(Array.Empty<byte>()));
        var actual = ToolRunMarkdownFormatter.ComputeUtf8Sha256Hex(string.Empty);

        Assert.Equal(expected, actual);
    }
}
