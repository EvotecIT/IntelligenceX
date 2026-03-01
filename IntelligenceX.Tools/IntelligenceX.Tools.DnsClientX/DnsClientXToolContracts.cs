using System;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.DnsClientX;

internal static class DnsClientXToolContracts {
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
            SetupToolName = "dnsclientx_ping",
            Requirements = new[] {
                new ToolSetupRequirement {
                    RequirementId = "dns_resolver_connectivity",
                    Kind = ToolSetupRequirementKinds.Connectivity,
                    IsRequired = true,
                    HintKeys = new[] { "endpoint", "timeout_ms" }
                }
            },
            SetupHintKeys = new[] { "target", "targets", "name", "type", "endpoint", "timeout_ms" }
        };
    }

    private static ToolHandoffContract? BuildHandoff(ToolDefinition definition) {
        if (string.Equals(definition.Name, "dnsclientx_query", StringComparison.OrdinalIgnoreCase)) {
            return new ToolHandoffContract {
                IsHandoffAware = true,
                OutboundRoutes = new[] {
                    new ToolHandoffRoute {
                        TargetPackId = "domaindetective",
                        TargetToolName = "domaindetective_domain_summary",
                        Reason = "Promote resolver-level DNS evidence into domain posture checks.",
                        Bindings = new[] {
                            new ToolHandoffBinding {
                                SourceField = "query/name",
                                TargetArgument = "domain",
                                IsRequired = true
                            },
                            new ToolHandoffBinding {
                                SourceField = "query/endpoint",
                                TargetArgument = "dns_endpoint",
                                IsRequired = false
                            }
                        }
                    }
                }
            };
        }

        if (string.Equals(definition.Name, "dnsclientx_ping", StringComparison.OrdinalIgnoreCase)) {
            return new ToolHandoffContract {
                IsHandoffAware = true,
                OutboundRoutes = new[] {
                    new ToolHandoffRoute {
                        TargetPackId = "domaindetective",
                        TargetToolName = "domaindetective_network_probe",
                        Reason = "Escalate host-level reachability checks to richer network probes when needed.",
                        Bindings = new[] {
                            new ToolHandoffBinding {
                                SourceField = "probed_targets/0",
                                TargetArgument = "host",
                                IsRequired = true
                            },
                            new ToolHandoffBinding {
                                SourceField = "timeout_ms",
                                TargetArgument = "timeout_ms",
                                IsRequired = false
                            }
                        }
                    }
                }
            };
        }

        return definition.Handoff;
    }

    private static ToolRecoveryContract? BuildRecovery(ToolDefinition definition) {
        if (definition.Recovery is { IsRecoveryAware: true }) {
            return definition.Recovery;
        }

        if (string.Equals(definition.Name, "dnsclientx_pack_info", StringComparison.OrdinalIgnoreCase)) {
            return definition.Recovery;
        }

        if (string.Equals(definition.Name, "dnsclientx_query", StringComparison.OrdinalIgnoreCase)) {
            return new ToolRecoveryContract {
                IsRecoveryAware = true,
                SupportsTransientRetry = true,
                MaxRetryAttempts = 2,
                RetryableErrorCodes = new[] { "timeout", "query_failed", "transport_unavailable" }
            };
        }

        if (string.Equals(definition.Name, "dnsclientx_ping", StringComparison.OrdinalIgnoreCase)) {
            return new ToolRecoveryContract {
                IsRecoveryAware = true,
                SupportsTransientRetry = false,
                MaxRetryAttempts = 0
            };
        }

        return definition.Recovery;
    }

    private static string ResolveRole(string toolName) {
        if (string.Equals(toolName, "dnsclientx_pack_info", StringComparison.OrdinalIgnoreCase)) {
            return ToolRoutingTaxonomy.RolePackInfo;
        }

        if (string.Equals(toolName, "dnsclientx_query", StringComparison.OrdinalIgnoreCase)) {
            return ToolRoutingTaxonomy.RoleResolver;
        }

        if (string.Equals(toolName, "dnsclientx_ping", StringComparison.OrdinalIgnoreCase)) {
            return ToolRoutingTaxonomy.RoleDiagnostic;
        }

        return ToolRoutingTaxonomy.RoleOperational;
    }
}
