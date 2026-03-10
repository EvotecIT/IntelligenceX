using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Service;
using IntelligenceX.Json;
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class ChatServiceRoutingTrimTests {
    [Fact]
    public void BuildHostPackPreflightCalls_SkipsPersistedPreflightAfterRestart() {
        var root = Path.Combine(Path.GetTempPath(), "ix-chat-pack-preflight-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var pendingActionsStorePath = Path.Combine(root, "pending-actions.json");
        const string threadId = "thread-pack-preflight-persist";

        try {
            var session1 = new ChatServiceSession(
                new ServiceOptions { PendingActionsStorePath = pendingActionsStorePath },
                Stream.Null);
            session1.RememberPackPreflightToolsForTesting(threadId, new[] { "customx_pack_probe" });

            var session2 = new ChatServiceSession(
                new ServiceOptions { PendingActionsStorePath = pendingActionsStorePath },
                Stream.Null);
            var registry = new ToolRegistry();
            registry.Register(new PreflightStubTool(
                "customx_pack_probe",
                CreateRoutingContract("active_directory", ToolRoutingTaxonomy.RolePackInfo)));
            registry.Register(new PreflightStubTool(
                "customx_health_scan",
                CreateRoutingContract("active_directory", ToolRoutingTaxonomy.RoleOperational)));
            SetSessionRegistry(session2, registry);

            var extractedCalls = new List<ToolCall> {
                new("call_operational_persisted", "customx_health_scan", "{}", new JsonObject(StringComparer.Ordinal), new JsonObject(StringComparer.Ordinal))
            };

            var result = BuildHostPackPreflightCallsMethod.Invoke(session2, new object?[] { threadId, registry.GetDefinitions(), extractedCalls });
            var preflightCalls = Assert.IsAssignableFrom<IReadOnlyList<ToolCall>>(result);

            Assert.Empty(preflightCalls);
            Assert.Equal(new[] { "customx_pack_probe" }, session2.GetRememberedPackPreflightToolsForTesting(threadId));
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

    [Fact]
    public void BuildHostPackPreflightCalls_DoesNotReuseExpiredPersistedPreflightAfterRestart() {
        var root = Path.Combine(Path.GetTempPath(), "ix-chat-pack-preflight-expired-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var pendingActionsStorePath = Path.Combine(root, "pending-actions.json");
        const string threadId = "thread-pack-preflight-expired";

        try {
            var session1 = new ChatServiceSession(
                new ServiceOptions { PendingActionsStorePath = pendingActionsStorePath },
                Stream.Null);
            session1.RememberPackPreflightToolsForTesting(
                threadId,
                new[] { "customx_pack_probe" },
                DateTime.UtcNow.AddHours(-12).Ticks);

            var session2 = new ChatServiceSession(
                new ServiceOptions { PendingActionsStorePath = pendingActionsStorePath },
                Stream.Null);
            var registry = new ToolRegistry();
            registry.Register(new PreflightStubTool(
                "customx_pack_probe",
                CreateRoutingContract("active_directory", ToolRoutingTaxonomy.RolePackInfo)));
            registry.Register(new PreflightStubTool(
                "customx_health_scan",
                CreateRoutingContract("active_directory", ToolRoutingTaxonomy.RoleOperational)));
            SetSessionRegistry(session2, registry);

            var extractedCalls = new List<ToolCall> {
                new("call_operational_expired", "customx_health_scan", "{}", new JsonObject(StringComparer.Ordinal), new JsonObject(StringComparer.Ordinal))
            };

            var result = BuildHostPackPreflightCallsMethod.Invoke(session2, new object?[] { threadId, registry.GetDefinitions(), extractedCalls });
            var preflightCalls = Assert.IsAssignableFrom<IReadOnlyList<ToolCall>>(result);

            var preflightCall = Assert.Single(preflightCalls);
            Assert.Equal("customx_pack_probe", preflightCall.Name);
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

    [Fact]
    public void BuildHostPackPreflightCalls_SkipsPersistedFailedPreflightAfterRestart() {
        var root = Path.Combine(Path.GetTempPath(), "ix-chat-pack-preflight-failed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var pendingActionsStorePath = Path.Combine(root, "pending-actions.json");
        const string threadId = "thread-pack-preflight-failed";

        try {
            var session1 = new ChatServiceSession(
                new ServiceOptions { PendingActionsStorePath = pendingActionsStorePath },
                Stream.Null);
            session1.RememberHostBootstrapFailureForTesting(threadId, "customx_pack_probe", "pack_preflight");

            var session2 = new ChatServiceSession(
                new ServiceOptions { PendingActionsStorePath = pendingActionsStorePath },
                Stream.Null);
            var registry = new ToolRegistry();
            registry.Register(new PreflightStubTool(
                "customx_pack_probe",
                CreateRoutingContract("active_directory", ToolRoutingTaxonomy.RolePackInfo)));
            registry.Register(new PreflightStubTool(
                "customx_health_scan",
                CreateRoutingContract("active_directory", ToolRoutingTaxonomy.RoleOperational)));
            SetSessionRegistry(session2, registry);

            var extractedCalls = new List<ToolCall> {
                new("call_operational_failed_preflight", "customx_health_scan", "{}", new JsonObject(StringComparer.Ordinal), new JsonObject(StringComparer.Ordinal))
            };

            var result = BuildHostPackPreflightCallsMethod.Invoke(session2, new object?[] { threadId, registry.GetDefinitions(), extractedCalls });
            var preflightCalls = Assert.IsAssignableFrom<IReadOnlyList<ToolCall>>(result);

            Assert.Empty(preflightCalls);
            Assert.Equal(new[] { "customx_pack_probe" }, session2.GetRecentHostBootstrapFailureToolNamesForTesting(threadId));
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

    [Fact]
    public async Task ExecuteToolAsync_SkipsPersistedFailedRecoveryHelperAfterRestart() {
        var root = Path.Combine(Path.GetTempPath(), "ix-chat-recovery-helper-failed-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var pendingActionsStorePath = Path.Combine(root, "pending-actions.json");
        const string threadId = "thread-recovery-helper-failed";

        try {
            var session1 = new ChatServiceSession(
                new ServiceOptions { PendingActionsStorePath = pendingActionsStorePath },
                Stream.Null);
            session1.RememberHostBootstrapFailureForTesting(threadId, "system_context", "recovery_helper");

            var session2 = new ChatServiceSession(
                new ServiceOptions { PendingActionsStorePath = pendingActionsStorePath },
                Stream.Null);
            var mainAttempts = 0;
            var helperAttempts = 0;

            var registry = new ToolRegistry();
            registry.Register(new StubTool(
                BuildOperationalRecoveryAwareDefinition(
                    "computerx_probe",
                    recoveryToolNames: new[] { "system_context" },
                    parameters: new JsonObject()
                        .Add("type", "object")
                        .Add("properties", new JsonObject()
                            .Add("computer_name", new JsonObject().Add("type", "string")))
                        .Add("required", new JsonArray().Add("computer_name"))),
                (_, _) => {
                    var attempt = System.Threading.Interlocked.Increment(ref mainAttempts);
                    if (attempt == 1) {
                        return Task.FromResult(ToolOutputEnvelope.Error(
                            errorCode: "transport_unavailable",
                            error: "Remote transport is temporarily unavailable.",
                            isTransient: true));
                    }

                    return Task.FromResult("""{"ok":true}""");
                }));
            registry.Register(new StubTool(
                BuildReadOnlyHelperDefinition(
                    "system_context",
                    parameters: new JsonObject()
                        .Add("type", "object")
                        .Add("properties", new JsonObject()
                            .Add("computer_name", new JsonObject().Add("type", "string")))
                        .Add("required", new JsonArray().Add("computer_name"))),
                (_, _) => {
                    System.Threading.Interlocked.Increment(ref helperAttempts);
                    return Task.FromResult("""{"ok":true}""");
                }));
            SetSessionRegistry(session2, registry);

            var arguments = new JsonObject().Add("computer_name", "srv-01");
            var call = new ToolCall(
                "call_main_persisted_helper_failure",
                "computerx_probe",
                JsonLite.Serialize(arguments),
                arguments,
                new JsonObject());

            var output = await session2.ExecuteToolAsyncForTesting(threadId, "Check srv-01 health.", call, 5, CancellationToken.None);

            Assert.True(output.Ok is true);
            Assert.Equal(2, mainAttempts);
            Assert.Equal(0, helperAttempts);
            Assert.Equal(new[] { "system_context" }, session2.GetRecentHostBootstrapFailureToolNamesForTesting(threadId));
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
