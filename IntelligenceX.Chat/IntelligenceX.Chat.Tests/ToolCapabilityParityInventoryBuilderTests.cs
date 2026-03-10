using System;
using System.Linq;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Json;
using IntelligenceX.Tools;
using IntelligenceX.Tools.ADPlayground;
using IntelligenceX.Tools.Common;
using IntelligenceX.Tools.System;
using IntelligenceX.Tools.TestimoX;
using Xunit;

namespace IntelligenceX.Chat.Tests;

/// <summary>
/// Tests for runtime phase-1 parity inventory generation.
/// </summary>
public sealed class ToolCapabilityParityInventoryBuilderTests {
    /// <summary>
    /// Ensures the runtime parity inventory reflects current remote-read-only coverage truth instead of crediting local-only wrappers.
    /// </summary>
    [Fact]
    public void Build_WithLivePacks_ReportsComputerXHealthyAndGovernedBacklog() {
        var registry = new ToolRegistry();
        registry.RegisterSystemPack(new SystemToolOptions());
        registry.RegisterActiveDirectoryPack(new ActiveDirectoryToolOptions());
        registry.RegisterTestimoXPack(new TestimoXToolOptions());
        registry.RegisterTestimoXAnalyticsPack(new TestimoXToolOptions());

        var entries = ToolCapabilityParityInventoryBuilder.Build(
            registry.GetDefinitions(),
            new[] {
                CreateEnabledPack("system", "System"),
                CreateEnabledPack("active_directory", "Active Directory"),
                CreateEnabledPack("testimox", "TestimoX"),
                CreateEnabledPack("testimox_analytics", "TestimoX Analytics")
            });

        var ad = Assert.Single(entries, static entry => string.Equals(entry.EngineId, "adplayground_monitoring", StringComparison.OrdinalIgnoreCase));
        var system = Assert.Single(entries, static entry => string.Equals(entry.EngineId, "computerx", StringComparison.OrdinalIgnoreCase));
        var testimox = Assert.Single(entries, static entry => string.Equals(entry.EngineId, "testimox", StringComparison.OrdinalIgnoreCase));
        var testimoxMonitoring = Assert.Single(entries, static entry => string.Equals(entry.EngineId, "testimox_analytics", StringComparison.OrdinalIgnoreCase));
        var governed = Assert.Single(entries, static entry => string.Equals(entry.EngineId, "testimox_powershell", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ToolCapabilityParityInventoryBuilder.HealthyStatus, ad.Status);
        Assert.Equal(ToolCapabilityParityInventoryBuilder.HealthyStatus, system.Status);
        Assert.Equal(ToolCapabilityParityInventoryBuilder.HealthyStatus, testimox.Status);
        Assert.Equal(ToolCapabilityParityInventoryBuilder.HealthyStatus, testimoxMonitoring.Status);
        Assert.Equal(ToolCapabilityParityInventoryBuilder.GovernedBacklogStatus, governed.Status);
        Assert.Empty(system.MissingCapabilities);
        Assert.True(entries.Sum(static entry => entry.MissingCapabilityCount) >= 0);
    }

    /// <summary>
    /// Ensures the parity inventory flags a gap when a required remote ComputerX wrapper contract is absent.
    /// </summary>
    [Fact]
    public void Build_WhenSystemMetricsWrapperMissing_ReportsComputerXParityGap() {
        var registry = new ToolRegistry();
        registry.RegisterSystemPack(new SystemToolOptions());

        var filteredDefinitions = registry.GetDefinitions()
            .Where(static definition => !string.Equals(definition.Name, "system_metrics_summary", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var entries = ToolCapabilityParityInventoryBuilder.Build(
            filteredDefinitions,
            new[] {
                CreateEnabledPack("system", "System")
            });

        var system = Assert.Single(entries, static entry => string.Equals(entry.EngineId, "computerx", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ToolCapabilityParityInventoryBuilder.GapStatus, system.Status);
        Assert.Contains("remote_metrics_summary", system.MissingCapabilities, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures AD monitoring parity reports a gap when the persisted heartbeat wrapper is absent.
    /// </summary>
    [Fact]
    public void Build_WhenAdMonitoringHeartbeatWrapperMissing_ReportsAdMonitoringParityGap() {
        var registry = new ToolRegistry();
        registry.RegisterActiveDirectoryPack(new ActiveDirectoryToolOptions());

        var filteredDefinitions = registry.GetDefinitions()
            .Where(static definition => !string.Equals(definition.Name, "ad_monitoring_service_heartbeat_get", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var entries = ToolCapabilityParityInventoryBuilder.Build(
            filteredDefinitions,
            new[] {
                CreateEnabledPack("active_directory", "Active Directory")
            });

        var ad = Assert.Single(entries, static entry => string.Equals(entry.EngineId, "adplayground_monitoring", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ToolCapabilityParityInventoryBuilder.GapStatus, ad.Status);
        Assert.Contains("service_heartbeat", ad.MissingCapabilities, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures AD monitoring parity reports a gap when the persisted diagnostics wrapper is absent.
    /// </summary>
    [Fact]
    public void Build_WhenAdMonitoringDiagnosticsWrapperMissing_ReportsAdMonitoringParityGap() {
        var registry = new ToolRegistry();
        registry.RegisterActiveDirectoryPack(new ActiveDirectoryToolOptions());

        var filteredDefinitions = registry.GetDefinitions()
            .Where(static definition => !string.Equals(definition.Name, "ad_monitoring_diagnostics_get", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var entries = ToolCapabilityParityInventoryBuilder.Build(
            filteredDefinitions,
            new[] {
                CreateEnabledPack("active_directory", "Active Directory")
            });

        var ad = Assert.Single(entries, static entry => string.Equals(entry.EngineId, "adplayground_monitoring", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ToolCapabilityParityInventoryBuilder.GapStatus, ad.Status);
        Assert.Contains("diagnostics_snapshot", ad.MissingCapabilities, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures ComputerX parity reports a gap when the local identity wrapper is absent.
    /// </summary>
    [Fact]
    public void Build_WhenSystemLocalIdentityInventoryWrapperMissing_ReportsComputerXParityGap() {
        var registry = new ToolRegistry();
        registry.RegisterSystemPack(new SystemToolOptions());

        var filteredDefinitions = registry.GetDefinitions()
            .Where(static definition => !string.Equals(definition.Name, "system_local_identity_inventory", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var entries = ToolCapabilityParityInventoryBuilder.Build(
            filteredDefinitions,
            new[] {
                CreateEnabledPack("system", "System")
            });

        var system = Assert.Single(entries, static entry => string.Equals(entry.EngineId, "computerx", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ToolCapabilityParityInventoryBuilder.GapStatus, system.Status);
        Assert.Contains("remote_local_identity_inventory", system.MissingCapabilities, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures ComputerX parity reports a gap when the privacy posture wrapper is absent.
    /// </summary>
    [Fact]
    public void Build_WhenSystemPrivacyPostureWrapperMissing_ReportsComputerXParityGap() {
        var registry = new ToolRegistry();
        registry.RegisterSystemPack(new SystemToolOptions());

        var filteredDefinitions = registry.GetDefinitions()
            .Where(static definition => !string.Equals(definition.Name, "system_privacy_posture", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var entries = ToolCapabilityParityInventoryBuilder.Build(
            filteredDefinitions,
            new[] {
                CreateEnabledPack("system", "System")
            });

        var system = Assert.Single(entries, static entry => string.Equals(entry.EngineId, "computerx", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ToolCapabilityParityInventoryBuilder.GapStatus, system.Status);
        Assert.Contains("remote_privacy_posture", system.MissingCapabilities, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures ComputerX parity reports a gap when the exploit protection wrapper is absent.
    /// </summary>
    [Fact]
    public void Build_WhenSystemExploitProtectionWrapperMissing_ReportsComputerXParityGap() {
        var registry = new ToolRegistry();
        registry.RegisterSystemPack(new SystemToolOptions());

        var filteredDefinitions = registry.GetDefinitions()
            .Where(static definition => !string.Equals(definition.Name, "system_exploit_protection", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var entries = ToolCapabilityParityInventoryBuilder.Build(
            filteredDefinitions,
            new[] {
                CreateEnabledPack("system", "System")
            });

        var system = Assert.Single(entries, static entry => string.Equals(entry.EngineId, "computerx", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ToolCapabilityParityInventoryBuilder.GapStatus, system.Status);
        Assert.Contains("remote_exploit_protection", system.MissingCapabilities, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures ComputerX parity reports a gap when the Office posture wrapper is absent.
    /// </summary>
    [Fact]
    public void Build_WhenSystemOfficePostureWrapperMissing_ReportsComputerXParityGap() {
        var registry = new ToolRegistry();
        registry.RegisterSystemPack(new SystemToolOptions());

        var filteredDefinitions = registry.GetDefinitions()
            .Where(static definition => !string.Equals(definition.Name, "system_office_posture", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var entries = ToolCapabilityParityInventoryBuilder.Build(
            filteredDefinitions,
            new[] {
                CreateEnabledPack("system", "System")
            });

        var system = Assert.Single(entries, static entry => string.Equals(entry.EngineId, "computerx", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ToolCapabilityParityInventoryBuilder.GapStatus, system.Status);
        Assert.Contains("remote_office_posture", system.MissingCapabilities, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures ComputerX parity reports a gap when the browser posture wrapper is absent.
    /// </summary>
    [Fact]
    public void Build_WhenSystemBrowserPostureWrapperMissing_ReportsComputerXParityGap() {
        var registry = new ToolRegistry();
        registry.RegisterSystemPack(new SystemToolOptions());

        var filteredDefinitions = registry.GetDefinitions()
            .Where(static definition => !string.Equals(definition.Name, "system_browser_posture", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var entries = ToolCapabilityParityInventoryBuilder.Build(
            filteredDefinitions,
            new[] {
                CreateEnabledPack("system", "System")
            });

        var system = Assert.Single(entries, static entry => string.Equals(entry.EngineId, "computerx", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ToolCapabilityParityInventoryBuilder.GapStatus, system.Status);
        Assert.Contains("remote_browser_posture", system.MissingCapabilities, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures ComputerX parity reports a gap when the backup posture wrapper is absent.
    /// </summary>
    [Fact]
    public void Build_WhenSystemBackupPostureWrapperMissing_ReportsComputerXParityGap() {
        var registry = new ToolRegistry();
        registry.RegisterSystemPack(new SystemToolOptions());

        var filteredDefinitions = registry.GetDefinitions()
            .Where(static definition => !string.Equals(definition.Name, "system_backup_posture", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var entries = ToolCapabilityParityInventoryBuilder.Build(
            filteredDefinitions,
            new[] {
                CreateEnabledPack("system", "System")
            });

        var system = Assert.Single(entries, static entry => string.Equals(entry.EngineId, "computerx", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ToolCapabilityParityInventoryBuilder.GapStatus, system.Status);
        Assert.Contains("remote_backup_posture", system.MissingCapabilities, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures ComputerX parity reports a gap when the TLS posture wrapper is absent.
    /// </summary>
    [Fact]
    public void Build_WhenSystemTlsPostureWrapperMissing_ReportsComputerXParityGap() {
        var registry = new ToolRegistry();
        registry.RegisterSystemPack(new SystemToolOptions());

        var filteredDefinitions = registry.GetDefinitions()
            .Where(static definition => !string.Equals(definition.Name, "system_tls_posture", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var entries = ToolCapabilityParityInventoryBuilder.Build(
            filteredDefinitions,
            new[] {
                CreateEnabledPack("system", "System")
            });

        var system = Assert.Single(entries, static entry => string.Equals(entry.EngineId, "computerx", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ToolCapabilityParityInventoryBuilder.GapStatus, system.Status);
        Assert.Contains("remote_tls_posture", system.MissingCapabilities, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures ComputerX parity reports a gap when the WinRM posture wrapper is absent.
    /// </summary>
    [Fact]
    public void Build_WhenSystemWinRmPostureWrapperMissing_ReportsComputerXParityGap() {
        var registry = new ToolRegistry();
        registry.RegisterSystemPack(new SystemToolOptions());

        var filteredDefinitions = registry.GetDefinitions()
            .Where(static definition => !string.Equals(definition.Name, "system_winrm_posture", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var entries = ToolCapabilityParityInventoryBuilder.Build(
            filteredDefinitions,
            new[] {
                CreateEnabledPack("system", "System")
            });

        var system = Assert.Single(entries, static entry => string.Equals(entry.EngineId, "computerx", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ToolCapabilityParityInventoryBuilder.GapStatus, system.Status);
        Assert.Contains("remote_winrm_posture", system.MissingCapabilities, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures ComputerX parity reports a gap when the PowerShell logging posture wrapper is absent.
    /// </summary>
    [Fact]
    public void Build_WhenSystemPowerShellLoggingPostureWrapperMissing_ReportsComputerXParityGap() {
        var registry = new ToolRegistry();
        registry.RegisterSystemPack(new SystemToolOptions());

        var filteredDefinitions = registry.GetDefinitions()
            .Where(static definition => !string.Equals(definition.Name, "system_powershell_logging_posture", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var entries = ToolCapabilityParityInventoryBuilder.Build(
            filteredDefinitions,
            new[] {
                CreateEnabledPack("system", "System")
            });

        var system = Assert.Single(entries, static entry => string.Equals(entry.EngineId, "computerx", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ToolCapabilityParityInventoryBuilder.GapStatus, system.Status);
        Assert.Contains("remote_powershell_logging_posture", system.MissingCapabilities, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures ComputerX parity reports a gap when the UAC posture wrapper is absent.
    /// </summary>
    [Fact]
    public void Build_WhenSystemUacPostureWrapperMissing_ReportsComputerXParityGap() {
        var registry = new ToolRegistry();
        registry.RegisterSystemPack(new SystemToolOptions());

        var filteredDefinitions = registry.GetDefinitions()
            .Where(static definition => !string.Equals(definition.Name, "system_uac_posture", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var entries = ToolCapabilityParityInventoryBuilder.Build(
            filteredDefinitions,
            new[] {
                CreateEnabledPack("system", "System")
            });

        var system = Assert.Single(entries, static entry => string.Equals(entry.EngineId, "computerx", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ToolCapabilityParityInventoryBuilder.GapStatus, system.Status);
        Assert.Contains("remote_uac_posture", system.MissingCapabilities, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures ComputerX parity reports a gap when the LDAP policy posture wrapper is absent.
    /// </summary>
    [Fact]
    public void Build_WhenSystemLdapPolicyPostureWrapperMissing_ReportsComputerXParityGap() {
        var registry = new ToolRegistry();
        registry.RegisterSystemPack(new SystemToolOptions());

        var filteredDefinitions = registry.GetDefinitions()
            .Where(static definition => !string.Equals(definition.Name, "system_ldap_policy_posture", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var entries = ToolCapabilityParityInventoryBuilder.Build(
            filteredDefinitions,
            new[] {
                CreateEnabledPack("system", "System")
            });

        var system = Assert.Single(entries, static entry => string.Equals(entry.EngineId, "computerx", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ToolCapabilityParityInventoryBuilder.GapStatus, system.Status);
        Assert.Contains("remote_ldap_policy_posture", system.MissingCapabilities, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures ComputerX parity reports a gap when the network client posture wrapper is absent.
    /// </summary>
    [Fact]
    public void Build_WhenSystemNetworkClientPostureWrapperMissing_ReportsComputerXParityGap() {
        var registry = new ToolRegistry();
        registry.RegisterSystemPack(new SystemToolOptions());

        var filteredDefinitions = registry.GetDefinitions()
            .Where(static definition => !string.Equals(definition.Name, "system_network_client_posture", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var entries = ToolCapabilityParityInventoryBuilder.Build(
            filteredDefinitions,
            new[] {
                CreateEnabledPack("system", "System")
            });

        var system = Assert.Single(entries, static entry => string.Equals(entry.EngineId, "computerx", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ToolCapabilityParityInventoryBuilder.GapStatus, system.Status);
        Assert.Contains("remote_network_client_posture", system.MissingCapabilities, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures ComputerX parity reports a gap when the account policy posture wrapper is absent.
    /// </summary>
    [Fact]
    public void Build_WhenSystemAccountPolicyPostureWrapperMissing_ReportsComputerXParityGap() {
        var registry = new ToolRegistry();
        registry.RegisterSystemPack(new SystemToolOptions());

        var filteredDefinitions = registry.GetDefinitions()
            .Where(static definition => !string.Equals(definition.Name, "system_account_policy_posture", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var entries = ToolCapabilityParityInventoryBuilder.Build(
            filteredDefinitions,
            new[] {
                CreateEnabledPack("system", "System")
            });

        var system = Assert.Single(entries, static entry => string.Equals(entry.EngineId, "computerx", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ToolCapabilityParityInventoryBuilder.GapStatus, system.Status);
        Assert.Contains("remote_account_policy_posture", system.MissingCapabilities, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures ComputerX parity reports a gap when the interactive logon posture wrapper is absent.
    /// </summary>
    [Fact]
    public void Build_WhenSystemInteractiveLogonPostureWrapperMissing_ReportsComputerXParityGap() {
        var registry = new ToolRegistry();
        registry.RegisterSystemPack(new SystemToolOptions());

        var filteredDefinitions = registry.GetDefinitions()
            .Where(static definition => !string.Equals(definition.Name, "system_interactive_logon_posture", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var entries = ToolCapabilityParityInventoryBuilder.Build(
            filteredDefinitions,
            new[] {
                CreateEnabledPack("system", "System")
            });

        var system = Assert.Single(entries, static entry => string.Equals(entry.EngineId, "computerx", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ToolCapabilityParityInventoryBuilder.GapStatus, system.Status);
        Assert.Contains("remote_interactive_logon_posture", system.MissingCapabilities, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures ComputerX parity reports a gap when the Device Guard posture wrapper is absent.
    /// </summary>
    [Fact]
    public void Build_WhenSystemDeviceGuardPostureWrapperMissing_ReportsComputerXParityGap() {
        var registry = new ToolRegistry();
        registry.RegisterSystemPack(new SystemToolOptions());

        var filteredDefinitions = registry.GetDefinitions()
            .Where(static definition => !string.Equals(definition.Name, "system_device_guard_posture", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var entries = ToolCapabilityParityInventoryBuilder.Build(
            filteredDefinitions,
            new[] {
                CreateEnabledPack("system", "System")
            });

        var system = Assert.Single(entries, static entry => string.Equals(entry.EngineId, "computerx", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ToolCapabilityParityInventoryBuilder.GapStatus, system.Status);
        Assert.Contains("remote_device_guard_posture", system.MissingCapabilities, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures ComputerX parity reports a gap when the Defender ASR posture wrapper is absent.
    /// </summary>
    [Fact]
    public void Build_WhenSystemDefenderAsrPostureWrapperMissing_ReportsComputerXParityGap() {
        var registry = new ToolRegistry();
        registry.RegisterSystemPack(new SystemToolOptions());

        var filteredDefinitions = registry.GetDefinitions()
            .Where(static definition => !string.Equals(definition.Name, "system_defender_asr_posture", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var entries = ToolCapabilityParityInventoryBuilder.Build(
            filteredDefinitions,
            new[] {
                CreateEnabledPack("system", "System")
            });

        var system = Assert.Single(entries, static entry => string.Equals(entry.EngineId, "computerx", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ToolCapabilityParityInventoryBuilder.GapStatus, system.Status);
        Assert.Contains("remote_defender_asr_posture", system.MissingCapabilities, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures ComputerX parity reports a gap when the Windows Update client status wrapper is absent.
    /// </summary>
    [Fact]
    public void Build_WhenSystemWindowsUpdateClientStatusWrapperMissing_ReportsComputerXParityGap() {
        var registry = new ToolRegistry();
        registry.RegisterSystemPack(new SystemToolOptions());

        var filteredDefinitions = registry.GetDefinitions()
            .Where(static definition => !string.Equals(definition.Name, "system_windows_update_client_status", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var entries = ToolCapabilityParityInventoryBuilder.Build(
            filteredDefinitions,
            new[] {
                CreateEnabledPack("system", "System")
            });

        var system = Assert.Single(entries, static entry => string.Equals(entry.EngineId, "computerx", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ToolCapabilityParityInventoryBuilder.GapStatus, system.Status);
        Assert.Contains("remote_windows_update_client_status", system.MissingCapabilities, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures ComputerX parity reports a gap when the Windows Update telemetry wrapper is absent.
    /// </summary>
    [Fact]
    public void Build_WhenSystemWindowsUpdateTelemetryWrapperMissing_ReportsComputerXParityGap() {
        var registry = new ToolRegistry();
        registry.RegisterSystemPack(new SystemToolOptions());

        var filteredDefinitions = registry.GetDefinitions()
            .Where(static definition => !string.Equals(definition.Name, "system_windows_update_telemetry", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var entries = ToolCapabilityParityInventoryBuilder.Build(
            filteredDefinitions,
            new[] {
                CreateEnabledPack("system", "System")
            });

        var system = Assert.Single(entries, static entry => string.Equals(entry.EngineId, "computerx", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ToolCapabilityParityInventoryBuilder.GapStatus, system.Status);
        Assert.Contains("remote_windows_update_telemetry", system.MissingCapabilities, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures ComputerX parity reports a gap when the certificate posture wrapper is absent.
    /// </summary>
    [Fact]
    public void Build_WhenSystemCertificatePostureWrapperMissing_ReportsComputerXParityGap() {
        var registry = new ToolRegistry();
        registry.RegisterSystemPack(new SystemToolOptions());

        var filteredDefinitions = registry.GetDefinitions()
            .Where(static definition => !string.Equals(definition.Name, "system_certificate_posture", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var entries = ToolCapabilityParityInventoryBuilder.Build(
            filteredDefinitions,
            new[] {
                CreateEnabledPack("system", "System")
            });

        var system = Assert.Single(entries, static entry => string.Equals(entry.EngineId, "computerx", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ToolCapabilityParityInventoryBuilder.GapStatus, system.Status);
        Assert.Contains("remote_certificate_posture", system.MissingCapabilities, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures ComputerX parity reports a gap when the credential posture wrapper is absent.
    /// </summary>
    [Fact]
    public void Build_WhenSystemCredentialPostureWrapperMissing_ReportsComputerXParityGap() {
        var registry = new ToolRegistry();
        registry.RegisterSystemPack(new SystemToolOptions());

        var filteredDefinitions = registry.GetDefinitions()
            .Where(static definition => !string.Equals(definition.Name, "system_credential_posture", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var entries = ToolCapabilityParityInventoryBuilder.Build(
            filteredDefinitions,
            new[] {
                CreateEnabledPack("system", "System")
            });

        var system = Assert.Single(entries, static entry => string.Equals(entry.EngineId, "computerx", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ToolCapabilityParityInventoryBuilder.GapStatus, system.Status);
        Assert.Contains("remote_credential_posture", system.MissingCapabilities, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures TestimoX parity reports a gap when the profile catalog wrapper is absent.
    /// </summary>
    [Fact]
    public void Build_WhenTestimoXProfilesWrapperMissing_ReportsTestimoXParityGap() {
        var registry = new ToolRegistry();
        registry.RegisterTestimoXPack(new TestimoXToolOptions());

        var filteredDefinitions = registry.GetDefinitions()
            .Where(static definition => !string.Equals(definition.Name, "testimox_profiles_list", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var entries = ToolCapabilityParityInventoryBuilder.Build(
            filteredDefinitions,
            new[] {
                CreateEnabledPack("testimox", "TestimoX")
            });

        var testimox = Assert.Single(entries, static entry => string.Equals(entry.EngineId, "testimox", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ToolCapabilityParityInventoryBuilder.GapStatus, testimox.Status);
        Assert.Contains("profile_catalog", testimox.MissingCapabilities, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures TestimoX parity reports a gap when the report job history wrapper is absent.
    /// </summary>
    [Fact]
    public void Build_WhenTestimoXReportJobHistoryWrapperMissing_ReportsTestimoXParityGap() {
        var registry = new ToolRegistry();
        registry.RegisterTestimoXAnalyticsPack(new TestimoXToolOptions());

        var filteredDefinitions = registry.GetDefinitions()
            .Where(static definition => !string.Equals(definition.Name, "testimox_report_job_history", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var entries = ToolCapabilityParityInventoryBuilder.Build(
            filteredDefinitions,
            new[] {
                CreateEnabledPack("testimox_analytics", "TestimoX Analytics")
            });

        var testimox = Assert.Single(entries, static entry => string.Equals(entry.EngineId, "testimox_analytics", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ToolCapabilityParityInventoryBuilder.GapStatus, testimox.Status);
        Assert.Contains("report_job_history", testimox.MissingCapabilities, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures TestimoX parity reports a gap when the maintenance window history wrapper is absent.
    /// </summary>
    [Fact]
    public void Build_WhenTestimoXMaintenanceWindowHistoryWrapperMissing_ReportsTestimoXParityGap() {
        var registry = new ToolRegistry();
        registry.RegisterTestimoXAnalyticsPack(new TestimoXToolOptions());

        var filteredDefinitions = registry.GetDefinitions()
            .Where(static definition => !string.Equals(definition.Name, "testimox_maintenance_window_history", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var entries = ToolCapabilityParityInventoryBuilder.Build(
            filteredDefinitions,
            new[] {
                CreateEnabledPack("testimox_analytics", "TestimoX Analytics")
            });

        var testimox = Assert.Single(entries, static entry => string.Equals(entry.EngineId, "testimox_analytics", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ToolCapabilityParityInventoryBuilder.GapStatus, testimox.Status);
        Assert.Contains("maintenance_window_history", testimox.MissingCapabilities, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures TestimoX parity reports a gap when the probe index status wrapper is absent.
    /// </summary>
    [Fact]
    public void Build_WhenTestimoXProbeIndexStatusWrapperMissing_ReportsTestimoXParityGap() {
        var registry = new ToolRegistry();
        registry.RegisterTestimoXAnalyticsPack(new TestimoXToolOptions());

        var filteredDefinitions = registry.GetDefinitions()
            .Where(static definition => !string.Equals(definition.Name, "testimox_probe_index_status", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var entries = ToolCapabilityParityInventoryBuilder.Build(
            filteredDefinitions,
            new[] {
                CreateEnabledPack("testimox_analytics", "TestimoX Analytics")
            });

        var testimox = Assert.Single(entries, static entry => string.Equals(entry.EngineId, "testimox_analytics", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ToolCapabilityParityInventoryBuilder.GapStatus, testimox.Status);
        Assert.Contains("probe_index_status", testimox.MissingCapabilities, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures TestimoX parity reports a gap when the monitoring diagnostics wrapper is absent.
    /// </summary>
    [Fact]
    public void Build_WhenTestimoXAnalyticsDiagnosticsWrapperMissing_ReportsTestimoXParityGap() {
        var registry = new ToolRegistry();
        registry.RegisterTestimoXAnalyticsPack(new TestimoXToolOptions());

        var filteredDefinitions = registry.GetDefinitions()
            .Where(static definition => !string.Equals(definition.Name, "testimox_analytics_diagnostics_get", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var entries = ToolCapabilityParityInventoryBuilder.Build(
            filteredDefinitions,
            new[] {
                CreateEnabledPack("testimox_analytics", "TestimoX Analytics")
            });

        var testimox = Assert.Single(entries, static entry => string.Equals(entry.EngineId, "testimox_analytics", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ToolCapabilityParityInventoryBuilder.GapStatus, testimox.Status);
        Assert.Contains("monitoring_diagnostics", testimox.MissingCapabilities, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures TestimoX parity reports a gap when the monitoring history wrapper is absent.
    /// </summary>
    [Fact]
    public void Build_WhenTestimoXHistoryQueryWrapperMissing_ReportsTestimoXParityGap() {
        var registry = new ToolRegistry();
        registry.RegisterTestimoXAnalyticsPack(new TestimoXToolOptions());

        var filteredDefinitions = registry.GetDefinitions()
            .Where(static definition => !string.Equals(definition.Name, "testimox_history_query", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var entries = ToolCapabilityParityInventoryBuilder.Build(
            filteredDefinitions,
            new[] {
                CreateEnabledPack("testimox_analytics", "TestimoX Analytics")
            });

        var testimox = Assert.Single(entries, static entry => string.Equals(entry.EngineId, "testimox_analytics", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ToolCapabilityParityInventoryBuilder.GapStatus, testimox.Status);
        Assert.Contains("monitoring_history", testimox.MissingCapabilities, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures TestimoX parity reports a gap when the report data snapshot wrapper is absent.
    /// </summary>
    [Fact]
    public void Build_WhenTestimoXReportDataSnapshotWrapperMissing_ReportsTestimoXParityGap() {
        var registry = new ToolRegistry();
        registry.RegisterTestimoXAnalyticsPack(new TestimoXToolOptions());

        var filteredDefinitions = registry.GetDefinitions()
            .Where(static definition => !string.Equals(definition.Name, "testimox_report_data_snapshot_get", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var entries = ToolCapabilityParityInventoryBuilder.Build(
            filteredDefinitions,
            new[] {
                CreateEnabledPack("testimox_analytics", "TestimoX Analytics")
            });

        var testimox = Assert.Single(entries, static entry => string.Equals(entry.EngineId, "testimox_analytics", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ToolCapabilityParityInventoryBuilder.GapStatus, testimox.Status);
        Assert.Contains("report_data_snapshot", testimox.MissingCapabilities, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures TestimoX parity reports a gap when the HTML report snapshot wrapper is absent.
    /// </summary>
    [Fact]
    public void Build_WhenTestimoXReportSnapshotWrapperMissing_ReportsTestimoXParityGap() {
        var registry = new ToolRegistry();
        registry.RegisterTestimoXAnalyticsPack(new TestimoXToolOptions());

        var filteredDefinitions = registry.GetDefinitions()
            .Where(static definition => !string.Equals(definition.Name, "testimox_report_snapshot_get", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var entries = ToolCapabilityParityInventoryBuilder.Build(
            filteredDefinitions,
            new[] {
                CreateEnabledPack("testimox_analytics", "TestimoX Analytics")
            });

        var testimox = Assert.Single(entries, static entry => string.Equals(entry.EngineId, "testimox_analytics", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ToolCapabilityParityInventoryBuilder.GapStatus, testimox.Status);
        Assert.Contains("report_snapshot", testimox.MissingCapabilities, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures TestimoX parity reports a gap when the baseline catalog wrapper is absent.
    /// </summary>
    [Fact]
    public void Build_WhenTestimoXBaselinesListWrapperMissing_ReportsTestimoXParityGap() {
        var registry = new ToolRegistry();
        registry.RegisterTestimoXPack(new TestimoXToolOptions());

        var filteredDefinitions = registry.GetDefinitions()
            .Where(static definition => !string.Equals(definition.Name, "testimox_baselines_list", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var entries = ToolCapabilityParityInventoryBuilder.Build(
            filteredDefinitions,
            new[] {
                CreateEnabledPack("testimox", "TestimoX")
            });

        var testimox = Assert.Single(entries, static entry => string.Equals(entry.EngineId, "testimox", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ToolCapabilityParityInventoryBuilder.GapStatus, testimox.Status);
        Assert.Contains("baseline_catalog", testimox.MissingCapabilities, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures TestimoX parity reports a gap when the baseline compare wrapper is absent.
    /// </summary>
    [Fact]
    public void Build_WhenTestimoXBaselineCompareWrapperMissing_ReportsTestimoXParityGap() {
        var registry = new ToolRegistry();
        registry.RegisterTestimoXPack(new TestimoXToolOptions());

        var filteredDefinitions = registry.GetDefinitions()
            .Where(static definition => !string.Equals(definition.Name, "testimox_baseline_compare", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var entries = ToolCapabilityParityInventoryBuilder.Build(
            filteredDefinitions,
            new[] {
                CreateEnabledPack("testimox", "TestimoX")
            });

        var testimox = Assert.Single(entries, static entry => string.Equals(entry.EngineId, "testimox", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ToolCapabilityParityInventoryBuilder.GapStatus, testimox.Status);
        Assert.Contains("baseline_compare", testimox.MissingCapabilities, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures TestimoX parity reports a gap when the source provenance wrapper is absent.
    /// </summary>
    [Fact]
    public void Build_WhenTestimoXSourceQueryWrapperMissing_ReportsTestimoXParityGap() {
        var registry = new ToolRegistry();
        registry.RegisterTestimoXPack(new TestimoXToolOptions());

        var filteredDefinitions = registry.GetDefinitions()
            .Where(static definition => !string.Equals(definition.Name, "testimox_source_query", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var entries = ToolCapabilityParityInventoryBuilder.Build(
            filteredDefinitions,
            new[] {
                CreateEnabledPack("testimox", "TestimoX")
            });

        var testimox = Assert.Single(entries, static entry => string.Equals(entry.EngineId, "testimox", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ToolCapabilityParityInventoryBuilder.GapStatus, testimox.Status);
        Assert.Contains("source_provenance", testimox.MissingCapabilities, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures TestimoX parity reports a gap when the baseline crosswalk wrapper is absent.
    /// </summary>
    [Fact]
    public void Build_WhenTestimoXBaselineCrosswalkWrapperMissing_ReportsTestimoXParityGap() {
        var registry = new ToolRegistry();
        registry.RegisterTestimoXPack(new TestimoXToolOptions());

        var filteredDefinitions = registry.GetDefinitions()
            .Where(static definition => !string.Equals(definition.Name, "testimox_baseline_crosswalk", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var entries = ToolCapabilityParityInventoryBuilder.Build(
            filteredDefinitions,
            new[] {
                CreateEnabledPack("testimox", "TestimoX")
            });

        var testimox = Assert.Single(entries, static entry => string.Equals(entry.EngineId, "testimox", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ToolCapabilityParityInventoryBuilder.GapStatus, testimox.Status);
        Assert.Contains("baseline_crosswalk", testimox.MissingCapabilities, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures local-only wrappers do not count toward remote parity simply because the tool name is registered.
    /// </summary>
    [Fact]
    public void Build_WhenLocalOnlySystemWrapperExists_DoesNotCreditRemoteParity() {
        var registry = new ToolRegistry();
        registry.RegisterSystemPack(new SystemToolOptions());

        var definitions = registry.GetDefinitions()
            .Select(static definition => {
                if (!string.Equals(definition.Name, "system_process_list", StringComparison.OrdinalIgnoreCase)) {
                    return definition;
                }

                return new ToolDefinition(
                    name: definition.Name,
                    description: definition.Description,
                    parameters: ToolSchema.Object(
                            ("name_contains", ToolSchema.String("Optional case-insensitive name filter.")),
                            ("max_processes", ToolSchema.Integer("Optional maximum processes to return (capped).")))
                        .WithTableViewOptions()
                        .NoAdditionalProperties(),
                    displayName: definition.DisplayName,
                    category: definition.Category,
                    tags: definition.Tags,
                    writeGovernance: definition.WriteGovernance,
                    aliases: definition.Aliases,
                    aliasOf: definition.AliasOf,
                    authentication: definition.Authentication,
                    routing: definition.Routing,
                    setup: definition.Setup,
                    handoff: definition.Handoff,
                    recovery: definition.Recovery);
            })
            .ToArray();

        var entries = ToolCapabilityParityInventoryBuilder.Build(
            definitions,
            new[] {
                CreateEnabledPack("system", "System")
            });

        var system = Assert.Single(entries, static entry => string.Equals(entry.EngineId, "computerx", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(ToolCapabilityParityInventoryBuilder.GapStatus, system.Status);
        Assert.Contains("remote_process_inventory", system.MissingCapabilities, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ensures parity detail summaries expose surfaced-vs-expected counts and governed backlog notes.
    /// </summary>
    [Fact]
    public void BuildDetailSummaries_WithLivePacks_EmitsDeltaAndGovernedDetail() {
        var registry = new ToolRegistry();
        registry.RegisterSystemPack(new SystemToolOptions());
        registry.RegisterActiveDirectoryPack(new ActiveDirectoryToolOptions());
        registry.RegisterTestimoXPack(new TestimoXToolOptions());
        registry.RegisterTestimoXAnalyticsPack(new TestimoXToolOptions());

        var entries = ToolCapabilityParityInventoryBuilder.Build(
            registry.GetDefinitions(),
            new[] {
                CreateEnabledPack("system", "System"),
                CreateEnabledPack("active_directory", "Active Directory"),
                CreateEnabledPack("testimox", "TestimoX"),
                CreateEnabledPack("testimox_analytics", "TestimoX Analytics")
            });

        var details = ToolCapabilityParityInventoryBuilder.BuildDetailSummaries(entries, maxItems: 8);

        Assert.Contains(details, static line => line.Contains("computerx [healthy]", StringComparison.OrdinalIgnoreCase)
            && line.Contains("surfaced=", StringComparison.OrdinalIgnoreCase)
            && line.Contains("registered_tools=", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(details, static line => line.Contains("testimox_powershell [governed_backlog]", StringComparison.OrdinalIgnoreCase));
    }

    private static ToolPackAvailabilityInfo CreateEnabledPack(string id, string name) {
        return new ToolPackAvailabilityInfo {
            Id = id,
            Name = name,
            SourceKind = "builtin",
            Enabled = true
        };
    }
}
