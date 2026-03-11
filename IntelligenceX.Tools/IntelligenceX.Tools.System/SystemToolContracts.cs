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
            ["system_local_identity_inventory"] = ToolRoutingTaxonomy.RoleDiagnostic,
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
        var setup = BuildSetup(definition, routing);
        var recovery = BuildRecovery(definition, routing);
        var updatedDefinition = ToolDefinitionOverlay.WithContracts(
            definition: definition,
            routing: routing,
            setup: setup,
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
            PackId = "system",
            Role = ResolveDeclaredRole(definition.Name, existing?.Role),
            DomainIntentFamily = existing?.DomainIntentFamily ?? string.Empty,
            DomainIntentActionId = existing?.DomainIntentActionId ?? string.Empty,
            DomainSignalTokens = existing?.DomainSignalTokens.Count > 0 ? existing.DomainSignalTokens : SystemSignalTokens,
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
                setupToolName: "system_info",
                requirementId: "system_host_access",
                requirementKind: ToolSetupRequirementKinds.Connectivity,
                setupHintKeys: SetupHintKeys));
    }

    private static ToolRecoveryContract? BuildRecovery(ToolDefinition definition, ToolRoutingContract routing) {
        return ToolContractDefaults.PreserveExplicitRecoveryOrCreateDefault(
            definition,
            routing.Role,
            () => {
                var supportsAlternateEngines = ToolParametersExposeAlternateEngineSelector(definition.Parameters);
                return ToolContractDefaults.CreateRecovery(
                    supportsTransientRetry: true,
                    maxRetryAttempts: 1,
                    retryableErrorCodes: new[] { "timeout", "query_failed", "probe_failed", "access_denied", "transport_unavailable" },
                    recoveryToolNames: new[] { "system_info" },
                    supportsAlternateEngines: supportsAlternateEngines,
                    alternateEngineIds: supportsAlternateEngines ? new[] { "cim", "wmi" } : null);
            });
    }

    private static bool ToolParametersExposeAlternateEngineSelector(JsonObject? parameters) {
        var properties = parameters?.GetObject("properties");
        return properties is not null
               && ToolAlternateEngineSelectorNames.TryResolveSelectorArgumentName(properties, out _);
    }

    private static string ResolveDeclaredRole(string toolName, string? existingRole) {
        var normalizedExistingRole = NormalizeRole(existingRole);
        if (normalizedExistingRole.Length > 0) {
            return normalizedExistingRole;
        }

        if (DeclaredRolesByToolName.TryGetValue(toolName, out var declaredRole)) {
            return declaredRole;
        }

        throw new InvalidOperationException(
            $"System tool '{toolName}' must declare an explicit routing role in SystemToolContracts or ToolDefinition.Routing.Role.");
    }

    private static string NormalizeRole(string? role) {
        var normalized = (role ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return string.Empty;
        }

        normalized = normalized.ToLowerInvariant();
        if (!ToolRoutingTaxonomy.IsAllowedRole(normalized)) {
            throw new InvalidOperationException(
                $"System tool routing role '{role}' is invalid. Allowed values: {string.Join(", ", ToolRoutingTaxonomy.AllowedRoles)}.");
        }

        return normalized;
    }
}
