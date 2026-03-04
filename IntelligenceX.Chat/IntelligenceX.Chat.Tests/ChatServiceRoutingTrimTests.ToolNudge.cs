using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Service;
using IntelligenceX.Json;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class ChatServiceRoutingTrimTests {

    [Fact]
    public void ShouldAttemptToolExecutionNudge_TriggersForActionSelectionJsonEvenWithoutContinuationSubset() {
        var userRequest = "{\"ix_action_selection\":{\"id\":\"act_001\",\"title\":\"Run forest probe\",\"request\":\"Run it.\"}}";
        var assistantDraft = "Ok, doing it now.";

        var result = ShouldAttemptToolExecutionNudgeMethod.Invoke(
            null,
            new object?[] { userRequest, assistantDraft, true, 0, 0, false });

        var value = Assert.IsType<bool>(result);
        Assert.True(value);
    }

    [Fact]
    public void ShouldAttemptToolExecutionNudge_TriggersForActionSelectionJsonWithCaseVariantKeys() {
        var userRequest = "{\"IX_Action_Selection\":{\"ActionId\":\"act_001\",\"Title\":\"Run forest probe\",\"Request\":\"Run it.\"}}";
        var assistantDraft = "Ok, doing it now.";

        var result = ShouldAttemptToolExecutionNudgeMethod.Invoke(
            null,
            new object?[] { userRequest, assistantDraft, true, 0, 0, false });

        var value = Assert.IsType<bool>(result);
        Assert.True(value);
    }

    [Fact]
    public void ShouldAttemptToolExecutionNudge_TriggersForFencedActionSelectionJsonPayload() {
        var userRequest = """
                          ```ix_action_selection
                          {"ix_action_selection":{"id":"act_001","title":"Run forest probe","request":"Run it."}}
                          ```
                          """;
        var assistantDraft = "Ok, doing it now.";

        var result = ShouldAttemptToolExecutionNudgeMethod.Invoke(
            null,
            new object?[] { userRequest, assistantDraft, true, 0, 0, false });

        var value = Assert.IsType<bool>(result);
        Assert.True(value);
    }

    [Fact]
    public void UserMatchesAssistantCallToAction_ParsesQuotedCtaWithFullwidthComma() {
        var method = typeof(ChatServiceSession).GetMethod(
            "UserMatchesAssistantCallToAction",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var userRequest = "继续执行";
        var assistantDraft = "请回复“继续执行”，我会马上执行并返回结果。";
        var result = method!.Invoke(null, new object?[] { userRequest, assistantDraft, false });

        Assert.True(Assert.IsType<bool>(result));
    }

    [Fact]
    public void UserMatchesAssistantCallToAction_ParsesQuoteOnlyLineWhenPreviousLineEndsWithFullwidthColon() {
        var method = typeof(ChatServiceSession).GetMethod(
            "UserMatchesAssistantCallToAction",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var userRequest = "继续执行";
        var assistantDraft = """
                             要继续请回复：
                             “继续执行”
                             """;
        var result = method!.Invoke(null, new object?[] { userRequest, assistantDraft, false });

        Assert.True(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldAttemptToolExecutionNudge_TriggersForNumericActionSelectionId() {
        var userRequest = "{\"ix_action_selection\":{\"id\":1,\"title\":\"Run\",\"request\":\"Run it.\"}}";
        var assistantDraft = "Ok, doing it now.";

        var result = ShouldAttemptToolExecutionNudgeMethod.Invoke(
            null,
            new object?[] { userRequest, assistantDraft, true, 0, 0, false });

        var value = Assert.IsType<bool>(result);
        Assert.True(value);
    }

    [Fact]
    public void ShouldAttemptToolExecutionNudge_DoesNotThrowOnInvalidActionSelectionJson() {
        var userRequest = "{";
        var assistantDraft = "Ok, doing it now.";

        var result = ShouldAttemptToolExecutionNudgeMethod.Invoke(
            null,
            new object?[] { userRequest, assistantDraft, true, 0, 0, false });

        var value = Assert.IsType<bool>(result);
        Assert.False(value);
    }

    [Fact]
    public void ShouldAttemptToolExecutionNudge_DoesNotTriggerForActionSelectionWithEmptyId() {
        var userRequest = "{\"ix_action_selection\":{\"id\":\"\",\"title\":\"Run\",\"request\":\"Run it.\"}}";
        var assistantDraft = "Ok, doing it now.";

        var result = ShouldAttemptToolExecutionNudgeMethod.Invoke(
            null,
            new object?[] { userRequest, assistantDraft, true, 0, 0, false });

        var value = Assert.IsType<bool>(result);
        Assert.False(value);
    }

    [Fact]
    public void ShouldAttemptToolExecutionNudge_DoesNotTriggerForActionSelectionWithZeroNumericId() {
        var userRequest = "{\"ix_action_selection\":{\"id\":0,\"title\":\"Run\",\"request\":\"Run it.\"}}";
        var assistantDraft = "Ok, doing it now.";

        var result = ShouldAttemptToolExecutionNudgeMethod.Invoke(
            null,
            new object?[] { userRequest, assistantDraft, true, 0, 0, false });

        var value = Assert.IsType<bool>(result);
        Assert.False(value);
    }

    [Fact]
    public void ShouldAttemptToolExecutionNudge_DoesNotTriggerForActionSelectionWhenSelectionIsNotObject() {
        var userRequest = "{\"ix_action_selection\":null}";
        var assistantDraft = "Ok, doing it now.";

        var result = ShouldAttemptToolExecutionNudgeMethod.Invoke(
            null,
            new object?[] { userRequest, assistantDraft, true, 0, 0, false });

        var value = Assert.IsType<bool>(result);
        Assert.False(value);
    }

    [Fact]
    public void ShouldAttemptToolExecutionNudge_DoesNotTriggerForActionSelectionWhenToolsUnavailable() {
        var userRequest = "{\"ix_action_selection\":{\"id\":\"act_001\",\"title\":\"Run\",\"request\":\"Run it.\"}}";
        var assistantDraft = "Ok, doing it now.";

        var result = ShouldAttemptToolExecutionNudgeMethod.Invoke(
            null,
            new object?[] { userRequest, assistantDraft, false, 0, 0, false });

        var value = Assert.IsType<bool>(result);
        Assert.False(value);
    }

    [Fact]
    public void ShouldAttemptToolExecutionNudge_DoesNotTriggerForActionSelectionWhenToolCallsAlreadyExist() {
        var userRequest = "{\"ix_action_selection\":{\"id\":\"act_001\",\"title\":\"Run\",\"request\":\"Run it.\"}}";
        var assistantDraft = "Ok, doing it now.";

        var result = ShouldAttemptToolExecutionNudgeMethod.Invoke(
            null,
            new object?[] { userRequest, assistantDraft, true, 1, 0, false });

        var value = Assert.IsType<bool>(result);
        Assert.False(value);
    }

    [Fact]
    public void ShouldAttemptToolExecutionNudge_TriggersForCompactFollowUpWithExecutionAckDraft() {
        var userRequest = "go ahead";
        var assistantDraft = "On it. Running the all-DC baseline now and returning the comparison matrix.";

        var args = new object?[] { userRequest, assistantDraft, true, 0, 0, false, true, null };
        var result = EvaluateToolExecutionNudgeDecisionMethod.Invoke(
            null,
            args);

        var value = Assert.IsType<bool>(result);
        Assert.True(value);
        Assert.Equal("compact_follow_up_execution_ack_draft", Assert.IsType<string>(args[7]));
    }

    [Fact]
    public void ShouldAttemptToolExecutionNudge_TriggersForCompactFollowUpWithMultilineBlockerDraft() {
        var userRequest = "go ahead";
        var assistantDraft = """
            Perfect — I started, and here is the blocker:
            - AD discovery returned one candidate only.
            - I need a DC list to run the all-DC comparison.
            """;

        var args = new object?[] { userRequest, assistantDraft, true, 0, 0, false, true, null };
        var result = EvaluateToolExecutionNudgeDecisionMethod.Invoke(
            null,
            args);

        var value = Assert.IsType<bool>(result);
        Assert.True(value);
        Assert.Equal("compact_follow_up_multiline_blocker_draft", Assert.IsType<string>(args[7]));
    }

    [Fact]
    public void ShouldAttemptToolExecutionNudge_TriggersForCompactFollowUpWithStructuredExecutionDeferredDraft() {
        var userRequest = "Verify LDAP connectivity across the forest and return a per-DC matrix.\nFollow-up: go ahead";
        var assistantDraft = """
            LDAP connectivity across the forest scope is confirmed.

            Evidence:
            - Forest: `ad.evotec.xyz`
            - Domains discovered: 2
            - DCs discovered: 5
            - Discovery status: sufficient

            LDAP connectivity status:
            - Only discovery evidence is currently present.
            - No per-DC port matrix has been produced yet.
            - Planned checks include LDAP 389 and LDAPS 636.
            - Planned checks include GC 3268 and 3269.
            - Diagnostic probe results are not included in this draft.
            """;

        var args = new object?[] { userRequest, assistantDraft, true, 0, 0, false, true, null };
        var result = EvaluateToolExecutionNudgeDecisionMethod.Invoke(
            null,
            args);

        var value = Assert.IsType<bool>(result);
        Assert.True(value);
        Assert.Equal("compact_follow_up_structured_execution_deferred_draft", Assert.IsType<string>(args[7]));
    }

    [Fact]
    public void ShouldAttemptToolExecutionNudge_DoesNotTriggerForCompactFollowUpStructuredDraftWithoutLinkedContext() {
        var userRequest = "go ahead";
        var assistantDraft = """
            Summary:
            Region A healthy | Region B healthy | Region C healthy.
            Latest rollup is complete.
            No anomalies detected in this summary.
            """;

        var args = new object?[] { userRequest, assistantDraft, true, 0, 0, false, true, null };
        var result = EvaluateToolExecutionNudgeDecisionMethod.Invoke(
            null,
            args);

        var value = Assert.IsType<bool>(result);
        Assert.False(value);
        Assert.Equal("no_continuation_subset_and_no_cta_or_contextual_follow_up", Assert.IsType<string>(args[7]));
    }

    [Fact]
    public void ShouldAttemptToolExecutionNudge_DoesNotTriggerForMultilineBlockerDraftWithoutCompactFollowUpHint() {
        var userRequest = "please continue";
        var assistantDraft = """
            Perfect — I started, and here is the blocker:
            - AD discovery returned one candidate only.
            - I need a DC list to run the all-DC comparison.
            """;

        var args = new object?[] { userRequest, assistantDraft, true, 0, 0, false, false, null };
        var result = EvaluateToolExecutionNudgeDecisionMethod.Invoke(
            null,
            args);

        var value = Assert.IsType<bool>(result);
        Assert.False(value);
        Assert.Equal("no_continuation_subset_and_no_cta_or_contextual_follow_up", Assert.IsType<string>(args[7]));
    }

    [Fact]
    public void ShouldAttemptToolExecutionNudge_TriggersForExecutionAckDraftReferencingRequestWithoutContinuationSubset() {
        var userRequest = "Find the user's latest authoritative lastLogon value by checking relevant DCs and return exact UTC timestamp plus source DC.";
        var assistantDraft = "I’ll query the user across the relevant DCs and return the max authoritative lastLogon timestamp plus source DC.";

        var args = new object?[] { userRequest, assistantDraft, true, 0, 0, false, false, null };
        var result = EvaluateToolExecutionNudgeDecisionMethod.Invoke(
            null,
            args);

        var value = Assert.IsType<bool>(result);
        Assert.True(value);
        var reason = Assert.IsType<string>(args[7]);
        Assert.True(
            string.Equals(reason, "execution_ack_draft_references_request", StringComparison.Ordinal)
            || string.Equals(reason, "assistant_draft_references_follow_up", StringComparison.Ordinal),
            "Unexpected reason: " + reason);
    }

    [Fact]
    public void ShouldAttemptToolExecutionNudge_TriggersForEmptyAssistantDraftWithSubstantialRequest() {
        var userRequest = "Find the user's latest authoritative lastLogon value by checking relevant DCs and return exact UTC timestamp plus source DC.";
        var assistantDraft = string.Empty;

        var args = new object?[] { userRequest, assistantDraft, true, 0, 0, false, false, null };
        var result = EvaluateToolExecutionNudgeDecisionMethod.Invoke(
            null,
            args);

        Assert.True(Assert.IsType<bool>(result));
        Assert.Equal("empty_assistant_draft_retry", Assert.IsType<string>(args[7]));
    }

    [Fact]
    public void ShouldAttemptToolExecutionNudge_DoesNotTriggerForEmptyAssistantDraftWithShortRequest() {
        var userRequest = "hi";
        var assistantDraft = string.Empty;

        var args = new object?[] { userRequest, assistantDraft, true, 0, 0, false, false, null };
        var result = EvaluateToolExecutionNudgeDecisionMethod.Invoke(
            null,
            args);

        Assert.False(Assert.IsType<bool>(result));
        Assert.Equal("empty_assistant_draft", Assert.IsType<string>(args[7]));
    }

    [Fact]
    public void ShouldAttemptToolExecutionNudge_TriggersForExecutionAckDraftReferencingRequestWithContinuationSubset() {
        var userRequest = "Find the user's latest authoritative lastLogon value by checking relevant DCs and return exact UTC timestamp plus source DC.";
        var assistantDraft = "I’ll query all relevant DCs and return the max authoritative lastLogon timestamp with source DC.";

        var args = new object?[] { userRequest, assistantDraft, true, 0, 0, true, false, null };
        var result = EvaluateToolExecutionNudgeDecisionMethod.Invoke(
            null,
            args);

        var value = Assert.IsType<bool>(result);
        Assert.True(value);
        Assert.Equal("execution_ack_draft_references_request", Assert.IsType<string>(args[7]));
    }

    [Fact]
    public void ShouldAttemptToolExecutionNudge_TriggersForStructuredContextualBlockerDraft() {
        var userRequest = "Find the user's latest authoritative lastLogon value by checking relevant DCs and return exact UTC timestamp plus source DC.";
        var assistantDraft = """
            I can do that.
            To get authoritative lastLogon, I need to query each relevant DC directly.
            - exact UTC timestamp
            - source DC with max value
            I will run this now.
            """;

        var args = new object?[] { userRequest, assistantDraft, true, 0, 0, true, false, null };
        var result = EvaluateToolExecutionNudgeDecisionMethod.Invoke(
            null,
            args);

        var value = Assert.IsType<bool>(result);
        Assert.True(value);
        var reason = Assert.IsType<string>(args[7]);
        Assert.True(
            string.Equals(reason, "structured_contextual_blocker_draft", StringComparison.Ordinal)
            || string.Equals(reason, "execution_intent_placeholder_draft", StringComparison.Ordinal),
            "Unexpected reason: " + reason);
    }

    [Fact]
    public void ShouldAttemptToolExecutionNudge_TriggersForStructuredScopeChoiceDraft() {
        var userRequest = "Find the user's latest authoritative lastLogon value by checking relevant DCs and return exact UTC timestamp plus source DC.";
        var assistantDraft = """
            Got it — I can do that, but I need one scope choice first because this account exists in `ad.evotec.xyz` while forest discovery also showed `ad.evotec.pl`.
            I’ll proceed in `ad.evotec.xyz` unless you want `ad.evotec.pl` instead.
            """;

        var args = new object?[] { userRequest, assistantDraft, true, 0, 0, false, false, null };
        var result = EvaluateToolExecutionNudgeDecisionMethod.Invoke(
            null,
            args);

        var value = Assert.IsType<bool>(result);
        Assert.True(value);
        Assert.Equal("structured_scope_choice_draft", Assert.IsType<string>(args[7]));
    }

    [Fact]
    public void ShouldAttemptToolExecutionNudge_DoesNotTriggerForStructuredScopeChoiceWithoutInlineOptions() {
        var userRequest = "Find the user's latest authoritative lastLogon value by checking relevant DCs and return exact UTC timestamp plus source DC.";
        var assistantDraft = """
            I can do that, but I need one scope choice first for this account.
            I will proceed with the default scope after you confirm.
            """;

        var args = new object?[] { userRequest, assistantDraft, true, 0, 0, false, false, null };
        var result = EvaluateToolExecutionNudgeDecisionMethod.Invoke(
            null,
            args);

        var value = Assert.IsType<bool>(result);
        Assert.False(value);
        Assert.Equal("no_continuation_subset_and_no_cta_or_contextual_follow_up", Assert.IsType<string>(args[7]));
    }

    [Fact]
    public void ShouldAttemptToolExecutionNudge_TriggersForLinkedFollowUpQuestionWithoutContinuationSubset() {
        var userRequest = "Find the user's latest authoritative lastLogon value by checking relevant DCs and return exact UTC timestamp plus source DC.";
        var assistantDraft = "To return the authoritative lastLogon with source DC, should I query only ad.evotec.xyz DCs or include both domains?";

        var args = new object?[] { userRequest, assistantDraft, true, 0, 0, false, false, null };
        var result = EvaluateToolExecutionNudgeDecisionMethod.Invoke(
            null,
            args);

        var value = Assert.IsType<bool>(result);
        Assert.True(value);
        Assert.Equal("assistant_question_linked_to_follow_up", Assert.IsType<string>(args[7]));
    }

    [Fact]
    public void ShouldAttemptToolExecutionNudge_TriggersForExecutionAckWithInlineCodeAndUnicodeApostrophe() {
        var userRequest = "Find the user's latest authoritative lastLogon value by checking relevant DCs and return exact UTC timestamp plus source DC.";
        var assistantDraft = "I’ll query all relevant DCs for this user’s non-replicated `lastLogon`, then return the latest exact UTC value and the DC that reported it.";

        var args = new object?[] { userRequest, assistantDraft, true, 0, 0, false, false, null };
        var result = EvaluateToolExecutionNudgeDecisionMethod.Invoke(
            null,
            args);

        var value = Assert.IsType<bool>(result);
        Assert.True(value);
        var reason = Assert.IsType<string>(args[7]);
        Assert.True(
            string.Equals(reason, "execution_ack_draft_references_request", StringComparison.Ordinal)
            || string.Equals(reason, "assistant_draft_references_follow_up", StringComparison.Ordinal),
            "Unexpected reason: " + reason);
    }

    [Fact]
    public void ShouldAttemptToolExecutionNudge_TriggersForExecutionAckWithMarkdownEmphasis() {
        var userRequest = "Find the user's latest authoritative lastLogon value by checking relevant DCs and return exact UTC timestamp plus source DC.";
        var assistantDraft = "Got it — I’ll query the user on each relevant writable DC and return the **latest authoritative `lastLogon`** with the exact UTC time and source DC.";

        var args = new object?[] { userRequest, assistantDraft, true, 0, 0, false, false, null };
        var result = EvaluateToolExecutionNudgeDecisionMethod.Invoke(
            null,
            args);

        var value = Assert.IsType<bool>(result);
        Assert.True(value);
    }

    [Fact]
    public void ShouldAttemptToolExecutionNudge_TriggersForRuntimeObservedExecutionAckDraft() {
        var userRequest = "Find the user's latest authoritative lastLogon value by checking relevant DCs and return exact UTC timestamp plus source DC.";
        var assistantDraft = "I’ll query the user across domain controllers and return the **latest authoritative `lastLogon`** (non-replicated) with the exact UTC timestamp and source DC.";

        var args = new object?[] { userRequest, assistantDraft, true, 0, 0, false, false, null };
        var result = EvaluateToolExecutionNudgeDecisionMethod.Invoke(
            null,
            args);

        var value = Assert.IsType<bool>(result);
        Assert.True(value);
    }

    [Fact]
    public void ShouldAttemptToolExecutionNudge_DoesNotTriggerForUnlinkedFollowUpQuestionWithoutContinuationSubset() {
        var userRequest = "Find the user's latest authoritative lastLogon value by checking relevant DCs and return exact UTC timestamp plus source DC.";
        var assistantDraft = "Would you like a short summary first?";

        var args = new object?[] { userRequest, assistantDraft, true, 0, 0, false, false, null };
        var result = EvaluateToolExecutionNudgeDecisionMethod.Invoke(
            null,
            args);

        var value = Assert.IsType<bool>(result);
        Assert.False(value);
        Assert.Equal("no_continuation_subset_and_no_cta_or_contextual_follow_up", Assert.IsType<string>(args[7]));
    }

    [Fact]
    public void ShouldAttemptToolExecutionNudge_DoesNotTriggerForExecutionAckDraftWithoutRequestReference() {
        var userRequest = "thanks";
        var assistantDraft = "I will execute that now and return the result immediately.";

        var args = new object?[] { userRequest, assistantDraft, true, 0, 0, false, false, null };
        var result = EvaluateToolExecutionNudgeDecisionMethod.Invoke(
            null,
            args);

        var value = Assert.IsType<bool>(result);
        Assert.False(value);
        Assert.Equal("no_continuation_subset_and_no_cta_or_contextual_follow_up", Assert.IsType<string>(args[7]));
    }

    [Fact]
    public void ShouldAttemptToolExecutionNudge_DoesNotTriggerForCompactFollowUpCourtesyDraft() {
        var userRequest = "thanks";
        var assistantDraft = "You're welcome.";

        var result = ShouldAttemptToolExecutionNudgeMethod.Invoke(
            null,
            new object?[] { userRequest, assistantDraft, true, 0, 0, false });

        var value = Assert.IsType<bool>(result);
        Assert.False(value);
    }

    [Fact]
    public void ShouldAttemptToolExecutionNudge_TriggersForSingleUnknownMutabilityPendingActionEnvelopeWithoutContinuationSubset() {
        var userRequest = "Run replication diagnostics now.";
        var assistantDraft = """
            [Action]
            ix:action:v1
            id: act_followup_unknown
            title: Continue with diagnostics
            reply: /act act_followup_unknown
            """;
        var args = new object?[] { userRequest, assistantDraft, true, 0, 0, false, false, null };

        var result = EvaluateToolExecutionNudgeDecisionMethod.Invoke(null, args);

        var value = Assert.IsType<bool>(result);
        Assert.True(value);
        Assert.Equal("single_unknown_pending_action_envelope", Assert.IsType<string>(args[7]));
    }

    [Fact]
    public void ShouldAttemptToolExecutionNudge_DoesNotTriggerForSingleMutatingPendingActionEnvelopeWithoutContinuationSubset_WhenEvaluatingReasonOutput() {
        var userRequest = "Run replication diagnostics now.";
        var assistantDraft = """
            [Action]
            ix:action:v1
            id: act_followup_mutating
            title: Reset replication queue
            mutating: true
            reply: /act act_followup_mutating
            """;
        var args = new object?[] { userRequest, assistantDraft, true, 0, 0, false, false, null };

        var result = EvaluateToolExecutionNudgeDecisionMethod.Invoke(null, args);

        var value = Assert.IsType<bool>(result);
        Assert.False(value);
        Assert.Equal("no_continuation_subset_and_no_cta_or_contextual_follow_up", Assert.IsType<string>(args[7]));
    }

    [Fact]
    public void TryBuildStructuredNextActionRetryPrompt_TriggersForContinuationFollowUpWithStructuredNextActions() {
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
        var userRequest = "Could you see if other dcs have the same issue?";
        var assistantDraft = """
            Got it — one blocker:
            - discovery returned only one DC candidate.
            - please provide the other DC hostnames.
            """;
        var args = new object?[] { toolDefinitions, toolCalls, toolOutputs, true, userRequest, assistantDraft, null, null };

        var result = TryBuildStructuredNextActionRetryPromptMethod.Invoke(null, args);

        Assert.True(Assert.IsType<bool>(result));
        var prompt = Assert.IsType<string>(args[6]);
        var reason = Assert.IsType<string>(args[7]);
        Assert.Equal("structured_next_action_found", reason);
        Assert.Contains("ix:structured-next-action-retry:v1", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("next_tool: ad_scope_discovery", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"discovery_fallback\":\"current_forest\"", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryBuildStructuredNextActionRetryPrompt_DoesNotTriggerWithoutContinuationFollowUpWhenStructuredNextActionsPresent() {
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
        var userRequest = "Run cross-dc check";
        var assistantDraft = "Can I proceed by running the next discovery action now?";
        var args = new object?[] { toolDefinitions, toolCalls, toolOutputs, false, userRequest, assistantDraft, null, null };

        var result = TryBuildStructuredNextActionRetryPromptMethod.Invoke(null, args);

        Assert.False(Assert.IsType<bool>(result));
        Assert.Equal("not_continuation_follow_up", Assert.IsType<string>(args[7]));
    }

    [Fact]
    public void TryBuildStructuredNextActionRetryPrompt_DoesNotTriggerWhenAssistantDraftDoesNotNeedContinuation() {
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
        var userRequest = "run discovery";
        var assistantDraft = "Done.";
        var args = new object?[] { toolDefinitions, toolCalls, toolOutputs, true, userRequest, assistantDraft, null, null };

        var result = TryBuildStructuredNextActionRetryPromptMethod.Invoke(null, args);

        Assert.False(Assert.IsType<bool>(result));
        Assert.Equal("assistant_draft_not_blocker_like", Assert.IsType<string>(args[7]));
    }

    [Fact]
    public void TryBuildStructuredNextActionRetryPrompt_DoesNotTriggerWhenNoStructuredNextActionsPresent() {
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
                Output = """{"ok":true,"domain_controllers":["AD0"]}""",
                Ok = true
            }
        };
        var userRequest = "go ahead";
        var assistantDraft = """
            one blocker:
            - please provide DC names.
            - discovery returned one host.
            """;
        var args = new object?[] { toolDefinitions, toolCalls, toolOutputs, true, userRequest, assistantDraft, null, null };

        var result = TryBuildStructuredNextActionRetryPromptMethod.Invoke(null, args);

        Assert.False(Assert.IsType<bool>(result));
        Assert.Equal("no_structured_next_action", Assert.IsType<string>(args[7]));
    }


    private static object CreateReplayOutputCompactionBudget(
        int maxOutputCharsPerCall,
        int maxOutputCharsTotal,
        long? effectiveContextLength,
        bool contextAwareBudgetApplied) {
        var budgetType = BuildToolRoundReplayInputWithBudgetMethod.GetParameters()[3].ParameterType;
        var value = Activator.CreateInstance(
            budgetType,
            maxOutputCharsPerCall,
            maxOutputCharsTotal,
            effectiveContextLength,
            contextAwareBudgetApplied);
        return value ?? throw new InvalidOperationException("ReplayOutputCompactionBudget instance could not be created.");
    }

    private static object CreateReplayOutputCompactionStats(
        int replayedCallCount,
        int originalTotalChars,
        int compactedTotalChars,
        int compactedCallCount) {
        var statsType = BuildReplayOutputCompactionStatusMessageMethod.GetParameters()[1].ParameterType;
        var value = Activator.CreateInstance(
            statsType,
            replayedCallCount,
            originalTotalChars,
            compactedTotalChars,
            compactedCallCount);
        return value ?? throw new InvalidOperationException("ReplayOutputCompactionStats instance could not be created.");
    }

    private static int ReadIntRecordProperty(object value, string propertyName) {
        var property = value.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)
                       ?? throw new InvalidOperationException($"Property '{propertyName}' not found.");
        var raw = property.GetValue(value);
        return Assert.IsType<int>(raw);
    }

    private static JsonArray GetChatInputItems(ChatInput input) {
        var toJson = typeof(ChatInput).GetMethod("ToJson", BindingFlags.NonPublic | BindingFlags.Instance)
                     ?? throw new InvalidOperationException("ChatInput.ToJson not found.");
        var value = toJson.Invoke(input, Array.Empty<object>());
        return Assert.IsType<JsonArray>(value);
    }

}
