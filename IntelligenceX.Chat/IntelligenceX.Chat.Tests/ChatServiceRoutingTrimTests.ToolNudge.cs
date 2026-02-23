using System;
using System.Collections.Generic;
using System.IO;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Service;
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
        var args = new object?[] { toolDefinitions, toolCalls, toolOutputs, userRequest, assistantDraft, null, null };

        var result = TryBuildStructuredNextActionRetryPromptMethod.Invoke(null, args);

        Assert.True(Assert.IsType<bool>(result));
        var prompt = Assert.IsType<string>(args[5]);
        var reason = Assert.IsType<string>(args[6]);
        Assert.Equal("structured_next_action_found", reason);
        Assert.Contains("ix:structured-next-action-retry:v1", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("next_tool: ad_scope_discovery", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"discovery_fallback\":\"current_forest\"", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryBuildStructuredNextActionRetryPrompt_TriggersWithoutContinuationFollowUpWhenStructuredNextActionsPresent() {
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
        var args = new object?[] { toolDefinitions, toolCalls, toolOutputs, userRequest, assistantDraft, null, null };

        var result = TryBuildStructuredNextActionRetryPromptMethod.Invoke(null, args);

        Assert.True(Assert.IsType<bool>(result));
        Assert.Equal("structured_next_action_found", Assert.IsType<string>(args[6]));
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
        var args = new object?[] { toolDefinitions, toolCalls, toolOutputs, userRequest, assistantDraft, null, null };

        var result = TryBuildStructuredNextActionRetryPromptMethod.Invoke(null, args);

        Assert.False(Assert.IsType<bool>(result));
        Assert.Equal("assistant_draft_not_blocker_like", Assert.IsType<string>(args[6]));
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
        var args = new object?[] { toolDefinitions, toolCalls, toolOutputs, userRequest, assistantDraft, null, null };

        var result = TryBuildStructuredNextActionRetryPromptMethod.Invoke(null, args);

        Assert.False(Assert.IsType<bool>(result));
        Assert.Equal("no_structured_next_action", Assert.IsType<string>(args[6]));
    }

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
        var args = new object?[] { toolDefinitions, toolCalls, toolOutputs, userRequest, assistantDraft, null, null };

        var result = TryBuildStructuredNextActionRetryPromptMethod.Invoke(null, args);

        Assert.True(Assert.IsType<bool>(result));
        var prompt = Assert.IsType<string>(args[5]);
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
        var args = new object?[] { toolDefinitions, toolCalls, toolOutputs, userRequest, assistantDraft, null, null };

        var result = TryBuildStructuredNextActionRetryPromptMethod.Invoke(null, args);

        Assert.True(Assert.IsType<bool>(result));
        var prompt = Assert.IsType<string>(args[5]);
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
        var args = new object?[] { toolDefinitions, toolCalls, toolOutputs, userRequest, assistantDraft, null, null };

        var result = TryBuildStructuredNextActionRetryPromptMethod.Invoke(null, args);

        Assert.True(Assert.IsType<bool>(result));
        var prompt = Assert.IsType<string>(args[5]);
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
        var args = new object?[] { toolDefinitions, toolCalls, toolOutputs, userRequest, assistantDraft, null, null };

        var result = TryBuildStructuredNextActionRetryPromptMethod.Invoke(null, args);

        Assert.True(Assert.IsType<bool>(result));
        var prompt = Assert.IsType<string>(args[5]);
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
            new object?[] { false, false, true, false, false, false, false, "On it." });

        Assert.True(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldForceExecutionContractBlockerAtFinalize_DoesNotTriggerWhenToolActivityExists() {
        var result = ShouldForceExecutionContractBlockerAtFinalizeMethod.Invoke(
            null,
            new object?[] { true, false, false, false, false, false, true, "Completed." });

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
            new object?[] { true, false, false, false, false, false, false, structuredBlocker });

        Assert.False(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldForceExecutionContractBlockerAtFinalize_TriggersForCompactFollowUpExecutionAckWithoutToolEvidence() {
        var draft = "On it. Running the all-DC reboot baseline now and returning the side-by-side matrix.";
        var result = ShouldForceExecutionContractBlockerAtFinalizeMethod.Invoke(
            null,
            new object?[] { false, false, false, false, true, true, false, draft });

        Assert.True(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldForceExecutionContractBlockerAtFinalize_DoesNotTriggerForCompactFollowUpQuestionDraft() {
        var draft = "Should I run this now across all domain controllers?";
        var result = ShouldForceExecutionContractBlockerAtFinalizeMethod.Invoke(
            null,
            new object?[] { false, false, false, false, true, true, false, draft });

        Assert.False(Assert.IsType<bool>(result));
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
    public void ShouldSuppressLocalToolRecoveryRetries_SuppressesNonFollowUpLocalReadOnlyFirstPass() {
        var result = ShouldSuppressLocalToolRecoveryRetriesMethod.Invoke(
            null,
            new object?[] {
                true,  // isLocalCompatibleLoopback
                false, // executionContractApplies
                false, // compactFollowUpTurn
                0,     // priorToolCalls
                0      // priorToolOutputs
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
                0,     // priorToolCalls
                0      // priorToolOutputs
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
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
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

    [Fact]
    public void TryBuildPackCapabilityFallbackToolCall_DoesNotBuildWhenNoPartialScopeSignal() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
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
            new() { CallId = "call-2", Name = "ad_environment_discover" }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-2",
                Output = """{"ok":true,"domain_controllers":["AD0","AD1","AD2"]}""",
                Ok = true
            }
        };
        var mutabilityHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["ad_scope_discovery"] = false
        };

        var args = new object?[] { toolDefinitions, toolCalls, toolOutputs, "run discovery", mutabilityHints, null, null };
        var result = TryBuildPackCapabilityFallbackToolCallMethod.Invoke(session, args);

        Assert.False(Assert.IsType<bool>(result));
        Assert.Equal("pack_contract_no_applicable_fallback", Assert.IsType<string>(args[6]));
    }

    [Fact]
    public void TryBuildPackCapabilityFallbackToolCall_BuildsEventlogLiveQueryFallbackWhenEvtxAccessDeniedWithHostHint() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var packMap = Assert.IsType<Dictionary<string, string>>(ToolPackIdsByToolNameField.GetValue(session));
        packMap["eventlog_evtx_find"] = "eventlog";
        packMap["eventlog_live_query"] = "eventlog";

        var schema = ToolSchema.Object().NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("eventlog_evtx_find", "find evtx", schema),
            new("eventlog_live_query", "live query", schema)
        };
        RebuildPackCapabilityFallbackContractsMethod.Invoke(session, new object?[] { toolDefinitions });

        var toolCalls = new List<ToolCallDto> {
            new() { CallId = "call-3", Name = "eventlog_evtx_find" }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-3",
                Output = """{"ok":false,"error_code":"access_denied"}""",
                Ok = false,
                ErrorCode = "access_denied"
            }
        };
        var mutabilityHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["eventlog_live_query"] = false
        };

        var args = new object?[] {
            toolDefinitions,
            toolCalls,
            toolOutputs,
            "Can you find out why and when AD0 was rebooted?",
            mutabilityHints,
            null,
            null
        };
        var result = TryBuildPackCapabilityFallbackToolCallMethod.Invoke(session, args);

        Assert.True(Assert.IsType<bool>(result));
        var toolCall = Assert.IsType<ToolCall>(args[5]);
        var reason = Assert.IsType<string>(args[6]);
        Assert.Equal("eventlog_live_query", toolCall.Name);
        Assert.Contains("pack_contract_partial_scope_autofallback", reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("evtx_access_denied_live_query_fallback", reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"machine_name\":\"AD0\"", toolCall.Input, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"log_name\":\"System\"", toolCall.Input, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryBuildPackCapabilityFallbackToolCall_ResolvesHostHintAgainstPriorDiscoveryOutputs() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var packMap = Assert.IsType<Dictionary<string, string>>(ToolPackIdsByToolNameField.GetValue(session));
        packMap["ad_scope_discovery"] = "active_directory";
        packMap["eventlog_evtx_find"] = "eventlog";
        packMap["eventlog_live_query"] = "eventlog";

        var schema = ToolSchema.Object().NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("ad_scope_discovery", "scope", schema),
            new("eventlog_evtx_find", "find evtx", schema),
            new("eventlog_live_query", "live query", schema)
        };
        RebuildPackCapabilityFallbackContractsMethod.Invoke(session, new object?[] { toolDefinitions });

        var toolCalls = new List<ToolCallDto> {
            new() { CallId = "call-ad", Name = "ad_scope_discovery" },
            new() { CallId = "call-evtx", Name = "eventlog_evtx_find" }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-ad",
                Output = """
                         {"ok":true,"domain_controllers":[{"machine_name":"AD0.contoso.local"},{"machine_name":"AD1.contoso.local"}]}
                         """,
                Ok = true
            },
            new() {
                CallId = "call-evtx",
                Output = """{"ok":false,"error_code":"access_denied"}""",
                Ok = false,
                ErrorCode = "access_denied"
            }
        };
        var mutabilityHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["eventlog_live_query"] = false
        };

        var args = new object?[] {
            toolDefinitions,
            toolCalls,
            toolOutputs,
            "Can you find out why and when AD0 was rebooted?",
            mutabilityHints,
            null,
            null
        };
        var result = TryBuildPackCapabilityFallbackToolCallMethod.Invoke(session, args);

        Assert.True(Assert.IsType<bool>(result));
        var toolCall = Assert.IsType<ToolCall>(args[5]);
        Assert.Equal("eventlog_live_query", toolCall.Name);
        Assert.Contains("\"machine_name\":\"AD0.contoso.local\"", toolCall.Input, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryBuildPackCapabilityFallbackToolCall_DoesNotBuildEventlogFallbackWithoutHostHint() {
        var session = new ChatServiceSession(new ServiceOptions(), Stream.Null);
        var packMap = Assert.IsType<Dictionary<string, string>>(ToolPackIdsByToolNameField.GetValue(session));
        packMap["eventlog_evtx_find"] = "eventlog";
        packMap["eventlog_live_query"] = "eventlog";

        var schema = ToolSchema.Object().NoAdditionalProperties();
        var toolDefinitions = new List<ToolDefinition> {
            new("eventlog_evtx_find", "find evtx", schema),
            new("eventlog_live_query", "live query", schema)
        };
        RebuildPackCapabilityFallbackContractsMethod.Invoke(session, new object?[] { toolDefinitions });

        var toolCalls = new List<ToolCallDto> {
            new() { CallId = "call-4", Name = "eventlog_evtx_find" }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-4",
                Output = """{"ok":false,"error_code":"access_denied"}""",
                Ok = false,
                ErrorCode = "access_denied"
            }
        };
        var mutabilityHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["eventlog_live_query"] = false
        };

        var args = new object?[] {
            toolDefinitions,
            toolCalls,
            toolOutputs,
            "Please continue with reboot checks.",
            mutabilityHints,
            null,
            null
        };
        var result = TryBuildPackCapabilityFallbackToolCallMethod.Invoke(session, args);

        Assert.False(Assert.IsType<bool>(result));
        Assert.Equal("pack_contract_no_applicable_fallback", Assert.IsType<string>(args[6]));
    }

    [Fact]
    public void ShouldSkipWeightedRouting_TrueForActionSelectionPayload() {
        var request = "{\"ix_action_selection\":{\"id\":\"act_001\",\"title\":\"Run\",\"request\":\"Run it.\"}}";
        var result = ShouldSkipWeightedRoutingMethod.Invoke(null, new object?[] { request });

        Assert.True(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldSkipWeightedRouting_TrueForCompactFollowUp() {
        var result = ShouldSkipWeightedRoutingMethod.Invoke(null, new object?[] { "run now" });

        Assert.True(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldSkipWeightedRouting_FalseForRegularRequestText() {
        var result = ShouldSkipWeightedRoutingMethod.Invoke(null, new object?[] { "Show failed logons across all domain controllers for the last 24 hours with source IP breakdown." });

        Assert.False(Assert.IsType<bool>(result));
    }

}
