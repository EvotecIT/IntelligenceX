using System;
using System.Collections.Generic;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.DnsClientX;

internal static class DnsClientXToolContracts {
    private static readonly IReadOnlyDictionary<string, string> DeclaredRolesByToolName =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            ["dnsclientx_pack_info"] = ToolRoutingTaxonomy.RolePackInfo,
            ["dnsclientx_query"] = ToolRoutingTaxonomy.RoleResolver,
            ["dnsclientx_ping"] = ToolRoutingTaxonomy.RoleDiagnostic
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
        "dnsclientx",
        "dns_client_x"
    };

    public static ITool Apply(ITool tool) {
        ArgumentNullException.ThrowIfNull(tool);

        var definition = tool.Definition;
        var routing = BuildRouting(definition);
        var setup = BuildSetup(definition, routing);
        var handoff = BuildHandoff(definition);
        var recovery = BuildRecovery(definition);
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
            PackId = "dnsclientx",
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
                setupToolName: "dnsclientx_ping",
                requirementId: "dns_resolver_connectivity",
                requirementKind: ToolSetupRequirementKinds.Connectivity,
                setupHintKeys: new[] { "target", "targets", "name", "type", "endpoint", "timeout_ms" },
                requirementHintKeys: new[] { "endpoint", "timeout_ms" }));
    }

    private static ToolHandoffContract? BuildHandoff(ToolDefinition definition) {
        if (string.Equals(definition.Name, "dnsclientx_query", StringComparison.OrdinalIgnoreCase)) {
            return ToolContractDefaults.CreateHandoff(new[] {
                ToolContractDefaults.CreateRoute(
                    targetPackId: "domaindetective",
                    targetToolName: "domaindetective_domain_summary",
                    reason: "Promote resolver-level DNS evidence into domain posture checks.",
                    bindings: new[] {
                        ToolContractDefaults.CreateBinding("query/name", "domain"),
                        ToolContractDefaults.CreateBinding("query/endpoint", "dns_endpoint", isRequired: false)
                    })
            });
        }

        if (string.Equals(definition.Name, "dnsclientx_ping", StringComparison.OrdinalIgnoreCase)) {
            return ToolContractDefaults.CreateHandoff(new[] {
                ToolContractDefaults.CreateRoute(
                    targetPackId: "domaindetective",
                    targetToolName: "domaindetective_network_probe",
                    reason: "Escalate host-level reachability checks to richer network probes when needed.",
                    bindings: new[] {
                        ToolContractDefaults.CreateBinding("probed_targets/0", "host"),
                        ToolContractDefaults.CreateBinding("timeout_ms", "timeout_ms", isRequired: false)
                    })
            });
        }

        return definition.Handoff;
    }

    private static ToolRecoveryContract? BuildRecovery(ToolDefinition definition) {
        var routingRole = definition.Routing?.Role;
        return ToolContractDefaults.PreserveExplicitRecoveryOrCreateDefault(
            definition,
            routingRole,
            () => {
                if (string.Equals(definition.Name, "dnsclientx_query", StringComparison.OrdinalIgnoreCase)) {
                    return ToolContractDefaults.CreateRecovery(
                        supportsTransientRetry: true,
                        maxRetryAttempts: 2,
                        retryableErrorCodes: new[] { "timeout", "query_failed", "transport_unavailable" });
                }

                if (string.Equals(definition.Name, "dnsclientx_ping", StringComparison.OrdinalIgnoreCase)) {
                    return ToolContractDefaults.CreateNoRetryRecovery();
                }

                return definition.Recovery;
            });
    }

    private static string ResolveRole(string toolName, string? existingRole) {
        return ToolRoutingRoleResolver.ResolveExplicitOrDeclared(
            explicitRole: existingRole,
            toolName: toolName,
            declaredRolesByToolName: DeclaredRolesByToolName,
            packDisplayName: "DnsClientX");
    }
}
