namespace IntelligenceX.Tests;

internal static partial class Program {
#if !NET472
    private static void TestGitHubCiSignalsSummarizeCheckRunsCountsAndFailures() {
        var root = IntelligenceX.Json.JsonLite.Parse("""
{
  "check_runs": [
    { "name": "unit-tests", "status": "completed", "conclusion": "failure", "details_url": "https://example/checks/2" },
    { "name": "lint", "status": "completed", "conclusion": "success", "details_url": "https://example/checks/1" },
    { "name": "build", "status": "in_progress", "conclusion": null, "details_url": "https://example/checks/3" }
  ]
}
""").AsObject();

        var runs = IntelligenceX.GitHub.GitHubCiSignals.ParseCheckRuns(root);
        var snapshot = IntelligenceX.GitHub.GitHubCiSignals.SummarizeCheckRuns(runs);

        AssertEqual(1, snapshot.PassedCount, "github ci signals passed count");
        AssertEqual(1, snapshot.FailedCount, "github ci signals failed count");
        AssertEqual(1, snapshot.PendingCount, "github ci signals pending count");
        AssertEqual(1, snapshot.FailedChecks.Count, "github ci signals failed checks count");
        AssertEqual("unit-tests", snapshot.FailedChecks[0].Name, "github ci signals failed check name");
    }

    private static void TestGitHubCiSignalsParseFailedWorkflowRunsFiltersHeadShaAndSorts() {
        var root = IntelligenceX.Json.JsonLite.Parse("""
{
  "workflow_runs": [
    { "id": 200, "name": "Zulu", "status": "completed", "conclusion": "failure", "head_sha": "head", "html_url": "https://example/runs/200" },
    { "id": 100, "name": "Alpha", "status": "completed", "conclusion": "timed_out", "head_sha": "head", "html_url": "https://example/runs/100" },
    { "id": 300, "name": "Ignored", "status": "completed", "conclusion": "success", "head_sha": "head", "html_url": "https://example/runs/300" },
    { "id": 400, "name": "OtherSha", "status": "completed", "conclusion": "failure", "head_sha": "other", "html_url": "https://example/runs/400" }
  ]
}
""").AsObject();

        var runs = IntelligenceX.GitHub.GitHubCiSignals.ParseFailedWorkflowRuns(root, "head", maxResults: 10);

        AssertEqual(2, runs.Count, "github ci signals failed workflow count");
        AssertEqual("Alpha", runs[0].Name, "github ci signals workflow sort first");
        AssertEqual("100", runs[0].RunId ?? string.Empty, "github ci signals workflow run id");
        AssertEqual(true, IntelligenceX.GitHub.GitHubCiSignals.IsPotentiallyOperationalConclusion(runs[0].Conclusion),
            "github ci signals operational conclusion");
        AssertEqual("Zulu", runs[1].Name, "github ci signals workflow sort second");
    }

    private static void TestGitHubCiSignalsSummarizeWorkflowFailureEvidenceClassifiesKinds() {
        var actionableRoot = IntelligenceX.Json.JsonLite.Parse("""
{
  "jobs": [
    {
      "name": "build-and-test",
      "status": "completed",
      "conclusion": "failure",
      "steps": [
        { "name": "Checkout", "status": "completed", "conclusion": "success" },
        { "name": "dotnet test", "status": "completed", "conclusion": "failure" }
      ]
    }
  ]
}
""").AsObject();
        var operationalRoot = IntelligenceX.Json.JsonLite.Parse("""
{
  "jobs": [
    {
      "name": "bootstrap",
      "status": "completed",
      "conclusion": "timed_out",
      "steps": [
        { "name": "Set up job", "status": "completed", "conclusion": "timed_out" }
      ]
    }
  ]
}
""").AsObject();

        var actionable = IntelligenceX.GitHub.GitHubCiSignals.SummarizeWorkflowFailureEvidence(
            IntelligenceX.GitHub.GitHubCiSignals.ParseWorkflowJobs(actionableRoot), 200);
        var operational = IntelligenceX.GitHub.GitHubCiSignals.SummarizeWorkflowFailureEvidence(
            IntelligenceX.GitHub.GitHubCiSignals.ParseWorkflowJobs(operationalRoot), 200);

        AssertEqual(IntelligenceX.GitHub.GitHubWorkflowFailureKind.Actionable, actionable.Kind,
            "github ci signals actionable failure kind");
        AssertContainsText(actionable.Summary, "dotnet test (failure)",
            "github ci signals actionable failure summary");
        AssertEqual(IntelligenceX.GitHub.GitHubWorkflowFailureKind.Operational, operational.Kind,
            "github ci signals operational failure kind");
        AssertContainsText(operational.Summary, "Set up job (timed_out)",
            "github ci signals operational failure summary");
    }

    private static void TestPrWatchRecommendActionsReadyToMergeStops() {
        var pr = new IntelligenceX.Cli.Todo.PrWatchRunner.PrState(
            Number: 740,
            Url: "https://github.com/EvotecIT/IntelligenceX/pull/740",
            Repo: "EvotecIT/IntelligenceX",
            HeadSha: "abc123",
            HeadBranch: "feature/test",
            State: "OPEN",
            Merged: false,
            Closed: false,
            Mergeable: "MERGEABLE",
            MergeStateStatus: "CLEAN",
            ReviewDecision: "APPROVED");
        var checks = new IntelligenceX.Cli.Todo.PrWatchRunner.CheckSummary(
            PendingCount: 0,
            FailedCount: 0,
            PassedCount: 5,
            AllTerminal: true);
        var retry = new IntelligenceX.Cli.Todo.PrWatchRunner.RetryState(
            CurrentShaRetriesUsed: 0,
            MaxFlakyRetries: 3,
            RetryFailurePolicy: IntelligenceX.Cli.Todo.PrWatchRunner.NormalizeRetryFailurePolicy(null));

        var actions = IntelligenceX.Cli.Todo.PrWatchRunner.RecommendActions(
            pr,
            checks,
            Array.Empty<IntelligenceX.Cli.Todo.PrWatchRunner.FailedRun>(),
            Array.Empty<IntelligenceX.Cli.Todo.PrWatchRunner.ReviewItem>(),
            retry,
            out var stopReason);

        AssertEqual(1, actions.Count, "ready to merge emits a single terminal action");
        AssertEqual("stop_ready_to_merge", actions[0].Name, "ready to merge action");
        AssertEqual("ready_to_merge", stopReason, "ready to merge stop reason");
    }

