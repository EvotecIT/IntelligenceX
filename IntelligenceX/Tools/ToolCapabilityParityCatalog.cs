using System;
using System.Collections.Generic;

namespace IntelligenceX.Tools;

/// <summary>
/// Describes how a surfaced IX tool maps to an upstream capability contract for parity checks.
/// </summary>
public readonly struct ToolCapabilityParityExpectationDescriptor {
    /// <summary>
    /// Initializes a new parity expectation descriptor.
    /// </summary>
    public ToolCapabilityParityExpectationDescriptor(
        string capabilityId,
        string toolName,
        ToolCapabilityParitySurfaceContractKind surfaceContractKind,
        string? surfaceParameterName,
        ToolCapabilityParitySourceContractKind sourceContractKind,
        string assemblyName,
        string typeName,
        string? propertyName,
        IReadOnlyList<string>? methodNames) {
        CapabilityId = capabilityId ?? string.Empty;
        ToolName = toolName ?? string.Empty;
        SurfaceContractKind = surfaceContractKind;
        SurfaceParameterName = surfaceParameterName ?? string.Empty;
        SourceContractKind = sourceContractKind;
        AssemblyName = assemblyName ?? string.Empty;
        TypeName = typeName ?? string.Empty;
        PropertyName = propertyName ?? string.Empty;
        MethodNames = methodNames ?? Array.Empty<string>();
    }

    /// <summary>
    /// Stable upstream capability identifier tracked by the parity inventory.
    /// </summary>
    public string CapabilityId { get; }

    /// <summary>
    /// Surfaced IX tool name expected to cover the capability.
    /// </summary>
    public string ToolName { get; }

    /// <summary>
    /// Surfacing contract used to verify the tool is exposed correctly.
    /// </summary>
    public ToolCapabilityParitySurfaceContractKind SurfaceContractKind { get; }

    /// <summary>
    /// Optional surfaced tool parameter required by the parity contract.
    /// </summary>
    public string SurfaceParameterName { get; }

    /// <summary>
    /// Upstream source contract kind used to validate availability.
    /// </summary>
    public ToolCapabilityParitySourceContractKind SourceContractKind { get; }

    /// <summary>
    /// Upstream assembly name used to resolve the source contract.
    /// </summary>
    public string AssemblyName { get; }

    /// <summary>
    /// Upstream type name used to resolve the source contract.
    /// </summary>
    public string TypeName { get; }

    /// <summary>
    /// Optional upstream property required by the source contract.
    /// </summary>
    public string PropertyName { get; }

    /// <summary>
    /// Optional upstream method names required by the source contract.
    /// </summary>
    public IReadOnlyList<string> MethodNames { get; }

    /// <summary>
    /// Creates a tool-presence parity expectation backed by an upstream type.
    /// </summary>
    public static ToolCapabilityParityExpectationDescriptor ForToolType(
        string capabilityId,
        string toolName,
        string typeName,
        string assemblyName) {
        return Create(
            capabilityId,
            toolName,
            ToolCapabilityParitySurfaceContractKind.ToolPresent,
            surfaceParameterName: null,
            ToolCapabilityParitySourceContractKind.TypeExists,
            assemblyName,
            typeName,
            propertyName: null,
            methodNames: null);
    }

    /// <summary>
    /// Creates a tool-presence parity expectation backed by an upstream public static method.
    /// </summary>
    public static ToolCapabilityParityExpectationDescriptor ForToolStaticMethod(
        string capabilityId,
        string toolName,
        string typeName,
        string methodName,
        string assemblyName) {
        return Create(
            capabilityId,
            toolName,
            ToolCapabilityParitySurfaceContractKind.ToolPresent,
            surfaceParameterName: null,
            ToolCapabilityParitySourceContractKind.PublicStaticMethod,
            assemblyName,
            typeName,
            propertyName: null,
            methodNames: new[] { methodName ?? string.Empty });
    }

    /// <summary>
    /// Creates a parameter-aware parity expectation backed by an upstream request property.
    /// </summary>
    public static ToolCapabilityParityExpectationDescriptor ForToolParameterProperty(
        string capabilityId,
        string toolName,
        string surfaceParameterName,
        string typeName,
        string propertyName,
        string assemblyName) {
        return Create(
            capabilityId,
            toolName,
            ToolCapabilityParitySurfaceContractKind.ToolParameterPresent,
            surfaceParameterName: surfaceParameterName,
            ToolCapabilityParitySourceContractKind.PublicInstanceProperty,
            assemblyName,
            typeName,
            propertyName,
            methodNames: null);
    }

    /// <summary>
    /// Creates a parameter-aware parity expectation backed by a single upstream static method.
    /// </summary>
    public static ToolCapabilityParityExpectationDescriptor ForToolParameterStaticMethod(
        string capabilityId,
        string toolName,
        string surfaceParameterName,
        string typeName,
        string methodName,
        string assemblyName) {
        return Create(
            capabilityId,
            toolName,
            ToolCapabilityParitySurfaceContractKind.ToolParameterPresent,
            surfaceParameterName: surfaceParameterName,
            ToolCapabilityParitySourceContractKind.PublicStaticMethod,
            assemblyName,
            typeName,
            propertyName: null,
            methodNames: new[] { methodName ?? string.Empty });
    }

    /// <summary>
    /// Creates a remote-tool parity expectation backed by an upstream request property.
    /// </summary>
    public static ToolCapabilityParityExpectationDescriptor ForRemoteToolProperty(
        string capabilityId,
        string toolName,
        string typeName,
        string propertyName,
        string assemblyName) {
        return ForToolParameterProperty(
            capabilityId,
            toolName,
            ToolCapabilityParityCatalog.RemoteComputerNameParameterName,
            typeName,
            propertyName,
            assemblyName);
    }

    /// <summary>
    /// Creates a remote-tool parity expectation backed by a single upstream static method.
    /// </summary>
    public static ToolCapabilityParityExpectationDescriptor ForRemoteToolStaticMethod(
        string capabilityId,
        string toolName,
        string typeName,
        string methodName,
        string assemblyName) {
        return ForToolParameterStaticMethod(
            capabilityId,
            toolName,
            ToolCapabilityParityCatalog.RemoteComputerNameParameterName,
            typeName,
            methodName,
            assemblyName);
    }

    /// <summary>
    /// Creates a remote-tool parity expectation backed by any matching upstream static method.
    /// </summary>
    public static ToolCapabilityParityExpectationDescriptor ForRemoteToolAnyStaticMethod(
        string capabilityId,
        string toolName,
        string typeName,
        string assemblyName,
        params string[] methodNames) {
        return Create(
            capabilityId,
            toolName,
            ToolCapabilityParitySurfaceContractKind.ToolParameterPresent,
            surfaceParameterName: ToolCapabilityParityCatalog.RemoteComputerNameParameterName,
            ToolCapabilityParitySourceContractKind.AnyPublicStaticMethod,
            assemblyName,
            typeName,
            propertyName: null,
            methodNames);
    }

    /// <summary>
    /// Creates a remote-tool parity expectation backed by multiple required upstream static methods.
    /// </summary>
    public static ToolCapabilityParityExpectationDescriptor ForRemoteToolAllStaticMethods(
        string capabilityId,
        string toolName,
        string typeName,
        string assemblyName,
        params string[] methodNames) {
        return Create(
            capabilityId,
            toolName,
            ToolCapabilityParitySurfaceContractKind.ToolParameterPresent,
            surfaceParameterName: ToolCapabilityParityCatalog.RemoteComputerNameParameterName,
            ToolCapabilityParitySourceContractKind.AllPublicStaticMethods,
            assemblyName,
            typeName,
            propertyName: null,
            methodNames);
    }

    private static ToolCapabilityParityExpectationDescriptor Create(
        string capabilityId,
        string toolName,
        ToolCapabilityParitySurfaceContractKind surfaceContractKind,
        string? surfaceParameterName,
        ToolCapabilityParitySourceContractKind sourceContractKind,
        string assemblyName,
        string typeName,
        string? propertyName,
        IReadOnlyList<string>? methodNames) {
        return new ToolCapabilityParityExpectationDescriptor(
            capabilityId,
            toolName,
            surfaceContractKind,
            surfaceParameterName,
            sourceContractKind,
            assemblyName,
            typeName,
            propertyName,
            methodNames);
    }
}

