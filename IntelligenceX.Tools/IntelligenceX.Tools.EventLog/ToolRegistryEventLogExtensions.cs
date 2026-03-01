using System.Collections.Generic;
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
        return ToolPackRegistry.GetRegisteredToolNames(options, CreateTools);
    }

    /// <summary>
    /// Returns tool catalog metadata for tools registered by <see cref="RegisterEventLogPack"/>.
    /// </summary>
    /// <param name="options">Tool options.</param>
    /// <returns>Catalog entries derived from runtime definitions and input schemas.</returns>
    public static IReadOnlyList<ToolPackToolCatalogEntryModel> GetRegisteredToolCatalog(EventLogToolOptions options) {
        return ToolPackRegistry.GetRegisteredToolCatalog(options, CreateTools);
    }

    /// <summary>
    /// Registers all EventLog tools into the provided registry.
    /// </summary>
    /// <param name="registry">Tool registry.</param>
    /// <param name="options">Tool options.</param>
    /// <returns>The same registry instance for chaining.</returns>
    public static ToolRegistry RegisterEventLogPack(this ToolRegistry registry, EventLogToolOptions options) {
        return ToolPackRegistry.RegisterPack(registry, options, CreateTools);
    }

    private static IEnumerable<ITool> CreateTools(EventLogToolOptions options) {
        foreach (var tool in CreateCoreTools(options)) {
            yield return EventLogToolContracts.Apply(tool);
        }
    }

    private static IEnumerable<ITool> CreateCoreTools(EventLogToolOptions options) {
        yield return new EventLogPackInfoTool(options);
        yield return new EventLogChannelListTool(options);
        yield return new EventLogProviderListTool(options);
        yield return new EventLogNamedEventsCatalogTool(options);
        yield return new EventLogNamedEventsQueryTool(options);
        yield return new EventLogTimelineExplainTool(options);
        yield return new EventLogTimelineQueryTool(options);
        yield return new EventLogTopEventsTool(options);
        yield return new EventLogLiveQueryTool(options);
        yield return new EventLogLiveStatsTool(options);
        yield return new EventLogEvtxFindTool(options);
        yield return new EventLogEvtxSecuritySummaryTool(options);
        yield return new EventLogEvtxQueryTool(options);
        yield return new EventLogEvtxStatsTool(options);
    }
}
