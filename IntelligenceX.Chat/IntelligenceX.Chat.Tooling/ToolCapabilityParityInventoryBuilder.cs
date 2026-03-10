using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ADPlayground.Monitoring.Diagnostics;
using ADPlayground.Monitoring.History;
using ADPlayground.Monitoring.Probes;
using ADPlayground.Monitoring.Probes.DirectoryHealth;
using ComputerX.Bios;
using ComputerX.Boot;
using ComputerX.Certificates;
using ComputerX.Credentials;
using ComputerX.Diagnostics;
using ComputerX.Devices;
using ComputerX.Features;
using ComputerX.Firewall;
using ComputerX.Hardware;
using ComputerX.Identity;
using ComputerX.InstalledApplications;
using ComputerX.Network;
using ComputerX.Office;
using ComputerX.Ports;
using ComputerX.Processes;
using ComputerX.Privacy;
using ComputerX.Rdp;
using ComputerX.Runtime;
using ComputerX.ScheduledTasks;
using ComputerX.Security.BitLocker;
using ComputerX.SecurityPolicy;
using ComputerX.Services;
using ComputerX.Smb;
using ComputerX.Storage;
using ComputerX.Time;
using ComputerX.Updates;
using ComputerX.ExploitProtection;
using ComputerX.Browsers;
using ComputerX.Backup;
using ComputerX.AppControl;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using TestimoX.Baselines;
using TestimoX.Baselines.Crosswalk;
using TestimoX.Execution;
using TestimoX.Providers;
using ADPlayground.Monitoring.Reporting;
using ComputerX.PlatformSecurity;
using ComputerX.RemoteAccess;

namespace IntelligenceX.Chat.Tooling;

/// <summary>
/// Builds a runtime parity inventory from live tool registration and upstream engine contracts.
/// </summary>
public static class ToolCapabilityParityInventoryBuilder {
    /// <summary>
    /// Status used when the current runtime surface matches the phase-1 parity slice.
    /// </summary>
    public const string HealthyStatus = "healthy";

    /// <summary>
    /// Status used when upstream read-only capabilities are still missing from the live IX surface.
    /// </summary>
    public const string GapStatus = "parity_gap";

    /// <summary>
    /// Status used when a capability family is intentionally kept outside autonomous phase-1 execution.
    /// </summary>
    public const string GovernedBacklogStatus = "governed_backlog";

    /// <summary>
    /// Status used when upstream source metadata was not available for inspection in this runtime.
    /// </summary>
    public const string SourceUnavailableStatus = "source_unavailable";

    /// <summary>
    /// Status used when the associated pack is not currently surfaced in the active runtime.
    /// </summary>
    public const string PackUnavailableStatus = "pack_unavailable";

