using System;
using IntelligenceX.Tools;

namespace IntelligenceX.Tools.Common.CrossPack;

/// <summary>
/// Shared catalog of System pack follow-up routes that accept a remote computer target.
/// </summary>
public static class SystemRemoteHostFollowUpCatalog {
    /// <summary>
    /// Stable route descriptors for System pack tools that accept a <c>computer_name</c> pivot.
    /// The first descriptor is reserved for the generic host-inspection route and may receive
    /// a caller-specific reason override via <see cref="CreateComputerTargetRoutes"/>.
    /// </summary>
    public static readonly (string TargetToolName, string Reason)[] ComputerTargetRouteDescriptors = {
        ("system_info", "Promote discovered host context into remote ComputerX host diagnostics."),
        ("system_time_sync", "Pivot into remote time-sync posture for discovered AD hosts when probe output points to NTP/time skew follow-up."),
        ("system_metrics_summary", "Pivot into remote memory/runtime telemetry for the discovered AD host."),
        ("system_hardware_summary", "Pivot into remote hardware inventory for the discovered AD host."),
        ("system_ports_list", "Pivot into remote listener and bound-port inspection for the discovered AD host."),
        ("system_service_list", "Pivot into remote Windows service inspection for the discovered AD host."),
        ("system_network_adapters", "Pivot into remote network-adapter and addressing inspection for the discovered AD host."),
        ("system_logical_disks_list", "Pivot into remote logical-disk inspection for the discovered AD host."),
        ("system_windows_update_client_status", "Pivot into remote low-privilege Windows Update/WSUS client status for the discovered AD host."),
        ("system_windows_update_telemetry", "Pivot into remote Windows Update freshness and reboot telemetry for the discovered AD host."),
        ("system_updates_installed", "Pivot into remote installed-update inventory when the discovered AD host needs exact patch presence follow-up."),
        ("system_patch_compliance", "Pivot into remote patch-compliance correlation when the discovered AD host needs installed-update coverage follow-up."),
        ("system_backup_posture", "Pivot into remote backup/recovery posture when the discovered AD host needs shadow-copy or restore coverage checks."),
        ("system_office_posture", "Pivot into remote Office macro/Protected View posture when the discovered AD host needs application hardening follow-up."),
        ("system_browser_posture", "Pivot into remote browser policy posture when the discovered AD host needs endpoint hardening follow-up."),
        ("system_tls_posture", "Pivot into remote TLS/SChannel posture when the discovered AD host needs protocol or cipher-hardening follow-up."),
        ("system_winrm_posture", "Pivot into remote WinRM posture when the discovered AD host needs remote-management hardening follow-up."),
        ("system_powershell_logging_posture", "Pivot into remote PowerShell logging posture when the discovered AD host needs script auditing or logging-policy follow-up."),
        ("system_uac_posture", "Pivot into remote UAC posture when the discovered AD host needs elevation-hardening follow-up."),
        ("system_ldap_policy_posture", "Pivot into remote LDAP signing/channel-binding posture when the discovered AD host needs host policy follow-up."),
        ("system_network_client_posture", "Pivot into remote network-client hardening posture when the discovered AD host needs name-resolution or redirect-policy follow-up."),
        ("system_account_policy_posture", "Pivot into remote account password/lockout posture when the discovered AD host needs effective host account-policy follow-up."),
        ("system_interactive_logon_posture", "Pivot into remote interactive logon posture when the discovered AD host needs console-logon policy follow-up."),
        ("system_device_guard_posture", "Pivot into remote Device Guard posture when the discovered AD host needs virtualization-security follow-up."),
        ("system_defender_asr_posture", "Pivot into remote Defender ASR posture when the discovered AD host needs host attack-surface reduction follow-up."),
        ("system_certificate_posture", "Pivot into remote certificate-store posture only when the follow-up is about machine certificate stores or trust-store posture on the discovered AD host.")
    };

    /// <summary>
    /// Builds the standard System pack remote-host follow-up routes with caller-provided source fields.
    /// </summary>
    public static ToolHandoffRoute[] CreateComputerTargetRoutes(
        IReadOnlyList<string> sourceFields,
        string? primaryReasonOverride = null,
        bool isRequired = false) {
        var descriptors = new (string TargetToolName, string Reason)[ComputerTargetRouteDescriptors.Length];
        descriptors[0] = (
            ComputerTargetRouteDescriptors[0].TargetToolName,
            string.IsNullOrWhiteSpace(primaryReasonOverride)
                ? ComputerTargetRouteDescriptors[0].Reason
                : primaryReasonOverride!);
        for (var i = 1; i < ComputerTargetRouteDescriptors.Length; i++) {
            descriptors[i] = ComputerTargetRouteDescriptors[i];
        }

        return ToolContractDefaults.CreateSharedTargetRoutes(
            targetPackId: "system",
            targetArgument: "computer_name",
            sourceFields: sourceFields,
            routeDescriptors: descriptors,
            isRequired: isRequired);
    }

    /// <summary>
    /// Builds a focused subset of System pack remote-host follow-up routes by tool name.
    /// </summary>
    public static ToolHandoffRoute[] CreateSelectedComputerTargetRoutes(
        IReadOnlyList<string> sourceFields,
        IReadOnlyList<(string TargetToolName, string? ReasonOverride)> routeSelections,
        bool isRequired = false) {
        if (routeSelections is null || routeSelections.Count == 0) {
            return Array.Empty<ToolHandoffRoute>();
        }

        var selectedDescriptors = new (string TargetToolName, string Reason)[routeSelections.Count];
        for (var i = 0; i < routeSelections.Count; i++) {
            var selection = routeSelections[i];
            var descriptor = FindDescriptor(selection.TargetToolName);
            selectedDescriptors[i] = (
                descriptor.TargetToolName,
                string.IsNullOrWhiteSpace(selection.ReasonOverride)
                    ? descriptor.Reason
                    : selection.ReasonOverride!);
        }

        return ToolContractDefaults.CreateSharedTargetRoutes(
            targetPackId: "system",
            targetArgument: "computer_name",
            sourceFields: sourceFields,
            routeDescriptors: selectedDescriptors,
            isRequired: isRequired);
    }

    private static (string TargetToolName, string Reason) FindDescriptor(string targetToolName) {
        for (var i = 0; i < ComputerTargetRouteDescriptors.Length; i++) {
            var descriptor = ComputerTargetRouteDescriptors[i];
            if (string.Equals(descriptor.TargetToolName, targetToolName, StringComparison.OrdinalIgnoreCase)) {
                return descriptor;
            }
        }

        throw new InvalidOperationException(
            $"System remote-host follow-up route '{targetToolName}' is not declared in {nameof(SystemRemoteHostFollowUpCatalog)}.");
    }
}
