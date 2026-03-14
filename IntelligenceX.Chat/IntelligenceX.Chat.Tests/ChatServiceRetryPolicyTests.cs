using System;
using System.Reflection;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Service;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using Xunit;

namespace IntelligenceX.Chat.Tests;

/// <summary>
/// Covers retry policy boundaries for transient/permanent tool failures.
/// </summary>
public sealed class ChatServiceRetryPolicyTests {
    private static readonly MethodInfo IsProjectionViewArgumentFailureMethod = typeof(ChatServiceSession).GetMethod(
        "IsProjectionViewArgumentFailure",
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("IsProjectionViewArgumentFailure not found.");
    private static readonly MethodInfo TryBuildProjectionArgsFallbackCallMethod = typeof(ChatServiceSession).GetMethod(
        "TryBuildProjectionArgsFallbackCall",
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("TryBuildProjectionArgsFallbackCall not found.");

    [Fact]
    public void ShouldRetryToolCall_DoesNotRetryPermanentAccessDeniedFailure() {
        var profile = InvokeResolveRetryProfile(
            "ad_replication_summary",
            BuildRecoveryAwareDefinition(
                "ad_replication_summary",
                maxRetryAttempts: 1,
                "timeout",
                "query_failed",
                "transport_unavailable"));
        var output = new ToolOutputDto {
            CallId = "call-1",
            Output = "{\"error_code\":\"permission_denied\",\"error\":\"Unauthorized: access denied to domain controller.\"}",
            Ok = false,
            ErrorCode = "permission_denied",
            Error = "Unauthorized: access denied to domain controller.",
            IsTransient = true
        };

        var shouldRetry = InvokeShouldRetryToolCall(output, profile, attemptIndex: 0);

        Assert.False(shouldRetry);
    }

    [Fact]
    public void ShouldRetryToolCall_WithoutRecoveryContract_DoesNotRetryTimeout() {
        var profile = InvokeResolveRetryProfile("ad_replication_summary");
        var output = new ToolOutputDto {
            CallId = "call-contractless",
            Output = "{\"error\":\"Tool timed out.\"}",
            Ok = false,
            ErrorCode = "tool_timeout",
            Error = "Tool timed out."
        };

        var shouldRetry = InvokeShouldRetryToolCall(output, profile, attemptIndex: 0);

        Assert.False(shouldRetry);
    }

    [Fact]
    public void ShouldRetryToolCall_RetriesTimeoutBeforeFinalAttemptOnly() {
        var profile = InvokeResolveRetryProfile(
            "ad_replication_summary",
            BuildRecoveryAwareDefinition("ad_replication_summary", maxRetryAttempts: 1, "timeout"));
        var output = new ToolOutputDto {
            CallId = "call-2",
            Output = "{\"error\":\"Tool timed out.\"}",
            Ok = false,
            ErrorCode = "tool_timeout",
            Error = "Tool timed out."
        };

        var shouldRetryFirstAttempt = InvokeShouldRetryToolCall(output, profile, attemptIndex: 0);
        var shouldRetryFinalAttempt = InvokeShouldRetryToolCall(output, profile, attemptIndex: 1);

        Assert.True(shouldRetryFirstAttempt);
        Assert.False(shouldRetryFinalAttempt);
    }

    [Fact]
    public void ShouldRetryToolCall_DoesNotRetryTransportSignalsForFsProfile() {
        var profile = InvokeResolveRetryProfile(
            "fs_read_file",
            BuildRecoveryAwareDefinition("fs_read_file", maxRetryAttempts: 1, "timeout", "query_failed"));
        var output = new ToolOutputDto {
            CallId = "call-3",
            Output = "{\"error\":\"Service temporarily unavailable.\"}",
            Ok = false,
            ErrorCode = "transport_unavailable",
            Error = "Service temporarily unavailable."
        };

        var shouldRetry = InvokeShouldRetryToolCall(output, profile, attemptIndex: 0);

        Assert.False(shouldRetry);
    }

    [Fact]
    public void ShouldRetryToolCall_RetriesTimeoutForDomainDetectiveProfile() {
        var profile = InvokeResolveRetryProfile(
            "domaindetective_domain_summary",
            BuildRecoveryAwareDefinition("domaindetective_domain_summary", maxRetryAttempts: 2, "timeout", "query_failed"));
        var output = new ToolOutputDto {
            CallId = "call-3c",
            Output = "{\"error\":\"Tool timed out.\"}",
            Ok = false,
            ErrorCode = "tool_timeout",
            Error = "Tool timed out."
        };

        var shouldRetry = InvokeShouldRetryToolCall(output, profile, attemptIndex: 0);

        Assert.True(shouldRetry);
    }

    [Fact]
    public void ShouldRetryToolCall_RetriesTransportForDnsClientXProfile() {
        var profile = InvokeResolveRetryProfile(
            "dnsclientx_query",
            BuildRecoveryAwareDefinition(
                "dnsclientx_query",
                maxRetryAttempts: 2,
                "timeout",
                "query_failed",
                "transport_unavailable"));
        var output = new ToolOutputDto {
            CallId = "call-3d",
            Output = "{\"error\":\"Service temporarily unavailable.\"}",
            Ok = false,
            ErrorCode = "transport_unavailable",
            Error = "Service temporarily unavailable."
        };

        var shouldRetry = InvokeShouldRetryToolCall(output, profile, attemptIndex: 0);

        Assert.True(shouldRetry);
    }

    [Fact]
    public void ShouldRetryToolCall_RetriesTransportForTestimoXProfile() {
        var profile = InvokeResolveRetryProfile(
            "testimox_rules_run",
            BuildRecoveryAwareDefinition(
                "testimox_rules_run",
                maxRetryAttempts: 1,
                "execution_failed",
                "timeout",
                "transport_unavailable"));
        var output = new ToolOutputDto {
            CallId = "call-3e",
            Output = "{\"error\":\"Upstream connection reset.\"}",
            Ok = false,
            ErrorCode = "transport_unavailable",
            Error = "Upstream connection reset."
        };

        var shouldRetry = InvokeShouldRetryToolCall(output, profile, attemptIndex: 0);

        Assert.True(shouldRetry);
    }

    [Fact]
    public void ShouldRetryToolCall_RetriesTimeoutForComputerXPrefixProfile() {
        var profile = InvokeResolveRetryProfile(
            "computerx_inventory_snapshot",
            BuildRecoveryAwareDefinition("computerx_inventory_snapshot", maxRetryAttempts: 1, "timeout", "query_failed"));
        var output = new ToolOutputDto {
            CallId = "call-3f",
            Output = "{\"error\":\"Tool timed out.\"}",
            Ok = false,
            ErrorCode = "tool_timeout",
            Error = "Tool timed out."
        };

        var shouldRetry = InvokeShouldRetryToolCall(output, profile, attemptIndex: 0);

        Assert.True(shouldRetry);
    }

    [Fact]
    public void ShouldRetryToolCall_DoesNotRetryTransportForCustomPackTaggedToolWithoutRecoveryContract() {
        var profile = InvokeResolveRetryProfile(
            "custom_diagnostic_probe",
            new ToolDefinition(
                name: "custom_diagnostic_probe",
                description: "Custom DNS diagnostics",
                parameters: null,
                category: "dns",
                tags: new[] { "pack:custom_dns_tools", "resolver" }));
        var output = new ToolOutputDto {
            CallId = "call-3g",
            Output = "{\"error\":\"Service temporarily unavailable.\"}",
            Ok = false,
            ErrorCode = "transport_unavailable",
            Error = "Service temporarily unavailable."
        };

        var shouldRetry = InvokeShouldRetryToolCall(output, profile, attemptIndex: 0);

        Assert.False(shouldRetry);
    }

    [Fact]
    public void ShouldRetryToolCall_RetriesTransportForCustomToolWithRecoveryContract() {
        var profile = InvokeResolveRetryProfile(
            "custom_diagnostic_probe",
            BuildRecoveryAwareDefinition(
                "custom_diagnostic_probe",
                maxRetryAttempts: 1,
                "timeout",
                "transport_unavailable"));
        var output = new ToolOutputDto {
            CallId = "call-3g2",
            Output = "{\"error\":\"Service temporarily unavailable.\"}",
            Ok = false,
            ErrorCode = "transport_unavailable",
            Error = "Service temporarily unavailable."
        };

        var shouldRetry = InvokeShouldRetryToolCall(output, profile, attemptIndex: 0);

        Assert.True(shouldRetry);
    }

    [Fact]
    public void ShouldRetryToolCall_RetriesTransportVariantCodeWhenProfileDeclaresTransportUnavailable() {
        var profile = InvokeResolveRetryProfile(
            "custom_diagnostic_probe",
            BuildRecoveryAwareDefinition(
                "custom_diagnostic_probe",
                maxRetryAttempts: 1,
                "transport_unavailable"));
        var output = new ToolOutputDto {
            CallId = "call-3g3",
            Output = "{\"error\":\"Upstream connection reset.\"}",
            Ok = false,
            ErrorCode = "connection_reset",
            Error = "Upstream connection reset.",
            IsTransient = false
        };

        var shouldRetry = InvokeShouldRetryToolCall(output, profile, attemptIndex: 0);

        Assert.True(shouldRetry);
    }

    [Fact]
    public void ShouldRetryToolCall_DoesNotRetryTransportForCustomFileScopePack() {
        var profile = InvokeResolveRetryProfile(
            "custom_file_reader",
            new ToolDefinition(
                name: "custom_file_reader",
                description: "Custom file reader",
                parameters: null,
                category: "filesystem",
                tags: new[] { "pack:custom_file_tools" }));
        var output = new ToolOutputDto {
            CallId = "call-3h",
            Output = "{\"error\":\"Service temporarily unavailable.\"}",
            Ok = false,
            ErrorCode = "transport_unavailable",
            Error = "Service temporarily unavailable."
        };

        var shouldRetry = InvokeShouldRetryToolCall(output, profile, attemptIndex: 0);

        Assert.False(shouldRetry);
    }

    [Fact]
    public void ShouldRetryToolCall_UsesRecoveryContractRetryBudgetForGenericPack() {
        var profile = InvokeResolveRetryProfile(
            "custom_domain_lookup",
            new ToolDefinition(
                name: "custom_domain_lookup",
                description: "Custom resolver",
                parameters: null,
                category: "dns",
                tags: new[] { "pack:custom_domain_tools", "resolver" },
                recovery: new ToolRecoveryContract {
                    IsRecoveryAware = true,
                    SupportsTransientRetry = true,
                    MaxRetryAttempts = 2,
                    RetryableErrorCodes = new[] { "timeout" }
                }));
        var output = new ToolOutputDto {
            CallId = "call-3i",
            Output = "{\"error\":\"Tool timed out.\"}",
            Ok = false,
            ErrorCode = "tool_timeout",
            Error = "Tool timed out."
        };

        var retryAttempt0 = InvokeShouldRetryToolCall(output, profile, attemptIndex: 0);
        var retryAttempt1 = InvokeShouldRetryToolCall(output, profile, attemptIndex: 1);
        var retryAttempt2 = InvokeShouldRetryToolCall(output, profile, attemptIndex: 2);

        Assert.True(retryAttempt0);
        Assert.True(retryAttempt1);
        Assert.False(retryAttempt2);
    }

    [Fact]
    public void ShouldRetryToolCall_RespectsRecoveryContractWhenTransientRetryDisabled() {
        var profile = InvokeResolveRetryProfile(
            "custom_inventory_probe",
            new ToolDefinition(
                name: "custom_inventory_probe",
                description: "Custom inventory probe",
                parameters: null,
                category: "system",
                tags: new[] { "pack:custom_system_tools" },
                recovery: new ToolRecoveryContract {
                    IsRecoveryAware = true,
                    SupportsTransientRetry = false,
                    MaxRetryAttempts = 0
                }));
        var output = new ToolOutputDto {
            CallId = "call-3j",
            Output = "{\"error\":\"Tool timed out.\"}",
            Ok = false,
            ErrorCode = "tool_timeout",
            Error = "Tool timed out."
        };

        var shouldRetry = InvokeShouldRetryToolCall(output, profile, attemptIndex: 0);

        Assert.False(shouldRetry);
    }

    [Fact]
    public void ResolveRetryProfile_ProjectsDeclaredRecoveryHelperNames() {
        var profile = InvokeResolveRetryProfile(
            "custom_inventory_probe",
            BuildRecoveryAwareDefinition(
                "custom_inventory_probe",
                maxRetryAttempts: 1,
                new[] { " system_context ", "system_context", "eventlog_context" },
                "timeout",
                "transport_unavailable"));

        Assert.Equal(new[] { "system_context", "eventlog_context" }, profile.RecoveryToolNames);
    }

    [Fact]
    public void ResolveRetryProfile_ProjectsDeclaredAlternateEngineIds() {
        var profile = InvokeResolveRetryProfile(
            "system_inventory_probe",
            BuildRecoveryAwareDefinition(
                "system_inventory_probe",
                maxRetryAttempts: 1,
                recoveryToolNames: null,
                alternateEngineIds: new[] { " cim ", "wmi", "cim" },
                retryableErrorCodes: new[] { "timeout", "transport_unavailable" }));

        Assert.Equal(new[] { "cim", "wmi" }, profile.AlternateEngineIds);
    }

    [Fact]
    public void TryBuildAlternateEngineFallbackCall_UsesExplicitEngineSelectorArgumentAndSkipsCurrentEngine() {
        var definition = BuildRecoveryAwareDefinition(
            "system_inventory_probe",
            maxRetryAttempts: 1,
            recoveryToolNames: null,
            alternateEngineIds: new[] { "cim", "wmi" },
            retryableErrorCodes: new[] { "timeout", "transport_unavailable" },
            parameters: new JsonObject()
                .Add("type", "object")
                .Add("properties", new JsonObject()
                    .Add("computer_name", new JsonObject().Add("type", "string"))
                    .Add("engine", new JsonObject().Add("type", "string"))));
        var profile = InvokeResolveRetryProfile("system_inventory_probe", definition);
        var arguments = new JsonObject()
            .Add("computer_name", "srv-01")
            .Add("engine", "cim");
        var call = new ToolCall(
            callId: "call-engine-1",
            name: definition.Name,
            input: JsonLite.Serialize(arguments),
            arguments: arguments,
            raw: new JsonObject());

        var built = ChatServiceSession.TryBuildAlternateEngineFallbackCallForTesting(
            call,
            definition,
            profile,
            out var fallbackCall,
            out var selectedEngineId);

        Assert.True(built);
        Assert.Equal("wmi", selectedEngineId);
        Assert.NotNull(fallbackCall.Arguments);
        Assert.Equal("srv-01", fallbackCall.Arguments!.GetString("computer_name"));
        Assert.Equal("wmi", fallbackCall.Arguments.GetString("engine"));
    }

    [Fact]
    public void TryBuildAlternateEngineFallbackCall_DoesNotReuseNonEngineTransportArgument() {
        var definition = BuildRecoveryAwareDefinition(
            "ad_replication_summary",
            maxRetryAttempts: 1,
            recoveryToolNames: null,
            alternateEngineIds: new[] { "rpc", "smtp" },
            retryableErrorCodes: new[] { "timeout", "transport_unavailable" },
            parameters: new JsonObject()
                .Add("type", "object")
                .Add("properties", new JsonObject()
                    .Add("transport", new JsonObject().Add("type", "string"))));
        var profile = InvokeResolveRetryProfile("ad_replication_summary", definition);
        var arguments = new JsonObject().Add("transport", "rpc");
        var call = new ToolCall(
            callId: "call-engine-2",
            name: definition.Name,
            input: JsonLite.Serialize(arguments),
            arguments: arguments,
            raw: new JsonObject());

        var built = ChatServiceSession.TryBuildAlternateEngineFallbackCallForTesting(
            call,
            definition,
            profile,
            out _,
            out _);

        Assert.False(built);
    }

    [Fact]
    public void TryBuildAlternateEngineFallbackCall_SkipsAlternateEnginesNotDeclaredBySelectorEnum() {
        var definition = BuildRecoveryAwareDefinition(
            "system_inventory_probe",
            maxRetryAttempts: 1,
            recoveryToolNames: null,
            alternateEngineIds: new[] { "cim", "wmi" },
            retryableErrorCodes: new[] { "timeout", "transport_unavailable" },
            parameters: new JsonObject()
                .Add("type", "object")
                .Add("properties", new JsonObject()
                    .Add("computer_name", new JsonObject().Add("type", "string"))
                    .Add("engine", new JsonObject()
                        .Add("type", "string")
                        .Add("enum", new JsonArray().Add("auto").Add("wmi")))));
        var profile = InvokeResolveRetryProfile("system_inventory_probe", definition);
        var arguments = new JsonObject()
            .Add("computer_name", "srv-01")
            .Add("engine", "auto");
        var call = new ToolCall(
            callId: "call-engine-3",
            name: definition.Name,
            input: JsonLite.Serialize(arguments),
            arguments: arguments,
            raw: new JsonObject());

        var built = ChatServiceSession.TryBuildAlternateEngineFallbackCallForTesting(
            call,
            definition,
            profile,
            out var fallbackCall,
            out var selectedEngineId);

        Assert.True(built);
        Assert.Equal("wmi", selectedEngineId);
        Assert.Equal("wmi", fallbackCall.Arguments!.GetString("engine"));
    }

    [Fact]
    public void TryBuildAlternateEngineFallbackCall_DoesNotBuildWhenContractEnginesAreOutsideSelectorEnum() {
        var definition = BuildRecoveryAwareDefinition(
            "system_inventory_probe",
            maxRetryAttempts: 1,
            recoveryToolNames: null,
            alternateEngineIds: new[] { "cim", "wmi" },
            retryableErrorCodes: new[] { "timeout", "transport_unavailable" },
            parameters: new JsonObject()
                .Add("type", "object")
                .Add("properties", new JsonObject()
                    .Add("computer_name", new JsonObject().Add("type", "string"))
                    .Add("engine", new JsonObject()
                        .Add("type", "string")
                        .Add("enum", new JsonArray().Add("auto").Add("native")))));
        var profile = InvokeResolveRetryProfile("system_inventory_probe", definition);
        var arguments = new JsonObject()
            .Add("computer_name", "srv-01")
            .Add("engine", "auto");
        var call = new ToolCall(
            callId: "call-engine-4",
            name: definition.Name,
            input: JsonLite.Serialize(arguments),
            arguments: arguments,
            raw: new JsonObject());

        var built = ChatServiceSession.TryBuildAlternateEngineFallbackCallForTesting(
            call,
            definition,
            profile,
            out _,
            out _);

        Assert.False(built);
    }

    [Fact]
    public void TryBuildPreferredHealthyAlternateEngineCall_PrefersHealthyEngineWhenCurrentSelectionIsAuto() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var definition = BuildRecoveryAwareDefinition(
            "system_inventory_probe",
            maxRetryAttempts: 2,
            recoveryToolNames: null,
            alternateEngineIds: new[] { "wmi", "cim" },
            retryableErrorCodes: new[] { "timeout", "transport_unavailable" },
            parameters: new JsonObject()
                .Add("type", "object")
                .Add("properties", new JsonObject()
                    .Add("computer_name", new JsonObject().Add("type", "string"))
                    .Add("engine", new JsonObject()
                        .Add("type", "string")
                        .Add("enum", new JsonArray().Add("auto").Add("wmi").Add("cim")))));
        var profile = InvokeResolveRetryProfile("system_inventory_probe", definition);
        session.RememberAlternateEngineFailureForTesting("thread-pref", definition.Name, "wmi");
        session.RememberAlternateEngineSuccessForTesting("thread-pref", definition.Name, "cim");

        var arguments = new JsonObject()
            .Add("computer_name", "srv-01")
            .Add("engine", "auto");
        var call = new ToolCall(
            callId: "call-engine-prefer-1",
            name: definition.Name,
            input: JsonLite.Serialize(arguments),
            arguments: arguments,
            raw: new JsonObject());

        var built = session.TryBuildPreferredHealthyAlternateEngineCallForTesting(
            "thread-pref",
            call,
            definition,
            profile,
            out var preferredCall,
            out var selectedEngineId);

        Assert.True(built);
        Assert.Equal("cim", selectedEngineId);
        Assert.Equal("cim", preferredCall.Arguments!.GetString("engine"));
    }

    [Fact]
    public void TryBuildPreferredHealthyAlternateEngineCall_DoesNotOverrideExplicitEngineSelection() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var definition = BuildRecoveryAwareDefinition(
            "system_inventory_probe",
            maxRetryAttempts: 2,
            recoveryToolNames: null,
            alternateEngineIds: new[] { "wmi", "cim" },
            retryableErrorCodes: new[] { "timeout", "transport_unavailable" },
            parameters: new JsonObject()
                .Add("type", "object")
                .Add("properties", new JsonObject()
                    .Add("computer_name", new JsonObject().Add("type", "string"))
                    .Add("engine", new JsonObject()
                        .Add("type", "string")
                        .Add("enum", new JsonArray().Add("auto").Add("wmi").Add("cim")))));
        var profile = InvokeResolveRetryProfile("system_inventory_probe", definition);
        session.RememberAlternateEngineSuccessForTesting("thread-explicit", definition.Name, "cim");

        var arguments = new JsonObject()
            .Add("computer_name", "srv-01")
            .Add("engine", "wmi");
        var call = new ToolCall(
            callId: "call-engine-prefer-2",
            name: definition.Name,
            input: JsonLite.Serialize(arguments),
            arguments: arguments,
            raw: new JsonObject());

        var built = session.TryBuildPreferredHealthyAlternateEngineCallForTesting(
            "thread-explicit",
            call,
            definition,
            profile,
            out _,
            out _);

        Assert.False(built);
    }

