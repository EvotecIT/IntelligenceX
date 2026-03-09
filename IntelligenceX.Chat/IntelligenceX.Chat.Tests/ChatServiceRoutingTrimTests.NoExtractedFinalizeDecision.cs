using System.Collections.Generic;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Service;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class ChatServiceRoutingTrimTests {

    [Fact]
    public void ResolveNoExtractedFinalizeContinuationDecisionForTesting_PrefersStructuredNextActionRetry() {
        var schema = ToolSchema.Object().NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("ad_environment_discover", "discover", schema),
            new("ad_scope_discovery", "scope", schema)
        };
        var toolCalls = new List<ToolCallDto> {
            new() { CallId = "call-1", Name = "ad_environment_discover" }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-1",
                Output = """
                         {"ok":true,"next_actions":[{"tool":"ad_scope_discovery","reason":"limited dc inventory","arguments":{"discovery_fallback":"current_forest"}}]}
                         """,
                Ok = true
            }
        };
        var assistantDraft = """
            I started, but I hit one blocker:
            - discovery returned only one domain controller.
            - I need one more follow-up step before the comparison is complete.
            """;

        var result = ChatServiceSession.ResolveNoExtractedFinalizeContinuationDecisionForTesting(
            toolDefinitions: toolDefinitions,
            toolCalls: toolCalls,
            toolOutputs: toolOutputs,
            continuationFollowUpTurn: true,
            userRequest: "Check the remaining controllers too.",
            assistantDraft: assistantDraft,
            structuredNextActionRetryUsed: false,
            toolProgressRecoveryUsed: false,
            assistantDraftToolCalls: 0);

        Assert.Equal("StructuredNextActionRetry", result.Kind);
        Assert.Equal("structured_next_action_found", result.Reason);
        Assert.True(result.ExpandToFullToolAvailability);
        Assert.Equal("ad_scope_discovery", result.PreferredToolName);
    }

    [Fact]
    public void ResolveNoExtractedFinalizeContinuationDecisionForTesting_FallsBackToToolProgressRecoveryAfterStructuredRetryBudget() {
        var schema = ToolSchema.Object().NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("ad_environment_discover", "discover", schema),
            new("ad_scope_discovery", "scope", schema)
        };
        var toolCalls = new List<ToolCallDto> {
            new() { CallId = "call-1", Name = "ad_environment_discover" }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-1",
                Output = """
                         {"ok":true,"next_actions":[{"tool":"ad_scope_discovery","reason":"limited dc inventory","arguments":{"discovery_fallback":"current_forest"}}]}
                         """,
                Ok = true
            }
        };
        var assistantDraft = """
            I started, but I hit one blocker:
            - discovery returned only one domain controller.
            - I need one more follow-up step before the comparison is complete.
            """;

        var result = ChatServiceSession.ResolveNoExtractedFinalizeContinuationDecisionForTesting(
            toolDefinitions: toolDefinitions,
            toolCalls: toolCalls,
            toolOutputs: toolOutputs,
            continuationFollowUpTurn: true,
            userRequest: "Check the remaining controllers too.",
            assistantDraft: assistantDraft,
            structuredNextActionRetryUsed: true,
            toolProgressRecoveryUsed: false,
            assistantDraftToolCalls: 0);

        Assert.Equal("ToolProgressRecovery", result.Kind);
        Assert.Equal("blocker_like_draft_after_tool_activity", result.Reason);
        Assert.True(result.ExpandToFullToolAvailability);
        Assert.Null(result.PreferredToolName);
    }

    [Fact]
    public void ResolveNoExtractedFinalizeReviewDecisionForTesting_PrefersResponseQualityReviewBeforeProactiveFollowUp() {
        var assistantDraft = """
            Cross-domain review is nearly complete. I compared the eventlog traces, reconciled reboot timing,
            mapped controller reachability, and summarized the remaining evidence so the final response can stay
            concise, direct, and aligned with the verified tool results from this turn without inventing any data.
            """;

        var result = ChatServiceSession.ResolveNoExtractedFinalizeReviewDecisionForTesting(
            noResultWatchdogTriggered: false,
            planExecuteReviewLoop: true,
            maxReviewPasses: 2,
            reviewPassesUsed: 0,
            userRequest: "Compare the reboot evidence across all discovered controllers, explain the anomaly pattern, and summarize the validated result.",
            assistantDraft: assistantDraft,
            executionContractApplies: false,
            hasToolActivity: true,
            proactiveModeEnabled: true,
            proactiveFollowUpUsed: false,
            continuationFollowUpTurn: false,
            compactFollowUpTurn: false);

        Assert.Equal("ResponseQualityReview", result.Kind);
        Assert.Equal("response_quality_review", result.Reason);
        Assert.Equal(1, result.ReviewPassNumber);
    }

    [Fact]
    public void ResolveNoExtractedFinalizeReviewDecisionForTesting_FallsBackToProactiveFollowUpWhenReviewLoopIsDisabled() {
        var assistantDraft = """
            Domain controller comparison finished. AD1 is healthy, AD2 shows the reboot anomaly, and the current
            evidence is ready for a short follow-up pass that can propose the next safest checks without changing scope.
            """;

        var result = ChatServiceSession.ResolveNoExtractedFinalizeReviewDecisionForTesting(
            noResultWatchdogTriggered: false,
            planExecuteReviewLoop: false,
            maxReviewPasses: 2,
            reviewPassesUsed: 0,
            userRequest: "Compare the reboot evidence across all discovered controllers, explain the anomaly pattern, and summarize the validated result.",
            assistantDraft: assistantDraft,
            executionContractApplies: false,
            hasToolActivity: true,
            proactiveModeEnabled: true,
            proactiveFollowUpUsed: false,
            continuationFollowUpTurn: false,
            compactFollowUpTurn: false);

        Assert.Equal("ProactiveFollowUpReview", result.Kind);
        Assert.Equal("allow_no_pending_actions", result.Reason);
        Assert.Equal(0, result.ReviewPassNumber);
    }

    [Fact]
    public void ResolveNoExtractedFinalizeReviewDecisionForTesting_RequiresRequestedArtifactEvenOnFollowUpTurn() {
        var assistantDraft = """
            Replication is healthy across all discovered controllers.
            LDAP checks passed on the FQDN endpoints.
            """;

        var result = ChatServiceSession.ResolveNoExtractedFinalizeReviewDecisionForTesting(
            noResultWatchdogTriggered: false,
            planExecuteReviewLoop: false,
            maxReviewPasses: 1,
            reviewPassesUsed: 0,
            userRequest: "Could you show me a table and diagram for replication?",
            assistantDraft: assistantDraft,
            executionContractApplies: false,
            hasToolActivity: true,
            proactiveModeEnabled: true,
            proactiveFollowUpUsed: false,
            continuationFollowUpTurn: true,
            compactFollowUpTurn: true);

        Assert.Equal("ProactiveFollowUpReview", result.Kind);
        Assert.Equal("allow_requested_artifact_missing", result.Reason);
        Assert.Equal(0, result.ReviewPassNumber);
    }

    [Fact]
    public void ResolveNoExtractedFinalizeReviewDecisionForTesting_AllowsArtifactAlreadyVisibleAboveWhenPlanJustifiesOmission() {
        var assistantDraft = """
            [Answer progression plan]
            ix:answer-plan:v1
            user_goal: explain why ADRODC is missing from the table above
            resolved_so_far: the compact table is already visible above
            unresolved_now: explain the missing row
            primary_artifact: prose
            requested_artifact_already_visible_above: true
            requested_artifact_visibility_reason: the table above is already visible, so repeating it adds no value
            repeats_prior_visible_content: false
            prior_visible_delta_reason: none
            reuse_prior_visuals: false
            reuse_reason: none
            repeat_adds_new_information: true
            repeat_novelty_reason: none
            advances_current_ask: true
            advance_reason: explains the missing row without redrawing the table

            The table above already shows the returned rows. ADRODC is absent because the collector output was partial.
            """;

        var result = ChatServiceSession.ResolveNoExtractedFinalizeReviewDecisionForTesting(
            noResultWatchdogTriggered: false,
            planExecuteReviewLoop: false,
            maxReviewPasses: 1,
            reviewPassesUsed: 0,
            userRequest: "use the table above and explain why ADRODC is missing",
            assistantDraft: assistantDraft,
            executionContractApplies: false,
            hasToolActivity: true,
            proactiveModeEnabled: true,
            proactiveFollowUpUsed: false,
            continuationFollowUpTurn: true,
            compactFollowUpTurn: true);

        Assert.Equal("None", result.Kind);
        Assert.Equal("review_loop_disabled", result.Reason);
        Assert.Equal(0, result.ReviewPassNumber);
    }

    [Fact]
    public void ResolveNoExtractedFinalizeReviewDecisionForTesting_RejectsTableOnlyDraftThatAlsoIncludesDiagram() {
        var assistantDraft = """
            | Server | Health |
            | --- | --- |
            | AD0 | healthy |

            ```mermaid
            flowchart TD
              AD0 --> AD1
            ```
            """;

        var result = ChatServiceSession.ResolveNoExtractedFinalizeReviewDecisionForTesting(
            noResultWatchdogTriggered: false,
            planExecuteReviewLoop: false,
            maxReviewPasses: 1,
            reviewPassesUsed: 0,
            userRequest: "show only the replication table",
            assistantDraft: assistantDraft,
            executionContractApplies: false,
            hasToolActivity: true,
            proactiveModeEnabled: true,
            proactiveFollowUpUsed: false,
            continuationFollowUpTurn: false,
            compactFollowUpTurn: false);

        Assert.Equal("ProactiveFollowUpReview", result.Kind);
        Assert.Equal("allow_requested_artifact_missing", result.Reason);
        Assert.Equal(0, result.ReviewPassNumber);
    }

    [Fact]
    public void ResolveNoExtractedFinalizeNoTextDecisionForTesting_SelectsToolOutputSynthesisRetry() {
        var result = ChatServiceSession.ResolveNoExtractedFinalizeNoTextDecisionForTesting(
            noTextToolOutputDirectRetryUsed: false,
            planExecuteReviewLoop: true,
            redactEnabled: false,
            hasSuccessfulToolOutput: true,
            toolOutputsCount: 1,
            assistantDraft: string.Empty,
            localNoTextDirectRetryUsed: false,
            isLocalCompatibleLoopback: true,
            availableToolCount: 2,
            priorToolCalls: 0,
            userRequest: "Summarize the tool result.");

        Assert.Equal("ToolOutputSynthesisRetry", result.Kind);
        Assert.Equal("tool_output_synthesis_retry", result.Reason);
    }

    [Fact]
    public void ResolveNoExtractedFinalizeNoTextDecisionForTesting_SelectsLocalDirectRetryWhenNoToolOutputNarrativeExists() {
        var result = ChatServiceSession.ResolveNoExtractedFinalizeNoTextDecisionForTesting(
            noTextToolOutputDirectRetryUsed: false,
            planExecuteReviewLoop: true,
            redactEnabled: false,
            hasSuccessfulToolOutput: false,
            toolOutputsCount: 0,
            assistantDraft: string.Empty,
            localNoTextDirectRetryUsed: false,
            isLocalCompatibleLoopback: true,
            availableToolCount: 2,
            priorToolCalls: 0,
            userRequest: "Summarize the tool result.");

        Assert.Equal("LocalDirectRetry", result.Kind);
        Assert.Equal("local_no_text_direct_retry", result.Reason);
    }

    [Fact]
    public void ResolveNoExtractedFinalizeNoTextOutcomeForTesting_PrefersLocalToolOutputFallbackBeforeRetryDecision() {
        var result = ChatServiceSession.ResolveNoExtractedFinalizeNoTextOutcomeForTesting(
            noTextToolOutputDirectRetryUsed: false,
            planExecuteReviewLoop: true,
            redactEnabled: false,
            hasSuccessfulToolOutput: true,
            assistantDraft: string.Empty,
            localNoTextDirectRetryUsed: false,
            isLocalCompatibleLoopback: true,
            availableToolCount: 2,
            priorToolCalls: 1,
            userRequest: "Summarize the tool result.");

        Assert.Contains("Recovered findings from executed tools", result.AssistantDraft, StringComparison.Ordinal);
        Assert.Equal("None", result.Kind);
        Assert.Equal("no_no_text_recovery_selected", result.Reason);
    }
}
