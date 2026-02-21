using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using IntelligenceX.Cli.GitHub;

namespace IntelligenceX.Cli.Todo;

internal static partial class PullRequestSignalBacktestRunner {
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private const string StateAll = "all";
    private const string StateMerged = "merged";
    private const string StateClosedUnmerged = "closed-unmerged";

    internal sealed record HistoricalSignalOutcome(
        int Number,
        string Title,
        string Url,
        string Outcome,
        double LifetimeDays,
        TriageIndexRunner.PullRequestOperationalSignals Signals
    );

    internal sealed record BacktestBucketStats(
        string Bucket,
        int Total,
        int Merged,
        int ClosedUnmerged,
        double MergeRate
    );

    private sealed record HistoricalPullRequest(
        int Number,
        string Title,
        string Body,
        string Url,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset UpdatedAtUtc,
        DateTimeOffset? ClosedAtUtc,
        DateTimeOffset? MergedAtUtc,
        string State,
        bool IsDraft,
        string Mergeable,
        string ReviewDecision,
        string StatusCheckState,
        int ChangedFiles,
        int Additions,
        int Deletions,
        int Comments,
        int Commits,
        string Author,
        IReadOnlyList<string> Labels
    );

    private sealed class Options {
        public string Repo { get; set; } = "EvotecIT/IntelligenceX";
        public int MaxPrs { get; set; } = 400;
        public string StateFilter { get; set; } = StateAll;
        public string OutputPath { get; set; } = Path.Combine("artifacts", "triage", "ix-pr-operational-signal-backtest.json");
        public string SummaryPath { get; set; } = Path.Combine("artifacts", "triage", "ix-pr-operational-signal-backtest.md");
        public bool ShowHelp { get; set; }
    }

    public static async Task<int> RunAsync(string[] args) {
        var options = ParseOptions(args);
        if (options.ShowHelp) {
            PrintHelp();
            return 0;
        }

        var (authCode, _, authErr) = await GhCli.RunAsync("auth", "status").ConfigureAwait(false);
        if (authCode != 0) {
            Console.Error.WriteLine("gh is not authenticated. Run `gh auth login`.");
            if (!string.IsNullOrWhiteSpace(authErr)) {
                Console.Error.WriteLine(authErr.Trim());
            }
            return 1;
        }

        List<HistoricalPullRequest> historicalPullRequests;
        try {
            historicalPullRequests = await FetchHistoricalPullRequestsAsync(options).ConfigureAwait(false);
        } catch (Exception ex) {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        var outcomes = AssessHistoricalOutcomes(historicalPullRequests);
        var nowUtc = DateTimeOffset.UtcNow;
        var report = BuildReport(options, nowUtc, outcomes);
        var summary = BuildMarkdownSummary(options, nowUtc, outcomes);

        WriteText(options.OutputPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        WriteText(options.SummaryPath, summary);

        var mergedCount = outcomes.Count(outcome => outcome.Outcome.Equals("merged", StringComparison.OrdinalIgnoreCase));
        var closedUnmergedCount = outcomes.Count - mergedCount;
        Console.WriteLine($"Generated PR signal backtest: {options.OutputPath}");
        Console.WriteLine($"Generated PR signal backtest summary: {options.SummaryPath}");
        Console.WriteLine($"Historical PRs analyzed: {outcomes.Count}");
        Console.WriteLine($"Merged: {mergedCount}");
        Console.WriteLine($"Closed unmerged: {closedUnmergedCount}");
        return 0;
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
                    }
                    break;
                case "--max-prs":
                    if (i + 1 < args.Length && int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxPrs)) {
                        options.MaxPrs = Math.Max(1, Math.Min(maxPrs, 2000));
                    }
                    break;
                case "--state":
                    if (i + 1 < args.Length) {
                        options.StateFilter = NormalizeStateFilter(args[++i]);
                    }
                    break;
                case "--out":
                    if (i + 1 < args.Length) {
                        options.OutputPath = args[++i];
                    }
                    break;
                case "--summary":
                    if (i + 1 < args.Length) {
                        options.SummaryPath = args[++i];
                    }
                    break;
                default:
                    Console.Error.WriteLine($"Unknown option: {arg}");
                    options.ShowHelp = true;
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(options.Repo) || !options.Repo.Contains('/', StringComparison.Ordinal)) {
            options.ShowHelp = true;
        }

        if (!options.StateFilter.Equals(StateAll, StringComparison.OrdinalIgnoreCase) &&
            !options.StateFilter.Equals(StateMerged, StringComparison.OrdinalIgnoreCase) &&
            !options.StateFilter.Equals(StateClosedUnmerged, StringComparison.OrdinalIgnoreCase)) {
            options.ShowHelp = true;
        }

