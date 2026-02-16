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

    private static void TestTriageIndexMatchPullRequestToIssuesSupportsExplicitReference() {
        var now = DateTimeOffset.UtcNow;
        var issue42 = new IntelligenceX.Cli.Todo.TriageIndexRunner.TriageIndexItem(
            "issue#42",
            "issue",
            42,
            "Parser crashes on null input",
            "https://github.com/EvotecIT/IntelligenceX/issues/42",
            now,
            Array.Empty<string>(),
            IntelligenceX.Cli.Todo.TriageIndexRunner.NormalizeText("Parser crashes on null input"),
            IntelligenceX.Cli.Todo.TriageIndexRunner.Tokenize("Parser crashes on null input"),
            IntelligenceX.Cli.Todo.TriageIndexRunner.Tokenize("Parser crashes on null input with stack trace"),
            null,
            new IntelligenceX.Cli.Todo.TriageIndexRunner.IssueSignals(4, "dev1")
        );
        var issue77 = new IntelligenceX.Cli.Todo.TriageIndexRunner.TriageIndexItem(
            "issue#77",
            "issue",
            77,
            "Website color tweaks",
            "https://github.com/EvotecIT/IntelligenceX/issues/77",
            now,
            Array.Empty<string>(),
            IntelligenceX.Cli.Todo.TriageIndexRunner.NormalizeText("Website color tweaks"),
            IntelligenceX.Cli.Todo.TriageIndexRunner.Tokenize("Website color tweaks"),
            IntelligenceX.Cli.Todo.TriageIndexRunner.Tokenize("Website color tweaks"),
            null,
            new IntelligenceX.Cli.Todo.TriageIndexRunner.IssueSignals(1, "dev2")
        );

        var matches = IntelligenceX.Cli.Todo.TriageIndexRunner.MatchPullRequestToIssues(
            "EvotecIT/IntelligenceX",
            "Fix parser null handling",
            "This closes #42 and adds regression coverage.",
            new[] { issue42, issue77 }
        );

        AssertEqual(true, matches.Count >= 1, "match exists");
        AssertEqual(42, matches[0].Number, "explicit issue number wins");
        AssertEqual(true, matches[0].Confidence >= 0.95, "explicit confidence");
    }

    private static void TestTriageIndexMatchPullRequestToIssuesSupportsDirectIssueReference() {
        var now = DateTimeOffset.UtcNow;
        var issue42 = new IntelligenceX.Cli.Todo.TriageIndexRunner.TriageIndexItem(
            "issue#42",
            "issue",
            42,
            "Parser crashes on null input",
            "https://github.com/EvotecIT/IntelligenceX/issues/42",
            now,
            Array.Empty<string>(),
            IntelligenceX.Cli.Todo.TriageIndexRunner.NormalizeText("Parser crashes on null input"),
            IntelligenceX.Cli.Todo.TriageIndexRunner.Tokenize("Parser crashes on null input"),
            IntelligenceX.Cli.Todo.TriageIndexRunner.Tokenize("Parser crashes on null input with stack trace"),
            null,
            new IntelligenceX.Cli.Todo.TriageIndexRunner.IssueSignals(4, "dev1")
        );

        var matches = IntelligenceX.Cli.Todo.TriageIndexRunner.MatchPullRequestToIssues(
            "EvotecIT/IntelligenceX",
            "Parser null handling follow-up",
            "Related issue #42; extending guardrails and tests.",
            new[] { issue42 }
        );

        AssertEqual(1, matches.Count, "direct reference match count");
        AssertEqual(42, matches[0].Number, "direct reference issue number");
        AssertEqual(true, matches[0].Confidence >= 0.90, "direct reference confidence");
    }

    private static void TestTriageIndexMatchIssueToPullRequestsSupportsDirectPullRequestReference() {
        var now = DateTimeOffset.UtcNow;
        var pullRequest58 = new IntelligenceX.Cli.Todo.TriageIndexRunner.TriageIndexItem(
            "pr#58",
            "pull_request",
            58,
            "Improve triage label reliability",
            "https://github.com/EvotecIT/IntelligenceX/pull/58",
            now,
            Array.Empty<string>(),
            IntelligenceX.Cli.Todo.TriageIndexRunner.NormalizeText("Improve triage label reliability"),
            IntelligenceX.Cli.Todo.TriageIndexRunner.Tokenize("Improve triage label reliability"),
            IntelligenceX.Cli.Todo.TriageIndexRunner.Tokenize("Improve triage label reliability for issues and pull requests"),
            new IntelligenceX.Cli.Todo.TriageIndexRunner.PullRequestSignals(false, "MERGEABLE", "APPROVED", "SUCCESS", 8, 180, 40, 4, 3, "dev3"),
            null
        );

        var matches = IntelligenceX.Cli.Todo.TriageIndexRunner.MatchIssueToPullRequests(
            "EvotecIT/IntelligenceX",
            "Issue triage labels are flaky",
            "Observed while validating PR #58 in production-like traffic.",
            new[] { pullRequest58 }
        );

        AssertEqual(1, matches.Count, "direct pull-request match count");
        AssertEqual(58, matches[0].Number, "direct pull-request number");
        AssertEqual(true, matches[0].Confidence >= 0.90, "direct pull-request confidence");
    }

    private static void TestTriageIndexMatchIssueToPullRequestsSupportsPullRequestUrlReference() {
        var now = DateTimeOffset.UtcNow;
        var pullRequest72 = new IntelligenceX.Cli.Todo.TriageIndexRunner.TriageIndexItem(
            "pr#72",
            "pull_request",
            72,
            "Add issue to PR linking suggestions",
            "https://github.com/EvotecIT/IntelligenceX/pull/72",
            now,
            Array.Empty<string>(),
            IntelligenceX.Cli.Todo.TriageIndexRunner.NormalizeText("Add issue to PR linking suggestions"),
            IntelligenceX.Cli.Todo.TriageIndexRunner.Tokenize("Add issue to PR linking suggestions"),
            IntelligenceX.Cli.Todo.TriageIndexRunner.Tokenize("Add issue to pull request linking suggestions in triage workflow"),
            new IntelligenceX.Cli.Todo.TriageIndexRunner.PullRequestSignals(false, "MERGEABLE", "REVIEW_REQUIRED", "SUCCESS", 5, 120, 22, 1, 2, "dev4"),
            null
        );

        var matches = IntelligenceX.Cli.Todo.TriageIndexRunner.MatchIssueToPullRequests(
            "EvotecIT/IntelligenceX",
            "Need stronger issue to PR linking",
            "See implementation draft: https://github.com/EvotecIT/IntelligenceX/pull/72",
            new[] { pullRequest72 }
        );

        AssertEqual(1, matches.Count, "pull-request URL match count");
        AssertEqual(72, matches[0].Number, "pull-request URL number");
        AssertEqual(true, matches[0].Confidence >= 0.90, "pull-request URL confidence");
    }

    private static void TestTriageIndexInferCategoryAndTagsDetectsSecurity() {
        var now = DateTimeOffset.UtcNow;
        var item = new IntelligenceX.Cli.Todo.TriageIndexRunner.TriageIndexItem(
            "pr#88",
            "pull_request",
            88,
            "Harden auth token validation",
            "https://example/pr/88",
            now,
            new[] { "security" },
            IntelligenceX.Cli.Todo.TriageIndexRunner.NormalizeText("Harden auth token validation"),
            IntelligenceX.Cli.Todo.TriageIndexRunner.Tokenize("Harden auth token validation"),
            IntelligenceX.Cli.Todo.TriageIndexRunner.Tokenize("Security fix for auth token validation and API checks"),
            new IntelligenceX.Cli.Todo.TriageIndexRunner.PullRequestSignals(false, "MERGEABLE", "APPROVED", "SUCCESS", 6, 40, 8, 1, 2, "dev"),
            null
        );

        var (category, tags) = IntelligenceX.Cli.Todo.TriageIndexRunner.InferCategoryAndTags(item);
        AssertEqual("security", category, "security category");
        AssertEqual(true, tags.Contains("security", StringComparer.OrdinalIgnoreCase), "security tag");
    }

    private static void TestTriageIndexInferCategoryAndTagsWithConfidenceUsesEvidenceStrength() {
        var now = DateTimeOffset.UtcNow;
        var strongEvidenceItem = new IntelligenceX.Cli.Todo.TriageIndexRunner.TriageIndexItem(
            "pr#99",
            "pull_request",
            99,
            "Security hardening for auth flow",
            "https://example/pr/99",
            now,
            new[] { "security", "authentication" },
            IntelligenceX.Cli.Todo.TriageIndexRunner.NormalizeText("Security hardening for auth flow"),
            IntelligenceX.Cli.Todo.TriageIndexRunner.Tokenize("Security hardening for auth flow"),
            IntelligenceX.Cli.Todo.TriageIndexRunner.Tokenize("Fix auth token security checks and API validation"),
            new IntelligenceX.Cli.Todo.TriageIndexRunner.PullRequestSignals(false, "MERGEABLE", "APPROVED", "SUCCESS", 4, 24, 5, 1, 1, "dev"),
            null
        );

        var weakEvidenceItem = strongEvidenceItem with {
            Id = "pr#100",
            Number = 100,
            Title = "Add helper utility",
            Labels = Array.Empty<string>(),
            NormalizedTitle = IntelligenceX.Cli.Todo.TriageIndexRunner.NormalizeText("Add helper utility"),
            TitleTokens = IntelligenceX.Cli.Todo.TriageIndexRunner.Tokenize("Add helper utility"),
            ContextTokens = IntelligenceX.Cli.Todo.TriageIndexRunner.Tokenize("utility helper support")
        };

        var strongInference = IntelligenceX.Cli.Todo.TriageIndexRunner.InferCategoryAndTagsWithConfidence(strongEvidenceItem);
        var weakInference = IntelligenceX.Cli.Todo.TriageIndexRunner.InferCategoryAndTagsWithConfidence(weakEvidenceItem);

        AssertEqual("security", strongInference.Category, "strong inference category");
        AssertEqual(true, strongInference.CategoryConfidence >= 0.80, "strong category confidence");
        AssertEqual(true, strongInference.TagConfidences.TryGetValue("security", out var securityConfidence) && securityConfidence >= 0.75,
            "strong security tag confidence");
        AssertEqual("feature", weakInference.Category, "weak inference fallback category");
        AssertEqual(true, weakInference.CategoryConfidence < 0.62, "weak category confidence below labeling threshold");
    }
#endif
}
