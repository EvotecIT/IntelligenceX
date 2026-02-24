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

internal static class PrWatchRunner {
    private const string DefaultRepo = "EvotecIT/IntelligenceX";
    private const string SnapshotSchema = "intelligencex.pr-watch.snapshot.v1";
    private const string StateSchema = "intelligencex.pr-watch.state.v1";
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
    private const int DefaultPollSeconds = 60;
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
    private static readonly JsonSerializerOptions JsonOptions = new() {
        WriteIndented = false
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

    internal sealed record WatchSnapshot(
        string Schema,
        DateTimeOffset CapturedAtUtc,
        PrState Pr,
        CheckSummary Checks,
        IReadOnlyList<FailedRun> FailedRuns,
        IReadOnlyList<ReviewItem> NewReviewItems,
        IReadOnlyList<RecommendedAction> Actions,
        string? StopReason,
        RetryState RetryState
    );

    private sealed class RunnerState {
        public string Schema { get; set; } = StateSchema;
        public string Repo { get; set; } = string.Empty;
        public int PrNumber { get; set; }
        public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
        public string LastSeenHeadSha { get; set; } = string.Empty;
        public Dictionary<string, int> RetriesBySha { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> SeenIssueCommentIds { get; set; } = new();
        public List<string> SeenReviewCommentIds { get; set; } = new();
        public List<string> SeenReviewIds { get; set; } = new();
    }

    private sealed class Options {
        public string Repo { get; set; } = DefaultRepo;
        public string PrSpec { get; set; } = "auto";
        public int PollSeconds { get; set; } = DefaultPollSeconds;
        public int MaxFlakyRetries { get; set; } = 3;
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

    private sealed record WatchCollectionResult(WatchSnapshot Snapshot, string StateFilePath);

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
            Console.Error.WriteLine("Unable to determine authenticated GitHub user from `gh api user`.");
            return 1;
        }

        if (options.Watch) {
            return await RunWatchAsync(options, authenticatedLogin).ConfigureAwait(false);
        }

        try {
            var snapshot = await CollectSnapshotAsync(options, authenticatedLogin).ConfigureAwait(false);
            PrintJson(snapshot.Snapshot);
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
        IReadOnlyList<FailedRun> failedRuns, IReadOnlyList<ReviewItem> newReviewItems, RetryState retryState, out string? stopReason) {
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
            if (checks.AllTerminal && failedRuns.Count > 0 && retryState.CurrentShaRetriesUsed < retryState.MaxFlakyRetries) {
                var dedupeKey = BuildRetryActionDedupeKey(pr.Repo, pr.Number, pr.HeadSha, failedRuns.Select(item => item.RunId));
                actions.Add(new RecommendedAction(ActionRetryFailedChecks, dedupeKey));
            }
        }

        if (actions.Count == 0) {
            actions.Add(new RecommendedAction(ActionIdleWait));
        }
        return actions;
    }

    private static async Task<int> RunWatchAsync(Options options, string authenticatedLogin) {
        var pollSeconds = options.PollSeconds;
        string? lastChangeKey = null;

        while (true) {
            WatchCollectionResult result;
            try {
                result = await CollectSnapshotAsync(options, authenticatedLogin).ConfigureAwait(false);
            } catch (Exception ex) {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }

            PrintJson(new {
                @event = "snapshot",
                payload = result.Snapshot,
                stateFile = result.StateFilePath,
                nextPollSeconds = pollSeconds
            });

            if (!string.IsNullOrWhiteSpace(result.Snapshot.StopReason)) {
                PrintJson(new {
                    @event = "stop",
                    reason = result.Snapshot.StopReason,
                    pr = result.Snapshot.Pr
                });
                return 0;
            }

            var currentChangeKey = BuildChangeKey(result.Snapshot);
            var changed = !string.Equals(currentChangeKey, lastChangeKey, StringComparison.Ordinal);
            var ciGreen = IsCiGreen(result.Snapshot.Checks);
            if (!ciGreen || changed || string.IsNullOrWhiteSpace(lastChangeKey)) {
                pollSeconds = options.PollSeconds;
            } else {
                pollSeconds = Math.Min(MaxPollSeconds, pollSeconds * 2);
            }

            lastChangeKey = currentChangeKey;
            await Task.Delay(TimeSpan.FromSeconds(pollSeconds)).ConfigureAwait(false);
        }
    }

    private static bool IsCiGreen(CheckSummary checks) {
        return checks.AllTerminal && checks.FailedCount == 0 && checks.PendingCount == 0;
    }

    private static string BuildChangeKey(WatchSnapshot snapshot) {
        var reviewIds = snapshot.NewReviewItems
            .Select(item => $"{item.Kind}:{item.Id}")
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        var actions = snapshot.Actions.Select(item => item.Name).ToArray();
        return string.Join("|", new[] {
            snapshot.Pr.HeadSha,
            snapshot.Pr.State,
            snapshot.Pr.Mergeable,
            snapshot.Pr.MergeStateStatus,
            snapshot.Pr.ReviewDecision,
            snapshot.Checks.PassedCount.ToString(CultureInfo.InvariantCulture),
            snapshot.Checks.FailedCount.ToString(CultureInfo.InvariantCulture),
            snapshot.Checks.PendingCount.ToString(CultureInfo.InvariantCulture),
            string.Join(",", reviewIds),
            string.Join(",", actions)
        });
    }

    private static async Task<WatchCollectionResult> CollectSnapshotAsync(Options options, string authenticatedLogin) {
        var pr = await ResolvePrAsync(options).ConfigureAwait(false);
        var statePath = ResolveStatePath(options, pr);
        var state = LoadState(statePath);
        var checks = await GetChecksAsync(pr).ConfigureAwait(false);
        var checksSummary = SummarizeChecks(checks);
        var failedRuns = await GetFailedRunsForHeadShaAsync(pr).ConfigureAwait(false);
        var newReviewItems = await GetNewReviewItemsAsync(pr, state, authenticatedLogin, options.ApprovedBots).ConfigureAwait(false);

        var retryState = new RetryState(
            CurrentShaRetriesUsed: GetRetriesUsed(state, pr.HeadSha),
            MaxFlakyRetries: options.MaxFlakyRetries
        );
        var actions = RecommendActions(pr, checksSummary, failedRuns, newReviewItems, retryState, out var stopReason);

        state.Repo = pr.Repo;
        state.PrNumber = pr.Number;
        state.LastSeenHeadSha = pr.HeadSha;
        state.UpdatedAtUtc = DateTimeOffset.UtcNow;
        SaveState(statePath, state);

        var snapshot = new WatchSnapshot(
            Schema: SnapshotSchema,
            CapturedAtUtc: DateTimeOffset.UtcNow,
            Pr: pr,
            Checks: checksSummary,
            FailedRuns: failedRuns,
            NewReviewItems: newReviewItems,
            Actions: actions,
            StopReason: stopReason,
            RetryState: retryState
        );
        return new WatchCollectionResult(snapshot, statePath);
    }

    private static string ResolveStatePath(Options options, PrState pr) {
        if (!string.IsNullOrWhiteSpace(options.StateFilePath)) {
            return options.StateFilePath!;
        }

        var repoToken = pr.Repo.Replace("/", "-", StringComparison.Ordinal);
        return Path.Combine("artifacts", "pr-watch", $"ix-pr-watch-{repoToken}-pr{pr.Number.ToString(CultureInfo.InvariantCulture)}.json");
    }

    private static RunnerState LoadState(string path) {
        if (!File.Exists(path)) {
            return new RunnerState();
        }

        try {
            var text = File.ReadAllText(path);
            var value = JsonSerializer.Deserialize<RunnerState>(text);
            if (value is null) {
                return new RunnerState();
            }
            value.RetriesBySha ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            value.SeenIssueCommentIds ??= new List<string>();
            value.SeenReviewCommentIds ??= new List<string>();
            value.SeenReviewIds ??= new List<string>();
            return value;
        } catch {
            return new RunnerState();
        }
    }

    private static void SaveState(string path, RunnerState state) {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) {
            Directory.CreateDirectory(directory);
        }

        var payload = JsonSerializer.Serialize(state, new JsonSerializerOptions {
            WriteIndented = true
        });
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, payload, new UTF8Encoding(false));
        File.Copy(tempPath, path, overwrite: true);
        File.Delete(tempPath);
    }

    private static int GetRetriesUsed(RunnerState state, string headSha) {
        if (string.IsNullOrWhiteSpace(headSha)) {
            return 0;
        }

        if (!state.RetriesBySha.TryGetValue(headSha, out var count)) {
            return 0;
        }

        return Math.Max(0, count);
    }

    private static async Task<string> GetAuthenticatedLoginAsync() {
        var (code, stdout, _) = await GhCli.RunAsync("api", "user").ConfigureAwait(false);
        if (code != 0 || string.IsNullOrWhiteSpace(stdout)) {
            return string.Empty;
        }

        try {
            using var doc = JsonDocument.Parse(stdout);
            return ReadString(doc.RootElement, "login");
        } catch {
            return string.Empty;
        }
    }

    private static async Task<PrState> ResolvePrAsync(Options options) {
        var args = new List<string> { "pr", "view" };
        if (!options.PrSpec.Equals("auto", StringComparison.OrdinalIgnoreCase)) {
            args.Add(options.PrSpec);
        }
        args.Add("--repo");
        args.Add(options.Repo);
        args.Add("--json");
        args.Add("number,url,state,mergedAt,closedAt,headRefName,headRefOid,mergeable,mergeStateStatus,reviewDecision");

        var (code, stdout, stderr) = await GhCli.RunAsync(TimeSpan.FromSeconds(90), args.ToArray()).ConfigureAwait(false);
        if (code != 0) {
            throw new InvalidOperationException(!string.IsNullOrWhiteSpace(stderr)
                ? stderr.Trim()
                : "Failed to resolve PR metadata via gh pr view.");
        }

        using var doc = JsonDocument.Parse(stdout);
        var root = doc.RootElement;
        var number = ReadInt(root, "number");
        if (number <= 0) {
            throw new InvalidOperationException("`gh pr view` response missing PR number.");
        }

        var state = ReadString(root, "state");
        var mergedAt = ReadNullableString(root, "mergedAt");
        var closedAt = ReadNullableString(root, "closedAt");
        return new PrState(
            Number: number,
            Url: ReadString(root, "url"),
            Repo: options.Repo,
            HeadSha: ReadString(root, "headRefOid"),
            HeadBranch: ReadString(root, "headRefName"),
            State: state,
            Merged: !string.IsNullOrWhiteSpace(mergedAt),
            Closed: !string.IsNullOrWhiteSpace(closedAt) || state.Equals("CLOSED", StringComparison.OrdinalIgnoreCase),
            Mergeable: ReadString(root, "mergeable"),
            MergeStateStatus: ReadString(root, "mergeStateStatus"),
            ReviewDecision: ReadString(root, "reviewDecision")
        );
    }

    private static async Task<IReadOnlyList<JsonElement>> GetChecksAsync(PrState pr) {
        var (code, stdout, stderr) = await GhCli.RunAsync(TimeSpan.FromSeconds(90),
            "pr", "checks", pr.Number.ToString(CultureInfo.InvariantCulture), "--repo", pr.Repo,
            "--json", "name,state,bucket,link,workflow,event,startedAt,completedAt").ConfigureAwait(false);
        if (code != 0) {
            throw new InvalidOperationException(!string.IsNullOrWhiteSpace(stderr)
                ? stderr.Trim()
                : "Failed to load PR checks.");
        }

        using var doc = JsonDocument.Parse(stdout);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) {
            throw new InvalidOperationException("Unexpected payload from `gh pr checks`.");
        }

        var list = new List<JsonElement>();
        foreach (var item in doc.RootElement.EnumerateArray()) {
            list.Add(item.Clone());
        }
        return list;
    }

    private static CheckSummary SummarizeChecks(IReadOnlyList<JsonElement> checks) {
        var pending = 0;
        var failed = 0;
        var passed = 0;
        foreach (var check in checks) {
            var bucket = ReadString(check, "bucket");
            var state = ReadString(check, "state");
            var isPending = bucket.Equals("pending", StringComparison.OrdinalIgnoreCase) || PendingCheckStates.Contains(state);
            if (isPending) {
                pending++;
            }

            if (bucket.Equals("fail", StringComparison.OrdinalIgnoreCase)) {
                failed++;
            }

            if (bucket.Equals("pass", StringComparison.OrdinalIgnoreCase)) {
                passed++;
            }
        }

        return new CheckSummary(
            PendingCount: pending,
            FailedCount: failed,
            PassedCount: passed,
            AllTerminal: pending == 0
        );
    }

    private static async Task<IReadOnlyList<FailedRun>> GetFailedRunsForHeadShaAsync(PrState pr) {
        if (string.IsNullOrWhiteSpace(pr.HeadSha)) {
            return Array.Empty<FailedRun>();
        }

        var endpoint = $"repos/{pr.Repo}/actions/runs";
        var (code, stdout, stderr) = await GhCli.RunAsync(TimeSpan.FromSeconds(90),
            "api", endpoint, "-X", "GET", "-f", $"head_sha={pr.HeadSha}", "-f", "per_page=100").ConfigureAwait(false);
        if (code != 0) {
            throw new InvalidOperationException(!string.IsNullOrWhiteSpace(stderr)
                ? stderr.Trim()
                : "Failed to load workflow runs for current SHA.");
        }

        using var doc = JsonDocument.Parse(stdout);
        if (doc.RootElement.ValueKind != JsonValueKind.Object ||
            !doc.RootElement.TryGetProperty("workflow_runs", out var runsNode) ||
            runsNode.ValueKind != JsonValueKind.Array) {
            return Array.Empty<FailedRun>();
        }

        var failedRuns = new List<FailedRun>();
        foreach (var run in runsNode.EnumerateArray()) {
            var headSha = ReadString(run, "head_sha");
            if (!headSha.Equals(pr.HeadSha, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var conclusion = ReadString(run, "conclusion");
            if (!FailedRunConclusions.Contains(conclusion)) {
                continue;
            }

            var runId = ReadLongAsString(run, "id");
            if (string.IsNullOrWhiteSpace(runId)) {
                continue;
            }

            failedRuns.Add(new FailedRun(
                RunId: runId,
                WorkflowName: ReadString(run, "name"),
                Status: ReadString(run, "status"),
                Conclusion: conclusion,
                Url: ReadString(run, "html_url")
            ));
        }

        return failedRuns
            .OrderBy(item => item.WorkflowName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.RunId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static async Task<IReadOnlyList<ReviewItem>> GetNewReviewItemsAsync(PrState pr, RunnerState state,
        string authenticatedLogin, IReadOnlySet<string> approvedBots) {
        var seenIssueComments = new HashSet<string>(state.SeenIssueCommentIds ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
        var seenReviewComments = new HashSet<string>(state.SeenReviewCommentIds ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
        var seenReviews = new HashSet<string>(state.SeenReviewIds ?? new List<string>(), StringComparer.OrdinalIgnoreCase);

        var issueComments = await GhApiListPaginatedAsync($"repos/{pr.Repo}/issues/{pr.Number.ToString(CultureInfo.InvariantCulture)}/comments").ConfigureAwait(false);
        var reviewComments = await GhApiListPaginatedAsync($"repos/{pr.Repo}/pulls/{pr.Number.ToString(CultureInfo.InvariantCulture)}/comments").ConfigureAwait(false);
        var reviews = await GhApiListPaginatedAsync($"repos/{pr.Repo}/pulls/{pr.Number.ToString(CultureInfo.InvariantCulture)}/reviews").ConfigureAwait(false);

        var newItems = new List<ReviewItem>();
        AppendIssueComments(newItems, issueComments, seenIssueComments, authenticatedLogin, approvedBots);
        AppendReviewComments(newItems, reviewComments, seenReviewComments, authenticatedLogin, approvedBots);
        AppendReviews(newItems, reviews, seenReviews, authenticatedLogin, approvedBots);

        state.SeenIssueCommentIds = seenIssueComments.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
        state.SeenReviewCommentIds = seenReviewComments.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
        state.SeenReviewIds = seenReviews.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();

        return newItems
            .OrderBy(item => item.CreatedAt, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Kind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void AppendIssueComments(List<ReviewItem> output, IReadOnlyList<JsonElement> items, HashSet<string> seenIds,
        string authenticatedLogin, IReadOnlySet<string> approvedBots) {
        foreach (var item in items) {
            var id = ReadLongAsString(item, "id");
            if (string.IsNullOrWhiteSpace(id) || seenIds.Contains(id)) {
                continue;
            }

            var author = ReadNestedString(item, "user", "login");
            var association = ReadString(item, "author_association");
            var sourceType = DetermineReviewSourceType(author, association, authenticatedLogin, approvedBots);
            seenIds.Add(id);

            output.Add(new ReviewItem(
                Kind: "issue_comment",
                Id: id,
                Author: author,
                AuthorAssociation: association,
                SourceType: sourceType,
                CreatedAt: ReadString(item, "created_at"),
                Body: ReadString(item, "body"),
                Path: null,
                Line: null,
                Url: ReadString(item, "html_url")
            ));
        }
    }

    private static void AppendReviewComments(List<ReviewItem> output, IReadOnlyList<JsonElement> items, HashSet<string> seenIds,
        string authenticatedLogin, IReadOnlySet<string> approvedBots) {
        foreach (var item in items) {
            var id = ReadLongAsString(item, "id");
            if (string.IsNullOrWhiteSpace(id) || seenIds.Contains(id)) {
                continue;
            }

            var author = ReadNestedString(item, "user", "login");
            var association = ReadString(item, "author_association");
            var sourceType = DetermineReviewSourceType(author, association, authenticatedLogin, approvedBots);
            seenIds.Add(id);

            var line = ReadNullableInt(item, "line") ?? ReadNullableInt(item, "original_line");
            output.Add(new ReviewItem(
                Kind: "review_comment",
                Id: id,
                Author: author,
                AuthorAssociation: association,
                SourceType: sourceType,
                CreatedAt: ReadString(item, "created_at"),
                Body: ReadString(item, "body"),
                Path: ReadNullableString(item, "path"),
                Line: line,
                Url: ReadString(item, "html_url")
            ));
        }
    }

    private static void AppendReviews(List<ReviewItem> output, IReadOnlyList<JsonElement> items, HashSet<string> seenIds,
        string authenticatedLogin, IReadOnlySet<string> approvedBots) {
        foreach (var item in items) {
            var id = ReadLongAsString(item, "id");
            if (string.IsNullOrWhiteSpace(id) || seenIds.Contains(id)) {
                continue;
            }

            var author = ReadNestedString(item, "user", "login");
            var association = ReadString(item, "author_association");
            var sourceType = DetermineReviewSourceType(author, association, authenticatedLogin, approvedBots);
            seenIds.Add(id);

            output.Add(new ReviewItem(
                Kind: "review",
                Id: id,
                Author: author,
                AuthorAssociation: association,
                SourceType: sourceType,
                CreatedAt: ReadString(item, "submitted_at"),
                Body: ReadString(item, "body"),
                Path: null,
                Line: null,
                Url: ReadString(item, "html_url")
            ));
        }
    }

    private static async Task<IReadOnlyList<JsonElement>> GhApiListPaginatedAsync(string endpoint) {
        const int pageSize = 100;
        var items = new List<JsonElement>();
        for (var page = 1; page <= 20; page++) {
            var delimiter = endpoint.Contains('?', StringComparison.Ordinal) ? "&" : "?";
            var pageEndpoint = $"{endpoint}{delimiter}per_page={pageSize.ToString(CultureInfo.InvariantCulture)}&page={page.ToString(CultureInfo.InvariantCulture)}";
            var (code, stdout, stderr) = await GhCli.RunAsync(TimeSpan.FromSeconds(90), "api", pageEndpoint).ConfigureAwait(false);
            if (code != 0) {
                throw new InvalidOperationException(!string.IsNullOrWhiteSpace(stderr)
                    ? stderr.Trim()
                    : $"Failed to query GitHub API endpoint: {endpoint}");
            }

            using var doc = JsonDocument.Parse(stdout);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) {
                throw new InvalidOperationException($"Unexpected array payload from GitHub API endpoint: {endpoint}");
            }

            var count = 0;
            foreach (var item in doc.RootElement.EnumerateArray()) {
                items.Add(item.Clone());
                count++;
            }

            if (count < pageSize) {
                break;
            }
        }
        return items;
    }

    private static bool IsReadyToMerge(PrState pr, CheckSummary checks, bool hasActionableReviewItems) {
        if (pr.Closed || pr.Merged) {
            return false;
        }

        if (!checks.AllTerminal || checks.FailedCount > 0 || checks.PendingCount > 0) {
            return false;
        }

        if (hasActionableReviewItems) {
            return false;
        }

        if (!pr.Mergeable.Equals("MERGEABLE", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        if (MergeConflictOrBlockingStates.Contains(pr.MergeStateStatus)) {
            return false;
        }

        if (MergeBlockingReviewDecisions.Contains(pr.ReviewDecision)) {
            return false;
        }

        return true;
    }

    private static Options ParseOptions(string[] args) {
        var options = new Options();
        for (var i = 0; i < args.Length; i++) {
            var arg = args[i];
            switch (arg) {
                case "-h":
                case "--help":
                    options.ShowHelp = true;
                    break;
                case "--repo":
                    if (i + 1 < args.Length) {
                        options.Repo = args[++i];
                    } else {
                        options.ParseFailed = true;
                        options.ShowHelp = true;
                    }
                    break;
                case "--pr":
                    if (i + 1 < args.Length) {
                        options.PrSpec = args[++i];
                    } else {
                        options.ParseFailed = true;
                        options.ShowHelp = true;
                    }
                    break;
                case "--poll-seconds":
                    if (i + 1 < args.Length &&
                        int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var pollSeconds) &&
                        pollSeconds > 0) {
                        options.PollSeconds = Math.Min(MaxPollSeconds, pollSeconds);
                    } else {
                        options.ParseFailed = true;
                        options.ShowHelp = true;
                    }
                    break;
                case "--max-flaky-retries":
                    if (i + 1 < args.Length &&
                        int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxFlakyRetries) &&
                        maxFlakyRetries >= 0) {
                        options.MaxFlakyRetries = Math.Min(10, maxFlakyRetries);
                    } else {
                        options.ParseFailed = true;
                        options.ShowHelp = true;
                    }
                    break;
                case "--state-file":
                    if (i + 1 < args.Length) {
                        options.StateFilePath = args[++i];
                    } else {
                        options.ParseFailed = true;
                        options.ShowHelp = true;
                    }
                    break;
                case "--watch":
                    options.Watch = true;
                    options.Once = false;
                    break;
                case "--once":
                    options.Once = true;
                    options.Watch = false;
                    break;
                case "--approved-bot":
                    if (i + 1 < args.Length) {
                        var value = (args[++i] ?? string.Empty).Trim();
                        if (string.IsNullOrWhiteSpace(value)) {
                            options.ParseFailed = true;
                            options.ShowHelp = true;
                        } else {
                            options.ApprovedBots.Add(value);
                        }
                    } else {
                        options.ParseFailed = true;
                        options.ShowHelp = true;
                    }
                    break;
                default:
                    Console.Error.WriteLine($"Unknown option: {arg}");
                    options.ParseFailed = true;
                    options.ShowHelp = true;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(options.Repo) || !options.Repo.Contains('/', StringComparison.Ordinal)) {
            options.ParseFailed = true;
            options.ShowHelp = true;
        }

        if (string.IsNullOrWhiteSpace(options.PrSpec)) {
            options.ParseFailed = true;
            options.ShowHelp = true;
        }

        if (options.Watch && options.Once) {
            options.ParseFailed = true;
            options.ShowHelp = true;
        }

        return options;
    }

    private static void PrintHelp() {
        Console.WriteLine("Usage:");
        Console.WriteLine("  intelligencex todo pr-watch [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --repo <owner/name>         Repository to watch (default: EvotecIT/IntelligenceX)");
        Console.WriteLine("  --pr <auto|number|url>      Target PR (default: auto from current branch)");
        Console.WriteLine("  --once                      Capture one snapshot and exit (default)");
        Console.WriteLine("  --watch                     Emit continuous snapshots until terminal state");
        Console.WriteLine("  --poll-seconds <n>          Base poll interval in watch mode (default: 60)");
        Console.WriteLine("  --max-flaky-retries <n>     Retry budget for recommendation classification (default: 3)");
        Console.WriteLine("  --state-file <path>         Optional watcher state file path");
        Console.WriteLine("  --approved-bot <login>      Additional approved bot login (repeatable)");
    }

    private static void PrintJson(object value) {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        Console.WriteLine(json);
    }

    private static string ReadString(JsonElement element, string propertyName) {
        if (!element.TryGetProperty(propertyName, out var node)) {
            return string.Empty;
        }
        return node.ValueKind == JsonValueKind.String
            ? (node.GetString() ?? string.Empty)
            : string.Empty;
    }

    private static string ReadLongAsString(JsonElement element, string propertyName) {
        if (!element.TryGetProperty(propertyName, out var node)) {
            return string.Empty;
        }
        if (node.ValueKind == JsonValueKind.Number && node.TryGetInt64(out var value)) {
            return value.ToString(CultureInfo.InvariantCulture);
        }
        if (node.ValueKind == JsonValueKind.String) {
            return node.GetString() ?? string.Empty;
        }
        return string.Empty;
    }

    private static string ReadNestedString(JsonElement element, string propertyName, string nestedPropertyName) {
        if (!element.TryGetProperty(propertyName, out var node) || node.ValueKind != JsonValueKind.Object) {
            return string.Empty;
        }

        return ReadString(node, nestedPropertyName);
    }

    private static int ReadInt(JsonElement element, string propertyName) {
        if (!element.TryGetProperty(propertyName, out var node)) {
            return 0;
        }
        return node.ValueKind == JsonValueKind.Number && node.TryGetInt32(out var value)
            ? value
            : 0;
    }

    private static int? ReadNullableInt(JsonElement element, string propertyName) {
        if (!element.TryGetProperty(propertyName, out var node)) {
            return null;
        }
        if (node.ValueKind == JsonValueKind.Number && node.TryGetInt32(out var value)) {
            return value;
        }
        return null;
    }

    private static string? ReadNullableString(JsonElement element, string propertyName) {
        if (!element.TryGetProperty(propertyName, out var node)) {
            return null;
        }
        return node.ValueKind == JsonValueKind.String
            ? node.GetString()
            : null;
    }
}
