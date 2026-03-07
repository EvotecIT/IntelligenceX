using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Cli.GitHub;

namespace IntelligenceX.Cli.Todo;

internal static class PrWatchConsolidationRunner {
    private const string DefaultRepo = "EvotecIT/IntelligenceX";
    private const string DefaultSource = "manual_cli";
    private const string ConfirmSchema = "intelligencex.pr-watch.snapshot.v1";
    private static readonly SemaphoreSlim ConsoleCaptureGate = new(1, 1);
    private static readonly JsonSerializerOptions CompactJson = new() { WriteIndented = false };

    private sealed class Options {
        public string Repo { get; set; } = DefaultRepo;
        public int MaxPrs { get; set; } = 200;
        public int MaxFlakyRetries { get; set; } = 3;
        public int StaleDays { get; set; } = 7;
        public bool IncludeDrafts { get; set; }
        public string Source { get; set; } = ResolveDefaultSource();
        public string? RunLink { get; set; } = ResolveDefaultRunLink();
        public string SnapshotDir { get; set; } = Path.Combine("artifacts", "pr-watch", "nightly");
        public string RollupPath { get; set; } = Path.Combine("artifacts", "pr-watch", "ix-pr-watch-nightly-rollup.json");
        public string SummaryPath { get; set; } = Path.Combine("artifacts", "pr-watch", "ix-pr-watch-nightly-summary.md");
        public string MetricsPath { get; set; } = Path.Combine("artifacts", "pr-watch", "ix-pr-watch-nightly-metrics.json");
        public string MetricsHistoryPath { get; set; } = Path.Combine("artifacts", "pr-watch", "ix-pr-watch-nightly-metrics-history.json");
        public string TrackerPath { get; set; } = Path.Combine("artifacts", "pr-watch", "ix-pr-watch-nightly-tracker.md");
        public bool PublishTrackingIssue { get; set; } = true;
        public string TrackerIssueTitle { get; set; } = string.Empty;
        public HashSet<string> TrackerIssueLabels { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> ApprovedBots { get; } = new(StringComparer.OrdinalIgnoreCase) {
            "intelligencex-review",
            "intelligencex-review[bot]",
            "chatgpt-codex-connector[bot]"
        };
    }

    private sealed record PrMeta(int Number, DateTimeOffset? UpdatedAtUtc);

    private sealed record RowData(
        int Number,
        string Url,
        string HeadSha,
        string State,
        string ReviewDecision,
        string MergeStateStatus,
        string StopReason,
        int PassedCount,
        int FailedCount,
        int PendingCount,
        int? DaysSinceUpdate,
        List<string> Actions
    );

