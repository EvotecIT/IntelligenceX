using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Service;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Json;
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
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
    public void BuildHostPackPreflightCalls_UsesExplicitPreflightFlagsFromOrchestrationCatalog() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var registry = new ToolRegistry();
        registry.Register(new PreflightStubTool(
            "customx_pack_probe",
            CreateRoutingContract("active_directory", ToolRoutingTaxonomy.RoleOperational)));
        registry.Register(new PreflightStubTool(
            "customx_discover_scope",
            CreateRoutingContract("active_directory", ToolRoutingTaxonomy.RoleOperational)));
        registry.Register(new PreflightStubTool(
            "customx_health_scan",
            CreateRoutingContract("active_directory", ToolRoutingTaxonomy.RoleOperational)));
        SetSessionRegistry(session, registry);
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(
            registry.GetDefinitions(),
            new IToolPack[] { new ExplicitPreflightCatalogOverlayPack() }));

        var extractedCalls = new List<ToolCall> {
            new("call_operational_flags_1", "customx_health_scan", "{}", new JsonObject(StringComparer.Ordinal), new JsonObject(StringComparer.Ordinal))
        };

        var result = BuildHostPackPreflightCallsMethod.Invoke(session, new object?[] { "thread-flags-1", registry.GetDefinitions(), extractedCalls });
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
    public void BuildHostPackPreflightCalls_UsesDeclaredSetupAndProbeHelperTools() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var registry = new ToolRegistry();
        registry.Register(new PreflightStubTool(
            "customx_pack_probe",
            CreateRoutingContract("eventlog", ToolRoutingTaxonomy.RolePackInfo)));
        registry.Register(new PreflightStubTool(
            "customx_live_query",
            CreateRoutingContract("eventlog", ToolRoutingTaxonomy.RoleOperational),
            parameters: CreateRequiredSchema("profile_id", "auth_probe_id"),
            authentication: new ToolAuthenticationContract {
                IsAuthenticationAware = true,
                RequiresAuthentication = true,
                Mode = ToolAuthenticationMode.ProfileReference,
                ProfileIdArgumentName = "profile_id",
                SupportsConnectivityProbe = true,
                ProbeToolName = "customx_connectivity_probe"
            },
            setup: new ToolSetupContract {
                IsSetupAware = true,
                SetupToolName = "customx_runtime_profile_validate"
            }));
        registry.Register(new PreflightStubTool(
            "customx_runtime_profile_validate",
            CreateRoutingContract("eventlog", ToolRoutingTaxonomy.RoleDiagnostic)));
        registry.Register(new PreflightStubTool(
            "customx_connectivity_probe",
            CreateRoutingContract("eventlog", ToolRoutingTaxonomy.RoleDiagnostic)));
        SetSessionRegistry(session, registry);
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(registry.GetDefinitions()));

        var extractedCalls = new List<ToolCall> {
            new("call_operational_4setup", "customx_live_query", "{}", new JsonObject(StringComparer.Ordinal), new JsonObject(StringComparer.Ordinal))
        };

        var result = BuildHostPackPreflightCallsMethod.Invoke(session, new object?[] { "thread-4setup", registry.GetDefinitions(), extractedCalls });
        var preflightCalls = Assert.IsAssignableFrom<IReadOnlyList<ToolCall>>(result);

        Assert.Equal(3, preflightCalls.Count);
        Assert.Equal("customx_pack_probe", preflightCalls[0].Name);
        Assert.Contains(preflightCalls.Skip(1), call => string.Equals(call.Name, "customx_runtime_profile_validate", StringComparison.Ordinal));
        Assert.Contains(preflightCalls.Skip(1), call => string.Equals(call.Name, "customx_connectivity_probe", StringComparison.Ordinal));
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
    public void BuildHostPackPreflightCalls_DoesNotDuplicateExplicitSetupAndProbeHelperCallsInSameRound() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var registry = new ToolRegistry();
        registry.Register(new PreflightStubTool(
            "customx_pack_probe",
            CreateRoutingContract("eventlog", ToolRoutingTaxonomy.RolePackInfo)));
        registry.Register(new PreflightStubTool(
            "customx_live_query",
            CreateRoutingContract("eventlog", ToolRoutingTaxonomy.RoleOperational),
            parameters: CreateRequiredSchema("profile_id", "auth_probe_id"),
            authentication: new ToolAuthenticationContract {
                IsAuthenticationAware = true,
                RequiresAuthentication = true,
                Mode = ToolAuthenticationMode.ProfileReference,
                ProfileIdArgumentName = "profile_id",
                SupportsConnectivityProbe = true,
                ProbeToolName = "customx_connectivity_probe"
            },
            setup: new ToolSetupContract {
                IsSetupAware = true,
                SetupToolName = "customx_runtime_profile_validate"
            }));
        registry.Register(new PreflightStubTool(
            "customx_runtime_profile_validate",
            CreateRoutingContract("eventlog", ToolRoutingTaxonomy.RoleDiagnostic)));
        registry.Register(new PreflightStubTool(
            "customx_connectivity_probe",
            CreateRoutingContract("eventlog", ToolRoutingTaxonomy.RoleDiagnostic)));
        SetSessionRegistry(session, registry);
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(registry.GetDefinitions()));

        var extractedCalls = new List<ToolCall> {
            new("call_operational_4helper", "customx_live_query", "{}", new JsonObject(StringComparer.Ordinal), new JsonObject(StringComparer.Ordinal)),
            new("call_setup_4helper", "customx_runtime_profile_validate", "{}", new JsonObject(StringComparer.Ordinal), new JsonObject(StringComparer.Ordinal)),
            new("call_probe_4helper", "customx_connectivity_probe", "{}", new JsonObject(StringComparer.Ordinal), new JsonObject(StringComparer.Ordinal))
        };

        var result = BuildHostPackPreflightCallsMethod.Invoke(session, new object?[] { "thread-4helper", registry.GetDefinitions(), extractedCalls });
        var preflightCalls = Assert.IsAssignableFrom<IReadOnlyList<ToolCall>>(result);

        var preflightCall = Assert.Single(preflightCalls);
        Assert.Equal("customx_pack_probe", preflightCall.Name);
    }

    [Fact]
    public void BuildHostPackPreflightCalls_UsesPackOwnedPreferredProbeHelperWithoutExplicitContractProbe() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var registry = new ToolRegistry();
        registry.Register(new PreflightStubTool(
            "customx_pack_probe",
            CreateRoutingContract("eventlog", ToolRoutingTaxonomy.RolePackInfo)));
        registry.Register(new PreflightStubTool(
            "customx_live_query",
            CreateRoutingContract("eventlog", ToolRoutingTaxonomy.RoleOperational)));
        registry.Register(new PreflightStubTool(
            "customx_connectivity_probe",
            CreateRoutingContract("eventlog", ToolRoutingTaxonomy.RoleDiagnostic)));
        SetSessionRegistry(session, registry);
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(
            registry.GetDefinitions(),
            new IToolPack[] { new PreferredProbeGuidancePack() }));

        var extractedCalls = new List<ToolCall> {
            new(
                "call_operational_preferred_probe",
                "customx_live_query",
                "{}",
                new JsonObject(StringComparer.Ordinal),
                new JsonObject(StringComparer.Ordinal))
        };

        var result = BuildHostPackPreflightCallsMethod.Invoke(session, new object?[] { "thread-preferred-probe", registry.GetDefinitions(), extractedCalls });
        var preflightCalls = Assert.IsAssignableFrom<IReadOnlyList<ToolCall>>(result);

        Assert.Equal(2, preflightCalls.Count);
        Assert.Equal("customx_pack_probe", preflightCalls[0].Name);
        Assert.Equal("customx_connectivity_probe", preflightCalls[1].Name);
    }

    [Fact]
    public void BuildHostPackPreflightCalls_AcceptsRecipeHelperWhenSourceArgumentShapeDiffersOnlyByCase() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var registry = new ToolRegistry();
        registry.Register(new PreflightStubTool(
            "customx_pack_probe",
            CreateRoutingContract("eventlog", ToolRoutingTaxonomy.RolePackInfo)));
        registry.Register(new PreflightStubTool(
            "customx_live_query",
            CreateRoutingContract("eventlog", ToolRoutingTaxonomy.RoleOperational),
            parameters: ToolSchema.Object(("Machine_Name", ToolSchema.String("Remote machine."))).NoAdditionalProperties()));
        registry.Register(new PreflightStubTool(
            "customx_recipe_resolver",
            CreateRoutingContract("eventlog", ToolRoutingTaxonomy.RoleResolver),
            parameters: ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties()));
        SetSessionRegistry(session, registry);
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(
            registry.GetDefinitions(),
            new IToolPack[] { new RecipeOverlapGuidancePack() }));

        var extractedCalls = new List<ToolCall> {
            new(
                "call_operational_recipe_case",
                "customx_live_query",
                "{}",
                new JsonObject(StringComparer.Ordinal),
                new JsonObject(StringComparer.Ordinal))
        };

        var result = BuildHostPackPreflightCallsMethod.Invoke(session, new object?[] { "thread-recipe-case", registry.GetDefinitions(), extractedCalls });
        var preflightCalls = Assert.IsAssignableFrom<IReadOnlyList<ToolCall>>(result);

        Assert.Equal(2, preflightCalls.Count);
        Assert.Equal("customx_pack_probe", preflightCalls[0].Name);
        Assert.Equal("customx_recipe_resolver", preflightCalls[1].Name);
    }

    [Fact]
    public void BuildHostPackPreflightCalls_SkipsRecipeOverlapHelperWhenContractShapeDiffers() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var registry = new ToolRegistry();
        registry.Register(new PreflightStubTool(
            "customx_pack_probe",
            CreateRoutingContract("eventlog", ToolRoutingTaxonomy.RolePackInfo)));
        registry.Register(new PreflightStubTool(
            "customx_live_query",
            CreateRoutingContract("eventlog", ToolRoutingTaxonomy.RoleOperational),
            parameters: ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties()));
        registry.Register(new PreflightStubTool(
            "customx_recipe_resolver",
            CreateRoutingContract("eventlog", ToolRoutingTaxonomy.RoleResolver),
            parameters: ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties()));
        registry.Register(new PreflightStubTool(
            "customx_directory_probe",
            CreateRoutingContract("eventlog", ToolRoutingTaxonomy.RoleDiagnostic),
            parameters: ToolSchema.Object(("directory_id", ToolSchema.String("Directory identifier."))).NoAdditionalProperties()));
        SetSessionRegistry(session, registry);
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(
            registry.GetDefinitions(),
            new IToolPack[] { new RecipeOverlapGuidancePack() }));

        var extractedCalls = new List<ToolCall> {
            new(
                "call_operational_recipe_overlap",
                "customx_live_query",
                "{}",
                new JsonObject(StringComparer.Ordinal),
                new JsonObject(StringComparer.Ordinal))
        };

        var result = BuildHostPackPreflightCallsMethod.Invoke(session, new object?[] { "thread-recipe-overlap", registry.GetDefinitions(), extractedCalls });
        var preflightCalls = Assert.IsAssignableFrom<IReadOnlyList<ToolCall>>(result);

        Assert.Equal(2, preflightCalls.Count);
        Assert.Equal("customx_pack_probe", preflightCalls[0].Name);
        Assert.Equal("customx_recipe_resolver", preflightCalls[1].Name);
        Assert.DoesNotContain(preflightCalls, static call => string.Equals(call.Name, "customx_directory_probe", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildHostPackPreflightCalls_SkipsRecipeOverlapHelperFromDifferentPack() {
        var session = ChatServiceTestSessionFactory.CreateIsolatedSession();
        var registry = new ToolRegistry();
        registry.Register(new PreflightStubTool(
            "customx_pack_probe",
            CreateRoutingContract("eventlog", ToolRoutingTaxonomy.RolePackInfo)));
        registry.Register(new PreflightStubTool(
            "customx_live_query",
            CreateRoutingContract("eventlog", ToolRoutingTaxonomy.RoleOperational),
            parameters: ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties()));
        registry.Register(new PreflightStubTool(
            "customx_recipe_resolver",
            CreateRoutingContract("eventlog", ToolRoutingTaxonomy.RoleResolver),
            parameters: ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties()));
        registry.Register(new PreflightStubTool(
            "customy_recipe_resolver",
            CreateRoutingContract("active_directory", ToolRoutingTaxonomy.RoleResolver),
            parameters: ToolSchema.Object(("machine_name", ToolSchema.String("Remote machine."))).NoAdditionalProperties()));
        SetSessionRegistry(session, registry);
        session.SetToolOrchestrationCatalogForTesting(ToolOrchestrationCatalog.Build(
            registry.GetDefinitions(),
            new IToolPack[] { new RecipeOverlapGuidancePack(), new CrossPackRecipeOverlapGuidancePack() }));

        var extractedCalls = new List<ToolCall> {
            new(
                "call_operational_recipe_cross_pack",
                "customx_live_query",
                "{}",
                new JsonObject(StringComparer.Ordinal),
                new JsonObject(StringComparer.Ordinal))
        };

        var result = BuildHostPackPreflightCallsMethod.Invoke(session, new object?[] { "thread-recipe-cross-pack", registry.GetDefinitions(), extractedCalls });
        var preflightCalls = Assert.IsAssignableFrom<IReadOnlyList<ToolCall>>(result);

        Assert.Equal(2, preflightCalls.Count);
        Assert.Equal("customx_pack_probe", preflightCalls[0].Name);
        Assert.Equal("customx_recipe_resolver", preflightCalls[1].Name);
        Assert.DoesNotContain(preflightCalls, static call => string.Equals(call.Name, "customy_recipe_resolver", StringComparison.Ordinal));
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

    private static JsonObject CreateRequiredSchema(params string[] requiredPropertyNames) {
        var normalizedPropertyNames = (requiredPropertyNames ?? Array.Empty<string>())
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var properties = new JsonObject(StringComparer.Ordinal);
        var required = new JsonArray();
        foreach (var propertyName in normalizedPropertyNames) {
            properties.Add(propertyName, new JsonObject(StringComparer.Ordinal).Add("type", "string"));
            required.Add(propertyName);
        }

        return new JsonObject(StringComparer.Ordinal)
            .Add("type", "object")
            .Add("properties", properties)
            .Add("required", required);
    }

    private sealed class PreflightStubTool : ITool {
        private readonly Func<JsonObject?, CancellationToken, Task<string>> _invoke;

        public PreflightStubTool(
            string name,
            ToolRoutingContract routing,
            JsonObject? parameters = null,
            ToolAuthenticationContract? authentication = null,
            ToolSetupContract? setup = null,
            ToolRecoveryContract? recovery = null) {
            Definition = new ToolDefinition(
                name,
                description: "preflight stub",
                parameters: parameters,
                routing: routing,
                authentication: authentication,
                setup: setup,
                recovery: recovery);
            _invoke = static (_, _) => Task.FromResult("""{"ok":true}""");
        }

        public ToolDefinition Definition { get; }

        public Task<string> InvokeAsync(JsonObject? arguments, CancellationToken cancellationToken) {
            return _invoke(arguments, cancellationToken);
        }
    }

    private sealed class ExplicitPreflightCatalogOverlayPack : IToolPack, IToolPackCatalogProvider {
        public ToolPackDescriptor Descriptor { get; } = new() {
            Id = "active_directory",
            Name = "Active Directory",
            Tier = ToolCapabilityTier.ReadOnly,
            Description = "Synthetic explicit preflight overlay."
        };

        public void Register(ToolRegistry registry) {
            _ = registry;
        }

        public IReadOnlyList<ToolPackToolCatalogEntryModel> GetToolCatalog() {
            return new[] {
                new ToolPackToolCatalogEntryModel {
                    Name = "customx_pack_probe",
                    IsPackInfoTool = true,
                    Routing = new ToolPackToolRoutingModel {
                        PackId = "active_directory",
                        Role = ToolRoutingTaxonomy.RoleOperational,
                        Source = ToolRoutingTaxonomy.SourceExplicit
                    }
                },
                new ToolPackToolCatalogEntryModel {
                    Name = "customx_discover_scope",
                    IsEnvironmentDiscoverTool = true,
                    Routing = new ToolPackToolRoutingModel {
                        PackId = "active_directory",
                        Role = ToolRoutingTaxonomy.RoleOperational,
                        Source = ToolRoutingTaxonomy.SourceExplicit
                    }
                }
            };
        }
    }

    private sealed class PreferredProbeGuidancePack : IToolPack, IToolPackGuidanceProvider {
        public ToolPackDescriptor Descriptor { get; } = new() {
            Id = "eventlog",
            Name = "EventLog",
            Tier = ToolCapabilityTier.ReadOnly,
            Description = "Synthetic preferred probe guidance."
        };

        public void Register(ToolRegistry registry) {
            _ = registry;
        }

        public ToolPackInfoModel GetPackGuidance() {
            return new ToolPackInfoModel {
                RuntimeCapabilities = new ToolPackRuntimeCapabilitiesModel {
                    PreferredProbeTools = new[] { "customx_connectivity_probe" }
                }
            };
        }
    }

    private sealed class RecipeOverlapGuidancePack : IToolPack, IToolPackGuidanceProvider {
        public ToolPackDescriptor Descriptor { get; } = new() {
            Id = "eventlog",
            Name = "EventLog",
            Tier = ToolCapabilityTier.ReadOnly,
            Description = "Synthetic recipe overlap guidance."
        };

        public void Register(ToolRegistry registry) {
            _ = registry;
        }

        public ToolPackInfoModel GetPackGuidance() {
            return new ToolPackInfoModel {
                RecommendedRecipes = new[] {
                    new ToolPackRecipeModel {
                        Id = "custom_runtime_triage",
                        Summary = "Synthetic runtime triage recipe.",
                        WhenToUse = "Use when runtime validation is needed before a live query.",
                        Steps = new[] {
                            new ToolPackFlowStepModel {
                                Goal = "Resolve runtime details",
                                SuggestedTools = new[] { "customx_recipe_resolver" }
                            },
                            new ToolPackFlowStepModel {
                                Goal = "Probe unrelated directory details",
                                SuggestedTools = new[] { "customx_directory_probe" }
                            },
                            new ToolPackFlowStepModel {
                                Goal = "Run the live query",
                                SuggestedTools = new[] { "customx_live_query" }
                            }
                        }
                    }
                }
            };
        }
    }

    private sealed class CrossPackRecipeOverlapGuidancePack : IToolPack, IToolPackGuidanceProvider {
        public ToolPackDescriptor Descriptor { get; } = new() {
            Id = "active_directory",
            Name = "Active Directory",
            Tier = ToolCapabilityTier.ReadOnly,
            Description = "Synthetic cross-pack recipe overlap guidance."
        };

        public void Register(ToolRegistry registry) {
            _ = registry;
        }

        public ToolPackInfoModel GetPackGuidance() {
            return new ToolPackInfoModel {
                Pack = "active_directory",
                RecommendedRecipes = new[] {
                    new ToolPackRecipeModel {
                        Id = "custom_runtime_triage",
                        Summary = "Synthetic cross-pack runtime triage recipe.",
                        WhenToUse = "Use when a different pack reuses the same recipe id.",
                        Steps = new[] {
                            new ToolPackFlowStepModel {
                                Goal = "Resolve cross-pack runtime details",
                                SuggestedTools = new[] { "customy_recipe_resolver" }
                            }
                        }
                    }
                }
            };
        }
    }
}
