using System;
using System.Collections.Generic;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

internal static class SystemToolContracts {
    private static readonly string[] SetupHintKeys = {
        "computer_name",
        "machine_name",
        "machine_names",
        "target"
    };

    private static readonly string[] SystemSignalTokens = {
        "system",
        "host",
        "computer",
        "process",
        "services",
        "firewall",
        "patch",
        "wsl"
    };

    public static ITool Apply(ITool tool) {
        ArgumentNullException.ThrowIfNull(tool);

        var definition = tool.Definition;
        var routing = BuildRouting(definition);
        var execution = BuildExecution(definition, routing);
        var setup = BuildSetup(definition, routing);
        var handoff = BuildHandoff(definition, routing);
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
            PackId = "system",
            Role = ResolveRole(definition.Name, existing?.Role),
            DomainIntentFamily = existing?.DomainIntentFamily ?? string.Empty,
            DomainIntentActionId = existing?.DomainIntentActionId ?? string.Empty,
            DomainSignalTokens = existing?.DomainSignalTokens.Count > 0 ? existing.DomainSignalTokens : SystemSignalTokens,
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
            SetupToolName = "system_info",
            Requirements = new[] {
                new ToolSetupRequirement {
                    RequirementId = "system_host_access",
                    Kind = ToolSetupRequirementKinds.Connectivity,
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

        var supportsAlternateEngines = ToolParametersExposeAlternateEngineSelector(definition.Parameters);
        return new ToolRecoveryContract {
            IsRecoveryAware = true,
            SupportsTransientRetry = true,
            MaxRetryAttempts = 1,
            RetryableErrorCodes = new[] { "timeout", "query_failed", "probe_failed", "access_denied", "transport_unavailable" },
            SupportsAlternateEngines = supportsAlternateEngines,
            AlternateEngineIds = supportsAlternateEngines ? new[] { "cim", "wmi" } : Array.Empty<string>(),
            RecoveryToolNames = new[] { "system_info" }
        };
    }

    private static ToolHandoffContract? BuildHandoff(ToolDefinition definition, ToolRoutingContract routing) {
        if (definition.Handoff is { IsHandoffAware: true }) {
            return definition.Handoff;
        }

        if (string.Equals(routing.Role, ToolRoutingTaxonomy.RolePackInfo, StringComparison.OrdinalIgnoreCase)
            || !ToolParametersExposeRemoteComputerName(definition.Parameters)) {
            return definition.Handoff;
        }

        return new ToolHandoffContract {
            IsHandoffAware = true,
            OutboundRoutes = new[] {
                CreateHostContextRoute(
                    targetPackId: "active_directory",
                    targetToolName: "ad_scope_discovery",
                    targetArgument: "domain_controller",
                    reason: "Reuse the same host as an AD scope or domain-controller hint when ComputerX evidence indicates directory follow-up."),
                CreateHostContextRoute(
                    targetPackId: "eventlog",
                    targetToolName: "eventlog_channels_list",
                    targetArgument: "machine_name",
                    reason: "Reuse the same host for remote Event Log channel discovery before live log triage.")
            }
        };
    }

    private static bool ToolParametersExposeAlternateEngineSelector(JsonObject? parameters) {
        var properties = parameters?.GetObject("properties");
        return properties is not null
               && ToolAlternateEngineSelectorNames.TryResolveSelectorArgumentName(properties, out _);
    }

    private static bool ToolParametersExposeRemoteComputerName(JsonObject? parameters) {
        return parameters?.GetObject("properties")?.GetObject("computer_name") is not null;
    }

    private static ToolHandoffRoute CreateHostContextRoute(
        string targetPackId,
        string targetToolName,
        string targetArgument,
        string reason) {
        return new ToolHandoffRoute {
            TargetPackId = targetPackId,
            TargetToolName = targetToolName,
            Reason = reason,
            Bindings = new[] {
                new ToolHandoffBinding {
                    SourceField = "meta/computer_name",
                    TargetArgument = targetArgument,
                    IsRequired = false
                },
                new ToolHandoffBinding {
                    SourceField = "computer_name",
                    TargetArgument = targetArgument,
                    IsRequired = false
                }
            }
        };
    }

    private static string ResolveRole(string toolName, string? existingRole) {
        var inferredRole = TryResolveDeclaredRole(toolName);
        if (inferredRole.Length == 0) {
            return ToolRoutingRoleResolver.ResolveExplicitOrDeclared(
                explicitRole: existingRole,
                toolName: toolName,
                declaredRolesByToolName: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                packDisplayName: "System");
        }

        return ToolRoutingRoleResolver.ResolveExplicitOrFallback(
            explicitRole: existingRole,
            fallbackRole: inferredRole,
            packDisplayName: "System");
    }

    private static string TryResolveDeclaredRole(string toolName) {
        if (string.Equals(toolName, "system_pack_info", StringComparison.OrdinalIgnoreCase)) {
            return ToolRoutingTaxonomy.RolePackInfo;
        }

        if (toolName.IndexOf("_list", StringComparison.OrdinalIgnoreCase) >= 0
            || toolName.IndexOf("_summary", StringComparison.OrdinalIgnoreCase) >= 0
            || toolName.IndexOf("_status", StringComparison.OrdinalIgnoreCase) >= 0
            || toolName.IndexOf("_posture", StringComparison.OrdinalIgnoreCase) >= 0
            || toolName.IndexOf("_compliance", StringComparison.OrdinalIgnoreCase) >= 0
            || toolName.IndexOf("_info", StringComparison.OrdinalIgnoreCase) >= 0
            || toolName.IndexOf("_identity", StringComparison.OrdinalIgnoreCase) >= 0
            || toolName.IndexOf("_details", StringComparison.OrdinalIgnoreCase) >= 0
            || toolName.IndexOf("_configuration", StringComparison.OrdinalIgnoreCase) >= 0
            || toolName.IndexOf("_updates", StringComparison.OrdinalIgnoreCase) >= 0
            || toolName.StartsWith("wsl_", StringComparison.OrdinalIgnoreCase)) {
            return ToolRoutingTaxonomy.RoleDiagnostic;
        }

        if (string.Equals(toolName, "system_whoami", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "system_time_sync", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "system_audit_options", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "system_security_options", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "system_boot_configuration", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "system_network_adapters", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "system_firewall_profiles", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "system_firewall_rules", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "system_installed_applications", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "system_builtin_accounts", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "system_local_identity_inventory", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "system_exploit_protection", StringComparison.OrdinalIgnoreCase)) {
            return ToolRoutingTaxonomy.RoleDiagnostic;
        }

        if (toolName.StartsWith("system_", StringComparison.OrdinalIgnoreCase)
            && toolName.IndexOf("unclassified", StringComparison.OrdinalIgnoreCase) < 0) {
            return ToolRoutingTaxonomy.RoleDiagnostic;
        }

        return string.Empty;
    }
}
