namespace IntelligenceX.Tests;

internal static partial class Program {
#if !NET472
    private static void TestPullRequestSignalBacktestBuildBucketStatsCalculatesMergeRates() {
        var outcomes = new[] {
            new IntelligenceX.Cli.Todo.PullRequestSignalBacktestRunner.HistoricalSignalOutcome(
                Number: 101,
                Title: "Ready merged",
                Url: "https://example/pr/101",
                Outcome: "merged",
                LifetimeDays: 2.5,
                Signals: new IntelligenceX.Cli.Todo.TriageIndexRunner.PullRequestOperationalSignals(
                    SizeBand: "small",
                    ChurnRisk: "low",
                    MergeReadiness: "ready",
                    Freshness: "fresh",
                    CheckHealth: "healthy",
                    ReviewLatency: "low",
                    MergeConflictRisk: "low"
                )
            ),
            new IntelligenceX.Cli.Todo.PullRequestSignalBacktestRunner.HistoricalSignalOutcome(
                Number: 102,
                Title: "Ready closed",
                Url: "https://example/pr/102",
                Outcome: "closed-unmerged",
                LifetimeDays: 4.0,
                Signals: new IntelligenceX.Cli.Todo.TriageIndexRunner.PullRequestOperationalSignals(
                    SizeBand: "small",
                    ChurnRisk: "low",
                    MergeReadiness: "ready",
                    Freshness: "recent",
                    CheckHealth: "healthy",
                    ReviewLatency: "medium",
                    MergeConflictRisk: "low"
                )
            ),
            new IntelligenceX.Cli.Todo.PullRequestSignalBacktestRunner.HistoricalSignalOutcome(
                Number: 103,
                Title: "Blocked closed",
                Url: "https://example/pr/103",
                Outcome: "closed-unmerged",
                LifetimeDays: 12.0,
                Signals: new IntelligenceX.Cli.Todo.TriageIndexRunner.PullRequestOperationalSignals(
                    SizeBand: "large",
                    ChurnRisk: "high",
                    MergeReadiness: "blocked",
                    Freshness: "aging",
                    CheckHealth: "failing",
                    ReviewLatency: "high",
                    MergeConflictRisk: "high"
                )
            )
        };

        var buckets = IntelligenceX.Cli.Todo.PullRequestSignalBacktestRunner.BuildBucketStats(
            outcomes,
            outcome => outcome.Signals.MergeReadiness);
        var ready = buckets.Single(bucket => bucket.Bucket.Equals("ready", StringComparison.OrdinalIgnoreCase));
        var blocked = buckets.Single(bucket => bucket.Bucket.Equals("blocked", StringComparison.OrdinalIgnoreCase));

        AssertEqual(2, ready.Total, "ready total");
        AssertEqual(1, ready.Merged, "ready merged");
        AssertEqual(1, ready.ClosedUnmerged, "ready closed");
        AssertEqual(0.5, ready.MergeRate, "ready merge rate");
        AssertEqual(1, blocked.Total, "blocked total");
        AssertEqual(0, blocked.Merged, "blocked merged");
        AssertEqual(1, blocked.ClosedUnmerged, "blocked closed");
        AssertEqual(0.0, blocked.MergeRate, "blocked merge rate");
    }
#endif
}
