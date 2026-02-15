using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ActiveDirectory;

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
        if (options is null) {
            throw new ArgumentNullException(nameof(options));
        }

        return CreateTools(options).Select(static tool => tool.Definition.Name).ToArray();
    }

    /// <summary>
    /// Returns tool catalog metadata for tools registered by <see cref="RegisterActiveDirectoryPack"/>.
    /// </summary>
    /// <param name="options">Tool options.</param>
    /// <returns>Catalog entries derived from runtime definitions and input schemas.</returns>
    public static IReadOnlyList<ToolPackToolCatalogEntryModel> GetRegisteredToolCatalog(ActiveDirectoryToolOptions options) {
        if (options is null) {
            throw new ArgumentNullException(nameof(options));
        }

        return ToolPackGuidance.CatalogFromTools(CreateTools(options));
    }

    /// <summary>
    /// Registers all ActiveDirectory tools into the provided registry.
    /// </summary>
    /// <param name="registry">Tool registry.</param>
    /// <param name="options">Tool options.</param>
    /// <returns>The same registry instance for chaining.</returns>
    public static ToolRegistry RegisterActiveDirectoryPack(this ToolRegistry registry, ActiveDirectoryToolOptions options) {
        if (registry is null) {
            throw new ArgumentNullException(nameof(registry));
        }
        if (options is null) {
            throw new ArgumentNullException(nameof(options));
        }

        foreach (var tool in CreateTools(options)) {
            registry.Register(tool);
        }
        return registry;
    }

    private static IEnumerable<ITool> CreateTools(ActiveDirectoryToolOptions options) {
        yield return new AdPackInfoTool(options);
        yield return new AdEnvironmentDiscoverTool(options);
        yield return new AdForestDiscoverTool(options);
        yield return new AdDomainInfoTool(options);
        yield return new AdDomainControllersTool(options);
        yield return new AdSpnSearchTool(options);
        yield return new AdSpnStatsTool(options);
        yield return new AdGroupsListTool(options);
        yield return new AdWhoAmITool(options);
        yield return new AdObjectGetTool(options);
        yield return new AdObjectResolveTool(options);
        yield return new AdDelegationAuditTool(options);
        yield return new AdPrivilegedGroupsSummaryTool(options);
        yield return new AdDomainAdminsSummaryTool(options);
        yield return new AdStaleAccountsTool(options);
        yield return new AdLdapQueryTool(options);
        yield return new AdLdapQueryPagedTool(options);
        yield return new AdLdapDiagnosticsTool(options);
        yield return new AdMonitoringProbeCatalogTool(options);
        yield return new AdMonitoringProbeRunTool(options);
        yield return new AdReplicationSummaryTool(options);
        yield return new AdSearchFacetsTool(options);
        yield return new AdSearchTool(options);
        yield return new AdGroupMembersTool(options);
        yield return new AdGroupMembersResolvedTool(options);
        yield return new AdUsersExpiredTool(options);
    }
}