    private const int MaxMissingCapabilities = 12;
    private static readonly CapabilityExpectation[] ComputerXReadOnlyExpectations = {
        CapabilityExpectation.ForRemoteTool("remote_runtime_summary", "system_info", static () => HasComputerNameProperty<SystemRuntimeQueryRequest>()),
        CapabilityExpectation.ForRemoteTool("remote_process_inventory", "system_process_list", static () => HasComputerNameProperty<ProcessListQueryRequest>()),
        CapabilityExpectation.ForRemoteTool("remote_network_adapter_inventory", "system_network_adapters", static () => HasComputerNameProperty<NetworkAdapterInventoryQueryRequest>()),
        CapabilityExpectation.ForRemoteTool("remote_port_inventory", "system_ports_list", static () => HasComputerNameProperty<PortInventoryQueryRequest>()),
        CapabilityExpectation.ForRemoteTool("remote_service_inventory", "system_service_list", static () => HasComputerNameProperty<ServiceListQueryRequest>()),
        CapabilityExpectation.ForRemoteTool("remote_scheduled_task_inventory", "system_scheduled_tasks_list", static () => HasComputerNameProperty<TaskSchedulerListQueryRequest>()),
        CapabilityExpectation.ForRemoteTool("remote_firewall_rule_inventory", "system_firewall_rules", static () => HasComputerNameProperty<FirewallRuleListQueryRequest>()),
        CapabilityExpectation.ForRemoteTool("remote_firewall_profile_inventory", "system_firewall_profiles", static () => HasComputerNameProperty<FirewallProfileListQueryRequest>()),
        CapabilityExpectation.ForRemoteTool("remote_local_identity_inventory", "system_local_identity_inventory", static () => HasComputerNameProperty<LocalIdentityInventoryQueryRequest>()),
        CapabilityExpectation.ForRemoteTool("remote_security_options", "system_security_options", static () => HasStaticMethod(typeof(SecurityOptionsQuery), "Get")),
        CapabilityExpectation.ForRemoteTool("remote_rdp_posture", "system_rdp_posture", static () => HasStaticMethod(typeof(RdpPolicyQuery), "Get")),
        CapabilityExpectation.ForRemoteTool("remote_smb_posture", "system_smb_posture", static () => HasStaticMethod(typeof(SmbConfigQuery), "Get")),
        CapabilityExpectation.ForRemoteTool("remote_boot_configuration", "system_boot_configuration", static () => HasStaticMethod(typeof(BootOptionsQuery), "Query")),
        CapabilityExpectation.ForRemoteTool("remote_bios_summary", "system_bios_summary", static () => HasStaticMethod(typeof(Bios), "Get") || HasStaticMethod(typeof(Bios), "GetAsync")),
        CapabilityExpectation.ForRemoteTool("remote_time_sync", "system_time_sync", static () => HasRemoteQueryMethod(typeof(TimeSync), "QueryRemoteStatusAsync")),
        CapabilityExpectation.ForRemoteTool("remote_bitlocker_status", "system_bitlocker_status", static () => HasStaticMethod(typeof(BitLocker), "Get") || HasStaticMethod(typeof(BitLocker), "GetAsync")),
        CapabilityExpectation.ForRemoteTool("remote_privacy_posture", "system_privacy_posture", static () => HasStaticMethod(typeof(PrivacyPosture), "Get") || HasStaticMethod(typeof(PrivacyPosture), "GetAsync")),
        CapabilityExpectation.ForRemoteTool("remote_exploit_protection", "system_exploit_protection", static () => HasStaticMethod(typeof(ExploitProtection), "Get") || HasStaticMethod(typeof(ExploitProtection), "GetAsync")),
        CapabilityExpectation.ForRemoteTool("remote_office_posture", "system_office_posture", static () => HasStaticMethod(typeof(OfficePosture), "Get") || HasStaticMethod(typeof(OfficePosture), "GetAsync")),
        CapabilityExpectation.ForRemoteTool("remote_browser_posture", "system_browser_posture", static () => HasStaticMethod(typeof(BrowserPosture), "Get") || HasStaticMethod(typeof(BrowserPosture), "GetAsync")),
        CapabilityExpectation.ForRemoteTool("remote_backup_posture", "system_backup_posture", static () => HasStaticMethod(typeof(Backup), "Get") || HasStaticMethod(typeof(Backup), "GetAsync")),
        CapabilityExpectation.ForRemoteTool("remote_tls_posture", "system_tls_posture", static () => HasStaticMethod(typeof(TlsPolicyQuery), "Get")),
        CapabilityExpectation.ForRemoteTool("remote_winrm_posture", "system_winrm_posture", static () => HasStaticMethod(typeof(WinRmPolicyQuery), "Get")),
        CapabilityExpectation.ForRemoteTool("remote_powershell_logging_posture", "system_powershell_logging_posture", static () => HasStaticMethod(typeof(PsLoggingPolicyQuery), "Get")),
        CapabilityExpectation.ForRemoteTool("remote_platform_security_posture", "system_platform_security_posture", static () => HasStaticMethod(typeof(global::ComputerX.PlatformSecurity.PlatformSecurity), "Get") || HasStaticMethod(typeof(global::ComputerX.PlatformSecurity.PlatformSecurity), "GetAsync")),
        CapabilityExpectation.ForRemoteTool("remote_app_control_posture", "system_app_control_posture", static () => HasStaticMethod(typeof(AppControl), "Get") || HasStaticMethod(typeof(AppControl), "GetAsync")),
        CapabilityExpectation.ForRemoteTool("remote_remote_access_posture", "system_remote_access_posture", static () => HasStaticMethod(typeof(RemoteAccess), "Get") || HasStaticMethod(typeof(RemoteAccess), "GetAsync")),
        CapabilityExpectation.ForRemoteTool("remote_uac_posture", "system_uac_posture", static () => HasStaticMethod(typeof(UacPolicyQuery), "Get")),
        CapabilityExpectation.ForRemoteTool("remote_ldap_policy_posture", "system_ldap_policy_posture", static () => HasStaticMethod(typeof(LdapPolicyQuery), "GetClient") && HasStaticMethod(typeof(LdapPolicyQuery), "GetServer")),
        CapabilityExpectation.ForRemoteTool("remote_network_client_posture", "system_network_client_posture", static () => HasStaticMethod(typeof(NetworkClientPolicyQuery), "Get")),
        CapabilityExpectation.ForRemoteTool("remote_account_policy_posture", "system_account_policy_posture", static () => HasStaticMethod(typeof(AccountPolicyQuery), "Get")),
        CapabilityExpectation.ForRemoteTool("remote_interactive_logon_posture", "system_interactive_logon_posture", static () => HasStaticMethod(typeof(InteractiveLogonPolicyQuery), "Get")),
        CapabilityExpectation.ForRemoteTool("remote_device_guard_posture", "system_device_guard_posture", static () => HasStaticMethod(typeof(DeviceGuardPolicyQuery), "Get")),
        CapabilityExpectation.ForRemoteTool("remote_defender_asr_posture", "system_defender_asr_posture", static () => HasStaticMethod(typeof(DefenderAsrPolicyQuery), "Get")),
        CapabilityExpectation.ForRemoteTool("remote_windows_update_client_status", "system_windows_update_client_status", static () => HasStaticMethod(typeof(WindowsUpdateClientStatusQuery), "Get")),
        CapabilityExpectation.ForRemoteTool("remote_windows_update_telemetry", "system_windows_update_telemetry", static () => HasStaticMethod(typeof(WindowsUpdateTelemetryQuery), "Get")),
        CapabilityExpectation.ForRemoteTool("remote_certificate_posture", "system_certificate_posture", static () => HasStaticMethod(typeof(CertificatePosture), "Get") || HasStaticMethod(typeof(CertificatePosture), "GetAsync")),
        CapabilityExpectation.ForRemoteTool("remote_credential_posture", "system_credential_posture", static () => HasStaticMethod(typeof(CredentialPosture), "Get") || HasStaticMethod(typeof(CredentialPosture), "GetAsync")),
        CapabilityExpectation.ForRemoteTool("remote_installed_applications", "system_installed_applications", static () => HasStaticMethod(typeof(InstalledApplications), "Query")),
        CapabilityExpectation.ForRemoteTool("remote_updates_installed", "system_updates_installed", static () => HasStaticMethod(typeof(Updates), "GetInstalledAsync")),
        CapabilityExpectation.ForRemoteTool("remote_patch_compliance", "system_patch_compliance", static () => HasStaticMethod(typeof(Updates), "GetInstalledAsync")),
        CapabilityExpectation.ForRemoteTool("remote_logical_disks", "system_logical_disks_list", static () => HasComputerNameProperty<LogicalDiskInventoryQueryRequest>()),
        CapabilityExpectation.ForRemoteTool("remote_disk_inventory", "system_disks_list", static () => HasComputerNameProperty<DiskInventoryQueryRequest>()),
        CapabilityExpectation.ForRemoteTool("remote_device_inventory", "system_devices_summary", static () => HasComputerNameProperty<DeviceInventoryQueryRequest>()),
        CapabilityExpectation.ForRemoteTool("remote_hardware_summary", "system_hardware_summary", static () => HasComputerNameProperty<HardwareSummaryQueryRequest>()),
        CapabilityExpectation.ForRemoteTool("remote_metrics_summary", "system_metrics_summary", static () => HasRemoteQueryMethod(typeof(SystemMetrics), "QueryRemoteAsync")),
        CapabilityExpectation.ForRemoteTool("remote_feature_inventory", "system_features_list", static () => HasComputerNameProperty<FeatureInventoryQueryRequest>())
    };

