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

internal static class PullRequestSignalBacktestRunner {
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

    private static List<HistoricalSignalOutcome> AssessHistoricalOutcomes(IReadOnlyList<HistoricalPullRequest> historicalPullRequests) {
        var outcomes = new List<HistoricalSignalOutcome>(historicalPullRequests.Count);
        foreach (var pullRequest in historicalPullRequests) {
            var signals = new TriageIndexRunner.PullRequestSignals(
                pullRequest.IsDraft,
                pullRequest.Mergeable,
                pullRequest.ReviewDecision,
                pullRequest.StatusCheckState,
                pullRequest.ChangedFiles,
                pullRequest.Additions,
                pullRequest.Deletions,
                pullRequest.Comments,
                pullRequest.Commits,
                pullRequest.Author
            );

            var item = new TriageIndexRunner.TriageIndexItem(
                Id: $"pr#{pullRequest.Number}",
                Kind: "pull_request",
                Number: pullRequest.Number,
                Title: pullRequest.Title,
                Url: pullRequest.Url,
                UpdatedAtUtc: pullRequest.CreatedAtUtc,
                Labels: pullRequest.Labels,
                NormalizedTitle: TriageIndexRunner.NormalizeText(pullRequest.Title),
                TitleTokens: TriageIndexRunner.Tokenize(pullRequest.Title),
                ContextTokens: TriageIndexRunner.Tokenize($"{pullRequest.Title}\n{pullRequest.Body}"),
                PullRequest: signals,
                Issue: null
            );

            var evaluationAtUtc = pullRequest.ClosedAtUtc ?? pullRequest.MergedAtUtc ?? pullRequest.UpdatedAtUtc;
            var operationalSignals = TriageIndexRunner.AssessPullRequestOperationalSignals(item, evaluationAtUtc);
            if (operationalSignals is null) {
                continue;
            }

            var merged = pullRequest.MergedAtUtc.HasValue ||
                         pullRequest.State.Equals("MERGED", StringComparison.OrdinalIgnoreCase);
            var lifetimeDays = Math.Round(
                Math.Max(0, (evaluationAtUtc - pullRequest.CreatedAtUtc).TotalDays),
                2,
                MidpointRounding.AwayFromZero);
            outcomes.Add(new HistoricalSignalOutcome(
                pullRequest.Number,
                pullRequest.Title,
                pullRequest.Url,
                merged ? "merged" : "closed-unmerged",
                lifetimeDays,
                operationalSignals
            ));
        }

        return outcomes;
    }

