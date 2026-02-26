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
    public void TryBuildHostStructuredNextActionToolCall_BuildsReadOnlyCallAndCoercesArguments() {
        var schema = ToolSchema.Object(
                ("include_trusts", ToolSchema.Boolean()),
                ("max_domains", ToolSchema.Integer()),
                ("domain_name", ToolSchema.String()))
            .NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("ad_scope_discovery", "scope", schema)
        };
        var toolCalls = new List<ToolCallDto> {
            new() { CallId = "call-20", Name = "ad_environment_discover" }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-20",
                Output = """
                         {"ok":true,"next_actions":[{"tool_name":"ad_scope_discovery","parameters":{"include_trusts":"false","max_domains":"250","domain_name":"contoso.com"}}]}
                         """,
                Ok = true
            }
        };
        var mutabilityHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["ad_scope_discovery"] = false
        };
        var args = new object?[] { toolDefinitions, toolCalls, toolOutputs, mutabilityHints, null, null };

        var result = TryBuildHostStructuredNextActionToolCallMethod.Invoke(null, args);

        Assert.True(Assert.IsType<bool>(result));
        var toolCall = Assert.IsType<ToolCall>(args[4]);
        Assert.Equal("ad_scope_discovery", toolCall.Name);
        Assert.NotNull(toolCall.Arguments);
        Assert.False(toolCall.Arguments!.GetBoolean("include_trusts", defaultValue: true));
        Assert.Equal(250, toolCall.Arguments.GetInt64("max_domains"));
        Assert.Equal("contoso.com", toolCall.Arguments.GetString("domain_name"));
        Assert.Equal("structured_next_action_readonly_autorun", Assert.IsType<string>(args[5]));
    }

    [Fact]
    public void TryBuildHostStructuredNextActionToolCall_AutoRunsWhenNextActionDeclaresReadOnlyMutability() {
        var schema = ToolSchema.Object().NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("ad_scope_discovery", "scope", schema)
        };
        var toolCalls = new List<ToolCallDto> {
            new() { CallId = "call-21a", Name = "ad_environment_discover" }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-21a",
                Output = """{"ok":true,"next_actions":[{"tool":"ad_scope_discovery","mutating":false}]}""",
                Ok = true
            }
        };
        var args = new object?[] { toolDefinitions, toolCalls, toolOutputs, null, null, null };

        var result = TryBuildHostStructuredNextActionToolCallMethod.Invoke(null, args);

        Assert.True(Assert.IsType<bool>(result));
        var call = Assert.IsType<ToolCall>(args[4]);
        Assert.Equal("ad_scope_discovery", call.Name);
        Assert.Equal("structured_next_action_readonly_autorun", Assert.IsType<string>(args[5]));
    }

    [Fact]
    public void TryBuildHostStructuredNextActionToolCall_DoesNotAutoRunWhenNextActionDeclaresMutating() {
        var schema = ToolSchema.Object().NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("ad_scope_discovery", "scope", schema)
        };
        var toolCalls = new List<ToolCallDto> {
            new() { CallId = "call-21b", Name = "ad_environment_discover" }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-21b",
                Output = """{"ok":true,"next_actions":[{"tool":"ad_scope_discovery","mutating":true}]}""",
                Ok = true
            }
        };
        var args = new object?[] { toolDefinitions, toolCalls, toolOutputs, null, null, null };

        var result = TryBuildHostStructuredNextActionToolCallMethod.Invoke(null, args);

        Assert.False(Assert.IsType<bool>(result));
        Assert.Equal("next_action_mutating_not_autorun", Assert.IsType<string>(args[5]));
    }

    [Fact]
    public void TryBuildHostStructuredNextActionToolCall_DoesNotAutoRunWhenMutabilityHintMissing() {
        var schema = ToolSchema.Object().NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("ad_scope_discovery", "scope", schema)
        };
        var toolCalls = new List<ToolCallDto> {
            new() { CallId = "call-21", Name = "ad_environment_discover" }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-21",
                Output = """{"ok":true,"next_actions":[{"tool":"ad_scope_discovery"}]}""",
                Ok = true
            }
        };
        var args = new object?[] { toolDefinitions, toolCalls, toolOutputs, null, null, null };

        var result = TryBuildHostStructuredNextActionToolCallMethod.Invoke(null, args);

        Assert.False(Assert.IsType<bool>(result));
        Assert.Equal("next_action_mutability_unknown", Assert.IsType<string>(args[5]));
    }

    [Fact]
    public void TryBuildHostStructuredNextActionToolCall_DoesNotAutoRunMutatingNextAction() {
        var schema = ToolSchema.Object().NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("powershell_run", "PowerShell", schema)
        };
        var toolCalls = new List<ToolCallDto> {
            new() { CallId = "call-22", Name = "system_pack_info" }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-22",
                Output = """{"ok":true,"next_actions":[{"tool":"powershell_run","arguments":{"script":"Get-Process"}}]}""",
                Ok = true
            }
        };
        var mutabilityHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["powershell_run"] = true
        };
        var args = new object?[] { toolDefinitions, toolCalls, toolOutputs, mutabilityHints, null, null };

        var result = TryBuildHostStructuredNextActionToolCallMethod.Invoke(null, args);

        Assert.False(Assert.IsType<bool>(result));
        Assert.Equal("next_action_mutating_not_autorun", Assert.IsType<string>(args[5]));
    }

    [Fact]
    public void TryBuildCarryoverStructuredNextActionToolCall_BuildsReadOnlyCallFromRememberedNextAction() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var schema = ToolSchema.Object(
                ("include_trusts", ToolSchema.Boolean()),
                ("max_domains", ToolSchema.Integer()))
            .NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("ad_environment_discover", "discover", ToolSchema.Object().NoAdditionalProperties()),
            new("ad_scope_discovery", "scope", schema)
        };
        var toolCalls = new List<ToolCallDto> {
            new() { CallId = "call-31", Name = "ad_environment_discover" }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-31",
                Output = """
                         {"ok":true,"next_actions":[{"tool":"ad_scope_discovery","mutating":false,"arguments":{"include_trusts":"true","max_domains":"3"}}]}
                         """,
                Ok = true
            }
        };
        var mutabilityHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["ad_scope_discovery"] = false
        };

        RememberStructuredNextActionCarryoverMethod.Invoke(
            session,
            new object?[] { "thread-carryover", toolDefinitions, toolCalls, toolOutputs, mutabilityHints });

        var args = new object?[] { "thread-carryover", toolDefinitions, mutabilityHints, null, null };
        var result = TryBuildCarryoverStructuredNextActionToolCallMethod.Invoke(session, args);

        Assert.True(Assert.IsType<bool>(result));
        var toolCall = Assert.IsType<ToolCall>(args[3]);
        Assert.Equal("ad_scope_discovery", toolCall.Name);
        Assert.NotNull(toolCall.Arguments);
        Assert.True(toolCall.Arguments!.GetBoolean("include_trusts", defaultValue: false));
        Assert.Equal(3, toolCall.Arguments.GetInt64("max_domains"));
        Assert.Equal("carryover_structured_next_action_readonly_autorun", Assert.IsType<string>(args[4]));
    }

    [Fact]
    public void TryBuildCarryoverStructuredNextActionToolCall_DoesNotReplayMutatingCarryover() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var toolDefinitions = new List<ToolDefinition> {
            new("ad_environment_discover", "discover", ToolSchema.Object().NoAdditionalProperties()),
            new("powershell_run", "PowerShell", ToolSchema.Object().NoAdditionalProperties())
        };
        var toolCalls = new List<ToolCallDto> {
            new() { CallId = "call-32", Name = "ad_environment_discover" }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-32",
                Output = """{"ok":true,"next_actions":[{"tool":"powershell_run","mutating":true,"arguments":{"script":"Get-Process"}}]}""",
                Ok = true
            }
        };
        var mutabilityHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["powershell_run"] = true
        };

        RememberStructuredNextActionCarryoverMethod.Invoke(
            session,
            new object?[] { "thread-carryover-mut", toolDefinitions, toolCalls, toolOutputs, mutabilityHints });

        var args = new object?[] { "thread-carryover-mut", toolDefinitions, mutabilityHints, null, null };
        var result = TryBuildCarryoverStructuredNextActionToolCallMethod.Invoke(session, args);

        Assert.False(Assert.IsType<bool>(result));
        Assert.Equal("carryover_missing", Assert.IsType<string>(args[4]));
    }

    [Fact]
    public void ShouldAttemptCarryoverStructuredNextActionReplay_RequiresCompactContinuation() {
        var result = ChatServiceSession.ShouldAttemptCarryoverStructuredNextActionReplay(
            continuationFollowUpTurn: true,
            compactFollowUpTurn: false,
            userRequest: "continue",
            assistantDraft: "I can run the next action now.");

        Assert.False(result);
    }

    [Fact]
    public void ShouldAttemptCarryoverStructuredNextActionReplay_SkipsWhenDraftAnchorsNewContext() {
        var result = ChatServiceSession.ShouldAttemptCarryoverStructuredNextActionReplay(
            continuationFollowUpTurn: true,
            compactFollowUpTurn: true,
            userRequest: "i mean other dcs",
            assistantDraft: "Perfect, understood: other DCs only. I will compare AD1 and AD2 now.");

        Assert.False(result);
    }

    [Fact]
    public void ShouldAttemptToolProgressRecovery_TriggersForBlockerLikeDraftAfterToolActivity() {
        var draft = """
            I started, but I hit one blocker:
            - discovery returned one candidate.
            - I need another pass before final comparison.
            """;
        var args = new object?[] {
            true,
            draft,
            true,
            1,
            1,
            0,
            false,
            null
        };

        var result = ShouldAttemptToolProgressRecoveryMethod.Invoke(null, args);

        Assert.True(Assert.IsType<bool>(result));
        Assert.Equal("blocker_like_draft_after_tool_activity", Assert.IsType<string>(args[7]));
    }

    [Fact]
    public void ShouldAttemptToolProgressRecovery_DoesNotTriggerWithoutPriorToolActivity() {
        var draft = "I can continue if you want me to.";
        var args = new object?[] {
            true,
            draft,
            true,
            0,
            0,
            0,
            false,
            null
        };

        var result = ShouldAttemptToolProgressRecoveryMethod.Invoke(null, args);

        Assert.False(Assert.IsType<bool>(result));
        Assert.Equal("missing_prior_tool_activity", Assert.IsType<string>(args[7]));
    }

    [Fact]
    public void ShouldAttemptToolProgressRecovery_DoesNotTriggerForNonFollowUpRequests() {
        var draft = """
            I started, but I hit one blocker:
            - discovery returned one candidate.
            - I need one more step.
            """;
        var args = new object?[] {
            false,
            draft,
            true,
            1,
            1,
            0,
            false,
            null
        };

        var result = ShouldAttemptToolProgressRecoveryMethod.Invoke(null, args);

        Assert.False(Assert.IsType<bool>(result));
        Assert.Equal("not_continuation_follow_up", Assert.IsType<string>(args[7]));
    }

    [Fact]
    public void BuildToolProgressRecoveryPrompt_EmitsStableMarkerAndToolList() {
        var toolCalls = new List<ToolCallDto> {
            new() { CallId = "call-1", Name = "ad_environment_discover" },
            new() { CallId = "call-2", Name = "eventlog_live_query" }
        };

        var result = BuildToolProgressRecoveryPromptMethod.Invoke(
            null,
            new object?[] { "go ahead", "I can continue if you want.", toolCalls });
        var prompt = Assert.IsType<string>(result);

        Assert.Contains("ix:tool-progress-recovery:v1", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ad_environment_discover", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("eventlog_live_query", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShouldAttemptToolExecutionNudge_TriggersForSingleReadOnlyPendingActionEnvelopeWithoutContinuationSubset() {
        var userRequest = "Could you do analysis of replication and trusts and then show a network diagram?";
        var assistantDraft = """
            Absolutely. Proceeding with that now.

            [Action]
            ix:action:v1
            id: act_001
            title: Run replication and trust analysis
            request: Run replication and trust diagnostics and return a network diagram.
            mutating: false
            reply: /act act_001
            """;

        var result = ShouldAttemptToolExecutionNudgeMethod.Invoke(
            null,
            new object?[] { userRequest, assistantDraft, true, 0, 0, false });

        var value = Assert.IsType<bool>(result);
        Assert.True(value);
    }

    [Fact]
    public void ShouldAttemptToolExecutionNudge_DoesNotTriggerForSingleMutatingPendingActionEnvelopeWithoutContinuationSubset() {
        var userRequest = "Disable stale account evotec\\john and then verify.";
        var assistantDraft = """
            Understood. Proceeding with that now.

            [Action]
            ix:action:v1
            id: act_disable
            title: Disable stale account
            request: Disable stale account evotec\john and verify status.
            mutating: true
            reply: /act act_disable
            """;

        var result = ShouldAttemptToolExecutionNudgeMethod.Invoke(
            null,
            new object?[] { userRequest, assistantDraft, true, 0, 0, false });

        var value = Assert.IsType<bool>(result);
        Assert.False(value);
    }

    [Fact]
    public void ShouldAttemptToolExecutionNudge_DoesNotTriggerForSingleUnknownMutabilityPendingActionEnvelopeWithoutContinuationSubset() {
        var userRequest = "Run replication diagnostics now.";
        var assistantDraft = """
            Proceeding now.

            [Action]
            ix:action:v1
            id: act_unknown
            title: Run replication diagnostics
            request: Run replication diagnostics and summarize findings.
            reply: /act act_unknown
            """;

        var result = ShouldAttemptToolExecutionNudgeMethod.Invoke(
            null,
            new object?[] { userRequest, assistantDraft, true, 0, 0, false });

        var value = Assert.IsType<bool>(result);
        Assert.False(value);
    }

    [Fact]
    public void ShouldEnforceExecuteOrExplainContract_TriggersForMutatingActionSelectionPayload() {
        var userRequest = "{\"ix_action_selection\":{\"id\":\"act_001\",\"title\":\"Reset account password\",\"request\":\"Reset password for user evotec\\\\john.\",\"mutating\":true}}";
        var result = ShouldEnforceExecuteOrExplainContractMethod.Invoke(null, new object?[] { userRequest });

        Assert.True(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldEnforceExecuteOrExplainContract_DoesNotTriggerForReadOnlyActionSelectionPayload() {
        var userRequest = "{\"ix_action_selection\":{\"id\":\"act_001\",\"title\":\"Failed logons (4625)\",\"request\":\"Run failed logon report for the last 24 hours.\",\"mutating\":false}}";
        var result = ShouldEnforceExecuteOrExplainContractMethod.Invoke(null, new object?[] { userRequest });

        Assert.False(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldEnforceExecuteOrExplainContract_DoesNotTriggerWhenMutabilityMetadataIsMissing() {
        var userRequest = "{\"ix_action_selection\":{\"id\":\"act_001\",\"title\":\"Check and disable user\",\"request\":\"Check stale state and disable user evotec\\\\john if needed.\"}}";
        var result = ShouldEnforceExecuteOrExplainContractMethod.Invoke(null, new object?[] { userRequest });

        Assert.False(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldEnforceExecuteOrExplainContract_TriggersForMutatingActionSelectionPayloadWithNumericBoolean() {
        var userRequest = "{\"ix_action_selection\":{\"id\":\"act_001\",\"title\":\"Disable account\",\"request\":\"Disable user evotec\\\\john and return confirmation.\",\"mutating\":1}}";
        var result = ShouldEnforceExecuteOrExplainContractMethod.Invoke(null, new object?[] { userRequest });

        Assert.True(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldEnforceExecuteOrExplainContract_DoesNotTriggerForReadOnlyAliasBooleanField() {
        var userRequest = "{\"ix_action_selection\":{\"id\":\"act_001\",\"title\":\"Failed logons\",\"request\":\"Run failed logon report.\",\"readonly\":true}}";
        var result = ShouldEnforceExecuteOrExplainContractMethod.Invoke(null, new object?[] { userRequest });

        Assert.False(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldEnforceExecuteOrExplainContract_DoesNotTriggerForPlainTextRequest() {
        var result = ShouldEnforceExecuteOrExplainContractMethod.Invoke(null, new object?[] { "run now" });

        Assert.False(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldForceExecutionContractBlockerAtFinalize_TriggersWhenExecutionPathHadNoToolEvidence() {
        var result = ShouldForceExecutionContractBlockerAtFinalizeMethod.Invoke(
            null,
            new object?[] { "Run the query and return UTC timestamp.", false, false, true, false, false, false, false, "On it." });

        Assert.True(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldForceExecutionContractBlockerAtFinalize_DoesNotTriggerWhenToolActivityExists() {
        var result = ShouldForceExecutionContractBlockerAtFinalizeMethod.Invoke(
            null,
            new object?[] { "Run the query and return UTC timestamp.", true, false, false, false, false, false, true, "Completed." });

        Assert.False(Assert.IsType<bool>(result));
    }

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

}
