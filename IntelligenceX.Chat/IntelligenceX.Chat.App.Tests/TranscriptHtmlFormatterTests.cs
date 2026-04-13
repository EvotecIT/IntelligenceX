using System;
using System.Collections.Generic;
using IntelligenceX.Chat.App;
using IntelligenceX.Chat.App.Rendering;
using OfficeIMO.Markdown;
using OfficeIMO.Markdown.Html;
using OfficeIMO.MarkdownRenderer;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Tests for transcript HTML rendering.
/// </summary>
public sealed partial class TranscriptHtmlFormatterTests {
    /// <summary>
    /// Ensures Mermaid fenced blocks are converted to Mermaid runtime placeholders when transcript rendering enables visuals.
    /// </summary>
    [Fact]
    public void Format_RendersMermaidFenceAsMermaidBlockWhenEnabled() {
        var options = OfficeImoMarkdownRuntimeContract.CreateTranscriptRendererOptions();
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

        Assert.True(
            html.Contains("class=\"mermaid\"", StringComparison.Ordinal)
            || html.Contains("language-mermaid", StringComparison.OrdinalIgnoreCase),
            "Expected native OfficeIMO mermaid HTML or a fenced language-mermaid fallback.");
        if (html.Contains("class=\"mermaid\"", StringComparison.Ordinal)) {
            Assert.Contains("data-mermaid-hash=", html, StringComparison.Ordinal);
        }
        Assert.Contains("flowchart LR", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures transcript rendering preserves Mermaid upgrade compatibility for longer real-world diagram replies.
    /// </summary>
    [Fact]
    public void Format_PreservesMermaidUpgradeCompatibility_ForTranscriptStyleDiagramReply() {
        var options = OfficeImoMarkdownRuntimeContract.CreateTranscriptRendererOptions();
        var now = new DateTime(2026, 3, 7, 23, 8, 26, DateTimeKind.Local);
        var html = TranscriptHtmlFormatter.Format(new[] {
            ("Assistant", """
                          Replication map preview:
                          ```mermaid
                          flowchart LR
                            subgraph D1["ad.evotec.xyz"]
                              AD0["AD0.ad.evotec.xyz"]
                              AD1["AD1.ad.evotec.xyz"]
                            end
                            subgraph D2["ad.evotec.pl"]
                              DC1["DC1.ad.evotec.pl"]
                            end
                            AD0 --- AD1
                            AD0 --- DC1
                          ```

                          Interpretation:
                          topology and recent replication are healthy.
                          """, now)
        }, "HH:mm:ss", options);

        Assert.Contains("Replication map preview:", html, StringComparison.Ordinal);
        Assert.Contains("flowchart LR", html, StringComparison.Ordinal);
        Assert.True(
            html.Contains("class=\"mermaid\"", StringComparison.Ordinal)
            || html.Contains("language-mermaid", StringComparison.OrdinalIgnoreCase),
            "Expected native OfficeIMO mermaid HTML or a fenced language-mermaid fallback.");
    }

    /// <summary>
    /// Ensures generic chart fenced blocks render through the native OfficeIMO visual contract.
    /// </summary>
    [Fact]
    public void Format_ComposesGenericChartFenceIntoNativeChartVisualContract() {
        var options = OfficeImoMarkdownRuntimeContract.CreateTranscriptRendererOptions();
        var now = new DateTime(2026, 2, 20, 9, 12, 45, DateTimeKind.Local);
        var html = TranscriptHtmlFormatter.Format(new[] {
            ("Assistant", """
                          Chart preview:
                          ```chart
                          {"type":"bar","data":{"labels":["A"],"datasets":[{"label":"X","data":[1]}]}}
                          ```
                          Interpretation line.
                          """, now)
        }, "HH:mm:ss", options);

        Assert.Contains("Chart preview:", html, StringComparison.Ordinal);
        AssertChartRendersAsNativeChartVisual(html);
    }

    /// <summary>
    /// Ensures generic network fenced blocks render through the native OfficeIMO visual contract.
    /// </summary>
    [Fact]
    public void Format_ComposesGenericNetworkFenceIntoNativeNetworkVisualContract() {
        var options = OfficeImoMarkdownRuntimeContract.CreateTranscriptRendererOptions();
        var now = new DateTime(2026, 2, 20, 9, 16, 22, DateTimeKind.Local);
        var html = TranscriptHtmlFormatter.Format(new[] {
            ("Assistant", """
                          Relationship network:
                          ```network
                          {"nodes":[{"id":"A","label":"User"},{"id":"B","label":"Group"}],"edges":[{"from":"A","to":"B","label":"memberOf"}]}
                          ```
                          Interpretation line.
                          """, now)
        }, "HH:mm:ss", options);

        Assert.Contains("Relationship network:", html, StringComparison.Ordinal);
        AssertNetworkRendersAsNativeNetworkVisual(html);
    }

    /// <summary>
    /// Ensures the live transcript formatter composes multiple generic chart/network fences in the same message.
    /// </summary>
    [Fact]
    public void Format_ComposesMultipleGenericVisualFencesIntoNativeVisualContract() {
        var options = OfficeImoMarkdownRuntimeContract.CreateTranscriptRendererOptions();
        var now = new DateTime(2026, 3, 23, 10, 18, 12, DateTimeKind.Local);
        var html = TranscriptHtmlFormatter.Format(new[] {
            ("Assistant", """
                          Mixed visual preview:

                          Generic chart:
                          ```chart
                          {"type":"bar","data":{"labels":["A"],"datasets":[{"label":"X","data":[1]}]}}
                          ```

                          Second chart:
                          ```chart
                          {"type":"bar","data":{"labels":["B"],"datasets":[{"label":"Y","data":[2]}]}}
                          ```

                          Generic network:
                          ```network
                          {"nodes":[{"id":"A","label":"User"},{"id":"B","label":"Group"}],"edges":[{"source":"A","target":"B","label":"memberOf"}]}
                          ```

                          Second network:
                          ```network
                          {"nodes":[{"id":"C","label":"Computer"},{"id":"D","label":"OU"}],"edges":[{"from":"C","to":"D","label":"contains"}]}
                          ```
                          """, now)
        }, "HH:mm:ss", options);

        Assert.Contains("Mixed visual preview:", html, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(html, "data-omd-visual-kind=\"chart\""));
        Assert.Equal(2, CountOccurrences(html, "data-omd-visual-kind=\"network\""));
        Assert.DoesNotContain("language-chart", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("language-network", html, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures the live transcript renderer can consume markdown recovered from the shared OfficeIMO visual-host HTML fixture.
    /// </summary>
    [Fact]
    public void Format_ComposesOfficeImoSharedVisualHostsFixtureRoundTripIntoNativeVisualContract() {
        var options = OfficeImoMarkdownRuntimeContract.CreateTranscriptRendererOptions();
        var now = new DateTime(2026, 3, 23, 12, 4, 18, DateTimeKind.Local);
        string sourceHtml = ReadOfficeImoHtmlFixture("officeimo-shared-visual-hosts.html");
        string markdown = sourceHtml.ToMarkdown(new HtmlToMarkdownOptions {
            BaseUri = new Uri("https://example.com/visuals/archive.html"),
            MarkdownWriteOptions = MarkdownWriteOptions.CreateOfficeIMOProfile()
        });

        var html = TranscriptHtmlFormatter.Format(new[] {
            ("Assistant", markdown, now)
        }, "HH:mm:ss", options);

        Assert.Contains("Shared Visual Archive", html, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(html, "data-omd-visual-kind=\"chart\""));
        Assert.Equal(1, CountOccurrences(html, "data-omd-visual-kind=\"network\""));
        Assert.Equal(1, CountOccurrences(html, "data-omd-visual-kind=\"dataview\""));
        Assert.Contains("Chart preview", html, StringComparison.Ordinal);
        Assert.Contains("Network preview", html, StringComparison.Ordinal);
        Assert.Contains("Dataview preview", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-omd-visual-contract", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("<figure class=\"omd-visual", html, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures historical JSON-fenced network payloads are upgraded into the generic network render path during transcript formatting.
    /// </summary>
    [Fact]
    public void Format_UpgradesLegacyJsonNetworkFenceForHistoricalTranscriptMessages() {
        var options = OfficeImoMarkdownRuntimeContract.CreateTranscriptRendererOptions();
        var now = new DateTime(2026, 3, 8, 18, 9, 19, DateTimeKind.Local);
        var html = TranscriptHtmlFormatter.Format(new[] {
            ("Assistant", """
                          Scope graph preview:
                          ```json
                          {"nodes":[{"id":"forest_ad.evotec.xyz","label":"Forest: ad.evotec.xyz","type":"forest"},{"id":"domain_ad.evotec.xyz","label":"Domain: ad.evotec.xyz","type":"domain"}],"edges":[{"source":"forest_ad.evotec.xyz","target":"domain_ad.evotec.xyz","label":"contains"}]}
                          ```
                          Interpretation: topology preview only.
                          """, now)
        }, "HH:mm:ss", options);

        Assert.Contains("Scope graph preview:", html, StringComparison.Ordinal);
        AssertNetworkRendersAsNativeNetworkVisual(html);
        Assert.DoesNotContain("language-json", html, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures rows, role styling, continuation classes, and copy indexes are rendered.
    /// </summary>
    [Fact]
    public void Format_RendersRoleRowsAndContinuationMarkers() {
        var options = OfficeImoMarkdownRuntimeContract.CreateTranscriptRendererOptions();
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
        var options = OfficeImoMarkdownRuntimeContract.CreateTranscriptRendererOptions();
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
        var options = OfficeImoMarkdownRuntimeContract.CreateTranscriptRendererOptions();
        var now = new DateTime(2026, 2, 22, 20, 18, 6, DateTimeKind.Local);
        var messages = new (string Role, string Text, DateTime Time, string? Model)[] {
            ("Assistant", "Running checks...", now, "gpt-5.4")
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
        var options = OfficeImoMarkdownRuntimeContract.CreateTranscriptRendererOptions();
        var now = new DateTime(2026, 2, 23, 7, 41, 0, DateTimeKind.Local);
        var messages = new (string Role, string Text, DateTime Time, string? Model)[] {
            ("Assistant", "Running cross-DC checks...", now, "gpt-5.4")
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
        var options = OfficeImoMarkdownRuntimeContract.CreateTranscriptRendererOptions();
        var now = new DateTime(2026, 2, 22, 20, 18, 6, DateTimeKind.Local);
        var messages = new (string Role, string Text, DateTime Time, string? Model)[] {
            ("Assistant", "Running checks...", now, "gpt-5.4")
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
        var options = OfficeImoMarkdownRuntimeContract.CreateTranscriptRendererOptions();
        var now = new DateTime(2026, 2, 23, 19, 12, 0, DateTimeKind.Local);
        var messages = new (string Role, string Text, DateTime Time, string? Model)[] {
            ("User", "Run check", now, null),
            ("Assistant", "Running checks...", now.AddSeconds(1), "gpt-5.4"),
            ("Assistant", "Done.", now.AddSeconds(2), "gpt-5.4")
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
        var options = OfficeImoMarkdownRuntimeContract.CreateTranscriptRendererOptions();
        var now = new DateTime(2026, 2, 23, 22, 5, 0, DateTimeKind.Local);
        var messages = new (string Role, string Text, DateTime Time, string? Model)[] {
            ("Assistant", "draft-one", now, "gpt-5.4"),
            ("Assistant", "draft-two", now.AddSeconds(1), "gpt-5.4")
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
        var options = OfficeImoMarkdownRuntimeContract.CreateTranscriptRendererOptions();
        var now = new DateTime(2026, 2, 23, 21, 6, 0, DateTimeKind.Local);
        var messages = new (string Role, string Text, DateTime Time, string? Model)[] {
            ("Assistant", "Status snapshot.", now, "gpt-5.4")
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
        var options = OfficeImoMarkdownRuntimeContract.CreateTranscriptRendererOptions();
        var now = new DateTime(2026, 2, 23, 21, 12, 0, DateTimeKind.Local);
        var messages = new (string Role, string Text, DateTime Time, string? Model)[] {
            ("Assistant", "Status snapshot.", now, "gpt-5.4")
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
        var options = OfficeImoMarkdownRuntimeContract.CreateTranscriptRendererOptions();
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
        var options = OfficeImoMarkdownRuntimeContract.CreateTranscriptRendererOptions();
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
        var options = OfficeImoMarkdownRuntimeContract.CreateTranscriptRendererOptions();
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
    /// Ensures execution-contract blockers render as styled outcome cards instead of raw protocol text bubbles.
    /// </summary>
    [Fact]
    public void Format_RendersExecutionBlockedAsOutcomeCard() {
        var options = OfficeImoMarkdownRuntimeContract.CreateTranscriptRendererOptions();
        var now = new DateTime(2026, 4, 7, 11, 47, 9, DateTimeKind.Local);
        var html = TranscriptHtmlFormatter.Format(new[] {
            ("Assistant", """
                          [Execution blocked]
                          ix:execution-contract:v1 I do not have confirmed tool output for this selected action yet.

                          Selected action request: hi Follow-up: graphite would be good

                          Reason code:
                          execution_contract_unmet_follow_up_draft_not_blocker_like

                          Please retry this action in this context, or use the action command below.
                          """, now)
        }, "HH:mm:ss", options);

        Assert.Contains("outcome-card", html, StringComparison.Ordinal);
        Assert.Contains("outcome-kind-execution-blocked", html, StringComparison.Ordinal);
        Assert.Contains(">Blocked</span>", html, StringComparison.Ordinal);
        Assert.Contains("I do not have confirmed tool output for this selected action yet.", html, StringComparison.Ordinal);
        Assert.Contains("Selected action:", html, StringComparison.Ordinal);
        Assert.Contains("execution_contract_unmet_follow_up_draft_not_blocker_like", html, StringComparison.Ordinal);
        Assert.DoesNotContain("ix:execution-contract:v1", html, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures execution-blocked metadata is rendered as safe literal text when it contains markdown or control characters.
    /// </summary>
    [Fact]
    public void Format_RendersExecutionBlockedMetadataAsEscapedLiteralText() {
        var options = OfficeImoMarkdownRuntimeContract.CreateTranscriptRendererOptions();
        var now = new DateTime(2026, 4, 7, 12, 5, 14, DateTimeKind.Local);
        var html = TranscriptHtmlFormatter.Format(new[] {
            ("Assistant", """
                          [Execution blocked]
                          ix:execution-contract:v1 Waiting for confirmed tool output.

                          Selected action request: <b>boom</b> **bold** /act `weird` 
                          Action: /act `quoted` 
                          Reason code:
                          reason_tick_`value`
                          """, now)
        }, "HH:mm:ss", options);

        Assert.Contains("outcome-kind-execution-blocked", html, StringComparison.Ordinal);
        Assert.Contains("Selected action:", html, StringComparison.Ordinal);
        Assert.Contains("boom", html, StringComparison.Ordinal);
        Assert.Contains("bold", html, StringComparison.Ordinal);
        Assert.Contains("weird", html, StringComparison.Ordinal);
        Assert.Contains("quoted", html, StringComparison.Ordinal);
        Assert.Contains("tick_", html, StringComparison.Ordinal);
        Assert.Contains("value", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>boom</b>", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<strong>bold</strong>", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\u0001", html, StringComparison.Ordinal);
        Assert.DoesNotContain("\u0002", html, StringComparison.Ordinal);
        Assert.DoesNotContain("\u0007", html, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures execution-blocked outcome cards strip leaked working-memory checkpoint payloads from transcript HTML.
    /// </summary>
    [Fact]
    public void Format_StripsWorkingMemoryCheckpointArtifactsFromExecutionBlockedOutcome() {
        var options = OfficeImoMarkdownRuntimeContract.CreateTranscriptRendererOptions();
        var now = new DateTime(2026, 4, 7, 20, 47, 32, DateTimeKind.Local);
        var html = TranscriptHtmlFormatter.Format(new[] {
            ("Assistant", """
                          [Execution blocked] ix: execution-contract: v1 I do not have confirmed tool output for this selected action yet.

                          Selected action request: [Working memory checkpoint] ix: working-memory: v1 domain_scope_family: ad_domain recent_tools: ad_environment_discover
                          recent_evidence_1: ad_environment_discover: #

                          ## Active Directory: Environment Discovery

                          | Field | Value |
                          | --- | --- |
                          | Domain controller |  |

                          Reason code: no_tool_calls_after_watchdog_retry

                          Please retry this action in this context, or use the action command below.
                          Tool receipt: no tools were run in this turn.
                          """, now)
        }, "HH:mm:ss", options);

        Assert.Contains("Execution blocked", html, StringComparison.Ordinal);
        Assert.Contains("no_tool_calls_after_watchdog_retry", html, StringComparison.Ordinal);
        Assert.Contains("Tool receipt: no tools were run in this turn.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Working memory checkpoint", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ix:working-memory:v1", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain_scope_family", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("recent_tools", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Active Directory: Environment Discovery", html, StringComparison.Ordinal);
    }
}
