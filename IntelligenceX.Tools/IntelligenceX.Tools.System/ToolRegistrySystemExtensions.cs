using System;
using System.Collections.Generic;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.System;

/// <summary>
/// Convenience registration helpers for the System tool pack.
/// </summary>
public static class ToolRegistrySystemExtensions {
    /// <summary>
    /// Returns the tool names registered by <see cref="RegisterSystemPack"/> on the current platform.
    /// </summary>
    /// <param name="options">Tool options.</param>
    /// <returns>Ordered tool names for the pack.</returns>
    public static IReadOnlyList<string> GetRegisteredToolNames(SystemToolOptions options) {
        return ToolPackRegistry.GetRegisteredToolNames(options, CreateTools);
    }

    /// <summary>
    /// Returns tool catalog metadata for tools registered by <see cref="RegisterSystemPack"/>.
    /// </summary>
    /// <param name="options">Tool options.</param>
    /// <returns>Catalog entries derived from runtime definitions and input schemas.</returns>
    public static IReadOnlyList<ToolPackToolCatalogEntryModel> GetRegisteredToolCatalog(SystemToolOptions options) {
        return ToolPackRegistry.GetRegisteredToolCatalog(options, CreateTools);
    }

    /// <summary>
    /// Registers all System tools into the provided registry.
    /// </summary>
    /// <param name="registry">Tool registry.</param>
    /// <param name="options">Tool options.</param>
    /// <returns>The same registry instance for chaining.</returns>
    public static ToolRegistry RegisterSystemPack(this ToolRegistry registry, SystemToolOptions options) {
        return ToolPackRegistry.RegisterPack(registry, options, CreateTools);
    }

    private static IEnumerable<ITool> CreateTools(SystemToolOptions options) {
        foreach (var tool in CreateCoreTools(options)) {
            yield return SystemToolContracts.Apply(tool);
        }
    }

    private static IEnumerable<ITool> CreateCoreTools(SystemToolOptions options) {
        yield return new SystemPackInfoTool(options);
        yield return new SystemInfoTool(options);
        yield return new SystemHardwareIdentityTool(options);
        yield return new SystemWhoAmITool(options);
        yield return new SystemProcessListTool(options);
        yield return new SystemNetworkAdaptersTool(options);
        yield return new SystemPatchDetailsTool(options);

        if (OperatingSystem.IsWindows()) {
            yield return new SystemPortsListTool(options);
            yield return new SystemServiceListTool(options);
            yield return new SystemScheduledTasksListTool(options);
            yield return new SystemFirewallRulesTool(options);
            yield return new SystemFirewallProfilesTool(options);
            yield return new SystemSecurityOptionsTool(options);
            yield return new SystemRdpPostureTool(options);
            yield return new SystemSmbPostureTool(options);
            yield return new SystemBootConfigurationTool(options);
            yield return new SystemBiosSummaryTool(options);
            yield return new SystemTimeSyncTool(options);
            yield return new SystemLocalIdentityInventoryTool(options);
            yield return new SystemBitlockerStatusTool(options);
            yield return new SystemPrivacyPostureTool(options);
            yield return new SystemExploitProtectionTool(options);
            yield return new SystemOfficePostureTool(options);
            yield return new SystemBrowserPostureTool(options);
            yield return new SystemBackupPostureTool(options);
            yield return new SystemCertificatePostureTool(options);
            yield return new SystemCredentialPostureTool(options);
            yield return new SystemTlsPostureTool(options);
            yield return new SystemWinRmPostureTool(options);
            yield return new SystemPowerShellLoggingPostureTool(options);
            yield return new SystemPlatformSecurityPostureTool(options);
            yield return new SystemAppControlPostureTool(options);
            yield return new SystemRemoteAccessPostureTool(options);
            yield return new SystemUacPostureTool(options);
            yield return new SystemLdapPolicyPostureTool(options);
            yield return new SystemNetworkClientPostureTool(options);
            yield return new SystemAccountPolicyPostureTool(options);
            yield return new SystemInteractiveLogonPostureTool(options);
            yield return new SystemAuditOptionsTool(options);
            yield return new SystemBuiltinAccountsTool(options);
            yield return new SystemDeviceGuardPostureTool(options);
            yield return new SystemDefenderAsrPostureTool(options);
            yield return new SystemInstalledApplicationsTool(options);
            yield return new SystemUpdatesInstalledTool(options);
            yield return new SystemWindowsUpdateClientStatusTool(options);
            yield return new SystemWindowsUpdateTelemetryTool(options);
            yield return new SystemPatchComplianceTool(options);
            yield return new SystemLogicalDisksListTool(options);
            yield return new SystemDisksListTool(options);
            yield return new SystemDevicesSummaryTool(options);
            yield return new SystemHardwareSummaryTool(options);
            yield return new SystemMetricsSummaryTool(options);
            yield return new SystemFeaturesListTool(options);
        }

        yield return new WslStatusTool(options);
    }
}