    [Fact]
    public void OrderAlternateEngineIdsByHealthForTesting_IgnoresExpiredSuccessTimestamps() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var expiredTicks = DateTime.UtcNow.AddHours(-2).Ticks;
        var freshTicks = DateTime.UtcNow.AddMinutes(-2).Ticks;

        session.RememberAlternateEngineSuccessForTesting("thread-order-success", "system_inventory_probe", "cim", expiredTicks);
        session.RememberAlternateEngineSuccessForTesting("thread-order-success", "system_inventory_probe", "wmi", freshTicks);

        var ordered = session.OrderAlternateEngineIdsByHealthForTesting(
            "thread-order-success",
            "system_inventory_probe",
            new[] { "cim", "wmi" });

        Assert.Equal(new[] { "wmi", "cim" }, ordered);
    }

    [Fact]
    public void OrderAlternateEngineIdsByHealthForTesting_IgnoresExpiredFailureTimestampsAfterRestart() {
        var pendingActionsStorePath = PendingActionsStorePathTestHelper.CreateAllowedPendingActionsStorePath("ix-chat-alt-engine-health-expired-failure", out var root);

        try {
            var expiredTicks = DateTime.UtcNow.AddHours(-2).Ticks;
            var session1 = new ChatServiceSession(
                new ServiceOptions { PendingActionsStorePath = pendingActionsStorePath },
                Stream.Null);
            session1.RememberAlternateEngineFailureForTesting(
                "thread-order-failure",
                "system_inventory_probe",
                "wmi",
                seenUtcTicks: expiredTicks);

            var session2 = new ChatServiceSession(
                new ServiceOptions { PendingActionsStorePath = pendingActionsStorePath },
                Stream.Null);
            var ordered = session2.OrderAlternateEngineIdsByHealthForTesting(
                "thread-order-failure",
                "system_inventory_probe",
                new[] { "wmi", "cim" });

            Assert.Equal(new[] { "wmi", "cim" }, ordered);
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
    public void TryBuildRecoveryHelperArgumentsForTesting_ClonesCopiedJsonValues() {
        var nestedSource = new JsonObject(StringComparer.Ordinal)
            .Add("computer_name", "srv-01");
        var sourceArguments = new JsonObject(StringComparer.Ordinal)
            .Add("computer", nestedSource);
        var failedCall = new ToolCall(
            callId: "call-recovery-clone",
            name: "system_inventory_probe",
            input: JsonLite.Serialize(sourceArguments),
            arguments: sourceArguments,
            raw: new JsonObject(StringComparer.Ordinal));
        var helperDefinition = new ToolDefinition(
            "system_context",
            description: "helper",
            parameters: new JsonObject()
                .Add("type", "object")
                .Add("properties", new JsonObject()
                    .Add("computer", new JsonObject().Add("type", "object")))
                .Add("required", new JsonArray().Add("computer")));

        var built = ChatServiceSession.TryBuildRecoveryHelperArgumentsForTesting(
            failedCall,
            helperDefinition,
            out var helperArguments);

        Assert.True(built);
        var helperComputer = Assert.IsType<JsonObject>(helperArguments.GetObject("computer"));
        Assert.NotSame(nestedSource, helperComputer);
        Assert.Equal("srv-01", helperComputer.GetString("computer_name"));

        nestedSource.Add("computer_name", "srv-02");

        Assert.Equal("srv-01", helperComputer.GetString("computer_name"));
    }

    [Fact]
    public void ShouldAttemptRecoveryHelperTools_AttemptsForStructuredTransportFailureWhenHelpersDeclared() {
        var profile = InvokeResolveRetryProfile(
            "custom_inventory_probe",
            BuildRecoveryAwareDefinition(
                "custom_inventory_probe",
                maxRetryAttempts: 1,
                new[] { "system_context" },
                "timeout",
                "transport_unavailable"));
        var output = new ToolOutputDto {
            CallId = "call-recovery-1",
            Output = "{\"error\":\"Service temporarily unavailable.\"}",
            Ok = false,
            ErrorCode = "transport_unavailable",
            Error = "Service temporarily unavailable.",
            IsTransient = true
        };

        var shouldAttempt = ChatServiceSession.ShouldAttemptRecoveryHelperToolsForTesting(output, profile);

        Assert.True(shouldAttempt);
    }

    [Fact]
    public void ShouldAttemptRecoveryHelperTools_DoesNotAttemptForPermanentFailure() {
        var profile = InvokeResolveRetryProfile(
            "custom_inventory_probe",
            BuildRecoveryAwareDefinition(
                "custom_inventory_probe",
                maxRetryAttempts: 1,
                new[] { "system_context" },
                "timeout",
                "transport_unavailable"));
        var output = new ToolOutputDto {
            CallId = "call-recovery-2",
            Output = "{\"error\":\"Unauthorized.\"}",
            Ok = false,
            ErrorCode = "permission_denied",
            Error = "Unauthorized.",
            IsTransient = true
        };

        var shouldAttempt = ChatServiceSession.ShouldAttemptRecoveryHelperToolsForTesting(output, profile);

        Assert.False(shouldAttempt);
    }

    [Fact]
    public void ShouldRetryToolCall_DoesNotRetryDomainScopeGuardrailFailure() {
        var profile = InvokeResolveRetryProfile(
            "ad_replication_summary",
            BuildRecoveryAwareDefinition("ad_replication_summary", maxRetryAttempts: 1, "timeout", "transport_unavailable"));
        var output = new ToolOutputDto {
            CallId = "call-3b",
            Output = "{\"error_code\":\"domain_scope_host_guardrail\",\"error\":\"Blocked host target.\"}",
            Ok = false,
            ErrorCode = "domain_scope_host_guardrail",
            Error = "Blocked host target.",
            IsTransient = true
        };

        var shouldRetry = InvokeShouldRetryToolCall(output, profile, attemptIndex: 0);

        Assert.False(shouldRetry);
    }

    [Fact]
    public void IsProjectionViewArgumentFailure_DetectsUnsupportedProjectionColumns() {
        var output = new ToolOutputDto {
            CallId = "call-4",
            Output = "{\"ok\":false,\"error_code\":\"invalid_argument\",\"error\":\"columns contains unsupported value 'bad_column'.\"}",
            Ok = false,
            ErrorCode = "invalid_argument",
            Error = "columns contains unsupported value 'bad_column'."
        };

        var detected = InvokeIsProjectionViewArgumentFailure(output);

        Assert.True(detected);
    }

    [Fact]
    public void IsProjectionViewArgumentFailure_DoesNotMatchUnrelatedPermanentErrors() {
        var output = new ToolOutputDto {
            CallId = "call-5",
            Output = "{\"ok\":false,\"error_code\":\"tool_exception\",\"error\":\"Access denied.\"}",
            Ok = false,
            ErrorCode = "tool_exception",
            Error = "Access denied."
        };

        var detected = InvokeIsProjectionViewArgumentFailure(output);

        Assert.False(detected);
    }

    [Fact]
    public void TryBuildProjectionArgsFallbackCall_StripsProjectionFormattingArgumentsAndPreservesTopByDefault() {
        var call = new ToolCall(
            callId: "call-6",
            name: "eventlog_top_events",
            input: null,
            arguments: new JsonObject()
                .Add("log_name", "System")
                .Add("columns", new JsonArray().Add("event_id"))
                .Add("sort_by", "event_id")
                .Add("sort_direction", "desc")
                .Add("top", 5),
            raw: new JsonObject());
        var output = new ToolOutputDto {
            CallId = call.CallId,
            Output = "{\"ok\":false,\"error_code\":\"invalid_argument\",\"error\":\"sort_by must be one of: event_id, level.\"}",
            Ok = false,
            ErrorCode = "invalid_argument",
            Error = "sort_by must be one of: event_id, level."
        };

        var args = new object?[] { call, output, null, null };
        var built = TryBuildProjectionArgsFallbackCallMethod.Invoke(null, args);
        var fallbackCall = Assert.IsType<ToolCall>(args[2]);

        Assert.True(Assert.IsType<bool>(built));
        Assert.NotNull(args[3]);
        Assert.NotNull(fallbackCall.Arguments);
        Assert.Equal("System", fallbackCall.Arguments!.GetString("log_name"));
        Assert.False(fallbackCall.Arguments.TryGetValue("columns", out _));
        Assert.False(fallbackCall.Arguments.TryGetValue("sort_by", out _));
        Assert.False(fallbackCall.Arguments.TryGetValue("sort_direction", out _));
        Assert.Equal(5, fallbackCall.Arguments.GetInt64("top"));
    }

    [Fact]
    public void TryBuildProjectionArgsFallbackCall_ParsesInputArgumentsWhenStructuredArgumentsMissing() {
        var call = new ToolCall(
            callId: "call-6b",
            name: "ad_search",
            input: """
                   {"query":"przemyslaw.klys","log_name":"System","columns":["objectSid"],"sort_by":"sAMAccountName","sort_direction":"asc","top":10}
                   """,
            arguments: null,
            raw: new JsonObject());
        var output = new ToolOutputDto {
            CallId = call.CallId,
            Output = "{\"ok\":false,\"error_code\":\"invalid_argument\",\"error\":\"columns contains unsupported value 'objectSid'.\"}",
            Ok = false,
            ErrorCode = "invalid_argument",
            Error = "columns contains unsupported value 'objectSid'."
        };

        var args = new object?[] { call, output, null, null };
        var built = TryBuildProjectionArgsFallbackCallMethod.Invoke(null, args);
        var fallbackCall = Assert.IsType<ToolCall>(args[2]);

        Assert.True(Assert.IsType<bool>(built));
        Assert.NotNull(fallbackCall.Arguments);
        Assert.Equal("przemyslaw.klys", fallbackCall.Arguments!.GetString("query"));
        Assert.Equal("System", fallbackCall.Arguments.GetString("log_name"));
        Assert.False(fallbackCall.Arguments.TryGetValue("columns", out _));
        Assert.False(fallbackCall.Arguments.TryGetValue("sort_by", out _));
        Assert.False(fallbackCall.Arguments.TryGetValue("sort_direction", out _));
        Assert.Equal(10, fallbackCall.Arguments.GetInt64("top"));
    }

    [Fact]
    public void TryBuildProjectionArgsFallbackCall_DropsTopAsLastResortForProjectionEnvelopeFailures() {
        var call = new ToolCall(
            callId: "call-7",
            name: "eventlog_top_events",
            input: null,
            arguments: new JsonObject()
                .Add("log_name", "System")
                .Add("top", 5),
            raw: new JsonObject());
        var output = new ToolOutputDto {
            CallId = call.CallId,
            Output = "{\"ok\":false,\"error_code\":\"invalid_argument\",\"error\":\"Failed to build table view response envelope.\"}",
            Ok = false,
            ErrorCode = "invalid_argument",
            Error = "Failed to build table view response envelope."
        };

        var args = new object?[] { call, output, null, null };
        var built = TryBuildProjectionArgsFallbackCallMethod.Invoke(null, args);
        var fallbackCall = Assert.IsType<ToolCall>(args[2]);

        Assert.True(Assert.IsType<bool>(built));
        Assert.NotNull(fallbackCall.Arguments);
        Assert.False(fallbackCall.Arguments!.TryGetValue("top", out _));
    }

    [Fact]
    public void TryBuildProjectionArgsFallbackCall_DropsTopOnExplicitTopValidationFailure() {
        var call = new ToolCall(
            callId: "call-8",
            name: "eventlog_top_events",
            input: null,
            arguments: new JsonObject()
                .Add("log_name", "System")
                .Add("top", 5000),
            raw: new JsonObject());
        var output = new ToolOutputDto {
            CallId = call.CallId,
            Output = "{\"ok\":false,\"error_code\":\"invalid_argument\",\"error\":\"top must be between 1 and 100 for table view response envelope.\"}",
            Ok = false,
            ErrorCode = "invalid_argument",
            Error = "top must be between 1 and 100 for table view response envelope."
        };

        var args = new object?[] { call, output, null, null };
        var built = TryBuildProjectionArgsFallbackCallMethod.Invoke(null, args);
        var fallbackCall = Assert.IsType<ToolCall>(args[2]);

        Assert.True(Assert.IsType<bool>(built));
        Assert.NotNull(fallbackCall.Arguments);
        Assert.Equal("System", fallbackCall.Arguments!.GetString("log_name"));
        Assert.False(fallbackCall.Arguments.TryGetValue("top", out _));
    }

    [Fact]
    public void TryBuildProjectionArgsFallbackCall_DoesNotDropTopOnIncidentalTopText() {
        var call = new ToolCall(
            callId: "call-8b",
            name: "eventlog_top_events",
            input: null,
            arguments: new JsonObject()
                .Add("log_name", "System")
                .Add("top", 5),
            raw: new JsonObject());
        var output = new ToolOutputDto {
            CallId = call.CallId,
            Output = "{\"ok\":false,\"error_code\":\"invalid_argument\",\"error\":\"Failed to query top events; invalid filter expression.\"}",
            Ok = false,
            ErrorCode = "invalid_argument",
            Error = "Failed to query top events; invalid filter expression."
        };

        var args = new object?[] { call, output, null, null };
        var built = TryBuildProjectionArgsFallbackCallMethod.Invoke(null, args);
        var fallbackCall = Assert.IsType<ToolCall>(args[2]);

        Assert.False(Assert.IsType<bool>(built));
        Assert.NotNull(fallbackCall.Arguments);
        Assert.Equal(5, fallbackCall.Arguments!.GetInt64("top"));
    }

    /// <summary>
    /// Ensures projection fallback keeps valid sort/top intent while pruning only unsupported columns.
    /// </summary>
    [Fact]
    public void TryBuildProjectionArgsFallbackCall_SelectivelyPrunesUnsupportedColumnsUsingMetadata() {
        var call = new ToolCall(
            callId: "call-9",
            name: "eventlog_top_events",
            input: null,
            arguments: new JsonObject()
                .Add("log_name", "System")
                .Add("columns", new JsonArray().Add("event_id").Add("unknown_column"))
                .Add("sort_by", "event_id")
                .Add("sort_direction", "desc")
                .Add("top", 5),
            raw: new JsonObject());
        var output = new ToolOutputDto {
            CallId = call.CallId,
            Output = """
                     {
                       "ok": false,
                       "error_code": "invalid_argument",
                       "error": "columns contains unsupported value 'unknown_column'.",
                       "meta": {
                         "available_columns": ["event_id", "level"]
                       }
                     }
                     """,
            Ok = false,
            ErrorCode = "invalid_argument",
            Error = "columns contains unsupported value 'unknown_column'."
        };

        var args = new object?[] { call, output, null, null };
        var built = TryBuildProjectionArgsFallbackCallMethod.Invoke(null, args);
        var fallbackCall = Assert.IsType<ToolCall>(args[2]);

        Assert.True(Assert.IsType<bool>(built));
        Assert.NotNull(fallbackCall.Arguments);
        Assert.Equal("System", fallbackCall.Arguments!.GetString("log_name"));
        var columns = fallbackCall.Arguments.GetArray("columns");
        Assert.NotNull(columns);
        Assert.Single(columns!);
        Assert.Equal("event_id", columns[0].AsString());
        Assert.Equal("event_id", fallbackCall.Arguments.GetString("sort_by"));
        Assert.Equal("desc", fallbackCall.Arguments.GetString("sort_direction"));
        Assert.Equal(5, fallbackCall.Arguments.GetInt64("top"));
    }

    /// <summary>
    /// Ensures selective projection pruning still works when available columns are surfaced under failure.meta in camelCase.
    /// </summary>
    [Fact]
    public void TryBuildProjectionArgsFallbackCall_SelectivelyPrunesUsingFailureMetaCamelCaseColumns() {
        var call = new ToolCall(
            callId: "call-9b",
            name: "eventlog_top_events",
            input: null,
            arguments: new JsonObject()
                .Add("log_name", "System")
                .Add("columns", new JsonArray().Add("event_id").Add("unknown_column"))
                .Add("sort_by", "event_id")
                .Add("sort_direction", "desc")
                .Add("top", 5),
            raw: new JsonObject());
        var output = new ToolOutputDto {
            CallId = call.CallId,
            Output = """
                     {
                       "ok": false,
                       "error_code": "invalid_argument",
                       "error": "columns contains unsupported value 'unknown_column'.",
                       "failure": {
                         "meta": {
                           "availableColumns": ["event_id", "level"]
                         }
                       }
                     }
                     """,
            Ok = false,
            ErrorCode = "invalid_argument",
            Error = "columns contains unsupported value 'unknown_column'."
        };

        var args = new object?[] { call, output, null, null };
        var built = TryBuildProjectionArgsFallbackCallMethod.Invoke(null, args);
        var fallbackCall = Assert.IsType<ToolCall>(args[2]);

        Assert.True(Assert.IsType<bool>(built));
        Assert.NotNull(fallbackCall.Arguments);
        var columns = fallbackCall.Arguments!.GetArray("columns");
        Assert.NotNull(columns);
        Assert.Single(columns!);
        Assert.Equal("event_id", columns[0].AsString());
        Assert.Equal("event_id", fallbackCall.Arguments.GetString("sort_by"));
        Assert.Equal("desc", fallbackCall.Arguments.GetString("sort_direction"));
        Assert.Equal(5, fallbackCall.Arguments.GetInt64("top"));
    }

    /// <summary>
    /// Ensures we still apply conservative projection reset when metadata does not identify a specific bad argument.
    /// </summary>
    [Fact]
    public void TryBuildProjectionArgsFallbackCall_FallsBackConservativelyWhenMetadataShowsNoSpecificInvalidProjectionArgs() {
        var call = new ToolCall(
            callId: "call-10",
            name: "eventlog_top_events",
            input: null,
            arguments: new JsonObject()
                .Add("log_name", "System")
                .Add("columns", new JsonArray().Add("event_id"))
                .Add("sort_by", "event_id")
                .Add("sort_direction", "desc")
                .Add("top", 5),
            raw: new JsonObject());
        var output = new ToolOutputDto {
            CallId = call.CallId,
            Output = """
                     {
                       "ok": false,
                       "error_code": "invalid_argument",
                       "error": "Failed to build table view response envelope.",
                       "meta": {
                         "available_columns": ["event_id", "level"]
                       }
                     }
                     """,
            Ok = false,
            ErrorCode = "invalid_argument",
            Error = "Failed to build table view response envelope."
        };

        var args = new object?[] { call, output, null, null };
        var built = TryBuildProjectionArgsFallbackCallMethod.Invoke(null, args);
        var fallbackCall = Assert.IsType<ToolCall>(args[2]);

        Assert.True(Assert.IsType<bool>(built));
        Assert.NotNull(fallbackCall.Arguments);
        Assert.False(fallbackCall.Arguments!.TryGetValue("columns", out _));
        Assert.False(fallbackCall.Arguments.TryGetValue("sort_by", out _));
        Assert.False(fallbackCall.Arguments.TryGetValue("sort_direction", out _));
        Assert.Equal(5, fallbackCall.Arguments.GetInt64("top"));
    }

    private static ChatServiceSession.RetryProfileSnapshot InvokeResolveRetryProfile(string toolName) {
        _ = toolName;
        return ChatServiceSession.ResolveRetryProfileForTesting(definition: null);
    }

    private static ChatServiceSession.RetryProfileSnapshot InvokeResolveRetryProfile(string toolName, ToolDefinition definition) {
        _ = toolName;
        return ChatServiceSession.ResolveRetryProfileForTesting(definition);
    }

    private static ToolDefinition BuildRecoveryAwareDefinition(string toolName, int maxRetryAttempts, params string[] retryableErrorCodes) {
        return BuildRecoveryAwareDefinition(toolName, maxRetryAttempts, recoveryToolNames: null, alternateEngineIds: null, retryableErrorCodes: retryableErrorCodes);
    }

    private static ToolDefinition BuildRecoveryAwareDefinition(
        string toolName,
        int maxRetryAttempts,
        string[]? recoveryToolNames,
        params string[] retryableErrorCodes) {
        return BuildRecoveryAwareDefinition(
            toolName,
            maxRetryAttempts,
            recoveryToolNames,
            alternateEngineIds: null,
            retryableErrorCodes: retryableErrorCodes);
    }

    private static ToolDefinition BuildRecoveryAwareDefinition(
        string toolName,
        int maxRetryAttempts,
        string[]? recoveryToolNames,
        string[]? alternateEngineIds,
        string[] retryableErrorCodes,
        JsonObject? parameters = null) {
        return new ToolDefinition(
            name: toolName,
            description: "Contract-driven retry policy test definition.",
            parameters: parameters,
            recovery: new ToolRecoveryContract {
                IsRecoveryAware = true,
                SupportsTransientRetry = true,
                MaxRetryAttempts = maxRetryAttempts,
                RetryableErrorCodes = retryableErrorCodes,
                RecoveryToolNames = recoveryToolNames ?? Array.Empty<string>(),
                SupportsAlternateEngines = alternateEngineIds is { Length: > 0 },
                AlternateEngineIds = alternateEngineIds ?? Array.Empty<string>()
            });
    }

    private static bool InvokeShouldRetryToolCall(ToolOutputDto output, ChatServiceSession.RetryProfileSnapshot profile, int attemptIndex) {
        return ChatServiceSession.ShouldRetryToolCallForTesting(output, profile, attemptIndex);
    }

    private static bool InvokeIsProjectionViewArgumentFailure(ToolOutputDto output) {
        var result = IsProjectionViewArgumentFailureMethod.Invoke(null, new object?[] { output });
        return Assert.IsType<bool>(result);
    }
}
