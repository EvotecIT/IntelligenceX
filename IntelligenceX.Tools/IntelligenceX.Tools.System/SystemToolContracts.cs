using System;
using System.Collections.Generic;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

internal static class SystemToolContracts {
    private static readonly IReadOnlyDictionary<string, string> DeclaredRolesByToolName =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            ["system_pack_info"] = ToolRoutingTaxonomy.RolePackInfo,
            ["system_info"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["system_hardware_identity"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["system_whoami"] = ToolRoutingTaxonomy.RoleOperational,
            ["system_process_list"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["system_network_adapters"] = ToolRoutingTaxonomy.RoleOperational,
            ["system_patch_details"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["system_ports_list"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["system_service_list"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["system_scheduled_tasks_list"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["system_firewall_rules"] = ToolRoutingTaxonomy.RoleOperational,
            ["system_firewall_profiles"] = ToolRoutingTaxonomy.RoleOperational,
            ["system_security_options"] = ToolRoutingTaxonomy.RoleOperational,
            ["system_rdp_posture"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["system_smb_posture"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["system_boot_configuration"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["system_bios_summary"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["system_time_sync"] = ToolRoutingTaxonomy.RoleOperational,
            ["system_local_identity_inventory"] = ToolRoutingTaxonomy.RoleOperational,
            ["system_bitlocker_status"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["system_privacy_posture"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["system_exploit_protection"] = ToolRoutingTaxonomy.RoleOperational,
            ["system_office_posture"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["system_browser_posture"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["system_backup_posture"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["system_certificate_posture"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["system_credential_posture"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["system_tls_posture"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["system_winrm_posture"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["system_powershell_logging_posture"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["system_platform_security_posture"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["system_app_control_posture"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["system_remote_access_posture"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["system_uac_posture"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["system_ldap_policy_posture"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["system_network_client_posture"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["system_account_policy_posture"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["system_interactive_logon_posture"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["system_audit_options"] = ToolRoutingTaxonomy.RoleOperational,
            ["system_builtin_accounts"] = ToolRoutingTaxonomy.RoleOperational,
            ["system_device_guard_posture"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["system_defender_asr_posture"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["system_installed_applications"] = ToolRoutingTaxonomy.RoleOperational,
            ["system_updates_installed"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["system_windows_update_client_status"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["system_windows_update_telemetry"] = ToolRoutingTaxonomy.RoleOperational,
            ["system_patch_compliance"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["system_logical_disks_list"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["system_disks_list"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["system_devices_summary"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["system_hardware_summary"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["system_metrics_summary"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["system_features_list"] = ToolRoutingTaxonomy.RoleDiagnostic,
            ["wsl_status"] = ToolRoutingTaxonomy.RoleDiagnostic
        };

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

    private static string ResolveRole(string toolName, string? explicitRole) {
        return ToolRoutingRoleResolver.ResolveExplicitOrDeclared(
            explicitRole: explicitRole,
            toolName: toolName,
            declaredRolesByToolName: DeclaredRolesByToolName,
            packDisplayName: "System");
    }
}
