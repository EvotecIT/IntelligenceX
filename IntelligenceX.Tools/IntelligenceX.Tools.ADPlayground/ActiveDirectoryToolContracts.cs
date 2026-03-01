using System;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

internal static class ActiveDirectoryToolContracts {
    private static readonly string[] DomainSignalTokens = {
        "dc",
        "ldap",
        "gpo",
        "kerberos",
        "replication",
        "sysvol",
        "netlogon",
        "ntds",
        "forest",
        "trust",
        "active_directory",
        "adplayground"
    };

    private static readonly string[] SetupHintKeys = {
        "domain_controller",
        "search_base_dn",
        "domain_name",
        "forest_name"
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
            PackId = "active_directory",
            Role = ResolveRole(definition.Name),
            DomainIntentFamily = ToolSelectionMetadata.DomainIntentFamilyAd,
            DomainIntentActionId = ToolSelectionMetadata.DomainIntentActionIdAd,
            DomainSignalTokens = existing?.DomainSignalTokens.Count > 0 ? existing.DomainSignalTokens : DomainSignalTokens,
            RequiresSelectionForFallback = existing?.RequiresSelectionForFallback ?? false,
            FallbackSelectionKeys = existing?.FallbackSelectionKeys ?? Array.Empty<string>(),
            FallbackHintKeys = existing?.FallbackHintKeys ?? Array.Empty<string>()
        };
    }

    private static ToolSetupContract? BuildSetup(ToolDefinition definition, ToolRoutingContract routing) {
        if (string.Equals(routing.Role, ToolRoutingTaxonomy.RolePackInfo, StringComparison.OrdinalIgnoreCase)) {
            return definition.Setup;
        }

        if (definition.Setup is { IsSetupAware: true }) {
            return definition.Setup;
        }

        return new ToolSetupContract {
            IsSetupAware = true,
            SetupToolName = "ad_environment_discover",
            Requirements = new[] {
                new ToolSetupRequirement {
                    RequirementId = "ad_directory_context",
                    Kind = ToolSetupRequirementKinds.Configuration,
                    IsRequired = true,
                    HintKeys = SetupHintKeys
                },
                new ToolSetupRequirement {
                    RequirementId = "ad_ldap_connectivity",
                    Kind = ToolSetupRequirementKinds.Connectivity,
                    IsRequired = true,
                    HintKeys = new[] { "domain_controller", "domain_name", "forest_name" }
                }
            },
            SetupHintKeys = SetupHintKeys
        };
    }

    private static ToolHandoffContract? BuildHandoff(ToolDefinition definition) {
        if (!string.Equals(definition.Name, "ad_handoff_prepare", StringComparison.OrdinalIgnoreCase)) {
            return definition.Handoff;
        }

        return new ToolHandoffContract {
            IsHandoffAware = true,
            OutboundRoutes = new[] {
                new ToolHandoffRoute {
                    TargetPackId = "active_directory",
                    TargetToolName = "ad_object_resolve",
                    Reason = "Use normalized identities from handoff payload for batched AD object resolution.",
                    Bindings = new[] {
                        new ToolHandoffBinding {
                            SourceField = "target_arguments/ad_object_resolve/identities",
                            TargetArgument = "identities",
                            IsRequired = true
                        }
                    }
                },
                new ToolHandoffRoute {
                    TargetPackId = "active_directory",
                    TargetToolName = "ad_scope_discovery",
                    Reason = "Use discovered domain hints to bootstrap AD scope before resolution calls.",
                    Bindings = new[] {
                        new ToolHandoffBinding {
                            SourceField = "target_arguments/ad_scope_discovery/domain_name",
                            TargetArgument = "domain_name",
                            IsRequired = false
                        },
                        new ToolHandoffBinding {
                            SourceField = "target_arguments/ad_scope_discovery/include_domain_controllers",
                            TargetArgument = "include_domain_controllers",
                            IsRequired = false
                        }
                    }
                }
            }
        };
    }

    private static ToolRecoveryContract? BuildRecovery(ToolDefinition definition, ToolRoutingContract routing) {
        if (definition.Recovery is { IsRecoveryAware: true }) {
            return definition.Recovery;
        }

        if (string.Equals(routing.Role, ToolRoutingTaxonomy.RolePackInfo, StringComparison.OrdinalIgnoreCase)) {
            return definition.Recovery;
        }

        return new ToolRecoveryContract {
            IsRecoveryAware = true,
            SupportsTransientRetry = true,
            MaxRetryAttempts = 1,
            RetryableErrorCodes = new[] { "timeout", "query_failed", "probe_failed", "discovery_failed", "transport_unavailable" }
        };
    }

    private static string ResolveRole(string toolName) {
        if (string.Equals(toolName, "ad_pack_info", StringComparison.OrdinalIgnoreCase)) {
            return ToolRoutingTaxonomy.RolePackInfo;
        }

        if (string.Equals(toolName, "ad_environment_discover", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "ad_scope_discovery", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "ad_forest_discover", StringComparison.OrdinalIgnoreCase)) {
            return ToolRoutingTaxonomy.RoleEnvironmentDiscover;
        }

        if (string.Equals(toolName, "ad_directory_discovery_diagnostics", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "ad_ldap_diagnostics", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "ad_monitoring_probe_catalog", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "ad_monitoring_probe_run", StringComparison.OrdinalIgnoreCase)) {
            return ToolRoutingTaxonomy.RoleDiagnostic;
        }

        if (string.Equals(toolName, "ad_dns_server_config", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "ad_dns_zone_config", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "ad_dns_zone_security", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "ad_dns_delegation", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "ad_dns_scavenging", StringComparison.OrdinalIgnoreCase)) {
            return ToolRoutingTaxonomy.RoleResolver;
        }

        return ToolRoutingTaxonomy.RoleOperational;
    }
}
