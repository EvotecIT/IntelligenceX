using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ADPlayground.Monitoring.Diagnostics;
using ADPlayground.Monitoring.Probes;
using ADPlayground.Network;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.ADPlayground;
using IntelligenceX.Tools.Common;
using IntelligenceX.Tools.System;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public class SystemAdMonitoringParityTests {
    private static readonly MethodInfo BuildMonitoringProbeChainContractMethod =
        typeof(AdMonitoringProbeRunTool).GetMethod("BuildChainContract", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("BuildChainContract not found.");

    private static void AssertRouteBindsToTarget(
        ToolDefinition definition,
        string expectedPackId,
        string expectedToolName,
        string expectedTargetArgument,
        params string[] expectedSourceFields) {
        var handoff = Assert.IsType<ToolHandoffContract>(definition.Handoff);
        var route = Assert.Single(
            handoff.OutboundRoutes,
            candidate => string.Equals(candidate.TargetPackId, expectedPackId, StringComparison.OrdinalIgnoreCase)
                         && string.Equals(candidate.TargetToolName, expectedToolName, StringComparison.OrdinalIgnoreCase));

        Assert.Equal(expectedSourceFields.Length, route.Bindings.Count);
        foreach (var sourceField in expectedSourceFields) {
            Assert.Contains(
                route.Bindings,
                binding => string.Equals(binding.SourceField, sourceField, StringComparison.OrdinalIgnoreCase)
                           && string.Equals(binding.TargetArgument, expectedTargetArgument, StringComparison.OrdinalIgnoreCase)
                           && !binding.IsRequired);
        }
    }

    [Theory]
    [InlineData(typeof(SystemHardwareSummaryTool))]
    [InlineData(typeof(SystemMetricsSummaryTool))]
    [InlineData(typeof(SystemInfoTool))]
    [InlineData(typeof(SystemProcessListTool))]
    [InlineData(typeof(SystemNetworkAdaptersTool))]
    [InlineData(typeof(SystemLocalIdentityInventoryTool))]
    [InlineData(typeof(SystemPortsListTool))]
    [InlineData(typeof(SystemServiceListTool))]
    [InlineData(typeof(SystemScheduledTasksListTool))]
    [InlineData(typeof(SystemDevicesSummaryTool))]
    [InlineData(typeof(SystemFeaturesListTool))]
    [InlineData(typeof(SystemPrivacyPostureTool))]
    [InlineData(typeof(SystemExploitProtectionTool))]
    [InlineData(typeof(SystemOfficePostureTool))]
    [InlineData(typeof(SystemBrowserPostureTool))]
    [InlineData(typeof(SystemBackupPostureTool))]
    [InlineData(typeof(SystemTlsPostureTool))]
    [InlineData(typeof(SystemWinRmPostureTool))]
    [InlineData(typeof(SystemPowerShellLoggingPostureTool))]
    [InlineData(typeof(SystemUacPostureTool))]
    [InlineData(typeof(SystemLdapPolicyPostureTool))]
    [InlineData(typeof(SystemNetworkClientPostureTool))]
    [InlineData(typeof(SystemAccountPolicyPostureTool))]
    [InlineData(typeof(SystemInteractiveLogonPostureTool))]
    [InlineData(typeof(SystemDeviceGuardPostureTool))]
    [InlineData(typeof(SystemDefenderAsrPostureTool))]
    [InlineData(typeof(SystemWindowsUpdateClientStatusTool))]
    [InlineData(typeof(SystemWindowsUpdateTelemetryTool))]
    [InlineData(typeof(SystemCertificatePostureTool))]
    [InlineData(typeof(SystemCredentialPostureTool))]
    public void RemoteComputerXParityTools_Definition_ShouldExposeOptionalComputerName(Type toolType) {
        var tool = (ITool)Activator.CreateInstance(toolType, new SystemToolOptions())!;
        var properties = tool.Definition.Parameters?.GetObject("properties");

        Assert.NotNull(properties);
        Assert.NotNull(properties!.GetObject("computer_name"));
    }

    [Fact]
    public void SystemPackRegistry_ShouldExposeNewRemoteParityTools() {
        var registry = new ToolRegistry();
        registry.RegisterSystemPack(new SystemToolOptions());

        var names = registry.GetDefinitions()
            .Select(static definition => definition.Name)
            .ToArray();

        Assert.Contains("system_metrics_summary", names, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("system_local_identity_inventory", names, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("system_privacy_posture", names, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("system_exploit_protection", names, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("system_office_posture", names, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("system_browser_posture", names, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("system_backup_posture", names, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("system_tls_posture", names, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("system_winrm_posture", names, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("system_powershell_logging_posture", names, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("system_uac_posture", names, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("system_ldap_policy_posture", names, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("system_network_client_posture", names, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("system_account_policy_posture", names, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("system_interactive_logon_posture", names, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("system_device_guard_posture", names, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("system_defender_asr_posture", names, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("system_windows_update_client_status", names, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("system_windows_update_telemetry", names, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("system_certificate_posture", names, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("system_credential_posture", names, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AdMonitoringProbeCatalog_ShouldListWindowsUpdateProbe() {
        var tool = new AdMonitoringProbeCatalogTool(new ActiveDirectoryToolOptions());
        var json = await tool.InvokeAsync(arguments: null, cancellationToken: CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        var probeKinds = document.RootElement.GetProperty("probe_kinds")
            .EnumerateArray()
            .Select(static node => node.GetProperty("probe_kind").GetString())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        Assert.Contains("windows_update", probeKinds, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void AdMonitoringProbeRun_Definition_ShouldAcceptWindowsUpdateProbeKindAndRequireWsusOption() {
        var tool = new AdMonitoringProbeRunTool(new ActiveDirectoryToolOptions());
        var schema = tool.Definition.Parameters;
        Assert.NotNull(schema);

        var properties = schema!.GetObject("properties");
        Assert.NotNull(properties);

        var probeKind = properties!.GetObject("probe_kind");
        Assert.NotNull(probeKind);

        var enumValues = probeKind!.GetArray("enum");
        Assert.NotNull(enumValues);
        Assert.Contains(
            "windows_update",
            enumValues!.Select(static value => value.AsString()).Where(static value => !string.IsNullOrWhiteSpace(value)),
            StringComparer.OrdinalIgnoreCase);

        Assert.NotNull(properties.GetObject("require_wsus"));
    }

    [Fact]
    public void ActiveDirectoryPackRegistry_ShouldExposeMonitoringRuntimeStateTools() {
        var registry = new ToolRegistry();
        registry.RegisterActiveDirectoryPack(new ActiveDirectoryToolOptions());

        var names = registry.GetDefinitions()
            .Select(static definition => definition.Name)
            .ToArray();

        Assert.Contains("ad_monitoring_service_heartbeat_get", names, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("ad_monitoring_diagnostics_get", names, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("ad_monitoring_metrics_get", names, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("ad_monitoring_dashboard_state_get", names, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ActiveDirectoryDiscoveryTools_ShouldDeclareSystemAndEventLogPivotHandoff() {
        var registry = new ToolRegistry();
        registry.RegisterActiveDirectoryPack(new ActiveDirectoryToolOptions());

        var definitionsByName = registry.GetDefinitions()
            .ToDictionary(static definition => definition.Name, StringComparer.OrdinalIgnoreCase);

        var scopeDiscovery = Assert.IsType<ToolDefinition>(definitionsByName["ad_scope_discovery"]);
        var environmentDiscover = Assert.IsType<ToolDefinition>(definitionsByName["ad_environment_discover"]);
        var monitoringProbeRun = Assert.IsType<ToolDefinition>(definitionsByName["ad_monitoring_probe_run"]);

        Assert.Contains(
            scopeDiscovery.Handoff?.OutboundRoutes ?? Array.Empty<ToolHandoffRoute>(),
            static route => string.Equals(route.TargetPackId, "system", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "system_metrics_summary", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            scopeDiscovery.Handoff?.OutboundRoutes ?? Array.Empty<ToolHandoffRoute>(),
            static route => string.Equals(route.TargetPackId, "eventlog", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "eventlog_channels_list", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            environmentDiscover.Handoff?.OutboundRoutes ?? Array.Empty<ToolHandoffRoute>(),
            static route => string.Equals(route.TargetPackId, "system", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "system_info", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            environmentDiscover.Handoff?.OutboundRoutes ?? Array.Empty<ToolHandoffRoute>(),
            static route => string.Equals(route.TargetPackId, "eventlog", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "eventlog_channels_list", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            monitoringProbeRun.Handoff?.OutboundRoutes ?? Array.Empty<ToolHandoffRoute>(),
            static route => string.Equals(route.TargetPackId, "system", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "system_time_sync", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            monitoringProbeRun.Handoff?.OutboundRoutes ?? Array.Empty<ToolHandoffRoute>(),
            static route => string.Equals(route.TargetPackId, "eventlog", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "eventlog_channels_list", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            monitoringProbeRun.Handoff?.OutboundRoutes ?? Array.Empty<ToolHandoffRoute>(),
            static route => string.Equals(route.TargetPackId, "system", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "system_windows_update_client_status", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            monitoringProbeRun.Handoff?.OutboundRoutes ?? Array.Empty<ToolHandoffRoute>(),
            static route => string.Equals(route.TargetPackId, "system", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "system_windows_update_telemetry", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            monitoringProbeRun.Handoff?.OutboundRoutes ?? Array.Empty<ToolHandoffRoute>(),
            static route => string.Equals(route.TargetPackId, "system", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "system_tls_posture", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            monitoringProbeRun.Handoff?.OutboundRoutes ?? Array.Empty<ToolHandoffRoute>(),
            static route => string.Equals(route.TargetPackId, "system", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "system_winrm_posture", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            monitoringProbeRun.Handoff?.OutboundRoutes ?? Array.Empty<ToolHandoffRoute>(),
            static route => string.Equals(route.TargetPackId, "system", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "system_powershell_logging_posture", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            monitoringProbeRun.Handoff?.OutboundRoutes ?? Array.Empty<ToolHandoffRoute>(),
            static route => string.Equals(route.TargetPackId, "system", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "system_uac_posture", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            monitoringProbeRun.Handoff?.OutboundRoutes ?? Array.Empty<ToolHandoffRoute>(),
            static route => string.Equals(route.TargetPackId, "system", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "system_ldap_policy_posture", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            monitoringProbeRun.Handoff?.OutboundRoutes ?? Array.Empty<ToolHandoffRoute>(),
            static route => string.Equals(route.TargetPackId, "system", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "system_network_client_posture", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            monitoringProbeRun.Handoff?.OutboundRoutes ?? Array.Empty<ToolHandoffRoute>(),
            static route => string.Equals(route.TargetPackId, "system", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "system_account_policy_posture", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            monitoringProbeRun.Handoff?.OutboundRoutes ?? Array.Empty<ToolHandoffRoute>(),
            static route => string.Equals(route.TargetPackId, "system", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "system_interactive_logon_posture", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            monitoringProbeRun.Handoff?.OutboundRoutes ?? Array.Empty<ToolHandoffRoute>(),
            static route => string.Equals(route.TargetPackId, "system", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "system_device_guard_posture", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            monitoringProbeRun.Handoff?.OutboundRoutes ?? Array.Empty<ToolHandoffRoute>(),
            static route => string.Equals(route.TargetPackId, "system", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "system_defender_asr_posture", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            monitoringProbeRun.Handoff?.OutboundRoutes ?? Array.Empty<ToolHandoffRoute>(),
            static route => string.Equals(route.TargetPackId, "system", StringComparison.OrdinalIgnoreCase)
                            && string.Equals(route.TargetToolName, "system_certificate_posture", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ActiveDirectoryDiscoveryTools_ShouldBindRemotePivotRoutesToCanonicalHostArguments() {
        var registry = new ToolRegistry();
        registry.RegisterActiveDirectoryPack(new ActiveDirectoryToolOptions());

        var definitionsByName = registry.GetDefinitions()
            .ToDictionary(static definition => definition.Name, StringComparer.OrdinalIgnoreCase);

        var scopeDiscovery = Assert.IsType<ToolDefinition>(definitionsByName["ad_scope_discovery"]);
        var environmentDiscover = Assert.IsType<ToolDefinition>(definitionsByName["ad_environment_discover"]);
        var monitoringProbeRun = Assert.IsType<ToolDefinition>(definitionsByName["ad_monitoring_probe_run"]);

        AssertRouteBindsToTarget(scopeDiscovery, "system", "system_metrics_summary", "computer_name",
            "domain_controllers/0/value", "requested_scope/domain_controller");
        AssertRouteBindsToTarget(scopeDiscovery, "system", "system_logical_disks_list", "computer_name",
            "domain_controllers/0/value", "requested_scope/domain_controller");
        AssertRouteBindsToTarget(scopeDiscovery, "eventlog", "eventlog_channels_list", "machine_name",
            "domain_controllers/0/value", "requested_scope/domain_controller");

        AssertRouteBindsToTarget(environmentDiscover, "system", "system_info", "computer_name",
            "context/domain_controller", "domain_controllers/0/value");
        AssertRouteBindsToTarget(environmentDiscover, "system", "system_metrics_summary", "computer_name",
            "context/domain_controller", "domain_controllers/0/value");
        AssertRouteBindsToTarget(environmentDiscover, "eventlog", "eventlog_channels_list", "machine_name",
            "context/domain_controller", "domain_controllers/0/value");

        AssertRouteBindsToTarget(monitoringProbeRun, "system", "system_time_sync", "computer_name",
            "normalized_request/domain_controller", "normalized_request/targets/0");
        AssertRouteBindsToTarget(monitoringProbeRun, "system", "system_windows_update_client_status", "computer_name",
            "normalized_request/domain_controller", "normalized_request/targets/0");
        AssertRouteBindsToTarget(monitoringProbeRun, "system", "system_logical_disks_list", "computer_name",
            "normalized_request/domain_controller", "normalized_request/targets/0");
        AssertRouteBindsToTarget(monitoringProbeRun, "eventlog", "eventlog_channels_list", "machine_name",
            "normalized_request/domain_controller", "normalized_request/targets/0");
    }

    [Fact]
    public void SystemRemoteParityTools_ShouldBindReverseAdAndEventLogHandoffsToCanonicalArguments() {
        var registry = new ToolRegistry();
        registry.RegisterSystemPack(new SystemToolOptions());

        var definitionsByName = registry.GetDefinitions()
            .ToDictionary(static definition => definition.Name, StringComparer.OrdinalIgnoreCase);

        AssertRouteBindsToTarget(definitionsByName["system_info"], "active_directory", "ad_scope_discovery", "domain_controller",
            "meta/computer_name", "computer_name");
        AssertRouteBindsToTarget(definitionsByName["system_info"], "eventlog", "eventlog_channels_list", "machine_name",
            "meta/computer_name", "computer_name");
        AssertRouteBindsToTarget(definitionsByName["system_metrics_summary"], "active_directory", "ad_scope_discovery", "domain_controller",
            "meta/computer_name", "computer_name");
        AssertRouteBindsToTarget(definitionsByName["system_metrics_summary"], "eventlog", "eventlog_channels_list", "machine_name",
            "meta/computer_name", "computer_name");
        AssertRouteBindsToTarget(definitionsByName["system_time_sync"], "active_directory", "ad_scope_discovery", "domain_controller",
            "meta/computer_name", "computer_name");
        AssertRouteBindsToTarget(definitionsByName["system_time_sync"], "eventlog", "eventlog_channels_list", "machine_name",
            "meta/computer_name", "computer_name");
        AssertRouteBindsToTarget(definitionsByName["system_logical_disks_list"], "active_directory", "ad_scope_discovery", "domain_controller",
            "meta/computer_name", "computer_name");
        AssertRouteBindsToTarget(definitionsByName["system_logical_disks_list"], "eventlog", "eventlog_channels_list", "machine_name",
            "meta/computer_name", "computer_name");
    }

    [Fact]
    public async Task AdMonitoringProbeCatalog_ShouldExposePreferredFollowUpTools() {
        var tool = new AdMonitoringProbeCatalogTool(new ActiveDirectoryToolOptions());
        var json = await tool.InvokeAsync(arguments: null, cancellationToken: CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        var probeKinds = document.RootElement.GetProperty("probe_kinds").EnumerateArray().ToArray();
        var ntp = Assert.Single(probeKinds, static node => string.Equals(node.GetProperty("probe_kind").GetString(), "ntp", StringComparison.OrdinalIgnoreCase));
        var ldap = Assert.Single(probeKinds, static node => string.Equals(node.GetProperty("probe_kind").GetString(), "ldap", StringComparison.OrdinalIgnoreCase));
        var directory = Assert.Single(probeKinds, static node => string.Equals(node.GetProperty("probe_kind").GetString(), "directory", StringComparison.OrdinalIgnoreCase));

        Assert.Contains(
            "system_time_sync",
            ntp.GetProperty("preferred_follow_up_tools").EnumerateArray().Select(static x => x.GetString()),
            StringComparer.OrdinalIgnoreCase);
        Assert.Contains(
            "system_windows_update_client_status",
            Assert.Single(probeKinds, static node => string.Equals(node.GetProperty("probe_kind").GetString(), "windows_update", StringComparison.OrdinalIgnoreCase))
                .GetProperty("preferred_follow_up_tools").EnumerateArray().Select(static x => x.GetString()),
            StringComparer.OrdinalIgnoreCase);
        Assert.Contains(
            "system_tls_posture",
            Assert.Single(probeKinds, static node => string.Equals(node.GetProperty("probe_kind").GetString(), "https", StringComparison.OrdinalIgnoreCase))
                .GetProperty("preferred_follow_up_tools").EnumerateArray().Select(static x => x.GetString()),
            StringComparer.OrdinalIgnoreCase);
        Assert.Contains(
            "system_ldap_policy_posture",
            ldap.GetProperty("preferred_follow_up_tools").EnumerateArray().Select(static x => x.GetString()),
            StringComparer.OrdinalIgnoreCase);
        Assert.Contains(
            "system_network_client_posture",
            Assert.Single(probeKinds, static node => string.Equals(node.GetProperty("probe_kind").GetString(), "dns", StringComparison.OrdinalIgnoreCase))
                .GetProperty("preferred_follow_up_tools").EnumerateArray().Select(static x => x.GetString()),
            StringComparer.OrdinalIgnoreCase);
        Assert.Contains(
            "ad_ldap_diagnostics",
            ldap.GetProperty("preferred_follow_up_tools").EnumerateArray().Select(static x => x.GetString()),
            StringComparer.OrdinalIgnoreCase);
        Assert.True(directory.TryGetProperty("directory_probe_sub_kinds", out var directorySubKinds));
        Assert.Equal(global::System.Text.Json.JsonValueKind.Array, directorySubKinds.ValueKind);
    }

    [Fact]
    public async Task AdMonitoringProbeCatalog_ShouldExposeDirectoryProbeSubKindsWithSpecificFollowUps() {
        var tool = new AdMonitoringProbeCatalogTool(new ActiveDirectoryToolOptions());
        var json = await tool.InvokeAsync(arguments: null, cancellationToken: CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        var directory = Assert.Single(
            document.RootElement.GetProperty("probe_kinds").EnumerateArray(),
            static node => string.Equals(node.GetProperty("probe_kind").GetString(), "directory", StringComparison.OrdinalIgnoreCase));
        var subKinds = directory.GetProperty("directory_probe_sub_kinds").EnumerateArray().ToArray();

        var ldapSearch = Assert.Single(
            subKinds,
            static node => string.Equals(node.GetProperty("directory_probe_kind").GetString(), "ldap_search", StringComparison.OrdinalIgnoreCase));
        var rpcEndpoint = Assert.Single(
            subKinds,
            static node => string.Equals(node.GetProperty("directory_probe_kind").GetString(), "rpc_endpoint", StringComparison.OrdinalIgnoreCase));
        var sysvolGpt = Assert.Single(
            subKinds,
            static node => string.Equals(node.GetProperty("directory_probe_kind").GetString(), "sysvol_gpt", StringComparison.OrdinalIgnoreCase));
        var dnsRegistration = Assert.Single(
            subKinds,
            static node => string.Equals(node.GetProperty("directory_probe_kind").GetString(), "dns_registration", StringComparison.OrdinalIgnoreCase));

        Assert.Contains(
            "ad_ldap_diagnostics",
            ldapSearch.GetProperty("preferred_follow_up_tools").EnumerateArray().Select(static x => x.GetString()),
            StringComparer.OrdinalIgnoreCase);
        Assert.Contains(
            "system_ports_list",
            rpcEndpoint.GetProperty("preferred_follow_up_tools").EnumerateArray().Select(static x => x.GetString()),
            StringComparer.OrdinalIgnoreCase);
        Assert.Contains(
            "system_logical_disks_list",
            sysvolGpt.GetProperty("preferred_follow_up_tools").EnumerateArray().Select(static x => x.GetString()),
            StringComparer.OrdinalIgnoreCase);
        Assert.Contains(
            "system_network_client_posture",
            dnsRegistration.GetProperty("preferred_follow_up_tools").EnumerateArray().Select(static x => x.GetString()),
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AdMonitoringProbeCatalog_ShouldExposeFollowUpProfilesForKerberosDnsServiceAndReplication() {
        var tool = new AdMonitoringProbeCatalogTool(new ActiveDirectoryToolOptions());
        var json = await tool.InvokeAsync(arguments: null, cancellationToken: CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        var probeKinds = document.RootElement.GetProperty("probe_kinds").EnumerateArray().ToArray();
        var kerberos = Assert.Single(probeKinds, static node => string.Equals(node.GetProperty("probe_kind").GetString(), "kerberos", StringComparison.OrdinalIgnoreCase));
        var dnsService = Assert.Single(probeKinds, static node => string.Equals(node.GetProperty("probe_kind").GetString(), "dns_service", StringComparison.OrdinalIgnoreCase));
        var replication = Assert.Single(probeKinds, static node => string.Equals(node.GetProperty("probe_kind").GetString(), "replication", StringComparison.OrdinalIgnoreCase));

        var kerberosProfiles = kerberos.GetProperty("follow_up_profiles").EnumerateArray().ToArray();
        var dnsServiceProfiles = dnsService.GetProperty("follow_up_profiles").EnumerateArray().ToArray();
        var replicationProfiles = replication.GetProperty("follow_up_profiles").EnumerateArray().ToArray();

        Assert.Contains(
            "system_time_sync",
            Assert.Single(kerberosProfiles, static node => string.Equals(node.GetProperty("id").GetString(), "time_skew_and_kdc_health", StringComparison.OrdinalIgnoreCase))
                .GetProperty("preferred_follow_up_tools").EnumerateArray().Select(static x => x.GetString()),
            StringComparer.OrdinalIgnoreCase);
        Assert.Contains(
            "system_network_client_posture",
            Assert.Single(dnsServiceProfiles, static node => string.Equals(node.GetProperty("id").GetString(), "srv_answer_validation", StringComparison.OrdinalIgnoreCase))
                .GetProperty("preferred_follow_up_tools").EnumerateArray().Select(static x => x.GetString()),
            StringComparer.OrdinalIgnoreCase);
        Assert.Contains(
            "system_logical_disks_list",
            Assert.Single(replicationProfiles, static node => string.Equals(node.GetProperty("id").GetString(), "sysvol_follow_through", StringComparison.OrdinalIgnoreCase))
                .GetProperty("preferred_follow_up_tools").EnumerateArray().Select(static x => x.GetString()),
            StringComparer.OrdinalIgnoreCase);
        Assert.Contains(
            "system_ports_list",
            Assert.Single(replicationProfiles, static node => string.Equals(node.GetProperty("id").GetString(), "connectivity_preflight", StringComparison.OrdinalIgnoreCase))
                .GetProperty("preferred_follow_up_tools").EnumerateArray().Select(static x => x.GetString()),
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AdMonitoringProbeCatalog_ShouldExposeResultSignalProfilesForKerberosDnsServiceAndReplication() {
        var tool = new AdMonitoringProbeCatalogTool(new ActiveDirectoryToolOptions());
        var json = await tool.InvokeAsync(arguments: null, cancellationToken: CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        var probeKinds = document.RootElement.GetProperty("probe_kinds").EnumerateArray().ToArray();
        var kerberos = Assert.Single(probeKinds, static node => string.Equals(node.GetProperty("probe_kind").GetString(), "kerberos", StringComparison.OrdinalIgnoreCase));
        var dnsService = Assert.Single(probeKinds, static node => string.Equals(node.GetProperty("probe_kind").GetString(), "dns_service", StringComparison.OrdinalIgnoreCase));
        var replication = Assert.Single(probeKinds, static node => string.Equals(node.GetProperty("probe_kind").GetString(), "replication", StringComparison.OrdinalIgnoreCase));

        var kerberosSignals = kerberos.GetProperty("result_signal_profiles").EnumerateArray().ToArray();
        var dnsServiceSignals = dnsService.GetProperty("result_signal_profiles").EnumerateArray().ToArray();
        var replicationSignals = replication.GetProperty("result_signal_profiles").EnumerateArray().ToArray();

        Assert.Contains(
            "system_time_sync",
            Assert.Single(kerberosSignals, static node => string.Equals(node.GetProperty("id").GetString(), "clock_skew_or_kdc_latency", StringComparison.OrdinalIgnoreCase))
                .GetProperty("preferred_follow_up_tools").EnumerateArray().Select(static x => x.GetString()),
            StringComparer.OrdinalIgnoreCase);
        Assert.Contains(
            "system_network_client_posture",
            Assert.Single(dnsServiceSignals, static node => string.Equals(node.GetProperty("id").GetString(), "missing_answers_or_nxdomain", StringComparison.OrdinalIgnoreCase))
                .GetProperty("preferred_follow_up_tools").EnumerateArray().Select(static x => x.GetString()),
            StringComparer.OrdinalIgnoreCase);
        Assert.Contains(
            "system_service_list",
            Assert.Single(replicationSignals, static node => string.Equals(node.GetProperty("id").GetString(), "sysvol_or_share_failure", StringComparison.OrdinalIgnoreCase))
                .GetProperty("preferred_follow_up_tools").EnumerateArray().Select(static x => x.GetString()),
            StringComparer.OrdinalIgnoreCase);
        Assert.Contains(
            "system_ports_list",
            Assert.Single(replicationSignals, static node => string.Equals(node.GetProperty("id").GetString(), "endpoint_connectivity_failure", StringComparison.OrdinalIgnoreCase))
                .GetProperty("preferred_follow_up_tools").EnumerateArray().Select(static x => x.GetString()),
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AdMonitoringProbeCatalog_ShouldExposeProfilesForHttpsPortAndAdws() {
        var tool = new AdMonitoringProbeCatalogTool(new ActiveDirectoryToolOptions());
        var json = await tool.InvokeAsync(arguments: null, cancellationToken: CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        var probeKinds = document.RootElement.GetProperty("probe_kinds").EnumerateArray().ToArray();
        var https = Assert.Single(probeKinds, static node => string.Equals(node.GetProperty("probe_kind").GetString(), "https", StringComparison.OrdinalIgnoreCase));
        var port = Assert.Single(probeKinds, static node => string.Equals(node.GetProperty("probe_kind").GetString(), "port", StringComparison.OrdinalIgnoreCase));
        var adws = Assert.Single(probeKinds, static node => string.Equals(node.GetProperty("probe_kind").GetString(), "adws", StringComparison.OrdinalIgnoreCase));

        var httpsSignals = https.GetProperty("result_signal_profiles").EnumerateArray().ToArray();
        var portSignals = port.GetProperty("result_signal_profiles").EnumerateArray().ToArray();
        var adwsSignals = adws.GetProperty("result_signal_profiles").EnumerateArray().ToArray();

        Assert.Contains(
            "system_tls_posture",
            Assert.Single(httpsSignals, static node => string.Equals(node.GetProperty("id").GetString(), "tls_or_chain_failure", StringComparison.OrdinalIgnoreCase))
                .GetProperty("preferred_follow_up_tools").EnumerateArray().Select(static x => x.GetString()),
            StringComparer.OrdinalIgnoreCase);
        Assert.Contains(
            "system_ports_list",
            Assert.Single(portSignals, static node => string.Equals(node.GetProperty("id").GetString(), "rpc_or_listener_missing", StringComparison.OrdinalIgnoreCase))
                .GetProperty("preferred_follow_up_tools").EnumerateArray().Select(static x => x.GetString()),
            StringComparer.OrdinalIgnoreCase);
        Assert.Contains(
            "system_service_list",
            Assert.Single(adwsSignals, static node => string.Equals(node.GetProperty("id").GetString(), "authentication_or_bind_failure", StringComparison.OrdinalIgnoreCase))
                .GetProperty("preferred_follow_up_tools").EnumerateArray().Select(static x => x.GetString()),
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AdMonitoringProbeCatalog_ShouldExposeProfilesForLdapDnsNtpPingAndWindowsUpdate() {
        var tool = new AdMonitoringProbeCatalogTool(new ActiveDirectoryToolOptions());
        var json = await tool.InvokeAsync(arguments: null, cancellationToken: CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        var probeKinds = document.RootElement.GetProperty("probe_kinds").EnumerateArray().ToArray();
        var ldap = Assert.Single(probeKinds, static node => string.Equals(node.GetProperty("probe_kind").GetString(), "ldap", StringComparison.OrdinalIgnoreCase));
        var dns = Assert.Single(probeKinds, static node => string.Equals(node.GetProperty("probe_kind").GetString(), "dns", StringComparison.OrdinalIgnoreCase));
        var ntp = Assert.Single(probeKinds, static node => string.Equals(node.GetProperty("probe_kind").GetString(), "ntp", StringComparison.OrdinalIgnoreCase));
        var ping = Assert.Single(probeKinds, static node => string.Equals(node.GetProperty("probe_kind").GetString(), "ping", StringComparison.OrdinalIgnoreCase));
        var windowsUpdate = Assert.Single(probeKinds, static node => string.Equals(node.GetProperty("probe_kind").GetString(), "windows_update", StringComparison.OrdinalIgnoreCase));

        Assert.Contains(
            "ad_ldap_diagnostics",
            Assert.Single(ldap.GetProperty("follow_up_profiles").EnumerateArray(), static node => string.Equals(node.GetProperty("id").GetString(), "ldaps_certificate_focus", StringComparison.OrdinalIgnoreCase))
                .GetProperty("preferred_follow_up_tools").EnumerateArray().Select(static x => x.GetString()),
            StringComparer.OrdinalIgnoreCase);
        Assert.Contains(
            "system_network_client_posture",
            Assert.Single(dns.GetProperty("result_signal_profiles").EnumerateArray(), static node => string.Equals(node.GetProperty("id").GetString(), "missing_or_wrong_answers", StringComparison.OrdinalIgnoreCase))
                .GetProperty("preferred_follow_up_tools").EnumerateArray().Select(static x => x.GetString()),
            StringComparer.OrdinalIgnoreCase);
        Assert.Contains(
            "system_time_sync",
            Assert.Single(ntp.GetProperty("result_signal_profiles").EnumerateArray(), static node => string.Equals(node.GetProperty("id").GetString(), "clock_skew_detected", StringComparison.OrdinalIgnoreCase))
                .GetProperty("preferred_follow_up_tools").EnumerateArray().Select(static x => x.GetString()),
            StringComparer.OrdinalIgnoreCase);
        Assert.Contains(
            "system_metrics_summary",
            Assert.Single(ping.GetProperty("result_signal_profiles").EnumerateArray(), static node => string.Equals(node.GetProperty("id").GetString(), "high_latency_or_jitter", StringComparison.OrdinalIgnoreCase))
                .GetProperty("preferred_follow_up_tools").EnumerateArray().Select(static x => x.GetString()),
            StringComparer.OrdinalIgnoreCase);
        Assert.Contains(
            "system_patch_compliance",
            Assert.Single(windowsUpdate.GetProperty("result_signal_profiles").EnumerateArray(), static node => string.Equals(node.GetProperty("id").GetString(), "missing_updates_or_reboot_required", StringComparison.OrdinalIgnoreCase))
                .GetProperty("preferred_follow_up_tools").EnumerateArray().Select(static x => x.GetString()),
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void AdMonitoringProbeRun_ChainContract_ShouldPreferLdapDiagnosticsForLdapFollowUp() {
        var chain = Assert.IsType<ToolChainContractModel>(BuildMonitoringProbeChainContractMethod.Invoke(
            null,
            new object?[] {
                "ldap",
                null,
                new ProbeResult { Status = ProbeStatus.Degraded, Target = "dc01.contoso.com" },
                new[] { "dc01.contoso.com" },
                "contoso.com",
                string.Empty,
                false,
                DirectoryDiscoveryFallback.CurrentDomain
            }));

        var ldapAction = Assert.Single(chain.NextActions, static action => string.Equals(action.Tool, "ad_ldap_diagnostics", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("dc01.contoso.com", ldapAction.SuggestedArguments["domain_controller"]);
        Assert.Equal("True", ldapAction.SuggestedArguments["verify_certificate"]);
    }

    [Fact]
    public void AdMonitoringProbeRun_ChainContract_ShouldPromoteWindowsUpdateHostFollowUps() {
        var chain = Assert.IsType<ToolChainContractModel>(BuildMonitoringProbeChainContractMethod.Invoke(
            null,
            new object?[] {
                "windows_update",
                null,
                new ProbeResult { Status = ProbeStatus.Down, Target = "dc02.contoso.com" },
                new[] { "dc02.contoso.com" },
                "contoso.com",
                string.Empty,
                false,
                DirectoryDiscoveryFallback.CurrentDomain
            }));

        Assert.Contains(chain.NextActions, static action => string.Equals(action.Tool, "system_windows_update_client_status", StringComparison.OrdinalIgnoreCase)
                                                            && string.Equals(action.SuggestedArguments["computer_name"], "dc02.contoso.com", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(chain.NextActions, static action => string.Equals(action.Tool, "system_windows_update_telemetry", StringComparison.OrdinalIgnoreCase)
                                                            && string.Equals(action.SuggestedArguments["computer_name"], "dc02.contoso.com", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AdMonitoringProbeRun_ChainContract_ShouldPromoteDirectoryRpcEndpointHostFollowUps() {
        var chain = Assert.IsType<ToolChainContractModel>(BuildMonitoringProbeChainContractMethod.Invoke(
            null,
            new object?[] {
                "directory",
                "rpc_endpoint",
                new ProbeResult { Status = ProbeStatus.Down, Target = "dc03.contoso.com" },
                new[] { "dc03.contoso.com" },
                "contoso.com",
                string.Empty,
                false,
                DirectoryDiscoveryFallback.CurrentForest
            }));

        Assert.Contains(chain.NextActions, static action => string.Equals(action.Tool, "system_ports_list", StringComparison.OrdinalIgnoreCase)
                                                            && string.Equals(action.SuggestedArguments["computer_name"], "dc03.contoso.com", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(chain.NextActions, static action => string.Equals(action.Tool, "system_service_list", StringComparison.OrdinalIgnoreCase)
                                                            && string.Equals(action.SuggestedArguments["computer_name"], "dc03.contoso.com", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AdMonitoringServiceHeartbeatGetTool_ShouldLoadSnapshotFromAllowedRoot() {
        var monitoringDirectory = CreateMonitoringDirectory();
        var snapshotPath = Path.Combine(monitoringDirectory, MonitoringServiceHeartbeatSnapshot.DefaultFileName);
        File.WriteAllText(snapshotPath, JsonSerializer.Serialize(new MonitoringServiceHeartbeatSnapshot {
            GeneratedUtc = DateTimeOffset.UtcNow,
            AgentName = "ad-agent-01",
            ProbeCount = 12,
            UptimeSeconds = 1234,
            DashboardInFlight = true,
            StallDetected = false
        }));

        var options = new ActiveDirectoryToolOptions();
        options.AllowedMonitoringRoots.Add(monitoringDirectory);
        var tool = new AdMonitoringServiceHeartbeatGetTool(options);

        var json = await tool.InvokeAsync(
            new JsonObject {
                ["monitoring_directory"] = JsonValue.From(monitoringDirectory)
            },
            CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        Assert.Equal("ad-agent-01", document.RootElement.GetProperty("snapshot").GetProperty("agent_name").GetString());
        Assert.Equal(12, document.RootElement.GetProperty("snapshot").GetProperty("probe_count").GetInt32());
    }

    [Fact]
    public async Task AdMonitoringMetricsGetTool_ShouldLoadSnapshotFromAllowedRoot() {
        var monitoringDirectory = CreateMonitoringDirectory();
        var snapshotPath = Path.Combine(monitoringDirectory, MonitoringMetricsSnapshot.DefaultFileName);
        File.WriteAllText(snapshotPath, JsonSerializer.Serialize(new MonitoringMetricsSnapshot {
            GeneratedUtc = DateTimeOffset.UtcNow,
            SinceUtc = DateTimeOffset.UtcNow.AddHours(-4),
            Agent = "ad-agent-01",
            ScheduledProbes = 80,
            InFlightProbes = 3,
            DueProbes = 7,
            ConfiguredMaxConcurrentProbes = 10,
            EffectiveMaxConcurrentProbes = 6,
            SchedulerAutoPilotEnabled = true,
            SchedulerAutoPilotBusy = true,
            StatusUp = 50,
            StatusDown = 2,
            StatusDegraded = 3,
            StatusUnknown = 1,
            StatusRecovering = 0
        }));

        var options = new ActiveDirectoryToolOptions();
        options.AllowedMonitoringRoots.Add(monitoringDirectory);
        var tool = new AdMonitoringMetricsGetTool(options);

        var json = await tool.InvokeAsync(
            new JsonObject {
                ["monitoring_directory"] = JsonValue.From(monitoringDirectory)
            },
            CancellationToken.None);

        using var document = JsonDocument.Parse(json);
        Assert.Equal("ad-agent-01", document.RootElement.GetProperty("snapshot").GetProperty("agent").GetString());
        Assert.Equal(80, document.RootElement.GetProperty("snapshot").GetProperty("scheduled_probes").GetInt32());
    }

    private static string CreateMonitoringDirectory() {
        var path = Path.Combine(Path.GetTempPath(), "ix-ad-monitoring-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
