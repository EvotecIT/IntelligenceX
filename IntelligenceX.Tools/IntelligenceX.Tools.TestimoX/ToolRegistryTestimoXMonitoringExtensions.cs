using System.Collections.Generic;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Tools.TestimoX;

/// <summary>
/// Convenience registration helpers for the TestimoX monitoring artifact pack.
/// </summary>
public static class ToolRegistryTestimoXMonitoringExtensions {
    /// <summary>
    /// Returns the tool names registered by <see cref="RegisterTestimoXMonitoringPack"/>.
    /// </summary>
    public static IReadOnlyList<string> GetRegisteredToolNames(TestimoXToolOptions options) {
        return ToolPackRegistry.GetRegisteredToolNames(options, CreateTools);
    }

    /// <summary>
    /// Returns tool catalog metadata for tools registered by <see cref="RegisterTestimoXMonitoringPack"/>.
    /// </summary>
    public static IReadOnlyList<ToolPackToolCatalogEntryModel> GetRegisteredToolCatalog(TestimoXToolOptions options) {
        return ToolPackRegistry.GetRegisteredToolCatalog(options, CreateTools);
    }

    /// <summary>
    /// Registers all TestimoX monitoring artifact tools into the provided registry.
    /// </summary>
    public static ToolRegistry RegisterTestimoXMonitoringPack(this ToolRegistry registry, TestimoXToolOptions options) {
        return ToolPackRegistry.RegisterPack(registry, options, CreateTools);
    }

    private static IEnumerable<ITool> CreateTools(TestimoXToolOptions options) {
        foreach (var tool in CreateCoreTools(options)) {
            yield return TestimoXMonitoringToolContracts.Apply(tool);
        }
    }

    private static IEnumerable<ITool> CreateCoreTools(TestimoXToolOptions options) {
        yield return new TestimoXMonitoringPackInfoTool(options);
        yield return new TestimoXReportJobHistoryTool(options);
        yield return new TestimoXHistoryQueryTool(options);
        yield return new TestimoXProbeIndexStatusTool(options);
        yield return new TestimoXMonitoringDiagnosticsGetTool(options);
        yield return new TestimoXMaintenanceWindowHistoryTool(options);
        yield return new TestimoXReportDataSnapshotGetTool(options);
        yield return new TestimoXReportSnapshotGetTool(options);
    }
}

