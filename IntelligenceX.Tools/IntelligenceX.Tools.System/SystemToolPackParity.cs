using IntelligenceX.Tools;

namespace IntelligenceX.Tools.System;

internal static class SystemToolPackParity {
    public static IReadOnlyList<ToolCapabilityParitySliceDescriptor> Slices { get; } = new[] {
        ToolCapabilityParityRuntime.CreateExpectationSliceDescriptor(
            engineId: "computerx",
            packId: "system",
            descriptors: ToolCapabilityParityCatalog.ComputerXReadOnlyExpectations,
            note: "Expanded remote read-only parity for ComputerX operator surfaces.",
            sourceUnavailableNote: "ComputerX remote read-only contracts were not available in this runtime.")
    };
}
