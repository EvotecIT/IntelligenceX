using System.Collections.Generic;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Convenience registration helpers for the ActiveDirectory tool pack.
/// </summary>
public static class ToolRegistryActiveDirectoryExtensions {
    /// <summary>
    /// Returns the tool names registered by <see cref="RegisterActiveDirectoryPack"/>.
    /// </summary>
    /// <param name="options">Tool options.</param>
    /// <returns>Ordered tool names for the pack.</returns>
    public static IReadOnlyList<string> GetRegisteredToolNames(ActiveDirectoryToolOptions options) {
        return ToolPackRegistry.GetRegisteredToolNames(options, CreateTools);
    }

    /// <summary>
    /// Returns tool catalog metadata for tools registered by <see cref="RegisterActiveDirectoryPack"/>.
    /// </summary>
    /// <param name="options">Tool options.</param>
    /// <returns>Catalog entries derived from runtime definitions and input schemas.</returns>
    public static IReadOnlyList<ToolPackToolCatalogEntryModel> GetRegisteredToolCatalog(ActiveDirectoryToolOptions options) {
        return ToolPackRegistry.GetRegisteredToolCatalog(options, CreateTools);
    }

    /// <summary>
    /// Registers all ActiveDirectory tools into the provided registry.
    /// </summary>
    /// <param name="registry">Tool registry.</param>
    /// <param name="options">Tool options.</param>
    /// <returns>The same registry instance for chaining.</returns>
    public static ToolRegistry RegisterActiveDirectoryPack(this ToolRegistry registry, ActiveDirectoryToolOptions options) {
        return ToolPackRegistry.RegisterPack(registry, options, CreateTools);
    }