/// <summary>
/// Structured surfaced-tool contract used by the runtime parity inventory.
/// </summary>
public enum ToolCapabilityParitySurfaceContractKind {
    /// <summary>
    /// The surfaced IX tool only needs to exist.
    /// </summary>
    ToolPresent,

    /// <summary>
    /// The surfaced IX tool must expose a specific parameter.
    /// </summary>
    ToolParameterPresent
}

/// <summary>
/// Structured upstream source contract used by the runtime parity inventory.
/// </summary>
public enum ToolCapabilityParitySourceContractKind {
    /// <summary>
    /// The upstream type must exist.
    /// </summary>
    TypeExists,

    /// <summary>
    /// The upstream type must expose a public instance property.
    /// </summary>
    PublicInstanceProperty,

    /// <summary>
    /// The upstream type must expose one specific public static method.
    /// </summary>
    PublicStaticMethod,

    /// <summary>
    /// The upstream type must expose at least one of the listed public static methods.
    /// </summary>
    AnyPublicStaticMethod,

    /// <summary>
    /// The upstream type must expose all listed public static methods.
    /// </summary>
    AllPublicStaticMethods
}

/// <summary>
/// Shared parity expectation catalog used by chat/runtime capability audits.
/// </summary>
public static class ToolCapabilityParityCatalog {
    /// <summary>
    /// Canonical parameter name used by remote ComputerX-backed surfaces.
    /// </summary>
    public const string RemoteComputerNameParameterName = "computer_name";

