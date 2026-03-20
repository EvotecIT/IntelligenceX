using System;
using System.Collections.Generic;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

internal static class SystemRoutingCatalog {
    private static readonly IReadOnlyDictionary<string, string> DeclaredRolesByToolName = BuildDeclaredRolesByToolName();

    private static readonly IReadOnlyDictionary<string, SelectionDescriptor> ExplicitSelectionDescriptors =
        new Dictionary<string, SelectionDescriptor>(StringComparer.OrdinalIgnoreCase) {
            ["system_connectivity_probe"] = new(
                Scope: "host",
                Operation: "probe",
                Entity: "host",
                Risk: ToolRoutingTaxonomy.RiskLow,
                AdditionalTags: new[] { "probe", "preflight", "connectivity" }),
            ["system_info"] = new(
                Scope: "host",
                Operation: ToolRoutingTaxonomy.OperationRead,
                Entity: "host",
                Risk: ToolRoutingTaxonomy.RiskLow,
                AdditionalTags: new[] { "inventory", "baseline" }),
            ["system_service_lifecycle"] = new(
                Scope: "host",
                Operation: "write",
                Entity: "service",
                Risk: ToolRoutingTaxonomy.RiskHigh,
                AdditionalTags: new[] { "service", "governed_write", "lifecycle" }),
            ["system_scheduled_task_lifecycle"] = new(
                Scope: "host",
                Operation: "write",
                Entity: "scheduled_task",
                Risk: ToolRoutingTaxonomy.RiskHigh,
                AdditionalTags: new[] { "scheduled_task", "task_scheduler", "governed_write", "lifecycle" })
        };

    public static readonly IReadOnlyList<string> SignalTokens = new[] {
        "system",
        "host",
        "computer",
        "process",
        "services",
        "scheduled_tasks",
        "firewall",
        "patch",
        "wsl"
    };

    public static ToolDefinition ApplySelectionMetadata(ToolDefinition definition) {
        ArgumentNullException.ThrowIfNull(definition);

        return ExplicitSelectionDescriptors.TryGetValue((definition.Name ?? string.Empty).Trim(), out var descriptor)
            ? ToolExplicitSelectionMetadata.Apply(
                definition,
                scope: descriptor.Scope,
                operation: descriptor.Operation,
                entity: descriptor.Entity,
                risk: descriptor.Risk,
                additionalTags: descriptor.AdditionalTags)
            : definition;
    }

    public static string ResolveRole(string toolName, string? explicitRole) {
        return ToolRoutingRoleResolver.ResolveExplicitOrDeclared(
            explicitRole: explicitRole,
            toolName: toolName,
            declaredRolesByToolName: DeclaredRolesByToolName,
            packDisplayName: "System");
    }

    private static IReadOnlyDictionary<string, string> BuildDeclaredRolesByToolName() {
        var declared = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        AddRoleGroup(declared, ToolRoutingTaxonomy.RolePackInfo,
            "system_pack_info");
        AddRoleGroup(declared, ToolRoutingTaxonomy.RoleDiagnostic,
            "system_connectivity_probe",
            "system_info",
            "system_hardware_identity",
            "system_process_list",
            "system_patch_details",
            "system_ports_list",
            "system_service_list",
            "system_scheduled_tasks_list",
            "system_rdp_posture",
            "system_smb_posture",
            "system_boot_configuration",
            "system_bios_summary",
            "system_bitlocker_status",
            "system_privacy_posture",
            "system_office_posture",
            "system_browser_posture",
            "system_backup_posture",
            "system_certificate_posture",
            "system_credential_posture",
            "system_tls_posture",
            "system_winrm_posture",
            "system_powershell_logging_posture",
            "system_platform_security_posture",
            "system_app_control_posture",
            "system_remote_access_posture",
            "system_uac_posture",
            "system_ldap_policy_posture",
            "system_network_client_posture",
            "system_account_policy_posture",
            "system_interactive_logon_posture",
            "system_device_guard_posture",
            "system_defender_asr_posture",
            "system_updates_installed",
            "system_windows_update_client_status",
            "system_patch_compliance",
            "system_logical_disks_list",
            "system_disks_list",
            "system_devices_summary",
            "system_hardware_summary",
            "system_metrics_summary",
            "system_features_list",
            "wsl_status");
        AddRoleGroup(declared, ToolRoutingTaxonomy.RoleOperational,
            "system_whoami",
            "system_network_adapters",
            "system_service_lifecycle",
            "system_scheduled_task_lifecycle",
            "system_firewall_rules",
            "system_firewall_profiles",
            "system_security_options",
            "system_time_sync",
            "system_local_identity_inventory",
            "system_exploit_protection",
            "system_audit_options",
            "system_builtin_accounts",
            "system_installed_applications",
            "system_windows_update_telemetry");
        return declared;
    }

    private static void AddRoleGroup(
        IDictionary<string, string> declared,
        string role,
        params string[] toolNames) {
        foreach (var toolName in toolNames) {
            declared[toolName] = role;
        }
    }

    private sealed record SelectionDescriptor(
        string Scope,
        string Operation,
        string Entity,
        string Risk,
        string[] AdditionalTags);
}