    private static void TestPrWatchRecommendActionsPrioritizesReviewBeforeRetry() {
        var pr = new IntelligenceX.Cli.Todo.PrWatchRunner.PrState(
            Number: 740,
            Url: "https://github.com/EvotecIT/IntelligenceX/pull/740",
            Repo: "EvotecIT/IntelligenceX",
            HeadSha: "abc123",
            HeadBranch: "feature/test",
            State: "OPEN",
            Merged: false,
            Closed: false,
            Mergeable: "MERGEABLE",
            MergeStateStatus: "CLEAN",
            ReviewDecision: "APPROVED");
        var checks = new IntelligenceX.Cli.Todo.PrWatchRunner.CheckSummary(
            PendingCount: 0,
            FailedCount: 1,
            PassedCount: 4,
            AllTerminal: true);
        var failedRuns = new[] {
            new IntelligenceX.Cli.Todo.PrWatchRunner.FailedRun("100", "ci", "completed", "failure", "https://example/run/100",
                "unknown", string.Empty)
        };
        var reviews = new[] {
            new IntelligenceX.Cli.Todo.PrWatchRunner.ReviewItem(
                Kind: "review_comment",
                Id: "300",
                Author: "maintainer",
                AuthorAssociation: "MEMBER",
                SourceType: "trusted_human",
                CreatedAt: "2026-02-24T00:00:00Z",
                Body: "Please fix regression",
                Path: "src/file.cs",
                Line: 42,
                Url: "https://example/review/300")
        };
        var retry = new IntelligenceX.Cli.Todo.PrWatchRunner.RetryState(
            CurrentShaRetriesUsed: 0,
            MaxFlakyRetries: 3,
            RetryFailurePolicy: IntelligenceX.Cli.Todo.PrWatchRunner.NormalizeRetryFailurePolicy(null));

        var actions = IntelligenceX.Cli.Todo.PrWatchRunner.RecommendActions(pr, checks, failedRuns, reviews, retry, out _);
        AssertEqual(true, actions.Count >= 3, "expected review+diagnose+retry actions");
        AssertEqual("process_review_comment", actions[0].Name, "review action should be first");
        AssertEqual("diagnose_ci_failure", actions[1].Name, "diagnose action should be second");
        AssertEqual("retry_failed_checks", actions[2].Name, "retry action should follow diagnose");
    }

    private static void TestPrWatchRecommendActionsSuppressesRetryForActionableFailures() {
        var pr = new IntelligenceX.Cli.Todo.PrWatchRunner.PrState(
            Number: 740,
            Url: "https://github.com/EvotecIT/IntelligenceX/pull/740",
            Repo: "EvotecIT/IntelligenceX",
            HeadSha: "abc123",
            HeadBranch: "feature/test",
            State: "OPEN",
            Merged: false,
            Closed: false,
            Mergeable: "MERGEABLE",
            MergeStateStatus: "CLEAN",
            ReviewDecision: "APPROVED");
        var checks = new IntelligenceX.Cli.Todo.PrWatchRunner.CheckSummary(
            PendingCount: 0,
            FailedCount: 1,
            PassedCount: 4,
            AllTerminal: true);
        var failedRuns = new[] {
            new IntelligenceX.Cli.Todo.PrWatchRunner.FailedRun("100", "ci", "completed", "failure", "https://example/run/100",
                "actionable", "job test: failed step dotnet test (failure)")
        };
        var retry = new IntelligenceX.Cli.Todo.PrWatchRunner.RetryState(
            CurrentShaRetriesUsed: 0,
            MaxFlakyRetries: 3,
            RetryFailurePolicy: "non-actionable-only");

        var actions = IntelligenceX.Cli.Todo.PrWatchRunner.RecommendActions(
            pr, checks, failedRuns, Array.Empty<IntelligenceX.Cli.Todo.PrWatchRunner.ReviewItem>(), retry, out _);

        AssertEqual(1, actions.Count, "actionable failures should not add retry");
        AssertEqual("diagnose_ci_failure", actions[0].Name, "actionable failures should still diagnose");
    }

    private static void TestPrWatchRecommendActionsLegacyPolicyKeepsRetryForActionableFailures() {
        var pr = new IntelligenceX.Cli.Todo.PrWatchRunner.PrState(
            Number: 740,
            Url: "https://github.com/EvotecIT/IntelligenceX/pull/740",
            Repo: "EvotecIT/IntelligenceX",
            HeadSha: "abc123",
            HeadBranch: "feature/test",
            State: "OPEN",
            Merged: false,
            Closed: false,
            Mergeable: "MERGEABLE",
            MergeStateStatus: "CLEAN",
            ReviewDecision: "APPROVED");
        var checks = new IntelligenceX.Cli.Todo.PrWatchRunner.CheckSummary(
            PendingCount: 0,
            FailedCount: 1,
            PassedCount: 4,
            AllTerminal: true);
        var failedRuns = new[] {
            new IntelligenceX.Cli.Todo.PrWatchRunner.FailedRun("100", "ci", "completed", "failure", "https://example/run/100",
                "actionable", "job test: failed step dotnet test (failure)")
        };
        var retry = new IntelligenceX.Cli.Todo.PrWatchRunner.RetryState(
            CurrentShaRetriesUsed: 0,
            MaxFlakyRetries: 3,
            RetryFailurePolicy: "any");

        var actions = IntelligenceX.Cli.Todo.PrWatchRunner.RecommendActions(
            pr, checks, failedRuns, Array.Empty<IntelligenceX.Cli.Todo.PrWatchRunner.ReviewItem>(), retry, out _);

        AssertEqual(2, actions.Count, "legacy retry policy should still diagnose and retry actionable failures");
        AssertEqual("diagnose_ci_failure", actions[0].Name, "legacy retry policy diagnose action");
        AssertEqual("retry_failed_checks", actions[1].Name, "legacy retry policy retry action");
    }

    private static void TestPrWatchShouldSuggestRetryForNonActionablePolicy() {
        var operationalOnly = IntelligenceX.Cli.Todo.PrWatchRunner.ShouldSuggestRetryForFailedRuns(new[] {
            new IntelligenceX.Cli.Todo.PrWatchRunner.FailedRun("100", "infra", "completed", "timed_out", "https://example/run/100",
                "operational", "job bootstrap: failed step Set up job (timed_out)")
        }, "non-actionable-only");
        var unknownOnly = IntelligenceX.Cli.Todo.PrWatchRunner.ShouldSuggestRetryForFailedRuns(new[] {
            new IntelligenceX.Cli.Todo.PrWatchRunner.FailedRun("200", "ci", "completed", "failure", "https://example/run/200",
                "unknown", string.Empty)
        }, "non-actionable-only");
        var mixedOrActionable = IntelligenceX.Cli.Todo.PrWatchRunner.ShouldSuggestRetryForFailedRuns(new[] {
            new IntelligenceX.Cli.Todo.PrWatchRunner.FailedRun("300", "ci", "completed", "failure", "https://example/run/300",
                "mixed", "job ci: failed steps checkout (timed_out); dotnet test (failure)")
        }, "non-actionable-only");
        var normalizedFallback = IntelligenceX.Cli.Todo.PrWatchRunner.NormalizeRetryFailurePolicy("unexpected-value");

        AssertEqual(true, operationalOnly, "operational failures should stay retryable");
        AssertEqual(true, unknownOnly, "unknown failures should stay retryable");
        AssertEqual(false, mixedOrActionable, "mixed/actionable failures should suppress retry");
        AssertEqual("any", normalizedFallback, "unexpected retry policy should normalize to legacy any mode");
    }

    private static void TestPrWatchDetermineReviewSourceTypeUsesPrecedence() {
        var approvedBots = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "intelligencex-review",
            "chatgpt-codex-connector[bot]"
        };

