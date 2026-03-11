using System;
using System.Collections.Generic;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.PowerShell;

internal static class PowerShellToolContracts {
    private static readonly IReadOnlyDictionary<string, string> DeclaredRolesByToolName =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            ["powershell_pack_info"] = ToolRoutingTaxonomy.RolePackInfo,
            ["powershell_environment_discover"] = ToolRoutingTaxonomy.RoleEnvironmentDiscover,
            ["powershell_hosts"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["powershell_run"] = ToolRoutingTaxonomy.RoleOperational
        };

    private static readonly string[] SetupHintKeys = {
        "host",
        "host_name",
        "host_names",
        "timeout_ms",
        "intent"
    };

    private static readonly string[] PowerShellSignalTokens = {
        "powershell",
        "script",
        "command",
        "host",
        "session"
    };

    public static ITool Apply(ITool tool) {
        ArgumentNullException.ThrowIfNull(tool);

        var definition = tool.Definition;
        var routing = BuildRouting(definition);
        var setup = BuildSetup(definition, routing);
        var recovery = BuildRecovery(definition, routing);
        var updatedDefinition = ToolDefinitionOverlay.WithContracts(
            definition: definition,
            routing: routing,
            setup: setup,
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
            PackId = "powershell",
            Role = ResolveRole(definition.Name, existing?.Role),
            DomainIntentFamily = existing?.DomainIntentFamily ?? string.Empty,
            DomainIntentActionId = existing?.DomainIntentActionId ?? string.Empty,
            DomainSignalTokens = existing?.DomainSignalTokens.Count > 0 ? existing.DomainSignalTokens : PowerShellSignalTokens,
            RequiresSelectionForFallback = existing?.RequiresSelectionForFallback ?? false,
            FallbackSelectionKeys = existing?.FallbackSelectionKeys ?? Array.Empty<string>(),
            FallbackHintKeys = existing?.FallbackHintKeys ?? Array.Empty<string>()
        };
    }

    private static ToolSetupContract? BuildSetup(ToolDefinition definition, ToolRoutingContract routing) {
        return ToolContractDefaults.PreserveExplicitSetupOrCreateDefault(
            definition,
            routing.Role,
            () => ToolContractDefaults.CreateRequiredSetup(
                setupToolName: "powershell_environment_discover",
                requirementId: "powershell_host_connectivity",
                requirementKind: ToolSetupRequirementKinds.Connectivity,
                setupHintKeys: SetupHintKeys));
    }

    private static ToolRecoveryContract? BuildRecovery(ToolDefinition definition, ToolRoutingContract routing) {
        return ToolContractDefaults.PreserveExplicitRecoveryOrCreateDefault(
            definition,
            routing.Role,
            () => {
                if (definition.WriteGovernance?.IsWriteCapable == true) {
                    return ToolContractDefaults.CreateNoRetryRecovery(
                        recoveryToolNames: new[] { "powershell_environment_discover" });
                }

                return ToolContractDefaults.CreateRecovery(
                    supportsTransientRetry: true,
                    maxRetryAttempts: 1,
                    retryableErrorCodes: new[] { "timeout", "query_failed", "probe_failed" },
                    recoveryToolNames: new[] { "powershell_environment_discover", "powershell_hosts" });
            });
    }

    private static string ResolveRole(string toolName, string? existingRole) {
        return ToolRoutingRoleResolver.ResolveExplicitOrDeclared(
            explicitRole: existingRole,
            toolName: toolName,
            declaredRolesByToolName: DeclaredRolesByToolName,
            packDisplayName: "PowerShell");
    }
}
