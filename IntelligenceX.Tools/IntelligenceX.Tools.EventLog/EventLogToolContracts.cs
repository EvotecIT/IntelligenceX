using System;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.EventLog;

internal static class EventLogToolContracts {
    private static readonly string[] SetupHintKeys = {
        "machine_name",
        "machine_names",
        "channel",
        "channels",
        "evtx_path",
        "path"
    };

    private static readonly string[] EventLogSignalTokens = {
        "eventlog",
        "security",
        "kerberos",
        "gpo",
        "ad_domain",
        "dc"
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
            PackId = "eventlog",
            Role = ResolveRole(definition.Name),
            DomainIntentFamily = string.IsNullOrWhiteSpace(existing?.DomainIntentFamily)
                ? ToolSelectionMetadata.DomainIntentFamilyAd
                : existing!.DomainIntentFamily,
            DomainIntentActionId = string.IsNullOrWhiteSpace(existing?.DomainIntentActionId)
                ? ToolSelectionMetadata.DomainIntentActionIdAd
                : existing!.DomainIntentActionId,
            DomainSignalTokens = existing?.DomainSignalTokens.Count > 0 ? existing.DomainSignalTokens : EventLogSignalTokens,
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
            SetupToolName = "eventlog_channels_list",
            Requirements = new[] {
                new ToolSetupRequirement {
                    RequirementId = "eventlog_channel_access",
                    Kind = ToolSetupRequirementKinds.Connectivity,
                    IsRequired = true,
                    HintKeys = SetupHintKeys
                }
            },
            SetupHintKeys = SetupHintKeys
        };
    }

    private static ToolHandoffContract? BuildHandoff(ToolDefinition definition) {
        if (string.Equals(definition.Name, "eventlog_named_events_query", StringComparison.OrdinalIgnoreCase)
            || string.Equals(definition.Name, "eventlog_timeline_query", StringComparison.OrdinalIgnoreCase)) {
            return new ToolHandoffContract {
                IsHandoffAware = true,
                OutboundRoutes = new[] {
                    new ToolHandoffRoute {
                        TargetPackId = "active_directory",
                        TargetToolName = "ad_handoff_prepare",
                        Reason = "Promote EventLog entity handoff payload into AD identity normalization before lookups.",
                        Bindings = new[] {
                            new ToolHandoffBinding {
                                SourceField = "meta/entity_handoff",
                                TargetArgument = "entity_handoff",
                                IsRequired = true
                            }
                        }
                    },
                    new ToolHandoffRoute {
                        TargetPackId = "active_directory",
                        TargetToolName = "ad_scope_discovery",
                        Reason = "Seed AD scope discovery with host evidence from EventLog query context.",
                        Bindings = new[] {
                            new ToolHandoffBinding {
                                SourceField = "meta/entity_handoff/computer_candidates/0/value",
                                TargetArgument = "domain_controller",
                                IsRequired = false
                            }
                        }
                    },
                    new ToolHandoffRoute {
                        TargetPackId = "system",
                        TargetToolName = "system_info",
                        Reason = "Pivot correlated Event Log host evidence into ComputerX-backed system context collection for the same remote machine.",
                        Bindings = new[] {
                            new ToolHandoffBinding {
                                SourceField = "meta/entity_handoff/computer_candidates/0/value",
                                TargetArgument = "computer_name",
                                IsRequired = false
                            }
                        }
                    },
                    new ToolHandoffRoute {
                        TargetPackId = "system",
                        TargetToolName = "system_metrics_summary",
                        Reason = "Pivot correlated Event Log host evidence into remote CPU and memory checks for the same machine.",
                        Bindings = new[] {
                            new ToolHandoffBinding {
                                SourceField = "meta/entity_handoff/computer_candidates/0/value",
                                TargetArgument = "computer_name",
                                IsRequired = false
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

        var supportsRetry = definition.Name.IndexOf("_query", StringComparison.OrdinalIgnoreCase) >= 0
                            || definition.Name.IndexOf("_find", StringComparison.OrdinalIgnoreCase) >= 0
                            || definition.Name.IndexOf("_top_events", StringComparison.OrdinalIgnoreCase) >= 0;

        return new ToolRecoveryContract {
            IsRecoveryAware = true,
            SupportsTransientRetry = supportsRetry,
            MaxRetryAttempts = supportsRetry ? 1 : 0,
            RetryableErrorCodes = supportsRetry
                ? new[] { "timeout", "query_failed", "probe_failed", "transport_unavailable" }
                : Array.Empty<string>(),
            RecoveryToolNames = new[] { "eventlog_channels_list" }
        };
    }

    private static string ResolveRole(string toolName) {
        if (string.Equals(toolName, "eventlog_pack_info", StringComparison.OrdinalIgnoreCase)) {
            return ToolRoutingTaxonomy.RolePackInfo;
        }

        if (toolName.IndexOf("_catalog", StringComparison.OrdinalIgnoreCase) >= 0
            || toolName.IndexOf("_list", StringComparison.OrdinalIgnoreCase) >= 0
            || toolName.IndexOf("_stats", StringComparison.OrdinalIgnoreCase) >= 0
            || toolName.IndexOf("_explain", StringComparison.OrdinalIgnoreCase) >= 0) {
            return ToolRoutingTaxonomy.RoleDiagnostic;
        }

        if (toolName.IndexOf("_query", StringComparison.OrdinalIgnoreCase) >= 0
            || toolName.IndexOf("_find", StringComparison.OrdinalIgnoreCase) >= 0
            || toolName.IndexOf("_top_events", StringComparison.OrdinalIgnoreCase) >= 0
            || toolName.IndexOf("_security_summary", StringComparison.OrdinalIgnoreCase) >= 0) {
            return ToolRoutingTaxonomy.RoleResolver;
        }

        return ToolRoutingTaxonomy.RoleOperational;
    }
}
