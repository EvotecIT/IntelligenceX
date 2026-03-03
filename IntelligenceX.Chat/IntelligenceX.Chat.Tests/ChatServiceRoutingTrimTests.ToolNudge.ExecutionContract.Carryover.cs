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
    public void TryBuildCarryoverStructuredNextActionToolCall_SkipsWhenAssistantDraftHostHintsConflict() {
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
                CallId = "call-evx-assistant-host",
                Name = "eventlog_top_events",
                ArgumentsJson = """{"log_name":"System","machine_name":"AD0.ad.evotec.xyz"}"""
            }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-evx-assistant-host",
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
            new object?[] { "thread-carryover-assistant-host", toolDefinitions, toolCalls, toolOutputs, mutabilityHints });

        var hostHintInput = ChatServiceSession.BuildCarryoverHostHintInputForTesting(
            "go ahead",
            "Proceeding now for AD1.ad.evotec.xyz and AD2.ad.evotec.xyz.");
        var args = new object?[] {
            "thread-carryover-assistant-host",
            hostHintInput,
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
    public void TryBuildCarryoverStructuredNextActionToolCall_SkipsWhenAssistantDraftContainsMixedHostHintsForMultiHostScope() {
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
                CallId = "call-evx-mixed-hosts",
                Name = "eventlog_top_events",
                ArgumentsJson = """{"log_name":"System","machine_name":"AD0.ad.evotec.xyz"}"""
            }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-evx-mixed-hosts",
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
            new object?[] { "thread-carryover-mixed-hosts", toolDefinitions, toolCalls, toolOutputs, mutabilityHints });

        var hostHintInput = ChatServiceSession.BuildCarryoverHostHintInputForTesting(
            "go ahead",
            "Proceeding now for AD1.ad.evotec.xyz and AD2.ad.evotec.xyz. Previous evidence for AD0.ad.evotec.xyz remains partial.");
        var args = new object?[] {
            "thread-carryover-mixed-hosts",
            hostHintInput,
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
    public void TryBuildCarryoverStructuredNextActionToolCall_SkipsRepeatedAutoReplayWithoutFreshContext() {
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
                CallId = "call-evx-repeat",
                Name = "eventlog_top_events",
                ArgumentsJson = """{"log_name":"System","machine_name":"AD0.ad.evotec.xyz"}"""
            }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-evx-repeat",
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
            new object?[] { "thread-carryover-repeat", toolDefinitions, toolCalls, toolOutputs, mutabilityHints });

        var firstArgs = new object?[] { "thread-carryover-repeat", "go ahead", toolDefinitions, mutabilityHints, null, null };
        var firstResult = TryBuildCarryoverStructuredNextActionToolCallMethod.Invoke(session, firstArgs);
        Assert.True(Assert.IsType<bool>(firstResult));

        var secondArgs = new object?[] { "thread-carryover-repeat", "go ahead", toolDefinitions, mutabilityHints, null, null };
        var secondResult = TryBuildCarryoverStructuredNextActionToolCallMethod.Invoke(session, secondArgs);

        Assert.False(Assert.IsType<bool>(secondResult));
        Assert.Equal("carryover_replay_requires_new_context", Assert.IsType<string>(secondArgs[5]));
    }

    [Fact]
    public void TryBuildCarryoverStructuredNextActionToolCall_SkipsRepeatedAutoReplayForSameSingleHostWhenArgumentsDrift() {
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
                CallId = "call-evx-repeat-drift",
                Name = "eventlog_top_events",
                ArgumentsJson = """{"log_name":"System","machine_name":"AD0.ad.evotec.xyz"}"""
            }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-evx-repeat-drift",
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
            new object?[] { "thread-carryover-repeat-drift", toolDefinitions, toolCalls, toolOutputs, mutabilityHints });

        var firstArgs = new object?[] { "thread-carryover-repeat-drift", "go ahead", toolDefinitions, mutabilityHints, null, null };
        var firstResult = TryBuildCarryoverStructuredNextActionToolCallMethod.Invoke(session, firstArgs);
        Assert.True(Assert.IsType<bool>(firstResult));

        var driftedToolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-evx-repeat-drift",
                Output = """
                         {"ok":true,"next_actions":[{"tool":"eventlog_live_query","mutating":false,"arguments":{"log_name":"Security","machine_name":"AD0.ad.evotec.xyz"}}]}
                         """,
                Ok = true
            }
        };
        RememberStructuredNextActionCarryoverMethod.Invoke(
            session,
            new object?[] { "thread-carryover-repeat-drift", toolDefinitions, toolCalls, driftedToolOutputs, mutabilityHints });

        var secondArgs = new object?[] { "thread-carryover-repeat-drift", "go ahead", toolDefinitions, mutabilityHints, null, null };
        var secondResult = TryBuildCarryoverStructuredNextActionToolCallMethod.Invoke(session, secondArgs);

        Assert.False(Assert.IsType<bool>(secondResult));
        Assert.Equal("carryover_replay_requires_new_context", Assert.IsType<string>(secondArgs[5]));
    }

    [Fact]
    public void TryBuildCarryoverStructuredNextActionToolCall_AllowsRepeatedReplayWhenUserPinsSameHost() {
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
                CallId = "call-evx-repeat-host",
                Name = "eventlog_top_events",
                ArgumentsJson = """{"log_name":"System","machine_name":"AD0.ad.evotec.xyz"}"""
            }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-evx-repeat-host",
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
            new object?[] { "thread-carryover-repeat-host", toolDefinitions, toolCalls, toolOutputs, mutabilityHints });

        var firstArgs = new object?[] { "thread-carryover-repeat-host", "go ahead", toolDefinitions, mutabilityHints, null, null };
        var firstResult = TryBuildCarryoverStructuredNextActionToolCallMethod.Invoke(session, firstArgs);
        Assert.True(Assert.IsType<bool>(firstResult));

        var secondArgs = new object?[] {
            "thread-carryover-repeat-host",
            "Run this against AD0.ad.evotec.xyz once more.",
            toolDefinitions,
            mutabilityHints,
            null,
            null
        };
        var secondResult = TryBuildCarryoverStructuredNextActionToolCallMethod.Invoke(session, secondArgs);

        Assert.True(Assert.IsType<bool>(secondResult));
        Assert.Equal("carryover_structured_next_action_readonly_autorun", Assert.IsType<string>(secondArgs[5]));
    }

    [Fact]
    public void TryBuildCarryoverStructuredNextActionToolCall_SkipsRepeatedReplayWhenOnlyAssistantDraftPinsSameHost() {
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
                CallId = "call-evx-repeat-assistant-host",
                Name = "eventlog_top_events",
                ArgumentsJson = """{"log_name":"System","machine_name":"AD0.ad.evotec.xyz"}"""
            }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-evx-repeat-assistant-host",
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
            new object?[] { "thread-carryover-repeat-assistant-host", toolDefinitions, toolCalls, toolOutputs, mutabilityHints });

        var firstResult = session.TryBuildCarryoverStructuredNextActionToolCallForTesting(
            threadId: "thread-carryover-repeat-assistant-host",
            replayDecisionUserRequest: "go ahead",
            hostHintUserRequest: "go ahead",
            toolDefinitions: toolDefinitions,
            mutatingToolHintsByName: mutabilityHints,
            toolCall: out _,
            reason: out var firstReason);
        Assert.True(firstResult);
        Assert.Equal("carryover_structured_next_action_readonly_autorun", firstReason);

        var assistantPinnedInput = ChatServiceSession.BuildCarryoverHostHintInputForTesting(
            "go ahead",
            "Still AD0.ad.evotec.xyz evidence in this replay.");
        var secondResult = session.TryBuildCarryoverStructuredNextActionToolCallForTesting(
            threadId: "thread-carryover-repeat-assistant-host",
            replayDecisionUserRequest: "go ahead",
            hostHintUserRequest: assistantPinnedInput,
            toolDefinitions: toolDefinitions,
            mutatingToolHintsByName: mutabilityHints,
            toolCall: out _,
            reason: out var secondReason);

        Assert.False(secondResult);
        Assert.Equal("carryover_replay_requires_new_context", secondReason);
    }

    [Fact]
    public void TryBuildCarryoverStructuredNextActionToolCall_SkipsScopeShiftWhenThreadHasMultiHostEvidence() {
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
                CallId = "call-evx-scope-shift",
                Name = "eventlog_top_events",
                ArgumentsJson = """{"log_name":"System","machine_name":"AD0.ad.evotec.xyz"}"""
            }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-evx-scope-shift",
                Output = """
                         {"ok":true,"next_actions":[{"tool":"eventlog_live_query","mutating":false,"arguments":{"log_name":"System","machine_name":"AD0.ad.evotec.xyz"}}]}
                         """,
                Ok = true
            }
        };
        var mutabilityHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["eventlog_live_query"] = false,
            ["eventlog_top_events"] = false
        };

        session.RememberThreadToolEvidenceForTesting(
            "thread-carryover-scope-shift",
            new List<ToolCallDto> {
                new() {
                    CallId = "evidence-1",
                    Name = "eventlog_live_query",
                    ArgumentsJson = """{"log_name":"System","machine_name":"AD1.ad.evotec.xyz"}"""
                },
                new() {
                    CallId = "evidence-2",
                    Name = "eventlog_live_query",
                    ArgumentsJson = """{"log_name":"System","machine_name":"AD2.ad.evotec.xyz"}"""
                }
            },
            new List<ToolOutputDto> {
                new() { CallId = "evidence-1", Output = """{"ok":true,"summary_markdown":"AD1 baseline"}""", Ok = true },
                new() { CallId = "evidence-2", Output = """{"ok":true,"summary_markdown":"AD2 baseline"}""", Ok = true }
            },
            mutabilityHints);

        RememberStructuredNextActionCarryoverMethod.Invoke(
            session,
            new object?[] { "thread-carryover-scope-shift", toolDefinitions, toolCalls, toolOutputs, mutabilityHints });

        var args = new object?[] {
            "thread-carryover-scope-shift",
            "other dcs",
            toolDefinitions,
            mutabilityHints,
            null,
            null
        };
        var result = TryBuildCarryoverStructuredNextActionToolCallMethod.Invoke(session, args);

        Assert.False(Assert.IsType<bool>(result));
        Assert.Equal("carryover_scope_shift_requires_fresh_plan", Assert.IsType<string>(args[5]));
    }

    [Fact]
    public void TryBuildCarryoverStructuredNextActionToolCall_SkipsQuestionScopeShiftWhenThreadHasMultiHostEvidence() {
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
                CallId = "call-evx-scope-shift-question",
                Name = "eventlog_top_events",
                ArgumentsJson = """{"log_name":"System","machine_name":"AD0.ad.evotec.xyz"}"""
            }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-evx-scope-shift-question",
                Output = """
                         {"ok":true,"next_actions":[{"tool":"eventlog_live_query","mutating":false,"arguments":{"log_name":"System","machine_name":"AD0.ad.evotec.xyz"}}]}
                         """,
                Ok = true
            }
        };
        var mutabilityHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["eventlog_live_query"] = false,
            ["eventlog_top_events"] = false
        };

        session.RememberThreadToolEvidenceForTesting(
            "thread-carryover-scope-shift-question",
            new List<ToolCallDto> {
                new() {
                    CallId = "evidence-question-1",
                    Name = "eventlog_live_query",
                    ArgumentsJson = """{"log_name":"System","machine_name":"AD1.ad.evotec.xyz"}"""
                },
                new() {
                    CallId = "evidence-question-2",
                    Name = "eventlog_live_query",
                    ArgumentsJson = """{"log_name":"System","machine_name":"AD2.ad.evotec.xyz"}"""
                }
            },
            new List<ToolOutputDto> {
                new() { CallId = "evidence-question-1", Output = """{"ok":true,"summary_markdown":"AD1 baseline"}""", Ok = true },
                new() { CallId = "evidence-question-2", Output = """{"ok":true,"summary_markdown":"AD2 baseline"}""", Ok = true }
            },
            mutabilityHints);

        RememberStructuredNextActionCarryoverMethod.Invoke(
            session,
            new object?[] { "thread-carryover-scope-shift-question", toolDefinitions, toolCalls, toolOutputs, mutabilityHints });

        var args = new object?[] {
            "thread-carryover-scope-shift-question",
            "those are correct dcs, go ahead?",
            toolDefinitions,
            mutabilityHints,
            null,
            null
        };
        var result = TryBuildCarryoverStructuredNextActionToolCallMethod.Invoke(session, args);

        Assert.False(Assert.IsType<bool>(result));
        Assert.Equal("carryover_scope_shift_requires_fresh_plan", Assert.IsType<string>(args[5]));
    }

    [Fact]
    public void TryBuildCarryoverStructuredNextActionToolCall_AllowsShortQuestionAcknowledgementReplay() {
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
                CallId = "call-evx-short-question-ack",
                Name = "eventlog_top_events",
                ArgumentsJson = """{"log_name":"System","machine_name":"AD0.ad.evotec.xyz"}"""
            }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call-evx-short-question-ack",
                Output = """
                         {"ok":true,"next_actions":[{"tool":"eventlog_live_query","mutating":false,"arguments":{"log_name":"System","machine_name":"AD0.ad.evotec.xyz"}}]}
                         """,
                Ok = true
            }
        };
        var mutabilityHints = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) {
            ["eventlog_live_query"] = false,
            ["eventlog_top_events"] = false
        };

        session.RememberThreadToolEvidenceForTesting(
            "thread-carryover-short-question-ack",
            new List<ToolCallDto> {
                new() {
                    CallId = "evidence-ack-1",
                    Name = "eventlog_live_query",
                    ArgumentsJson = """{"log_name":"System","machine_name":"AD1.ad.evotec.xyz"}"""
                },
                new() {
                    CallId = "evidence-ack-2",
                    Name = "eventlog_live_query",
                    ArgumentsJson = """{"log_name":"System","machine_name":"AD2.ad.evotec.xyz"}"""
                }
            },
            new List<ToolOutputDto> {
                new() { CallId = "evidence-ack-1", Output = """{"ok":true,"summary_markdown":"AD1 baseline"}""", Ok = true },
                new() { CallId = "evidence-ack-2", Output = """{"ok":true,"summary_markdown":"AD2 baseline"}""", Ok = true }
            },
            mutabilityHints);

        RememberStructuredNextActionCarryoverMethod.Invoke(
            session,
            new object?[] { "thread-carryover-short-question-ack", toolDefinitions, toolCalls, toolOutputs, mutabilityHints });

        var args = new object?[] {
            "thread-carryover-short-question-ack",
            "continue now?",
            toolDefinitions,
            mutabilityHints,
            null,
            null
        };
        var result = TryBuildCarryoverStructuredNextActionToolCallMethod.Invoke(session, args);

        Assert.True(Assert.IsType<bool>(result));
        Assert.Equal("carryover_structured_next_action_readonly_autorun", Assert.IsType<string>(args[5]));
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
    public void ShouldAttemptCarryoverStructuredNextActionReplay_SkipsContextualCompactFollowUpWithoutDraftAnchor() {
        var result = ChatServiceSession.ShouldAttemptCarryoverStructuredNextActionReplay(
            continuationFollowUpTurn: false,
            compactFollowUpTurn: true,
            userRequest: "i mean other dcs",
            assistantDraft: "On it.");

        Assert.False(result);
    }

    [Fact]
    public void ShouldAttemptCarryoverStructuredNextActionReplay_SkipsTwoTokenScopeShiftWithoutDraftAnchor() {
        var result = ChatServiceSession.ShouldAttemptCarryoverStructuredNextActionReplay(
            continuationFollowUpTurn: false,
            compactFollowUpTurn: true,
            userRequest: "other dcs",
            assistantDraft: "On it.");

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
    public void ShouldAttemptCarryoverStructuredNextActionReplay_SkipsWhenUserExplicitlyReferencesToolId() {
        var result = ChatServiceSession.ShouldAttemptCarryoverStructuredNextActionReplay(
            continuationFollowUpTurn: false,
            compactFollowUpTurn: true,
            userRequest: "go with eventlog_evtx_query now",
            assistantDraft: "I can run the next action now.");

        Assert.False(result);
    }
}

