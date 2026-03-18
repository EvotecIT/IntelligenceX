using System;
using System.Collections.Generic;

namespace IntelligenceX.Tools.System;

internal static class SystemToolPackRepresentativeExamples {
    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> ByToolName { get; } =
        new Dictionary<string, IReadOnlyList<string>>(global::System.StringComparer.OrdinalIgnoreCase) {
            ["system_connectivity_probe"] = new[] {
                "preflight remote ComputerX reachability before collecting deeper host inventory or posture data",
                "confirm OS, hardware, and optional time-sync access on a server before running heavier diagnostics"
            },
            ["system_metrics_summary"] = new[] {
                "collect system inventory plus CPU, memory, and disk health locally or on reachable machines",
                "baseline a server after AD or Event Log discovery before deeper host follow-up"
            },
            ["system_info"] = new[] {
                "collect system inventory plus CPU, memory, and disk health locally or on reachable machines",
                "pivot from AD-discovered domain controllers into host identity, operating system, and reachability evidence"
            },
            ["system_hardware_summary"] = new[] {
                "capture hardware identity and posture details for the current machine or a remote target"
            },
            ["system_logical_disks_list"] = new[] {
                "check logical disks, free space, and storage layout on a local or remote machine",
                "verify low disk space or volume pressure on remote servers before blaming AD or application failures"
            },
            ["system_service_list"] = new[] {
                "inspect service state on a local or remote Windows host when a workflow needs host-level follow-up"
            },
            ["system_time_sync"] = new[] {
                "check time skew, time source, and w32time posture locally or on remote domain controllers"
            },
            ["system_windows_update_client_status"] = new[] {
                "inspect WSUS or Windows Update client posture on remote servers when patch drift might explain operational issues"
            },
            ["system_windows_update_telemetry"] = new[] {
                "summarize update freshness, reboot pressure, and servicing telemetry for remote Windows hosts"
            },
            ["system_patch_compliance"] = new[] {
                "compare missing KB or CVE coverage against installed updates when prioritizing server patch remediation"
            }
        };
}
