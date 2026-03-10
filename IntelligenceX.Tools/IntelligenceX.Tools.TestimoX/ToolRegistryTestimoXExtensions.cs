using System.Collections.Generic;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.TestimoX;

/// <summary>
/// Convenience registration helpers for the IX.TestimoX tool pack.
/// </summary>
public static class ToolRegistryTestimoXExtensions {
    /// <summary>
    /// Returns the tool names registered by <see cref="RegisterTestimoXPack"/>.
    /// </summary>
    public static IReadOnlyList<string> GetRegisteredToolNames(TestimoXToolOptions options) {
        return ToolPackRegistry.GetRegisteredToolNames(options, CreateTools);
    }

    /// <summary>
    /// Returns tool catalog metadata for tools registered by <see cref="RegisterTestimoXPack"/>.
    /// </summary>
    public static IReadOnlyList<ToolPackToolCatalogEntryModel> GetRegisteredToolCatalog(TestimoXToolOptions options) {
        return ToolPackRegistry.GetRegisteredToolCatalog(options, CreateTools);
    }

    /// <summary>
    /// Registers all IX.TestimoX tools into the provided registry.
    /// </summary>
    public static ToolRegistry RegisterTestimoXPack(this ToolRegistry registry, TestimoXToolOptions options) {
        return ToolPackRegistry.RegisterPack(registry, options, CreateTools);
    }

    private static IEnumerable<ITool> CreateTools(TestimoXToolOptions options) {
        foreach (var tool in CreateCoreTools(options)) {
            yield return TestimoXToolContracts.Apply(tool);
        }
    }

    private static IEnumerable<ITool> CreateCoreTools(TestimoXToolOptions options) {
        yield return new TestimoXPackInfoTool(options);
        yield return new TestimoXRunsListTool(options);
        yield return new TestimoXRunSummaryTool(options);
        yield return new TestimoXBaselinesListTool(options);
        yield return new TestimoXBaselineCompareTool(options);
        yield return new TestimoXProfilesListTool(options);
        yield return new TestimoXRuleInventoryTool(options);
        yield return new TestimoXSourceQueryTool(options);
        yield return new TestimoXBaselineCrosswalkTool(options);
        yield return new TestimoXRulesListTool(options);
        yield return new TestimoXRulesRunTool(options);
    }
}