    private static readonly CapabilityExpectation[] TestimoXCoreReadOnlyExpectations = {
        CapabilityExpectation.ForTool("baseline_catalog", "testimox_baselines_list", static () => typeof(BaselineListEntry) is not null),
        CapabilityExpectation.ForTool("baseline_compare", "testimox_baseline_compare", static () => typeof(BaselineComparisonRow) is not null),
        CapabilityExpectation.ForTool("profile_catalog", "testimox_profiles_list", static () => typeof(RuleSelectionProfileInfo) is not null),
        CapabilityExpectation.ForTool("rule_inventory", "testimox_rule_inventory", static () => typeof(RuleInventoryEntry) is not null),
        CapabilityExpectation.ForTool("source_provenance", "testimox_source_query", static () => typeof(RuleOverview) is not null),
        CapabilityExpectation.ForTool("baseline_crosswalk", "testimox_baseline_crosswalk", static () => typeof(RuleCrosswalkReport) is not null),
        CapabilityExpectation.ForTool("run_catalog", "testimox_runs_list", static () => typeof(ToolingRuleRunRequest) is not null),
        CapabilityExpectation.ForTool("run_summary", "testimox_run_summary", static () => typeof(ToolingRuleRunRequest) is not null),
        CapabilityExpectation.ForTool("rule_catalog", "testimox_rules_list", static () => typeof(ToolingRuleDiscoveryRequest) is not null),
        CapabilityExpectation.ForTool("rule_execution", "testimox_rules_run", static () => typeof(ToolingRuleRunRequest) is not null)
    };

    private static readonly CapabilityExpectation[] TestimoXAnalyticsReadOnlyExpectations = {
        CapabilityExpectation.ForTool("analytics_diagnostics", "testimox_analytics_diagnostics_get", static () => typeof(MonitoringDiagnosticsSnapshot) is not null),
        CapabilityExpectation.ForTool("probe_index_status", "testimox_probe_index_status", static () => typeof(ProbeIndexStatusEntry) is not null),
        CapabilityExpectation.ForTool("maintenance_window_history", "testimox_maintenance_window_history", static () => typeof(MaintenanceWindowHistoryEntry) is not null),
        CapabilityExpectation.ForTool("report_data_snapshot", "testimox_report_data_snapshot_get", static () => typeof(MonitoringReportDataSnapshot) is not null),
        CapabilityExpectation.ForTool("report_snapshot", "testimox_report_snapshot_get", static () => typeof(MonitoringReportSnapshot) is not null),
        CapabilityExpectation.ForTool("monitoring_history", "testimox_history_query", static () => typeof(MonitoringAvailabilityRollupSample) is not null),
        CapabilityExpectation.ForTool("report_job_history", "testimox_report_job_history", static () => typeof(MonitoringReportJobSummary) is not null),
    };

