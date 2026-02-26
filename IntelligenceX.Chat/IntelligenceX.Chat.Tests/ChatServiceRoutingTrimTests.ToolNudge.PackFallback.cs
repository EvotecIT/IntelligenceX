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
    public void ShouldForceExecutionContractBlockerAtFinalize_DoesNotTriggerForStructuredBlockerEnvelope() {
        var structuredBlocker = """
            [Execution blocked]
            ix:execution-contract:v1
            [Action]
            ix:action:v1
            id: act_001
            reply: /act act_001
            """;
        var result = ShouldForceExecutionContractBlockerAtFinalizeMethod.Invoke(
            null,
            new object?[] { "Run the query and return UTC timestamp.", true, false, false, false, false, false, false, structuredBlocker });

        Assert.False(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldForceExecutionContractBlockerAtFinalize_TriggersForCompactFollowUpExecutionAckWithoutToolEvidence() {
        var draft = "On it. Running the all-DC reboot baseline now and returning the side-by-side matrix.";
        var result = ShouldForceExecutionContractBlockerAtFinalizeMethod.Invoke(
            null,
            new object?[] { "Run the baseline now.", false, false, false, false, true, true, false, draft });

        Assert.True(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldForceExecutionContractBlockerAtFinalize_DoesNotTriggerForCompactFollowUpQuestionDraft() {
        var draft = "Should I run this now across all domain controllers?";
        var result = ShouldForceExecutionContractBlockerAtFinalizeMethod.Invoke(
            null,
            new object?[] { "Run the baseline now.", false, false, false, false, true, true, false, draft });

        Assert.False(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldForceExecutionContractBlockerAtFinalize_TriggersForExecutionIntentPlaceholderWithoutPriorRecoveryFlags() {
        var userRequest = "Find the user's latest authoritative lastLogon value by checking relevant DCs and return exact UTC timestamp plus source DC.";
        var draft = "I’ll query the user across domain controllers and return the latest authoritative lastLogon timestamp with source DC.";
        var result = ShouldForceExecutionContractBlockerAtFinalizeMethod.Invoke(
            null,
            new object?[] { userRequest, false, false, false, false, false, false, false, draft });

        Assert.True(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldAttemptNoToolExecutionWatchdog_TriggersForStrictNoToolTurnAfterRecoveryAttempt() {
        var args = new object?[] {
            "{\"ix_action_selection\":{\"id\":\"act_001\",\"title\":\"Disable account\",\"request\":\"Disable user evotec\\\\john and return confirmation.\",\"mutating\":true}}",
            "Ok, doing it now.",
            true,
            0,
            0,
            0,
            false,
            false,
            true,
            false,
            false,
            null
        };

        var result = ShouldAttemptNoToolExecutionWatchdogMethod.Invoke(null, args);

        Assert.True(Assert.IsType<bool>(result));
        Assert.Equal("strict_contract_watchdog_retry", Assert.IsType<string>(args[11]));
    }

    [Fact]
    public void ShouldAttemptNoToolExecutionWatchdog_DoesNotTriggerWhenToolsUnavailable() {
        var args = new object?[] {
            "{\"ix_action_selection\":{\"id\":\"act_001\",\"title\":\"Disable account\",\"request\":\"Disable user evotec\\\\john and return confirmation.\",\"mutating\":true}}",
            "Ok, doing it now.",
            false,
            0,
            0,
            0,
            false,
            false,
            true,
            false,
            false,
            null
        };

        var result = ShouldAttemptNoToolExecutionWatchdogMethod.Invoke(null, args);

        Assert.False(Assert.IsType<bool>(result));
        Assert.Equal("tools_unavailable", Assert.IsType<string>(args[11]));
    }

    [Fact]
    public void ShouldAttemptNoToolExecutionWatchdog_TriggersForCompactFollowUpBlockerDraftWithoutExecutionContract() {
        var args = new object?[] {
            "go ahead",
            """
            I started, but I hit one blocker:
            - discovery returned one candidate.
            - I need one more execution pass to continue.
            """,
            true,
            0,
            0,
            0,
            true,
            true,
            true,
            false,
            false,
            null
        };

        var result = ShouldAttemptNoToolExecutionWatchdogMethod.Invoke(null, args);

        Assert.True(Assert.IsType<bool>(result));
        Assert.Equal("compact_follow_up_watchdog_retry", Assert.IsType<string>(args[11]));
    }

    [Fact]
    public void ShouldAttemptNoToolExecutionWatchdog_TriggersForContextualFollowUpWithoutCompactHint() {
        var args = new object?[] {
            "could we check if all other dcs had similar issues?",
            """
            Absolutely — I can check if all other DCs had similar issues, but I hit one blocker:
            - discovery returned one candidate.
            - I need one more execution pass to continue.
            """,
            true,
            0,
            0,
            0,
            false,
            false,
            false,
            false,
            false,
            null
        };

        var result = ShouldAttemptNoToolExecutionWatchdogMethod.Invoke(null, args);

        Assert.True(Assert.IsType<bool>(result));
        Assert.Equal("contextual_follow_up_watchdog_retry", Assert.IsType<string>(args[11]));
    }

    [Fact]
    public void ShouldAttemptNoToolExecutionWatchdog_TriggersForEmptyAssistantDraftAfterNudge() {
        var args = new object?[] {
            "please continue",
            string.Empty,
            true,
            0,
            0,
            0,
            false,
            false,
            true,
            false,
            false,
            null
        };

        var result = ShouldAttemptNoToolExecutionWatchdogMethod.Invoke(null, args);

        Assert.True(Assert.IsType<bool>(result));
        Assert.Equal("empty_assistant_draft_watchdog_retry", Assert.IsType<string>(args[11]));
    }

    [Fact]
    public void ShouldAttemptNoToolExecutionWatchdog_TriggersAfterExecutionNudgeOutsideCompactFollowUp() {
        var args = new object?[] {
            "please continue",
            """
            I started, but I hit one blocker:
            - discovery returned one candidate.
            - I need one more execution pass to continue.
            """,
            true,
            0,
            0,
            0,
            false,
            false,
            true,
            false,
            false,
            null
        };

        var result = ShouldAttemptNoToolExecutionWatchdogMethod.Invoke(null, args);

        Assert.True(Assert.IsType<bool>(result));
        Assert.Equal("compact_follow_up_watchdog_retry", Assert.IsType<string>(args[11]));
    }

    [Fact]
    public void ShouldSuppressLocalToolRecoveryRetries_DoesNotSuppressWhenToolsAreAvailable() {
        var result = ShouldSuppressLocalToolRecoveryRetriesMethod.Invoke(
            null,
            new object?[] {
                true,  // isLocalCompatibleLoopback
                false, // executionContractApplies
                false, // compactFollowUpTurn
                true,  // toolsAvailable
                0,     // priorToolCalls
                0,     // priorToolOutputs
                "Check AD status", // userRequest
                "Working on it."   // assistantDraft
            });

        Assert.False(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldSuppressLocalToolRecoveryRetries_SuppressesNonFollowUpToollessLocalReadOnlyFirstPass() {
        var result = ShouldSuppressLocalToolRecoveryRetriesMethod.Invoke(
            null,
            new object?[] {
                true,  // isLocalCompatibleLoopback
                false, // executionContractApplies
                false, // compactFollowUpTurn
                false, // toolsAvailable
                0,     // priorToolCalls
                0,     // priorToolOutputs
                "Check AD status", // userRequest
                "Working on it."   // assistantDraft
            });

        Assert.True(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldSuppressLocalToolRecoveryRetries_DoesNotSuppressCompactFollowUp() {
        var result = ShouldSuppressLocalToolRecoveryRetriesMethod.Invoke(
            null,
            new object?[] {
                true,  // isLocalCompatibleLoopback
                false, // executionContractApplies
                true,  // compactFollowUpTurn
                true,  // toolsAvailable
                0,     // priorToolCalls
                0,     // priorToolOutputs
                "go ahead", // userRequest
                "On it."    // assistantDraft
            });

        Assert.False(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldSuppressLocalToolRecoveryRetries_DoesNotSuppressLinkedQuestionDraft() {
        var result = ShouldSuppressLocalToolRecoveryRetriesMethod.Invoke(
            null,
            new object?[] {
                true,  // isLocalCompatibleLoopback
                false, // executionContractApplies
                false, // compactFollowUpTurn
                true,  // toolsAvailable
                0,     // priorToolCalls
                0,     // priorToolOutputs
                "Find the user's latest authoritative lastLogon value by checking relevant DCs and return exact UTC timestamp plus source DC.",
                "To return the authoritative lastLogon with source DC, should I query only ad.evotec.xyz DCs or include both domains?"
            });

        Assert.False(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldSuppressLocalToolRecoveryRetries_DoesNotSuppressExecutionAckDraftWithInlineCode() {
        var result = ShouldSuppressLocalToolRecoveryRetriesMethod.Invoke(
            null,
            new object?[] {
                true,  // isLocalCompatibleLoopback
                false, // executionContractApplies
                false, // compactFollowUpTurn
                true,  // toolsAvailable
                0,     // priorToolCalls
                0,     // priorToolOutputs
                "Find the user's latest authoritative lastLogon value by checking relevant DCs and return exact UTC timestamp plus source DC.",
                "I’ll query all relevant DCs for this user’s non-replicated `lastLogon`, then return the latest exact UTC value and the DC that reported it."
            });

        Assert.False(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldAttemptContinuationSubsetEscape_TriggersForFollowUpSubsetWithNoToolActivity() {
        var args = new object?[] {
            false, // executionContractApplies
            true,  // usedContinuationSubset
            false, // continuationSubsetEscapeUsed
            true,  // toolsAvailable
            0,     // priorToolCalls
            0,     // priorToolOutputs
            null
        };

        var result = ShouldAttemptContinuationSubsetEscapeMethod.Invoke(null, args);

        Assert.True(Assert.IsType<bool>(result));
        Assert.Equal("continuation_subset_no_tool_activity", Assert.IsType<string>(args[6]));
    }

    [Fact]
    public void ShouldAttemptContinuationSubsetEscape_DoesNotTriggerForExecutionContractTurn() {
        var args = new object?[] {
            true,  // executionContractApplies
            true,  // usedContinuationSubset
            false, // continuationSubsetEscapeUsed
            true,  // toolsAvailable
            0,     // priorToolCalls
            0,     // priorToolOutputs
            null
        };

        var result = ShouldAttemptContinuationSubsetEscapeMethod.Invoke(null, args);

        Assert.False(Assert.IsType<bool>(result));
        Assert.Equal("execution_contract_turn", Assert.IsType<string>(args[6]));
    }

    [Fact]
    public void ShouldAttemptContinuationSubsetEscape_DoesNotTriggerWhenSubsetWasNotUsed() {
        var args = new object?[] {
            false, // executionContractApplies
            false, // usedContinuationSubset
            false, // continuationSubsetEscapeUsed
            true,  // toolsAvailable
            0,     // priorToolCalls
            0,     // priorToolOutputs
            null
        };

        var result = ShouldAttemptContinuationSubsetEscapeMethod.Invoke(null, args);

        Assert.False(Assert.IsType<bool>(result));
        Assert.Equal("full_tools_already_available", Assert.IsType<string>(args[6]));
    }

    [Fact]
    public void BuildNoToolExecutionWatchdogPrompt_EmitsStableMarker() {
        var result = BuildNoToolExecutionWatchdogPromptMethod.Invoke(null, new object?[] { "run now", "draft" });
        var text = Assert.IsType<string>(result);

        Assert.Contains("ix:execution-watchdog:v1", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildExecutionContractBlockerText_EmitsStableMarkerAndReason() {
        var result = BuildExecutionContractBlockerTextMethod.Invoke(null, new object?[] { "run now", "draft", "no_tool_calls_after_watchdog_retry" });
        var text = Assert.IsType<string>(result);

        Assert.Contains("ix:execution-contract:v1", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no_tool_calls_after_watchdog_retry", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Reply `continue`", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildExecutionContractEscapePrompt_EmitsStableMarker() {
        var result = BuildExecutionContractEscapePromptMethod.Invoke(null, new object?[] { "run now", "draft" });
        var text = Assert.IsType<string>(result);

        Assert.Contains("ix:execution-contract-escape:v1", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("full tool availability", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildContinuationSubsetEscapePrompt_EmitsStableMarker() {
        var result = BuildContinuationSubsetEscapePromptMethod.Invoke(null, new object?[] { "run now", "draft" });
        var text = Assert.IsType<string>(result);

        Assert.Contains("ix:continuation-subset-escape:v1", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("full tool availability", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildExecutionContractBlockerText_EmbedsReplayableActionEnvelopeForSelectionPayload() {
        var request = "{\"ix_action_selection\":{\"id\":\"act_failed4625\",\"title\":\"Failed logons (4625)\",\"request\":\"Run failed logon report on ADO Security.\",\"mutating\":false}}";
        var result = BuildExecutionContractBlockerTextMethod.Invoke(null, new object?[] { request, "draft", "no_tool_calls_after_watchdog_retry" });
        var text = Assert.IsType<string>(result);

        Assert.Contains("[Action]", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ix:action:v1", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id: act_failed4625", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("mutating: false", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reply: /act act_failed4625", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildExecutionContractBlockerText_EmbedsReplayableActionEnvelopeForSingleActionAssistantDraft() {
        var draft = """
            Pulling failed logons from ADO now would be the next step.
            You can run one of these follow-up actions:
            1. Run failed logon report (4625) on ADO Security (/act act_failed4625)
            """;
        var result = BuildExecutionContractBlockerTextMethod.Invoke(
            null,
            new object?[] { "failed logons please", draft, "no_tool_calls_after_watchdog_retry" });
        var text = Assert.IsType<string>(result);

        Assert.Contains("[Action]", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ix:action:v1", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("id: act_failed4625", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reply: /act act_failed4625", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildExecutionContractBlockerText_DoesNotEmbedReplayableActionEnvelopeForAmbiguousAssistantDraft() {
        var draft = """
            You can run one of these follow-up actions:
            1. Run failed logon report (4625) on ADO Security (/act act_failed4625)
            2. Pull account lockout events (4740) on ADO Security (/act act_lockouts4740)
            """;
        var result = BuildExecutionContractBlockerTextMethod.Invoke(
            null,
            new object?[] { "security log please", draft, "no_tool_calls_after_watchdog_retry" });
        var text = Assert.IsType<string>(result);

        Assert.DoesNotContain("ix:action:v1", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShouldAllowHostStructuredNextActionReplay_TrueForExecutionAcknowledgeDraft() {
        var result = ShouldAllowHostStructuredNextActionReplayMethod.Invoke(
            null,
            new object?[] { "On it. Running the all-DC baseline now and returning the comparison matrix." });

        Assert.True(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldAllowHostStructuredNextActionReplay_FalseForQuestionDraft() {
        var result = ShouldAllowHostStructuredNextActionReplayMethod.Invoke(
            null,
            new object?[] { "If you want, should I run that now?" });

        Assert.False(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldAllowHostStructuredNextActionReplay_FalseForLongEvidenceSummaryDraft() {
        var draft = """
            Confirmed — here is the exact outcome:
            - AD0 reboot marker 2026-02-17T07:41:18Z.
            - AD1 reboot marker 2026-02-17T07:41:22Z.
            - AD2 reboot marker 2026-02-17T07:50:01Z.
            - Classification unexpected reboot.
            - Event 41 present.
            - Event 6008 present.
            - Event 6005 present.
            - Planned 1074 entries also present.
            - Cross-DC same-window pattern detected.
            - Next best check is root-cause correlation.
            """;

        var result = ShouldAllowHostStructuredNextActionReplayMethod.Invoke(
            null,
            new object?[] { draft });

        Assert.False(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldTriggerNoResultPhaseLoopWatchdog_TriggersForRepeatedPhaseLoopsWithToolActivity() {
        var args = new object?[] {
            9,
            true,
            false,
            false,
            false,
            true,
            """
            On it. Running now.
            I can return a side-by-side matrix right after this pass.
            """,
            null
        };

        var result = ShouldTriggerNoResultPhaseLoopWatchdogMethod.Invoke(null, args);

        Assert.True(Assert.IsType<bool>(result));
        Assert.Equal("phase_loop_with_tool_activity", Assert.IsType<string>(args[7]));
    }

    [Fact]
    public void ShouldTriggerNoResultPhaseLoopWatchdog_DoesNotTriggerBelowThreshold() {
        var args = new object?[] {
            3,
            true,
            false,
            true,
            true,
            true,
            "On it.",
            null
        };

        var result = ShouldTriggerNoResultPhaseLoopWatchdogMethod.Invoke(null, args);

        Assert.False(Assert.IsType<bool>(result));
        Assert.Equal("phase_loop_threshold_not_met", Assert.IsType<string>(args[7]));
    }

    [Fact]
    public void ShouldEmitInterimResultSnapshot_TrueForNormalExecutionDraft() {
        var draft = """
            I checked AD0 and confirmed a reboot window around 2026-02-22 17:56 UTC.
            I am correlating matching events on AD1 and AD2 and will return a compact matrix.
            """;
        var result = ShouldEmitInterimResultSnapshotMethod.Invoke(null, new object?[] { draft });

        Assert.True(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldEmitInterimResultSnapshot_FalseForExecutionContractMarkerDraft() {
        var draft = """
            I could not execute in this turn.
            ix:execution-contract:v1
            reason: no_tool_calls_after_watchdog_retry
            """;
        var result = ShouldEmitInterimResultSnapshotMethod.Invoke(null, new object?[] { draft });

        Assert.False(Assert.IsType<bool>(result));
    }

    [Fact]
    public void TryBuildPackCapabilityFallbackToolCall_BuildsReadOnlyFallbackForAdPartialScope() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var packMap = Assert.IsType<Dictionary<string, string>>(ToolPackIdsByToolNameField.GetValue(session));
        packMap["ad_environment_discover"] = "active_directory";
        packMap["ad_scope_discovery"] = "active_directory";

        var schema = ToolSchema.Object().NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("ad_environment_discover", "discover", schema),
            new("ad_scope_discovery", "scope", schema)
        };
        RebuildPackCapabilityFallbackContractsMethod.Invoke(session, new object?[] { toolDefinitions });

        var toolCalls = new List<ToolCallDto> {
            new() { CallId = "call-1", Name = "ad_environment_discover" }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-1",
                Output = """
                         {"discovery_status":{"limited_discovery":true,"domain_name":"contoso.local","forest_name":"contoso.local","include_trusts":true}}
                         """,
                Ok = true
            }
        };
        var mutabilityHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["ad_scope_discovery"] = false
        };

        var args = new object?[] { toolDefinitions, toolCalls, toolOutputs, "run discovery", mutabilityHints, null, null };
        var result = TryBuildPackCapabilityFallbackToolCallMethod.Invoke(session, args);

        Assert.True(Assert.IsType<bool>(result));
        var toolCall = Assert.IsType<ToolCall>(args[5]);
        var reason = Assert.IsType<string>(args[6]);
        Assert.Equal("ad_scope_discovery", toolCall.Name);
        Assert.Contains("pack_contract_partial_scope_autofallback", reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"discovery_fallback\":\"current_forest\"", toolCall.Input, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"domain_name\":\"contoso.local\"", toolCall.Input, StringComparison.OrdinalIgnoreCase);
    }

}
