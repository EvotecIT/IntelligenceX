using System.Collections.Generic;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.DomainDetective;

/// <summary>
/// Convenience registration helpers for the DomainDetective tool pack.
/// </summary>
public static class ToolRegistryDomainDetectiveExtensions {
    /// <summary>
    /// Returns the tool names registered by <see cref="RegisterDomainDetectivePack"/>.
    /// </summary>
    public static IReadOnlyList<string> GetRegisteredToolNames(DomainDetectiveToolOptions options) {
        return ToolPackRegistry.GetRegisteredToolNames(options, CreateTools);
    }

    /// <summary>
    /// Returns tool catalog metadata for tools registered by <see cref="RegisterDomainDetectivePack"/>.
    /// </summary>
    public static IReadOnlyList<ToolPackToolCatalogEntryModel> GetRegisteredToolCatalog(DomainDetectiveToolOptions options) {
        return ToolPackRegistry.GetRegisteredToolCatalog(options, CreateTools);
    }

    /// <summary>
    /// Registers all DomainDetective tools into the provided registry.
    /// </summary>
    public static ToolRegistry RegisterDomainDetectivePack(this ToolRegistry registry, DomainDetectiveToolOptions options) {
        return ToolPackRegistry.RegisterPack(registry, options, CreateTools);
    }

    private static IEnumerable<ITool> CreateTools(DomainDetectiveToolOptions options) {
        yield return new DomainDetectivePackInfoTool(options);
        yield return new DomainDetectiveChecksCatalogTool(options);
        yield return new DomainDetectiveDomainSummaryTool(options);
        yield return new DomainDetectiveNetworkProbeTool(options);
    }
}
