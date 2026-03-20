using System.Collections.Generic;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.ADPlayground;

/// <summary>
/// Convenience registration helpers for the Active Directory lifecycle slice.
/// </summary>
public static class ToolRegistryActiveDirectoryLifecycleExtensions {
    /// <summary>
    /// Returns the tool names registered by <see cref="RegisterActiveDirectoryLifecyclePack"/>.
    /// </summary>
    /// <param name="options">Tool options.</param>
    /// <returns>Ordered tool names for the pack.</returns>
    public static IReadOnlyList<string> GetRegisteredToolNames(ActiveDirectoryToolOptions options) {
        return ToolPackRegistry.GetRegisteredToolNames(options, CreateTools);
    }

    /// <summary>
    /// Returns tool catalog metadata for tools registered by <see cref="RegisterActiveDirectoryLifecyclePack"/>.
    /// </summary>
    /// <param name="options">Tool options.</param>
    /// <returns>Catalog entries derived from runtime definitions and input schemas.</returns>
    public static IReadOnlyList<ToolPackToolCatalogEntryModel> GetRegisteredToolCatalog(ActiveDirectoryToolOptions options) {
        return ToolPackGuidance.ApplyRepresentativeExamples(
            ToolPackRegistry.GetRegisteredToolCatalog(options, CreateTools),
            ActiveDirectoryLifecycleToolPackRepresentativeExamples.ByToolName);
    }

    /// <summary>
    /// Registers all Active Directory lifecycle tools into the provided registry.
    /// </summary>
    /// <param name="registry">Tool registry.</param>
    /// <param name="options">Tool options.</param>
    /// <returns>The same registry instance for chaining.</returns>
    public static ToolRegistry RegisterActiveDirectoryLifecyclePack(this ToolRegistry registry, ActiveDirectoryToolOptions options) {
        return ToolPackRegistry.RegisterPack(registry, options, CreateTools);
    }

    internal static IEnumerable<ITool> CreateTools(ActiveDirectoryToolOptions options) {
        foreach (var tool in CreateCoreTools(options)) {
            yield return ToolDefinitionOverlay.WithDefinition(tool, ActiveDirectoryLifecyclePackContractCatalog.Apply(tool.Definition));
        }
    }

    internal static IEnumerable<ITool> CreateCoreTools(ActiveDirectoryToolOptions options) {
        yield return new AdLifecyclePackInfoTool(options);
        yield return new AdUserLifecycleTool(options);
        yield return new AdComputerLifecycleTool(options);
        yield return new AdGroupLifecycleTool(options);
        yield return new AdOuLifecycleTool(options);
    }
}
