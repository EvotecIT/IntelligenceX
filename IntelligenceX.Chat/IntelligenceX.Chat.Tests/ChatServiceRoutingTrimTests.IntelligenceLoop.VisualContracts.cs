using System;
using System.Collections.Generic;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Service;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class ChatServiceRoutingTrimTests {
    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_UsesStructuredVisualOverrideToDisableVisuals() {
        var request = """
            [Proactive visualization guidance]
            ix:proactive-visualization:v1
            allow_new_visuals: false
            If needed, use `visnetwork`.
            """;
        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...");

        Assert.Contains("allow_new_visuals: false", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("request_has_visual_contract: true", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_UsesStructuredVisualOverrideToEnableVisuals() {
        var request = """
            [Proactive visualization guidance]
            ix:proactive-visualization:v1
            allow_new_visuals: true
            Keep response compact unless a visual helps.
            """;
        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...");

        Assert.Contains("allow_new_visuals: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("request_has_visual_contract: true", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_InferPreferredVisualFromAssistantDraftWhenVisualsAreAllowed() {
        var request = """
            [Proactive visualization guidance]
            ix:proactive-visualization:v1
            allow_new_visuals: true
            """;
        var draft = """
            Current findings:
            ```ix-network
            {"nodes":[{"id":"AD0"}],"edges":[]}
            ```
            """;

        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, draft);

        Assert.Contains("allow_new_visuals: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("request_has_visual_contract: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual: network", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual_source: draft", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_InferPreferredVisualFromToolOutputsWhenVisualsAreAllowed() {
        var request = """
            [Proactive visualization guidance]
            ix:proactive-visualization:v1
            allow_new_visuals: true
            """;
        var draft = """
            Current findings:
            | host | status |
            | --- | --- |
            | AD0 | healthy |
            """;
        var outputs = new List<ToolOutputDto> {
            new() {
                CallId = "c1",
                Output = "{\"nodes\":[{\"id\":\"AD0\"}],\"edges\":[]}",
                Ok = true
            }
        };

        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, draft, outputs);

        Assert.Contains("allow_new_visuals: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("request_has_visual_contract: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual: network", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual_source: tool_outputs", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_InferPreferredVisualFromToolOutputRenderHintsWhenVisualsAreAllowed() {
        var request = """
            [Proactive visualization guidance]
            ix:proactive-visualization:v1
            allow_new_visuals: true
            """;
        var outputs = new List<ToolOutputDto> {
            new() {
                CallId = "c1",
                Output = "{\"ok\":true,\"events\":[]}",
                RenderJson = "{\"kind\":\"table\",\"rows_path\":\"events\"}",
                Ok = true
            }
        };

        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...", outputs);

        Assert.Contains("allow_new_visuals: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("request_has_visual_contract: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual: table", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual_source: tool_outputs", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual_priority: 100", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_PrefersHigherPriorityRenderHintFromArrayWhenVisualsAreAllowed() {
        var request = """
            [Proactive visualization guidance]
            ix:proactive-visualization:v1
            allow_new_visuals: true
            """;
        var outputs = new List<ToolOutputDto> {
            new() {
                CallId = "c1",
                Output = "{\"ok\":true,\"events\":[]}",
                RenderJson = """
                    [
                      {"kind":"table","rows_path":"events"},
                      {"kind":"network","content":{"nodes":[{"id":"AD0"}],"edges":[]}}
                    ]
                    """,
                Ok = true
            }
        };

        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...", outputs);

        Assert.Contains("allow_new_visuals: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("request_has_visual_contract: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual: network", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual_source: tool_outputs", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual_priority: 400", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_PrefersExplicitRenderHintPriorityOverVisualTypePriority() {
        var request = """
            [Proactive visualization guidance]
            ix:proactive-visualization:v1
            allow_new_visuals: true
            """;
        var outputs = new List<ToolOutputDto> {
            new() {
                CallId = "c1",
                Output = "{\"ok\":true,\"events\":[]}",
                RenderJson = """
                    [
                      {"kind":"network","priority":100},
                      {"kind":"table","priority":900}
                    ]
                    """,
                Ok = true
            }
        };

        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...", outputs);

        Assert.Contains("allow_new_visuals: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("request_has_visual_contract: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual: table", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual_source: tool_outputs", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual_priority: 900", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_FallsBackToVisualTypePriorityWhenRenderHintPriorityIsInvalid() {
        var request = """
            [Proactive visualization guidance]
            ix:proactive-visualization:v1
            allow_new_visuals: true
            """;
        var outputs = new List<ToolOutputDto> {
            new() {
                CallId = "c1",
                Output = "{\"ok\":true}",
                RenderJson = """
                    [
                      {"kind":"table","priority":"high"},
                      {"kind":"network"}
                    ]
                    """,
                Ok = true
            }
        };

        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...", outputs);

        Assert.Contains("allow_new_visuals: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("request_has_visual_contract: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual: network", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual_source: tool_outputs", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual_priority: 400", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_InferPreferredVisualFromCodeRenderHintLanguageFallback() {
        var request = """
            [Proactive visualization guidance]
            ix:proactive-visualization:v1
            allow_new_visuals: true
            """;
        var outputs = new List<ToolOutputDto> {
            new() {
                CallId = "c1",
                Output = "{\"ok\":true}",
                RenderJson = "{\"kind\":\"code\",\"language\":\"chart\",\"content\":\"{}\"}",
                Ok = true
            }
        };

        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...", outputs);

        Assert.Contains("allow_new_visuals: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("request_has_visual_contract: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual: chart", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual_source: tool_outputs", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_PrefersToolOutputRenderHintsOverPayloadAliasesWhenVisualsAreAllowed() {
        var request = """
            [Proactive visualization guidance]
            ix:proactive-visualization:v1
            allow_new_visuals: true
            """;
        var outputs = new List<ToolOutputDto> {
            new() {
                CallId = "c1",
                Output = "{\"nodes\":[{\"id\":\"AD0\"}],\"edges\":[{\"from\":\"AD0\",\"to\":\"AD1\"}]}",
                RenderJson = "{\"kind\":\"code\",\"language\":\"chart\",\"content\":\"{}\"}",
                Ok = true
            }
        };

        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...", outputs);

        Assert.Contains("allow_new_visuals: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("request_has_visual_contract: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual: chart", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual_source: tool_outputs", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_NormalizesLegacyNetworkRenderHintLanguageWhenVisualsAreAllowed() {
        var request = """
            [Proactive visualization guidance]
            ix:proactive-visualization:v1
            allow_new_visuals: true
            """;
        var outputs = new List<ToolOutputDto> {
            new() {
                CallId = "c1",
                Output = "{\"ok\":true}",
                RenderJson = "{\"kind\":\"code\",\"language\":\"visnetwork\",\"content\":\"{}\"}",
                Ok = true
            }
        };

        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...", outputs);

        Assert.Contains("allow_new_visuals: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("request_has_visual_contract: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual: network", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual_source: tool_outputs", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_InferPreferredVisualFromToolOutputsUsingLinksAliasWhenVisualsAreAllowed() {
        var request = """
            [Proactive visualization guidance]
            ix:proactive-visualization:v1
            allow_new_visuals: true
            """;
        var outputs = new List<ToolOutputDto> {
            new() {
                CallId = "c1",
                Output = "{\"nodes\":[{\"id\":\"AD0\"}],\"links\":[{\"source\":\"AD0\",\"target\":\"AD1\"}]}",
                Ok = true
            }
        };

        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...", outputs);

        Assert.Contains("allow_new_visuals: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("request_has_visual_contract: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual: network", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual_source: tool_outputs", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_InferPreferredVisualFromToolOutputsUsingRelationshipsAliasesWhenVisualsAreAllowed() {
        var request = """
            [Proactive visualization guidance]
            ix:proactive-visualization:v1
            allow_new_visuals: true
            """;
        var outputs = new List<ToolOutputDto> {
            new() {
                CallId = "c1",
                Output = "{\"entities\":[{\"id\":\"AD0\"}],\"relationships\":[{\"source\":\"AD0\",\"target\":\"AD1\"}]}",
                Ok = true
            }
        };

        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...", outputs);

        Assert.Contains("allow_new_visuals: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("request_has_visual_contract: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual: network", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual_source: tool_outputs", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_InferNetworkPreferredVisualFromToolOutputsUsingCaseInsensitivePropertyNames() {
        var request = """
            [Proactive visualization guidance]
            ix:proactive-visualization:v1
            allow_new_visuals: true
            """;
        var outputs = new List<ToolOutputDto> {
            new() {
                CallId = "c1",
                Output = "{\"Nodes\":[{\"id\":\"AD0\"}],\"RELATIONSHIPS\":[{\"source\":\"AD0\",\"target\":\"AD1\"}]}",
                Ok = true
            }
        };

        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...", outputs);

        Assert.Contains("allow_new_visuals: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("request_has_visual_contract: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual: network", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual_source: tool_outputs", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_InferChartPreferredVisualFromToolOutputsUsingXAxisAndPointsAliasesWhenVisualsAreAllowed() {
        var request = """
            [Proactive visualization guidance]
            ix:proactive-visualization:v1
            allow_new_visuals: true
            """;
        var outputs = new List<ToolOutputDto> {
            new() {
                CallId = "c1",
                Output = "{\"x\":[\"Jan\",\"Feb\"],\"points\":[4,7]}",
                Ok = true
            }
        };

        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...", outputs);

        Assert.Contains("allow_new_visuals: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("request_has_visual_contract: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual: chart", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual_source: tool_outputs", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_InferChartPreferredVisualFromToolOutputsUsingCaseInsensitivePropertyNames() {
        var request = """
            [Proactive visualization guidance]
            ix:proactive-visualization:v1
            allow_new_visuals: true
            """;
        var outputs = new List<ToolOutputDto> {
            new() {
                CallId = "c1",
                Output = "{\"LABELS\":[\"Jan\",\"Feb\"],\"Series\":[4,7]}",
                Ok = true
            }
        };

        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...", outputs);

        Assert.Contains("allow_new_visuals: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("request_has_visual_contract: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual: chart", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual_source: tool_outputs", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_InferTablePreferredVisualFromToolOutputsUsingRecordsAliasWhenVisualsAreAllowed() {
        var request = """
            [Proactive visualization guidance]
            ix:proactive-visualization:v1
            allow_new_visuals: true
            """;
        var outputs = new List<ToolOutputDto> {
            new() {
                CallId = "c1",
                Output = "{\"keys\":[\"host\",\"status\"],\"records\":[[\"DC01\",\"healthy\"]]}",
                Ok = true
            }
        };

        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...", outputs);

        Assert.Contains("allow_new_visuals: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("request_has_visual_contract: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual: table", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual_source: tool_outputs", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_InferTablePreferredVisualFromToolOutputsUsingCaseInsensitivePropertyNames() {
        var request = """
            [Proactive visualization guidance]
            ix:proactive-visualization:v1
            allow_new_visuals: true
            """;
        var outputs = new List<ToolOutputDto> {
            new() {
                CallId = "c1",
                Output = "{\"HEADERS\":[\"host\",\"status\"],\"Rows\":[[\"DC01\",\"healthy\"]]}",
                Ok = true
            }
        };

        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...", outputs);

        Assert.Contains("allow_new_visuals: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("request_has_visual_contract: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual: table", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual_source: tool_outputs", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_DoesNotEnableVisualsFromToolOutputsWithoutVisualContract() {
        var outputs = new List<ToolOutputDto> {
            new() {
                CallId = "c1",
                Output = "{\"labels\":[\"Jan\",\"Feb\"],\"series\":[1,2]}",
                Ok = true
            }
        };

        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt("analyze logons", "Current findings...", outputs);

        Assert.Contains("allow_new_visuals: false", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("request_has_visual_contract: false", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual: auto", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual_source: none", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_InferTablePreferredVisualFromAssistantDraftMarkdownTableWhenVisualsAreAllowed() {
        var request = """
            [Proactive visualization guidance]
            ix:proactive-visualization:v1
            allow_new_visuals: true
            """;
        var draft = """
            Current findings:
            | dc | status |
            | --- | --- |
            | dc01 | healthy |
            """;

        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, draft);

        Assert.Contains("allow_new_visuals: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("request_has_visual_contract: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual: table", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_InferNetworkPreferredVisualFromAssistantDraftStructuredJsonWhenVisualsAreAllowed() {
        var request = """
            [Proactive visualization guidance]
            ix:proactive-visualization:v1
            allow_new_visuals: true
            """;
        var draft = """
            Current findings:
            {"nodes":[{"id":"AD0"}],"edges":[{"from":"AD0","to":"AD1"}]}
            """;

        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, draft);

        Assert.Contains("allow_new_visuals: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("request_has_visual_contract: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual: network", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_InferChartPreferredVisualFromAssistantDraftStructuredJsonWhenVisualsAreAllowed() {
        var request = """
            [Proactive visualization guidance]
            ix:proactive-visualization:v1
            allow_new_visuals: true
            """;
        var draft = """
            Current findings:
            {"labels":["Jan","Feb"],"datasets":[{"label":"Auth failures","data":[4,7]}]}
            """;

        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, draft);

        Assert.Contains("allow_new_visuals: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("request_has_visual_contract: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual: chart", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_InferTablePreferredVisualFromAssistantDraftStructuredJsonWhenVisualsAreAllowed() {
        var request = """
            [Proactive visualization guidance]
            ix:proactive-visualization:v1
            allow_new_visuals: true
            """;
        var draft = """
            Current findings:
            {"headers":["dc","status"],"rows":[["DC01","healthy"]]}
            """;

        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, draft);

        Assert.Contains("allow_new_visuals: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("request_has_visual_contract: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual: table", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_DoesNotInferPreferredVisualFromDraftWhenVisualsRemainDisabled() {
        var request = """
            [Proactive visualization guidance]
            ix:proactive-visualization:v1
            """;
        var draft = """
            Current findings:
            ```ix-network
            {"nodes":[{"id":"AD0"}],"edges":[]}
            ```
            """;

        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, draft);

        Assert.Contains("allow_new_visuals: false", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("request_has_visual_contract: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual: auto", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_ParsesStructuredVisualOverrideWithInlineComment() {
        var request = """
            [Proactive visualization guidance]
            ix:proactive-visualization:v1
            allow_new_visuals: true # include graph only when evidence is complex
            """;
        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...");

        Assert.Contains("allow_new_visuals: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("request_has_visual_contract: true", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_UsesGenericVisualOptionalityGuidanceText() {
        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt("Analyze current findings.", "Current findings...");

        Assert.Contains("keep visual blocks optional", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tables/diagrams/charts/networks", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_UsesStructuredPreferredVisualAlias() {
        var request = """
            [Proactive visualization guidance]
            ix:proactive-visualization:v1
            preferred_visual: visnetwork
            """;
        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...");

        Assert.Contains("allow_new_visuals: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("request_has_visual_contract: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual: network", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual_source: request", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("If preferred_visual is set, prefer that visual format", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_NormalizesGraphPreferredVisualAlias() {
        var request = """
            [Proactive visualization guidance]
            ix:proactive-visualization:v1
            preferred_visual: graph
            """;
        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...");

        Assert.Contains("allow_new_visuals: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual: network", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual_source: request", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_ParsesPreferredVisualTypeWithInlineComment() {
        var request = """
            [Proactive visualization guidance]
            ix:proactive-visualization:v1
            preferred_visual_type: chart # keep compact
            """;
        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...");

        Assert.Contains("allow_new_visuals: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual: chart", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_NormalizesPlotPreferredVisualAlias() {
        var request = """
            [Proactive visualization guidance]
            ix:proactive-visualization:v1
            preferred_visual_type: plot
            """;
        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...");

        Assert.Contains("allow_new_visuals: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual: chart", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_NormalizesMarkdownTablePreferredVisualAlias() {
        var request = """
            [Proactive visualization guidance]
            ix:proactive-visualization:v1
            preferred_visual_type: markdown-table
            """;
        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...");

        Assert.Contains("allow_new_visuals: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual: table", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("If preferred_visual is set, prefer that visual format", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_NormalizesDatatablePreferredVisualAlias() {
        var request = """
            [Proactive visualization guidance]
            ix:proactive-visualization:v1
            preferred_visual: datatable
            """;
        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...");

        Assert.Contains("allow_new_visuals: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual: table", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual_source: request", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_PreservesExplicitAutoPreferredVisualOverride() {
        var request = """
            [Proactive visualization guidance]
            ix:proactive-visualization:v1
            preferred_visual: auto
            """;
        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...");

        Assert.Contains("allow_new_visuals: false", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("request_has_visual_contract: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual: auto", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("If preferred_visual is set, prefer that visual format", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_KeepsExplicitAutoPreferredVisualOverrideWhenSignalExists() {
        var request = """
            [Proactive visualization guidance]
            ix:proactive-visualization:v1
            preferred_visual: auto
            Use `visnetwork` only if required.
            """;
        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...");

        Assert.Contains("allow_new_visuals: false", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("request_has_visual_contract: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual: auto", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_UsesStructuredMaxNewVisualsOverride() {
        var request = """
            [Proactive visualization guidance]
            ix:proactive-visualization:v1
            max_new_visuals: 2
            """;
        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...");

        Assert.Contains("allow_new_visuals: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("max_new_visuals: 2", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("include at most 2 new visual block(s)", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_DisablesVisualsWhenMaxNewVisualsIsZero() {
        var request = """
            [Proactive visualization guidance]
            ix:proactive-visualization:v1
            allow_new_visuals: true
            max_new_visuals: 0
            """;
        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...");

        Assert.Contains("allow_new_visuals: false", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("max_new_visuals: 0", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("do not introduce new mermaid/chart/network blocks", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_ClampsStructuredMaxNewVisualsToSupportedRange() {
        var request = """
            [Proactive visualization guidance]
            ix:proactive-visualization:v1
            max_new_visuals: 99
            """;
        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...");

        Assert.Contains("allow_new_visuals: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("max_new_visuals: 3", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("include at most 3 new visual block(s)", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_ClampsLargeStructuredMaxNewVisualsToSupportedRange() {
        var request = """
            [Proactive visualization guidance]
            ix:proactive-visualization:v1
            max_new_visuals: 9999999999
            """;
        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...");

        Assert.Contains("allow_new_visuals: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("max_new_visuals: 3", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("include at most 3 new visual block(s)", text, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("2147483647")]
    [InlineData("2147483648")]
    [InlineData("9223372036854775807")]
    public void BuildProactiveFollowUpReviewPrompt_ClampsBoundaryStructuredMaxNewVisualsToSupportedRange(string value) {
        var request = $$"""
            [Proactive visualization guidance]
            ix:proactive-visualization:v1
            max_new_visuals: {{value}}
            """;
        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...");
        var userRequestHeaderIndex = text.IndexOf("User request:", StringComparison.Ordinal);
        Assert.True(userRequestHeaderIndex >= 0, "Prompt should include a User request section.");
        var contractHeader = text.Substring(0, userRequestHeaderIndex);

        Assert.Contains("allow_new_visuals: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("max_new_visuals: 3", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("include at most 3 new visual block(s)", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain($"max_new_visuals: {value}", contractHeader, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("abc")]
    public void BuildProactiveFollowUpReviewPrompt_IgnoresInvalidStructuredMaxNewVisualsOverride(string invalidMax) {
        var request = $$"""
            [Proactive visualization guidance]
            ix:proactive-visualization:v1
            allow_new_visuals: true
            max_new_visuals: {{invalidMax}}
            """;
        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...");

        Assert.Contains("allow_new_visuals: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("max_new_visuals: 1", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("include at most 1 new visual block(s)", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_TreatsMarkerAsVisualContractWhenOverridesAreInvalid() {
        var request = """
            [Proactive visualization guidance]
            ix:proactive-visualization:v1
            max_new_visuals:
            """;
        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...");

        Assert.Contains("request_has_visual_contract: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("allow_new_visuals: false", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("max_new_visuals: 0", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_DoesNotTreatEmbeddedMarkerTextAsVisualContract() {
        const string request = "Please treat ix:proactive-visualization:v1 as a literal log token.";
        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...");

        Assert.Contains("request_has_visual_contract: false", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("allow_new_visuals: false", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("max_new_visuals: 0", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_TreatsMarkerLineWithInlineCommentAsVisualContract() {
        var request = """
            [Proactive visualization guidance]
            ix:proactive-visualization:v1 # from orchestrator
            """;
        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...");

        Assert.Contains("request_has_visual_contract: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("allow_new_visuals: false", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("max_new_visuals: 0", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_TreatsMarkerLineWithSlashSlashCommentAsVisualContract() {
        var request = """
            [Proactive visualization guidance]
            ix:proactive-visualization:v1 // from orchestrator
            """;
        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...");

        Assert.Contains("request_has_visual_contract: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("allow_new_visuals: false", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("max_new_visuals: 0", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_TreatsMarkerLineWithSemicolonCommentAndCrLfAsVisualContract() {
        const string request = "[Proactive visualization guidance]\r\n\r\nix:proactive-visualization:v1 ; from orchestrator\r\n";
        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...");

        Assert.Contains("request_has_visual_contract: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("allow_new_visuals: false", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("max_new_visuals: 0", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_DoesNotTreatMarkerLineWithSingleSlashAsVisualContract() {
        var request = """
            [Proactive visualization guidance]
            ix:proactive-visualization:v1 / from orchestrator
            """;
        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...");

        Assert.Contains("request_has_visual_contract: false", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("allow_new_visuals: false", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("max_new_visuals: 0", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_IgnoresUnknownStructuredPreferredVisual() {
        var request = """
            [Proactive visualization guidance]
            ix:proactive-visualization:v1
            allow_new_visuals: true
            preferred_visual: radar
            """;
        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...");

        Assert.Contains("allow_new_visuals: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual: auto", text, StringComparison.OrdinalIgnoreCase);
    }
}
