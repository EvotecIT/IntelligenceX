using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Service;
using IntelligenceX.Json;
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.Tools;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class ChatServiceRoutingTrimTests {
    private static readonly MethodInfo BuildHostPackPreflightCallsMethod =
        typeof(ChatServiceSession).GetMethod("BuildHostPackPreflightCalls", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("BuildHostPackPreflightCalls not found.");

    [Fact]
    public void BuildHostPackPreflightCalls_UsesContractRolesNotNameSuffixes() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var registry = new ToolRegistry();
        registry.Register(new PreflightStubTool(
            "customx_pack_probe",
            CreateRoutingContract("active_directory", ToolRoutingTaxonomy.RolePackInfo)));
        registry.Register(new PreflightStubTool(
            "customx_discover_scope",
            CreateRoutingContract("active_directory", ToolRoutingTaxonomy.RoleEnvironmentDiscover)));
        registry.Register(new PreflightStubTool(
            "customx_health_scan",
            CreateRoutingContract("active_directory", ToolRoutingTaxonomy.RoleDiagnostic)));
        SetSessionRegistry(session, registry);

        var extractedCalls = new List<ToolCall> {
            new("call_operational_1", "customx_health_scan", "{}", new JsonObject(StringComparer.Ordinal), new JsonObject(StringComparer.Ordinal))
        };

        var result = BuildHostPackPreflightCallsMethod.Invoke(session, new object?[] { "thread-1", registry.GetDefinitions(), extractedCalls });
        var preflightCalls = Assert.IsAssignableFrom<IReadOnlyList<ToolCall>>(result);

        Assert.Equal(2, preflightCalls.Count);
        Assert.Equal("customx_pack_probe", preflightCalls[0].Name);
        Assert.Equal("customx_discover_scope", preflightCalls[1].Name);
    }

    [Fact]
    public void BuildHostPackPreflightCalls_SkipsEnvironmentDiscoverWhenRequiredArgumentsExist() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var registry = new ToolRegistry();
        registry.Register(new PreflightStubTool(
            "customx_pack_probe",
            CreateRoutingContract("active_directory", ToolRoutingTaxonomy.RolePackInfo)));
        registry.Register(new PreflightStubTool(
            "customx_discover_scope",
            CreateRoutingContract("active_directory", ToolRoutingTaxonomy.RoleEnvironmentDiscover),
            parameters: CreateRequiredSchema("domain_name")));
        registry.Register(new PreflightStubTool(
            "customx_health_scan",
            CreateRoutingContract("active_directory", ToolRoutingTaxonomy.RoleOperational)));
        SetSessionRegistry(session, registry);

        var extractedCalls = new List<ToolCall> {
            new("call_operational_2", "customx_health_scan", "{}", new JsonObject(StringComparer.Ordinal), new JsonObject(StringComparer.Ordinal))
        };

        var result = BuildHostPackPreflightCallsMethod.Invoke(session, new object?[] { "thread-2", registry.GetDefinitions(), extractedCalls });
        var preflightCalls = Assert.IsAssignableFrom<IReadOnlyList<ToolCall>>(result);

        Assert.Single(preflightCalls);
        Assert.Equal("customx_pack_probe", preflightCalls[0].Name);
    }

    [Fact]
    public void BuildHostPackPreflightCalls_UsesProviderSafeCallIds() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var registry = new ToolRegistry();
        registry.Register(new PreflightStubTool(
            "customx_pack_probe",
            CreateRoutingContract("active_directory", ToolRoutingTaxonomy.RolePackInfo)));
        registry.Register(new PreflightStubTool(
            "customx_discover_scope",
            CreateRoutingContract("active_directory", ToolRoutingTaxonomy.RoleEnvironmentDiscover)));
        registry.Register(new PreflightStubTool(
            "customx_health_scan",
            CreateRoutingContract("active_directory", ToolRoutingTaxonomy.RoleOperational)));
        SetSessionRegistry(session, registry);

        var extractedCalls = new List<ToolCall> {
            new("call_operational_3", "customx_health_scan", "{}", new JsonObject(StringComparer.Ordinal), new JsonObject(StringComparer.Ordinal))
        };

        var result = BuildHostPackPreflightCallsMethod.Invoke(session, new object?[] { "thread-3", registry.GetDefinitions(), extractedCalls });
        var preflightCalls = Assert.IsAssignableFrom<IReadOnlyList<ToolCall>>(result);

        Assert.NotEmpty(preflightCalls);
        foreach (var preflightCall in preflightCalls) {
            Assert.True(preflightCall.CallId.Length <= 64, $"Expected provider-safe call_id length, observed {preflightCall.CallId.Length}: {preflightCall.CallId}");
        }
    }

    [Fact]
    public void BuildHostPackPreflightCalls_UsesDeclaredRecoveryHelperTools() {
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
            new("call_operational_4", "customx_health_scan", "{}", new JsonObject(StringComparer.Ordinal), new JsonObject(StringComparer.Ordinal))
        };

        var result = BuildHostPackPreflightCallsMethod.Invoke(session, new object?[] { "thread-4", registry.GetDefinitions(), extractedCalls });
        var preflightCalls = Assert.IsAssignableFrom<IReadOnlyList<ToolCall>>(result);

        Assert.Equal(2, preflightCalls.Count);
        Assert.Equal("customx_pack_probe", preflightCalls[0].Name);
        Assert.Equal("customx_recovery_discover", preflightCalls[1].Name);
    }

    [Fact]
    public void BuildHostPackPreflightCalls_DoesNotDuplicateExplicitRecoveryHelperCallsInSameRound() {
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
            new("call_operational_4", "customx_health_scan", "{}", new JsonObject(StringComparer.Ordinal), new JsonObject(StringComparer.Ordinal)),
            new("call_explicit_helper_4", "customx_recovery_discover", "{}", new JsonObject(StringComparer.Ordinal), new JsonObject(StringComparer.Ordinal))
        };

        var result = BuildHostPackPreflightCallsMethod.Invoke(session, new object?[] { "thread-4b", registry.GetDefinitions(), extractedCalls });
        var preflightCalls = Assert.IsAssignableFrom<IReadOnlyList<ToolCall>>(result);

        var preflightCall = Assert.Single(preflightCalls);
        Assert.Equal("customx_pack_probe", preflightCall.Name);
    }

    [Fact]
    public void BuildHostPackPreflightCalls_PrefersHealthyAlternatePackRoleCandidateWhenDefaultPathIsSuppressed() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        session.RememberHostBootstrapFailureForTesting("thread-5", "customx_pack_probe_remote", "pack_preflight");
        session.SetToolRoutingStatsForTesting(new Dictionary<string, (long LastUsedUtcTicks, long LastSuccessUtcTicks)>(StringComparer.OrdinalIgnoreCase) {
            ["customx_pack_probe_local"] = (DateTime.UtcNow.Ticks, DateTime.UtcNow.Ticks)
        });

        var registry = new ToolRegistry();
        registry.Register(new PreflightStubTool(
            "customx_pack_probe_remote",
            CreateRoutingContract("active_directory", ToolRoutingTaxonomy.RolePackInfo)));
        registry.Register(new PreflightStubTool(
            "customx_pack_probe_local",
            CreateRoutingContract("active_directory", ToolRoutingTaxonomy.RolePackInfo)));
        registry.Register(new PreflightStubTool(
            "customx_discover_scope",
            CreateRoutingContract("active_directory", ToolRoutingTaxonomy.RoleEnvironmentDiscover)));
        registry.Register(new PreflightStubTool(
            "customx_health_scan",
            CreateRoutingContract("active_directory", ToolRoutingTaxonomy.RoleOperational)));
        SetSessionRegistry(session, registry);

        var extractedCalls = new List<ToolCall> {
            new("call_operational_5", "customx_health_scan", "{}", new JsonObject(StringComparer.Ordinal), new JsonObject(StringComparer.Ordinal))
        };

        var result = BuildHostPackPreflightCallsMethod.Invoke(session, new object?[] { "thread-5", registry.GetDefinitions(), extractedCalls });
        var preflightCalls = Assert.IsAssignableFrom<IReadOnlyList<ToolCall>>(result);

        Assert.Equal(2, preflightCalls.Count);
        Assert.Equal("customx_pack_probe_local", preflightCalls[0].Name);
        Assert.Equal("customx_discover_scope", preflightCalls[1].Name);
    }

    private static ToolRoutingContract CreateRoutingContract(string packId, string role) {
        return new ToolRoutingContract {
            IsRoutingAware = true,
            RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
            PackId = packId,
            Role = role
        };
    }

    private static JsonObject CreateRequiredSchema(string requiredPropertyName) {
        return new JsonObject(StringComparer.Ordinal)
            .Add("type", "object")
            .Add("properties", new JsonObject(StringComparer.Ordinal)
                .Add(requiredPropertyName, new JsonObject(StringComparer.Ordinal).Add("type", "string")))
            .Add("required", new JsonArray().Add(requiredPropertyName));
    }

    private sealed class PreflightStubTool : ITool {
        private readonly Func<JsonObject?, CancellationToken, Task<string>> _invoke;

        public PreflightStubTool(string name, ToolRoutingContract routing, JsonObject? parameters = null, ToolRecoveryContract? recovery = null) {
            Definition = new ToolDefinition(name, description: "preflight stub", parameters: parameters, routing: routing, recovery: recovery);
            _invoke = static (_, _) => Task.FromResult("""{"ok":true}""");
        }

        public ToolDefinition Definition { get; }

        public Task<string> InvokeAsync(JsonObject? arguments, CancellationToken cancellationToken) {
            return _invoke(arguments, cancellationToken);
        }
    }
}
