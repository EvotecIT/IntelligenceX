using System;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.DomainDetective;

internal static class DomainDetectiveToolContracts {
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
            Role = ResolveRole(definition.Name),
            DomainIntentFamily = ToolSelectionMetadata.DomainIntentFamilyPublic,
            DomainIntentActionId = ToolSelectionMetadata.DomainIntentActionIdPublic,
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
            SetupToolName = "domaindetective_checks_catalog",
            Requirements = new[] {
                new ToolSetupRequirement {
                    RequirementId = "public_dns_connectivity",
                    Kind = ToolSetupRequirementKinds.Connectivity,
                    IsRequired = true,
                    HintKeys = new[] { "domain", "host", "dns_endpoint", "timeout_ms" }
                }
            },
            SetupHintKeys = new[] { "domain", "checks", "dns_endpoint", "host", "timeout_ms" }
        };
    }

    private static ToolHandoffContract? BuildHandoff(ToolDefinition definition) {
        if (string.Equals(definition.Name, "domaindetective_domain_summary", StringComparison.OrdinalIgnoreCase)) {
            return new ToolHandoffContract {
                IsHandoffAware = true,
                OutboundRoutes = new[] {
                    new ToolHandoffRoute {
                        TargetPackId = "active_directory",
                        TargetToolName = "ad_scope_discovery",
                        Reason = "Escalate directory-scoped investigations into explicit AD scope discovery.",
                        Bindings = new[] {
                            new ToolHandoffBinding {
                                SourceField = "domain",
                                TargetArgument = "domain_name",
                                IsRequired = true
                            }
                        }
                    },
                    new ToolHandoffRoute {
                        TargetPackId = "active_directory",
                        TargetToolName = "ad_directory_discovery_diagnostics",
                        Reason = "Follow up on directory-focused domain issues with AD discovery diagnostics.",
                        Bindings = new[] {
                            new ToolHandoffBinding {
                                SourceField = "domain",
                                TargetArgument = "forest_name",
                                IsRequired = true
                            }
                        }
                    }
                }
            };
        }

        if (string.Equals(definition.Name, "domaindetective_network_probe", StringComparison.OrdinalIgnoreCase)) {
            return new ToolHandoffContract {
                IsHandoffAware = true,
                OutboundRoutes = new[] {
                    new ToolHandoffRoute {
                        TargetPackId = "active_directory",
                        TargetToolName = "ad_scope_discovery",
                        Reason = "Use host evidence as an AD domain-controller hint when intent shifts to directory diagnostics.",
                        Bindings = new[] {
                            new ToolHandoffBinding {
                                SourceField = "host",
                                TargetArgument = "domain_controller",
                                IsRequired = true
                            }
                        }
                    }
                }
            };
        }

        return definition.Handoff;
    }

    private static ToolRecoveryContract? BuildRecovery(ToolDefinition definition, ToolRoutingContract routing) {
        if (definition.Recovery is { IsRecoveryAware: true }) {
            return definition.Recovery;
        }

        if (string.Equals(routing.Role, ToolRoutingTaxonomy.RolePackInfo, StringComparison.OrdinalIgnoreCase)) {
            return definition.Recovery;
        }

        if (string.Equals(definition.Name, "domaindetective_checks_catalog", StringComparison.OrdinalIgnoreCase)) {
            return new ToolRecoveryContract {
                IsRecoveryAware = true,
                SupportsTransientRetry = false,
                MaxRetryAttempts = 0
            };
        }

        if (string.Equals(definition.Name, "domaindetective_domain_summary", StringComparison.OrdinalIgnoreCase)) {
            return new ToolRecoveryContract {
                IsRecoveryAware = true,
                SupportsTransientRetry = true,
                MaxRetryAttempts = 2,
                RetryableErrorCodes = new[] { "timeout", "query_failed", "transport_unavailable" },
                RecoveryToolNames = new[] { "domaindetective_checks_catalog" }
            };
        }

        if (string.Equals(definition.Name, "domaindetective_network_probe", StringComparison.OrdinalIgnoreCase)) {
            return new ToolRecoveryContract {
                IsRecoveryAware = true,
                SupportsTransientRetry = true,
                MaxRetryAttempts = 1,
                RetryableErrorCodes = new[] { "probe_failed", "timeout", "transport_unavailable" },
                RecoveryToolNames = new[] { "domaindetective_checks_catalog" }
            };
        }

        return definition.Recovery;
    }

    private static string ResolveRole(string toolName) {
        if (string.Equals(toolName, "domaindetective_pack_info", StringComparison.OrdinalIgnoreCase)) {
            return ToolRoutingTaxonomy.RolePackInfo;
        }

        if (string.Equals(toolName, "domaindetective_checks_catalog", StringComparison.OrdinalIgnoreCase)) {
            return ToolRoutingTaxonomy.RoleDiagnostic;
        }

        if (string.Equals(toolName, "domaindetective_network_probe", StringComparison.OrdinalIgnoreCase)) {
            return ToolRoutingTaxonomy.RoleDiagnostic;
        }

        return ToolRoutingTaxonomy.RoleOperational;
    }
}
