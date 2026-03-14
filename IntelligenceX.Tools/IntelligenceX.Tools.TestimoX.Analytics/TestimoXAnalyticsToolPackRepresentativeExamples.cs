using System;
using System.Collections.Generic;

namespace IntelligenceX.Tools.TestimoX;

internal static class TestimoXAnalyticsToolPackRepresentativeExamples {
    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> ByToolName { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase) {
            ["testimox_history_query"] = new[] {
                "query monitoring availability rollups from an allowed history store to spot uptime or probe trends"
            },
            ["testimox_probe_index_status"] = new[] {
                "read the latest per-probe monitoring status snapshot before drilling into specific history ranges"
            },
            ["testimox_report_job_history"] = new[] {
                "inspect monitoring report generation history and pick a report job for snapshot follow-up"
            },
            ["testimox_report_snapshot_get"] = new[] {
                "open a stored monitoring HTML report snapshot from an allowed history directory"
            },
            ["testimox_analytics_diagnostics_get"] = new[] {
                "collect a compact TestimoX analytics diagnostics snapshot for the monitoring history store"
            }
        };
}
