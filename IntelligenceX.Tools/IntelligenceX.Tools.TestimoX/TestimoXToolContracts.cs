using System;
using System.Collections.Generic;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.TestimoX;

internal static class TestimoXToolContracts {
    private const string DomainIntentFamily = "security_posture";
    private const string DomainIntentActionId = "act_domain_scope_security_posture";

    private static readonly IReadOnlyDictionary<string, string> DeclaredRolesByToolName =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            ["testimox_pack_info"] = ToolRoutingTaxonomy.RolePackInfo,
            ["testimox_runs_list"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["testimox_run_summary"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["testimox_baselines_list"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["testimox_baseline_compare"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["testimox_profiles_list"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["testimox_rule_inventory"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["testimox_source_query"] = ToolRoutingTaxonomy.RoleResolver,
            ["testimox_baseline_crosswalk"] = ToolRoutingTaxonomy.RoleResolver,
            ["testimox_rules_list"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["testimox_rules_run"] = ToolRoutingTaxonomy.RoleOperational
        };

    private static readonly string[] SetupHintKeys = {
        "search_text",
        "categories",
        "tags",
        "source_types",
        "rule_origin"
    };

    private static readonly string[] TestimoXSignalTokens = {
        "testimox",
        "testimo",
        "baseline",
        "assessment",
        "compliance",
        "hardening",
        "security",
        "posture",
        DomainIntentFamily,
        DomainIntentActionId
    };

    public static ITool Apply(ITool tool) {
        ArgumentNullException.ThrowIfNull(tool);

        var definition = tool.Definition;
        var routing = BuildRouting(definition);
        var setup = BuildSetup(definition, routing);
        var handoff = BuildHandoff(definition);
        var recovery = BuildRecovery(definition, routing);
        var updatedDefinition = ToolDefinitionOverlay.WithContracts(
            definition: definition,
            routing: routing,
            setup: setup,
            handoff: handoff,
            recovery: recovery);
        return ToolDefinitionOverlay.WithDefinition(tool, updatedDefinition);
    }

    private static ToolRoutingContract BuildRouting(ToolDefinition definition) {
        var existing = definition.Routing;
        return new ToolRoutingContract {
            IsRoutingAware = true,
            RoutingContractId = string.IsNullOrWhiteSpace(existing?.RoutingContractId)
                ? ToolRoutingContract.DefaultContractId
                : existing!.RoutingContractId,
            RoutingSource = ToolRoutingTaxonomy.SourceExplicit,
            PackId = "testimox",
            Role = ResolveRole(definition.Name, existing?.Role),
            DomainIntentFamily = string.IsNullOrWhiteSpace(existing?.DomainIntentFamily)
                ? DomainIntentFamily
                : existing!.DomainIntentFamily,
            DomainIntentActionId = string.IsNullOrWhiteSpace(existing?.DomainIntentActionId)
                ? DomainIntentActionId
                : existing!.DomainIntentActionId,
            DomainSignalTokens = existing?.DomainSignalTokens.Count > 0 ? existing.DomainSignalTokens : TestimoXSignalTokens,
            RequiresSelectionForFallback = existing?.RequiresSelectionForFallback ?? false,
            FallbackSelectionKeys = existing?.FallbackSelectionKeys ?? Array.Empty<string>(),
            FallbackHintKeys = existing?.FallbackHintKeys ?? Array.Empty<string>()
        };
    }

    private static ToolHandoffContract? BuildHandoff(ToolDefinition definition) {
        if (string.Equals(definition.Name, "testimox_run_summary", StringComparison.OrdinalIgnoreCase)) {
            var routes = new List<ToolHandoffRoute> {
                ToolContractDefaults.CreateRoute(
                    targetPackId: "active_directory",
                    targetToolName: "ad_scope_discovery",
                    reason: "Promote stored TestimoX domain or domain-controller scope into explicit AD scope follow-up.",
                    bindings: new[] {
                        ToolContractDefaults.CreateBinding("rows[].domain", "domain_name", isRequired: false),
                        ToolContractDefaults.CreateBinding("rows[].domain_controller", "domain_controller", isRequired: false)
                    })
            };
            routes.AddRange(ToolContractDefaults.CreateRemoteHostFollowUpRoutes(
                sourceField: "rows[].domain_controller",
                systemReason: "Promote stored TestimoX domain-controller scope into remote ComputerX host inspection.",
                eventLogReason: "Promote stored TestimoX domain-controller scope into remote EventViewerX follow-up.",
                isRequired: false));
            return ToolContractDefaults.CreateHandoff(routes);
        }

        return definition.Handoff;
    }

    private static ToolSetupContract? BuildSetup(ToolDefinition definition, ToolRoutingContract routing) {
        return ToolContractDefaults.PreserveExplicitSetupOrCreateDefault(
            definition,
            routing.Role,
            () => {
                if (string.Equals(definition.Name, "testimox_runs_list", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(definition.Name, "testimox_run_summary", StringComparison.OrdinalIgnoreCase)) {
                    return ToolContractDefaults.CreateHintOnlySetup(
                        definition.Routing?.FallbackHintKeys?.Count > 0
                            ? definition.Routing.FallbackHintKeys
                            : SetupHintKeys);
                }

                return ToolContractDefaults.CreateRequiredSetup(
                    setupToolName: "testimox_rules_list",
                    requirementId: "testimox_rules_catalog",
                    requirementKind: ToolSetupRequirementKinds.Capability,
                    setupHintKeys: SetupHintKeys);
            });
    }

    private static ToolRecoveryContract? BuildRecovery(ToolDefinition definition, ToolRoutingContract routing) {
        return ToolContractDefaults.PreserveExplicitRecoveryOrCreateDefault(
            definition,
            routing.Role,
            () => {
                var supportsRetry = string.Equals(definition.Name, "testimox_rules_run", StringComparison.OrdinalIgnoreCase);
                return ToolContractDefaults.CreateRecovery(
                    supportsTransientRetry: supportsRetry,
                    maxRetryAttempts: supportsRetry ? 1 : 0,
                    retryableErrorCodes: supportsRetry
                        ? new[] { "execution_failed", "timeout", "transport_unavailable" }
                        : Array.Empty<string>(),
                    recoveryToolNames: new[] { "testimox_rules_list" });
            });
    }

    private static string ResolveRole(string toolName, string? existingRole) {
        return ToolRoutingRoleResolver.ResolveExplicitOrDeclared(
            explicitRole: existingRole,
            toolName: toolName,
            declaredRolesByToolName: DeclaredRolesByToolName,
            packDisplayName: "TestimoX");
    }
}
