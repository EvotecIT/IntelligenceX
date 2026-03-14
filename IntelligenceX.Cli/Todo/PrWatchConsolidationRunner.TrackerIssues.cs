using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using IntelligenceX.Cli.GitHub;

namespace IntelligenceX.Cli.Todo;

internal static partial class PrWatchConsolidationRunner {
    private static async Task<string> SyncTrackerIssueAsync(Options options, JsonObject rollup, JsonObject metrics) {
        var existing = await FindOpenTrackerIssuesAsync(options).ConfigureAwait(false);
        var plan = BuildTrackerIssueSyncPlan(rollup, metrics, existing);
        foreach (var issue in plan.IssuesToClose) {
            var closeComment = plan.PublishTrackerIssue
                ? "Closing automatically because a duplicate tracker issue exists for this source and the oldest open tracker remains the canonical sink."
                : null;
            await CloseTrackerIssueAsync(options, issue, closeComment).ConfigureAwait(false);
        }

        if (!plan.PublishTrackerIssue) {
            return string.Empty;
        }

        return await UpsertTrackerIssueAsync(options, plan.CanonicalIssue).ConfigureAwait(false);
    }

    private static async Task<List<JsonObject>> FindOpenTrackerIssuesAsync(Options options) {
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
            .Where(issue => ReadString(issue, "body").Contains(marker, StringComparison.Ordinal))
            .OrderBy(static issue => ReadInt(issue, "number"))
            .ToList();
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

    private static async Task CloseTrackerIssueAsync(Options options, JsonObject existing, string? comment = null) {
        var issueNumber = ReadInt(existing, "number");
        if (string.IsNullOrWhiteSpace(comment)) {
            var title = ReadString(existing, "title");
            comment = string.IsNullOrWhiteSpace(title)
                ? "Closing automatically because the latest PR babysit rollup is clean and no longer needs a tracking issue."
                : $"Closing automatically because the latest PR babysit rollup for '{title}' is clean and no longer needs a tracking issue.";
        }

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
        return ReadTrackerSignals(rollup, metrics).HasActionableContent;
    }

    internal static TrackerSignals ReadTrackerSignalsForTests(JsonObject rollup, JsonObject metrics) =>
        ReadTrackerSignals(rollup, metrics);

    internal static TrackerIssueSyncPlan BuildTrackerIssueSyncPlanForTests(
        JsonObject rollup,
        JsonObject metrics,
        IReadOnlyList<JsonObject> matchingOpenIssues) =>
        BuildTrackerIssueSyncPlan(rollup, metrics, matchingOpenIssues);

    private static TrackerSignals ReadTrackerSignals(JsonObject rollup, JsonObject metrics) {
        var ratios = metrics["ratiosPct"] as JsonObject;
        return new TrackerSignals(
            FailedTargets: (rollup["failedTargets"] as JsonArray)?.Count ?? 0,
            StaleInfraBlocked: (rollup["staleInfraBlocked"] as JsonArray)?.Count ?? 0,
            ReviewRequired: (rollup["reviewRequired"] as JsonArray)?.Count ?? 0,
            RetryBudgetExhausted: (rollup["retryBudgetExhausted"] as JsonArray)?.Count ?? 0,
            StaleOpenPrsRatioPct: ReadDoubleNullable(ratios, "staleOpenPrs").GetValueOrDefault(),
            ReviewRequiredRatioPct: ReadDoubleNullable(ratios, "reviewRequiredPrs").GetValueOrDefault(),
            RetryBudgetExhaustedRatioPct: ReadDoubleNullable(ratios, "retryBudgetExhaustedPrs").GetValueOrDefault(),
            NoProgressRatioPct: ReadDoubleNullable(ratios, "noProgressPrs").GetValueOrDefault());
    }

    private static TrackerIssueSyncPlan BuildTrackerIssueSyncPlan(
        JsonObject rollup,
        JsonObject metrics,
        IReadOnlyList<JsonObject> matchingOpenIssues) {
        var orderedIssues = matchingOpenIssues
            .OrderBy(static issue => ReadInt(issue, "number"))
            .ToList();
        var signals = ReadTrackerSignals(rollup, metrics);
        if (!signals.HasActionableContent) {
            return new TrackerIssueSyncPlan(
                PublishTrackerIssue: false,
                CanonicalIssue: null,
                IssuesToClose: orderedIssues);
        }

        var canonicalIssue = orderedIssues.FirstOrDefault();
        IReadOnlyList<JsonObject> duplicates = canonicalIssue is null
            ? Array.Empty<JsonObject>()
            : orderedIssues.Skip(1).ToList();
        return new TrackerIssueSyncPlan(
            PublishTrackerIssue: true,
            CanonicalIssue: canonicalIssue,
            IssuesToClose: duplicates);
    }

    private static string FormatRatio(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string TrackerMarker(string source) => $"<!-- intelligencex:pr-watch-rollup-tracker:{SanitizeSource(source)} -->";

    private static string SanitizeSource(string source) =>
        string.IsNullOrWhiteSpace(source)
            ? "default"
            : new string(source.Select(static c => char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.' ? c : '-').ToArray()).Trim('-');
}
