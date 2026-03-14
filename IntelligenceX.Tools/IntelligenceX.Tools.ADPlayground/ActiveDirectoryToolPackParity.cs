using System.Linq;
using IntelligenceX.Tools;

namespace IntelligenceX.Tools.ADPlayground;

internal static class ActiveDirectoryToolPackParity {
    public static IReadOnlyList<ToolCapabilityParitySliceDescriptor> Slices { get; } = new[] {
        new ToolCapabilityParitySliceDescriptor {
            EngineId = "adplayground_monitoring",
            PackId = "active_directory",
            Evaluate = static definitions => {
                var probeKinds = ToolCapabilityParityRuntime.DiscoverAdMonitoringProbeKinds();
                if (probeKinds.Length == 0) {
                    return ToolCapabilityParityRuntime.CreateStatusEvaluation(
                        ToolCapabilityParityStatuses.SourceUnavailable,
                        sourceAvailable: false,
                        note: "ADPlayground.Monitoring probe metadata was not available in this runtime.");
                }

                var packDefinitions = ToolCapabilityParityRuntime.GetDefinitionsByPackId(definitions, "active_directory");
                var readOnlyCoverage = ToolCapabilityParityRuntime.EvaluateAvailableExpectations(
                    definitions,
                    ToolCapabilityParityCatalog.AdMonitoringReadOnlyExpectations,
                    packDefinitions);
                var surfacedProbeKinds = ToolCapabilityParityRuntime.DiscoverSurfacedAdMonitoringProbeKinds(packDefinitions);

                return ToolCapabilityParityRuntime.CreateCapabilityEvaluation(
                    expectedCapabilities: probeKinds.Concat(readOnlyCoverage.ExpectedCapabilities),
                    surfacedCapabilities: surfacedProbeKinds.Concat(readOnlyCoverage.SurfacedCapabilities),
                    note: "Probe-kind and persisted runtime-state parity between ADPlayground.Monitoring and IX AD monitoring tools.",
                    sourceAvailable: true);
            }
        }
    };
}
