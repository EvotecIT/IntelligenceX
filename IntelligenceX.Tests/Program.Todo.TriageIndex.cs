namespace IntelligenceX.Tests;

internal static partial class Program {
#if !NET472
    private static void TestTriageIndexTokenizeNormalizesAndDropsStopWords() {
        var tokens = IntelligenceX.Cli.Todo.TriageIndexRunner.Tokenize("Fix the OpenClaw PR dedupe backlog and update index");
        AssertEqual(true, tokens.Contains("openclaw", StringComparer.OrdinalIgnoreCase), "token contains openclaw");
        AssertEqual(true, tokens.Contains("dedupe", StringComparer.OrdinalIgnoreCase), "token contains dedupe");
        AssertEqual(false, tokens.Contains("the", StringComparer.OrdinalIgnoreCase), "stopword removed");
        AssertEqual(false, tokens.Contains("and", StringComparer.OrdinalIgnoreCase), "stopword removed");
    }

    private static void TestTriageIndexDuplicateClustersGroupNearMatches() {
        var now = DateTimeOffset.UtcNow;
        var item1 = new IntelligenceX.Cli.Todo.TriageIndexRunner.TriageIndexItem(
            "pr#1",
            "pull_request",
            1,
            "OpenClaw backlog dedupe index",
            "https://example/pr/1",
            now,
            Array.Empty<string>(),
            IntelligenceX.Cli.Todo.TriageIndexRunner.NormalizeText("OpenClaw backlog dedupe index"),
            IntelligenceX.Cli.Todo.TriageIndexRunner.Tokenize("OpenClaw backlog dedupe index"),
            IntelligenceX.Cli.Todo.TriageIndexRunner.Tokenize("OpenClaw backlog dedupe index for PR triage"),
            new IntelligenceX.Cli.Todo.TriageIndexRunner.PullRequestSignals(false, "MERGEABLE", "APPROVED", "SUCCESS", 4, 80, 10, 0, 2, "dev1"),
            null
        );
        var item2 = new IntelligenceX.Cli.Todo.TriageIndexRunner.TriageIndexItem(
            "issue#2",
            "issue",
            2,
            "PR backlog dedupe index for OpenClaw",
            "https://example/issues/2",
            now,
            Array.Empty<string>(),
            IntelligenceX.Cli.Todo.TriageIndexRunner.NormalizeText("PR backlog dedupe index for OpenClaw"),
            IntelligenceX.Cli.Todo.TriageIndexRunner.Tokenize("PR backlog dedupe index for OpenClaw"),
            IntelligenceX.Cli.Todo.TriageIndexRunner.Tokenize("Need a dedupe index for OpenClaw PR backlog"),
            null,
            new IntelligenceX.Cli.Todo.TriageIndexRunner.IssueSignals(3, "dev2")
        );
        var item3 = new IntelligenceX.Cli.Todo.TriageIndexRunner.TriageIndexItem(
            "pr#3",
            "pull_request",
            3,
            "Website typography refresh",
            "https://example/pr/3",
            now,
            Array.Empty<string>(),
            IntelligenceX.Cli.Todo.TriageIndexRunner.NormalizeText("Website typography refresh"),
            IntelligenceX.Cli.Todo.TriageIndexRunner.Tokenize("Website typography refresh"),
            IntelligenceX.Cli.Todo.TriageIndexRunner.Tokenize("Website typography refresh"),
            new IntelligenceX.Cli.Todo.TriageIndexRunner.PullRequestSignals(false, "MERGEABLE", "REVIEW_REQUIRED", "PENDING", 2, 20, 5, 1, 1, "dev3"),
            null
        );

        var clusters = IntelligenceX.Cli.Todo.TriageIndexRunner.BuildDuplicateClusters(new[] { item1, item2, item3 }, 0.75);
        AssertEqual(1, clusters.Count, "cluster count");
        AssertEqual(true, clusters[0].ItemIds.Contains("pr#1", StringComparer.OrdinalIgnoreCase), "cluster member pr#1");
        AssertEqual(true, clusters[0].ItemIds.Contains("issue#2", StringComparer.OrdinalIgnoreCase), "cluster member issue#2");
        AssertEqual(false, clusters[0].ItemIds.Contains("pr#3", StringComparer.OrdinalIgnoreCase), "cluster excludes unrelated item");
    }

    private static void TestTriageIndexScoreRewardsMergeableApprovedPrs() {
        var now = DateTimeOffset.UtcNow;
        var strong = new IntelligenceX.Cli.Todo.TriageIndexRunner.TriageIndexItem(
            "pr#10",
            "pull_request",
            10,
            "Strong PR",
            "https://example/pr/10",
            now,
            new[] { "ready-to-merge" },
            "strong pr",
            new[] { "strong", "pr" },
            new[] { "strong", "pr" },
            new IntelligenceX.Cli.Todo.TriageIndexRunner.PullRequestSignals(false, "MERGEABLE", "APPROVED", "SUCCESS", 5, 120, 30, 2, 3, "dev"),
            null
        );
        var weak = new IntelligenceX.Cli.Todo.TriageIndexRunner.TriageIndexItem(
            "pr#11",
            "pull_request",
            11,
            "Weak PR",
            "https://example/pr/11",
            now.AddDays(-35),
            new[] { "blocked" },
            "weak pr",
            new[] { "weak", "pr" },
            new[] { "weak", "pr" },
            new IntelligenceX.Cli.Todo.TriageIndexRunner.PullRequestSignals(true, "CONFLICTING", "CHANGES_REQUESTED", "FAILURE", 220, 6000, 2000, 40, 60, "dev"),
            null
        );

        var strongScore = IntelligenceX.Cli.Todo.TriageIndexRunner.ScorePullRequest(strong, now, out _);
        var weakScore = IntelligenceX.Cli.Todo.TriageIndexRunner.ScorePullRequest(weak, now, out _);
        AssertEqual(true, strongScore > weakScore, "strong score should be higher");
    }
#endif
}