    /// <summary>
    /// Canonical ComputerX assembly name.
    /// </summary>
    public const string ComputerXAssemblyName = "ComputerX";

    /// <summary>
    /// Canonical EventViewerX assembly name.
    /// </summary>
    public const string EventViewerXAssemblyName = "EventViewerX";

    /// <summary>
    /// Canonical ADPlayground.Monitoring assembly name.
    /// </summary>
    public const string AdMonitoringAssemblyName = "ADPlayground.Monitoring";

    /// <summary>
    /// Canonical TestimoX assembly name.
    /// </summary>
    public const string TestimoXAssemblyName = "TestimoX";

    /// <summary>
    /// Canonical AD monitoring probe-definition base type.
    /// </summary>
    public const string AdMonitoringProbeDefinitionTypeName = "ADPlayground.Monitoring.Probes.ProbeDefinition";

    /// <summary>
    /// Canonical AD monitoring directory-health probe-definition base type.
    /// </summary>
    public const string AdMonitoringDirectoryHealthProbeDefinitionBaseTypeName =
        "ADPlayground.Monitoring.Probes.DirectoryHealth.DirectoryHealthProbeDefinitionBase";

    /// <summary>
    /// Canonical TestimoX PowerShell provider type.
    /// </summary>
    public const string TestimoXPowerShellProviderTypeName = "TestimoX.Providers.PowerShellRuleProvider";

    /// <summary>
    /// Canonical parameter name used by remote EventViewerX-backed surfaces.
    /// </summary>
    public const string RemoteMachineNameParameterName = "machine_name";