    internal static IReadOnlyList<BacktestBucketStats> BuildBucketStats(
        IReadOnlyList<HistoricalSignalOutcome> outcomes,
        Func<HistoricalSignalOutcome, string> bucketSelector) {
        var grouped = outcomes
            .GroupBy(outcome => NormalizeBucket(bucketSelector(outcome)), StringComparer.OrdinalIgnoreCase)
            .Select(group => {
                var merged = group.Count(outcome => outcome.Outcome.Equals("merged", StringComparison.OrdinalIgnoreCase));
                var total = group.Count();
                var closedUnmerged = total - merged;
                var mergeRate = total > 0
                    ? Math.Round((double)merged / total, 4, MidpointRounding.AwayFromZero)
                    : 0;
                return new BacktestBucketStats(group.Key, total, merged, closedUnmerged, mergeRate);
            })
            .OrderByDescending(stat => stat.Total)
            .ThenBy(stat => stat.Bucket, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return grouped;
    }

    private static object BuildReport(
        Options options,
        DateTimeOffset generatedAtUtc,
        IReadOnlyList<HistoricalSignalOutcome> outcomes) {
        var mergedCount = outcomes.Count(outcome => outcome.Outcome.Equals("merged", StringComparison.OrdinalIgnoreCase));
        var closedUnmergedCount = outcomes.Count - mergedCount;
        var mergeRate = outcomes.Count > 0
            ? Math.Round((double)mergedCount / outcomes.Count, 4, MidpointRounding.AwayFromZero)
            : 0;
        var avgLifetimeDays = outcomes.Count > 0
            ? Math.Round(outcomes.Average(outcome => outcome.LifetimeDays), 2, MidpointRounding.AwayFromZero)
            : 0;
        var bySizeBand = BuildBucketStats(outcomes, outcome => outcome.Signals.SizeBand);
        var byChurnRisk = BuildBucketStats(outcomes, outcome => outcome.Signals.ChurnRisk);
        var byMergeReadiness = BuildBucketStats(outcomes, outcome => outcome.Signals.MergeReadiness);
        var byFreshness = BuildBucketStats(outcomes, outcome => outcome.Signals.Freshness);
        var byCheckHealth = BuildBucketStats(outcomes, outcome => outcome.Signals.CheckHealth);
        var byReviewLatency = BuildBucketStats(outcomes, outcome => outcome.Signals.ReviewLatency);
        var byMergeConflictRisk = BuildBucketStats(outcomes, outcome => outcome.Signals.MergeConflictRisk);

        static IReadOnlyList<object> ConvertBuckets(IReadOnlyList<BacktestBucketStats> buckets) {
            return buckets
                .Select(bucket => new {
                    bucket = bucket.Bucket,
                    total = bucket.Total,
                    merged = bucket.Merged,
                    closedUnmerged = bucket.ClosedUnmerged,
                    mergeRate = bucket.MergeRate
                })
                .Cast<object>()
                .ToList();
        }

        return new {
            schema = "intelligencex.pr-operational-signal-backtest.v1",
            generatedAtUtc = generatedAtUtc.UtcDateTime.ToString("o", CultureInfo.InvariantCulture),
            repo = options.Repo,
            settings = new {
                maxPrs = options.MaxPrs,
                state = options.StateFilter
            },
            summary = new {
                totalPullRequests = outcomes.Count,
                merged = mergedCount,
                closedUnmerged = closedUnmergedCount,
                mergeRate,
                averageLifetimeDays = avgLifetimeDays,
                signalBreakdown = new {
                    sizeBand = ConvertBuckets(bySizeBand),
                    churnRisk = ConvertBuckets(byChurnRisk),
                    mergeReadiness = ConvertBuckets(byMergeReadiness),
                    freshness = ConvertBuckets(byFreshness),
                    checkHealth = ConvertBuckets(byCheckHealth),
                    reviewLatency = ConvertBuckets(byReviewLatency),
                    mergeConflictRisk = ConvertBuckets(byMergeConflictRisk)
                }
            },
            items = outcomes
                .OrderByDescending(outcome => outcome.LifetimeDays)
                .ThenBy(outcome => outcome.Number)
                .Select(outcome => new {
                    number = outcome.Number,
                    title = outcome.Title,
                    url = outcome.Url,
                    outcome = outcome.Outcome,
                    lifetimeDays = outcome.LifetimeDays,
                    signals = new {
                        sizeBand = outcome.Signals.SizeBand,
                        churnRisk = outcome.Signals.ChurnRisk,
                        mergeReadiness = outcome.Signals.MergeReadiness,
                        freshness = outcome.Signals.Freshness,
                        checkHealth = outcome.Signals.CheckHealth,
                        reviewLatency = outcome.Signals.ReviewLatency,
                        mergeConflictRisk = outcome.Signals.MergeConflictRisk
                    }
                })
                .ToList()
        };
    }

    private static string BuildMarkdownSummary(
        Options options,
        DateTimeOffset generatedAtUtc,
        IReadOnlyList<HistoricalSignalOutcome> outcomes) {
        var mergedCount = outcomes.Count(outcome => outcome.Outcome.Equals("merged", StringComparison.OrdinalIgnoreCase));
        var closedUnmergedCount = outcomes.Count - mergedCount;
        var mergeRate = outcomes.Count > 0
            ? Math.Round((double)mergedCount / outcomes.Count, 4, MidpointRounding.AwayFromZero)
            : 0;
        var avgLifetimeDays = outcomes.Count > 0
            ? Math.Round(outcomes.Average(outcome => outcome.LifetimeDays), 2, MidpointRounding.AwayFromZero)
            : 0;

        var sb = new StringBuilder();
        sb.AppendLine("# PR Operational Signal Backtest");
        sb.AppendLine();
        sb.AppendLine($"- Generated: {generatedAtUtc.UtcDateTime:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"- Repo: `{options.Repo}`");
        sb.AppendLine($"- Historical sample size: {outcomes.Count}");
        sb.AppendLine($"- Outcome filter: `{options.StateFilter}`");
        sb.AppendLine($"- Merged: {mergedCount}");
        sb.AppendLine($"- Closed unmerged: {closedUnmergedCount}");
        sb.AppendLine($"- Merge rate: {(mergeRate * 100).ToString("0.00", CultureInfo.InvariantCulture)}%");
        sb.AppendLine($"- Average PR lifetime: {avgLifetimeDays.ToString("0.00", CultureInfo.InvariantCulture)} days");
        sb.AppendLine();
        AppendBucketSection(sb, "Merge Readiness", BuildBucketStats(outcomes, outcome => outcome.Signals.MergeReadiness));
        AppendBucketSection(sb, "Check Health", BuildBucketStats(outcomes, outcome => outcome.Signals.CheckHealth));
        AppendBucketSection(sb, "Merge Conflict Risk", BuildBucketStats(outcomes, outcome => outcome.Signals.MergeConflictRisk));
        AppendBucketSection(sb, "Review Latency", BuildBucketStats(outcomes, outcome => outcome.Signals.ReviewLatency));
        AppendBucketSection(sb, "Size Band", BuildBucketStats(outcomes, outcome => outcome.Signals.SizeBand));
        AppendBucketSection(sb, "Churn Risk", BuildBucketStats(outcomes, outcome => outcome.Signals.ChurnRisk));
        AppendBucketSection(sb, "Freshness", BuildBucketStats(outcomes, outcome => outcome.Signals.Freshness));
        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    private static void AppendBucketSection(StringBuilder sb, string title, IReadOnlyList<BacktestBucketStats> buckets) {
        sb.AppendLine($"## {title}");
        sb.AppendLine();
        if (buckets.Count == 0) {
            sb.AppendLine("- No data.");
            sb.AppendLine();
            return;
        }

        foreach (var bucket in buckets) {
            sb.AppendLine(
                $"- `{bucket.Bucket}`: {bucket.Total} total, {bucket.Merged} merged, {bucket.ClosedUnmerged} closed-unmerged, merge rate {(bucket.MergeRate * 100).ToString("0.00", CultureInfo.InvariantCulture)}%");
        }
        sb.AppendLine();
    }

    private static string NormalizeBucket(string bucket) {
        return string.IsNullOrWhiteSpace(bucket) ? "unknown" : bucket.Trim().ToLowerInvariant();
    }

    private static void WriteText(string path, string content) {
        var fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory)) {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(fullPath, content, Utf8NoBom);
    }

    private static (string Owner, string Name) SplitRepo(string repo) {
        var parts = repo.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2) {
            throw new InvalidOperationException($"Invalid repo value: '{repo}'. Expected owner/name.");
        }
        return (parts[0], parts[1]);
    }

    private static async Task<JsonElement> QueryGraphQlAsync(string query, params (string Key, string? Value)[] variables) {
        var args = new List<string> {
            "api",
            "graphql",
            "-f",
            $"query={query}"
        };

        foreach (var (key, value) in variables) {
            if (value is null) {
                continue;
            }
            args.Add("-F");
            args.Add($"{key}={value}");
        }

        var (code, stdout, stderr) = await GhCli.RunAsync(TimeSpan.FromSeconds(120), args.ToArray()).ConfigureAwait(false);
        if (code != 0) {
            throw new InvalidOperationException(stderr.Trim().Length > 0 ? stderr.Trim() : "Failed to query GitHub GraphQL API.");
        }

        using var document = JsonDocument.Parse(stdout);
        var root = document.RootElement.Clone();
        if (root.TryGetProperty("errors", out var errors) &&
            errors.ValueKind == JsonValueKind.Array &&
            errors.GetArrayLength() > 0) {
            var first = errors[0];
            var message = first.TryGetProperty("message", out var msg)
                ? (msg.GetString() ?? "GraphQL error")
                : "GraphQL error";
            throw new InvalidOperationException($"GitHub GraphQL returned errors: {message}");
        }

        return root;
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value) {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out value)) {
            return true;
        }

