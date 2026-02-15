using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.EventLog;

/// <summary>
/// Convenience registration helpers for the EventLog tool pack.
/// </summary>
public static class ToolRegistryEventLogExtensions {
    /// <summary>
    /// Returns the tool names registered by <see cref="RegisterEventLogPack"/>.
    /// </summary>
    /// <param name="options">Tool options.</param>
    /// <returns>Ordered tool names for the pack.</returns>
    public static IReadOnlyList<string> GetRegisteredToolNames(EventLogToolOptions options) {
        if (options is null) {
            throw new ArgumentNullException(nameof(options));
        }

        return CreateTools(options).Select(static tool => tool.Definition.Name).ToArray();
    }

    /// <summary>
    /// Returns tool catalog metadata for tools registered by <see cref="RegisterEventLogPack"/>.
    /// </summary>
    /// <param name="options">Tool options.</param>
    /// <returns>Catalog entries derived from runtime definitions and input schemas.</returns>
    public static IReadOnlyList<ToolPackToolCatalogEntryModel> GetRegisteredToolCatalog(EventLogToolOptions options) {
        if (options is null) {
            throw new ArgumentNullException(nameof(options));
        }

        return ToolPackGuidance.CatalogFromTools(CreateTools(options));
    }

    /// <summary>
    /// Registers all EventLog tools into the provided registry.
    /// </summary>
    /// <param name="registry">Tool registry.</param>
    /// <param name="options">Tool options.</param>
    /// <returns>The same registry instance for chaining.</returns>
    public static ToolRegistry RegisterEventLogPack(this ToolRegistry registry, EventLogToolOptions options) {
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

    private static IEnumerable<ITool> CreateTools(EventLogToolOptions options) {
        yield return new EventLogPackInfoTool(options);
        yield return new EventLogChannelListTool(options);
        yield return new EventLogProviderListTool(options);
        yield return new EventLogTopEventsTool(options);
        yield return new EventLogLiveQueryTool(options);
        yield return new EventLogLiveStatsTool(options);
        yield return new EventLogEvtxFindTool(options);
        yield return new EventLogEvtxQueryTool(options);
        yield return new EventLogEvtxStatsTool(options);
        yield return new EventLogEvtxUserLogonsReportTool(options);
        yield return new EventLogEvtxFailedLogonsReportTool(options);
        yield return new EventLogEvtxAccountLockoutsReportTool(options);
    }
}