    public static async Task<int> RunAsync(string[] args) {
        try {
            var options = ParseOptions(args);
            if (options is null) {
                PrintHelp();
                return 0;
            }

            Directory.CreateDirectory(options.SnapshotDir);
            foreach (var existing in Directory.EnumerateFiles(options.SnapshotDir)) {
                File.Delete(existing);
            }

            var metas = await ListOpenPrsAsync(options).ConfigureAwait(false);
            var rows = new List<RowData>();
            var failedTargets = new List<int>();

            foreach (var meta in metas) {
                try {
                    var snapshot = await RunPrWatchSnapshotAsync(
                        repo: options.Repo,
                        prSpec: meta.Number.ToString(CultureInfo.InvariantCulture),
                        maxFlakyRetries: options.MaxFlakyRetries,
                        phase: "observe",
                        source: options.Source,
                        runLink: options.RunLink,
                        approvedBots: options.ApprovedBots).ConfigureAwait(false);
                    var row = BuildRow(snapshot, meta.UpdatedAtUtc);
                    rows.Add(row);
                    WriteJson(Path.Combine(options.SnapshotDir, $"pr-{row.Number.ToString(CultureInfo.InvariantCulture)}.json"), snapshot);
                } catch {
                    failedTargets.Add(meta.Number);
                }
            }

            var rollup = BuildRollup(options, metas.Count, rows, failedTargets);
            WriteJson(options.RollupPath, rollup);

            var previousMetrics = LoadPreviousMetrics(options.MetricsHistoryPath);
            var metrics = BuildMetrics(options, rollup, rows, previousMetrics);
            WriteJson(options.MetricsPath, metrics);
            UpdateMetricsHistory(options.MetricsHistoryPath, metrics);

            WriteText(options.TrackerPath, BuildTrackerBody(options, rollup, metrics));
            var trackerUrl = options.PublishTrackingIssue
                ? await SyncTrackerIssueAsync(options, rollup, metrics).ConfigureAwait(false)
                : string.Empty;

            var summary = BuildSummary(options, rollup, metrics, trackerUrl);
            WriteText(options.SummaryPath, summary);
            AppendStepSummaryIfPresent(options.SummaryPath);
            return 0;
        } catch (Exception ex) {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static Options? ParseOptions(string[] args) {
        if (args.Length > 0 && IsHelp(args[0])) {
            return null;
        }

        var options = new Options();
        for (var i = 0; i < args.Length; i++) {
            var arg = args[i];
            switch (arg) {
                case "--repo": options.Repo = Next(args, ref i, arg); break;
                case "--max-prs": options.MaxPrs = ParseInt(Next(args, ref i, arg), options.MaxPrs, arg, 1, 2000); break;
                case "--max-flaky-retries": options.MaxFlakyRetries = ParseInt(Next(args, ref i, arg), options.MaxFlakyRetries, arg, 1, 50); break;
                case "--stale-days": options.StaleDays = ParseInt(Next(args, ref i, arg), options.StaleDays, arg, 1, 3650); break;
                case "--include-drafts": options.IncludeDrafts = ParseBool(Next(args, ref i, arg), options.IncludeDrafts, arg); break;
                case "--approved-bot": options.ApprovedBots.Add(Next(args, ref i, arg)); break;
                case "--approved-bots": AddCsv(options.ApprovedBots, Next(args, ref i, arg)); break;
                case "--source": options.Source = Next(args, ref i, arg); break;
                case "--run-link": options.RunLink = Next(args, ref i, arg); break;
                case "--snapshot-dir": options.SnapshotDir = Next(args, ref i, arg); break;
                case "--rollup-path": options.RollupPath = Next(args, ref i, arg); break;
                case "--summary-path": options.SummaryPath = Next(args, ref i, arg); break;
                case "--metrics-path": options.MetricsPath = Next(args, ref i, arg); break;
                case "--metrics-history-path": options.MetricsHistoryPath = Next(args, ref i, arg); break;
                case "--tracker-path": options.TrackerPath = Next(args, ref i, arg); break;
                case "--publish-tracking-issue": options.PublishTrackingIssue = ParseBool(Next(args, ref i, arg), options.PublishTrackingIssue, arg); break;
                case "--tracker-issue-title": options.TrackerIssueTitle = Next(args, ref i, arg); break;
                case "--tracker-issue-label": options.TrackerIssueLabels.Add(Next(args, ref i, arg)); break;
                case "--tracker-issue-labels": AddCsv(options.TrackerIssueLabels, Next(args, ref i, arg)); break;
                default: throw new InvalidOperationException($"Unknown option: {arg}");
            }
        }

        options.Source = ResolveSourceWithDefault(options.Source, Environment.GetEnvironmentVariable("GITHUB_EVENT_NAME"));
        return options;
    }

    private static async Task<List<PrMeta>> ListOpenPrsAsync(Options options) {
        var (exitCode, stdOut, stdErr) = await GhCli.RunAsync(
            "pr", "list",
            "--repo", options.Repo,
            "--state", "open",
            "--limit", options.MaxPrs.ToString(CultureInfo.InvariantCulture),
            "--json", "number,updatedAt,isDraft").ConfigureAwait(false);
        if (exitCode != 0) {
            throw new InvalidOperationException($"gh pr list failed: {stdErr.Trim()}");
        }

        var result = new List<PrMeta>();
        var root = JsonNode.Parse(stdOut) as JsonArray ?? new JsonArray();
        foreach (var node in root) {
            if (node is not JsonObject item) {
                continue;
            }
            var number = ReadInt(item, "number");
            var isDraft = ReadBool(item, "isDraft");
            if (!options.IncludeDrafts && isDraft) {
                continue;
            }
            var updatedAt = ReadDate(item, "updatedAt");
            if (number > 0) {
                result.Add(new PrMeta(number, updatedAt));
            }
        }
        return result.OrderBy(static m => m.Number).ToList();
    }

    internal static async Task<JsonObject> RunPrWatchSnapshotAsync(
        string repo,
        string prSpec,
        int maxFlakyRetries,
        string phase,
        string source,
        string? runLink,
        IEnumerable<string> approvedBots,
        bool applyRetry = false,
        int retryCooldownMinutes = 15,
        string confirmApplyRetry = "") {
        var args = new List<string> {
            "--repo", repo,
            "--pr", prSpec,
            "--max-flaky-retries", maxFlakyRetries.ToString(CultureInfo.InvariantCulture),
            "--phase", phase,
            "--source", source,
            "--once"
        };
        if (!string.IsNullOrWhiteSpace(runLink)) {
            args.Add("--run-link");
            args.Add(runLink!);
        }
        foreach (var bot in approvedBots) {
            args.Add("--approved-bot");
            args.Add(bot);
        }
        if (applyRetry) {
            args.Add("--retry-cooldown-minutes");
            args.Add(retryCooldownMinutes.ToString(CultureInfo.InvariantCulture));
            args.Add("--apply-retry");
            args.Add("--confirm-apply-retry");
            args.Add(confirmApplyRetry);
        }

        await ConsoleCaptureGate.WaitAsync().ConfigureAwait(false);
        var originalOut = Console.Out;
        var originalErr = Console.Error;
        var outWriter = new StringWriter(CultureInfo.InvariantCulture);
        var errWriter = new StringWriter(CultureInfo.InvariantCulture);
        try {
            Console.SetOut(outWriter);
            Console.SetError(errWriter);
            var code = await PrWatchRunner.RunAsync(args.ToArray()).ConfigureAwait(false);
            if (code != 0) {
                var err = string.IsNullOrWhiteSpace(errWriter.ToString()) ? outWriter.ToString() : errWriter.ToString();
                throw new InvalidOperationException($"pr-watch failed for {prSpec}: {err.Trim()}");
            }
        } finally {
            Console.SetOut(originalOut);
            Console.SetError(originalErr);
            ConsoleCaptureGate.Release();
        }

        var line = outWriter.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault(static value => value.StartsWith("{", StringComparison.Ordinal));
        if (string.IsNullOrWhiteSpace(line)) {
            throw new InvalidOperationException($"Missing JSON snapshot for PR {prSpec}.");
        }

        var snapshot = JsonNode.Parse(line) as JsonObject;
        if (snapshot is null || !string.Equals(ReadString(snapshot, "schema"), ConfirmSchema, StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException($"Invalid snapshot payload for PR {prSpec}.");
        }
        return snapshot;
    }

    private static RowData BuildRow(JsonObject snapshot, DateTimeOffset? updatedAt) {
        var pr = snapshot["pr"] as JsonObject ?? new JsonObject();
        var checks = snapshot["checks"] as JsonObject ?? new JsonObject();
        var actions = (snapshot["actions"] as JsonArray ?? new JsonArray())
            .OfType<JsonObject>()
            .Select(static action => ReadString(action, "name"))
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .ToList();
        int? daysSinceUpdate = null;
        if (updatedAt.HasValue) {
            daysSinceUpdate = (int)Math.Floor((DateTimeOffset.UtcNow - updatedAt.Value).TotalDays);
        }

        return new RowData(
            Number: ReadInt(pr, "number"),
            Url: ReadString(pr, "url"),
            HeadSha: ReadString(pr, "headSha"),
            State: ReadString(pr, "state"),
            ReviewDecision: ReadString(pr, "reviewDecision"),
            MergeStateStatus: ReadString(pr, "mergeStateStatus"),
            StopReason: ReadString(snapshot, "stopReason"),
            PassedCount: ReadInt(checks, "passedCount"),
            FailedCount: ReadInt(checks, "failedCount"),
            PendingCount: ReadInt(checks, "pendingCount"),
            DaysSinceUpdate: daysSinceUpdate,
            Actions: actions);
    }

    private static JsonObject BuildRollup(Options options, int totalTargets, IReadOnlyList<RowData> rows, IReadOnlyList<int> failedTargets) {
        var staleInfra = rows.Where(row => IsOpen(row) && (row.DaysSinceUpdate ?? -1) >= options.StaleDays && row.PendingCount > 0 && row.Actions.Contains("idle_wait", StringComparer.OrdinalIgnoreCase)).ToList();
        var reviewRequired = rows.Where(row => IsOpen(row) && IsReviewBlocking(row.ReviewDecision)).ToList();
        var retryBudgetExhausted = rows.Where(static row => string.Equals(row.StopReason, "retry_budget_exhausted", StringComparison.OrdinalIgnoreCase)).ToList();
        var noProgress = rows.Where(row => IsOpen(row) && (row.DaysSinceUpdate ?? -1) >= options.StaleDays && (row.Actions.Contains("idle_wait", StringComparer.OrdinalIgnoreCase) || string.Equals(row.StopReason, "retry_budget_exhausted", StringComparison.OrdinalIgnoreCase) || IsReviewBlocking(row.ReviewDecision))).ToList();

        var noProgressBuckets = noProgress
            .GroupBy(row => $"{AgeClass(row.DaysSinceUpdate)}::{ProgressClass(row)}", StringComparer.OrdinalIgnoreCase)
            .OrderBy(static group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => (JsonNode)new JsonObject {
                ["ageClass"] = group.Key.Split("::")[0],
                ["progressClass"] = group.Key.Split("::")[1],
                ["count"] = group.Count(),
                ["prs"] = new JsonArray(group.Select(static row => (JsonNode)row.Number).OrderBy(static n => n).ToArray())
            }).ToArray();

        return new JsonObject {
            ["schema"] = "intelligencex.pr-watch.nightly.v1",
            ["repo"] = options.Repo,
            ["generatedAtUtc"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            ["staleDaysThreshold"] = options.StaleDays,
            ["totalTargets"] = totalTargets,
            ["totalSnapshots"] = rows.Count,
            ["failedTargets"] = new JsonArray(failedTargets.OrderBy(static n => n).Select(static n => (JsonNode)n).ToArray()),
            ["staleInfraBlocked"] = BuildPrArray(staleInfra),
            ["reviewRequired"] = BuildPrArray(reviewRequired),
            ["retryBudgetExhausted"] = BuildPrArray(retryBudgetExhausted),
            ["noProgressByAgeClass"] = new JsonArray(noProgressBuckets),
            ["prs"] = BuildPrArray(rows)
        };
    }

    private static JsonArray BuildPrArray(IEnumerable<RowData> rows) {
        var nodes = rows
            .OrderBy(static row => row.Number)
            .Select(static row => (JsonNode)new JsonObject {
                ["number"] = row.Number,
                ["url"] = row.Url,
                ["headSha"] = row.HeadSha,
                ["daysSinceUpdate"] = row.DaysSinceUpdate,
                ["stopReason"] = row.StopReason,
                ["reviewDecision"] = row.ReviewDecision,
                ["mergeStateStatus"] = row.MergeStateStatus,
                ["actions"] = new JsonArray(row.Actions.Select(static action => (JsonNode)action).ToArray()),
                ["checks"] = new JsonObject {
                    ["passedCount"] = row.PassedCount,
                    ["failedCount"] = row.FailedCount,
                    ["pendingCount"] = row.PendingCount
                }
            }).ToArray();
        return new JsonArray(nodes);
    }

    private static bool IsOpen(RowData row) => string.Equals(row.State, "OPEN", StringComparison.OrdinalIgnoreCase);
    private static bool IsReviewBlocking(string value) => string.Equals(value, "REVIEW_REQUIRED", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "CHANGES_REQUESTED", StringComparison.OrdinalIgnoreCase);
    private static string AgeClass(int? days) => !days.HasValue ? "unknown" : days >= 30 ? "30d_plus" : days >= 14 ? "14_29d" : days >= 7 ? "7_13d" : "under_7d";
    private static string ProgressClass(RowData row) => string.Equals(row.StopReason, "retry_budget_exhausted", StringComparison.OrdinalIgnoreCase) ? "retry_budget_exhausted" : IsReviewBlocking(row.ReviewDecision) ? "review_required" : row.Actions.Contains("idle_wait", StringComparer.OrdinalIgnoreCase) ? "idle_wait" : row.Actions.Contains("diagnose_ci_failure", StringComparer.OrdinalIgnoreCase) ? "ci_failure" : "other";

    private static JsonObject BuildMetrics(Options options, JsonObject rollup, IReadOnlyList<RowData> rows, JsonObject? previousMetrics) {
        var totalSnapshots = rows.Count;
        var staleOpenPrs = rows.Count(row => (row.DaysSinceUpdate ?? -1) >= options.StaleDays);
        var reviewRequiredPrs = rows.Count(row => IsReviewBlocking(row.ReviewDecision) && IsOpen(row));
        var retryBudgetExhaustedPrs = rows.Count(row => string.Equals(row.StopReason, "retry_budget_exhausted", StringComparison.OrdinalIgnoreCase));
        var noProgressSet = new HashSet<int>(
            rows.Where(row => IsOpen(row) && (row.DaysSinceUpdate ?? -1) >= options.StaleDays && (row.Actions.Contains("idle_wait", StringComparer.OrdinalIgnoreCase) || IsReviewBlocking(row.ReviewDecision) || string.Equals(row.StopReason, "retry_budget_exhausted", StringComparison.OrdinalIgnoreCase)))
                .Select(static row => row.Number));
        var noProgressPrs = noProgressSet.Count;

        var allDays = rows.Where(static row => row.DaysSinceUpdate.HasValue).Select(static row => row.DaysSinceUpdate!.Value).OrderBy(static v => v).ToList();
        var noProgressDays = rows.Where(row => noProgressSet.Contains(row.Number) && row.DaysSinceUpdate.HasValue).Select(static row => row.DaysSinceUpdate!.Value).OrderBy(static v => v).ToList();

        var staleRatio = Ratio(staleOpenPrs, totalSnapshots);
        var reviewRatio = Ratio(reviewRequiredPrs, totalSnapshots);
        var retryRatio = Ratio(retryBudgetExhaustedPrs, totalSnapshots);
        var noProgressRatio = Ratio(noProgressPrs, totalSnapshots);

        var previousTotals = previousMetrics?["totals"] as JsonObject;
        var previousRatios = previousMetrics?["ratiosPct"] as JsonObject;
        var previousStale = ReadIntNullable(previousTotals, "staleOpenPrs");
        var previousReview = ReadIntNullable(previousTotals, "reviewRequiredPrs");
        var previousNoProgress = ReadIntNullable(previousTotals, "noProgressPrs");
        var previousStaleRatio = ReadDoubleNullable(previousRatios, "staleOpenPrs");

        return new JsonObject {
            ["schema"] = "intelligencex.pr-watch.metrics.v1",
            ["repo"] = options.Repo,
            ["generatedAtUtc"] = ReadString(rollup, "generatedAtUtc"),
            ["source"] = options.Source,
            ["runLink"] = options.RunLink,
            ["staleDaysThreshold"] = options.StaleDays,
            ["totals"] = new JsonObject {
                ["targetsScanned"] = ReadInt(rollup, "totalTargets"),
                ["snapshotsCaptured"] = totalSnapshots,
                ["failedTargets"] = (rollup["failedTargets"] as JsonArray)?.Count ?? 0,
                ["staleOpenPrs"] = staleOpenPrs,
                ["reviewRequiredPrs"] = reviewRequiredPrs,
                ["retryBudgetExhaustedPrs"] = retryBudgetExhaustedPrs,
                ["noProgressPrs"] = noProgressPrs
            },
            ["ratiosPct"] = new JsonObject {
                ["staleOpenPrs"] = staleRatio,
                ["reviewRequiredPrs"] = reviewRatio,
                ["retryBudgetExhaustedPrs"] = retryRatio,
                ["noProgressPrs"] = noProgressRatio
            },
            ["mediansDays"] = new JsonObject {
                ["medianAllDaysSinceUpdate"] = Median(allDays),
                ["medianNoProgressDaysSinceUpdate"] = Median(noProgressDays)
            },
            ["deltas"] = new JsonObject {
                ["staleOpenPrs"] = previousStale.HasValue ? staleOpenPrs - previousStale.Value : null,
                ["reviewRequiredPrs"] = previousReview.HasValue ? reviewRequiredPrs - previousReview.Value : null,
                ["noProgressPrs"] = previousNoProgress.HasValue ? noProgressPrs - previousNoProgress.Value : null,
                ["staleOpenPrsPctPoints"] = previousStaleRatio.HasValue ? Math.Round(staleRatio - previousStaleRatio.Value, 2, MidpointRounding.AwayFromZero) : null
            },
            ["successMetrics"] = new JsonObject {
                ["medianTimeToUnblockProxyDays"] = Median(noProgressDays),
                ["stalePrReductionSincePrevious"] = previousStale.HasValue ? previousStale.Value - staleOpenPrs : null
            }
        };
    }

    private static JsonObject? LoadPreviousMetrics(string historyPath) {
        if (!File.Exists(historyPath)) {
            return null;
        }

        try {
            var history = JsonNode.Parse(File.ReadAllText(historyPath)) as JsonArray;
            return history?.LastOrDefault() as JsonObject;
        } catch {
            return null;
        }
    }

    private static void UpdateMetricsHistory(string historyPath, JsonObject metrics) {
        JsonArray history;
        if (File.Exists(historyPath)) {
            try {
                history = JsonNode.Parse(File.ReadAllText(historyPath)) as JsonArray ?? new JsonArray();
            } catch {
                history = new JsonArray();
            }
        } else {
            history = new JsonArray();
        }

        history.Add(JsonNode.Parse(metrics.ToJsonString(CompactJson)));
        while (history.Count > 120) {
            history.RemoveAt(0);
        }
        WriteJson(historyPath, history);
    }

    private static string BuildTrackerBody(Options options, JsonObject rollup, JsonObject metrics) {
        var builder = new StringBuilder();
        builder.AppendLine(TrackerMarker(options.Source));
        builder.AppendLine("# IX PR Babysit Rollup Tracker");
        builder.AppendLine();
        builder.AppendLine($"- Generated: {DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)}");
        builder.AppendLine($"- Repo: `{options.Repo}`");
        builder.AppendLine($"- Source: `{options.Source}`");
        if (!string.IsNullOrWhiteSpace(options.RunLink)) {
            builder.AppendLine($"- Workflow run: {options.RunLink}");
        }
        builder.AppendLine();
        builder.AppendLine("## Metrics");
        builder.AppendLine($"- Median time-to-unblock proxy (days): {FormatNullable(metrics["successMetrics"] as JsonObject, "medianTimeToUnblockProxyDays")}");
        builder.AppendLine($"- Stale open PR ratio: {ReadNumberText(metrics["ratiosPct"] as JsonObject, "staleOpenPrs")}%");
        builder.AppendLine($"- Review-required ratio: {ReadNumberText(metrics["ratiosPct"] as JsonObject, "reviewRequiredPrs")}%");
        builder.AppendLine($"- No-progress ratio: {ReadNumberText(metrics["ratiosPct"] as JsonObject, "noProgressPrs")}%");
        builder.AppendLine();
        builder.AppendLine("## Buckets");
        builder.AppendLine($"- Stale infra blockers: {(rollup["staleInfraBlocked"] as JsonArray)?.Count ?? 0}");
        builder.AppendLine($"- Stuck review-required: {(rollup["reviewRequired"] as JsonArray)?.Count ?? 0}");
        builder.AppendLine($"- Retry budget exhausted: {(rollup["retryBudgetExhausted"] as JsonArray)?.Count ?? 0}");
        return builder.ToString();
    }

    private static string BuildSummary(Options options, JsonObject rollup, JsonObject metrics, string trackerIssueUrl) {
        var builder = new StringBuilder();
        builder.AppendLine("# IX PR Babysit Nightly Consolidation");
        builder.AppendLine();
        builder.AppendLine($"- Generated: {DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)}");
        builder.AppendLine($"- Repo: `{options.Repo}`");
        if (!string.IsNullOrWhiteSpace(options.RunLink)) {
            builder.AppendLine($"- Workflow run: {options.RunLink}");
        }
        builder.AppendLine();
        builder.AppendLine("## Totals");
        builder.AppendLine($"- Targets scanned: {ReadInt(rollup, "totalTargets")}");
        builder.AppendLine($"- Snapshots captured: {ReadInt(rollup, "totalSnapshots")}");
        builder.AppendLine($"- Failed targets: {(rollup["failedTargets"] as JsonArray)?.Count ?? 0}");
        builder.AppendLine();
        builder.AppendLine("## Buckets");
        builder.AppendLine($"- Stale infra blockers: {(rollup["staleInfraBlocked"] as JsonArray)?.Count ?? 0}");
        builder.AppendLine($"- Stuck review-required: {(rollup["reviewRequired"] as JsonArray)?.Count ?? 0}");
        builder.AppendLine($"- Retry budget exhausted: {(rollup["retryBudgetExhausted"] as JsonArray)?.Count ?? 0}");
        builder.AppendLine();
        builder.AppendLine("## Success metrics");
        builder.AppendLine($"- Median time-to-unblock proxy (days): {FormatNullable(metrics["successMetrics"] as JsonObject, "medianTimeToUnblockProxyDays")}");
        builder.AppendLine($"- Stale open PR ratio: {ReadNumberText(metrics["ratiosPct"] as JsonObject, "staleOpenPrs")}%");
        builder.AppendLine($"- Review-required ratio: {ReadNumberText(metrics["ratiosPct"] as JsonObject, "reviewRequiredPrs")}%");
        builder.AppendLine($"- No-progress ratio: {ReadNumberText(metrics["ratiosPct"] as JsonObject, "noProgressPrs")}%");
        if (!string.IsNullOrWhiteSpace(trackerIssueUrl)) {
            builder.AppendLine();
            builder.AppendLine("## Tracker issue");
            builder.AppendLine($"- {trackerIssueUrl}");
        }
        return builder.ToString();
    }

    private static async Task<string> SyncTrackerIssueAsync(Options options, JsonObject rollup, JsonObject metrics) {
        var actionable = HasActionableTrackerContent(rollup, metrics);
        var existing = await FindOpenTrackerIssueAsync(options).ConfigureAwait(false);
        if (!actionable) {
            if (existing is not null) {
                await CloseTrackerIssueAsync(options, existing).ConfigureAwait(false);
            }
            return string.Empty;
        }

        return await UpsertTrackerIssueAsync(options, existing).ConfigureAwait(false);
    }

    private static async Task<JsonObject?> FindOpenTrackerIssueAsync(Options options) {
        var marker = TrackerMarker(options.Source);
        var (listCode, listOut, listErr) = await GhCli.RunAsync(
            "issue", "list",
            "--repo", options.Repo,
            "--state", "open",
            "--limit", "200",
            "--json", "number,url,body").ConfigureAwait(false);
        if (listCode != 0) {
            throw new InvalidOperationException($"gh issue list failed: {listErr.Trim()}");
        }

        var issues = JsonNode.Parse(listOut) as JsonArray ?? new JsonArray();
        return issues
            .OfType<JsonObject>()
            .FirstOrDefault(issue => ReadString(issue, "body").Contains(marker, StringComparison.Ordinal));
    }

    private static async Task<string> UpsertTrackerIssueAsync(Options options, JsonObject? existing) {
        var title = string.IsNullOrWhiteSpace(options.TrackerIssueTitle)
            ? $"IX PR Babysit Rollup Tracker ({options.Source})"
            : options.TrackerIssueTitle;

        string issueUrl;
        int issueNumber;
        if (existing is null) {
            var createArgs = new List<string> {
                "issue", "create",
                "--repo", options.Repo,
                "--title", title,
                "--body-file", options.TrackerPath
            };
            var (createCode, createOut, createErr) = await GhCli.RunAsync(createArgs.ToArray()).ConfigureAwait(false);
            if (createCode != 0) {
                throw new InvalidOperationException($"gh issue create failed: {createErr.Trim()}");
            }
            issueUrl = createOut.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? string.Empty;
            issueNumber = ParseIssueNumber(issueUrl);
        } else {
            issueNumber = ReadInt(existing, "number");
            issueUrl = ReadString(existing, "url");
            var (editCode, _, editErr) = await GhCli.RunAsync(
                "issue", "edit", issueNumber.ToString(CultureInfo.InvariantCulture),
                "--repo", options.Repo,
                "--title", title,
                "--body-file", options.TrackerPath).ConfigureAwait(false);
            if (editCode != 0) {
                throw new InvalidOperationException($"gh issue edit failed: {editErr.Trim()}");
            }
        }

        foreach (var label in options.TrackerIssueLabels) {
            var (labelCode, _, labelErr) = await GhCli.RunAsync(
                "issue", "edit", issueNumber.ToString(CultureInfo.InvariantCulture),
                "--repo", options.Repo,
                "--add-label", label).ConfigureAwait(false);
            if (labelCode != 0) {
                Console.Error.WriteLine($"Warning: failed to add tracker label '{label}': {labelErr.Trim()}");
            }
        }

        return issueUrl;
    }

    private static async Task CloseTrackerIssueAsync(Options options, JsonObject existing) {
        var issueNumber = ReadInt(existing, "number");
        var title = ReadString(existing, "title");
        var comment = string.IsNullOrWhiteSpace(title)
            ? "Closing automatically because the latest PR babysit rollup is clean and no longer needs a tracking issue."
            : $"Closing automatically because the latest PR babysit rollup for '{title}' is clean and no longer needs a tracking issue.";
        var (closeCode, _, closeErr) = await GhCli.RunAsync(
            "issue", "close", issueNumber.ToString(CultureInfo.InvariantCulture),
            "--repo", options.Repo,
            "--reason", "completed",
            "--comment", comment).ConfigureAwait(false);
        if (closeCode != 0) {
            throw new InvalidOperationException($"gh issue close failed: {closeErr.Trim()}");
        }
    }

    internal static bool HasActionableTrackerContent(JsonObject rollup, JsonObject metrics) {
        if ((rollup["failedTargets"] as JsonArray)?.Count > 0) {
            return true;
        }

        if (((rollup["staleInfraBlocked"] as JsonArray)?.Count ?? 0) > 0 ||
            ((rollup["reviewRequired"] as JsonArray)?.Count ?? 0) > 0 ||
            ((rollup["retryBudgetExhausted"] as JsonArray)?.Count ?? 0) > 0) {
            return true;
        }

        var ratios = metrics["ratiosPct"] as JsonObject;
        return ReadDoubleNullable(ratios, "staleOpenPrs").GetValueOrDefault() > 0 ||
               ReadDoubleNullable(ratios, "reviewRequiredPrs").GetValueOrDefault() > 0 ||
               ReadDoubleNullable(ratios, "retryBudgetExhaustedPrs").GetValueOrDefault() > 0 ||
               ReadDoubleNullable(ratios, "noProgressPrs").GetValueOrDefault() > 0;
    }

    private static string TrackerMarker(string source) => $"<!-- intelligencex:pr-watch-rollup-tracker:{SanitizeSource(source)} -->";
    private static string SanitizeSource(string source) => string.IsNullOrWhiteSpace(source) ? "default" : new string(source.Select(static c => char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.' ? c : '-').ToArray()).Trim('-');

    private static string Next(string[] args, ref int index, string argName) {
        if (index + 1 >= args.Length) {
            throw new InvalidOperationException($"{argName} requires a value.");
        }
        return args[++index];
    }

    private static int ParseInt(string raw, int current, string optionName, int min, int max) {
        if (string.IsNullOrWhiteSpace(raw)) {
            return current;
        }
        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < min || value > max) {
            throw new InvalidOperationException($"{optionName} must be between {min} and {max}.");
        }
        return value;
    }

    private static bool ParseBool(string raw, bool current, string optionName) {
        if (string.IsNullOrWhiteSpace(raw)) {
            return current;
        }
        if (bool.TryParse(raw, out var value)) {
            return value;
        }
        throw new InvalidOperationException($"{optionName} must be true/false.");
    }

    private static void AddCsv(HashSet<string> values, string csv) {
        foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
            values.Add(part);
        }
    }

    private static bool IsHelp(string value) => value.Equals("-h", StringComparison.OrdinalIgnoreCase) || value.Equals("--help", StringComparison.OrdinalIgnoreCase) || value.Equals("help", StringComparison.OrdinalIgnoreCase);
    internal static string ResolveSourceWithDefault(string? source, string? eventName) {
        if (!string.IsNullOrWhiteSpace(source)) {
            return source.Trim();
        }

        if (!string.IsNullOrWhiteSpace(eventName)) {
            return eventName.Trim();
        }

        return DefaultSource;
    }

    private static string ResolveDefaultSource() => ResolveSourceWithDefault(null, Environment.GetEnvironmentVariable("GITHUB_EVENT_NAME"));
    private static string? ResolveDefaultRunLink() {
        var server = Environment.GetEnvironmentVariable("GITHUB_SERVER_URL");
        var repo = Environment.GetEnvironmentVariable("GITHUB_REPOSITORY");
        var runId = Environment.GetEnvironmentVariable("GITHUB_RUN_ID");
        return string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(repo) || string.IsNullOrWhiteSpace(runId) ? null : $"{server.TrimEnd('/')}/{repo}/actions/runs/{runId}";
    }

    private static void WriteJson(string path, JsonNode node) { EnsureDirectory(path); File.WriteAllText(path, node.ToJsonString(CompactJson)); }
    private static void WriteText(string path, string text) { EnsureDirectory(path); File.WriteAllText(path, text, Encoding.UTF8); }
    private static void EnsureDirectory(string path) { var dir = Path.GetDirectoryName(path); if (!string.IsNullOrWhiteSpace(dir)) { Directory.CreateDirectory(dir); } }

    private static void AppendStepSummaryIfPresent(string summaryPath) {
        var target = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");
        if (string.IsNullOrWhiteSpace(target) || !File.Exists(summaryPath)) {
            return;
        }
        File.AppendAllText(target, File.ReadAllText(summaryPath) + Environment.NewLine, Encoding.UTF8);
    }

    private static int ReadInt(JsonObject node, string name) {
        if (!node.TryGetPropertyValue(name, out var value) || value is null) { return 0; }
        return value is JsonValue jv && jv.TryGetValue<int>(out var intValue) ? intValue : 0;
    }

    private static int? ReadIntNullable(JsonObject? node, string name) {
        if (node is null || !node.TryGetPropertyValue(name, out var value) || value is null) { return null; }
        return value is JsonValue jv && jv.TryGetValue<int>(out var intValue) ? intValue : null;
    }

    private static bool ReadBool(JsonObject node, string name) => node.TryGetPropertyValue(name, out var value) && value is JsonValue jv && jv.TryGetValue<bool>(out var boolValue) && boolValue;
    private static string ReadString(JsonObject node, string name) => node.TryGetPropertyValue(name, out var value) && value is JsonValue jv && jv.TryGetValue<string>(out var text) ? text ?? string.Empty : string.Empty;
    private static DateTimeOffset? ReadDate(JsonObject node, string name) => DateTimeOffset.TryParse(ReadString(node, name), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value) ? value.ToUniversalTime() : null;
    private static double? ReadDoubleNullable(JsonObject? node, string name) => node is not null && node.TryGetPropertyValue(name, out var value) && value is JsonValue jv && jv.TryGetValue<double>(out var doubleValue) ? doubleValue : null;
    private static string ReadNumberText(JsonObject? node, string name) => node is not null && node.TryGetPropertyValue(name, out var value) && value is not null ? value.ToString() : "0";
    private static string FormatNullable(JsonObject? node, string name) => node is not null && node.TryGetPropertyValue(name, out var value) && value is not null ? value.ToString() : "n/a";
    private static double Ratio(int numerator, int denominator) => denominator <= 0 ? 0 : Math.Round((double)numerator * 100.0 / denominator, 2, MidpointRounding.AwayFromZero);
    private static double? Median(IReadOnlyList<int> values) => values.Count == 0 ? null : values.Count % 2 == 1 ? values[values.Count / 2] : (values[(values.Count / 2) - 1] + values[values.Count / 2]) / 2.0;

    private static int ParseIssueNumber(string url) {
        var token = url.TrimEnd('/').Split('/').LastOrDefault();
        return int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : 0;
    }

    private static void PrintHelp() {
        Console.WriteLine("Usage: intelligencex todo pr-watch-consolidate [options]");
        Console.WriteLine("  --repo <owner/name>");
        Console.WriteLine("  --max-prs <n>");
        Console.WriteLine("  --max-flaky-retries <n>");
        Console.WriteLine("  --stale-days <n>");
        Console.WriteLine("  --include-drafts <bool>");
        Console.WriteLine("  --approved-bot <login> (repeatable)");
        Console.WriteLine("  --approved-bots <csv>");
        Console.WriteLine("  --source <value>");
        Console.WriteLine("  --run-link <url>");
        Console.WriteLine("  --snapshot-dir <path>");
        Console.WriteLine("  --rollup-path <path>");
        Console.WriteLine("  --summary-path <path>");
        Console.WriteLine("  --metrics-path <path>");
        Console.WriteLine("  --metrics-history-path <path>");
        Console.WriteLine("  --tracker-path <path>");
        Console.WriteLine("  --publish-tracking-issue <bool>");
        Console.WriteLine("  --tracker-issue-title <text>");
        Console.WriteLine("  --tracker-issue-label <label> (repeatable)");
        Console.WriteLine("  --tracker-issue-labels <csv>");
    }
}
