using IntelligenceX.Tools;

namespace IntelligenceX.Tools.TestimoX;

internal static class TestimoXAnalyticsToolPackParity {
    public static IReadOnlyList<ToolCapabilityParitySliceDescriptor> Slices { get; } = new[] {
        ToolCapabilityParityRuntime.CreateExpectationSliceDescriptor(
            engineId: "testimox_analytics",
            packId: "testimox_analytics",
            descriptors: ToolCapabilityParityCatalog.TestimoXAnalyticsReadOnlyExpectations,
            note: "Persisted analytics, report, snapshot, and maintenance artifact parity for TestimoX analytics tooling.",
            sourceUnavailableNote: "TestimoX analytics artifact contracts were not available in this runtime.")
    };
}
