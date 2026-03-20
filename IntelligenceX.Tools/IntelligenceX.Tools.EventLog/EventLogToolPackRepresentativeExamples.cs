using System;
using System.Collections.Generic;

namespace IntelligenceX.Tools.EventLog;

internal static class EventLogToolPackRepresentativeExamples {
    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> ByToolName { get; } =
        new Dictionary<string, IReadOnlyList<string>>(System.StringComparer.OrdinalIgnoreCase) {
            ["eventlog_connectivity_probe"] = new[] {
                "preflight remote Event Log reachability and confirm channel access before deeper live queries",
                "verify a host exposes the expected Security or System channels before running authentication triage"
            },
            ["eventlog_channel_policy_set"] = new[] {
                "preview or apply a governed Event Log channel retention or size change on a local or remote Windows host",
                "enable or disable a Windows event channel with audit-ready rollback context before verification"
            },
            ["eventlog_classic_log_ensure"] = new[] {
                "preview or apply a governed custom classic Event Log and source provisioning change on a local or remote Windows host",
                "ensure a classic log and provider registration exist with audit-ready configuration before verification"
            },
            ["eventlog_classic_log_remove"] = new[] {
                "preview or apply governed cleanup for a custom classic Event Log source and optional custom log removal on a local or remote Windows host",
                "remove a custom classic log/source registration with rollback-ready context and verification guidance"
            },
            ["eventlog_collector_subscriptions_list"] = new[] {
                "inspect current Windows Event Collector subscriptions on a local or remote collector host before planning a governed change",
                "verify WEC subscription names, enabled state, and query shape before or after a collector subscription write"
            },
            ["eventlog_collector_subscription_set"] = new[] {
                "preview or apply a governed Windows Event Collector subscription change on a local or remote collector host",
                "enable, disable, or replace a WEC subscription XML payload with rollback-ready context before verification"
            },
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
