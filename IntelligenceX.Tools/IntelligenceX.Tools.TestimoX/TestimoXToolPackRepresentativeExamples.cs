using System;
using System.Collections.Generic;

namespace IntelligenceX.Tools.TestimoX;

internal static class TestimoXToolPackRepresentativeExamples {
    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> ByToolName { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase) {
            ["testimox_runs_list"] = new[] {
                "review stored TestimoX runs from an allowed result store and pick a run for deeper follow-up"
            },
            ["testimox_run_summary"] = new[] {
                "summarize a stored TestimoX run, filter by scope or rule name, and inspect outcome patterns"
            },
            ["testimox_rules_run"] = new[] {
                "run selected TestimoX rules against chosen domains or domain controllers and inspect typed outcomes"
            },
            ["testimox_baseline_compare"] = new[] {
                "compare TestimoX vendor baselines and highlight desired-value drift for remediation planning"
            },
            ["testimox_baseline_crosswalk"] = new[] {
                "map TestimoX rules to vendor baseline documentation and remediation references"
            }
        };
}