    private static readonly CapabilityExpectation[] AdMonitoringReadOnlyExpectations = {
        CapabilityExpectation.ForTool("service_heartbeat", "ad_monitoring_service_heartbeat_get", static () => typeof(MonitoringServiceHeartbeatSnapshot) is not null),
        CapabilityExpectation.ForTool("diagnostics_snapshot", "ad_monitoring_diagnostics_get", static () => typeof(MonitoringDiagnosticsSnapshot) is not null),
        CapabilityExpectation.ForTool("metrics_snapshot", "ad_monitoring_metrics_get", static () => typeof(MonitoringMetricsSnapshot) is not null),
        CapabilityExpectation.ForTool("dashboard_state", "ad_monitoring_dashboard_state_get", static () => typeof(MonitoringDashboardAutoGenerateSnapshot) is not null)
    };

    /// <summary>
    /// Builds the phase-1 parity inventory. Returns an empty array when no live tool definitions are available.
    /// </summary>
    public static SessionCapabilityParityEntryDto[] Build(
        IReadOnlyList<ToolDefinition>? definitions,
        IEnumerable<ToolPackAvailabilityInfo>? packAvailability = null) {
        if (definitions is not { Count: > 0 }) {
            return Array.Empty<SessionCapabilityParityEntryDto>();
        }

        var packEnabledIds = new HashSet<string>(
            (packAvailability ?? Array.Empty<ToolPackAvailabilityInfo>())
            .Where(static pack => pack.Enabled)
            .Select(static pack => ToolPackBootstrap.NormalizePackId(pack.Id))
            .Where(static packId => packId.Length > 0),
            StringComparer.OrdinalIgnoreCase);
        var definitionNames = new HashSet<string>(
            definitions
                .Where(static definition => definition is not null)
                .Select(static definition => (definition.Name ?? string.Empty).Trim())
                .Where(static name => name.Length > 0),
            StringComparer.OrdinalIgnoreCase);
        var definitionsByName = BuildDefinitionsByName(definitions);
        var definitionsByPackId = BuildDefinitionsByPackId(definitions);

        var entries = new List<SessionCapabilityParityEntryDto>(4);
        TryAddEntry(entries, BuildAdMonitoringEntry(definitionNames, definitionsByPackId, packEnabledIds));
        TryAddEntry(entries, BuildComputerXEntry(definitionsByName, definitionsByPackId, packEnabledIds));
        TryAddEntry(entries, BuildTestimoXCoreEntry(definitionsByName, definitionsByPackId, packEnabledIds));
        TryAddEntry(entries, BuildTestimoXAnalyticsEntry(definitionsByName, definitionsByPackId, packEnabledIds));
        TryAddEntry(entries, BuildTestimoXPowerShellEntry(definitionsByPackId, packEnabledIds));

        return entries
            .OrderBy(static entry => entry.EngineId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Formats a one-line parity summary for host diagnostics.
    /// </summary>
    public static string FormatSummary(IReadOnlyList<SessionCapabilityParityEntryDto>? entries) {
        if (entries is not { Count: > 0 }) {
            return "engines=0, healthy=0, gaps=0, governed_backlog=0, missing_readonly=0";
        }

        var healthy = 0;
        var gaps = 0;
        var governedBacklog = 0;
        var missingReadonly = 0;
        for (var i = 0; i < entries.Count; i++) {
            var entry = entries[i];
            if (entry is null) {
                continue;
            }

            missingReadonly += Math.Max(0, entry.MissingCapabilityCount);
            if (string.Equals(entry.Status, HealthyStatus, StringComparison.OrdinalIgnoreCase)) {
                healthy++;
            } else if (string.Equals(entry.Status, GapStatus, StringComparison.OrdinalIgnoreCase)) {
                gaps++;
            } else if (string.Equals(entry.Status, GovernedBacklogStatus, StringComparison.OrdinalIgnoreCase)) {
                governedBacklog++;
            }
        }

        return
            $"engines={entries.Count}, " +
            $"healthy={healthy}, " +
            $"gaps={gaps}, " +
            $"governed_backlog={governedBacklog}, " +
            $"missing_readonly={missingReadonly}";
    }

    /// <summary>
    /// Builds compact attention summaries for non-healthy parity entries.
    /// </summary>
    public static IReadOnlyList<string> BuildAttentionSummaries(IReadOnlyList<SessionCapabilityParityEntryDto>? entries, int maxItems = 6) {
        if (entries is not { Count: > 0 } || maxItems <= 0) {
            return Array.Empty<string>();
        }

        var lines = new List<string>(Math.Min(maxItems, entries.Count));
        for (var i = 0; i < entries.Count && lines.Count < maxItems; i++) {
            var entry = entries[i];
            if (entry is null
                || string.Equals(entry.Status, HealthyStatus, StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry.Status, PackUnavailableStatus, StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry.Status, SourceUnavailableStatus, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            if (string.Equals(entry.Status, GovernedBacklogStatus, StringComparison.OrdinalIgnoreCase)) {
                lines.Add($"{entry.EngineId}: governed backlog ({entry.Note ?? "intentionally not autonomous in phase 1."})");
                continue;
            }

            var suffix = entry.MissingCapabilityCount > 0
                ? $"missing {entry.MissingCapabilityCount} ({string.Join(", ", entry.MissingCapabilities)})"
                : entry.Note ?? entry.Status;
            lines.Add($"{entry.EngineId}: {suffix}");
        }

        return lines.Count == 0 ? Array.Empty<string>() : lines.ToArray();
    }

    /// <summary>
    /// Builds operator-facing per-engine parity detail summaries.
    /// </summary>
    public static IReadOnlyList<string> BuildDetailSummaries(IReadOnlyList<SessionCapabilityParityEntryDto>? entries, int maxItems = 8) {
        if (entries is not { Count: > 0 } || maxItems <= 0) {
            return Array.Empty<string>();
        }

        var lines = new List<string>(Math.Min(maxItems, entries.Count));
        for (var i = 0; i < entries.Count && lines.Count < maxItems; i++) {
            var entry = entries[i];
            if (entry is null) {
                continue;
            }

            var prefix = $"{entry.EngineId} [{entry.Status}]";
            if (string.Equals(entry.Status, GovernedBacklogStatus, StringComparison.OrdinalIgnoreCase)) {
                lines.Add($"{prefix}: registered_tools={entry.RegisteredToolCount}; {entry.Note ?? "governed backlog."}");
                continue;
            }

            if (string.Equals(entry.Status, SourceUnavailableStatus, StringComparison.OrdinalIgnoreCase)) {
                lines.Add($"{prefix}: source metadata unavailable; registered_tools={entry.RegisteredToolCount}.");
                continue;
            }

            if (string.Equals(entry.Status, PackUnavailableStatus, StringComparison.OrdinalIgnoreCase)) {
                lines.Add($"{prefix}: pack unavailable.");
                continue;
            }

            var detail = $"{prefix}: surfaced={entry.SurfacedCapabilityCount}/{entry.ExpectedCapabilityCount}, registered_tools={entry.RegisteredToolCount}";
            if (entry.MissingCapabilityCount > 0) {
                detail += $", missing={entry.MissingCapabilityCount}";
                if (entry.MissingCapabilities.Length > 0) {
                    detail += $" ({FormatCapabilityList(entry.MissingCapabilities, entry.MissingCapabilityCount)})";
                }
            }

            lines.Add(detail);
        }

        return lines.Count == 0 ? Array.Empty<string>() : lines.ToArray();
    }

    private static SessionCapabilityParityEntryDto? BuildAdMonitoringEntry(
        HashSet<string> definitionNames,
        Dictionary<string, List<ToolDefinition>> definitionsByPackId,
        HashSet<string> packEnabledIds) {
        const string packId = "active_directory";
        var registeredToolCount = GetRegisteredToolCount(definitionsByPackId, packId);
        if (registeredToolCount == 0 && !packEnabledIds.Contains(packId)) {
            return null;
        }

        var upstreamKinds = DiscoverAdMonitoringProbeKinds();
        if (upstreamKinds.Length == 0) {
            return CreateStatusEntry(
                engineId: "adplayground_monitoring",
                packId,
                status: SourceUnavailableStatus,
                sourceAvailable: false,
                registeredToolCount,
                expectedCapabilityCount: 0,
                surfacedCapabilityCount: 0,
                missingCapabilities: Array.Empty<string>(),
                note: "ADPlayground.Monitoring probe metadata was not available in this runtime.");
        }

        var surfacedKinds = DiscoverSurfacedAdMonitoringProbeKinds(definitionNames, definitionsByPackId);
        var availableReadOnlyExpectations = AdMonitoringReadOnlyExpectations
            .Where(static expectation => expectation.IsAvailable())
            .ToArray();
        var surfacedReadOnlyCapabilities = availableReadOnlyExpectations
            .Where(expectation => expectation.IsSurfaced(BuildDefinitionsByName(definitionsByPackId.TryGetValue(packId, out var packDefinitions) ? packDefinitions : Array.Empty<ToolDefinition>())))
            .Select(static expectation => expectation.CapabilityId)
            .ToArray();
        return CreateCapabilityEntry(
            engineId: "adplayground_monitoring",
            packId,
            sourceAvailable: true,
            registeredToolCount,
            expectedCapabilities: upstreamKinds.Concat(availableReadOnlyExpectations.Select(static expectation => expectation.CapabilityId)),
            surfacedCapabilities: surfacedKinds.Concat(surfacedReadOnlyCapabilities),
            note: "Probe-kind and persisted runtime-state parity between ADPlayground.Monitoring and IX AD monitoring tools.");
    }

    private static SessionCapabilityParityEntryDto? BuildComputerXEntry(
        IReadOnlyDictionary<string, ToolDefinition> definitionsByName,
        Dictionary<string, List<ToolDefinition>> definitionsByPackId,
        HashSet<string> packEnabledIds) {
        const string packId = "system";
        var registeredToolCount = GetRegisteredToolCount(definitionsByPackId, packId);
        if (registeredToolCount == 0 && !packEnabledIds.Contains(packId)) {
            return null;
        }

        var availableExpectations = ComputerXReadOnlyExpectations
            .Where(static expectation => expectation.IsAvailable())
            .ToArray();
        if (availableExpectations.Length == 0) {
            return CreateStatusEntry(
                engineId: "computerx",
                packId,
                status: SourceUnavailableStatus,
                sourceAvailable: false,
                registeredToolCount,
                expectedCapabilityCount: 0,
                surfacedCapabilityCount: 0,
                missingCapabilities: Array.Empty<string>(),
                note: "ComputerX remote read-only contracts were not available in this runtime.");
        }

        var surfacedCapabilities = availableExpectations
            .Where(expectation => expectation.IsSurfaced(definitionsByName))
            .Select(static expectation => expectation.CapabilityId)
            .ToArray();
        return CreateCapabilityEntry(
            engineId: "computerx",
            packId,
            sourceAvailable: true,
            registeredToolCount,
            expectedCapabilities: availableExpectations.Select(static expectation => expectation.CapabilityId),
            surfacedCapabilities: surfacedCapabilities,
            note: "Expanded remote read-only parity for ComputerX operator surfaces.");
    }

    private static SessionCapabilityParityEntryDto? BuildTestimoXCoreEntry(
        IReadOnlyDictionary<string, ToolDefinition> definitionsByName,
        Dictionary<string, List<ToolDefinition>> definitionsByPackId,
        HashSet<string> packEnabledIds) {
        const string packId = "testimox";
        var registeredToolCount = GetRegisteredToolCount(definitionsByPackId, packId);
        if (registeredToolCount == 0 && !packEnabledIds.Contains(packId)) {
            return null;
        }

        var availableExpectations = TestimoXCoreReadOnlyExpectations
            .Where(static expectation => expectation.IsAvailable())
            .ToArray();
        if (availableExpectations.Length == 0) {
            return CreateStatusEntry(
                engineId: "testimox",
                packId,
                status: SourceUnavailableStatus,
                sourceAvailable: false,
                registeredToolCount,
                expectedCapabilityCount: 0,
                surfacedCapabilityCount: 0,
                missingCapabilities: Array.Empty<string>(),
                note: "TestimoX rule tooling contracts were not available in this runtime.");
        }

        var surfacedCapabilities = availableExpectations
            .Where(expectation => expectation.IsSurfaced(definitionsByName))
            .Select(static expectation => expectation.CapabilityId)
            .ToArray();
        return CreateCapabilityEntry(
            engineId: "testimox",
            packId,
            sourceAvailable: true,
            registeredToolCount,
            expectedCapabilities: availableExpectations.Select(static expectation => expectation.CapabilityId),
            surfacedCapabilities: surfacedCapabilities,
            note: "Profiles, inventory, baseline crosswalk, catalog, and execution parity for TestimoX tooling service.");
    }

    private static SessionCapabilityParityEntryDto? BuildTestimoXAnalyticsEntry(
        IReadOnlyDictionary<string, ToolDefinition> definitionsByName,
        Dictionary<string, List<ToolDefinition>> definitionsByPackId,
        HashSet<string> packEnabledIds) {
        const string packId = "testimox_analytics";
        var registeredToolCount = GetRegisteredToolCount(definitionsByPackId, packId);
        if (registeredToolCount == 0 && !packEnabledIds.Contains(packId)) {
            return null;
        }

        var availableExpectations = TestimoXAnalyticsReadOnlyExpectations
            .Where(static expectation => expectation.IsAvailable())
            .ToArray();
        if (availableExpectations.Length == 0) {
            return CreateStatusEntry(
                engineId: "testimox_analytics",
                packId,
                status: SourceUnavailableStatus,
                sourceAvailable: false,
                registeredToolCount,
                expectedCapabilityCount: 0,
                surfacedCapabilityCount: 0,
                missingCapabilities: Array.Empty<string>(),
                note: "TestimoX analytics artifact contracts were not available in this runtime.");
        }

        var surfacedCapabilities = availableExpectations
            .Where(expectation => expectation.IsSurfaced(definitionsByName))
            .Select(static expectation => expectation.CapabilityId)
            .ToArray();
        return CreateCapabilityEntry(
            engineId: "testimox_analytics",
            packId,
            sourceAvailable: true,
            registeredToolCount,
            expectedCapabilities: availableExpectations.Select(static expectation => expectation.CapabilityId),
            surfacedCapabilities: surfacedCapabilities,
            note: "Persisted analytics, report, snapshot, and maintenance artifact parity for TestimoX analytics tooling.");
    }

    private static SessionCapabilityParityEntryDto? BuildTestimoXPowerShellEntry(
        Dictionary<string, List<ToolDefinition>> definitionsByPackId,
        HashSet<string> packEnabledIds) {
        const string packId = "testimox";
        var registeredToolCount = GetRegisteredToolCount(definitionsByPackId, packId);
        if (registeredToolCount == 0 && !packEnabledIds.Contains(packId)) {
            return null;
        }

        var sourceAvailable = typeof(PowerShellRuleProvider) is not null;
        if (!sourceAvailable) {
            return null;
        }

        return CreateStatusEntry(
            engineId: "testimox_powershell",
            packId,
            status: GovernedBacklogStatus,
            sourceAvailable: true,
            registeredToolCount,
            expectedCapabilityCount: 0,
            surfacedCapabilityCount: 0,
            missingCapabilities: Array.Empty<string>(),
            note: "PowerShell/provider-backed TestimoX service-management flows stay governed outside autonomous phase 1.");
    }

    private static Dictionary<string, List<ToolDefinition>> BuildDefinitionsByPackId(IReadOnlyList<ToolDefinition> definitions) {
        var result = new Dictionary<string, List<ToolDefinition>>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < definitions.Count; i++) {
            var definition = definitions[i];
            if (definition is null || !ToolHealthDiagnostics.TryResolvePackId(definition, out var packId) || packId.Length == 0) {
                continue;
            }

            if (!result.TryGetValue(packId, out var list)) {
                list = new List<ToolDefinition>();
                result[packId] = list;
            }

            list.Add(definition);
        }

        return result;
    }

    private static Dictionary<string, ToolDefinition> BuildDefinitionsByName(IReadOnlyList<ToolDefinition> definitions) {
        var result = new Dictionary<string, ToolDefinition>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < definitions.Count; i++) {
            var definition = definitions[i];
            var name = (definition?.Name ?? string.Empty).Trim();
            if (name.Length == 0 || result.ContainsKey(name)) {
                continue;
            }

            result[name] = definition!;
        }

        return result;
    }

    private static int GetRegisteredToolCount(Dictionary<string, List<ToolDefinition>> definitionsByPackId, string packId) {
        return definitionsByPackId.TryGetValue(packId, out var definitions)
            ? definitions.Count
            : 0;
    }

    private static SessionCapabilityParityEntryDto CreateCapabilityEntry(
        string engineId,
        string packId,
        bool sourceAvailable,
        int registeredToolCount,
        IEnumerable<string> expectedCapabilities,
        IEnumerable<string> surfacedCapabilities,
        string? note) {
        var expected = NormalizeDistinctValues(expectedCapabilities, maxItems: 0);
        var surfaced = NormalizeDistinctValues(surfacedCapabilities, maxItems: 0);
        var missing = expected
            .Except(surfaced, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var status = missing.Length > 0 ? GapStatus : HealthyStatus;

        return new SessionCapabilityParityEntryDto {
            EngineId = engineId,
            PackId = packId,
            Status = status,
            SourceAvailable = sourceAvailable,
            RegisteredToolCount = Math.Max(0, registeredToolCount),
            ExpectedCapabilityCount = expected.Length,
            SurfacedCapabilityCount = surfaced.Length,
            MissingCapabilityCount = missing.Length,
            MissingCapabilities = NormalizeDistinctValues(missing, MaxMissingCapabilities),
            Note = note
        };
    }

    private static SessionCapabilityParityEntryDto CreateStatusEntry(
        string engineId,
        string packId,
        string status,
        bool sourceAvailable,
        int registeredToolCount,
        int expectedCapabilityCount,
        int surfacedCapabilityCount,
        IEnumerable<string> missingCapabilities,
        string? note) {
        var missing = NormalizeDistinctValues(missingCapabilities, MaxMissingCapabilities);
        return new SessionCapabilityParityEntryDto {
            EngineId = engineId,
            PackId = packId,
            Status = status,
            SourceAvailable = sourceAvailable,
            RegisteredToolCount = Math.Max(0, registeredToolCount),
            ExpectedCapabilityCount = Math.Max(0, expectedCapabilityCount),
            SurfacedCapabilityCount = Math.Max(0, surfacedCapabilityCount),
            MissingCapabilityCount = missing.Length,
            MissingCapabilities = missing,
            Note = note
        };
    }

    private static string[] DiscoverAdMonitoringProbeKinds() {
        var assembly = typeof(ProbeDefinition).Assembly;
        var baseType = typeof(ProbeDefinition);
        var directoryBaseType = typeof(DirectoryHealthProbeDefinitionBase);
        try {
            return assembly.GetTypes()
                .Where(type => !type.IsAbstract && baseType.IsAssignableFrom(type))
                .Select(type => directoryBaseType.IsAssignableFrom(type)
                    ? "directory"
                    : NormalizeCapabilityId(type.Name, "ProbeDefinition"))
                .Where(static value => value.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        } catch (ReflectionTypeLoadException ex) {
            return ex.Types
                .Where(static type => type is not null)
                .Where(type => !type!.IsAbstract && baseType.IsAssignableFrom(type))
                .Select(type => directoryBaseType.IsAssignableFrom(type!)
                    ? "directory"
                    : NormalizeCapabilityId(type!.Name, "ProbeDefinition"))
                .Where(static value => value.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray()!;
        }
    }

    private static string[] DiscoverSurfacedAdMonitoringProbeKinds(
        HashSet<string> definitionNames,
        Dictionary<string, List<ToolDefinition>> definitionsByPackId) {
        if (!definitionNames.Contains("ad_monitoring_probe_run")) {
            return Array.Empty<string>();
        }

        if (!definitionsByPackId.TryGetValue("active_directory", out var definitions) || definitions.Count == 0) {
            return Array.Empty<string>();
        }

        var definition = definitions.FirstOrDefault(static candidate =>
            string.Equals(candidate.Name, "ad_monitoring_probe_run", StringComparison.OrdinalIgnoreCase));
        var enumValues = definition?.Parameters?
            .GetObject("properties")?
            .GetObject("probe_kind")?
            .GetArray("enum");
        if (enumValues is null || enumValues.Count == 0) {
            return Array.Empty<string>();
        }

        return NormalizeDistinctValues(
            enumValues
                .Select(static value => value?.AsString())
                .Where(static value => !string.IsNullOrWhiteSpace(value))!,
            maxItems: 0);
    }

    private static bool HasComputerNameProperty<TRequest>() {
        return typeof(TRequest).GetProperty("ComputerName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase) is not null;
    }

    private static bool HasRemoteQueryMethod(Type type, string methodName) {
        return type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase) is not null;
    }

    private static bool HasStaticMethod(Type type, string methodName) {
        return type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase) is not null;
    }

    private static bool HasToolParameter(IReadOnlyDictionary<string, ToolDefinition> definitionsByName, string toolName, string parameterName) {
        if (!definitionsByName.TryGetValue(toolName, out var definition) || definition.Parameters is null) {
            return false;
        }

        var properties = definition.Parameters.GetObject("properties");
        return properties is not null && properties.TryGetValue(parameterName, out _);
    }

    private static string FormatCapabilityList(IReadOnlyList<string> capabilities, int totalCount) {
        var shown = capabilities?.Where(static value => !string.IsNullOrWhiteSpace(value)).ToArray() ?? Array.Empty<string>();
        if (shown.Length == 0) {
            return string.Empty;
        }

        var suffix = totalCount > shown.Length ? $", +{totalCount - shown.Length} more" : string.Empty;
        return string.Join(", ", shown) + suffix;
    }

    private static string NormalizeCapabilityId(string? value, string suffixToTrim) {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(suffixToTrim)
            && normalized.EndsWith(suffixToTrim, StringComparison.OrdinalIgnoreCase)) {
            normalized = normalized[..^suffixToTrim.Length];
        }

        return string.Concat(
                normalized.SelectMany(static (ch, index) =>
                    char.IsUpper(ch) && index > 0
                        ? new[] { '_', char.ToLowerInvariant(ch) }
                        : new[] { char.ToLowerInvariant(ch) }))
            .Trim('_');
    }

    private static string[] NormalizeDistinctValues(IEnumerable<string> values, int maxItems) {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values ?? Array.Empty<string>()) {
            var normalized = (value ?? string.Empty).Trim();
            if (normalized.Length == 0 || !seen.Add(normalized)) {
                continue;
            }

            result.Add(normalized);
            if (maxItems > 0 && result.Count >= maxItems) {
                break;
            }
        }

        return result.Count == 0 ? Array.Empty<string>() : result.ToArray();
    }

    private static void TryAddEntry(ICollection<SessionCapabilityParityEntryDto> entries, SessionCapabilityParityEntryDto? entry) {
        if (entry is not null) {
            entries.Add(entry);
        }
    }

    private readonly record struct CapabilityExpectation(
        string CapabilityId,
        Func<IReadOnlyDictionary<string, ToolDefinition>, bool> IsSurfaced,
        Func<bool> IsAvailable) {
        public static CapabilityExpectation ForTool(string capabilityId, string toolName, Func<bool> isAvailable) {
            return new CapabilityExpectation(
                CapabilityId: capabilityId,
                IsSurfaced: definitionsByName => definitionsByName.ContainsKey((toolName ?? string.Empty).Trim()),
                IsAvailable: isAvailable ?? throw new ArgumentNullException(nameof(isAvailable)));
        }

        public static CapabilityExpectation ForRemoteTool(string capabilityId, string toolName, Func<bool> isAvailable) {
            return new CapabilityExpectation(
                CapabilityId: capabilityId,
                IsSurfaced: definitionsByName => HasToolParameter(definitionsByName, (toolName ?? string.Empty).Trim(), "computer_name"),
                IsAvailable: isAvailable ?? throw new ArgumentNullException(nameof(isAvailable)));
        }
    }
}
