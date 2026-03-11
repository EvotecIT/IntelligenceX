using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Service;
using IntelligenceX.Json;
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class ChatServiceRoutingTrimTests {
    private static readonly MethodInfo RememberSuccessfulPackPreflightCallsMethod =
        typeof(ChatServiceSession).GetMethod("RememberSuccessfulPackPreflightCalls", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("RememberSuccessfulPackPreflightCalls not found.");

    private static readonly MethodInfo RememberFailedPackPreflightCallsMethod =
        typeof(ChatServiceSession).GetMethod("RememberFailedPackPreflightCalls", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("RememberFailedPackPreflightCalls not found.");

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

    [Fact]
    public void RememberFailedPackPreflightCalls_PersistsHostGeneratedRecoveryHelperFailures() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var registry = new ToolRegistry();
        registry.Register(new PreflightStubTool(
            "customx_pack_probe",
            CreateRoutingContract("active_directory", ToolRoutingTaxonomy.RolePackInfo)));
        registry.Register(new PreflightStubTool(
            "customx_health_scan",
            CreateRoutingContract("active_directory", ToolRoutingTaxonomy.RoleOperational),
            recovery: new ToolRecoveryContract {
                IsRecoveryAware = true,
                RecoveryToolNames = new[] { "customx_recovery_discover" }
            }));
        registry.Register(new PreflightStubTool(
            "customx_recovery_discover",
            CreateRoutingContract("active_directory", ToolRoutingTaxonomy.RoleDiagnostic)));
        SetSessionRegistry(session, registry);

        var extractedCalls = new List<ToolCall> {
            new("call_operational_recovery_persist", "customx_health_scan", "{}", new JsonObject(StringComparer.Ordinal), new JsonObject(StringComparer.Ordinal))
        };

        var result = BuildHostPackPreflightCallsMethod.Invoke(session, new object?[] { "thread-pack-helper-persist", registry.GetDefinitions(), extractedCalls });
        var preflightCalls = Assert.IsAssignableFrom<IReadOnlyList<ToolCall>>(result);
        var helperCall = Assert.Single(preflightCalls, call => string.Equals(call.Name, "customx_recovery_discover", StringComparison.OrdinalIgnoreCase));
        var outputs = new[] {
            new ToolOutputDto {
                CallId = helperCall.CallId,
                Output = """{"ok":false}""",
                Ok = false,
                ErrorCode = "transport_unavailable",
                Error = "Bootstrap helper failed.",
                IsTransient = true
            }
        };

        RememberFailedPackPreflightCallsMethod.Invoke(session, new object?[] { "thread-pack-helper-persist", preflightCalls, outputs });

        Assert.Equal(new[] { "customx_recovery_discover" }, session.GetRecentHostBootstrapFailureToolNamesForTesting("thread-pack-helper-persist"));
    }

    [Fact]
    public void RememberSuccessfulPackPreflightCalls_RemembersHostGeneratedRecoveryHelpersForLaterTurns() {
        var root = Path.Combine(Path.GetTempPath(), "ix-chat-pack-helper-success-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var pendingActionsStorePath = Path.Combine(root, "pending-actions.json");
        const string threadId = "thread-pack-helper-success";

        try {
            var session1 = new ChatServiceSession(
                new ServiceOptions { PendingActionsStorePath = pendingActionsStorePath },
                Stream.Null);
            var registry1 = new ToolRegistry();
            registry1.Register(new PreflightStubTool(
                "customx_pack_probe",
                CreateRoutingContract("active_directory", ToolRoutingTaxonomy.RolePackInfo)));
            registry1.Register(new PreflightStubTool(
                "customx_health_scan",
                CreateRoutingContract("active_directory", ToolRoutingTaxonomy.RoleOperational),
                recovery: new ToolRecoveryContract {
                    IsRecoveryAware = true,
                    RecoveryToolNames = new[] { "customx_recovery_discover" }
                }));
            registry1.Register(new PreflightStubTool(
                "customx_recovery_discover",
                CreateRoutingContract("active_directory", ToolRoutingTaxonomy.RoleDiagnostic)));
            SetSessionRegistry(session1, registry1);

            var extractedCalls = new List<ToolCall> {
                new("call_operational_recovery_success", "customx_health_scan", "{}", new JsonObject(StringComparer.Ordinal), new JsonObject(StringComparer.Ordinal))
            };

            var result1 = BuildHostPackPreflightCallsMethod.Invoke(session1, new object?[] { threadId, registry1.GetDefinitions(), extractedCalls });
            var preflightCalls1 = Assert.IsAssignableFrom<IReadOnlyList<ToolCall>>(result1);
            var helperCall = Assert.Single(preflightCalls1, call => string.Equals(call.Name, "customx_recovery_discover", StringComparison.OrdinalIgnoreCase));
            var outputs = new[] {
                new ToolOutputDto {
                    CallId = helperCall.CallId,
                    Output = """{"ok":true}""",
                    Ok = true
                }
            };

            RememberSuccessfulPackPreflightCallsMethod.Invoke(session1, new object?[] { threadId, preflightCalls1, outputs });

            var session2 = new ChatServiceSession(
                new ServiceOptions { PendingActionsStorePath = pendingActionsStorePath },
                Stream.Null);
            var registry2 = new ToolRegistry();
            registry2.Register(new PreflightStubTool(
                "customx_pack_probe",
                CreateRoutingContract("active_directory", ToolRoutingTaxonomy.RolePackInfo)));
            registry2.Register(new PreflightStubTool(
                "customx_health_scan",
                CreateRoutingContract("active_directory", ToolRoutingTaxonomy.RoleOperational),
                recovery: new ToolRecoveryContract {
                    IsRecoveryAware = true,
                    RecoveryToolNames = new[] { "customx_recovery_discover" }
                }));
            registry2.Register(new PreflightStubTool(
                "customx_recovery_discover",
                CreateRoutingContract("active_directory", ToolRoutingTaxonomy.RoleDiagnostic)));
            SetSessionRegistry(session2, registry2);

            var result2 = BuildHostPackPreflightCallsMethod.Invoke(session2, new object?[] { threadId, registry2.GetDefinitions(), extractedCalls });
            var preflightCalls2 = Assert.IsAssignableFrom<IReadOnlyList<ToolCall>>(result2);

            Assert.Equal(new[] { "customx_pack_probe", "customx_recovery_discover" }, session2.GetRememberedPackPreflightToolsForTesting(threadId));
            Assert.Single(preflightCalls2);
            Assert.Equal("customx_pack_probe", preflightCalls2[0].Name);
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
    public void RememberSuccessfulPackPreflightCalls_RemovesCurrentRoundBootstrapFailuresFromRememberedSet() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-pack-preflight-prune-failures";

        session.RememberPackPreflightToolsForTesting(threadId, new[] { "customx_pack_probe", "customx_recovery_discover" });

        var executedCalls = new[] {
            new ToolCall("call_pack_probe_prune", "customx_pack_probe", "{}", new JsonObject(StringComparer.Ordinal), new JsonObject(StringComparer.Ordinal)),
            new ToolCall("call_recovery_prune", "customx_recovery_discover", "{}", new JsonObject(StringComparer.Ordinal), new JsonObject(StringComparer.Ordinal))
        };
        var outputs = new[] {
            new ToolOutputDto {
                CallId = "call_pack_probe_prune",
                Output = """{"ok":false}""",
                Ok = false,
                ErrorCode = "transport_unavailable",
                Error = "Pack probe failed.",
                IsTransient = true
            },
            new ToolOutputDto {
                CallId = "call_recovery_prune",
                Output = """{"ok":true}""",
                Ok = true
            }
        };

        RememberSuccessfulPackPreflightCallsMethod.Invoke(session, new object?[] { threadId, executedCalls, outputs });

        Assert.Equal(new[] { "customx_recovery_discover" }, session.GetRememberedPackPreflightToolsForTesting(threadId));
    }

    [Fact]
    public void RememberSuccessfulPackPreflightCalls_ClearsFailuresOnlyForCurrentSuccesses() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        const string threadId = "thread-pack-preflight-clear-success-only";

        session.RememberPackPreflightToolsForTesting(threadId, new[] { "customx_pack_probe", "customx_recovery_discover" });
        session.RememberHostBootstrapFailureForTesting(threadId, "customx_pack_probe", "pack_preflight");
        session.RememberHostBootstrapFailureForTesting(threadId, "customx_recovery_discover", "recovery_helper");

        var executedCalls = new[] {
            new ToolCall("call_recovery_success_only", "customx_recovery_discover", "{}", new JsonObject(StringComparer.Ordinal), new JsonObject(StringComparer.Ordinal))
        };
        var outputs = new[] {
            new ToolOutputDto {
                CallId = "call_recovery_success_only",
                Output = """{"ok":true}""",
                Ok = true
            }
        };

        RememberSuccessfulPackPreflightCallsMethod.Invoke(session, new object?[] { threadId, executedCalls, outputs });

        Assert.Equal(new[] { "customx_pack_probe" }, session.GetRecentHostBootstrapFailureToolNamesForTesting(threadId));
    }
}
