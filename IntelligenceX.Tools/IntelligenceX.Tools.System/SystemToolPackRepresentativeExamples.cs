using System;
using System.Collections.Generic;

namespace IntelligenceX.Tools.System;

internal static class SystemToolPackRepresentativeExamples {
    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> ByToolName { get; } =
        new Dictionary<string, IReadOnlyList<string>>(global::System.StringComparer.OrdinalIgnoreCase) {
            ["system_metrics_summary"] = new[] {
                "collect system inventory plus CPU, memory, and disk health locally or on reachable machines"
            },
            ["system_info"] = new[] {
                "collect system inventory plus CPU, memory, and disk health locally or on reachable machines"
            },
            ["system_hardware_summary"] = new[] {
                "capture hardware identity and posture details for the current machine or a remote target"
            },
            ["system_logical_disks_list"] = new[] {
                "check logical disks, free space, and storage layout on a local or remote machine"
            },
            ["system_service_list"] = new[] {
                "inspect service state on a local or remote Windows host when a workflow needs host-level follow-up"
            }
        };
}