    private static IEnumerable<ITool> CreateTools(ActiveDirectoryToolOptions options) {
        yield return new AdPackInfoTool(options);
        yield return new AdEnvironmentDiscoverTool(options);
        yield return new AdScopeDiscoveryTool(options);
        yield return new AdForestDiscoverTool(options);
        yield return new AdGpoListTool(options);
        yield return new AdGpoChangesTool(options);
        yield return new AdGpoHealthTool(options);
        yield return new AdGpoPermissionReadTool(options);
        yield return new AdGpoPermissionAdministrativeTool(options);
        yield return new AdGpoPermissionConsistencyTool(options);
        yield return new AdGpoPermissionUnknownTool(options);
        yield return new AdGpoPermissionRootTool(options);
        yield return new AdGpoPermissionReportTool(options);
        yield return new AdGpoInventoryHealthTool(options);
        yield return new AdGpoDuplicatesTool(options);
        yield return new AdGpoBlockedInheritanceTool(options);
        yield return new AdGpoOuLinkSummaryTool(options);
        yield return new AdGpoRedirectTool(options);
        yield return new AdGpoIntegrityTool(options);
        yield return new AdDomainInfoTool(options);
        yield return new AdForestFunctionalTool(options);
        yield return new AdDsHeuristicsTool(options);
        yield return new AdLapsSchemaPostureTool(options);
        yield return new AdAzureAdSsoTool(options);
        yield return new AdDomainStatisticsTool(options);
        yield return new AdDomainContainerDefaultsTool(options);
        yield return new AdDomainControllerFactsTool(options);
        yield return new AdDomainControllerSecurityTool(options);
        yield return new AdDcFleetPostureTool(options);
        yield return new AdRegistrationPostureTool(options);
        yield return new AdDomainControllersTool(options);
        yield return new AdFsmoRolesTool(options);
        yield return new AdClientServerAuthPostureTool(options);
        yield return new AdLegacyCveExposureTool(options);
        yield return new AdFirewallProfilesTool(options);
        yield return new AdTimeServiceConfigurationTool(options);
        yield return new AdLlmnrPolicyTool(options);
        yield return new AdWdigestPolicyTool(options);
        yield return new AdWinRmPolicyTool(options);
        yield return new AdProxyPolicyTool(options);
        yield return new AdSchannelPolicyTool(options);
        yield return new AdTerminalServicesRedirectionPolicyTool(options);
        yield return new AdTerminalServicesTimeoutPolicyTool(options);
        yield return new AdNameResolutionPolicyTool(options);
        yield return new AdLsaProtectionPolicyTool(options);
        yield return new AdNetSessionHardeningPolicyTool(options);
        yield return new AdLimitBlankPasswordUsePolicyTool(options);
        yield return new AdPku2uPolicyTool(options);
        yield return new AdHardenedPathsPolicyTool(options);
        yield return new AdKdcProxyPolicyTool(options);
        yield return new AdKerberosPacPolicyTool(options);
        yield return new AdPowerShellLoggingPolicyTool(options);
        yield return new AdNoLmHashPolicyTool(options);
        yield return new AdNtlmRestrictionsPolicyTool(options);
        yield return new AdRestrictNtlmConfigurationTool(options);
        yield return new AdLogonUxUacPolicyTool(options);
        yield return new AdDenyLogonRightsPolicyTool(options);
        yield return new AdDefenderAsrPolicyTool(options);
        yield return new AdEveryoneIncludesAnonymousPolicyTool(options);
        yield return new AdEnableDelegationPrivilegePolicyTool(options);
        yield return new AdLanManagerSettingsTool(options);
        yield return new AdMachineAccountQuotaTool(options);
        yield return new AdDuplicateAccountsTool(options);
        yield return new AdOuProtectionTool(options);
        yield return new AdLapsCoverageTool(options);
        yield return new AdKerberosCryptoPostureTool(options);
        yield return new AdSpnSearchTool(options);
        yield return new AdSpnStatsTool(options);
        yield return new AdSpnHygieneTool(options);
        yield return new AdGroupsListTool(options);
        yield return new AdWhoAmITool(options);
        yield return new AdRecycleBinLifetimeTool(options);
        yield return new AdObjectGetTool(options);
        yield return new AdObjectResolveTool(options);
        yield return new AdHandoffPrepareTool(options);
        yield return new AdDelegationAuditTool(options);
        yield return new AdPrivilegedGroupsSummaryTool(options);
        yield return new AdDomainAdminsSummaryTool(options);
        yield return new AdStaleAccountsTool(options);
        yield return new AdNeverLoggedInAccountsTool(options);
        yield return new AdServiceAccountUsageTool(options);
        yield return new AdKrbtgtHealthTool(options);
        yield return new AdLdapQueryTool(options);
        yield return new AdLdapQueryPagedTool(options);
        yield return new AdLdapDiagnosticsTool(options);
        yield return new AdDirectoryDiscoveryDiagnosticsTool(options);
        yield return new AdDnsServerConfigTool(options);
        yield return new AdDnsZoneConfigTool(options);
        yield return new AdDnsZoneSecurityTool(options);
        yield return new AdDnsDelegationTool(options);
        yield return new AdDnsScavengingTool(options);
        yield return new AdMonitoringProbeCatalogTool(options);
        yield return new AdMonitoringProbeRunTool(options);
        yield return new AdReplicationSummaryTool(options);
        yield return new AdReplicationConnectionsTool(options);
        yield return new AdReplicationStatusTool(options);
        yield return new AdPasswordPolicyTool(options);
        yield return new AdPasswordPolicyRollupTool(options);
        yield return new AdPasswordPolicyLengthTool(options);
        yield return new AdSchemaVersionTool(options);
        yield return new AdNullSessionPostureTool(options);
        yield return new AdShadowCredentialsRiskTool(options);
        yield return new AdDcShadowIndicatorsTool(options);
        yield return new AdDangerousExtendedRightsTool(options);
        yield return new AdSmartCardPostureTool(options);
        yield return new AdPkiTemplatesTool(options);
        yield return new AdPkiPostureTool(options);
        yield return new AdSitesTool(options);
        yield return new AdSubnetsTool(options);
        yield return new AdSiteLinksTool(options);
        yield return new AdSiteCoverageTool(options);
        yield return new AdTrustTool(options);
        yield return new AdSystemStateBackupTool(options);
        yield return new AdSearchFacetsTool(options);
        yield return new AdSearchTool(options);
        yield return new AdGroupMembersTool(options);
        yield return new AdGroupMembersResolvedTool(options);
        yield return new AdUsersExpiredTool(options);
    }
}
