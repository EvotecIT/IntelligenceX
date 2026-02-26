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

            var plannedAudit = BuildPlannedAuditRecords(
                result.Snapshot,
                options.Phase,
                options.Source,
                options.RunLink);
            AppendAuditRecords(options.AuditLogPath, plannedAudit);
            result = result with {
                Snapshot = result.Snapshot with { Audit = plannedAudit }
            };

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
        var retryDedupeKey = BuildRetryActionDedupeKey(pr.Repo, pr.Number, pr.HeadSha, failedRuns.Select(item => item.RunId));
        var allowRetryAction = !ShouldSuppressRetryAction(
            GetLastRetryDedupeKey(state, pr.HeadSha),
            GetLastRetryAt(state, pr.HeadSha),
            retryDedupeKey,
            options.RetryCooldownMinutes,
            DateTimeOffset.UtcNow);
        var actions = RecommendActions(pr, checksSummary, failedRuns, newReviewItems, retryState, out var stopReason, allowRetryAction);

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
            RetryState: retryState,
            Audit: Array.Empty<AuditRecord>()
        );
        return new WatchCollectionResult(snapshot, statePath, state);
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
            value.LastRetryDedupeBySha ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            value.LastRetryAtBySha ??= new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
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

    private static void AppendAuditRecords(string path, IReadOnlyList<AuditRecord> records) {
        if (string.IsNullOrWhiteSpace(path) || records.Count == 0) {
            return;
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) {
            Directory.CreateDirectory(directory);
        }

        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        foreach (var record in records) {
            writer.WriteLine(JsonSerializer.Serialize(record, JsonOptions));
        }
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

    private static string? GetLastRetryDedupeKey(RunnerState state, string headSha) {
        if (string.IsNullOrWhiteSpace(headSha)) {
            return null;
        }

        return state.LastRetryDedupeBySha.TryGetValue(headSha, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
    }

    private static DateTimeOffset? GetLastRetryAt(RunnerState state, string headSha) {
        if (string.IsNullOrWhiteSpace(headSha)) {
            return null;
        }

        return state.LastRetryAtBySha.TryGetValue(headSha, out var value)
            ? value
            : null;
    }

    private static async Task<RetryApplyOutcome> TryApplyRetryActionAsync(WatchCollectionResult result) {
        var retryAction = result.Snapshot.Actions
            .FirstOrDefault(action => action.Name.Equals(ActionRetryFailedChecks, StringComparison.OrdinalIgnoreCase));
        if (retryAction is null) {
            Console.Error.WriteLine("Assist retry requested, but no eligible retry action was planned.");
            return new RetryApplyOutcome(
                Applied: false,
                Result: "skipped",
                Reason: "no_eligible_retry_action",
                DedupeKey: null);
        }

        var runIds = result.Snapshot.FailedRuns
            .Select(item => item.RunId)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (runIds.Count == 0) {
            Console.Error.WriteLine("Assist retry requested, but no failed run IDs are available.");
            return new RetryApplyOutcome(
                Applied: false,
                Result: "skipped",
                Reason: "no_failed_run_ids",
                DedupeKey: retryAction.DedupeKey);
        }

        var failedReruns = new List<string>();
        var rerunSucceeded = false;
        foreach (var runId in runIds) {
            var (code, _, stderr) = await GhCli.RunAsync(
                TimeSpan.FromSeconds(90),
                "run",
                "rerun",
                runId,
                "--repo",
                result.Snapshot.Pr.Repo,
                "--failed").ConfigureAwait(false);
            if (code == 0) {
                rerunSucceeded = true;
                continue;
            }

            var failure = string.IsNullOrWhiteSpace(stderr)
                ? $"run:{runId}"
                : $"run:{runId}:{stderr.Trim()}";
            failedReruns.Add(failure);
        }

        if (!rerunSucceeded) {
            return new RetryApplyOutcome(
                Applied: false,
                Result: "failed",
                Reason: "rerun_all_failed",
                DedupeKey: retryAction.DedupeKey,
                ErrorMessage: "Retry rerun failed for all targeted workflow runs.");
        }

        var headSha = result.Snapshot.Pr.HeadSha;
        var currentCount = GetRetriesUsed(result.State, headSha);
        result.State.RetriesBySha[headSha] = currentCount + 1;
        if (!string.IsNullOrWhiteSpace(retryAction.DedupeKey)) {
            result.State.LastRetryDedupeBySha[headSha] = retryAction.DedupeKey!;
        }
        result.State.LastRetryAtBySha[headSha] = DateTimeOffset.UtcNow;
        result.State.UpdatedAtUtc = DateTimeOffset.UtcNow;
        SaveState(result.StateFilePath, result.State);

        Console.Error.WriteLine(
            $"Applied retry_failed_checks for PR #{result.Snapshot.Pr.Number.ToString(CultureInfo.InvariantCulture)} on SHA {headSha} (runs={runIds.Count.ToString(CultureInfo.InvariantCulture)}).");

        if (failedReruns.Count > 0) {
            return new RetryApplyOutcome(
                Applied: false,
                Result: "failed",
                Reason: "rerun_partial_failure",
                DedupeKey: retryAction.DedupeKey,
                ErrorMessage:
                $"Retry rerun partially failed for PR #{result.Snapshot.Pr.Number.ToString(CultureInfo.InvariantCulture)}: {string.Join("; ", failedReruns)}");
        }

        return new RetryApplyOutcome(
            Applied: true,
            Result: "success",
            Reason: "rerun_applied",
            DedupeKey: retryAction.DedupeKey);
    }

    private static async Task<string> GetAuthenticatedLoginAsync() {
        var (code, stdout, _) = await GhCli.RunAsync("api", "user").ConfigureAwait(false);
        if (code == 0 && !string.IsNullOrWhiteSpace(stdout)) {
            try {
                using var doc = JsonDocument.Parse(stdout);
                var login = ReadString(doc.RootElement, "login");
                if (!string.IsNullOrWhiteSpace(login)) {
                    return login;
                }
            } catch {
                // Fall back to actor-derived login below.
            }
        }
        return ResolveAuthenticatedLoginFallback();
    }

    internal static string ResolveAuthenticatedLoginFallback() {
        var actor = Environment.GetEnvironmentVariable("GITHUB_ACTOR");
        if (string.IsNullOrWhiteSpace(actor)) {
            actor = Environment.GetEnvironmentVariable("GITHUB_TRIGGERING_ACTOR");
        }
        return actor?.Trim() ?? string.Empty;
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

}