        value = default;
        return false;
    }

    private static bool TryReadNumber(JsonElement element, out int number) {
        number = 0;
        if (!TryGetProperty(element, "number", out var numberValue)) {
            return false;
        }

        if (numberValue.ValueKind == JsonValueKind.Number && numberValue.TryGetInt32(out number)) {
            return true;
        }

        if (numberValue.ValueKind == JsonValueKind.String &&
            int.TryParse(numberValue.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number)) {
            return true;
        }

        return false;
    }

    private static string ReadString(JsonElement element, string propertyName) {
        return TryGetProperty(element, propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static DateTimeOffset ReadDate(JsonElement element, string propertyName) {
        if (!TryGetProperty(element, propertyName, out var value) || value.ValueKind != JsonValueKind.String) {
            return DateTimeOffset.UtcNow;
        }

        return DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : DateTimeOffset.UtcNow;
    }

    private static DateTimeOffset? ReadNullableDate(JsonElement element, string propertyName) {
        if (!TryGetProperty(element, propertyName, out var value) || value.ValueKind == JsonValueKind.Null) {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String) {
            return null;
        }

        return DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : null;
    }

    private static bool ReadBoolean(JsonElement element, string propertyName) {
        if (!TryGetProperty(element, propertyName, out var value)) {
            return false;
        }

        return value.ValueKind switch {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            _ => false
        };
    }

    private static int ReadInt(JsonElement element, string propertyName) {
        if (!TryGetProperty(element, propertyName, out var value)) {
            return 0;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) {
            return number;
        }

        return value.ValueKind == JsonValueKind.String &&
               int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;
    }

    private static int ReadNestedInt(JsonElement element, string outerProperty, string innerProperty) {
        if (!TryGetProperty(element, outerProperty, out var outer) || !TryGetProperty(outer, innerProperty, out var inner)) {
            return 0;
        }

        if (inner.ValueKind == JsonValueKind.Number && inner.TryGetInt32(out var number)) {
            return number;
        }

        return inner.ValueKind == JsonValueKind.String &&
               int.TryParse(inner.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;
    }

    private static string ReadNestedString(JsonElement element, string outerProperty, string innerProperty) {
        if (!TryGetProperty(element, outerProperty, out var outer) || !TryGetProperty(outer, innerProperty, out var inner)) {
            return string.Empty;
        }

        return inner.ValueKind == JsonValueKind.String ? inner.GetString() ?? string.Empty : string.Empty;
    }

    private static string ReadNestedNestedString(
        JsonElement element,
        string outerProperty,
        string arrayProperty,
        int index,
        string nestedProperty,
        string innerProperty,
        string finalProperty) {
        if (!TryGetProperty(element, outerProperty, out var outer) ||
            !TryGetProperty(outer, arrayProperty, out var arrayValue) ||
            arrayValue.ValueKind != JsonValueKind.Array ||
            arrayValue.GetArrayLength() <= index) {
            return string.Empty;
        }

        var item = arrayValue[index];
        if (!TryGetProperty(item, nestedProperty, out var nested) ||
            !TryGetProperty(nested, innerProperty, out var inner) ||
            !TryGetProperty(inner, finalProperty, out var final)) {
            return string.Empty;
        }

        return final.ValueKind == JsonValueKind.String ? final.GetString() ?? string.Empty : string.Empty;
    }

    private static IReadOnlyList<string> ReadLabels(JsonElement element) {
        if (!TryGetProperty(element, "labels", out var labels) ||
            !TryGetProperty(labels, "nodes", out var nodes) ||
            nodes.ValueKind != JsonValueKind.Array) {
            return Array.Empty<string>();
        }

        var values = new List<string>();
        foreach (var node in nodes.EnumerateArray()) {
            var value = ReadString(node, "name");
            if (!string.IsNullOrWhiteSpace(value)) {
                values.Add(value.Trim());
            }
        }

        return values;
    }
}
