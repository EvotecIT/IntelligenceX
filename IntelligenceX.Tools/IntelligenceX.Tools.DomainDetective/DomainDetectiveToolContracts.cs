using System;
using System.Collections.Generic;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.DomainDetective;

internal static class DomainDetectiveToolContracts {
    private static readonly IReadOnlyDictionary<string, string> DeclaredRolesByToolName =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            ["domaindetective_pack_info"] = ToolRoutingTaxonomy.RolePackInfo,
            ["domaindetective_checks_catalog"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["domaindetective_domain_summary"] = ToolRoutingTaxonomy.RoleOperational,
            ["domaindetective_network_probe"] = ToolRoutingTaxonomy.RoleDiagnostic
        };

    private static readonly string[] DomainSignalTokens = {
        "dns",
        "mx",
        "spf",
        "dmarc",
        "dkim",
        "ns",
        "dnssec",
        "caa",
        "whois",
        "mta_sts",
        "bimi",
        "domaindetective",
        "domain_detective"
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
            PackId = "domaindetective",
            Role = ResolveRole(definition.Name, existing?.Role),
            DomainIntentFamily = ToolSelectionMetadata.DomainIntentFamilyPublic,
            DomainIntentActionId = ToolSelectionMetadata.DomainIntentActionIdPublic,
            DomainSignalTokens = existing?.DomainSignalTokens.Count > 0 ? existing.DomainSignalTokens : DomainSignalTokens,
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
                setupToolName: "domaindetective_checks_catalog",
                requirementId: "public_dns_connectivity",
                requirementKind: ToolSetupRequirementKinds.Connectivity,
                setupHintKeys: new[] { "domain", "checks", "dns_endpoint", "host", "timeout_ms" },
                requirementHintKeys: new[] { "domain", "host", "dns_endpoint", "timeout_ms" }));
    }

    private static ToolHandoffContract? BuildHandoff(ToolDefinition definition) {
        if (string.Equals(definition.Name, "domaindetective_domain_summary", StringComparison.OrdinalIgnoreCase)) {
            return ToolContractDefaults.CreateHandoff(new[] {
                ToolContractDefaults.CreateRoute(
                    targetPackId: "active_directory",
                    targetToolName: "ad_scope_discovery",
                    reason: "Escalate directory-scoped investigations into explicit AD scope discovery.",
                    bindings: new[] {
                        ToolContractDefaults.CreateBinding("domain", "domain_name")
                    }),
                ToolContractDefaults.CreateRoute(
                    targetPackId: "active_directory",
                    targetToolName: "ad_directory_discovery_diagnostics",
                    reason: "Follow up on directory-focused domain issues with AD discovery diagnostics.",
                    bindings: new[] {
                        ToolContractDefaults.CreateBinding("domain", "forest_name")
                    })
            });
        }

        if (string.Equals(definition.Name, "domaindetective_network_probe", StringComparison.OrdinalIgnoreCase)) {
            return ToolContractDefaults.CreateHandoff(new[] {
                ToolContractDefaults.CreateRoute(
                    targetPackId: "active_directory",
                    targetToolName: "ad_scope_discovery",
                    reason: "Use host evidence as an AD domain-controller hint when intent shifts to directory diagnostics.",
                    bindings: new[] {
                        ToolContractDefaults.CreateBinding("host", "domain_controller")
                    })
            });
        }

        return definition.Handoff;
    }

    private static ToolRecoveryContract? BuildRecovery(ToolDefinition definition, ToolRoutingContract routing) {
        return ToolContractDefaults.PreserveExplicitRecoveryOrCreateDefault(
            definition,
            routing.Role,
            () => {
                if (string.Equals(definition.Name, "domaindetective_checks_catalog", StringComparison.OrdinalIgnoreCase)) {
                    return ToolContractDefaults.CreateNoRetryRecovery();
                }

                if (string.Equals(definition.Name, "domaindetective_domain_summary", StringComparison.OrdinalIgnoreCase)) {
                    return ToolContractDefaults.CreateRecovery(
                        supportsTransientRetry: true,
                        maxRetryAttempts: 2,
                        retryableErrorCodes: new[] { "timeout", "query_failed", "transport_unavailable" },
                        recoveryToolNames: new[] { "domaindetective_checks_catalog" });
                }

                if (string.Equals(definition.Name, "domaindetective_network_probe", StringComparison.OrdinalIgnoreCase)) {
                    return ToolContractDefaults.CreateRecovery(
                        supportsTransientRetry: true,
                        maxRetryAttempts: 1,
                        retryableErrorCodes: new[] { "probe_failed", "timeout", "transport_unavailable" },
                        recoveryToolNames: new[] { "domaindetective_checks_catalog" });
                }

                return definition.Recovery;
            });
    }

    private static string ResolveRole(string toolName, string? existingRole) {
        return ToolRoutingRoleResolver.ResolveExplicitOrDeclared(
            explicitRole: existingRole,
            toolName: toolName,
            declaredRolesByToolName: DeclaredRolesByToolName,
            packDisplayName: "DomainDetective");
    }
}
