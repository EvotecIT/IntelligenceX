using System;
using System.Collections.Generic;
using System.Linq;

namespace IntelligenceX.Chat.Host;

internal static partial class Program {
    private static List<string> EvaluateScenarioRollupAssertions(ChatScenarioDefinition scenario, IReadOnlyList<ScenarioTurnRun> turnRuns) {
        var failures = new List<string>();
        if (scenario is null || scenario.MaxPhaseP95DurationMs.Count == 0) {
            return failures;
        }

        var rollups = BuildScenarioPhaseTimingRollups(turnRuns);
        var observedByPhase = rollups.ToDictionary(
            static rollup => rollup.Phase,
            static rollup => rollup,
            StringComparer.OrdinalIgnoreCase);
        foreach (var phaseLimit in scenario.MaxPhaseP95DurationMs) {
            if (!TryNormalizeScenarioPhaseName(phaseLimit.Key, out var normalizedPhase)) {
                failures.Add($"Expected scenario rollup phase to be a known phase name, but observed '{phaseLimit.Key}'.");
                continue;
            }

            var maxP95DurationMs = phaseLimit.Value;
            if (maxP95DurationMs < 0) {
                failures.Add(
                    $"Expected scenario rollup phase '{phaseLimit.Key}' (normalized '{normalizedPhase}') p95 threshold to be >= 0ms;"
                    + $" observed {maxP95DurationMs}ms.");
                continue;
            }

            if (!observedByPhase.TryGetValue(normalizedPhase, out var observedRollup)) {
                failures.Add($"Expected scenario rollup phase '{normalizedPhase}' to be present for p95 guardrail checks.");
                continue;
            }

            if (observedRollup.P95DurationMs <= maxP95DurationMs) {
                continue;
            }

            failures.Add(
                $"Expected scenario rollup phase '{normalizedPhase}' p95 <= {maxP95DurationMs}ms;"
                + $" observed {observedRollup.P95DurationMs}ms across {observedRollup.Samples} sample(s).");
        }

        return failures;
    }
}
