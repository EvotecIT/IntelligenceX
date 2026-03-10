using System.Collections.Generic;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.TestimoX;

/// <summary>
/// Convenience registration helpers for the TestimoX analytics artifact pack.
/// </summary>
public static class ToolRegistryTestimoXAnalyticsExtensions {
    /// <summary>
    /// Returns the tool names registered by <see cref="RegisterTestimoXAnalyticsPack"/>.
    /// </summary>
    public static IReadOnlyList<string> GetRegisteredToolNames(TestimoXToolOptions options) {
        return ToolPackRegistry.GetRegisteredToolNames(options, CreateTools);
    }

    /// <summary>
    /// Returns tool catalog metadata for tools registered by <see cref="RegisterTestimoXAnalyticsPack"/>.
    /// </summary>
    public static IReadOnlyList<ToolPackToolCatalogEntryModel> GetRegisteredToolCatalog(TestimoXToolOptions options) {
        return ToolPackRegistry.GetRegisteredToolCatalog(options, CreateTools);
    }

    /// <summary>
    /// Registers all TestimoX analytics artifact tools into the provided registry.
    /// </summary>
    public static ToolRegistry RegisterTestimoXAnalyticsPack(this ToolRegistry registry, TestimoXToolOptions options) {
        return ToolPackRegistry.RegisterPack(registry, options, CreateTools);
    }

    private static IEnumerable<ITool> CreateTools(TestimoXToolOptions options) {
        foreach (var tool in CreateCoreTools(options)) {
            yield return TestimoXAnalyticsToolContracts.Apply(tool);
        }
    }

    private static IEnumerable<ITool> CreateCoreTools(TestimoXToolOptions options) {
        yield return new TestimoXAnalyticsPackInfoTool(options);
        yield return new TestimoXReportJobHistoryTool(options);
        yield return new TestimoXHistoryQueryTool(options);
        yield return new TestimoXProbeIndexStatusTool(options);
        yield return new TestimoXAnalyticsDiagnosticsGetTool(options);
        yield return new TestimoXMaintenanceWindowHistoryTool(options);
        yield return new TestimoXReportDataSnapshotGetTool(options);
        yield return new TestimoXReportSnapshotGetTool(options);
    }
}
