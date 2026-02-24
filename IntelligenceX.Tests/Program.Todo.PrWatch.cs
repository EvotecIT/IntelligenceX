namespace IntelligenceX.Tests;

internal static partial class Program {
#if !NET472
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
            MaxFlakyRetries: 3);

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
            new IntelligenceX.Cli.Todo.PrWatchRunner.FailedRun("100", "ci", "completed", "failure", "https://example/run/100")
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
            MaxFlakyRetries: 3);

        var actions = IntelligenceX.Cli.Todo.PrWatchRunner.RecommendActions(pr, checks, failedRuns, reviews, retry, out _);
        AssertEqual(true, actions.Count >= 3, "expected review+diagnose+retry actions");
        AssertEqual("process_review_comment", actions[0].Name, "review action should be first");
        AssertEqual("diagnose_ci_failure", actions[1].Name, "diagnose action should be second");
        AssertEqual("retry_failed_checks", actions[2].Name, "retry action should follow diagnose");
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
                new IntelligenceX.Cli.Todo.PrWatchRunner.FailedRun("100", "build", "completed", "failure", "https://example/run/100")
            },
            NewReviewItems: Array.Empty<IntelligenceX.Cli.Todo.PrWatchRunner.ReviewItem>(),
            Actions: new[] {
                new IntelligenceX.Cli.Todo.PrWatchRunner.RecommendedAction("diagnose_ci_failure"),
                new IntelligenceX.Cli.Todo.PrWatchRunner.RecommendedAction("retry_failed_checks", "retry_failed_checks:deadbeef1234")
            },
            StopReason: null,
            RetryState: new IntelligenceX.Cli.Todo.PrWatchRunner.RetryState(0, 3),
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
        AssertEqual("retry_budget_available", records[1].Reason, "retry reason");
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
#endif
}
