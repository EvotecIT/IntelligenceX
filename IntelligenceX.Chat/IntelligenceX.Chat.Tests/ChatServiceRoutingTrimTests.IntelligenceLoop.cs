using System;
using System.Collections.Generic;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Service;
using IntelligenceX.Json;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.OpenAI.ToolCalling;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class ChatServiceRoutingTrimTests {
    [Theory]
    [InlineData(null, ChatRequestOptionLimits.DefaultToolRounds, ChatRequestOptionLimits.DefaultToolRounds)]
    [InlineData(null, 500, ChatRequestOptionLimits.MaxToolRounds)]
    [InlineData(null, 0, ChatRequestOptionLimits.MinToolRounds)]
    [InlineData(0, ChatRequestOptionLimits.DefaultToolRounds, ChatRequestOptionLimits.MinToolRounds)]
    [InlineData(ChatRequestOptionLimits.MinToolRounds, ChatRequestOptionLimits.DefaultToolRounds, ChatRequestOptionLimits.MinToolRounds)]
    [InlineData(ChatRequestOptionLimits.DefaultToolRounds, ChatRequestOptionLimits.DefaultToolRounds, ChatRequestOptionLimits.DefaultToolRounds)]
    [InlineData(300, ChatRequestOptionLimits.DefaultToolRounds, ChatRequestOptionLimits.MaxToolRounds)]
    public void ResolveMaxToolRounds_ClampsToSupportedRange(int? requested, int serviceDefault, int expected) {
        ChatRequestOptions? options = requested is null
            ? null
            : new ChatRequestOptions {
                MaxToolRounds = requested.Value
            };

        var result = ChatServiceSession.ResolveMaxToolRounds(options, serviceDefault);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ResolveMaxReviewPasses_DefaultsToSafeValueWhenUnset() {
        var result = ChatServiceSession.ResolveMaxReviewPasses(null);
        Assert.Equal(1, result);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(5, 3)]
    public void ResolveMaxReviewPasses_ClampsToSupportedRange(int requested, int expected) {
        var options = new ChatRequestOptions {
            MaxReviewPasses = requested
        };

        var result = ChatServiceSession.ResolveMaxReviewPasses(options);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(null, 8)]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(4, 4)]
    [InlineData(120, 60)]
    public void ResolveModelHeartbeatSeconds_RespectsBounds(int? requested, int expected) {
        ChatRequestOptions? options = requested is null
            ? null
            : new ChatRequestOptions {
                ModelHeartbeatSeconds = requested.Value
            };

        var result = ChatServiceSession.ResolveModelHeartbeatSeconds(options);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void BuildReviewPassClampMessage_ReportsRequestedAndEffectiveValues() {
        var result = BuildReviewPassClampMessageMethod.Invoke(null, new object?[] { 9, 3 });
        var text = Assert.IsType<string>(result);

        Assert.Contains("requested review passes (9)", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("adjusted to 3", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("0..3", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildModelHeartbeatClampMessage_ReportsRequestedAndEffectiveValues() {
        var result = BuildModelHeartbeatClampMessageMethod.Invoke(null, new object?[] { 120, 60 });
        var text = Assert.IsType<string>(result);

        Assert.Contains("requested model heartbeat seconds (120)", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("adjusted to 60", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("0..60", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SelectLocalCompatibleModel_PrefersLoadedDefaultEntry() {
        var models = new[] {
            CreateModel("m1", "model-a", isDefault: false, runtimeState: "loaded"),
            CreateModel("m2", "model-b", isDefault: true, runtimeState: "loaded"),
            CreateModel("m3", "model-c", isDefault: true, runtimeState: "not-loaded")
        };

        var selected = ChatServiceSession.SelectLocalCompatibleModel(models);
        Assert.Equal("model-b", selected);
    }

    [Fact]
    public void IsLoopbackEndpoint_DetectsLocalhostUrls() {
        Assert.True(ChatServiceSession.IsLoopbackEndpoint("http://127.0.0.1:1234/v1"));
        Assert.True(ChatServiceSession.IsLoopbackEndpoint("http://localhost:11434/v1"));
        Assert.False(ChatServiceSession.IsLoopbackEndpoint("https://api.openai.com/v1"));
    }

    [Fact]
    public void BuildNoTextResponseFallbackText_IncludesModelAndEndpointForCompatibleTransport() {
        var text = ChatServiceSession.BuildNoTextResponseFallbackText(
            model: "local-model",
            transport: OpenAITransportKind.CompatibleHttp,
            baseUrl: "http://127.0.0.1:1234/v1");

        Assert.Contains("No response text was produced", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("local-model", text, StringComparison.Ordinal);
        Assert.Contains("127.0.0.1:1234", text, StringComparison.Ordinal);
    }

    [Fact]
    public void LooksLikeRuntimeControlPayloadArtifact_DetectsChannelEnvelopeShape() {
        var payload = "<|channel|>commentary to=ad_pack_info\n<|constrain|>json<|message|>{}";
        var result = ChatServiceSession.LooksLikeRuntimeControlPayloadArtifact(payload);
        Assert.True(result);
    }

    [Fact]
    public void LooksLikeRuntimeControlPayloadArtifact_DoesNotFlagRegularAssistantText() {
        var payload = "Sure - I can check replication health. Please share the domain or forest name.";
        var result = ChatServiceSession.LooksLikeRuntimeControlPayloadArtifact(payload);
        Assert.False(result);
    }

    [Theory]
    [InlineData("show latest failed logons", "ok", false, false, 0, 1, false)]
    [InlineData("show latest failed logons", "Findings: 4625 events are highest on DC01 between 02:00 and 05:00 UTC. Top source hosts are APP-17 and APP-22. Service account svc-backup is responsible for most failures and appears to have an outdated secret rotation. I already correlated event IDs 4625 and 4740, and lockouts are isolated to one OU. Recommended next step is to rotate the secret and re-run the report with a 6-hour window.", false, true, 0, 1, false)]
    [InlineData("Could you check events from AD0, AD1 and AD2 for the last 24 hours and summarize the main risks and priorities to address first?", "Findings: 4625 events are highest on DC01 between 02:00 and 05:00 UTC. Top source hosts are APP-17 and APP-22. Service account svc-backup is responsible for most failures and appears to have an outdated secret rotation. I already correlated event IDs 4625 and 4740, and lockouts are isolated to one OU. Recommended next step is to rotate the secret and re-run the report with a 6-hour window.", true, true, 0, 1, true)]
    [InlineData("show latest failed logons", "ok", true, false, 0, 1, false)]
    [InlineData("show latest failed logons", "ok", false, false, 1, 1, false)]
    public void ResponseQualityReviewEval_ScenariosMatchExpectedDecision(
        string userRequest,
        string assistantDraft,
        bool executionContractApplies,
        bool hasToolActivity,
        int reviewPassesUsed,
        int maxReviewPasses,
        bool expected) {
        var result = ChatServiceSession.ShouldAttemptResponseQualityReview(
            userRequest,
            assistantDraft,
            executionContractApplies,
            hasToolActivity,
            reviewPassesUsed,
            maxReviewPasses);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void ResponseQualityReviewEval_AllowsToolBackedLongDraftWithUnicodeQuestionSignal() {
        var draft =
            "one two three four five six seven eight nine ten eleven twelve thirteen fourteen fifteen sixteen seventeen eighteen nineteen twenty " +
            "twentyone twentytwo twentythree twentyfour twentyfive twentysix twentyseven twentyeight twentynine thirty thirtyone thirtytwo " +
            "thirtythree thirtyfour thirtyfive thirtysix thirtyseven thirtyeight thirtynine forty？";

        var result = ChatServiceSession.ShouldAttemptResponseQualityReview(
            "Could you check events from AD0, AD1 and AD2 for the last 24 hours and summarize the main risks and priorities to address first?",
            draft,
            executionContractApplies: true,
            hasToolActivity: true,
            reviewPassesUsed: 0,
            maxReviewPasses: 1);

        Assert.True(result);
    }

    [Fact]
    public void SmartReviewDeltaBuffer_EnablesForActionOrientedRequestsWithReviewPasses() {
        var request = new ChatRequest {
            RequestId = "req",
            Text = "Could you check events from AD0, AD1, and AD2 for the last 24 hours, list relevant failures, and summarize the top risks to fix first?",
            Options = new ChatRequestOptions {
                PlanExecuteReviewLoop = true
            }
        };

        var result = ChatServiceSession.ShouldBufferDraftDeltasForSmartReview(request);
        Assert.True(result);
    }

    [Fact]
    public void SmartReviewDeltaBuffer_DisablesWhenPlanExecuteReviewLoopIsUnset() {
        var request = new ChatRequest {
            RequestId = "req",
            Text = "Could you check events from AD0, AD1, and AD2 for the last 24 hours, list relevant failures, and summarize the top risks to fix first?",
            Options = new ChatRequestOptions()
        };

        var result = ChatServiceSession.ShouldBufferDraftDeltasForSmartReview(request);
        Assert.False(result);
    }

    [Fact]
    public void SmartReviewDeltaBuffer_DisablesWhenReviewPassesAreOff() {
        var request = new ChatRequest {
            RequestId = "req",
            Text = "Could you check events from AD0, AD1, and AD2 for the last 24 hours, list relevant failures, and summarize the top risks to fix first?",
            Options = new ChatRequestOptions {
                MaxReviewPasses = 0
            }
        };

        var result = ChatServiceSession.ShouldBufferDraftDeltasForSmartReview(request);
        Assert.False(result);
    }

    [Fact]
    public void SmartReviewDeltaBuffer_DisablesForCasualTurns() {
        var request = new ChatRequest {
            RequestId = "req",
            Text = "Hello Kai",
            Options = new ChatRequestOptions()
        };

        var result = ChatServiceSession.ShouldBufferDraftDeltasForSmartReview(request);
        Assert.False(result);
    }

    [Fact]
    public void SmartReviewDeltaBuffer_DisablesWhenPlanExecuteReviewLoopIsOff() {
        var request = new ChatRequest {
            RequestId = "req",
            Text = "Could you check events from AD0, AD1, and AD2 for the last 24 hours, list relevant failures, and summarize the top risks to fix first?",
            Options = new ChatRequestOptions {
                PlanExecuteReviewLoop = false
            }
        };

        var result = ChatServiceSession.ShouldBufferDraftDeltasForSmartReview(request);
        Assert.False(result);
    }

    [Fact]
    public void BuildResponseQualityReviewPrompt_EmitsStableMarkerAndPassMetadata() {
        var text = ChatServiceSession.BuildResponseQualityReviewPrompt("run diagnostics on dc01", "ok", false, 1, 2);

        Assert.Contains("ix:response-review:v1", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Review pass 1/2", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Tool activity this turn: none.", text, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("[Proactive execution mode]\nix:proactive-mode:v1\nenabled: true", true, true)]
    [InlineData("[Proactive execution mode]\nix:proactive-mode:v1\nenabled: false", true, false)]
    [InlineData("[Proactive execution mode]\nix:proactive-mode:v1\nenabled:true", true, true)]
    [InlineData("[Proactive execution mode]\nix:proactive-mode:v1\nenabled : FALSE", true, false)]
    [InlineData("[Proactive execution mode]\nix:proactive-mode:v1\nenabled:\t\"true\"", true, true)]
    [InlineData("[Proactive execution mode]\nix:proactive-mode:v1\nenabled\uFF1A false", true, false)]
    [InlineData("no marker", false, false)]
    public void TryReadProactiveModeFromRequestText_ParsesStructuredMarker(string requestText, bool expectedRead, bool expectedEnabled) {
        var result = ChatServiceSession.TryReadProactiveModeFromRequestText(requestText, out var enabled);
        Assert.Equal(expectedRead, result);
        Assert.Equal(expectedEnabled, enabled);
    }

    [Fact]
    public void TryReadProactiveModeFromRequestText_ParsesEnabledLineBeyondLegacyTailWindow() {
        var requestText = "[Proactive execution mode]\nix:proactive-mode:v1\n" + new string('x', 420) + "\nenabled: true";

        var result = ChatServiceSession.TryReadProactiveModeFromRequestText(requestText, out var enabled);

        Assert.True(result);
        Assert.True(enabled);
    }

    [Fact]
    public void TryReadProactiveModeFromRequestText_DoesNotCrossIntoNextStructuredSection() {
        var requestText = """
                          [Proactive execution mode]
                          ix:proactive-mode:v1
                          note: keep parsing local

                          [Persistent memory]
                          enabled: true
                          """;

        var result = ChatServiceSession.TryReadProactiveModeFromRequestText(requestText, out var enabled);

        Assert.False(result);
        Assert.False(enabled);
    }

    [Theory]
    [InlineData("[Proactive execution mode]\nix:proactive-mode:v1\nenabled: \"true")]
    [InlineData("[Proactive execution mode]\nix:proactive-mode:v1\nenabled: true\"")]
    public void TryReadProactiveModeFromRequestText_IgnoresMalformedQuotedValues(string requestText) {
        var result = ChatServiceSession.TryReadProactiveModeFromRequestText(requestText, out var enabled);

        Assert.False(result);
        Assert.False(enabled);
    }

    [Theory]
    [InlineData(true, true, false, false, false, "Findings summary without actions.", true)]
    [InlineData(true, true, false, false, false, "Findings summary. Do you want me to continue?", false)]
    [InlineData(false, true, false, false, false, "Findings summary without actions.", false)]
    [InlineData(true, false, false, false, false, "Findings summary without actions.", false)]
    [InlineData(true, true, true, false, false, "Findings summary without actions.", false)]
    [InlineData(true, true, false, false, false, "[Action]\nix:action:v1\nid: act_read\nmutating: false\nreply: /act act_read", true)]
    [InlineData(true, true, false, false, false, "[Action]\nix:action:v1\nid: act_unknown\nreply: /act act_unknown", true)]
    [InlineData(true, true, false, false, false, "[Action]\nix:action:v1\nid: act_mut\nmutating: true\nreply: /act act_mut", false)]
    [InlineData(true, true, false, false, false, "I can continue once I get one minimal input:\n- time window\n- target DC list\n- then I will execute immediately", false)]
    [InlineData(true, true, false, true, false, "Findings summary without actions.", false)]
    [InlineData(true, true, false, false, true, "Findings summary without actions.", false)]
    public void ShouldAttemptProactiveFollowUpReview_RespectsToolActivityAndActionBlocks(
        bool proactiveModeEnabled,
        bool hasToolActivity,
        bool proactiveFollowUpUsed,
        bool continuationFollowUpTurn,
        bool compactFollowUpTurn,
        string assistantDraft,
        bool expected) {
        var result = ChatServiceSession.ShouldAttemptProactiveFollowUpReview(
            proactiveModeEnabled,
            hasToolActivity,
            proactiveFollowUpUsed,
            continuationFollowUpTurn,
            compactFollowUpTurn,
            assistantDraft);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void ResolveProactiveFollowUpReviewDecision_SkipsWhenMutatingPendingActionExistsAndReportsMutabilityCounts() {
        var draft = """
            [Action]
            ix:action:v1
            id: act_read
            mutating: false
            reply: /act act_read

            [Action]
            ix:action:v1
            id: act_unknown
            reply: /act act_unknown

            [Action]
            ix:action:v1
            id: act_mut
            mutating: true
            reply: /act act_mut
            """;

        var decision = ChatServiceSession.ResolveProactiveFollowUpReviewDecision(
            proactiveModeEnabled: true,
            hasToolActivity: true,
            proactiveFollowUpUsed: false,
            continuationFollowUpTurn: false,
            compactFollowUpTurn: false,
            assistantDraft: draft);

        Assert.False(decision.ShouldAttempt);
        Assert.Equal("skip_pending_mutating_actions", decision.Reason);
        Assert.Equal(1, decision.PendingReadOnlyCount);
        Assert.Equal(1, decision.PendingUnknownCount);
        Assert.Equal(1, decision.PendingMutatingCount);
    }

    [Fact]
    public void ResolveProactiveFollowUpReviewDecision_AllowsNonMutatingPendingActionsAndReportsMutabilityCounts() {
        var draft = """
            [Action]
            ix:action:v1
            id: act_read
            mutating: false
            reply: /act act_read

            [Action]
            ix:action:v1
            id: act_unknown
            reply: /act act_unknown
            """;

        var decision = ChatServiceSession.ResolveProactiveFollowUpReviewDecision(
            proactiveModeEnabled: true,
            hasToolActivity: true,
            proactiveFollowUpUsed: false,
            continuationFollowUpTurn: false,
            compactFollowUpTurn: false,
            assistantDraft: draft);

        Assert.True(decision.ShouldAttempt);
        Assert.Equal("allow_pending_non_mutating_actions", decision.Reason);
        Assert.Equal(1, decision.PendingReadOnlyCount);
        Assert.Equal(1, decision.PendingUnknownCount);
        Assert.Equal(0, decision.PendingMutatingCount);
    }

    [Fact]
    public void ResolveAssistantTextBeforeNoTextFallback_ReusesPriorDraftWhenCurrentDraftIsEmptyAndToolActivityExists() {
        var resolved = ChatServiceSession.ResolveAssistantTextBeforeNoTextFallback(
            assistantDraft: " \n\t",
            lastNonEmptyAssistantDraft: "Cross-DC matrix ready: AD1 normal, AD2 unexpected reboot signal.",
            hasToolActivity: true);

        Assert.Equal("Cross-DC matrix ready: AD1 normal, AD2 unexpected reboot signal.", resolved);
    }

    [Fact]
    public void ResolveAssistantTextBeforeNoTextFallback_DoesNotReusePriorDraftWithoutToolActivity() {
        var resolved = ChatServiceSession.ResolveAssistantTextBeforeNoTextFallback(
            assistantDraft: string.Empty,
            lastNonEmptyAssistantDraft: "Prior draft",
            hasToolActivity: false);

        Assert.Equal(string.Empty, resolved);
    }

    [Theory]
    [InlineData("[warning] No response text was produced by the model.", true)]
    [InlineData("<|channel|>commentary\n<|message|>{}", true)]
    [InlineData("Usable prior draft", false)]
    public void ResolveAssistantTextBeforeNoTextFallback_SkipsInvalidPriorDraftShapes(string priorDraft, bool expectEmpty) {
        var resolved = ChatServiceSession.ResolveAssistantTextBeforeNoTextFallback(
            assistantDraft: string.Empty,
            lastNonEmptyAssistantDraft: priorDraft,
            hasToolActivity: true);

        if (expectEmpty) {
            Assert.Equal(string.Empty, resolved);
            return;
        }

        Assert.Equal("Usable prior draft", resolved);
    }

    [Fact]
    public void ResolveAssistantTextFromToolOutputsFallback_BuildsRecoveredSummaryFromToolMetadata() {
        var text = ChatServiceSession.ResolveAssistantTextFromToolOutputsFallback(
            assistantDraft: string.Empty,
            toolCalls: new[] {
                new ToolCallDto {
                    CallId = "call-1",
                    Name = "eventlog_live_query",
                    ArgumentsJson = "{}"
                }
            },
            toolOutputs: new[] {
                new ToolOutputDto {
                    CallId = "call-1",
                    Output = "{\"ok\":true}",
                    SummaryMarkdown = "Cross-DC check complete: AD1 healthy, AD2 has Event 41 signal."
                }
            });

        Assert.Contains("Recovered findings from executed tools", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("eventlog_live_query", text, StringComparison.Ordinal);
        Assert.Contains("AD2 has Event 41 signal", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveAssistantTextFromToolOutputsFallback_PreservesNonEmptyAssistantDraft() {
        var text = ChatServiceSession.ResolveAssistantTextFromToolOutputsFallback(
            assistantDraft: "Already have final response.",
            toolCalls: Array.Empty<ToolCallDto>(),
            toolOutputs: Array.Empty<ToolOutputDto>());

        Assert.Equal("Already have final response.", text);
    }

    [Fact]
    public void ResolveAssistantTextFromToolOutputsFallback_IgnoresNullCallAndOutputEntries() {
        var text = ChatServiceSession.ResolveAssistantTextFromToolOutputsFallback(
            assistantDraft: string.Empty,
            toolCalls: new List<ToolCallDto?> {
                null,
                new ToolCallDto {
                    CallId = "call-2",
                    Name = "ad_user_find",
                    ArgumentsJson = "{}"
                }
            },
            toolOutputs: new List<ToolOutputDto?> {
                null,
                new ToolOutputDto {
                    CallId = "call-2",
                    Output = "{\"ok\":true}",
                    SummaryMarkdown = "Found 12 matching users in requested scope."
                }
            });

        Assert.Contains("Recovered findings from executed tools", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ad_user_find", text, StringComparison.Ordinal);
        Assert.Contains("Found 12 matching users", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveAssistantTextFromToolOutputsFallback_ReturnsEmptyWhenOnlyNullOutputsArePresent() {
        var text = ChatServiceSession.ResolveAssistantTextFromToolOutputsFallback(
            assistantDraft: string.Empty,
            toolCalls: Array.Empty<ToolCallDto?>(),
            toolOutputs: new List<ToolOutputDto?> {
                null,
                null
            });

        Assert.Equal(string.Empty, text);
    }

    [Fact]
    public void ResolveAssistantTextFromToolOutputsFallback_RemainingCountTracksUsableOutputsOnly() {
        var text = ChatServiceSession.ResolveAssistantTextFromToolOutputsFallback(
            assistantDraft: string.Empty,
            toolCalls: new List<ToolCallDto?> {
                new ToolCallDto { CallId = "c1", Name = "tool_a", ArgumentsJson = "{}" },
                new ToolCallDto { CallId = "c2", Name = "tool_b", ArgumentsJson = "{}" },
                new ToolCallDto { CallId = "c3", Name = "tool_c", ArgumentsJson = "{}" },
                new ToolCallDto { CallId = "c4", Name = "tool_d", ArgumentsJson = "{}" },
                new ToolCallDto { CallId = "c5", Name = "tool_e", ArgumentsJson = "{}" }
            },
            toolOutputs: new List<ToolOutputDto?> {
                new ToolOutputDto { CallId = "c1", Output = "{\"ok\":true}", SummaryMarkdown = "A" },
                null,
                new ToolOutputDto { CallId = "c2", Output = "{\"ok\":true}", SummaryMarkdown = "B" },
                new ToolOutputDto { CallId = "c3", Output = "{\"ok\":true}", SummaryMarkdown = "C" },
                null,
                new ToolOutputDto { CallId = "c4", Output = "{\"ok\":true}", SummaryMarkdown = "D" },
                new ToolOutputDto { CallId = "c5", Output = "{\"ok\":true}", SummaryMarkdown = "E" }
            });

        Assert.Contains("Recovered findings from executed tools", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("... and 2 more tool output(s).", text, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_EmitsStableMarkersAndAvoidsDefaultVisualInsertion() {
        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt("analyze failed logons", "Current findings...");

        Assert.Contains("ix:proactive-followup:v1", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ix:proactive-visualization:v1", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("allow_new_visuals: false", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("request_has_visual_contract: false", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("natural and conversational", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("do not introduce new mermaid/ix-chart/ix-network blocks", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("do not force that label on every line", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("avoid repeating rigid templates", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("signal -> why it matters -> exact next validation/fix action", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Signal <text> -> Why it matters: <text> -> Next action: <text>", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("hidden regressions", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_AllowsOneVisualWhenStructuredVisualContractIsPresent() {
        var request = """
            Build the summary and include this visual contract if useful:
            ```ix-network
            {"nodes":[],"edges":[]}
            ```
            """;
        var draft = """
            Current findings:
            - lockouts concentrated on one OU
            """;

        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, draft);

        Assert.Contains("allow_new_visuals: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("request_has_visual_contract: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual: ix-network", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("include at most 1 new visual block(s)", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_DoesNotEnableVisualsForPlainKeywordMentions() {
        var request = "Could you include a mermaid diagram if useful?";
        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...");

        Assert.Contains("allow_new_visuals: false", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("request_has_visual_contract: false", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_AllowsVisualsForBacktickedLegacyNetworkToken() {
        var request = "If needed, use `visnetwork` for relationship mapping.";
        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...");

        Assert.Contains("allow_new_visuals: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("request_has_visual_contract: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual: ix-network", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_AllowsVisualsForBacktickedTableToken() {
        var request = "If useful, return a compact `table` for quick comparison.";
        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...");

        Assert.Contains("allow_new_visuals: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("request_has_visual_contract: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual: table", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_AllowsVisualsForBacktickedChartAliasToken() {
        var request = "If needed, include a compact `chart` for trend comparison.";
        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...");

        Assert.Contains("allow_new_visuals: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("request_has_visual_contract: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual: ix-chart", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_AllowsVisualsForBacktickedDiagramAliasToken() {
        var request = "If needed, include a relationship `diagram`.";
        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...");

        Assert.Contains("allow_new_visuals: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("request_has_visual_contract: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual: mermaid", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_AllowsVisualsForBacktickedMarkdownTableAliasToken() {
        var request = "If useful, return a compact `markdown-table` summary.";
        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...");

        Assert.Contains("allow_new_visuals: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("request_has_visual_contract: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual: table", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_AllowsVisualsForWhitespacePrefixedFenceLanguage() {
        var request = """
            Build a relationship summary with explicit contract:
            ``` ix-network
            {"nodes":[],"edges":[]}
            ```
            """;
        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...");

        Assert.Contains("allow_new_visuals: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("request_has_visual_contract: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual: ix-network", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_AllowsVisualsForTildeFenceLanguage() {
        var request = """
            Render network evidence if needed:
            ~~~ix-network
            {"nodes":[],"edges":[]}
            ~~~
            """;
        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...");

        Assert.Contains("allow_new_visuals: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("request_has_visual_contract: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual: ix-network", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_AllowsVisualsForDoubleBacktickedLegacyNetworkToken() {
        var request = "Use ``visnetwork`` only if relationship mapping is necessary.";
        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...");

        Assert.Contains("allow_new_visuals: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("request_has_visual_contract: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preferred_visual: ix-network", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_DoesNotEnableVisualsForMismatchedInlineBacktickDelimiters() {
        var request = "Use ``visnetwork``` only when relationship mapping is required.";
        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...");

        Assert.Contains("allow_new_visuals: false", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("request_has_visual_contract: false", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProactiveFollowUpReviewPrompt_DetectsValidTokenAfterMalformedInlineSequence() {
        var request = "Malformed token first: ``visnetwork` then valid: `visnetwork`.";
        var text = ChatServiceSession.BuildProactiveFollowUpReviewPrompt(request, "Current findings...");

        Assert.Contains("allow_new_visuals: true", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("request_has_visual_contract: true", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CopyChatOptionsWithoutTools_DisablesToolExecutionForReviewPasses() {
        var source = new ChatOptions {
            Model = "gpt-test",
            NewThread = true,
            ParallelToolCalls = true,
            ToolChoice = ToolChoice.Auto
        };

        var copy = ChatServiceSession.CopyChatOptionsWithoutTools(source, false);

        Assert.Null(copy.Tools);
        Assert.Null(copy.ToolChoice);
        Assert.False(copy.ParallelToolCalls);
        Assert.False(copy.NewThread);
        Assert.Equal("gpt-test", copy.Model);

        // Ensure review-option copying does not mutate the original model options.
        Assert.True(source.NewThread);
        Assert.Equal(ToolChoice.Auto, source.ToolChoice);
        Assert.True(source.ParallelToolCalls);
    }

    private static ModelInfo CreateModel(string id, string model, bool isDefault, string runtimeState) {
        return new ModelInfo(
            id: id,
            model: model,
            displayName: model,
            description: string.Empty,
            supportedReasoningEfforts: Array.Empty<ReasoningEffortOption>(),
            defaultReasoningEffort: null,
            isDefault: isDefault,
            raw: new JsonObject(),
            additional: null,
            ownedBy: null,
            publisher: null,
            architecture: null,
            quantization: null,
            compatibilityType: null,
            runtimeState: runtimeState,
            modelType: null,
            maxContextLength: null,
            loadedContextLength: null,
            capabilities: Array.Empty<string>());
    }
}
