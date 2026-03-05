using System;
using System.Collections.Generic;
using IntelligenceX.Chat.App;
using IntelligenceX.Chat.App.Rendering;
using OfficeIMO.MarkdownRenderer;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Tests for transcript HTML rendering.
/// </summary>
public sealed class TranscriptHtmlFormatterTests {
    /// <summary>
    /// Ensures Mermaid fenced blocks are converted to Mermaid runtime placeholders when transcript rendering enables visuals.
    /// </summary>
    [Fact]
    public void Format_RendersMermaidFenceAsMermaidBlockWhenEnabled() {
        var options = ChatMarkdownOptions.Create();
        var now = new DateTime(2026, 2, 19, 14, 41, 2, DateTimeKind.Local);
        var html = TranscriptHtmlFormatter.Format(new[] {
            ("Assistant", """
                          Relationship preview:
                          ```mermaid
                          flowchart LR
                            A[User] --> B[Group]
                          ```
                          End of preview.
                          """, now)
        }, "HH:mm:ss", options);

        Assert.Contains("class=\"mermaid\"", html, StringComparison.Ordinal);
        Assert.Contains("data-mermaid-hash=", html, StringComparison.Ordinal);
        Assert.DoesNotContain("language-mermaid", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("flowchart LR", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures ix-chart fenced blocks either render natively through OfficeIMO or remain as host-rendered fences.
    /// </summary>
    [Fact]
    public void Format_SupportsIxChartViaRendererOrHostRuntime() {
        var options = ChatMarkdownOptions.Create();
        var now = new DateTime(2026, 2, 20, 9, 12, 45, DateTimeKind.Local);
        var html = TranscriptHtmlFormatter.Format(new[] {
            ("Assistant", """
                          Chart preview:
                          ```ix-chart
                          {"type":"bar","data":{"labels":["A"],"datasets":[{"label":"X","data":[1]}]}}
                          ```
                          Interpretation line.
                          """, now)
        }, "HH:mm:ss", options);

        Assert.Contains("Chart preview:", html, StringComparison.Ordinal);
        Assert.True(
            html.Contains("class=\"omd-chart\"", StringComparison.Ordinal)
            || html.Contains("language-ix-chart", StringComparison.Ordinal),
            "Expected OfficeIMO-native chart HTML or the legacy ix-chart code fence.");
    }

    /// <summary>
    /// Ensures ix-network fenced blocks either render natively through OfficeIMO or remain as host-rendered fences.
    /// </summary>
    [Fact]
    public void Format_SupportsIxNetworkViaRendererOrHostRuntime() {
        var options = ChatMarkdownOptions.Create();
        var now = new DateTime(2026, 2, 20, 9, 16, 22, DateTimeKind.Local);
        var html = TranscriptHtmlFormatter.Format(new[] {
            ("Assistant", """
                          Relationship network:
                          ```ix-network
                          {"nodes":[{"id":"A","label":"User"},{"id":"B","label":"Group"}],"edges":[{"from":"A","to":"B","label":"memberOf"}]}
                          ```
                          Interpretation line.
                          """, now)
        }, "HH:mm:ss", options);

        Assert.Contains("Relationship network:", html, StringComparison.Ordinal);
        Assert.True(
            html.Contains("class=\"omd-network\"", StringComparison.Ordinal)
            || html.Contains("language-ix-network", StringComparison.Ordinal),
            "Expected OfficeIMO-native network HTML or the legacy ix-network code fence.");
    }

    /// <summary>
    /// Ensures rows, role styling, continuation classes, and copy indexes are rendered.
    /// </summary>
    [Fact]
    public void Format_RendersRoleRowsAndContinuationMarkers() {
        var options = MarkdownRendererPresets.CreateChatStrictMinimal();
        var now = new DateTime(2026, 2, 11, 18, 45, 31, DateTimeKind.Local);
        var html = TranscriptHtmlFormatter.Format(new[] {
            ("Assistant", "first", now),
            ("Assistant", "second", now.AddSeconds(2)),
            ("User", "hello", now.AddSeconds(3)),
            ("Tools", "tool output", now.AddSeconds(4)),
            ("System", "notice", now.AddSeconds(5))
        }, "HH:mm:ss", options);

        Assert.Contains("msg-row assistant", html);
        Assert.Contains("msg-row assistant cont", html);
        Assert.Contains("msg-row user", html);
        Assert.Contains("msg-row tools", html);
        Assert.Contains("msg-row system", html);
        Assert.Contains("class='meta hidden'", html);
        Assert.Contains("data-msg-index='0'", html);
        Assert.Contains("data-msg-index='1'", html);
        Assert.Contains("data-msg-index='2'", html);
        Assert.Contains("data-msg-index='3'", html);
        Assert.Contains("data-msg-index='4'", html);
        Assert.Contains("IntelligenceX &middot; 18:45:31", html);
        Assert.Contains("You &middot; 18:45:34", html);
    }

    /// <summary>
    /// Ensures assistant responses render an inline model badge when model metadata is available.
    /// </summary>
    [Fact]
    public void Format_RendersAssistantModelBadgeWhenProvided() {
        var options = MarkdownRendererPresets.CreateChatStrictMinimal();
        var now = new DateTime(2026, 2, 20, 20, 31, 6, DateTimeKind.Local);
        var html = TranscriptHtmlFormatter.Format(new[] {
            ("User", "hello", now, null),
            ("Assistant", "hi", now.AddSeconds(1), "openai/gpt-oss-20b")
        }, "HH:mm:ss", options);

        Assert.Contains("bubble-model-chip", html, StringComparison.Ordinal);
        Assert.Contains(">openai/gpt-oss-20b</span>", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures assistant streaming decorations render provisional styling and timeline trace details.
    /// </summary>
    [Fact]
    public void Format_RendersAssistantTurnTraceAndProvisionalStateWhenDecorationsProvided() {
        var options = MarkdownRendererPresets.CreateChatStrictMinimal();
        var now = new DateTime(2026, 2, 22, 20, 18, 6, DateTimeKind.Local);
        var messages = new (string Role, string Text, DateTime Time, string? Model)[] {
            ("Assistant", "Running checks...", now, "gpt-5.3-codex")
        };
        var html = TranscriptHtmlFormatter.Format(
            messages,
            "HH:mm:ss",
            options,
            new Dictionary<int, TranscriptMessageDecoration> {
                [0] = new TranscriptMessageDecoration {
                    IsProvisional = true,
                    Timeline = new[] { "plan", "execute", "review" }
                }
            });

        Assert.Contains("bubble-provisional", html, StringComparison.Ordinal);
        Assert.Contains("msg-row assistant assistant-draft", html, StringComparison.Ordinal);
        Assert.Contains("assistant-draft-meta-pill", html, StringComparison.Ordinal);
        Assert.Contains("Draft/Thinking", html, StringComparison.Ordinal);
        Assert.Contains("assistant-turn-live-pill", html, StringComparison.Ordinal);
        Assert.Contains("assistant-turn-trace-list", html, StringComparison.Ordinal);
        Assert.Contains(">plan</li>", html, StringComparison.Ordinal);
        Assert.Contains(">execute</li>", html, StringComparison.Ordinal);
        Assert.Contains(">review</li>", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures tool-activity decorations render a distinct channel style and trace affordance.
    /// </summary>
    [Fact]
    public void Format_RendersToolActivityChannelWhenDecorationRequestsIt() {
        var options = MarkdownRendererPresets.CreateChatStrictMinimal();
        var now = new DateTime(2026, 2, 23, 7, 41, 0, DateTimeKind.Local);
        var messages = new (string Role, string Text, DateTime Time, string? Model)[] {
            ("Assistant", "Running cross-DC checks...", now, "gpt-5.3-codex")
        };
        var html = TranscriptHtmlFormatter.Format(
            messages,
            "HH:mm:ss",
            options,
            new Dictionary<int, TranscriptMessageDecoration> {
                [0] = new TranscriptMessageDecoration {
                    Channel = AssistantBubbleChannelKind.ToolActivity,
                    Timeline = new[] { "run Eventlog Live Query", "done Eventlog Live Query" }
                }
            });

        Assert.Contains("assistant-tool-meta-pill", html, StringComparison.Ordinal);
        Assert.Contains("Tool Activity", html, StringComparison.Ordinal);
        Assert.Contains("bubble-tool-activity", html, StringComparison.Ordinal);
        Assert.Contains("assistant-turn-live-pill'>Tool</span>", html, StringComparison.Ordinal);
        Assert.Contains("assistant-turn-trace-title'>Tool trace</span>", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures trace sections can be suppressed while keeping provisional bubble styling.
    /// </summary>
    [Fact]
    public void Format_DoesNotRenderAssistantTurnTraceWhenDisabled() {
        var options = MarkdownRendererPresets.CreateChatStrictMinimal();
        var now = new DateTime(2026, 2, 22, 20, 18, 6, DateTimeKind.Local);
        var messages = new (string Role, string Text, DateTime Time, string? Model)[] {
            ("Assistant", "Running checks...", now, "gpt-5.3-codex")
        };
        var html = TranscriptHtmlFormatter.Format(
            messages,
            "HH:mm:ss",
            options,
            new Dictionary<int, TranscriptMessageDecoration> {
                [0] = new TranscriptMessageDecoration {
                    IsProvisional = true,
                    Timeline = new[] { "plan", "execute", "review" }
                }
            },
            showAssistantTurnTrace: false);

        Assert.Contains("bubble-provisional", html, StringComparison.Ordinal);
        Assert.DoesNotContain("assistant-turn-trace", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures provisional assistant draft bubbles can be hidden while final responses remain available.
    /// </summary>
    [Fact]
    public void Format_HidesAssistantDraftBubbleWhenDraftVisibilityDisabled() {
        var options = MarkdownRendererPresets.CreateChatStrictMinimal();
        var now = new DateTime(2026, 2, 23, 19, 12, 0, DateTimeKind.Local);
        var messages = new (string Role, string Text, DateTime Time, string? Model)[] {
            ("User", "Run check", now, null),
            ("Assistant", "Running checks...", now.AddSeconds(1), "gpt-5.3-codex"),
            ("Assistant", "Done.", now.AddSeconds(2), "gpt-5.3-codex")
        };
        var html = TranscriptHtmlFormatter.Format(
            messages,
            "HH:mm:ss",
            options,
            new Dictionary<int, TranscriptMessageDecoration> {
                [1] = new TranscriptMessageDecoration {
                    IsProvisional = true,
                    Timeline = new[] { "plan", "execute" }
                }
            },
            showAssistantTurnTrace: true,
            showAssistantDraftBubbles: false);

        Assert.DoesNotContain("Running checks...", html, StringComparison.Ordinal);
        Assert.DoesNotContain("bubble-provisional", html, StringComparison.Ordinal);
        Assert.DoesNotContain("assistant-draft-meta-pill", html, StringComparison.Ordinal);
        Assert.Contains("Done.", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures draft continuations keep visible metadata and never collapse into continuation rows.
    /// </summary>
    [Fact]
    public void Format_KeepsDraftMetadataVisibleAcrossStreamingContinuations() {
        var options = MarkdownRendererPresets.CreateChatStrictMinimal();
        var now = new DateTime(2026, 2, 23, 22, 5, 0, DateTimeKind.Local);
        var messages = new (string Role, string Text, DateTime Time, string? Model)[] {
            ("Assistant", "draft-one", now, "gpt-5.3-codex"),
            ("Assistant", "draft-two", now.AddSeconds(1), "gpt-5.3-codex")
        };
        var html = TranscriptHtmlFormatter.Format(
            messages,
            "HH:mm:ss",
            options,
            new Dictionary<int, TranscriptMessageDecoration> {
                [0] = new TranscriptMessageDecoration {
                    IsProvisional = true,
                    Timeline = new[] { "thinking" }
                },
                [1] = new TranscriptMessageDecoration {
                    IsProvisional = true,
                    Timeline = new[] { "review" }
                }
            },
            showAssistantTurnTrace: true,
            showAssistantDraftBubbles: true);

        Assert.DoesNotContain("msg-row assistant assistant-draft cont", html, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(html, "assistant-draft-meta-pill"));
        Assert.Equal(2, CountOccurrences(html, "Draft/Thinking"));
    }

    /// <summary>
    /// Ensures trace rendering keeps only the latest bounded timeline items as defense-in-depth.
    /// </summary>
    [Fact]
    public void Format_CapsAssistantTurnTraceTimelineToBoundedEntryCount() {
        var options = MarkdownRendererPresets.CreateChatStrictMinimal();
        var now = new DateTime(2026, 2, 23, 21, 6, 0, DateTimeKind.Local);
        var messages = new (string Role, string Text, DateTime Time, string? Model)[] {
            ("Assistant", "Status snapshot.", now, "gpt-5.3-codex")
        };
        var html = TranscriptHtmlFormatter.Format(
            messages,
            "HH:mm:ss",
            options,
            new Dictionary<int, TranscriptMessageDecoration> {
                [0] = new TranscriptMessageDecoration {
                    Timeline = new[] {
                        "step-1", "step-2", "step-3", "step-4", "step-5", "step-6",
                        "step-7", "step-8", "step-9", "step-10", "step-11", "step-12"
                    }
                }
            });

        Assert.Contains("assistant-turn-trace-count'>8</span>", html, StringComparison.Ordinal);
        Assert.DoesNotContain(">step-1</li>", html, StringComparison.Ordinal);
        Assert.DoesNotContain(">step-2</li>", html, StringComparison.Ordinal);
        Assert.Contains(">step-5</li>", html, StringComparison.Ordinal);
        Assert.Contains(">step-12</li>", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures capped trace rendering still fills with older non-empty items when trailing entries are blank.
    /// </summary>
    [Fact]
    public void Format_CapsAssistantTurnTraceTimelineUsingLastNonEmptyEntries() {
        var options = MarkdownRendererPresets.CreateChatStrictMinimal();
        var now = new DateTime(2026, 2, 23, 21, 12, 0, DateTimeKind.Local);
        var messages = new (string Role, string Text, DateTime Time, string? Model)[] {
            ("Assistant", "Status snapshot.", now, "gpt-5.3-codex")
        };
        var html = TranscriptHtmlFormatter.Format(
            messages,
            "HH:mm:ss",
            options,
            new Dictionary<int, TranscriptMessageDecoration> {
                [0] = new TranscriptMessageDecoration {
                    Timeline = new[] {
                        "step-1", "step-2", "step-3", "step-4", "step-5", "step-6",
                        "step-7", "step-8", "step-9", "", " ", "\t"
                    }
                }
            });

        Assert.Contains("assistant-turn-trace-count'>8</span>", html, StringComparison.Ordinal);
        Assert.DoesNotContain(">step-1</li>", html, StringComparison.Ordinal);
        Assert.Contains(">step-2</li>", html, StringComparison.Ordinal);
        Assert.Contains(">step-9</li>", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures non-assistant rows ignore assistant-only transcript decorations.
    /// </summary>
    [Fact]
    public void Format_IgnoresAssistantDecorationsForNonAssistantMessages() {
        var options = MarkdownRendererPresets.CreateChatStrictMinimal();
        var now = new DateTime(2026, 2, 22, 20, 20, 0, DateTimeKind.Local);
        var messages = new (string Role, string Text, DateTime Time, string? Model)[] {
            ("User", "hello", now, null)
        };
        var html = TranscriptHtmlFormatter.Format(
            messages,
            "HH:mm:ss",
            options,
            new Dictionary<int, TranscriptMessageDecoration> {
                [0] = new TranscriptMessageDecoration {
                    IsProvisional = true,
                    Timeline = new[] { "ignored" }
                }
            });

        Assert.DoesNotContain("bubble-provisional", html, StringComparison.Ordinal);
        Assert.DoesNotContain("assistant-turn-trace", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures repeated system rows never collapse metadata visibility.
    /// </summary>
    [Fact]
    public void Format_DoesNotHideMetaForSystemContinuations() {
        var options = MarkdownRendererPresets.CreateChatStrictMinimal();
        var now = new DateTime(2026, 2, 17, 14, 22, 0, DateTimeKind.Local);
        var html = TranscriptHtmlFormatter.Format(new[] {
            ("System", "Restarting local runtime...", now),
            ("System", "Could not connect to local runtime.", now.AddSeconds(3))
        }, "yyyy-MM-dd HH:mm:ss", options);

        Assert.Contains("msg-row system", html);
        Assert.DoesNotContain("msg-row system cont", html);
        Assert.DoesNotContain("class='meta hidden'", html);
        Assert.Contains("System &middot; 2026-02-17 14:22:00", html);
        Assert.Contains("System &middot; 2026-02-17 14:22:03", html);
    }

    /// <summary>
    /// Ensures blank transcript text is skipped while preserving index progression.
    /// </summary>
    [Fact]
    public void Format_SkipsBlankMessagesButKeepsIndexProgression() {
        var options = MarkdownRendererPresets.CreateChatStrictMinimal();
        var now = new DateTime(2026, 2, 11, 19, 0, 0, DateTimeKind.Local);
        var html = TranscriptHtmlFormatter.Format(new[] {
            ("Assistant", "a", now),
            ("Assistant", "   ", now.AddSeconds(1)),
            ("Assistant", "b", now.AddSeconds(2))
        }, "HH:mm:ss", options);

        Assert.Contains("data-msg-index='0'", html);
        Assert.Contains("data-msg-index='2'", html);
        Assert.DoesNotContain("data-msg-index='1'", html);
    }

    /// <summary>
    /// Ensures assistant error outcomes render as structured callout cards.
    /// </summary>
    [Fact]
    public void Format_RendersAssistantErrorAsCalloutCard() {
        var options = MarkdownRendererPresets.CreateChatStrictMinimal();
        var now = new DateTime(2026, 2, 11, 19, 10, 0, DateTimeKind.Local);
        var html = TranscriptHtmlFormatter.Format(new[] {
            ("Assistant", "[error] Chat failed\n\nChatGPT usage limit reached. Try again later.", now)
        }, "HH:mm:ss", options);

        Assert.Contains("bubble bubble-callout", html);
        Assert.Contains("outcome-card outcome-error", html);
        Assert.Contains("outcome-badge'>Error</span>", html);
        Assert.Contains("outcome-title'>Chat failed</span>", html);
        Assert.Contains("usage limit reached", html, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures system warnings use the same callout card styling as assistant outcomes.
    /// </summary>
    [Fact]
    public void Format_RendersSystemWarningAsCalloutCard() {
        var options = MarkdownRendererPresets.CreateChatStrictMinimal();
        var now = new DateTime(2026, 2, 16, 18, 2, 0, DateTimeKind.Local);
        var html = TranscriptHtmlFormatter.Format(new[] {
            ("System", "[warning] Tool health checks need attention\n\nFound 1 startup warning.", now)
        }, "HH:mm:ss", options);

        Assert.Contains("msg-row system", html);
        Assert.Contains("bubble bubble-callout", html);
        Assert.Contains("outcome-card outcome-warn", html);
        Assert.Contains("outcome-badge'>Warning</span>", html);
        Assert.Contains("Tool health checks need attention", html);
    }

    /// <summary>
    /// Ensures transcript rendering normalizes common token-join artifacts before markdown conversion.
    /// </summary>
    [Fact]
    public void Format_NormalizesCommonMarkdownSpacingArtifacts() {
        var options = MarkdownRendererPresets.CreateChatStrictMinimal();
        var now = new DateTime(2026, 2, 13, 19, 25, 24, DateTimeKind.Local);
        var html = TranscriptHtmlFormatter.Format(new[] {
            ("Assistant", "✅I can run 1) LDAP checks, or2) cert checks.", now)
        }, "HH:mm:ss", options);

        Assert.Contains("✅ I", html);
        Assert.Contains("or 2)", html);
        Assert.DoesNotContain("✅I", html);
        Assert.DoesNotContain("or2)", html);
    }

    /// <summary>
    /// Ensures malformed collapsed status metrics render as proper bullet rows rather than literal markdown markers.
    /// </summary>
    [Fact]
    public void Format_RepairsCollapsedStatusMetricMarkdownBeforeRender() {
        var options = MarkdownRendererPresets.CreateChatStrictMinimal();
        var now = new DateTime(2026, 2, 13, 19, 25, 24, DateTimeKind.Local);
        var html = TranscriptHtmlFormatter.Format(new[] {
            ("Assistant", "**Status: HEALTHY** - **Servers checked:**5 -**Replication edges:**62 -*Failed edges:**0 -*Stale edges (>24h):**0 - **Servers with failures:**0", now)
        }, "HH:mm:ss", options);

        Assert.Contains("Status <strong>HEALTHY</strong>", html);
        Assert.Contains("<li>Servers checked <strong>5</strong></li>", html);
        Assert.Contains("<li>Replication edges <strong>62</strong></li>", html);
        Assert.Contains("<li>Failed edges <strong>0</strong></li>", html);
        Assert.Contains("<li>Stale edges (&gt;24h) <strong>0</strong></li>", html);
        Assert.Contains("<li>Servers with failures <strong>0</strong></li>", html);
        Assert.DoesNotContain("**Servers checked:**", html);
        Assert.DoesNotContain("**Replication edges:**", html);
    }

    private static int CountOccurrences(string value, string token) {
        if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(token)) {
            return 0;
        }

        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(token, index, StringComparison.Ordinal)) >= 0) {
            count++;
            index += token.Length;
        }

        return count;
    }

    /// <summary>
    /// Ensures pending action markdown summary lines render as actionable chips instead of raw /act text lines.
    /// </summary>
    [Fact]
    public void Format_RendersPendingActionsAsActionChips() {
        var options = MarkdownRendererPresets.CreateChatStrictMinimal();
        var now = new DateTime(2026, 2, 15, 20, 36, 14, DateTimeKind.Local);
        var html = TranscriptHtmlFormatter.Format(new[] {
            ("Assistant", """
                          You can run one of these follow-up actions:
                          1. Run failed logon report (4625) on ADO Security (`/act act_failed4625`)
                          2. Pull account lockout events (4740) (`/act act_lockout4740`)
                          """, now)
        }, "HH:mm:ss", options);

        Assert.Contains("ix-action-cta", html);
        Assert.Contains("class='ix-action-btn'", html);
        Assert.Contains("data-act-cmd='/act act_failed4625'", html);
        Assert.Contains("data-act-cmd='/act act_lockout4740'", html);
        Assert.DoesNotContain("You can run one of these follow-up actions:", html);
        Assert.DoesNotContain("`/act act_failed4625`", html);
    }

    /// <summary>
    /// Ensures /act-looking lines inside fenced code blocks are not converted into actionable chips.
    /// </summary>
    [Fact]
    public void Format_DoesNotExtractPendingActionsFromFencedCode() {
        var options = MarkdownRendererPresets.CreateChatStrictMinimal();
        var now = new DateTime(2026, 2, 16, 8, 15, 0, DateTimeKind.Local);
        var html = TranscriptHtmlFormatter.Format(new[] {
            ("Assistant", """
                          Example script:
                          ~~~text
                          1. Dangerous demo (`/act act_danger`)
                          ~~~
                          You can run one of these follow-up actions:
                          1. Safe action (`/act act_safe`)
                          """, now)
        }, "HH:mm:ss", options);

        Assert.Contains("data-act-cmd='/act act_safe'", html);
        Assert.DoesNotContain("data-act-cmd='/act act_danger'", html);
    }

    /// <summary>
    /// Ensures inline backtick spans always end up as code tags in transcript HTML.
    /// </summary>
    [Fact]
    public void Format_AlwaysRendersInlineBackticksAsCodeTags() {
        var options = MarkdownRendererPresets.CreateChatStrictMinimal();
        var now = new DateTime(2026, 2, 15, 23, 18, 6, DateTimeKind.Local);
        var html = TranscriptHtmlFormatter.Format(new[] {
            ("Assistant", "Use `/act act_ad0_sys3210_msg` to run it.", now)
        }, "HH:mm:ss", options);

        Assert.Contains("<code>/act act_ad0_sys3210_msg</code>", html);
        Assert.DoesNotContain("`/act act_ad0_sys3210_msg`", html);
    }

    /// <summary>
    /// Ensures common strong-emphasis phrases from assistant prose render as strong tags, not literal markers.
    /// </summary>
    [Fact]
    public void Format_RendersCommonStrongPhrasesWithoutLiteralMarkers() {
        var options = MarkdownRendererPresets.CreateChatStrictMinimal();
        var now = new DateTime(2026, 2, 16, 13, 6, 34, DateTimeKind.Local);
        var html = TranscriptHtmlFormatter.Format(new[] {
            ("Assistant", "If you want, I can run a **“Top 8 high-signal security pack”** now, or list only **GPO-related** reports.", now),
            ("Assistant", "Those were ** unresolved privileged SIDs** in a group sweep.", now.AddSeconds(1))
        }, "HH:mm:ss", options);

        Assert.Contains("<strong>“Top 8 high-signal security pack”</strong>", html);
        Assert.Contains("<strong>GPO-related</strong>", html);
        Assert.Contains("<strong>unresolved privileged SIDs</strong>", html);
        Assert.DoesNotContain("**“Top 8 high-signal security pack”**", html);
        Assert.DoesNotContain("**GPO-related**", html);
        Assert.DoesNotContain("** unresolved privileged SIDs**", html);
    }

    /// <summary>
    /// Ensures malformed compact ordered menu text is repaired before HTML rendering so literal markdown markers are not visible.
    /// </summary>
    [Fact]
    public void Format_RepairsCollapsedOrderedMenuWithoutLiteralMarkers() {
        var options = MarkdownRendererPresets.CreateChatStrictMinimal();
        var now = new DateTime(2026, 2, 17, 13, 1, 40, DateTimeKind.Local);
        var text = "Love it 😄\nIf “oki doki” means *“we’re good for now”* — perfect.\n\nQuick next-step menu (pick one and I’ll run it right away):\n1) **Privilege hygiene sweep(Domain Admins + other privileged groups, nested exposure) 2)** Delegation risk audit**(unconstrained / constrained / protocol transition) 3)** Replication + DC health snapshot** (stale links, failing partners, LDAP/Kerberos basics)\n\nOr just say “done” and I’ll keep quiet like a well-configured service.";
        var normalized = TranscriptMarkdownNormalizer.NormalizeForRendering(text);
        Assert.Contains("\n2. **Delegation risk audit** (unconstrained / constrained / protocol transition)", normalized);
        var html = TranscriptHtmlFormatter.Format(new[] {
            ("Assistant", text, now)
        }, "HH:mm:ss", options);

        Assert.Contains("<strong>Privilege hygiene sweep</strong>", html);
        Assert.Contains("<strong>Delegation risk audit</strong>", html);
        Assert.Contains("<strong>Replication + DC health snapshot</strong>", html);
        Assert.DoesNotContain("**", html);
    }

    /// <summary>
    /// Ensures display HTML repairs malformed AD comparison bullets and nested strong markers.
    /// </summary>
    [Fact]
    public void Format_RepairsAdComparisonBulletArtifactsForDisplayHtml() {
        var options = MarkdownRendererPresets.CreateChatStrictMinimal();
        var now = new DateTime(2026, 2, 18, 19, 3, 10, DateTimeKind.Local);
        var text = "-AD1 starkes Muster\n-** AD2** eher Secure-Channel\n- Signal **AD1 has very high `7034/7023` volume, mostly from **Service Control Manager**.**";

        var html = TranscriptHtmlFormatter.Format(new[] {
            ("Assistant", text, now)
        }, "HH:mm:ss", options);

        Assert.Contains("AD1 starkes Muster", html);
        Assert.Contains("<strong>AD2</strong> eher Secure-Channel", html);
        Assert.Contains("Signal", html);
        Assert.Contains("7034/7023", html);
        Assert.Contains("from Service Control Manager.", html);
        Assert.DoesNotContain("-AD1", html, StringComparison.Ordinal);
        Assert.DoesNotContain("from **Service", html, StringComparison.Ordinal);
        Assert.DoesNotContain("**.**", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures split host-label bullets render as proper list items when label and sentence arrive on separate lines.
    /// </summary>
    [Fact]
    public void Format_RepairsSplitHostLabelBulletsIntoRenderableListItems() {
        var options = MarkdownRendererPresets.CreateChatStrictMinimal();
        var now = new DateTime(2026, 2, 19, 9, 7, 51, DateTimeKind.Local);
        var text = """
                   **Bewertung:**
                   Ja, es gibt auffällige Unterschiede (nicht symmetrisch):
                   -AD1
                   starkes Muster von Dienstabbrüchen/-fehlern (`7034/7023`).
                   -** AD2**
                   eher Secure-Channel/TLS/Policy/Power-Signale (`3210/1129/36874/41`).
                   """;

        var html = TranscriptHtmlFormatter.Format(new[] {
            ("Assistant", text, now)
        }, "HH:mm:ss", options);

        Assert.Contains("<li>AD1", html, StringComparison.Ordinal);
        Assert.Contains("starkes Muster von Dienstabbr", html, StringComparison.Ordinal);
        Assert.Contains("7034/7023", html, StringComparison.Ordinal);
        Assert.Contains("<li><strong>AD2</strong>", html, StringComparison.Ordinal);
        Assert.Contains("3210/1129/36874/41", html, StringComparison.Ordinal);
        Assert.DoesNotContain("-AD1", html, StringComparison.Ordinal);
        Assert.DoesNotContain("-** AD2**", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures unicode-dash bullets are normalized before markdown render so list HTML is stable.
    /// </summary>
    [Fact]
    public void Format_NormalizesUnicodeDashBulletsForDisplayHtml() {
        var options = MarkdownRendererPresets.CreateChatStrictMinimal();
        var now = new DateTime(2026, 2, 19, 10, 12, 0, DateTimeKind.Local);
        var text = "—** AD2** eher Secure-Channel/TLS";

        var html = TranscriptHtmlFormatter.Format(new[] {
            ("Assistant", text, now)
        }, "HH:mm:ss", options);

        Assert.Contains("<li><strong>AD2</strong> eher Secure-Channel/TLS</li>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("—** AD2**", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures live streaming preview plus transcript HTML rendering repairs tight signal-flow labels and overwrapped strong spans.
    /// </summary>
    [Fact]
    public void Format_StreamingPreviewPipelineRepairsSignalTypographyArtifacts() {
        var options = MarkdownRendererPresets.CreateChatStrictMinimal();
        var now = new DateTime(2026, 2, 20, 11, 8, 0, DateTimeKind.Local);
        var raw = string.Join('\n', [
            "- Signal **Catalog count includes hidden/disabled/deprecated rules -> **Why it matters:**external/custom rules can drift or disappear between hosts ->**Next action:**break down `rule_origin` (`builtin` vs `external`) and confirm expected external rules are present.**",
            "- TestimoX rules available ****359****"
        ]);

        var preview = TranscriptMarkdownNormalizer.NormalizeForStreamingPreview(raw);
        var html = TranscriptHtmlFormatter.Format(new[] {
            ("Assistant", preview, now)
        }, "HH:mm:ss", options);

        Assert.Contains("**Why it matters:** external/custom rules can drift", preview, StringComparison.Ordinal);
        Assert.Contains("**Next action:** break down `rule_origin`", preview, StringComparison.Ordinal);
        Assert.Contains("TestimoX rules available **359**", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("****359****", preview, StringComparison.Ordinal);

        Assert.Contains("<strong>Catalog count includes hidden/disabled/deprecated rules</strong>", html, StringComparison.Ordinal);
        Assert.Contains("<strong>Why it matters:</strong> external/custom rules can drift or disappear between hosts", html, StringComparison.Ordinal);
        Assert.Contains("<strong>Next action:</strong> break down <code>rule_origin</code> (<code>builtin</code> vs <code>external</code>)", html, StringComparison.Ordinal);
        Assert.Contains("TestimoX rules available <strong>359</strong>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("</strong>external/custom", html, StringComparison.Ordinal);
        Assert.DoesNotContain("</strong>break down", html, StringComparison.Ordinal);
    }
}
