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
    public void TryBuildStructuredNextActionRetryPrompt_UsesSuggestedArgumentsShape() {
        var schema = ToolSchema.Object().NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("eventlog_named_events_query", "named events", schema),
            new("ad_handoff_prepare", "handoff", schema)
        };
        var toolCalls = new List<ToolCallDto> {
            new() { CallId = "call-7", Name = "eventlog_named_events_query" }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-7",
                Output = """
                         {"ok":true,"next_actions":[{"tool":"ad_handoff_prepare","reason":"normalize eventlog identities","suggested_arguments":{"entity_handoff_ref":"meta.entity_handoff","entity_handoff_contract":"eventlog_entity_handoff"}}]}
                         """,
                Ok = true
            }
        };
        var userRequest = "continue";
        var assistantDraft = "Can I continue with identity normalization now?";
        var args = new object?[] { toolDefinitions, toolCalls, toolOutputs, true, userRequest, assistantDraft, null, null };

        var result = TryBuildStructuredNextActionRetryPromptMethod.Invoke(null, args);

        Assert.True(Assert.IsType<bool>(result));
        var prompt = Assert.IsType<string>(args[6]);
        Assert.Contains("next_tool: ad_handoff_prepare", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"entity_handoff_ref\":\"meta.entity_handoff\"", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryBuildStructuredNextActionRetryPrompt_ReadsNestedNextActionsArrays() {
        var schema = ToolSchema.Object().NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("system_pack_info", "pack", schema),
            new("system_updates_installed", "updates", schema)
        };
        var toolCalls = new List<ToolCallDto> {
            new() { CallId = "call-9", Name = "system_pack_info" }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-9",
                Output = """
                         {"ok":true,"data":{"next_actions":[{"tool":"system_updates_installed","arguments":{"computer_name":"srv01"}}]}}
                         """,
                Ok = true
            }
        };
        var userRequest = "go on";
        var assistantDraft = "Should I fetch installed updates on srv01 next?";
        var args = new object?[] { toolDefinitions, toolCalls, toolOutputs, true, userRequest, assistantDraft, null, null };

        var result = TryBuildStructuredNextActionRetryPromptMethod.Invoke(null, args);

        Assert.True(Assert.IsType<bool>(result));
        var prompt = Assert.IsType<string>(args[6]);
        Assert.Contains("next_tool: system_updates_installed", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"computer_name\":\"srv01\"", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryBuildStructuredNextActionRetryPrompt_ReadsStringToolEntries() {
        var schema = ToolSchema.Object().NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("ad_scope_discovery", "scope", schema),
            new("ad_forest_discover", "forest", schema)
        };
        var toolCalls = new List<ToolCallDto> {
            new() { CallId = "call-11", Name = "ad_environment_discover" }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-11",
                Output = """{"ok":true,"next_actions":["ad_scope_discovery","ad_forest_discover"]}""",
                Ok = true
            }
        };
        var userRequest = "continue";
        var assistantDraft = "Can I continue with the next discovery phase?";
        var args = new object?[] { toolDefinitions, toolCalls, toolOutputs, true, userRequest, assistantDraft, null, null };

        var result = TryBuildStructuredNextActionRetryPromptMethod.Invoke(null, args);

        Assert.True(Assert.IsType<bool>(result));
        var prompt = Assert.IsType<string>(args[6]);
        Assert.Contains("next_tool: ad_scope_discovery", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryBuildStructuredNextActionRetryPrompt_ReadsToolNameAndParametersAliases() {
        var schema = ToolSchema.Object().NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("ad_scope_discovery", "scope", schema),
            new("ad_forest_discover", "forest", schema)
        };
        var toolCalls = new List<ToolCallDto> {
            new() { CallId = "call-12", Name = "ad_environment_discover" }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-12",
                Output = """
                         {"ok":true,"meta":{"next_actions":[{"tool_name":"ad_scope_discovery","reason":"expand_dc_coverage","parameters":{"discovery_fallback":"current_forest","include_trusts":"false"}}]}}
                         """,
                Ok = true
            }
        };
        var userRequest = "continue";
        var assistantDraft = "Should I continue with expanded scope discovery now?";
        var args = new object?[] { toolDefinitions, toolCalls, toolOutputs, true, userRequest, assistantDraft, null, null };

        var result = TryBuildStructuredNextActionRetryPromptMethod.Invoke(null, args);

        Assert.True(Assert.IsType<bool>(result));
        var prompt = Assert.IsType<string>(args[6]);
        Assert.Contains("next_tool: ad_scope_discovery", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"discovery_fallback\":\"current_forest\"", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Do not ask for another confirmation", prompt, StringComparison.OrdinalIgnoreCase);
    }

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
    public void TryBuildHostStructuredNextActionToolCall_DoesNotAutoRunSelfLoopArguments() {
        var schema = ToolSchema.Object(
                ("log_name", ToolSchema.String()),
                ("machine_name", ToolSchema.String()))
            .NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("eventlog_live_query", "live", schema)
        };
        var toolCalls = new List<ToolCallDto> {
            new() {
                CallId = "call-self-loop",
                Name = "eventlog_live_query",
                ArgumentsJson = """{"log_name":"System","machine_name":"AD0.ad.evotec.xyz"}"""
            }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-self-loop",
                Output = """
                         {"ok":true,"next_actions":[{"tool":"eventlog_live_query","mutating":false,"arguments":{"log_name":"System","machine_name":"AD0.ad.evotec.xyz"}}]}
                         """,
                Ok = true
            }
        };
        var mutabilityHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["eventlog_live_query"] = false
        };
        var args = new object?[] { toolDefinitions, toolCalls, toolOutputs, mutabilityHints, null, null };

        var result = TryBuildHostStructuredNextActionToolCallMethod.Invoke(null, args);

        Assert.False(Assert.IsType<bool>(result));
        Assert.Equal("next_action_self_loop", Assert.IsType<string>(args[5]));
    }

    [Fact]
    public void TryBuildCarryoverStructuredNextActionToolCall_BuildsReadOnlyCallFromRememberedNextAction() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
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

        var args = new object?[] { "thread-carryover", "go ahead", toolDefinitions, mutabilityHints, null, null };
        var result = TryBuildCarryoverStructuredNextActionToolCallMethod.Invoke(session, args);

        Assert.True(Assert.IsType<bool>(result));
        var toolCall = Assert.IsType<ToolCall>(args[4]);
        Assert.Equal("ad_scope_discovery", toolCall.Name);
        Assert.NotNull(toolCall.Arguments);
        Assert.True(toolCall.Arguments!.GetBoolean("include_trusts", defaultValue: false));
        Assert.Equal(3, toolCall.Arguments.GetInt64("max_domains"));
        Assert.Equal("carryover_structured_next_action_readonly_autorun", Assert.IsType<string>(args[5]));
    }

    [Fact]
    public void TryBuildCarryoverStructuredNextActionToolCall_DoesNotReplayMutatingCarryover() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
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

        var args = new object?[] { "thread-carryover-mut", "continue", toolDefinitions, mutabilityHints, null, null };
        var result = TryBuildCarryoverStructuredNextActionToolCallMethod.Invoke(session, args);

        Assert.False(Assert.IsType<bool>(result));
        Assert.Equal("carryover_missing", Assert.IsType<string>(args[5]));
    }

    [Fact]
    public void RememberStructuredNextActionCarryover_DoesNotPersistSelfLoopArguments() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var schema = ToolSchema.Object(
                ("log_name", ToolSchema.String()),
                ("machine_name", ToolSchema.String()))
            .NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("eventlog_live_query", "live", schema)
        };
        var toolCalls = new List<ToolCallDto> {
            new() {
                CallId = "call-loop",
                Name = "eventlog_live_query",
                ArgumentsJson = """{"log_name":"System","machine_name":"AD0.ad.evotec.xyz"}"""
            }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-loop",
                Output = """
                         {"ok":true,"next_actions":[{"tool":"eventlog_live_query","mutating":false,"arguments":{"log_name":"System","machine_name":"AD0.ad.evotec.xyz"}}]}
                         """,
                Ok = true
            }
        };
        var mutabilityHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["eventlog_live_query"] = false
        };

        RememberStructuredNextActionCarryoverMethod.Invoke(
            session,
            new object?[] { "thread-carryover-loop", toolDefinitions, toolCalls, toolOutputs, mutabilityHints });

        var args = new object?[] { "thread-carryover-loop", "go ahead", toolDefinitions, mutabilityHints, null, null };
        var result = TryBuildCarryoverStructuredNextActionToolCallMethod.Invoke(session, args);

        Assert.False(Assert.IsType<bool>(result));
        Assert.Equal("carryover_missing", Assert.IsType<string>(args[5]));
    }

    [Fact]
    public void TryBuildCarryoverStructuredNextActionToolCall_SkipsWhenUserHostHintConflicts() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var schema = ToolSchema.Object(
                ("log_name", ToolSchema.String()),
                ("machine_name", ToolSchema.String()))
            .NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("eventlog_top_events", "top", schema),
            new("eventlog_live_query", "live", schema)
        };
        var toolCalls = new List<ToolCallDto> {
            new() {
                CallId = "call-evx-top",
                Name = "eventlog_top_events",
                ArgumentsJson = """{"log_name":"System","machine_name":"AD0.ad.evotec.xyz"}"""
            }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-evx-top",
                Output = """
                         {"ok":true,"next_actions":[{"tool":"eventlog_live_query","mutating":false,"arguments":{"log_name":"System","machine_name":"AD0.ad.evotec.xyz"}}]}
                         """,
                Ok = true
            }
        };
        var mutabilityHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["eventlog_live_query"] = false
        };

        RememberStructuredNextActionCarryoverMethod.Invoke(
            session,
            new object?[] { "thread-carryover-host", toolDefinitions, toolCalls, toolOutputs, mutabilityHints });

        var args = new object?[] {
            "thread-carryover-host",
            "Run this against AD1.ad.evotec.xyz now.",
            toolDefinitions,
            mutabilityHints,
            null,
            null
        };
        var result = TryBuildCarryoverStructuredNextActionToolCallMethod.Invoke(session, args);

        Assert.False(Assert.IsType<bool>(result));
        Assert.Equal("carryover_host_hint_mismatch", Assert.IsType<string>(args[5]));
    }

    [Fact]
    public void ShouldAttemptCarryoverStructuredNextActionReplay_RequiresCompactFollowUp() {
        var result = ChatServiceSession.ShouldAttemptCarryoverStructuredNextActionReplay(
            continuationFollowUpTurn: true,
            compactFollowUpTurn: false,
            userRequest: "continue",
            assistantDraft: "I can run the next action now.");

        Assert.False(result);
    }

    [Fact]
    public void ShouldAttemptCarryoverStructuredNextActionReplay_AllowsCompactAcknowledgeWithoutExpandedContinuation() {
        var result = ChatServiceSession.ShouldAttemptCarryoverStructuredNextActionReplay(
            continuationFollowUpTurn: false,
            compactFollowUpTurn: true,
            userRequest: "go ahead",
            assistantDraft: "I can run the next action now.");

        Assert.True(result);
    }

    [Fact]
    public void ShouldAttemptCarryoverStructuredNextActionReplay_SkipsCompactQuestions() {
        var result = ChatServiceSession.ShouldAttemptCarryoverStructuredNextActionReplay(
            continuationFollowUpTurn: false,
            compactFollowUpTurn: true,
            userRequest: "why?",
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
    public void ShouldAttemptCarryoverStructuredNextActionReplay_SkipsWhenLongLegacyExpansionContainsContextualFollowUp() {
        var legacyExpandedRequest =
            new string('a', 520)
            + "\nFollow-up: i mean other dcs";

        var result = ChatServiceSession.ShouldAttemptCarryoverStructuredNextActionReplay(
            continuationFollowUpTurn: true,
            compactFollowUpTurn: true,
            userRequest: legacyExpandedRequest,
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
    public void ShouldAttemptToolExecutionNudge_TriggersForSingleUnknownMutabilityPendingActionEnvelopeWithoutContinuationSubset_InExecutionContractSuite() {
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
        Assert.True(value);
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
    public void ShouldEnforceExecuteOrExplainContract_TriggersForMutatingActionSelectionPayloadWithCaseVariantKeys() {
        var userRequest = "{\"IX_Action_Selection\":{\"ActionId\":\"act_001\",\"title\":\"Disable account\",\"request\":\"Disable user evotec\\\\john and return confirmation.\",\"Mutating\":\"true\"}}";
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
    public void ShouldForceExecutionContractBlockerAtFinalize_DoesNotTriggerForCompactFollowUpQuestion() {
        var result = ShouldForceExecutionContractBlockerAtFinalizeMethod.Invoke(
            null,
            new object?[] {
                "aale to chyba masz toole do event logow?",
                false,
                false,
                false,
                false,
                true,
                true,
                false,
                "W tej sesji nie mam aktywnego eventlog packa."
            });

        Assert.False(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldForceExecutionContractBlockerAtFinalize_DoesNotTriggerWhenToolActivityExists() {
        var result = ShouldForceExecutionContractBlockerAtFinalizeMethod.Invoke(
            null,
            new object?[] { "Run the query and return UTC timestamp.", true, false, false, false, false, false, true, "Completed." });

        Assert.False(Assert.IsType<bool>(result));
    }
}
