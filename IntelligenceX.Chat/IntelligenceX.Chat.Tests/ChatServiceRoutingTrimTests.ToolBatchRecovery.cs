using System;
using System.Collections.Generic;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class ChatServiceRoutingTrimTests {
    [Theory]
    [InlineData("fs_write_file", false)]
    [InlineData("ad_user_create", false)]
    [InlineData("system_service_restart", false)]
    [InlineData("ad_replication_summary", false)]
    [InlineData("eventlog_query", false)]
    public void IsLikelyMutatingToolName_DoesNotInferMutabilityFromToolName(string toolName, bool expected) {
        var result = IsLikelyMutatingToolNameMethod.Invoke(null, new object?[] { toolName });
        Assert.Equal(expected, Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldReplayToolCallAtLowConcurrency_AllowsTransientNonMutatingFailure() {
        var call = new ToolCall("call_001", "ad_replication_summary", null, null, new JsonObject());
        var output = new ToolOutputDto {
            CallId = "call_001",
            Output = "{\"ok\":false,\"error\":\"timed out\"}",
            Ok = false,
            ErrorCode = "tool_timeout",
            Error = "timed out after 20s",
            IsTransient = true
        };

        var result = ShouldReplayToolCallAtLowConcurrencyMethod.Invoke(null, new object?[] { call, output });
        Assert.True(Assert.IsType<bool>(result));
    }

    [Fact]
    public void ShouldReplayToolCallAtLowConcurrency_ReplaysWhenNoStructuredMutabilityHintIsAvailable() {
        var call = new ToolCall("call_002", "fs_write_file", null, null, new JsonObject());
        var output = new ToolOutputDto {
            CallId = "call_002",
            Output = "{\"ok\":false,\"error\":\"timed out\"}",
            Ok = false,
            ErrorCode = "tool_timeout",
            Error = "timed out after 20s",
            IsTransient = true
        };

        var result = ShouldReplayToolCallAtLowConcurrencyMethod.Invoke(null, new object?[] { call, output });
        Assert.True(Assert.IsType<bool>(result));
    }

    [Fact]
    public void CollectLowConcurrencyRecoveryIndexes_CapsRecoveryPassToThreeCalls() {
        var calls = new List<ToolCall>();
        var outputs = new List<ToolOutputDto>();
        for (var i = 0; i < 5; i++) {
            calls.Add(new ToolCall("call_" + i, "ad_replication_summary", null, null, new JsonObject()));
            outputs.Add(new ToolOutputDto {
                CallId = "call_" + i,
                Output = "{\"ok\":false,\"error\":\"timed out\"}",
                Ok = false,
                ErrorCode = "tool_timeout",
                Error = "timed out",
                IsTransient = true
            });
        }

        var result = CollectLowConcurrencyRecoveryIndexesMethod.Invoke(null, new object?[] { calls, outputs });
        var indexes = Assert.IsType<int[]>(result);

        Assert.Equal(3, indexes.Length);
        Assert.Equal(0, indexes[0]);
        Assert.Equal(1, indexes[1]);
        Assert.Equal(2, indexes[2]);
    }

    [Fact]
    public void HasLikelyMutatingToolCalls_FalseWithoutStructuredHints() {
        var calls = new List<ToolCall> {
            new("call_1", "ad_replication_summary", null, null, new JsonObject()),
            new("call_2", "fs_write_file", null, null, new JsonObject())
        };

        var result = HasLikelyMutatingToolCallsMethod.Invoke(null, new object?[] { calls });
        Assert.False(Assert.IsType<bool>(result));
    }

    [Fact]
    public void BuildToolBatchRecoveringMessage_DescribesLowConcurrencyRetryPass() {
        var result = BuildToolBatchRecoveringMessageMethod.Invoke(null, new object?[] { 2, 7 });
        var text = Assert.IsType<string>(result);

        Assert.Contains("Retrying 2", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("low concurrency", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("7 total", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildToolBatchRecoveredMessage_ReportsRemainingFailuresWhenPresent() {
        var result = BuildToolBatchRecoveredMessageMethod.Invoke(null, new object?[] { 2, 1 });
        var text = Assert.IsType<string>(result);

        Assert.Contains("2 retried", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1 failure", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildToolHeartbeatMessage_ContainsToolAndElapsedSeconds() {
        var result = BuildToolHeartbeatMessageMethod.Invoke(null, new object?[] { "ad_replication_summary", 13 });
        var text = Assert.IsType<string>(result);

        Assert.Contains("ad_replication_summary", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("13s", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildMutatingToolHintsByName_DetectsReadWriteSchemaHints() {
        var schema = new JsonObject()
            .Add("type", "object")
            .Add("properties", new JsonObject()
                .Add("intent", new JsonObject()
                    .Add("type", "string")
                    .Add("enum", new JsonArray().Add("read_only").Add("read_write"))));
        var definitions = new List<ToolDefinition> {
            new("powershell_run", "PowerShell runtime", schema)
        };

        var result = BuildMutatingToolHintsByNameMethod.Invoke(null, new object?[] { definitions });
        var hints = Assert.IsAssignableFrom<IReadOnlyDictionary<string, bool>>(result);

        Assert.True(hints.TryGetValue("powershell_run", out var isMutating));
        Assert.True(isMutating);
    }

    [Fact]
    public void BuildMutatingToolHintsByName_DetectsReadOnlyTagHints() {
        var definitions = new List<ToolDefinition> {
            new("ad_whoami", "AD context", tags: new[] { "read_only", "inventory" })
        };

        var result = BuildMutatingToolHintsByNameMethod.Invoke(null, new object?[] { definitions });
        var hints = Assert.IsAssignableFrom<IReadOnlyDictionary<string, bool>>(result);

        Assert.True(hints.TryGetValue("ad_whoami", out var isMutating));
        Assert.False(isMutating);
    }
}
