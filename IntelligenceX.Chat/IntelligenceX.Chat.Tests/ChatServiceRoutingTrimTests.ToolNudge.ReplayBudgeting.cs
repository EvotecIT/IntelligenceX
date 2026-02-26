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
    public void BuildToolRoundReplayInput_UsesIndexedFallbackWhenRawCallIdIsMismatched() {
        var callA = new ToolCall(
            callId: "call_a",
            name: "eventlog_live_query",
            input: "{\"machine_name\":\"AD0\"}",
            arguments: null,
            raw: new JsonObject().Add("type", "tool_call").Add("name", "eventlog_live_query"));
        var callB = new ToolCall(
            callId: "call_b",
            name: "eventlog_live_query",
            input: "{\"machine_name\":\"AD1\"}",
            arguments: null,
            raw: new JsonObject().Add("type", "tool_call").Add("name", "eventlog_live_query"));
        var extracted = new List<ToolCall> { callA, callB };
        var byId = new Dictionary<string, ToolCall>(StringComparer.OrdinalIgnoreCase) {
            ["call_a"] = callA,
            ["call_b"] = callB
        };
        var outputs = new List<ToolOutputDto> {
            new() { CallId = "mismatch_0", Output = "out-a", Ok = true },
            new() { CallId = "mismatch_1", Output = "out-b", Ok = true }
        };

        var inputObj = BuildToolRoundReplayInputMethod.Invoke(
            null,
            new object?[] { extracted, byId, outputs });
        var input = Assert.IsType<ChatInput>(inputObj);
        var items = GetChatInputItems(input);

        Assert.Equal(4, items.Count);

        var outputCallIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < items.Count; i++) {
            var item = Assert.IsType<JsonObject>(items[i].AsObject());
            if (!string.Equals(item.GetString("type"), "custom_tool_call_output", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var callId = item.GetString("call_id");
            if (!string.IsNullOrWhiteSpace(callId)) {
                outputCallIds.Add(callId!);
            }
        }

        Assert.Contains("call_a", outputCallIds);
        Assert.Contains("call_b", outputCallIds);
    }

    [Fact]
    public void BuildToolRoundReplayInput_PrefersExplicitCallIdMatchOverIndexedFallbackForDelayedOutput() {
        var callA = new ToolCall(
            callId: "call_a",
            name: "eventlog_live_query",
            input: "{\"machine_name\":\"AD0\"}",
            arguments: null,
            raw: new JsonObject().Add("type", "tool_call").Add("name", "eventlog_live_query"));
        var callB = new ToolCall(
            callId: "call_b",
            name: "eventlog_live_query",
            input: "{\"machine_name\":\"AD1\"}",
            arguments: null,
            raw: new JsonObject().Add("type", "tool_call").Add("name", "eventlog_live_query"));
        var extracted = new List<ToolCall> { callA, callB };
        var byId = new Dictionary<string, ToolCall>(StringComparer.OrdinalIgnoreCase) {
            ["call_a"] = callA,
            ["call_b"] = callB
        };
        var outputs = new List<ToolOutputDto> {
            new() { CallId = "mismatch_0", Output = "out-a-indexed-fallback", Ok = true },
            new() { CallId = "mismatch_1", Output = "out-b-indexed-fallback", Ok = true },
            new() { CallId = "call_a", Output = "out-a-direct-delayed", Ok = true }
        };

        var inputObj = BuildToolRoundReplayInputMethod.Invoke(
            null,
            new object?[] { extracted, byId, outputs });
        var input = Assert.IsType<ChatInput>(inputObj);
        var items = GetChatInputItems(input);

        Assert.Equal(4, items.Count);
        var outputByCallId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < items.Count; i++) {
            var item = Assert.IsType<JsonObject>(items[i].AsObject());
            if (!string.Equals(item.GetString("type"), "custom_tool_call_output", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var callId = item.GetString("call_id");
            if (string.IsNullOrWhiteSpace(callId)) {
                continue;
            }

            outputByCallId[callId!] = item.GetString("output") ?? string.Empty;
        }

        Assert.Equal("out-a-direct-delayed", outputByCallId["call_a"]);
        Assert.Equal("out-b-indexed-fallback", outputByCallId["call_b"]);
    }

    [Fact]
    public void BuildToolRoundReplayInput_PrefersLatestExplicitOutputWhenSameCallIdRepeats() {
        var callA = new ToolCall(
            callId: "call_a",
            name: "eventlog_live_query",
            input: "{\"machine_name\":\"AD0\"}",
            arguments: null,
            raw: new JsonObject().Add("type", "tool_call").Add("name", "eventlog_live_query"));
        var extracted = new List<ToolCall> { callA };
        var byId = new Dictionary<string, ToolCall>(StringComparer.OrdinalIgnoreCase) {
            ["call_a"] = callA
        };
        var outputs = new List<ToolOutputDto> {
            new() { CallId = "call_a", Output = "out-a-direct-first", Ok = true },
            new() { CallId = "call_a", Output = "out-a-direct-latest", Ok = true }
        };

        var inputObj = BuildToolRoundReplayInputMethod.Invoke(
            null,
            new object?[] { extracted, byId, outputs });
        var input = Assert.IsType<ChatInput>(inputObj);
        var items = GetChatInputItems(input);

        Assert.Equal(2, items.Count);
        var outputPayload = string.Empty;
        for (var i = 0; i < items.Count; i++) {
            var item = Assert.IsType<JsonObject>(items[i].AsObject());
            if (!string.Equals(item.GetString("type"), "custom_tool_call_output", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            outputPayload = item.GetString("output") ?? string.Empty;
            break;
        }

        Assert.Equal("out-a-direct-latest", outputPayload);
    }

    [Fact]
    public void BuildToolRoundReplayInput_UsesLatestExplicitOutputAfterFallbackAndDirectDuplicates() {
        var callA = new ToolCall(
            callId: "call_a",
            name: "eventlog_live_query",
            input: "{\"machine_name\":\"AD0\"}",
            arguments: null,
            raw: new JsonObject().Add("type", "tool_call").Add("name", "eventlog_live_query"));
        var callB = new ToolCall(
            callId: "call_b",
            name: "eventlog_live_query",
            input: "{\"machine_name\":\"AD1\"}",
            arguments: null,
            raw: new JsonObject().Add("type", "tool_call").Add("name", "eventlog_live_query"));
        var extracted = new List<ToolCall> { callA, callB };
        var byId = new Dictionary<string, ToolCall>(StringComparer.OrdinalIgnoreCase) {
            ["call_a"] = callA,
            ["call_b"] = callB
        };
        var outputs = new List<ToolOutputDto> {
            new() { CallId = "mismatch_0", Output = "out-a-indexed-fallback", Ok = true },
            new() { CallId = "mismatch_1", Output = "out-b-indexed-fallback", Ok = true },
            new() { CallId = "call_a", Output = "out-a-direct-first", Ok = true },
            new() { CallId = "call_a", Output = "out-a-direct-latest", Ok = true }
        };

        var inputObj = BuildToolRoundReplayInputMethod.Invoke(
            null,
            new object?[] { extracted, byId, outputs });
        var input = Assert.IsType<ChatInput>(inputObj);
        var items = GetChatInputItems(input);

        Assert.Equal(4, items.Count);
        var outputByCallId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < items.Count; i++) {
            var item = Assert.IsType<JsonObject>(items[i].AsObject());
            if (!string.Equals(item.GetString("type"), "custom_tool_call_output", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var callId = item.GetString("call_id");
            if (string.IsNullOrWhiteSpace(callId)) {
                continue;
            }

            outputByCallId[callId!] = item.GetString("output") ?? string.Empty;
        }

        Assert.Equal("out-a-direct-latest", outputByCallId["call_a"]);
        Assert.Equal("out-b-indexed-fallback", outputByCallId["call_b"]);
    }

    [Fact]
    public void BuildToolRoundReplayInput_PrefersLatestFallbackOutputWhenOnlyFallbackMatchesExist() {
        var callA = new ToolCall(
            callId: "call_a",
            name: "eventlog_live_query",
            input: "{\"machine_name\":\"AD0\"}",
            arguments: null,
            raw: new JsonObject().Add("type", "tool_call").Add("name", "eventlog_live_query"));
        var extracted = new List<ToolCall> { callA };
        var byId = new Dictionary<string, ToolCall>(StringComparer.OrdinalIgnoreCase) {
            ["call_a"] = callA
        };
        var outputs = new List<ToolOutputDto> {
            new() { CallId = "mismatch_0", Output = "out-a-fallback-first", Ok = true },
            new() { CallId = "mismatch_1", Output = "out-a-fallback-latest", Ok = true }
        };

        var inputObj = BuildToolRoundReplayInputMethod.Invoke(
            null,
            new object?[] { extracted, byId, outputs });
        var input = Assert.IsType<ChatInput>(inputObj);
        var items = GetChatInputItems(input);

        Assert.Equal(2, items.Count);
        var outputPayload = string.Empty;
        for (var i = 0; i < items.Count; i++) {
            var item = Assert.IsType<JsonObject>(items[i].AsObject());
            if (!string.Equals(item.GetString("type"), "custom_tool_call_output", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            outputPayload = item.GetString("output") ?? string.Empty;
            break;
        }

        Assert.Equal("out-a-fallback-latest", outputPayload);
    }

    [Fact]
    public void BuildToolRoundReplayInput_DoesNotDowngradeExplicitOutputWhenLaterFallbackArrives() {
        var callA = new ToolCall(
            callId: "call_a",
            name: "eventlog_live_query",
            input: "{\"machine_name\":\"AD0\"}",
            arguments: null,
            raw: new JsonObject().Add("type", "tool_call").Add("name", "eventlog_live_query"));
        var extracted = new List<ToolCall> { callA };
        var byId = new Dictionary<string, ToolCall>(StringComparer.OrdinalIgnoreCase) {
            ["call_a"] = callA
        };
        var outputs = new List<ToolOutputDto> {
            new() { CallId = "call_a", Output = "out-a-direct", Ok = true },
            new() { CallId = "mismatch_1", Output = "out-a-fallback-later", Ok = true }
        };

        var inputObj = BuildToolRoundReplayInputMethod.Invoke(
            null,
            new object?[] { extracted, byId, outputs });
        var input = Assert.IsType<ChatInput>(inputObj);
        var items = GetChatInputItems(input);

        Assert.Equal(2, items.Count);
        var outputPayload = string.Empty;
        for (var i = 0; i < items.Count; i++) {
            var item = Assert.IsType<JsonObject>(items[i].AsObject());
            if (!string.Equals(item.GetString("type"), "custom_tool_call_output", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            outputPayload = item.GetString("output") ?? string.Empty;
            break;
        }

        Assert.Equal("out-a-direct", outputPayload);
    }

    [Fact]
    public void BuildToolRoundReplayInput_DelayedMixedReplay_EmitsSingleLatestOutputPerCall() {
        var callA = new ToolCall(
            callId: "call_a",
            name: "eventlog_live_query",
            input: "{\"machine_name\":\"AD0\"}",
            arguments: null,
            raw: new JsonObject().Add("type", "tool_call").Add("name", "eventlog_live_query"));
        var callB = new ToolCall(
            callId: "call_b",
            name: "eventlog_live_query",
            input: "{\"machine_name\":\"AD1\"}",
            arguments: null,
            raw: new JsonObject().Add("type", "tool_call").Add("name", "eventlog_live_query"));
        var extracted = new List<ToolCall> { callA, callB };
        var byId = new Dictionary<string, ToolCall>(StringComparer.OrdinalIgnoreCase) {
            ["call_a"] = callA,
            ["call_b"] = callB
        };
        var outputs = new List<ToolOutputDto> {
            new() { CallId = "mismatch_0", Output = "out-a-fallback-first", Ok = true },
            new() { CallId = "mismatch_1", Output = "out-b-fallback-first", Ok = true },
            new() { CallId = "call_a", Output = "out-a-explicit-first", Ok = true },
            new() { CallId = "call_a", Output = "out-a-explicit-latest", Ok = true },
            new() { CallId = "mismatch_2", Output = "out-a-fallback-late", Ok = true },
            new() { CallId = "call_b", Output = "out-b-explicit-latest", Ok = true },
            new() { CallId = "orphan_call", Output = "out-orphan", Ok = true }
        };

        var inputObj = BuildToolRoundReplayInputMethod.Invoke(
            null,
            new object?[] { extracted, byId, outputs });
        var input = Assert.IsType<ChatInput>(inputObj);
        var items = GetChatInputItems(input);

        Assert.Equal(4, items.Count);
        var sequence = new List<string>();
        var outputByCallId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < items.Count; i++) {
            var item = Assert.IsType<JsonObject>(items[i].AsObject());
            var type = item.GetString("type");
            var callId = item.GetString("call_id");
            if (!string.IsNullOrWhiteSpace(callId)
                && (string.Equals(type, "custom_tool_call", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(type, "custom_tool_call_output", StringComparison.OrdinalIgnoreCase))) {
                sequence.Add(type + ":" + callId);
            }

            if (!string.Equals(type, "custom_tool_call_output", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            if (string.IsNullOrWhiteSpace(callId)) {
                continue;
            }

            outputByCallId[callId!] = item.GetString("output") ?? string.Empty;
        }

        Assert.Equal(2, outputByCallId.Count);
        Assert.Equal("out-a-explicit-latest", outputByCallId["call_a"]);
        Assert.Equal("out-b-explicit-latest", outputByCallId["call_b"]);
        Assert.Equal(
            new[] {
                "custom_tool_call:call_a",
                "custom_tool_call_output:call_a",
                "custom_tool_call:call_b",
                "custom_tool_call_output:call_b"
            },
            sequence);
    }

    [Fact]
    public void BuildToolRoundReplayInput_CompactsOversizedOutput_WithReplayCompactionMarker() {
        var callA = new ToolCall(
            callId: "call_a",
            name: "eventlog_live_query",
            input: "{\"machine_name\":\"AD0\"}",
            arguments: null,
            raw: new JsonObject().Add("type", "tool_call").Add("name", "eventlog_live_query"));
        var extracted = new List<ToolCall> { callA };
        var byId = new Dictionary<string, ToolCall>(StringComparer.OrdinalIgnoreCase) {
            ["call_a"] = callA
        };
        var oversized = new string('X', 20_000);
        var outputs = new List<ToolOutputDto> {
            new() { CallId = "call_a", Output = oversized, Ok = true }
        };

        var inputObj = BuildToolRoundReplayInputMethod.Invoke(
            null,
            new object?[] { extracted, byId, outputs });
        var input = Assert.IsType<ChatInput>(inputObj);
        var items = GetChatInputItems(input);

        Assert.Equal(2, items.Count);
        string? outputPayload = null;
        for (var i = 0; i < items.Count; i++) {
            var item = Assert.IsType<JsonObject>(items[i].AsObject());
            if (!string.Equals(item.GetString("type"), "custom_tool_call_output", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            outputPayload = item.GetString("output");
            break;
        }

        var compacted = Assert.IsType<string>(outputPayload);
        Assert.Contains("ix:replay-output-compacted:v1", compacted, StringComparison.OrdinalIgnoreCase);
        Assert.True(compacted.Length <= 6000);
        Assert.True(compacted.Length < oversized.Length);
    }

    [Fact]
    public void BuildToolRoundReplayInput_EnforcesTotalReplayOutputBudgetAcrossCalls() {
        static ToolCall BuildCall(string callId, string machineName) {
            return new ToolCall(
                callId: callId,
                name: "eventlog_live_query",
                input: "{\"machine_name\":\"" + machineName + "\"}",
                arguments: null,
                raw: new JsonObject().Add("type", "tool_call").Add("name", "eventlog_live_query"));
        }

        var callA = BuildCall("call_a", "AD0");
        var callB = BuildCall("call_b", "AD1");
        var callC = BuildCall("call_c", "AD2");
        var callD = BuildCall("call_d", "AD3");
        var extracted = new List<ToolCall> { callA, callB, callC, callD };
        var byId = new Dictionary<string, ToolCall>(StringComparer.OrdinalIgnoreCase) {
            ["call_a"] = callA,
            ["call_b"] = callB,
            ["call_c"] = callC,
            ["call_d"] = callD
        };
        var oversized = new string('Y', 20_000);
        var outputs = new List<ToolOutputDto> {
            new() { CallId = "call_a", Output = oversized, Ok = true },
            new() { CallId = "call_b", Output = oversized, Ok = true },
            new() { CallId = "call_c", Output = oversized, Ok = true },
            new() { CallId = "call_d", Output = oversized, Ok = true }
        };

        var inputObj = BuildToolRoundReplayInputMethod.Invoke(
            null,
            new object?[] { extracted, byId, outputs });
        var input = Assert.IsType<ChatInput>(inputObj);
        var items = GetChatInputItems(input);

        Assert.Equal(8, items.Count);
        var totalReplayOutputChars = 0;
        var outputsByCallId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < items.Count; i++) {
            var item = Assert.IsType<JsonObject>(items[i].AsObject());
            if (!string.Equals(item.GetString("type"), "custom_tool_call_output", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var callId = item.GetString("call_id");
            if (string.IsNullOrWhiteSpace(callId)) {
                continue;
            }

            var outputPayload = item.GetString("output") ?? string.Empty;
            outputsByCallId[callId!] = outputPayload;
            totalReplayOutputChars += outputPayload.Length;
        }

        Assert.Equal(4, outputsByCallId.Count);
        Assert.True(outputsByCallId["call_a"].Length <= 6000);
        Assert.True(outputsByCallId["call_b"].Length <= 6000);
        Assert.True(outputsByCallId["call_c"].Length <= 6000);
        Assert.Equal(string.Empty, outputsByCallId["call_d"]);
        Assert.True(totalReplayOutputChars <= 16_000);
        Assert.Contains("ix:replay-output-compacted:v1", outputsByCallId["call_a"], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ix:replay-output-compacted:v1", outputsByCallId["call_b"], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ix:replay-output-compacted:v1", outputsByCallId["call_c"], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveContextAwareReplayOutputCharBudgets_AdjustsByContextWindow() {
        var smallObj = ResolveContextAwareReplayOutputCharBudgetsMethod.Invoke(null, new object?[] { 8_192L });
        var mediumObj = ResolveContextAwareReplayOutputCharBudgetsMethod.Invoke(null, new object?[] { 16_384L });
        var defaultObj = ResolveContextAwareReplayOutputCharBudgetsMethod.Invoke(null, new object?[] { 32_768L });
        var largeObj = ResolveContextAwareReplayOutputCharBudgetsMethod.Invoke(null, new object?[] { 65_536L });
        var unknownObj = ResolveContextAwareReplayOutputCharBudgetsMethod.Invoke(null, new object?[] { 0L });

        var small = Assert.IsType<ValueTuple<int, int>>(smallObj);
        var medium = Assert.IsType<ValueTuple<int, int>>(mediumObj);
        var @default = Assert.IsType<ValueTuple<int, int>>(defaultObj);
        var large = Assert.IsType<ValueTuple<int, int>>(largeObj);
        var unknown = Assert.IsType<ValueTuple<int, int>>(unknownObj);

        Assert.Equal((2_500, 7_000), small);
        Assert.Equal((4_000, 11_000), medium);
        Assert.Equal((6_000, 16_000), @default);
        Assert.Equal((8_000, 22_000), large);
        Assert.Equal((6_000, 16_000), unknown);
    }

    [Fact]
    public void BuildToolRoundReplayInputWithBudget_AppliesProvidedBudgetAndTracksStats() {
        var callA = new ToolCall(
            callId: "call_a",
            name: "eventlog_live_query",
            input: "{\"machine_name\":\"AD0\"}",
            arguments: null,
            raw: new JsonObject().Add("type", "tool_call").Add("name", "eventlog_live_query"));
        var extracted = new List<ToolCall> { callA };
        var byId = new Dictionary<string, ToolCall>(StringComparer.OrdinalIgnoreCase) {
            ["call_a"] = callA
        };
        var oversized = new string('Z', 12_000);
        var outputs = new List<ToolOutputDto> {
            new() { CallId = "call_a", Output = oversized, Ok = true }
        };
        var smallBudget = CreateReplayOutputCompactionBudget(
            maxOutputCharsPerCall: 2_500,
            maxOutputCharsTotal: 7_000,
            effectiveContextLength: 8_192L,
            contextAwareBudgetApplied: true);

        var invokeArgs = new object?[] { extracted, byId, outputs, smallBudget, null };
        var inputObj = BuildToolRoundReplayInputWithBudgetMethod.Invoke(null, invokeArgs);
        var input = Assert.IsType<ChatInput>(inputObj);
        var items = GetChatInputItems(input);

        Assert.Equal(2, items.Count);
        string? outputPayload = null;
        for (var i = 0; i < items.Count; i++) {
            var item = Assert.IsType<JsonObject>(items[i].AsObject());
            if (!string.Equals(item.GetString("type"), "custom_tool_call_output", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            outputPayload = item.GetString("output");
            break;
        }

        var compacted = Assert.IsType<string>(outputPayload);
        Assert.Contains("ix:replay-output-compacted:v1", compacted, StringComparison.OrdinalIgnoreCase);
        Assert.True(compacted.Length <= 2_500);
        Assert.True(compacted.Length < oversized.Length);

        Assert.NotNull(invokeArgs[4]);
        var statsObj = invokeArgs[4]!;
        Assert.Equal(1, ReadIntRecordProperty(statsObj, "ReplayedCallCount"));
        Assert.Equal(12_000, ReadIntRecordProperty(statsObj, "OriginalTotalChars"));
        Assert.Equal(compacted.Length, ReadIntRecordProperty(statsObj, "CompactedTotalChars"));
        Assert.Equal(1, ReadIntRecordProperty(statsObj, "CompactedCallCount"));
    }

    [Fact]
    public void BuildReplayOutputCompactionStatusMessage_EmitsMarkerAndBudgetDetails() {
        var budget = CreateReplayOutputCompactionBudget(
            maxOutputCharsPerCall: 2_500,
            maxOutputCharsTotal: 7_000,
            effectiveContextLength: 8_192L,
            contextAwareBudgetApplied: true);
        var stats = CreateReplayOutputCompactionStats(
            replayedCallCount: 2,
            originalTotalChars: 24_000,
            compactedTotalChars: 5_000,
            compactedCallCount: 1);

        var messageObj = BuildReplayOutputCompactionStatusMessageMethod.Invoke(null, new[] { budget, stats });
        var message = Assert.IsType<string>(messageObj);

        Assert.Contains("ix:replay-output-budget:v1", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("where=tool_replay_input", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reason=output_budget_compaction", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("compacted_calls=1", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("replayed_calls=2", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("per_call_budget=2500", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("total_budget=7000", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("context_aware=true", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("context_tier=small", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("context_length=8192", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildNativeHostReplayReviewPrompt_IncludesToolIdentityAndEvidence() {
        var call = new ToolCall(
            callId: "host_carryover_next_action_123",
            name: "ad_scope_discovery",
            input: "{\"include_trusts\":true}",
            arguments: null,
            raw: new JsonObject().Add("type", "tool_call").Add("name", "ad_scope_discovery"));
        var outputs = new List<ToolOutputDto> {
            new() {
                CallId = "host_carryover_next_action_123",
                Output = "{\"ok\":true,\"domain_controllers\":[\"AD0\",\"AD1\"]}",
                Ok = true
            }
        };

        var promptObj = BuildNativeHostReplayReviewPromptMethod.Invoke(
            null,
            new object?[] { call, outputs });
        var prompt = Assert.IsType<string>(promptObj);

        Assert.Contains("ix:host-replay-review:v1", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("executed_tool: ad_scope_discovery", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("host_carryover_next_action_123", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("domain_controllers", prompt, StringComparison.OrdinalIgnoreCase);
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
