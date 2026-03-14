using IntelligenceX.Tools;

namespace IntelligenceX.Tools.TestimoX;

internal static class TestimoXToolPackParity {
    public static IReadOnlyList<ToolCapabilityParitySliceDescriptor> Slices { get; } = new ToolCapabilityParitySliceDescriptor[] {
        ToolCapabilityParityRuntime.CreateExpectationSliceDescriptor(
            engineId: "testimox",
            packId: "testimox",
            descriptors: ToolCapabilityParityCatalog.TestimoXCoreReadOnlyExpectations,
            note: "Profiles, inventory, baseline crosswalk, catalog, and execution parity for TestimoX tooling service.",
            sourceUnavailableNote: "TestimoX rule tooling contracts were not available in this runtime."),
        ToolCapabilityParityRuntime.CreateGovernedBacklogSliceDescriptor(
            engineId: "testimox_powershell",
            packId: "testimox",
            isApplicable: static () => ToolCapabilityParityRuntime.HasType(
                ToolCapabilityParityCatalog.TestimoXPowerShellProviderTypeName,
                ToolCapabilityParityCatalog.TestimoXAssemblyName),
            note: "PowerShell/provider-backed TestimoX service-management flows stay governed outside autonomous phase 1.")
    };
}
