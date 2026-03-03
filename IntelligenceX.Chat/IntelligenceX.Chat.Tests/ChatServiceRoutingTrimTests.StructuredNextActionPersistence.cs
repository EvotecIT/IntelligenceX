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
    public void TryBuildCarryoverStructuredNextActionToolCall_RehydratesPersistedSnapshotAfterRestart() {
        var root = Path.Combine(Path.GetTempPath(), "ix-chat-structured-carryover-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var pendingActionsStorePath = Path.Combine(root, "pending-actions.json");
        const string threadId = "thread-carryover-persist";

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

        try {
            var session1 = new ChatServiceSession(
                new ServiceOptions { PendingActionsStorePath = pendingActionsStorePath },
                Stream.Null);
            RememberStructuredNextActionCarryoverMethod.Invoke(
                session1,
                new object?[] { threadId, toolDefinitions, toolCalls, toolOutputs, mutabilityHints });

            var session2 = new ChatServiceSession(
                new ServiceOptions { PendingActionsStorePath = pendingActionsStorePath },
                Stream.Null);
            var args = new object?[] { threadId, "go ahead", toolDefinitions, mutabilityHints, null, null };
            var result = TryBuildCarryoverStructuredNextActionToolCallMethod.Invoke(session2, args);

            Assert.True(Assert.IsType<bool>(result));
            var toolCall = Assert.IsType<ToolCall>(args[4]);
            Assert.Equal("ad_scope_discovery", toolCall.Name);
            Assert.NotNull(toolCall.Arguments);
            Assert.True(toolCall.Arguments!.GetBoolean("include_trusts", defaultValue: false));
            Assert.Equal(3, toolCall.Arguments.GetInt64("max_domains"));
            Assert.Equal("carryover_structured_next_action_readonly_autorun", Assert.IsType<string>(args[5]));
        } finally {
            try {
                if (Directory.Exists(root)) {
                    Directory.Delete(root, recursive: true);
                }
            } catch {
                // Best effort test cleanup only.
            }
        }
    }
}