    /// <summary>
    /// Shared ComputerX read-only capability expectations.
    /// </summary>
    public static IReadOnlyList<ToolCapabilityParityExpectationDescriptor> ComputerXReadOnlyExpectations { get; } = new[] {
        ToolCapabilityParityExpectationDescriptor.ForRemoteToolProperty("remote_runtime_summary", "system_info", "ComputerX.Runtime.SystemRuntimeQueryRequest", "ComputerName", ComputerXAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForRemoteToolProperty("remote_process_inventory", "system_process_list", "ComputerX.Processes.ProcessListQueryRequest", "ComputerName", ComputerXAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForRemoteToolProperty("remote_network_adapter_inventory", "system_network_adapters", "ComputerX.Network.NetworkAdapterInventoryQueryRequest", "ComputerName", ComputerXAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForRemoteToolProperty("remote_port_inventory", "system_ports_list", "ComputerX.Ports.PortInventoryQueryRequest", "ComputerName", ComputerXAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForRemoteToolProperty("remote_service_inventory", "system_service_list", "ComputerX.Services.ServiceListQueryRequest", "ComputerName", ComputerXAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForRemoteToolProperty("remote_scheduled_task_inventory", "system_scheduled_tasks_list", "ComputerX.ScheduledTasks.TaskSchedulerListQueryRequest", "ComputerName", ComputerXAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForRemoteToolProperty("remote_firewall_rule_inventory", "system_firewall_rules", "ComputerX.Firewall.FirewallRuleListQueryRequest", "ComputerName", ComputerXAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForRemoteToolProperty("remote_firewall_profile_inventory", "system_firewall_profiles", "ComputerX.Firewall.FirewallProfileListQueryRequest", "ComputerName", ComputerXAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForRemoteToolProperty("remote_local_identity_inventory", "system_local_identity_inventory", "ComputerX.Identity.LocalIdentityInventoryQueryRequest", "ComputerName", ComputerXAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForRemoteToolStaticMethod("remote_security_options", "system_security_options", "ComputerX.SecurityPolicy.SecurityOptionsQuery", "Get", ComputerXAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForRemoteToolStaticMethod("remote_rdp_posture", "system_rdp_posture", "ComputerX.Rdp.RdpPolicyQuery", "Get", ComputerXAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForRemoteToolStaticMethod("remote_smb_posture", "system_smb_posture", "ComputerX.Smb.SmbConfigQuery", "Get", ComputerXAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForRemoteToolStaticMethod("remote_boot_configuration", "system_boot_configuration", "ComputerX.Boot.BootOptionsQuery", "Query", ComputerXAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForRemoteToolAnyStaticMethod("remote_bios_summary", "system_bios_summary", "ComputerX.Bios.Bios", ComputerXAssemblyName, "Get", "GetAsync"),
        ToolCapabilityParityExpectationDescriptor.ForRemoteToolStaticMethod("remote_time_sync", "system_time_sync", "ComputerX.Time.TimeSync", "QueryRemoteStatusAsync", ComputerXAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForRemoteToolAnyStaticMethod("remote_bitlocker_status", "system_bitlocker_status", "ComputerX.Security.BitLocker.BitLocker", ComputerXAssemblyName, "Get", "GetAsync"),
        ToolCapabilityParityExpectationDescriptor.ForRemoteToolAnyStaticMethod("remote_privacy_posture", "system_privacy_posture", "ComputerX.Privacy.PrivacyPosture", ComputerXAssemblyName, "Get", "GetAsync"),
        ToolCapabilityParityExpectationDescriptor.ForRemoteToolAnyStaticMethod("remote_exploit_protection", "system_exploit_protection", "ComputerX.ExploitProtection.ExploitProtection", ComputerXAssemblyName, "Get", "GetAsync"),
        ToolCapabilityParityExpectationDescriptor.ForRemoteToolAnyStaticMethod("remote_office_posture", "system_office_posture", "ComputerX.Office.OfficePosture", ComputerXAssemblyName, "Get", "GetAsync"),
        ToolCapabilityParityExpectationDescriptor.ForRemoteToolAnyStaticMethod("remote_browser_posture", "system_browser_posture", "ComputerX.Browsers.BrowserPosture", ComputerXAssemblyName, "Get", "GetAsync"),
        ToolCapabilityParityExpectationDescriptor.ForRemoteToolAnyStaticMethod("remote_backup_posture", "system_backup_posture", "ComputerX.Backup.Backup", ComputerXAssemblyName, "Get", "GetAsync"),
        ToolCapabilityParityExpectationDescriptor.ForRemoteToolStaticMethod("remote_tls_posture", "system_tls_posture", "ComputerX.SecurityPolicy.TlsPolicyQuery", "Get", ComputerXAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForRemoteToolStaticMethod("remote_winrm_posture", "system_winrm_posture", "ComputerX.SecurityPolicy.WinRmPolicyQuery", "Get", ComputerXAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForRemoteToolStaticMethod("remote_powershell_logging_posture", "system_powershell_logging_posture", "ComputerX.SecurityPolicy.PsLoggingPolicyQuery", "Get", ComputerXAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForRemoteToolAnyStaticMethod("remote_platform_security_posture", "system_platform_security_posture", "ComputerX.PlatformSecurity.PlatformSecurity", ComputerXAssemblyName, "Get", "GetAsync"),
        ToolCapabilityParityExpectationDescriptor.ForRemoteToolAnyStaticMethod("remote_app_control_posture", "system_app_control_posture", "ComputerX.AppControl.AppControl", ComputerXAssemblyName, "Get", "GetAsync"),
        ToolCapabilityParityExpectationDescriptor.ForRemoteToolAnyStaticMethod("remote_remote_access_posture", "system_remote_access_posture", "ComputerX.RemoteAccess.RemoteAccess", ComputerXAssemblyName, "Get", "GetAsync"),
        ToolCapabilityParityExpectationDescriptor.ForRemoteToolStaticMethod("remote_uac_posture", "system_uac_posture", "ComputerX.SecurityPolicy.UacPolicyQuery", "Get", ComputerXAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForRemoteToolAllStaticMethods("remote_ldap_policy_posture", "system_ldap_policy_posture", "ComputerX.SecurityPolicy.LdapPolicyQuery", ComputerXAssemblyName, "GetClient", "GetServer"),
        ToolCapabilityParityExpectationDescriptor.ForRemoteToolStaticMethod("remote_network_client_posture", "system_network_client_posture", "ComputerX.SecurityPolicy.NetworkClientPolicyQuery", "Get", ComputerXAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForRemoteToolStaticMethod("remote_account_policy_posture", "system_account_policy_posture", "ComputerX.SecurityPolicy.AccountPolicyQuery", "Get", ComputerXAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForRemoteToolStaticMethod("remote_interactive_logon_posture", "system_interactive_logon_posture", "ComputerX.SecurityPolicy.InteractiveLogonPolicyQuery", "Get", ComputerXAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForRemoteToolStaticMethod("remote_audit_options", "system_audit_options", "ComputerX.Audit.AuditOptionsQuery", "Get", ComputerXAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForRemoteToolStaticMethod("remote_builtin_accounts", "system_builtin_accounts", "ComputerX.SecurityPolicy.BuiltinAccountsQuery", "Get", ComputerXAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForRemoteToolStaticMethod("remote_device_guard_posture", "system_device_guard_posture", "ComputerX.SecurityPolicy.DeviceGuardPolicyQuery", "Get", ComputerXAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForRemoteToolStaticMethod("remote_defender_asr_posture", "system_defender_asr_posture", "ComputerX.SecurityPolicy.DefenderAsrPolicyQuery", "Get", ComputerXAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForRemoteToolStaticMethod("remote_windows_update_client_status", "system_windows_update_client_status", "ComputerX.Updates.WindowsUpdateClientStatusQuery", "Get", ComputerXAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForRemoteToolStaticMethod("remote_windows_update_telemetry", "system_windows_update_telemetry", "ComputerX.Updates.WindowsUpdateTelemetryQuery", "Get", ComputerXAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForRemoteToolAnyStaticMethod("remote_certificate_posture", "system_certificate_posture", "ComputerX.Certificates.CertificatePosture", ComputerXAssemblyName, "Get", "GetAsync"),
        ToolCapabilityParityExpectationDescriptor.ForRemoteToolAnyStaticMethod("remote_credential_posture", "system_credential_posture", "ComputerX.Credentials.CredentialPosture", ComputerXAssemblyName, "Get", "GetAsync"),
        ToolCapabilityParityExpectationDescriptor.ForRemoteToolStaticMethod("remote_installed_applications", "system_installed_applications", "ComputerX.InstalledApplications.InstalledApplications", "Query", ComputerXAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForRemoteToolStaticMethod("remote_updates_installed", "system_updates_installed", "ComputerX.Updates.Updates", "GetInstalledAsync", ComputerXAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForRemoteToolStaticMethod("remote_patch_compliance", "system_patch_compliance", "ComputerX.Updates.Updates", "GetInstalledAsync", ComputerXAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForRemoteToolProperty("remote_logical_disks", "system_logical_disks_list", "ComputerX.Storage.LogicalDiskInventoryQueryRequest", "ComputerName", ComputerXAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForRemoteToolProperty("remote_disk_inventory", "system_disks_list", "ComputerX.Storage.DiskInventoryQueryRequest", "ComputerName", ComputerXAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForRemoteToolProperty("remote_device_inventory", "system_devices_summary", "ComputerX.Devices.DeviceInventoryQueryRequest", "ComputerName", ComputerXAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForRemoteToolProperty("remote_hardware_summary", "system_hardware_summary", "ComputerX.Hardware.HardwareSummaryQueryRequest", "ComputerName", ComputerXAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForRemoteToolStaticMethod("remote_metrics_summary", "system_metrics_summary", "ComputerX.Diagnostics.SystemMetrics", "QueryRemoteAsync", ComputerXAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForRemoteToolProperty("remote_feature_inventory", "system_features_list", "ComputerX.Features.FeatureInventoryQueryRequest", "ComputerName", ComputerXAssemblyName)
    };

    /// <summary>
    /// Shared EventViewerX read-only capability expectations.
    /// </summary>
    public static IReadOnlyList<ToolCapabilityParityExpectationDescriptor> EventViewerXReadOnlyExpectations { get; } = new[] {
        ToolCapabilityParityExpectationDescriptor.ForToolParameterProperty("remote_channel_catalog", "eventlog_channels_list", RemoteMachineNameParameterName, "EventViewerX.Reports.Inventory.EventCatalogQueryRequest", "MachineName", EventViewerXAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForToolParameterProperty("remote_provider_catalog", "eventlog_providers_list", RemoteMachineNameParameterName, "EventViewerX.Reports.Inventory.EventCatalogQueryRequest", "MachineName", EventViewerXAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForToolParameterStaticMethod("remote_collector_subscription_catalog", "eventlog_collector_subscriptions_list", RemoteMachineNameParameterName, "EventViewerX.SearchEvents", "GetCollectorSubscriptions", EventViewerXAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForToolParameterProperty("remote_live_event_query", "eventlog_live_query", RemoteMachineNameParameterName, "EventViewerX.Reports.Live.LiveEventQueryRequest", "MachineName", EventViewerXAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForToolParameterProperty("remote_live_event_stats", "eventlog_live_stats", RemoteMachineNameParameterName, "EventViewerX.Reports.Live.LiveStatsQueryRequest", "MachineName", EventViewerXAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForToolParameterProperty("remote_top_events", "eventlog_top_events", RemoteMachineNameParameterName, "EventViewerX.Reports.Live.LiveEventQueryRequest", "MachineName", EventViewerXAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForToolStaticMethod("named_event_catalog", "eventlog_named_events_catalog", "EventViewerX.EventObjectSlim", "GetEventInfoForNamedEvents", EventViewerXAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForToolParameterStaticMethod("remote_named_event_query", "eventlog_named_events_query", RemoteMachineNameParameterName, "EventViewerX.SearchEvents", "FindEventsByNamedEvents", EventViewerXAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForToolParameterStaticMethod("remote_timeline_correlation", "eventlog_timeline_query", RemoteMachineNameParameterName, "EventViewerX.Reports.Correlation.NamedEventsTimelineQueryExecutor", "TryBuildAsync", EventViewerXAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForToolType("evtx_event_query", "eventlog_evtx_query", "EventViewerX.Reports.Evtx.EvtxEventReportResult", EventViewerXAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForToolType("evtx_event_stats", "eventlog_evtx_stats", "EventViewerX.Reports.Stats.EvtxStatsReport", EventViewerXAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForToolType("evtx_security_summary", "eventlog_evtx_security_summary", "EventViewerX.Reports.Security.SecurityEvtxQueryRequest", EventViewerXAssemblyName)
    };

    /// <summary>
    /// Shared EventViewerX governed-write capability expectations.
    /// </summary>
    public static IReadOnlyList<ToolCapabilityParityExpectationDescriptor> EventViewerXGovernedWriteExpectations { get; } = new[] {
        ToolCapabilityParityExpectationDescriptor.ForToolStaticMethod("channel_policy_write", "eventlog_channel_policy_set", "EventViewerX.SearchEvents", "SetChannelPolicyDetailed", EventViewerXAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForToolStaticMethod("classic_log_ensure_write", "eventlog_classic_log_ensure", "EventViewerX.SearchEvents", "CreateLog", EventViewerXAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForToolStaticMethod("classic_log_source_remove_write", "eventlog_classic_log_remove", "EventViewerX.SearchEvents", "RemoveSource", EventViewerXAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForToolStaticMethod("classic_log_remove_write", "eventlog_classic_log_remove", "EventViewerX.SearchEvents", "RemoveLog", EventViewerXAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForToolStaticMethod("collector_subscription_enabled_write", "eventlog_collector_subscription_set", "EventViewerX.SearchEvents", "SetCollectorSubscriptionEnabled", EventViewerXAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForToolStaticMethod("collector_subscription_xml_write", "eventlog_collector_subscription_set", "EventViewerX.SearchEvents", "SetCollectorSubscriptionXml", EventViewerXAssemblyName)
    };

    /// <summary>
    /// Shared TestimoX core read-only capability expectations.
    /// </summary>
    public static IReadOnlyList<ToolCapabilityParityExpectationDescriptor> TestimoXCoreReadOnlyExpectations { get; } = new[] {
        ToolCapabilityParityExpectationDescriptor.ForToolType("baseline_catalog", "testimox_baselines_list", "TestimoX.Baselines.BaselineListEntry", TestimoXAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForToolType("baseline_compare", "testimox_baseline_compare", "TestimoX.Baselines.BaselineComparisonRow", TestimoXAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForToolType("profile_catalog", "testimox_profiles_list", "TestimoX.Execution.RuleSelectionProfileInfo", TestimoXAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForToolType("rule_inventory", "testimox_rule_inventory", "TestimoX.Execution.RuleInventoryEntry", TestimoXAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForToolType("source_provenance", "testimox_source_query", "TestimoX.Execution.RuleOverview", TestimoXAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForToolType("baseline_crosswalk", "testimox_baseline_crosswalk", "TestimoX.Baselines.Crosswalk.RuleCrosswalkReport", TestimoXAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForToolType("run_catalog", "testimox_runs_list", "TestimoX.Execution.ToolingRuleRunRequest", TestimoXAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForToolType("run_summary", "testimox_run_summary", "TestimoX.Execution.ToolingRuleRunRequest", TestimoXAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForToolType("rule_catalog", "testimox_rules_list", "TestimoX.Execution.ToolingRuleDiscoveryRequest", TestimoXAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForToolType("rule_execution", "testimox_rules_run", "TestimoX.Execution.ToolingRuleRunRequest", TestimoXAssemblyName)
    };

    /// <summary>
    /// Shared TestimoX analytics read-only capability expectations.
    /// </summary>
    public static IReadOnlyList<ToolCapabilityParityExpectationDescriptor> TestimoXAnalyticsReadOnlyExpectations { get; } = new[] {
        ToolCapabilityParityExpectationDescriptor.ForToolType("analytics_diagnostics", "testimox_analytics_diagnostics_get", "ADPlayground.Monitoring.Diagnostics.MonitoringDiagnosticsSnapshot", AdMonitoringAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForToolType("probe_index_status", "testimox_probe_index_status", "ADPlayground.Monitoring.History.ProbeIndexStatusEntry", AdMonitoringAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForToolType("maintenance_window_history", "testimox_maintenance_window_history", "ADPlayground.Monitoring.Reporting.MaintenanceWindowHistoryEntry", AdMonitoringAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForToolType("report_data_snapshot", "testimox_report_data_snapshot_get", "ADPlayground.Monitoring.Reporting.MonitoringReportDataSnapshot", AdMonitoringAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForToolType("report_snapshot", "testimox_report_snapshot_get", "ADPlayground.Monitoring.Reporting.MonitoringReportSnapshot", AdMonitoringAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForToolType("monitoring_history", "testimox_history_query", "ADPlayground.Monitoring.Reporting.MonitoringAvailabilityRollupSample", AdMonitoringAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForToolType("report_job_history", "testimox_report_job_history", "ADPlayground.Monitoring.Reporting.MonitoringReportJobSummary", AdMonitoringAssemblyName)
    };

    /// <summary>
    /// Shared ADPlayground monitoring read-only capability expectations.
    /// </summary>
    public static IReadOnlyList<ToolCapabilityParityExpectationDescriptor> AdMonitoringReadOnlyExpectations { get; } = new[] {
        ToolCapabilityParityExpectationDescriptor.ForToolType("service_heartbeat", "ad_monitoring_service_heartbeat_get", "ADPlayground.Monitoring.Diagnostics.MonitoringServiceHeartbeatSnapshot", AdMonitoringAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForToolType("diagnostics_snapshot", "ad_monitoring_diagnostics_get", "ADPlayground.Monitoring.Diagnostics.MonitoringDiagnosticsSnapshot", AdMonitoringAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForToolType("metrics_snapshot", "ad_monitoring_metrics_get", "ADPlayground.Monitoring.Diagnostics.MonitoringMetricsSnapshot", AdMonitoringAssemblyName),
        ToolCapabilityParityExpectationDescriptor.ForToolType("dashboard_state", "ad_monitoring_dashboard_state_get", "ADPlayground.Monitoring.Diagnostics.MonitoringDashboardAutoGenerateSnapshot", AdMonitoringAssemblyName)
    };
}