        return options;
    }

    private static string NormalizeStateFilter(string value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return StateAll;
        }

        return value.Trim().ToLowerInvariant() switch {
            "all" => StateAll,
            "merged" => StateMerged,
            "closed" => StateClosedUnmerged,
            "closed-unmerged" => StateClosedUnmerged,
            _ => value.Trim().ToLowerInvariant()
        };
    }

    private static void PrintHelp() {
        Console.WriteLine("Usage:");
        Console.WriteLine("  intelligencex todo backtest-pr-signals [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --repo <owner/name>         Repository to scan (default: EvotecIT/IntelligenceX)");
        Console.WriteLine("  --max-prs <n>               Historical PRs to analyze (1-2000, default: 400)");
        Console.WriteLine("  --state <all|merged|closed-unmerged>  Filter historical outcome set (default: all)");
        Console.WriteLine("  --out <path>                JSON output path (default: artifacts/triage/ix-pr-operational-signal-backtest.json)");
        Console.WriteLine("  --summary <path>            Markdown summary path (default: artifacts/triage/ix-pr-operational-signal-backtest.md)");
    }

    private static async Task<List<HistoricalPullRequest>> FetchHistoricalPullRequestsAsync(Options options) {
        var (owner, name) = SplitRepo(options.Repo);
        var results = new List<HistoricalPullRequest>();
        string? cursor = null;
        var query = """
query($owner: String!, $name: String!, $n: Int!, $cursor: String) {
  repository(owner: $owner, name: $name) {
    pullRequests(states: [MERGED, CLOSED], first: $n, after: $cursor, orderBy: { field: UPDATED_AT, direction: DESC }) {
      nodes {
        number
        title
        body
        url
        createdAt
        updatedAt
        closedAt
        mergedAt
        state
        isDraft
        mergeable
        reviewDecision
        changedFiles
        additions
        deletions
        comments { totalCount }
        commits(last: 1) {
          totalCount
          nodes {
            commit {
              statusCheckRollup {
                state
              }
            }
          }
        }
        author { login }
        labels(first: 30) { nodes { name } }
      }
      pageInfo { hasNextPage endCursor }
    }
  }
}
""";

        while (results.Count < options.MaxPrs) {
            var pageSize = Math.Min(100, options.MaxPrs - results.Count);
            var root = await QueryGraphQlAsync(
                query,
                ("owner", owner),
                ("name", name),
                ("n", pageSize.ToString(CultureInfo.InvariantCulture)),
                ("cursor", cursor)
            ).ConfigureAwait(false);

            if (!TryGetProperty(root, "data", out var data) ||
                !TryGetProperty(data, "repository", out var repository) ||
                !TryGetProperty(repository, "pullRequests", out var pullRequests) ||
                !TryGetProperty(pullRequests, "nodes", out var nodes) ||
                nodes.ValueKind != JsonValueKind.Array) {
                break;
            }

            foreach (var item in nodes.EnumerateArray()) {
                if (results.Count >= options.MaxPrs) {
                    break;
                }

                if (!TryReadNumber(item, out var number)) {
                    continue;
                }

                var mergedAt = ReadNullableDate(item, "mergedAt");
                var closedAt = ReadNullableDate(item, "closedAt");
                var include = options.StateFilter switch {
                    StateMerged => mergedAt.HasValue,
                    StateClosedUnmerged => !mergedAt.HasValue && closedAt.HasValue,
                    _ => true
                };
                if (!include) {
                    continue;
                }

                var title = ReadString(item, "title");
                var body = ReadString(item, "body");
                var url = ReadString(item, "url");
                var createdAt = ReadDate(item, "createdAt");
                var updatedAt = ReadDate(item, "updatedAt");
                var state = ReadString(item, "state");
                var isDraft = ReadBoolean(item, "isDraft");
                var mergeable = ReadString(item, "mergeable");
                var reviewDecision = ReadString(item, "reviewDecision");
                var statusCheckState = ReadNestedNestedString(item, "commits", "nodes", 0, "commit", "statusCheckRollup", "state");
                var changedFiles = ReadInt(item, "changedFiles");
                var additions = ReadInt(item, "additions");
                var deletions = ReadInt(item, "deletions");
                var comments = ReadNestedInt(item, "comments", "totalCount");
                var commits = ReadNestedInt(item, "commits", "totalCount");
                var author = ReadNestedString(item, "author", "login");
                var labels = ReadLabels(item);
                results.Add(new HistoricalPullRequest(
                    number,
                    title,
                    body,
                    url,
                    createdAt,
                    updatedAt,
                    closedAt,
                    mergedAt,
                    state,
                    isDraft,
                    mergeable,
                    reviewDecision,
                    statusCheckState,
                    changedFiles,
                    additions,
                    deletions,
                    comments,
                    commits,
                    author,
                    labels
                ));
            }

            if (!TryGetProperty(pullRequests, "pageInfo", out var pageInfo) ||
                !TryGetProperty(pageInfo, "hasNextPage", out var hasNextPageProp) ||
                hasNextPageProp.ValueKind != JsonValueKind.True && hasNextPageProp.ValueKind != JsonValueKind.False ||
                !hasNextPageProp.GetBoolean()) {
                break;
            }

            cursor = ReadString(pageInfo, "endCursor");
            if (string.IsNullOrWhiteSpace(cursor)) {
                break;
            }
        }

        return results;
    }

}
