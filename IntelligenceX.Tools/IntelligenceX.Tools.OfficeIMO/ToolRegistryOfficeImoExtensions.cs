using System.Collections.Generic;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.OfficeIMO;

/// <summary>
/// Convenience registration helpers for the OfficeIMO tool pack.
/// </summary>
public static class ToolRegistryOfficeImoExtensions {
    /// <summary>
    /// Returns the tool names registered by <see cref="RegisterOfficeImoPack"/>.
    /// </summary>
    /// <param name="options">Tool options.</param>
    /// <returns>Ordered tool names for the pack.</returns>
    public static IReadOnlyList<string> GetRegisteredToolNames(OfficeImoToolOptions options) {
        return ToolPackRegistry.GetRegisteredToolNames(options, CreateTools);
    }

    /// <summary>
    /// Returns tool catalog metadata for tools registered by <see cref="RegisterOfficeImoPack"/>.
    /// </summary>
    /// <param name="options">Tool options.</param>
    /// <returns>Catalog entries derived from runtime definitions and input schemas.</returns>
    public static IReadOnlyList<ToolPackToolCatalogEntryModel> GetRegisteredToolCatalog(OfficeImoToolOptions options) {
        return ToolPackRegistry.GetRegisteredToolCatalog(options, CreateTools);
    }

    /// <summary>
    /// Registers all OfficeIMO tools into the provided registry.
    /// </summary>
    /// <param name="registry">Tool registry.</param>
    /// <param name="options">Tool options.</param>
    /// <returns>The same registry instance for chaining.</returns>
    public static ToolRegistry RegisterOfficeImoPack(this ToolRegistry registry, OfficeImoToolOptions options) {
        return ToolPackRegistry.RegisterPack(registry, options, CreateTools);
    }

    private static IEnumerable<ITool> CreateTools(OfficeImoToolOptions options) {
        foreach (var tool in CreateCoreTools(options)) {
            yield return OfficeImoToolContracts.Apply(tool);
        }
    }

    private static IEnumerable<ITool> CreateCoreTools(OfficeImoToolOptions options) {
        yield return new OfficeImoPackInfoTool(options);
        yield return new OfficeImoReadTool(options);
    }
}
