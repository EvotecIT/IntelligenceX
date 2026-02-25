using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using IntelligenceX.Cli.GitHub;

namespace IntelligenceX.Cli.Todo;

internal static partial class PrWatchRunner {
    private const string DefaultRepo = "EvotecIT/IntelligenceX";
    private const string SnapshotSchema = "intelligencex.pr-watch.snapshot.v1";
    private const string StateSchema = "intelligencex.pr-watch.state.v1";
    private const string AuditSchema = "intelligencex.pr-watch.audit.v1";
    private const string ReviewSourceTrustedHuman = "trusted_human";
    private const string ReviewSourceApprovedBot = "approved_bot";
    private const string ReviewSourceOther = "other";
    private const string ActionStopPrClosed = "stop_pr_closed";
    private const string ActionStopReadyToMerge = "stop_ready_to_merge";
    private const string ActionStopExhaustedRetries = "stop_exhausted_retries";
    private const string ActionProcessReviewComment = "process_review_comment";
    private const string ActionDiagnoseCiFailure = "diagnose_ci_failure";
    private const string ActionRetryFailedChecks = "retry_failed_checks";
    private const string ActionIdleWait = "idle_wait";
    private const string StopReasonPrClosed = "pr_closed";
    private const string StopReasonReadyToMerge = "ready_to_merge";
    private const string StopReasonRetryBudgetExhausted = "retry_budget_exhausted";
    private const string ConfirmApplyRetryToken = "RETRY_CHECKS";
    private const string DefaultAuditLogPath = "artifacts/pr-watch/ix-pr-watch-audit.jsonl";
    private const string DefaultPhase = "observe";
    private const string DefaultSource = "manual_cli";
    private const int DefaultPollSeconds = 60;
    private const int DefaultRetryCooldownMinutes = 15;
    private const int MaxRetryCooldownMinutes = 24 * 60;
    private const int MaxPollSeconds = 60 * 60;
    private static readonly HashSet<string> PendingCheckStates = new(StringComparer.OrdinalIgnoreCase) {
        "QUEUED",
        "IN_PROGRESS",
        "PENDING",
        "WAITING",
        "REQUESTED"
    };
    private static readonly HashSet<string> FailedRunConclusions = new(StringComparer.OrdinalIgnoreCase) {
        "failure",
        "timed_out",
        "cancelled",
        "action_required",
        "startup_failure",
        "stale"
    };
    private static readonly HashSet<string> TrustedAuthorAssociations = new(StringComparer.OrdinalIgnoreCase) {
        "OWNER",
        "MEMBER",
        "COLLABORATOR"
    };
    private static readonly HashSet<string> MergeBlockingReviewDecisions = new(StringComparer.OrdinalIgnoreCase) {
        "REVIEW_REQUIRED",
        "CHANGES_REQUESTED"
    };
    private static readonly HashSet<string> MergeConflictOrBlockingStates = new(StringComparer.OrdinalIgnoreCase) {
        "BLOCKED",
        "DIRTY",
        "DRAFT",
        "UNKNOWN"
    };
    private static readonly HashSet<string> AllowedPhases = new(StringComparer.OrdinalIgnoreCase) {
        "observe",
        "assist",
        "repair"
    };
    private static readonly JsonSerializerOptions JsonOptions = new() {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    internal sealed record PrState(
        int Number,
        string Url,
        string Repo,
        string HeadSha,
        string HeadBranch,
        string State,
        bool Merged,
        bool Closed,
        string Mergeable,
        string MergeStateStatus,
        string ReviewDecision
    );

    internal sealed record CheckSummary(
        int PendingCount,
        int FailedCount,
        int PassedCount,
        bool AllTerminal
    );

    internal sealed record FailedRun(
        string RunId,
        string WorkflowName,
        string Status,
        string Conclusion,
        string Url
    );

    internal sealed record ReviewItem(
        string Kind,
        string Id,
        string Author,
        string AuthorAssociation,
        string SourceType,
        string CreatedAt,
        string Body,
        string? Path,
        int? Line,
        string Url
    );

    internal sealed record RecommendedAction(
        string Name,
        string? DedupeKey = null
    );

    internal sealed record RetryState(
        int CurrentShaRetriesUsed,
        int MaxFlakyRetries
    );

    internal sealed record AuditRecord(
        string Schema,
        DateTimeOffset TimestampUtc,
        int PrNumber,
        string Repo,
        string HeadSha,
        string Phase,
        string Action,
        string? DedupeKey,
        string Source,
        string Reason,
        string Result,
        string? RunLink
    );

    internal sealed record WatchSnapshot(
        string Schema,
        DateTimeOffset CapturedAtUtc,
        PrState Pr,
        CheckSummary Checks,
        IReadOnlyList<FailedRun> FailedRuns,
        IReadOnlyList<ReviewItem> NewReviewItems,
        IReadOnlyList<RecommendedAction> Actions,
        string? StopReason,
        RetryState RetryState,
        IReadOnlyList<AuditRecord> Audit
    );

    private sealed class RunnerState {
        public string Schema { get; set; } = StateSchema;
        public string Repo { get; set; } = string.Empty;
        public int PrNumber { get; set; }
        public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
        public string LastSeenHeadSha { get; set; } = string.Empty;
        public Dictionary<string, int> RetriesBySha { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> LastRetryDedupeBySha { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, DateTimeOffset> LastRetryAtBySha { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> SeenIssueCommentIds { get; set; } = new();
        public List<string> SeenReviewCommentIds { get; set; } = new();
        public List<string> SeenReviewIds { get; set; } = new();
    }

    private sealed class Options {
        public string Repo { get; set; } = DefaultRepo;
        public string PrSpec { get; set; } = "auto";
        public int PollSeconds { get; set; } = DefaultPollSeconds;
        public int MaxFlakyRetries { get; set; } = 3;
        public int RetryCooldownMinutes { get; set; } = DefaultRetryCooldownMinutes;
        public bool ApplyRetry { get; set; }
        public string ConfirmApplyRetry { get; set; } = string.Empty;
        public string Phase { get; set; } = DefaultPhase;
        public string Source { get; set; } = DefaultSource;
        public string? RunLink { get; set; }
        public string AuditLogPath { get; set; } = DefaultAuditLogPath;
        public string? StateFilePath { get; set; }
        public bool Once { get; set; } = true;
        public bool Watch { get; set; }
        public bool ShowHelp { get; set; }
        public bool ParseFailed { get; set; }
        public HashSet<string> ApprovedBots { get; } = new(StringComparer.OrdinalIgnoreCase) {
            "intelligencex-review",
            "intelligencex-review[bot]",
            "chatgpt-codex-connector[bot]"
        };
    }

    private sealed record WatchCollectionResult(WatchSnapshot Snapshot, string StateFilePath, RunnerState State);

    private sealed record RetryApplyOutcome(
        bool Applied,
        string Result,
        string Reason,
        string? DedupeKey,
        string? ErrorMessage = null
    );

    public static async Task<int> RunAsync(string[] args) {
        var options = ParseOptions(args);
        if (options.ShowHelp) {
            PrintHelp();
            return options.ParseFailed ? 1 : 0;
        }

        var (authCode, _, authErr) = await GhCli.RunAsync("auth", "status").ConfigureAwait(false);
        if (authCode != 0) {
            Console.Error.WriteLine("gh is not authenticated. Run `gh auth login`.");
            if (!string.IsNullOrWhiteSpace(authErr)) {
                Console.Error.WriteLine(authErr.Trim());
            }
            return 1;
        }

        var authenticatedLogin = await GetAuthenticatedLoginAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(authenticatedLogin)) {
            Console.Error.WriteLine("Unable to determine authenticated GitHub user from `gh api user` or `GITHUB_ACTOR`.");
            return 1;
        }

        if (options.ApplyRetry && !options.Once) {
            Console.Error.WriteLine("`--apply-retry` is supported only in `--once` mode.");
            return 1;
        }

        if (options.ApplyRetry && !string.Equals(options.ConfirmApplyRetry, ConfirmApplyRetryToken, StringComparison.Ordinal)) {
            Console.Error.WriteLine("`--apply-retry` requires `--confirm-apply-retry RETRY_CHECKS`.");
            return 1;
        }

        if (options.Watch) {
            return await RunWatchAsync(options, authenticatedLogin).ConfigureAwait(false);
        }

        try {
            var result = await CollectSnapshotAsync(options, authenticatedLogin).ConfigureAwait(false);
            var plannedAudit = BuildPlannedAuditRecords(
                result.Snapshot,
                options.Phase,
                options.Source,
                options.RunLink);
            AppendAuditRecords(options.AuditLogPath, plannedAudit);
            result = result with {
                Snapshot = result.Snapshot with { Audit = plannedAudit }
            };

            if (options.ApplyRetry) {
                RetryApplyOutcome retryOutcome;
                try {
                    retryOutcome = await TryApplyRetryActionAsync(result).ConfigureAwait(false);
                } catch (Exception ex) {
                    var fallbackRetryAction = result.Snapshot.Actions
                        .FirstOrDefault(action => action.Name.Equals(ActionRetryFailedChecks, StringComparison.OrdinalIgnoreCase));
                    var failureAudit = BuildExecutionAuditRecord(
                        result.Snapshot,
                        options.Phase,
                        options.Source,
                        options.RunLink,
                        ActionRetryFailedChecks,
                        fallbackRetryAction?.DedupeKey,
                        "failed",
                        "retry_execution_exception");
                    AppendAuditRecords(options.AuditLogPath, new[] { failureAudit });
                    result = result with {
                        Snapshot = result.Snapshot with { Audit = result.Snapshot.Audit.Concat(new[] { failureAudit }).ToList() }
                    };
                    Console.Error.WriteLine(ex.Message);
                    return 1;
                }

                var retryAudit = BuildExecutionAuditRecord(
                    result.Snapshot,
                    options.Phase,
                    options.Source,
                    options.RunLink,
                    ActionRetryFailedChecks,
                    retryOutcome.DedupeKey,
                    retryOutcome.Result,
                    retryOutcome.Reason);
                AppendAuditRecords(options.AuditLogPath, new[] { retryAudit });
                result = result with {
                    Snapshot = result.Snapshot with { Audit = result.Snapshot.Audit.Concat(new[] { retryAudit }).ToList() }
                };

                if (retryOutcome.Result.Equals("failed", StringComparison.OrdinalIgnoreCase)) {
                    Console.Error.WriteLine(retryOutcome.ErrorMessage ?? "Retry rerun failed.");
                    return 1;
                }

                if (retryOutcome.Applied) {
                    var refreshedRetryState = new RetryState(
                        CurrentShaRetriesUsed: GetRetriesUsed(result.State, result.Snapshot.Pr.HeadSha),
                        MaxFlakyRetries: options.MaxFlakyRetries
                    );
                    var retryDedupeKey = BuildRetryActionDedupeKey(
                        result.Snapshot.Pr.Repo,
                        result.Snapshot.Pr.Number,
                        result.Snapshot.Pr.HeadSha,
                        result.Snapshot.FailedRuns.Select(item => item.RunId));
                    var allowRetryAction = !ShouldSuppressRetryAction(
                        GetLastRetryDedupeKey(result.State, result.Snapshot.Pr.HeadSha),
                        GetLastRetryAt(result.State, result.Snapshot.Pr.HeadSha),
                        retryDedupeKey,
                        options.RetryCooldownMinutes,
                        DateTimeOffset.UtcNow);
                    var refreshedActions = RecommendActions(
                        result.Snapshot.Pr,
                        result.Snapshot.Checks,
                        result.Snapshot.FailedRuns,
                        result.Snapshot.NewReviewItems,
                        refreshedRetryState,
                        out var refreshedStopReason,
                        allowRetryAction);

                    result = result with {
                        Snapshot = result.Snapshot with {
                            CapturedAtUtc = DateTimeOffset.UtcNow,
                            RetryState = refreshedRetryState,
                            Actions = refreshedActions,
                            StopReason = refreshedStopReason,
                            Audit = result.Snapshot.Audit
                        }
                    };
                }
            }

            PrintJson(result.Snapshot);
            return 0;
        } catch (Exception ex) {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    internal static string DetermineReviewSourceType(string author, string authorAssociation, string authenticatedLogin,
        IReadOnlySet<string> approvedBots) {
        if (string.IsNullOrWhiteSpace(author)) {
            return ReviewSourceOther;
        }

        if (approvedBots.Contains(author)) {
            return ReviewSourceApprovedBot;
        }

        if (string.Equals(author, authenticatedLogin, StringComparison.OrdinalIgnoreCase)) {
            return ReviewSourceTrustedHuman;
        }

        if (TrustedAuthorAssociations.Contains(authorAssociation)) {
            return ReviewSourceTrustedHuman;
        }

        return ReviewSourceOther;
    }

    internal static string BuildRetryActionDedupeKey(string repo, int prNumber, string headSha, IEnumerable<string> runIds) {
        var normalizedRunIds = (runIds ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var payload = $"{repo}|{prNumber.ToString(CultureInfo.InvariantCulture)}|{headSha}|{ActionRetryFailedChecks}|{string.Join(",", normalizedRunIds)}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        var token = Convert.ToHexString(bytes).ToLowerInvariant();
        return $"retry_failed_checks:{token[..12]}";
    }

    internal static IReadOnlyList<RecommendedAction> RecommendActions(PrState pr, CheckSummary checks,
        IReadOnlyList<FailedRun> failedRuns, IReadOnlyList<ReviewItem> newReviewItems, RetryState retryState, out string? stopReason,
        bool allowRetryAction = true) {
        var actions = new List<RecommendedAction>();
        stopReason = null;

        var hasActionableReviewItems = newReviewItems.Any(item =>
            item.SourceType.Equals(ReviewSourceTrustedHuman, StringComparison.OrdinalIgnoreCase) ||
            item.SourceType.Equals(ReviewSourceApprovedBot, StringComparison.OrdinalIgnoreCase));

        if (pr.Closed || pr.Merged) {
            if (hasActionableReviewItems) {
                actions.Add(new RecommendedAction(ActionProcessReviewComment));
            }
            actions.Add(new RecommendedAction(ActionStopPrClosed));
            stopReason = StopReasonPrClosed;
            return actions;
        }

        if (IsReadyToMerge(pr, checks, hasActionableReviewItems)) {
            actions.Add(new RecommendedAction(ActionStopReadyToMerge));
            stopReason = StopReasonReadyToMerge;
            return actions;
        }

        if (hasActionableReviewItems) {
            actions.Add(new RecommendedAction(ActionProcessReviewComment));
        }

        if (checks.FailedCount > 0) {
            if (checks.AllTerminal && retryState.CurrentShaRetriesUsed >= retryState.MaxFlakyRetries) {
                actions.Add(new RecommendedAction(ActionStopExhaustedRetries));
                stopReason = StopReasonRetryBudgetExhausted;
                return actions;
            }

            actions.Add(new RecommendedAction(ActionDiagnoseCiFailure));
            if (allowRetryAction && checks.AllTerminal && failedRuns.Count > 0 && retryState.CurrentShaRetriesUsed < retryState.MaxFlakyRetries) {
                var dedupeKey = BuildRetryActionDedupeKey(pr.Repo, pr.Number, pr.HeadSha, failedRuns.Select(item => item.RunId));
                actions.Add(new RecommendedAction(ActionRetryFailedChecks, dedupeKey));
            }
        }

        if (actions.Count == 0) {
            actions.Add(new RecommendedAction(ActionIdleWait));
        }
        return actions;
    }

    internal static bool ShouldSuppressRetryAction(string? lastRetryDedupeKey, DateTimeOffset? lastRetryAtUtc,
        string currentRetryDedupeKey, int retryCooldownMinutes, DateTimeOffset nowUtc) {
        if (!string.IsNullOrWhiteSpace(lastRetryDedupeKey) &&
            !string.IsNullOrWhiteSpace(currentRetryDedupeKey) &&
            string.Equals(lastRetryDedupeKey, currentRetryDedupeKey, StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        if (lastRetryAtUtc.HasValue && retryCooldownMinutes > 0) {
            var elapsed = nowUtc - lastRetryAtUtc.Value;
            if (elapsed < TimeSpan.FromMinutes(retryCooldownMinutes)) {
                return true;
            }
        }

        return false;
    }

    internal static string NormalizePhase(string? phase) {
        var value = (phase ?? string.Empty).Trim().ToLowerInvariant();
        if (AllowedPhases.Contains(value)) {
            return value;
        }

        return DefaultPhase;
    }

    internal static string NormalizeSource(string? source) {
        var value = (source ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(value)) {
            return DefaultSource;
        }

        var sb = new StringBuilder(value.Length);
        foreach (var ch in value) {
            if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '-' || ch == '_' || ch == '.') {
                sb.Append(ch);
            } else {
                sb.Append('-');
            }
        }

        var normalized = sb.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(normalized) ? DefaultSource : normalized;
    }

    internal static IReadOnlyList<AuditRecord> BuildPlannedAuditRecords(WatchSnapshot snapshot, string phase, string source, string? runLink) {
        var normalizedPhase = NormalizePhase(phase);
        var normalizedSource = NormalizeSource(source);
        var records = new List<AuditRecord>();
        foreach (var action in snapshot.Actions) {
            records.Add(BuildAuditRecord(
                snapshot,
                normalizedPhase,
                normalizedSource,
                runLink,
                action.Name,
                action.DedupeKey,
                "planned",
                ResolvePlannedActionReason(snapshot, action)));
        }

        return records;
    }

    internal static AuditRecord BuildExecutionAuditRecord(WatchSnapshot snapshot, string phase, string source, string? runLink,
        string action, string? dedupeKey, string result, string reason) {
        return BuildAuditRecord(
            snapshot,
            NormalizePhase(phase),
            NormalizeSource(source),
            runLink,
            action,
            dedupeKey,
            result,
            reason);
    }

    private static AuditRecord BuildAuditRecord(WatchSnapshot snapshot, string phase, string source, string? runLink,
        string action, string? dedupeKey, string result, string reason) {
        return new AuditRecord(
            Schema: AuditSchema,
            TimestampUtc: DateTimeOffset.UtcNow,
            PrNumber: snapshot.Pr.Number,
            Repo: snapshot.Pr.Repo,
            HeadSha: snapshot.Pr.HeadSha,
            Phase: phase,
            Action: action,
            DedupeKey: string.IsNullOrWhiteSpace(dedupeKey) ? null : dedupeKey,
            Source: source,
            Reason: reason,
            Result: result,
            RunLink: string.IsNullOrWhiteSpace(runLink) ? null : runLink);
    }

    private static string ResolvePlannedActionReason(WatchSnapshot snapshot, RecommendedAction action) {
        if (action.Name.Equals(ActionRetryFailedChecks, StringComparison.OrdinalIgnoreCase)) {
            return "retry_budget_available";
        }

        if (action.Name.Equals(ActionDiagnoseCiFailure, StringComparison.OrdinalIgnoreCase)) {
            return "checks_failed";
        }

        if (action.Name.Equals(ActionProcessReviewComment, StringComparison.OrdinalIgnoreCase)) {
            return "new_actionable_review_feedback";
        }

        if (action.Name.Equals(ActionIdleWait, StringComparison.OrdinalIgnoreCase)) {
            return "no_actionable_changes";
        }

        if (action.Name.StartsWith("stop_", StringComparison.OrdinalIgnoreCase)) {
            return string.IsNullOrWhiteSpace(snapshot.StopReason)
                ? "terminal_state"
                : $"stop:{snapshot.StopReason}";
        }

        return "planned_action";
    }

}
