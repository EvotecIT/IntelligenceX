using System;
using System.Collections.Generic;
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

    private static readonly string[] NamedEventCatalogSetupHintKeys = {
        "named_events",
        "categories",
        "machine_name",
        "machine_names"
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
        var execution = BuildExecution(definition, routing);
        var setup = BuildSetup(definition, routing);
        var handoff = BuildHandoff(definition);
        var recovery = BuildRecovery(definition, routing);
        var updatedDefinition = ToolDefinitionOverlay.WithContracts(
            definition: definition,
            execution: execution,
            routing: routing,
            setup: setup,
            handoff: handoff,
            recovery: recovery);
        return ToolDefinitionOverlay.WithDefinition(tool, updatedDefinition);
    }

    private static ToolExecutionContract? BuildExecution(ToolDefinition definition, ToolRoutingContract routing) {
        if (definition.Execution is { IsExecutionAware: true }) {
            return definition.Execution;
        }

        if (string.Equals(routing.Role, ToolRoutingTaxonomy.RolePackInfo, StringComparison.OrdinalIgnoreCase)) {
            return definition.Execution;
        }

        var traits = ToolExecutionTraitProjection.Project(definition);
        return new ToolExecutionContract {
            IsExecutionAware = true,
            ExecutionScope = traits.ExecutionScope,
            TargetScopeArguments = traits.TargetScopeArguments,
            RemoteHostArguments = traits.RemoteHostArguments
        };
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
            Role = ResolveRole(definition.Name, existing?.Role),
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

        if (string.Equals(definition.Name, "eventlog_named_events_query", StringComparison.OrdinalIgnoreCase)) {
            return new ToolSetupContract {
                IsSetupAware = true,
                SetupToolName = "eventlog_named_events_catalog",
                Requirements = new[] {
                    new ToolSetupRequirement {
                        RequirementId = "eventlog_named_event_catalog",
                        Kind = ToolSetupRequirementKinds.Capability,
                        IsRequired = true,
                        HintKeys = NamedEventCatalogSetupHintKeys
                    },
                    new ToolSetupRequirement {
                        RequirementId = "eventlog_channel_access",
                        Kind = ToolSetupRequirementKinds.Connectivity,
                        IsRequired = false,
                        HintKeys = SetupHintKeys
                    }
                },
                SetupHintKeys = MergeHintKeys(NamedEventCatalogSetupHintKeys, SetupHintKeys)
            };
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

    private static string[] MergeHintKeys(params IReadOnlyList<string>[] groups) {
        var values = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < groups.Length; i++) {
            var group = groups[i];
            if (group is null || group.Count == 0) {
                continue;
            }

            for (var j = 0; j < group.Count; j++) {
                var candidate = (group[j] ?? string.Empty).Trim();
                if (candidate.Length == 0 || !seen.Add(candidate)) {
                    continue;
                }

                values.Add(candidate);
            }
        }

        return values.ToArray();
    }

    private static ToolHandoffContract? BuildHandoff(ToolDefinition definition) {
        if (definition.Handoff is { IsHandoffAware: true }) {
            return definition.Handoff;
        }

        if (string.Equals(definition.Name, "eventlog_evtx_find", StringComparison.OrdinalIgnoreCase)) {
            return new ToolHandoffContract {
                IsHandoffAware = true,
                OutboundRoutes = new[] {
                    CreatePathHandoffRoute("eventlog", "eventlog_evtx_query", "files[].path"),
                    CreatePathHandoffRoute("eventlog", "eventlog_evtx_security_summary", "files[].path"),
                    CreatePathHandoffRoute("eventlog", "eventlog_evtx_stats", "files[].path")
                }
            };
        }

        if (string.Equals(definition.Name, "eventlog_evtx_security_summary", StringComparison.OrdinalIgnoreCase)) {
            return CreateAdEntityHandoffContract();
        }

        if (string.Equals(definition.Name, "eventlog_named_events_query", StringComparison.OrdinalIgnoreCase)
            || string.Equals(definition.Name, "eventlog_timeline_query", StringComparison.OrdinalIgnoreCase)) {
            return CreateEventLogQueryHandoffContract();
        }

        return definition.Handoff;
    }

    private static ToolHandoffContract CreateAdEntityHandoffContract() {
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
                }
            }
        };
    }

    private static ToolHandoffContract CreateEventLogQueryHandoffContract() {
        return new ToolHandoffContract {
            IsHandoffAware = true,
            OutboundRoutes = new[] {
                CreateAdEntityHandoffContract().OutboundRoutes[0],
                CreateAdEntityHandoffContract().OutboundRoutes[1],
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

    private static ToolHandoffRoute CreatePathHandoffRoute(string targetPackId, string targetToolName, string sourceField) {
        return new ToolHandoffRoute {
            TargetPackId = targetPackId,
            TargetToolName = targetToolName,
            Reason = "Promote discovered EVTX file paths into local follow-up analysis.",
            Bindings = new[] {
                new ToolHandoffBinding {
                    SourceField = sourceField,
                    TargetArgument = "path",
                    IsRequired = true
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

    private static string ResolveRole(string toolName, string? existingRole) {
        var inferredRole = TryResolveDeclaredRole(toolName);
        if (inferredRole.Length == 0) {
            return ToolRoutingRoleResolver.ResolveExplicitOrDeclared(
                explicitRole: existingRole,
                toolName: toolName,
                declaredRolesByToolName: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                packDisplayName: "EventLog");
        }

        return ToolRoutingRoleResolver.ResolveExplicitOrFallback(
            explicitRole: existingRole,
            fallbackRole: inferredRole,
            packDisplayName: "EventLog");
    }

    private static string TryResolveDeclaredRole(string toolName) {
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

        if (string.Equals(toolName, "eventlog_entity_handoff", StringComparison.OrdinalIgnoreCase)) {
            return ToolRoutingTaxonomy.RoleOperational;
        }

        return string.Empty;
    }
}