        var trusted = IntelligenceX.Cli.Todo.PrWatchRunner.DetermineReviewSourceType(
            author: "maintainer",
            authorAssociation: "MEMBER",
            authenticatedLogin: "operator",
            approvedBots: approvedBots);
        AssertEqual("trusted_human", trusted, "trusted association");

        var approvedBot = IntelligenceX.Cli.Todo.PrWatchRunner.DetermineReviewSourceType(
            author: "intelligencex-review",
            authorAssociation: "NONE",
            authenticatedLogin: "operator",
            approvedBots: approvedBots);
        AssertEqual("approved_bot", approvedBot, "approved bot precedence");

        var other = IntelligenceX.Cli.Todo.PrWatchRunner.DetermineReviewSourceType(
            author: "external-contributor",
            authorAssociation: "NONE",
            authenticatedLogin: "operator",
            approvedBots: approvedBots);
        AssertEqual("other", other, "unknown source should be other");
    }

    private static void TestPrWatchRetryDedupeKeyIsStableAcrossRunOrdering() {
        var keyA = IntelligenceX.Cli.Todo.PrWatchRunner.BuildRetryActionDedupeKey(
            "EvotecIT/IntelligenceX",
            740,
            "abc123",
            new[] { "200", "100", "300" });
        var keyB = IntelligenceX.Cli.Todo.PrWatchRunner.BuildRetryActionDedupeKey(
            "EvotecIT/IntelligenceX",
            740,
            "abc123",
            new[] { "300", "200", "100" });

        AssertEqual(keyA, keyB, "dedupe key should be stable regardless of input order");
        AssertContainsText(keyA, "retry_failed_checks:", "dedupe key prefix");
    }

    private static void TestPrWatchRetrySuppressionByMatchingDedupeKey() {
        var now = new DateTimeOffset(2026, 2, 24, 8, 0, 0, TimeSpan.Zero);
        var suppress = IntelligenceX.Cli.Todo.PrWatchRunner.ShouldSuppressRetryAction(
            lastRetryDedupeKey: "retry_failed_checks:abc123",
            lastRetryAtUtc: now.AddMinutes(-20),
            currentRetryDedupeKey: "retry_failed_checks:abc123",
            retryCooldownMinutes: 15,
            nowUtc: now);
        AssertEqual(true, suppress, "matching dedupe key should suppress retry");
    }

    private static void TestPrWatchRetrySuppressionByCooldown() {
        var now = new DateTimeOffset(2026, 2, 24, 8, 0, 0, TimeSpan.Zero);
        var suppress = IntelligenceX.Cli.Todo.PrWatchRunner.ShouldSuppressRetryAction(
            lastRetryDedupeKey: "retry_failed_checks:old",
            lastRetryAtUtc: now.AddMinutes(-5),
            currentRetryDedupeKey: "retry_failed_checks:new",
            retryCooldownMinutes: 15,
            nowUtc: now);
        AssertEqual(true, suppress, "active cooldown should suppress retry");
    }

    private static void TestPrWatchRetrySuppressionAllowsRetryWhenWindowExpired() {
        var now = new DateTimeOffset(2026, 2, 24, 8, 0, 0, TimeSpan.Zero);
        var suppress = IntelligenceX.Cli.Todo.PrWatchRunner.ShouldSuppressRetryAction(
            lastRetryDedupeKey: "retry_failed_checks:old",
            lastRetryAtUtc: now.AddMinutes(-30),
            currentRetryDedupeKey: "retry_failed_checks:new",
            retryCooldownMinutes: 15,
            nowUtc: now);
        AssertEqual(false, suppress, "retry should be allowed when dedupe changed and cooldown expired");
    }

    private static void TestPrWatchNormalizePhaseFallback() {
        var normalized = IntelligenceX.Cli.Todo.PrWatchRunner.NormalizePhase("unknown-phase");
        AssertEqual("observe", normalized, "unknown phase should fall back to observe");
    }

    private static void TestPrWatchNormalizeSourceSanitizesUnsafeChars() {
        var normalized = IntelligenceX.Cli.Todo.PrWatchRunner.NormalizeSource("Workflow Dispatch / Scheduler");
        AssertEqual("workflow-dispatch---scheduler", normalized, "source should be normalized for audit metadata");
    }

    private static void TestPrWatchBuildPlannedAuditRecordsIncludesDedupeAndReason() {
        var snapshot = new IntelligenceX.Cli.Todo.PrWatchRunner.WatchSnapshot(
            Schema: "intelligencex.pr-watch.snapshot.v1",
            CapturedAtUtc: new DateTimeOffset(2026, 2, 24, 8, 0, 0, TimeSpan.Zero),
            Pr: new IntelligenceX.Cli.Todo.PrWatchRunner.PrState(
                Number: 746,
                Url: "https://github.com/EvotecIT/IntelligenceX/pull/746",
                Repo: "EvotecIT/IntelligenceX",
                HeadSha: "abc123",
                HeadBranch: "feature/audit",
                State: "OPEN",
                Merged: false,
                Closed: false,
                Mergeable: "MERGEABLE",
                MergeStateStatus: "CLEAN",
                ReviewDecision: "APPROVED"),
            Checks: new IntelligenceX.Cli.Todo.PrWatchRunner.CheckSummary(
                PendingCount: 0,
                FailedCount: 1,
                PassedCount: 4,
                AllTerminal: true),
            FailedRuns: new[] {
                new IntelligenceX.Cli.Todo.PrWatchRunner.FailedRun("100", "build", "completed", "failure", "https://example/run/100",
                    "actionable", "job build: failed step dotnet test (failure)")
            },
            NewReviewItems: Array.Empty<IntelligenceX.Cli.Todo.PrWatchRunner.ReviewItem>(),
            Actions: new[] {
                new IntelligenceX.Cli.Todo.PrWatchRunner.RecommendedAction("diagnose_ci_failure"),
                new IntelligenceX.Cli.Todo.PrWatchRunner.RecommendedAction("retry_failed_checks", "retry_failed_checks:deadbeef1234")
            },
            StopReason: null,
            RetryState: new IntelligenceX.Cli.Todo.PrWatchRunner.RetryState(0, 3, "non-actionable-only"),
            Audit: Array.Empty<IntelligenceX.Cli.Todo.PrWatchRunner.AuditRecord>());

        var records = IntelligenceX.Cli.Todo.PrWatchRunner.BuildPlannedAuditRecords(
            snapshot,
            "assist",
            "workflow_dispatch",
            "https://github.com/EvotecIT/IntelligenceX/actions/runs/1");

        AssertEqual(2, records.Count, "expected one planned audit record per action");
        AssertEqual("assist", records[0].Phase, "phase should be preserved");
        AssertEqual("workflow_dispatch", records[0].Source, "source should be preserved");
        AssertEqual("diagnose_ci_failure", records[0].Action, "first action name");
        AssertEqual("checks_failed", records[0].Reason, "diagnose reason");
        AssertEqual("retry_failed_checks:deadbeef1234", records[1].DedupeKey, "retry dedupe key");
        AssertEqual("retry_budget_available:non-actionable-only", records[1].Reason, "retry reason");
        AssertEqual("planned", records[1].Result, "planned result");
    }

    private static void TestPrWatchResolveAuthenticatedLoginFallbackUsesActorEnv() {
        var originalActor = Environment.GetEnvironmentVariable("GITHUB_ACTOR");
        var originalTriggeringActor = Environment.GetEnvironmentVariable("GITHUB_TRIGGERING_ACTOR");
        try {
            Environment.SetEnvironmentVariable("GITHUB_ACTOR", "ix-bot-user");
            Environment.SetEnvironmentVariable("GITHUB_TRIGGERING_ACTOR", null);
            var login = IntelligenceX.Cli.Todo.PrWatchRunner.ResolveAuthenticatedLoginFallback();
            AssertEqual("ix-bot-user", login, "authenticated login fallback should use GITHUB_ACTOR");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_ACTOR", originalActor);
            Environment.SetEnvironmentVariable("GITHUB_TRIGGERING_ACTOR", originalTriggeringActor);
        }
    }

    private static void TestPrWatchResolveAuthenticatedLoginFallbackPrefersActorOverTriggeringActor() {
        var originalActor = Environment.GetEnvironmentVariable("GITHUB_ACTOR");
        var originalTriggeringActor = Environment.GetEnvironmentVariable("GITHUB_TRIGGERING_ACTOR");
        try {
            Environment.SetEnvironmentVariable("GITHUB_ACTOR", "primary-actor");
            Environment.SetEnvironmentVariable("GITHUB_TRIGGERING_ACTOR", "secondary-actor");
            var login = IntelligenceX.Cli.Todo.PrWatchRunner.ResolveAuthenticatedLoginFallback();
            AssertEqual("primary-actor", login, "fallback should prefer GITHUB_ACTOR over GITHUB_TRIGGERING_ACTOR");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_ACTOR", originalActor);
            Environment.SetEnvironmentVariable("GITHUB_TRIGGERING_ACTOR", originalTriggeringActor);
        }
    }

    private static void TestPrWatchResolveAuthenticatedLoginFallbackReturnsEmptyWhenUnset() {
        var originalActor = Environment.GetEnvironmentVariable("GITHUB_ACTOR");
        var originalTriggeringActor = Environment.GetEnvironmentVariable("GITHUB_TRIGGERING_ACTOR");
        try {
            Environment.SetEnvironmentVariable("GITHUB_ACTOR", null);
            Environment.SetEnvironmentVariable("GITHUB_TRIGGERING_ACTOR", null);
            var login = IntelligenceX.Cli.Todo.PrWatchRunner.ResolveAuthenticatedLoginFallback();
            AssertEqual(string.Empty, login, "fallback should return empty string when actor env vars are missing");
        } finally {
            Environment.SetEnvironmentVariable("GITHUB_ACTOR", originalActor);
            Environment.SetEnvironmentVariable("GITHUB_TRIGGERING_ACTOR", originalTriggeringActor);
        }
    }

    private static void TestPrWatchConsolidationResolveSourceWithDefaultKeepsTrimmedExplicitSource() {
        var source = IntelligenceX.Cli.Todo.PrWatchConsolidationRunner.ResolveSourceWithDefault("  workflow_dispatch  ", "schedule");
        AssertEqual("workflow_dispatch", source, "explicit source should be trimmed and preserved");
    }

    private static void TestPrWatchConsolidationResolveSourceWithDefaultUsesEventNameWhenSourceEmpty() {
        var source = IntelligenceX.Cli.Todo.PrWatchConsolidationRunner.ResolveSourceWithDefault(string.Empty, "schedule");
        AssertEqual("schedule", source, "empty source should use event name fallback");
    }

    private static void TestPrWatchConsolidationResolveSourceWithDefaultUsesManualCliWhenSourceAndEventEmpty() {
        var source = IntelligenceX.Cli.Todo.PrWatchConsolidationRunner.ResolveSourceWithDefault(" ", null);
        AssertEqual("manual_cli", source, "empty source and event name should fall back to manual_cli");
    }

    private static void TestPrWatchConsolidationTrackerIssueSkippedWhenRollupClean() {
        var rollup = new global::System.Text.Json.Nodes.JsonObject {
            ["failedTargets"] = new global::System.Text.Json.Nodes.JsonArray(),
            ["staleInfraBlocked"] = new global::System.Text.Json.Nodes.JsonArray(),
            ["reviewRequired"] = new global::System.Text.Json.Nodes.JsonArray(),
            ["retryBudgetExhausted"] = new global::System.Text.Json.Nodes.JsonArray()
        };
        var metrics = new global::System.Text.Json.Nodes.JsonObject {
            ["ratiosPct"] = new global::System.Text.Json.Nodes.JsonObject {
                ["staleOpenPrs"] = 0,
                ["reviewRequiredPrs"] = 0,
                ["retryBudgetExhaustedPrs"] = 0,
                ["noProgressPrs"] = 0
            }
        };
        var existing = new[] {
            BuildTrackerIssue(798, "IX PR Babysit Rollup Tracker (schedule)")
        };

        var plan = IntelligenceX.Cli.Todo.PrWatchConsolidationRunner.BuildTrackerIssueSyncPlanForTests(rollup, metrics, existing);
        AssertEqual(false, plan.PublishTrackerIssue, "clean rollup should not publish tracker issue");
        AssertEqual(1, plan.IssuesToClose.Count, "clean rollup should close existing tracker issue");
        AssertEqual(798, plan.IssuesToClose[0]["number"]?.GetValue<int>() ?? 0, "clean plan should close matching tracker issue");
    }

    private static void TestPrWatchConsolidationTrackerIssuePublishesWhenRatiosOrBucketsNonZero() {
        var rollup = new global::System.Text.Json.Nodes.JsonObject {
            ["failedTargets"] = new global::System.Text.Json.Nodes.JsonArray(),
            ["staleInfraBlocked"] = new global::System.Text.Json.Nodes.JsonArray(),
            ["reviewRequired"] = new global::System.Text.Json.Nodes.JsonArray(),
            ["retryBudgetExhausted"] = new global::System.Text.Json.Nodes.JsonArray {
                new global::System.Text.Json.Nodes.JsonObject { ["number"] = 123 }
            }
        };
        var metrics = new global::System.Text.Json.Nodes.JsonObject {
            ["ratiosPct"] = new global::System.Text.Json.Nodes.JsonObject {
                ["staleOpenPrs"] = 0,
                ["reviewRequiredPrs"] = 0,
                ["retryBudgetExhaustedPrs"] = 0,
                ["noProgressPrs"] = 4.5
            }
        };
        var existing = new[] {
            BuildTrackerIssue(12, "newer duplicate"),
            BuildTrackerIssue(5, "canonical older"),
            BuildTrackerIssue(9, "middle duplicate")
        };

        var plan = IntelligenceX.Cli.Todo.PrWatchConsolidationRunner.BuildTrackerIssueSyncPlanForTests(rollup, metrics, existing);
        AssertEqual(true, plan.PublishTrackerIssue, "non-zero buckets or ratios should keep tracker issue publishing enabled");
        AssertNotNull(plan.CanonicalIssue, "actionable tracker plan should keep canonical issue");
        AssertEqual(5, plan.CanonicalIssue!["number"]?.GetValue<int>() ?? 0, "oldest matching issue should remain canonical");
        AssertEqual(2, plan.IssuesToClose.Count, "actionable plan should reconcile duplicate tracker issues");
    }

    private static void TestPrWatchConsolidationTrackerSignalsHandleMissingRatios() {
        var rollup = new global::System.Text.Json.Nodes.JsonObject {
            ["failedTargets"] = new global::System.Text.Json.Nodes.JsonArray(),
            ["staleInfraBlocked"] = new global::System.Text.Json.Nodes.JsonArray(),
            ["reviewRequired"] = new global::System.Text.Json.Nodes.JsonArray(),
            ["retryBudgetExhausted"] = new global::System.Text.Json.Nodes.JsonArray()
        };
        var metrics = new global::System.Text.Json.Nodes.JsonObject();

        var signals = IntelligenceX.Cli.Todo.PrWatchConsolidationRunner.ReadTrackerSignalsForTests(rollup, metrics);
        AssertEqual(0, signals.FailedTargets, "missing ratios should not affect failed target count");
        AssertEqual(0d, signals.StaleOpenPrsRatioPct, "missing stale ratio should default to zero");
        AssertEqual(0d, signals.NoProgressRatioPct, "missing no-progress ratio should default to zero");
        AssertEqual(false, signals.HasActionableContent, "missing ratios with empty buckets should remain non-actionable");
    }

    private static void TestPrWatchConsolidationTrackerPublishesWhenRetryPolicyGuidanceRequestsChange() {
        var rollup = new global::System.Text.Json.Nodes.JsonObject {
            ["failedTargets"] = new global::System.Text.Json.Nodes.JsonArray(),
            ["staleInfraBlocked"] = new global::System.Text.Json.Nodes.JsonArray(),
            ["reviewRequired"] = new global::System.Text.Json.Nodes.JsonArray(),
            ["retryBudgetExhausted"] = new global::System.Text.Json.Nodes.JsonArray()
        };
        var metrics = new global::System.Text.Json.Nodes.JsonObject {
            ["ratiosPct"] = new global::System.Text.Json.Nodes.JsonObject {
                ["staleOpenPrs"] = 0,
                ["reviewRequiredPrs"] = 0,
                ["retryBudgetExhaustedPrs"] = 0,
                ["noProgressPrs"] = 0
            },
            ["retryPolicyGuidance"] = new global::System.Text.Json.Nodes.JsonObject {
                ["currentPolicy"] = "any",
                ["suggestedPolicy"] = "non-actionable-only",
                ["dominantFailureProfile"] = "operational_or_unknown",
                ["dominanceStreak"] = 3,
                ["confidence"] = "high",
                ["shouldConsiderChange"] = true,
                ["reason"] = "Repeated operational/unknown-dominant runs suggest retry noise is mostly infra-like."
            }
        };

        var signals = IntelligenceX.Cli.Todo.PrWatchConsolidationRunner.ReadTrackerSignalsForTests(rollup, metrics);
        var plan = IntelligenceX.Cli.Todo.PrWatchConsolidationRunner.BuildTrackerIssueSyncPlanForTests(
            rollup, metrics, Array.Empty<global::System.Text.Json.Nodes.JsonObject>());

        AssertEqual(true, signals.RetryPolicyChangeRecommended, "guidance should flow into tracker signals");
        AssertEqual(true, signals.HasActionableContent, "guidance-only governance signal should remain actionable");
        AssertEqual(true, plan.PublishTrackerIssue, "guidance recommendation should keep tracker publishing enabled");
    }

    private static void TestPrWatchConsolidationTrackerLabelPlanAddsGovernanceLabelWhenOptedIn() {
        var metrics = new global::System.Text.Json.Nodes.JsonObject {
            ["governanceSignals"] = new global::System.Text.Json.Nodes.JsonObject {
                ["retryPolicyReviewSuggested"] = true,
                ["suggestedRetryFailurePolicy"] = "non-actionable-only",
                ["source"] = "retry_policy_guidance"
            }
        };

        var plan = IntelligenceX.Cli.Todo.PrWatchConsolidationRunner.BuildTrackerIssueLabelPlanForTests(
            trackerIssueLabels: new[] { "ix/pr-watch" },
            applyGovernanceSignalLabel: true,
            metrics: metrics);

        AssertContains(plan.LabelsToAdd, "ix/pr-watch", "static tracker label should still be added");
        AssertContains(plan.LabelsToAdd, "ix/retry-policy-review-suggested", "governance tracker label should be added when opted in");
        AssertEqual(0, plan.LabelsToRemove.Count, "no governance label removal expected when recommendation is active");
    }

    private static void TestPrWatchConsolidationTrackerLabelPlanRemovesGovernanceLabelWhenSignalClears() {
        var metrics = new global::System.Text.Json.Nodes.JsonObject {
            ["governanceSignals"] = new global::System.Text.Json.Nodes.JsonObject {
                ["retryPolicyReviewSuggested"] = false
            }
        };
        var existingIssue = new global::System.Text.Json.Nodes.JsonObject {
            ["labels"] = new global::System.Text.Json.Nodes.JsonArray {
                new global::System.Text.Json.Nodes.JsonObject { ["name"] = "ix/retry-policy-review-suggested" },
                new global::System.Text.Json.Nodes.JsonObject { ["name"] = "ix/pr-watch" }
            }
        };

        var plan = IntelligenceX.Cli.Todo.PrWatchConsolidationRunner.BuildTrackerIssueLabelPlanForTests(
            trackerIssueLabels: new[] { "ix/pr-watch" },
            applyGovernanceSignalLabel: true,
            metrics: metrics,
            existingIssue: existingIssue);

        AssertEqual(0, plan.LabelsToAdd.Count, "no new labels expected when existing labels already cover static set");
        AssertContains(plan.LabelsToRemove, "ix/retry-policy-review-suggested", "governance tracker label should be removed when signal clears");
    }

    private static void TestPrWatchMonitorRollupIncludesFailedRunKinds() {
        var rows = new[] {
            new global::System.Text.Json.Nodes.JsonObject {
                ["number"] = 101,
                ["url"] = "https://example/pr/101",
                ["headSha"] = "sha-101",
                ["stopReason"] = "",
                ["actions"] = new global::System.Text.Json.Nodes.JsonArray("diagnose_ci_failure"),
                ["checks"] = new global::System.Text.Json.Nodes.JsonObject {
                    ["passedCount"] = 1,
                    ["failedCount"] = 1,
                    ["pendingCount"] = 0
                },
                ["failedRuns"] = new global::System.Text.Json.Nodes.JsonObject {
                    ["count"] = 2,
                    ["byKind"] = new global::System.Text.Json.Nodes.JsonObject {
                        ["actionable"] = 1,
                        ["operational"] = 0,
                        ["mixed"] = 0,
                        ["unknown"] = 1
                    }
                }
            },
            new global::System.Text.Json.Nodes.JsonObject {
                ["number"] = 102,
                ["url"] = "https://example/pr/102",
                ["headSha"] = "sha-102",
                ["stopReason"] = "retry_budget_exhausted",
                ["actions"] = new global::System.Text.Json.Nodes.JsonArray("diagnose_ci_failure", "stop_exhausted_retries"),
                ["checks"] = new global::System.Text.Json.Nodes.JsonObject {
                    ["passedCount"] = 0,
                    ["failedCount"] = 1,
                    ["pendingCount"] = 0
                },
                ["failedRuns"] = new global::System.Text.Json.Nodes.JsonObject {
                    ["count"] = 2,
                    ["byKind"] = new global::System.Text.Json.Nodes.JsonObject {
                        ["actionable"] = 0,
                        ["operational"] = 1,
                        ["mixed"] = 1,
                        ["unknown"] = 0
                    }
                }
            }
        };

        var rollup = IntelligenceX.Cli.Todo.PrWatchMonitorRunner.BuildRollupForTests(
            "owner/repo", "non-actionable-only", rows);
        var summary = IntelligenceX.Cli.Todo.PrWatchMonitorRunner.BuildSummaryForTests(
            "owner/repo", "non-actionable-only", rollup);

        var failedRuns = rollup["failedRuns"] as global::System.Text.Json.Nodes.JsonObject ?? new();
        var byKind = failedRuns["byKind"] as global::System.Text.Json.Nodes.JsonObject ?? new();
        AssertEqual("non-actionable-only", rollup["retryFailurePolicy"]?.ToString() ?? string.Empty,
            "monitor rollup should preserve retry policy");
        AssertEqual(4, failedRuns["count"]?.GetValue<int>() ?? 0, "monitor rollup total failed runs");
        AssertEqual(1, byKind["actionable"]?.GetValue<int>() ?? 0, "monitor rollup actionable count");
        AssertEqual(1, byKind["operational"]?.GetValue<int>() ?? 0, "monitor rollup operational count");
        AssertEqual(1, byKind["mixed"]?.GetValue<int>() ?? 0, "monitor rollup mixed count");
        AssertEqual(1, byKind["unknown"]?.GetValue<int>() ?? 0, "monitor rollup unknown count");
        AssertContainsText(summary, "Retry policy: `non-actionable-only`", "monitor summary retry policy");
        AssertContainsText(summary, "Failed run kinds: actionable=1, operational=1, mixed=1, unknown=1",
            "monitor summary failure kind counts");
    }

    private static void TestPrWatchConsolidationFailureKindsFlowIntoRollupMetricsAndSummary() {
        var snapshots = new[] {
            BuildPrWatchSnapshotForTests(
                number: 201,
                failedRuns: new[] {
                    BuildPrWatchFailedRunForTests("actionable"),
                    BuildPrWatchFailedRunForTests("unknown")
                }),
            BuildPrWatchSnapshotForTests(
                number: 202,
                failedRuns: new[] {
                    BuildPrWatchFailedRunForTests("operational"),
                    BuildPrWatchFailedRunForTests("mixed")
                })
        };

        var rollup = IntelligenceX.Cli.Todo.PrWatchConsolidationRunner.BuildRollupForTests(
            "owner/repo", 7, "non-actionable-only", snapshots);
        var metrics = IntelligenceX.Cli.Todo.PrWatchConsolidationRunner.BuildMetricsForTests(
            "owner/repo", 7, "non-actionable-only", snapshots);
        var summary = IntelligenceX.Cli.Todo.PrWatchConsolidationRunner.BuildSummaryForTests(
            "owner/repo", 7, "non-actionable-only", rollup, metrics);
        var trackerBody = IntelligenceX.Cli.Todo.PrWatchConsolidationRunner.BuildTrackerBodyForTests(
            "owner/repo", 7, "non-actionable-only", rollup, metrics);

        var rollupFailedRuns = rollup["failedRuns"] as global::System.Text.Json.Nodes.JsonObject ?? new();
        var rollupByKind = rollupFailedRuns["byKind"] as global::System.Text.Json.Nodes.JsonObject ?? new();
        var metricFailedRuns = metrics["failedRuns"] as global::System.Text.Json.Nodes.JsonObject ?? new();
        var metricByKind = metricFailedRuns["byKind"] as global::System.Text.Json.Nodes.JsonObject ?? new();

        AssertEqual("non-actionable-only", rollup["retryFailurePolicy"]?.ToString() ?? string.Empty,
            "nightly rollup should preserve retry policy");
        AssertEqual(4, rollupFailedRuns["count"]?.GetValue<int>() ?? 0, "nightly rollup total failed runs");
        AssertEqual(1, rollupByKind["actionable"]?.GetValue<int>() ?? 0, "nightly rollup actionable count");
        AssertEqual(1, rollupByKind["operational"]?.GetValue<int>() ?? 0, "nightly rollup operational count");
        AssertEqual(1, rollupByKind["mixed"]?.GetValue<int>() ?? 0, "nightly rollup mixed count");
        AssertEqual(1, rollupByKind["unknown"]?.GetValue<int>() ?? 0, "nightly rollup unknown count");
        AssertEqual(4, metricFailedRuns["count"]?.GetValue<int>() ?? 0, "nightly metrics total failed runs");
        AssertEqual(1, metricByKind["actionable"]?.GetValue<int>() ?? 0, "nightly metrics actionable count");
        AssertEqual(1, metricByKind["operational"]?.GetValue<int>() ?? 0, "nightly metrics operational count");
        AssertEqual(1, metricByKind["mixed"]?.GetValue<int>() ?? 0, "nightly metrics mixed count");
        AssertEqual(1, metricByKind["unknown"]?.GetValue<int>() ?? 0, "nightly metrics unknown count");
        AssertContainsText(summary, "Retry policy: `non-actionable-only`", "nightly summary retry policy");
        AssertContainsText(summary, "Failed workflow runs: 4", "nightly summary failed run count");
        AssertContainsText(summary, "Actionable: 1", "nightly summary actionable count");
        AssertContainsText(trackerBody, "## CI failure evidence", "tracker body ci failure section");
        AssertContainsText(trackerBody, "Operational: 1", "tracker body operational count");
    }

    private static void TestPrWatchRetryPolicyGuidanceSuggestsNonActionableOnlyAfterOperationalStreak() {
        var snapshots = new[] {
            BuildPrWatchSnapshotForTests(
                number: 301,
                failedRuns: new[] {
                    BuildPrWatchFailedRunForTests("operational"),
                    BuildPrWatchFailedRunForTests("unknown")
                })
        };
        var history = new global::System.Text.Json.Nodes.JsonArray {
            BuildPrWatchMetricsHistoryEntryForTests("any", "operational_or_unknown", 1, 0, 0, 1),
            BuildPrWatchMetricsHistoryEntryForTests("any", "operational_or_unknown", 0, 2, 0, 0)
        };

        var metrics = IntelligenceX.Cli.Todo.PrWatchConsolidationRunner.BuildMetricsForTests(
            "owner/repo", 7, "any", snapshots, previousMetrics: null, metricsHistory: history);
        var guidance = metrics["retryPolicyGuidance"] as global::System.Text.Json.Nodes.JsonObject ?? new();
        var governanceSignals = metrics["governanceSignals"] as global::System.Text.Json.Nodes.JsonObject ?? new();
        var governanceSummary = metrics["governanceSummary"] as global::System.Text.Json.Nodes.JsonObject ?? new();
        var summary = IntelligenceX.Cli.Todo.PrWatchConsolidationRunner.BuildSummaryForTests(
            "owner/repo", 7, "any",
            IntelligenceX.Cli.Todo.PrWatchConsolidationRunner.BuildRollupForTests("owner/repo", 7, "any", snapshots),
            metrics);
        var trackerBody = IntelligenceX.Cli.Todo.PrWatchConsolidationRunner.BuildTrackerBodyForTests(
            "owner/repo", 7, "any",
            IntelligenceX.Cli.Todo.PrWatchConsolidationRunner.BuildRollupForTests("owner/repo", 7, "any", snapshots),
            metrics);

        AssertEqual("any", guidance["currentPolicy"]?.ToString() ?? string.Empty, "guidance current policy");
        AssertEqual("non-actionable-only", guidance["suggestedPolicy"]?.ToString() ?? string.Empty, "guidance suggested policy");
        AssertEqual("operational_or_unknown", guidance["dominantFailureProfile"]?.ToString() ?? string.Empty, "guidance profile");
        AssertEqual(3, guidance["dominanceStreak"]?.GetValue<int>() ?? 0, "guidance streak");
        AssertEqual("high", guidance["confidence"]?.ToString() ?? string.Empty, "guidance confidence");
        AssertEqual(true, guidance["shouldConsiderChange"]?.GetValue<bool>() ?? false, "guidance change flag");
        AssertEqual(true, governanceSignals["retryPolicyReviewSuggested"]?.GetValue<bool>() ?? false, "governance signal change flag");
        AssertEqual("non-actionable-only", governanceSignals["suggestedRetryFailurePolicy"]?.ToString() ?? string.Empty, "governance signal suggested policy");
        AssertEqual("retry_policy_guidance", governanceSignals["source"]?.ToString() ?? string.Empty, "governance signal source");
        AssertEqual(true, governanceSummary["retryPolicyReviewSuggested"]?.GetValue<bool>() ?? false, "governance summary active flag");
        AssertContainsText(governanceSummary["summaryLine"]?.ToString() ?? string.Empty, "retry-policy-review-suggested=yes",
            "governance summary line should expose compact recommendation");
        AssertContainsText(summary, "Governance: retry-policy-review-suggested=yes; suggested policy `non-actionable-only`; confidence high; streak 3; profile operational_or_unknown",
            "summary should include compact governance line");
        AssertContainsText(summary, "Consider `non-actionable-only` (high confidence).", "summary guidance recommendation");
        AssertContainsText(summary, "This is advisory only; workflow inputs remain unchanged until a maintainer opts in.",
            "summary guidance should stay opt-in");
        AssertContainsText(trackerBody, "Suggested policy: `non-actionable-only`", "tracker guidance suggested policy");
    }

    private static void TestPrWatchRetryPolicyGuidanceKeepsAnyWhenActionableTrendIsNotStable() {
        var snapshots = new[] {
            BuildPrWatchSnapshotForTests(
                number: 302,
                failedRuns: new[] {
                    BuildPrWatchFailedRunForTests("actionable")
                })
        };
        var history = new global::System.Text.Json.Nodes.JsonArray {
            BuildPrWatchMetricsHistoryEntryForTests("any", "balanced", 1, 1, 0, 0)
        };

        var metrics = IntelligenceX.Cli.Todo.PrWatchConsolidationRunner.BuildMetricsForTests(
            "owner/repo", 7, "any", snapshots, previousMetrics: null, metricsHistory: history);
        var guidance = metrics["retryPolicyGuidance"] as global::System.Text.Json.Nodes.JsonObject ?? new();
        var governanceSignals = metrics["governanceSignals"] as global::System.Text.Json.Nodes.JsonObject ?? new();
        var governanceSummary = metrics["governanceSummary"] as global::System.Text.Json.Nodes.JsonObject ?? new();
        var trackerBody = IntelligenceX.Cli.Todo.PrWatchConsolidationRunner.BuildTrackerBodyForTests(
            "owner/repo", 7, "any",
            IntelligenceX.Cli.Todo.PrWatchConsolidationRunner.BuildRollupForTests("owner/repo", 7, "any", snapshots),
            metrics);

        AssertEqual("any", guidance["currentPolicy"]?.ToString() ?? string.Empty, "non-stable guidance current policy");
        AssertEqual("any", guidance["suggestedPolicy"]?.ToString() ?? string.Empty, "non-stable guidance suggested policy");
        AssertEqual(false, guidance["shouldConsiderChange"]?.GetValue<bool>() ?? true, "non-stable guidance should not change");
        AssertEqual(false, governanceSignals["retryPolicyReviewSuggested"]?.GetValue<bool>() ?? true, "non-stable governance signal should not request change");
        AssertEqual(false, governanceSummary["retryPolicyReviewSuggested"]?.GetValue<bool>() ?? true, "non-stable governance summary should not request change");
        AssertEqual("no active policy-review suggestions", governanceSummary["summaryLine"]?.ToString() ?? string.Empty, "non-stable governance summary line");
        AssertContainsText(trackerBody, "Keep `any`.", "tracker guidance keep policy");
        AssertContainsText(trackerBody, "Governance: no active policy-review suggestions", "tracker should include compact inactive governance line");
    }

    private static global::System.Text.Json.Nodes.JsonObject BuildTrackerIssue(int number, string title) {
        return new global::System.Text.Json.Nodes.JsonObject {
            ["number"] = number,
            ["title"] = title,
            ["url"] = $"https://github.com/EvotecIT/IntelligenceX/issues/{number}",
            ["body"] = $"<!-- intelligencex:pr-watch-rollup-tracker:schedule -->{Environment.NewLine}{title}"
        };
    }

    private static global::System.Text.Json.Nodes.JsonObject BuildPrWatchSnapshotForTests(int number,
        IEnumerable<global::System.Text.Json.Nodes.JsonObject> failedRuns) {
        return new global::System.Text.Json.Nodes.JsonObject {
            ["pr"] = new global::System.Text.Json.Nodes.JsonObject {
                ["number"] = number,
                ["url"] = $"https://example/pr/{number}",
                ["headSha"] = $"sha-{number}",
                ["state"] = "OPEN",
                ["reviewDecision"] = "APPROVED",
                ["mergeStateStatus"] = "CLEAN"
            },
            ["checks"] = new global::System.Text.Json.Nodes.JsonObject {
                ["passedCount"] = 0,
                ["failedCount"] = 1,
                ["pendingCount"] = 0
            },
            ["actions"] = new global::System.Text.Json.Nodes.JsonArray("diagnose_ci_failure"),
            ["stopReason"] = "",
            ["failedRuns"] = new global::System.Text.Json.Nodes.JsonArray(failedRuns.Select(static run => (global::System.Text.Json.Nodes.JsonNode)run).ToArray())
        };
    }

    private static global::System.Text.Json.Nodes.JsonObject BuildPrWatchFailedRunForTests(string failureKind) {
        return new global::System.Text.Json.Nodes.JsonObject {
            ["failureKind"] = failureKind,
            ["workflowName"] = "ci",
            ["conclusion"] = "failure",
            ["url"] = "https://example/run/1"
        };
    }

    private static global::System.Text.Json.Nodes.JsonObject BuildPrWatchMetricsHistoryEntryForTests(string currentPolicy,
        string dominantFailureProfile, int actionable, int operational, int mixed, int unknown) {
        return new global::System.Text.Json.Nodes.JsonObject {
            ["retryPolicyGuidance"] = new global::System.Text.Json.Nodes.JsonObject {
                ["currentPolicy"] = currentPolicy,
                ["suggestedPolicy"] = currentPolicy,
                ["dominantFailureProfile"] = dominantFailureProfile,
                ["dominanceStreak"] = 1,
                ["confidence"] = "low",
                ["shouldConsiderChange"] = false,
                ["reason"] = "test"
            },
            ["failedRuns"] = new global::System.Text.Json.Nodes.JsonObject {
                ["count"] = actionable + operational + mixed + unknown,
                ["byKind"] = new global::System.Text.Json.Nodes.JsonObject {
                    ["actionable"] = actionable,
                    ["operational"] = operational,
                    ["mixed"] = mixed,
                    ["unknown"] = unknown
                }
            }
        };
    }

    private static void TestPrWatchMonitorComposeSourceTagAppendsActionWhenPresent() {
        var source = IntelligenceX.Cli.Todo.PrWatchMonitorRunner.ComposeSourceTag("pull_request_review", "submitted");
        AssertEqual("pull_request_review:submitted", source, "source tag should append action");
    }

    private static void TestPrWatchMonitorComposeSourceTagSkipsEmptyAction() {
        var source = IntelligenceX.Cli.Todo.PrWatchMonitorRunner.ComposeSourceTag("schedule", string.Empty);
        AssertEqual("schedule", source, "source tag should remain unchanged when action is empty");
    }

    private static void TestPrWatchMonitorResolveEventActionFromPayload() {
        var payload = global::System.Text.Json.Nodes.JsonNode.Parse("{\"action\":\"edited\"}") as global::System.Text.Json.Nodes.JsonObject;
        var action = IntelligenceX.Cli.Todo.PrWatchMonitorRunner.ResolveEventActionFromGitHubEventPayload(payload);
        AssertEqual("edited", action, "event action should resolve from payload");
    }

    private static void TestPrWatchMonitorResolvePrSpecFromPayload() {
        var payload = global::System.Text.Json.Nodes.JsonNode.Parse("{\"pull_request\":{\"number\":742}}") as global::System.Text.Json.Nodes.JsonObject;
        var prSpec = IntelligenceX.Cli.Todo.PrWatchMonitorRunner.ResolvePrSpecFromGitHubEventPayload(payload);
        AssertEqual("742", prSpec, "PR spec should resolve from event payload pull_request.number");
    }

    private static void TestPrWatchMonitorResolveSourceWithEventDefaultsKeepsExplicitSource() {
        var payload = global::System.Text.Json.Nodes.JsonNode.Parse("{\"action\":\"edited\"}") as global::System.Text.Json.Nodes.JsonObject;
        var source = IntelligenceX.Cli.Todo.PrWatchMonitorRunner.ResolveSourceWithEventDefaults("workflow_dispatch", true, payload, "pull_request");
        AssertEqual("workflow_dispatch", source, "explicit source should bypass action suffix auto-append");
    }

    private static void TestPrWatchMonitorResolveSourceWithEventDefaultsNormalizesWhitespaceExplicitSource() {
        var payload = global::System.Text.Json.Nodes.JsonNode.Parse("{\"action\":\"edited\"}") as global::System.Text.Json.Nodes.JsonObject;
        var source = IntelligenceX.Cli.Todo.PrWatchMonitorRunner.ResolveSourceWithEventDefaults("   ", true, payload, "pull_request");
        AssertEqual("manual_cli", source, "whitespace explicit source should normalize to manual_cli and bypass action suffix");
    }

    private static void TestPrWatchMonitorResolveSourceWithEventDefaultsUsesEventNameWhenSourceEmpty() {
        var payload = global::System.Text.Json.Nodes.JsonNode.Parse("{\"action\":\"edited\"}") as global::System.Text.Json.Nodes.JsonObject;
        var source = IntelligenceX.Cli.Todo.PrWatchMonitorRunner.ResolveSourceWithEventDefaults(string.Empty, false, payload, "pull_request");
        AssertEqual("pull_request:edited", source, "empty source should fall back to event name before appending action");
    }

    private static void TestPrWatchMonitorResolveSourceWithEventDefaultsUsesManualCliWhenSourceAndEventNameEmpty() {
        var payload = global::System.Text.Json.Nodes.JsonNode.Parse("{\"action\":\"edited\"}") as global::System.Text.Json.Nodes.JsonObject;
        var source = IntelligenceX.Cli.Todo.PrWatchMonitorRunner.ResolveSourceWithEventDefaults(string.Empty, false, payload, null);
        AssertEqual("manual_cli:edited", source, "empty source should fall back to manual_cli when event name is unavailable");
    }

    private static void TestPrWatchMonitorResolvePrSpecWithEventDefaultsKeepsExplicitPr() {
        var payload = global::System.Text.Json.Nodes.JsonNode.Parse("{\"pull_request\":{\"number\":742}}") as global::System.Text.Json.Nodes.JsonObject;
        var prSpec = IntelligenceX.Cli.Todo.PrWatchMonitorRunner.ResolvePrSpecWithEventDefaults("123", payload);
        AssertEqual("123", prSpec, "explicit PR should be preserved even when payload contains pull_request.number");
    }

    private static void TestPrWatchMonitorLoadGitHubEventPayloadParseFailureWarnsAndDefaultsSafely() {
        var originalError = Console.Error;
        using var errorWriter = new StringWriter();
        var tempPath = Path.GetTempFileName();
        try {
            File.WriteAllText(tempPath, "{not-valid-json");
            Console.SetError(errorWriter);

            var payload = IntelligenceX.Cli.Todo.PrWatchMonitorRunner.LoadGitHubEventPayload(tempPath);
            var source = IntelligenceX.Cli.Todo.PrWatchMonitorRunner.ResolveSourceWithEventDefaults(string.Empty, false, payload, "pull_request");
            var prSpec = IntelligenceX.Cli.Todo.PrWatchMonitorRunner.ResolvePrSpecWithEventDefaults(string.Empty, payload);

            AssertEqual(true, payload is null, "invalid JSON payload should fail closed to null");
            AssertEqual("pull_request", source, "source should safely default when payload parsing fails");
            AssertEqual(string.Empty, prSpec, "PR spec should safely default when payload parsing fails");

            var stderr = errorWriter.ToString();
            AssertContainsText(stderr, "Warning: failed to parse GITHUB_EVENT_PATH payload", "parse warning prefix");
            AssertContainsText(stderr, "JsonReaderException", "parse warning should include exception type");
        } finally {
            Console.SetError(originalError);
            if (File.Exists(tempPath)) {
                File.Delete(tempPath);
            }
        }
    }
#endif
}
