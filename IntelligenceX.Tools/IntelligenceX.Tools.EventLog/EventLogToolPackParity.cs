using IntelligenceX.Tools;

namespace IntelligenceX.Tools.EventLog;

internal static class EventLogToolPackParity {
    public static IReadOnlyList<ToolCapabilityParitySliceDescriptor> Slices { get; } = new[] {
        ToolCapabilityParityRuntime.CreateExpectationSliceDescriptor(
            engineId: "eventviewerx",
            packId: "eventlog",
            descriptors: ToolCapabilityParityCatalog.EventViewerXReadOnlyExpectations,
            note: "Remote live-log, named-event correlation, and local EVTX parity for EventViewerX-backed Event Log tooling.",
            sourceUnavailableNote: "EventViewerX read-only contracts were not available in this runtime."),
        ToolCapabilityParityRuntime.CreateExpectationSliceDescriptor(
            engineId: "eventviewerx",
            packId: "eventlog",
            descriptors: ToolCapabilityParityCatalog.EventViewerXGovernedWriteExpectations,
            note: "Governed Event Log channel-policy, classic-log provisioning and cleanup, and collector-subscription administration parity for EventViewerX-backed Event Log tooling.",
            sourceUnavailableNote: "EventViewerX governed-write contracts were not available in this runtime.")
    };
}
