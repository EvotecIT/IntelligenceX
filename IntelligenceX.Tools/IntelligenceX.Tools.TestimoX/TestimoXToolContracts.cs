using System;
using System.Collections.Generic;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.TestimoX;

internal static class TestimoXToolContracts {
    private const string SecurityPostureDomainIntentFamily = "security_posture";
    private const string SecurityPostureDomainIntentActionId = "act_domain_scope_security_posture";

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
        "security",
        "posture"
    };

    public static ITool Apply(ITool tool) {
        ArgumentNullException.ThrowIfNull(tool);

        var definition = tool.Definition;
        var routing = BuildRouting(definition);
        var setup = BuildSetup(definition, routing);
        var handoff = BuildHandoff(definition, routing);
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
            DomainIntentFamily = ResolveDomainIntentFamily(definition.Name, existing?.DomainIntentFamily),
            DomainIntentActionId = ResolveDomainIntentActionId(definition.Name, existing?.DomainIntentActionId),
            DomainSignalTokens = existing?.DomainSignalTokens.Count > 0 ? existing.DomainSignalTokens : TestimoXSignalTokens,
            RequiresSelectionForFallback = existing?.RequiresSelectionForFallback ?? false,
            FallbackSelectionKeys = existing?.FallbackSelectionKeys ?? Array.Empty<string>(),
            FallbackHintKeys = existing?.FallbackHintKeys ?? Array.Empty<string>()
        };
    }

    private static ToolSetupContract? BuildSetup(ToolDefinition definition, ToolRoutingContract routing) {
        if (string.Equals(routing.Role, ToolRoutingTaxonomy.RolePackInfo, StringComparison.OrdinalIgnoreCase)) {
            return definition.Setup;
        }

        if (string.Equals(definition.Name, "testimox_runs_list", StringComparison.OrdinalIgnoreCase)
            || string.Equals(definition.Name, "testimox_run_summary", StringComparison.OrdinalIgnoreCase)) {
            if (definition.Setup is { IsSetupAware: true }) {
                return definition.Setup;
            }

            return new ToolSetupContract {
                IsSetupAware = true,
                SetupHintKeys = definition.Routing?.FallbackHintKeys?.Count > 0
                    ? definition.Routing.FallbackHintKeys
                    : SetupHintKeys
            };
        }

        if (definition.Setup is { IsSetupAware: true }) {
            return definition.Setup;
        }

        return new ToolSetupContract {
            IsSetupAware = true,
            SetupToolName = "testimox_rules_list",
            Requirements = new[] {
                new ToolSetupRequirement {
                    RequirementId = "testimox_rules_catalog",
                    Kind = ToolSetupRequirementKinds.Capability,
                    IsRequired = true,
                    HintKeys = SetupHintKeys
                }
            },
            SetupHintKeys = SetupHintKeys
        };
    }

    private static ToolRecoveryContract? BuildRecovery(ToolDefinition definition, ToolRoutingContract routing) {
        if (definition.Recovery is { IsRecoveryAware: true }) {
            return definition.Recovery;
        }

        if (string.Equals(routing.Role, ToolRoutingTaxonomy.RolePackInfo, StringComparison.OrdinalIgnoreCase)) {
            return definition.Recovery;
        }

        var supportsRetry = string.Equals(definition.Name, "testimox_rules_run", StringComparison.OrdinalIgnoreCase);
        return new ToolRecoveryContract {
            IsRecoveryAware = true,
            SupportsTransientRetry = supportsRetry,
            MaxRetryAttempts = supportsRetry ? 1 : 0,
            RetryableErrorCodes = supportsRetry
                ? new[] { "execution_failed", "timeout", "transport_unavailable" }
                : Array.Empty<string>(),
            RecoveryToolNames = new[] { "testimox_rules_list" }
        };
    }

    private static ToolHandoffContract? BuildHandoff(ToolDefinition definition, ToolRoutingContract routing) {
        if (definition.Handoff is { IsHandoffAware: true }) {
            return definition.Handoff;
        }

        if (string.Equals(routing.Role, ToolRoutingTaxonomy.RolePackInfo, StringComparison.OrdinalIgnoreCase)) {
            return definition.Handoff;
        }

        if (string.Equals(definition.Name, "testimox_rules_run", StringComparison.OrdinalIgnoreCase)) {
            return CreateScopeAndHostFollowUpHandoff(
                domainSourceField: "include_domains/0",
                domainControllerSourceField: "include_domain_controllers/0",
                adReason: "Promote explicit TestimoX execution scope into AD scope discovery for the same domain/DC set.",
                systemReason: "Promote explicit TestimoX execution scope into ComputerX-backed remote host diagnostics for the same domain controller.");
        }

        if (string.Equals(definition.Name, "testimox_run_summary", StringComparison.OrdinalIgnoreCase)) {
            return CreateScopeAndHostFollowUpHandoff(
                domainSourceField: "rows/0/domain",
                domainControllerSourceField: "rows/0/domain_controller",
                adReason: "Promote stored TestimoX run scope into AD scope discovery before identity or ownership follow-up.",
                systemReason: "Promote stored TestimoX run domain-controller evidence into ComputerX-backed remote host diagnostics.");
        }

        return definition.Handoff;
    }

    private static ToolHandoffContract CreateScopeAndHostFollowUpHandoff(
        string domainSourceField,
        string domainControllerSourceField,
        string adReason,
        string systemReason) {
        return new ToolHandoffContract {
            IsHandoffAware = true,
            OutboundRoutes = new[] {
                new ToolHandoffRoute {
                    TargetPackId = "active_directory",
                    TargetToolName = "ad_scope_discovery",
                    Reason = adReason,
                    Bindings = new[] {
                        new ToolHandoffBinding {
                            SourceField = domainSourceField,
                            TargetArgument = "domain_name",
                            IsRequired = false
                        },
                        new ToolHandoffBinding {
                            SourceField = domainControllerSourceField,
                            TargetArgument = "domain_controller",
                            IsRequired = false
                        }
                    }
                },
                new ToolHandoffRoute {
                    TargetPackId = "system",
                    TargetToolName = "system_info",
                    Reason = systemReason,
                    Bindings = new[] {
                        new ToolHandoffBinding {
                            SourceField = domainControllerSourceField,
                            TargetArgument = "computer_name",
                            IsRequired = false
                        }
                    }
                },
                new ToolHandoffRoute {
                    TargetPackId = "system",
                    TargetToolName = "system_metrics_summary",
                    Reason = "Promote TestimoX scope evidence into remote CPU and memory follow-up for the same domain controller.",
                    Bindings = new[] {
                        new ToolHandoffBinding {
                            SourceField = domainControllerSourceField,
                            TargetArgument = "computer_name",
                            IsRequired = false
                        }
                    }
                },
                new ToolHandoffRoute {
                    TargetPackId = "eventlog",
                    TargetToolName = "eventlog_channels_list",
                    Reason = "Promote TestimoX scope evidence into remote Event Log channel discovery for the same domain controller before log triage.",
                    Bindings = new[] {
                        new ToolHandoffBinding {
                            SourceField = domainControllerSourceField,
                            TargetArgument = "machine_name",
                            IsRequired = false
                        }
                    }
                },
                new ToolHandoffRoute {
                    TargetPackId = "eventlog",
                    TargetToolName = "eventlog_live_stats",
                    Reason = "Promote TestimoX scope evidence into remote Event Log live statistics for the same domain controller.",
                    Bindings = new[] {
                        new ToolHandoffBinding {
                            SourceField = domainControllerSourceField,
                            TargetArgument = "machine_name",
                            IsRequired = false
                        }
                    }
                }
            }
        };
    }

    private static string ResolveDomainIntentFamily(string toolName, string? explicitFamily) {
        if (!string.IsNullOrWhiteSpace(explicitFamily)) {
            return explicitFamily!;
        }

        return string.Equals(toolName, "testimox_run_summary", StringComparison.OrdinalIgnoreCase)
               || string.Equals(toolName, "testimox_rules_run", StringComparison.OrdinalIgnoreCase)
            ? SecurityPostureDomainIntentFamily
            : string.Empty;
    }

    private static string ResolveDomainIntentActionId(string toolName, string? explicitActionId) {
        if (!string.IsNullOrWhiteSpace(explicitActionId)) {
            return explicitActionId!;
        }

        return string.Equals(toolName, "testimox_run_summary", StringComparison.OrdinalIgnoreCase)
               || string.Equals(toolName, "testimox_rules_run", StringComparison.OrdinalIgnoreCase)
            ? SecurityPostureDomainIntentActionId
            : string.Empty;
    }

    private static string ResolveRole(string toolName, string? explicitRole) {
        return ToolRoutingRoleResolver.ResolveExplicitOrDeclared(
            explicitRole: explicitRole,
            toolName: toolName,
            declaredRolesByToolName: DeclaredRolesByToolName,
            packDisplayName: "TestimoX");
    }
}
