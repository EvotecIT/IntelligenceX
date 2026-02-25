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
    public void SupportsSyntheticHostReplayItems_EnablesOnlyCompatibleHttpTransport() {
        var nativeResult = SupportsSyntheticHostReplayItemsMethod.Invoke(null, new object?[] { OpenAITransportKind.Native });
        var appServerResult = SupportsSyntheticHostReplayItemsMethod.Invoke(null, new object?[] { OpenAITransportKind.AppServer });
        var compatibleResult = SupportsSyntheticHostReplayItemsMethod.Invoke(null, new object?[] { OpenAITransportKind.CompatibleHttp });
        var copilotCliResult = SupportsSyntheticHostReplayItemsMethod.Invoke(null, new object?[] { OpenAITransportKind.CopilotCli });

        Assert.False(Assert.IsType<bool>(nativeResult));
        Assert.False(Assert.IsType<bool>(appServerResult));
        Assert.True(Assert.IsType<bool>(compatibleResult));
        Assert.False(Assert.IsType<bool>(copilotCliResult));
    }

    [Fact]
    public void BuildHostReplayReviewInput_UsesTextReplayForNativeSafeMode() {
        var call = new ToolCall(
            callId: "host_next_action_123",
            name: "ad_scope_discovery",
            input: "{\"domain\":\"ad.evotec.xyz\"}",
            arguments: null,
            raw: new JsonObject().Add("type", "tool_call").Add("name", "ad_scope_discovery"));
        var outputs = new List<ToolOutputDto> {
            new() {
                CallId = "host_next_action_123",
                Output = "{\"ok\":true,\"domain\":\"ad.evotec.xyz\"}",
                Ok = true
            }
        };

        var inputObj = BuildHostReplayReviewInputMethod.Invoke(
            null,
            new object?[] { call, outputs, false });

        var input = Assert.IsType<ChatInput>(inputObj);
        var items = GetChatInputItems(input);
        Assert.Equal(1, items.Count);

        var first = Assert.IsType<JsonObject>(items[0].AsObject());
        Assert.Equal("text", first.GetString("type"));
        Assert.Contains("ix:host-replay-review:v1", first.GetString("text"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildHostReplayReviewInput_UsesSyntheticReplayItemsWhenSupported() {
        var call = new ToolCall(
            callId: "host_pack_fallback_123",
            name: "eventlog_live_query",
            input: "{\"machine_name\":\"AD0\"}",
            arguments: null,
            raw: new JsonObject().Add("type", "tool_call").Add("name", "eventlog_live_query"));
        var outputs = new List<ToolOutputDto> {
            new() {
                CallId = "host_pack_fallback_123",
                Output = "{\"ok\":true}",
                Ok = true
            }
        };

        var inputObj = BuildHostReplayReviewInputMethod.Invoke(
            null,
            new object?[] { call, outputs, true });

        var input = Assert.IsType<ChatInput>(inputObj);
        var items = GetChatInputItems(input);
        Assert.Equal(2, items.Count);

        var first = Assert.IsType<JsonObject>(items[0].AsObject());
        var second = Assert.IsType<JsonObject>(items[1].AsObject());
        Assert.Equal("custom_tool_call", first.GetString("type"));
        Assert.Equal("custom_tool_call_output", second.GetString("type"));
    }

    [Fact]
    public void BuildHostReplayReviewInput_FallsBackToTextReplayWhenOutputCallIdsDoNotMatch() {
        var call = new ToolCall(
            callId: "host_pack_fallback_123",
            name: "eventlog_live_query",
            input: "{\"machine_name\":\"AD0\"}",
            arguments: null,
            raw: new JsonObject().Add("type", "tool_call").Add("name", "eventlog_live_query"));
        var outputs = new List<ToolOutputDto> {
            new() {
                CallId = "host_next_action_mismatch",
                Output = "{\"ok\":true}",
                Ok = true
            }
        };

        var inputObj = BuildHostReplayReviewInputMethod.Invoke(
            null,
            new object?[] { call, outputs, true });

        var input = Assert.IsType<ChatInput>(inputObj);
        var items = GetChatInputItems(input);
        Assert.Single(items);

        var first = Assert.IsType<JsonObject>(items[0].AsObject());
        Assert.Equal("text", first.GetString("type"));
        Assert.Contains("ix:host-replay-review:v1", first.GetString("text"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveToolOutputCallId_UsesRawCallIdWhenMatched() {
        var extracted = new List<ToolCall> {
            new(
                callId: "call_123",
                name: "eventlog_live_query",
                input: "{}",
                arguments: null,
                raw: new JsonObject().Add("type", "tool_call").Add("name", "eventlog_live_query"))
        };
        var byId = new Dictionary<string, ToolCall>(StringComparer.OrdinalIgnoreCase) {
            ["call_123"] = extracted[0]
        };

        var result = ResolveToolOutputCallIdMethod.Invoke(
            null,
            new object?[] { extracted, byId, "call_123", 0 });

        Assert.Equal("call_123", Assert.IsType<string>(result));
    }

    [Fact]
    public void ResolveToolOutputCallId_FallsBackToIndexedCallWhenRawCallIdIsMismatched() {
        var first = new ToolCall(
            callId: "call_123",
            name: "eventlog_live_query",
            input: "{}",
            arguments: null,
            raw: new JsonObject().Add("type", "tool_call").Add("name", "eventlog_live_query"));
        var second = new ToolCall(
            callId: "call_456",
            name: "eventlog_live_query",
            input: "{}",
            arguments: null,
            raw: new JsonObject().Add("type", "tool_call").Add("name", "eventlog_live_query"));
        var extracted = new List<ToolCall> { first, second };
        var byId = new Dictionary<string, ToolCall>(StringComparer.OrdinalIgnoreCase) {
            ["call_123"] = first,
            ["call_456"] = second
        };

        var result = ResolveToolOutputCallIdMethod.Invoke(
            null,
            new object?[] { extracted, byId, "mismatch_call_id", 1 });

        Assert.Equal("call_456", Assert.IsType<string>(result));
    }

    [Fact]
    public void ResolveToolOutputCallId_ReturnsEmptyWhenNoSafeFallbackExists() {
        var first = new ToolCall(
            callId: "call_123",
            name: "eventlog_live_query",
            input: "{}",
            arguments: null,
            raw: new JsonObject().Add("type", "tool_call").Add("name", "eventlog_live_query"));
        var second = new ToolCall(
            callId: "call_456",
            name: "eventlog_live_query",
            input: "{}",
            arguments: null,
            raw: new JsonObject().Add("type", "tool_call").Add("name", "eventlog_live_query"));
        var extracted = new List<ToolCall> { first, second };
        var byId = new Dictionary<string, ToolCall>(StringComparer.OrdinalIgnoreCase) {
            ["call_123"] = first,
            ["call_456"] = second
        };

        var result = ResolveToolOutputCallIdMethod.Invoke(
            null,
            new object?[] { extracted, byId, string.Empty, 99 });

        Assert.Equal(string.Empty, Assert.IsType<string>(result));
    }

    [Fact]
    public void BuildToolRoundReplayInput_DeduplicatesByCallId_AndSkipsOrphanOutputs() {
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
            new() { CallId = "call_a", Output = "out-a-first", Ok = true },
            new() { CallId = "call_b", Output = "out-b-first", Ok = true },
            new() { CallId = "call_a", Output = "out-a-duplicate", Ok = true },
            new() { CallId = "orphan_call", Output = "out-orphan", Ok = true },
            new() { CallId = string.Empty, Output = "out-empty-id", Ok = true }
        };

        var inputObj = BuildToolRoundReplayInputMethod.Invoke(
            null,
            new object?[] { extracted, byId, outputs });
        var input = Assert.IsType<ChatInput>(inputObj);
        var items = GetChatInputItems(input);

        Assert.Equal(4, items.Count);

        var toolCalls = 0;
        var toolOutputs = 0;
        string? callAOutput = null;
        for (var i = 0; i < items.Count; i++) {
            var item = Assert.IsType<JsonObject>(items[i].AsObject());
            var type = item.GetString("type");
            if (string.Equals(type, "custom_tool_call", StringComparison.OrdinalIgnoreCase)) {
                toolCalls++;
                continue;
            }

            if (!string.Equals(type, "custom_tool_call_output", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            toolOutputs++;
            var callId = item.GetString("call_id");
            if (string.Equals(callId, "call_a", StringComparison.OrdinalIgnoreCase)) {
                callAOutput = item.GetString("output");
            }
        }

        Assert.Equal(2, toolCalls);
        Assert.Equal(2, toolOutputs);
        Assert.Equal("out-a-duplicate", callAOutput);
    }

    [Fact]
    public void BuildToolRoundReplayInput_DropAfterToolCall_ReplaysOnlyCompletedPair() {
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
            new() { CallId = "call_a", Output = "out-a-complete", Ok = true }
        };

        var inputObj = BuildToolRoundReplayInputMethod.Invoke(
            null,
            new object?[] { extracted, byId, outputs });
        var input = Assert.IsType<ChatInput>(inputObj);
        var items = GetChatInputItems(input);

        Assert.Equal(2, items.Count);

        var replayedCallIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var outputCallIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < items.Count; i++) {
            var item = Assert.IsType<JsonObject>(items[i].AsObject());
            var type = item.GetString("type");
            var callId = item.GetString("call_id");
            if (string.IsNullOrWhiteSpace(callId)) {
                continue;
            }

            if (string.Equals(type, "custom_tool_call", StringComparison.OrdinalIgnoreCase)) {
                replayedCallIds.Add(callId!);
                continue;
            }

            if (string.Equals(type, "custom_tool_call_output", StringComparison.OrdinalIgnoreCase)) {
                outputCallIds.Add(callId!);
            }
        }

        Assert.Contains("call_a", replayedCallIds);
        Assert.DoesNotContain("call_b", replayedCallIds);
        Assert.Contains("call_a", outputCallIds);
        Assert.DoesNotContain("call_b", outputCallIds);
    }

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

}
