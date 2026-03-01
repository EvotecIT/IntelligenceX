using System.Collections.Generic;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.DnsClientX;

/// <summary>
/// Convenience registration helpers for the DnsClientX tool pack.
/// </summary>
public static class ToolRegistryDnsClientXExtensions {
    /// <summary>
    /// Returns the tool names registered by <see cref="RegisterDnsClientXPack"/>.
    /// </summary>
    public static IReadOnlyList<string> GetRegisteredToolNames(DnsClientXToolOptions options) {
        return ToolPackRegistry.GetRegisteredToolNames(options, CreateTools);
    }

    /// <summary>
    /// Returns tool catalog metadata for tools registered by <see cref="RegisterDnsClientXPack"/>.
    /// </summary>
    public static IReadOnlyList<ToolPackToolCatalogEntryModel> GetRegisteredToolCatalog(DnsClientXToolOptions options) {
        return ToolPackRegistry.GetRegisteredToolCatalog(options, CreateTools);
    }

    /// <summary>
    /// Registers all DnsClientX tools into the provided registry.
    /// </summary>
    public static ToolRegistry RegisterDnsClientXPack(this ToolRegistry registry, DnsClientXToolOptions options) {
        return ToolPackRegistry.RegisterPack(registry, options, CreateTools);
    }

    private static IEnumerable<ITool> CreateTools(DnsClientXToolOptions options) {
        foreach (var tool in CreateCoreTools(options)) {
            yield return DnsClientXToolContracts.Apply(tool);
        }
    }

    private static IEnumerable<ITool> CreateCoreTools(DnsClientXToolOptions options) {
        yield return new DnsClientXPackInfoTool(options);
        yield return new DnsClientXQueryTool(options);
        yield return new DnsClientXPingTool(options);
    }
}
