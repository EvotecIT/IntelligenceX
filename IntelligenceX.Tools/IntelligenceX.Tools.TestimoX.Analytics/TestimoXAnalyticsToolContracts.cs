using System;
using System.Collections.Generic;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.TestimoX;

internal static class TestimoXAnalyticsToolContracts {
    private const string DomainIntentFamily = "monitoring_artifacts";
    private const string DomainIntentActionId = "act_domain_scope_monitoring_artifacts";

    private static readonly IReadOnlyDictionary<string, string> DeclaredRolesByToolName =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            ["testimox_analytics_pack_info"] = ToolRoutingTaxonomy.RolePackInfo,
            ["testimox_report_job_history"] = ToolRoutingTaxonomy.RoleResolver,
            ["testimox_history_query"] = ToolRoutingTaxonomy.RoleResolver,
            ["testimox_probe_index_status"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["testimox_analytics_diagnostics_get"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["testimox_maintenance_window_history"] = ToolRoutingTaxonomy.RoleResolver,
            ["testimox_report_data_snapshot_get"] = ToolRoutingTaxonomy.RoleResolver,
            ["testimox_report_snapshot_get"] = ToolRoutingTaxonomy.RoleResolver
        };

    private static readonly string[] SetupHintKeys = {
        "history_directory",
        "report_key",
        "job_key",
        "probe_names",
        "since_utc"
    };

    private static readonly string[] SignalTokens = {
        "monitoring",
        "history",
        "report",
        "snapshot",
        "maintenance",
        "availability",
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
            PackId = "testimox_analytics",
            Role = ResolveRole(definition.Name, existing?.Role),
            DomainIntentFamily = string.IsNullOrWhiteSpace(existing?.DomainIntentFamily)
                ? DomainIntentFamily
                : existing!.DomainIntentFamily,
            DomainIntentActionId = string.IsNullOrWhiteSpace(existing?.DomainIntentActionId)
                ? DomainIntentActionId
                : existing!.DomainIntentActionId,
            DomainSignalTokens = existing?.DomainSignalTokens.Count > 0 ? existing.DomainSignalTokens : SignalTokens,
            RequiresSelectionForFallback = existing?.RequiresSelectionForFallback ?? false,
            FallbackSelectionKeys = existing?.FallbackSelectionKeys ?? Array.Empty<string>(),
            FallbackHintKeys = existing?.FallbackHintKeys ?? Array.Empty<string>()
        };
    }

    private static ToolHandoffContract? BuildHandoff(ToolDefinition definition) {
        if (string.Equals(definition.Name, "testimox_analytics_diagnostics_get", StringComparison.OrdinalIgnoreCase)) {
            var routes = new List<ToolHandoffRoute> {
                ToolContractDefaults.CreateRoute(
                    targetPackId: "filesystem",
                    targetToolName: "fs_read",
                    reason: "Promote analytics diagnostics into local snapshot file inspection when the raw diagnostics JSON is needed.",
                    bindings: new[] {
                        ToolContractDefaults.CreateBinding("snapshot_path", "path")
                    })
            };
            routes.AddRange(ToolContractDefaults.CreateRemoteHostFollowUpRoutes(
                sourceField: "slow_probes[].target",
                systemReason: "Promote slow probe diagnostics into remote host inspection for affected targets.",
                eventLogReason: "Promote slow probe diagnostics into remote EventViewerX follow-up for affected targets.",
                isRequired: false));
            return ToolContractDefaults.CreateHandoff(routes);
        }

        if (string.Equals(definition.Name, "testimox_report_job_history", StringComparison.OrdinalIgnoreCase)) {
            return ToolContractDefaults.CreateHandoff(new[] {
                ToolContractDefaults.CreateRoute(
                    targetPackId: "testimox_analytics",
                    targetToolName: "testimox_report_data_snapshot_get",
                    reason: "Promote report job history into cached monitoring report data snapshot retrieval.",
                    bindings: new[] {
                        ToolContractDefaults.CreateBinding("history_directory", "history_directory"),
                        ToolContractDefaults.CreateBinding("jobs[].report_key", "report_key")
                    }),
                ToolContractDefaults.CreateRoute(
                    targetPackId: "testimox_analytics",
                    targetToolName: "testimox_report_snapshot_get",
                    reason: "Promote report job history into cached monitoring HTML report snapshot retrieval.",
                    bindings: new[] {
                        ToolContractDefaults.CreateBinding("history_directory", "history_directory"),
                        ToolContractDefaults.CreateBinding("jobs[].report_key", "report_key")
                    }),
                ToolContractDefaults.CreateRoute(
                    targetPackId: "filesystem",
                    targetToolName: "fs_read",
                    reason: "Promote report job history into local report file inspection when a stored report path is available.",
                    bindings: new[] {
                        ToolContractDefaults.CreateBinding("jobs[].report_path", "path", isRequired: false)
                    })
            });
        }

        if (string.Equals(definition.Name, "testimox_history_query", StringComparison.OrdinalIgnoreCase)) {
            return ToolContractDefaults.CreateHandoff(ToolContractDefaults.CreateRemoteHostFollowUpRoutes(
                sourceField: "rows[].target",
                systemReason: "Promote monitoring target history into remote host inspection.",
                eventLogReason: "Promote monitoring target history into remote EventViewerX follow-up.",
                isRequired: false));
        }

        return definition.Handoff;
    }

    private static ToolSetupContract? BuildSetup(ToolDefinition definition, ToolRoutingContract routing) {
        return ToolContractDefaults.PreserveExplicitSetupOrCreateDefault(
            definition,
            routing.Role,
            () => ToolContractDefaults.CreateHintOnlySetup(SetupHintKeys));
    }

    private static ToolRecoveryContract? BuildRecovery(ToolDefinition definition, ToolRoutingContract routing) {
        return ToolContractDefaults.PreserveExplicitRecoveryOrCreateDefault(
            definition,
            routing.Role,
            () => ToolContractDefaults.CreateNoRetryRecovery());
    }

    private static string ResolveRole(string toolName, string? existingRole) {
        return ToolRoutingRoleResolver.ResolveExplicitOrDeclared(
            explicitRole: existingRole,
            toolName: toolName,
            declaredRolesByToolName: DeclaredRolesByToolName,
            packDisplayName: "TestimoX Analytics");
    }
}
