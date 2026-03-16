using System;
using System.Collections.Generic;

namespace IntelligenceX.Tools.EventLog;

internal static class EventLogToolPackRepresentativeExamples {
    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> ByToolName { get; } =
        new Dictionary<string, IReadOnlyList<string>>(System.StringComparer.OrdinalIgnoreCase) {
            ["eventlog_live_query"] = new[] {
                "inspect Windows event logs and summarize recurring failures on this machine or a reachable host",
                "triage authentication, service, or update failures on remote servers before pivoting into AD or system tools"
            },
            ["eventlog_timeline_query"] = new[] {
                "build a live event timeline for a local or remote host before pivoting into deeper follow-up tools",
                "reconstruct a host incident timeline and correlate identities, hosts, and channels for follow-up pivots"
            },
            ["eventlog_named_events_query"] = new[] {
                "correlate named Windows events across channels on a reachable machine and turn them into actionable follow-up evidence"
            },
            ["eventlog_evtx_query"] = new[] {
                "analyze EVTX files under allowed roots when the evidence is local instead of querying a live remote machine"
            },
            ["eventlog_channels_list"] = new[] {
                "discover available channels on a local or remote Windows host before running scoped Event Log queries"
            }
        };
}
