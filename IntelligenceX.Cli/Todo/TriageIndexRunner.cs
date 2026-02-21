using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using IntelligenceX.Cli.GitHub;

namespace IntelligenceX.Cli.Todo;

internal static partial class TriageIndexRunner {
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase) {
        "a", "an", "and", "are", "as", "at", "be", "by", "for", "from",
        "how", "in", "is", "it", "of", "on", "or", "that", "the", "to",
        "was", "were", "will", "with", "this", "these", "those", "into", "over",
        "under", "fix", "update", "add", "remove", "improve", "refactor", "cleanup"
    };
    private static readonly Regex ExplicitIssueRef = new(
        @"(?i)\b(?:fix(?:e[sd])?|close[sd]?|resolve[sd]?|ref(?:s|erences?)|related to)\s+#(?<num>\d+)\b",
        RegexOptions.Compiled
    );
    private static readonly Regex ExplicitRepoIssueRef = new(
        @"(?i)\b(?:fix(?:e[sd])?|close[sd]?|resolve[sd]?|ref(?:s|erences?)|related to)\s+(?<owner>[A-Za-z0-9_.-]+)/(?<repo>[A-Za-z0-9_.-]+)#(?<num>\d+)\b",
        RegexOptions.Compiled
    );
    private static readonly Regex ExplicitIssueUrlRef = new(
        @"(?i)\b(?:fix(?:e[sd])?|close[sd]?|resolve[sd]?|ref(?:s|erences?)|related to)\s+https?://github\.com/(?<owner>[A-Za-z0-9_.-]+)/(?<repo>[A-Za-z0-9_.-]+)/issues/(?<num>\d+)\b",
        RegexOptions.Compiled
    );
    private static readonly Regex DirectIssueRef = new(
        @"(?i)\b(?:issue|bug|ticket|task)\s*#(?<num>\d+)\b",
        RegexOptions.Compiled
    );
    private static readonly Regex DirectRepoIssueRef = new(
        @"(?i)\b(?:issue|bug|ticket|task)\s+(?<owner>[A-Za-z0-9_.-]+)/(?<repo>[A-Za-z0-9_.-]+)#(?<num>\d+)\b",
        RegexOptions.Compiled
    );
    private static readonly Regex DirectIssueUrlRef = new(
        @"(?i)\b(?:issue|bug|ticket|task)\s+https?://github\.com/(?<owner>[A-Za-z0-9_.-]+)/(?<repo>[A-Za-z0-9_.-]+)/issues/(?<num>\d+)\b",
        RegexOptions.Compiled
    );
    private static readonly Regex BareIssueUrlRef = new(
        @"(?i)\bhttps?://github\.com/(?<owner>[A-Za-z0-9_.-]+)/(?<repo>[A-Za-z0-9_.-]+)/issues/(?<num>\d+)\b",
        RegexOptions.Compiled
    );
    private static readonly Regex ExplicitPullRequestRef = new(
        @"(?i)\b(?:fix(?:e[sd])?\s+by|close[sd]?\s+by|resolve[sd]?\s+by|implemented\s+in|addressed\s+in)\s+(?:pr|pull\s*request|pull)\s*#(?<num>\d+)\b",
        RegexOptions.Compiled
    );
    private static readonly Regex DirectPullRequestRef = new(
        @"(?i)\b(?:pr|pull\s*request|pull)\s*#(?<num>\d+)\b",
        RegexOptions.Compiled
    );
    private static readonly Regex DirectRepoPullRequestRef = new(
        @"(?i)\b(?:pr|pull\s*request|pull)\s+(?<owner>[A-Za-z0-9_.-]+)/(?<repo>[A-Za-z0-9_.-]+)#(?<num>\d+)\b",
        RegexOptions.Compiled
    );
    private static readonly Regex DirectPullRequestUrlRef = new(
        @"(?i)\b(?:pr|pull\s*request|pull)\s+https?://github\.com/(?<owner>[A-Za-z0-9_.-]+)/(?<repo>[A-Za-z0-9_.-]+)/pull/(?<num>\d+)\b",
        RegexOptions.Compiled
    );
    private static readonly Regex BarePullRequestUrlRef = new(
        @"(?i)\bhttps?://github\.com/(?<owner>[A-Za-z0-9_.-]+)/(?<repo>[A-Za-z0-9_.-]+)/pull/(?<num>\d+)\b",
        RegexOptions.Compiled
    );

    internal sealed record PullRequestSignals(
        bool IsDraft,
        string Mergeable,
        string ReviewDecision,
        string StatusCheckState,
        int ChangedFiles,
        int Additions,
        int Deletions,
        int Comments,
        int Commits,
        string Author
    );

    internal sealed record IssueSignals(
        int Comments,
        string Author
    );

    internal sealed record TriageIndexItem(
        string Id,
        string Kind,
        int Number,
        string Title,
        string Url,
        DateTimeOffset UpdatedAtUtc,
        IReadOnlyList<string> Labels,
        string NormalizedTitle,
        IReadOnlyList<string> TitleTokens,
        IReadOnlyList<string> ContextTokens,
        PullRequestSignals? PullRequest,
        IssueSignals? Issue
    );

    internal sealed record DuplicateCluster(
        string Id,
        double Confidence,
        string CanonicalItemId,
        IReadOnlyList<string> ItemIds,
        string Reason
    );

    private sealed record RawPullRequest(
        int Number,
        string Title,
        string Body,
        string Url,
        DateTimeOffset UpdatedAtUtc,
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

    private sealed record RawIssue(
        int Number,
        string Title,
        string Body,
        string Url,
        DateTimeOffset UpdatedAtUtc,
        int Comments,
        string Author,
        IReadOnlyList<string> Labels
    );

    private sealed record ItemWithScore(
        TriageIndexItem Item,
        double? Score,
        IReadOnlyList<string> ScoreReasons,
        string? DuplicateClusterId
    );

    private sealed record BestPullRequest(
        string Id,
        int Number,
        string Title,
        string Url,
        double Score,
        IReadOnlyList<string> Reasons,
        string? DuplicateClusterId
    );

    internal sealed record RelatedIssueCandidate(
        int Number,
        string Url,
        double Confidence,
        string Reason
    );

    internal sealed record RelatedPullRequestCandidate(
        int Number,
        string Url,
        double Confidence,
        string Reason
    );

    internal sealed record ItemEnrichment(
        string Category,
        double CategoryConfidence,
        IReadOnlyList<string> Tags,
        IReadOnlyDictionary<string, double> TagConfidences,
        string? MatchedIssueUrl,
        double? MatchedIssueConfidence,
        IReadOnlyList<RelatedIssueCandidate> RelatedIssues,
        string? MatchedPullRequestUrl,
        double? MatchedPullRequestConfidence,
        IReadOnlyList<RelatedPullRequestCandidate> RelatedPullRequests
    );

    internal sealed record SignalQualityAssessment(
        string Level,
        double Score,
        IReadOnlyList<string> Reasons
    );

    internal sealed record PullRequestOperationalSignals(
        string SizeBand,
        string ChurnRisk,
        string MergeReadiness,
        string Freshness,
        string CheckHealth,
        string ReviewLatency,
        string MergeConflictRisk
    );

    private const int ChurnHighChangedFilesThreshold = 120;
    private const int ChurnHighChangeVolumeThreshold = 3500;
    private const int ChurnHighCommentsThreshold = 35;
    private const int ChurnHighCommitsThreshold = 35;
    private const int ChurnMediumChangedFilesThreshold = 40;
    private const int ChurnMediumChangeVolumeThreshold = 1200;
    private const int ChurnMediumCommentsThreshold = 12;
    private const int ChurnMediumCommitsThreshold = 15;
    private const int ReviewLatencyLowAgeDaysThreshold = 2;
    private const int ReviewLatencyMediumAgeDaysThreshold = 10;
    private const int ReadyReviewLatencyLowAgeDaysThreshold = 1;
    private const int ReadyReviewLatencyMediumAgeDaysThreshold = 4;
    private const int PendingReviewLatencyHighAgeDaysThreshold = 4;
    private const int ConflictRiskMediumAgeDaysThreshold = 14;
    private const int ConflictRiskHighAgeDaysThreshold = 21;

    private sealed record IssueReferenceHint(
        int Number,
        double Confidence,
        string Reason
    );

    private sealed record PullRequestReferenceHint(
        int Number,
        double Confidence,
        string Reason
    );

    private sealed class Options {
        public string Repo { get; set; } = "EvotecIT/IntelligenceX";
        public int MaxPrs { get; set; } = 100;
        public int MaxIssues { get; set; } = 100;
        public double DuplicateThreshold { get; set; } = 0.82;
        public int BestLimit { get; set; } = 20;
        public string OutputPath { get; set; } = Path.Combine("artifacts", "triage", "ix-triage-index.json");
        public string SummaryPath { get; set; } = Path.Combine("artifacts", "triage", "ix-triage-index.md");
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

        var nowUtc = DateTimeOffset.UtcNow;
        List<RawPullRequest> pullRequests;
        List<RawIssue> issues;
        try {
            (pullRequests, issues) = await FetchOpenWorkAsync(options).ConfigureAwait(false);
        } catch (Exception ex) {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        var items = BuildItems(pullRequests, issues);
        var clusters = BuildDuplicateClusters(items, options.DuplicateThreshold);
        var clusterByItem = BuildClusterLookup(clusters);
        var enrichments = BuildItemEnrichments(options.Repo, items, pullRequests, issues);

        var scoredItems = new List<ItemWithScore>(items.Count);
        foreach (var item in items) {
            List<string> reasons;
            double? score;
            if (item.Kind == "pull_request") {
                score = ScorePullRequest(item, nowUtc, out reasons);
            } else {
                score = null;
                reasons = new List<string>();
            }
            var clusterId = clusterByItem.TryGetValue(item.Id, out var foundClusterId) ? foundClusterId : null;
            scoredItems.Add(new ItemWithScore(item, score, reasons, clusterId));
        }

        var bestPullRequests = BuildBestPullRequests(scoredItems, clusters, options.BestLimit);
        var report = BuildReport(options, nowUtc, scoredItems, clusters, bestPullRequests, enrichments);
        var summary = BuildMarkdownSummary(options, nowUtc, scoredItems, clusters, bestPullRequests, enrichments);

        WriteText(options.OutputPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        WriteText(options.SummaryPath, summary);

        Console.WriteLine($"Generated triage index: {options.OutputPath}");
        Console.WriteLine($"Generated triage summary: {options.SummaryPath}");
        Console.WriteLine($"Items: {items.Count} (PRs: {pullRequests.Count}, Issues: {issues.Count})");
        Console.WriteLine($"Duplicate clusters: {clusters.Count}");
        Console.WriteLine($"Best PR candidates: {bestPullRequests.Count}");
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
                case "--max-issues":
                    if (i + 1 < args.Length && int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxIssues)) {
                        options.MaxIssues = Math.Max(1, Math.Min(maxIssues, 2000));
                    }
                    break;
                case "--duplicate-threshold":
                    if (i + 1 < args.Length &&
                        double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out var threshold)) {
                        options.DuplicateThreshold = Math.Clamp(threshold, 0.50, 1.0);
                    }
                    break;
                case "--best-limit":
                    if (i + 1 < args.Length && int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var bestLimit)) {
                        options.BestLimit = Math.Max(1, Math.Min(bestLimit, 100));
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

        if (string.IsNullOrWhiteSpace(options.Repo) || !options.Repo.Contains('/')) {
            options.ShowHelp = true;
        }

        return options;
    }

    private static void PrintHelp() {
        Console.WriteLine("Usage:");
        Console.WriteLine("  intelligencex todo build-triage-index [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --repo <owner/name>         Repository to scan (default: EvotecIT/IntelligenceX)");
        Console.WriteLine("  --max-prs <n>               Open PRs to scan (1-2000, default: 100)");
        Console.WriteLine("  --max-issues <n>            Open issues to scan (1-2000, default: 100)");
        Console.WriteLine("  --duplicate-threshold <n>   Duplicate confidence threshold 0.50-1.0 (default: 0.82)");
        Console.WriteLine("  --best-limit <n>            Best PR candidates in summary (1-100, default: 20)");
        Console.WriteLine("  --out <path>                JSON output path (default: artifacts/triage/ix-triage-index.json)");
        Console.WriteLine("  --summary <path>            Markdown summary path (default: artifacts/triage/ix-triage-index.md)");
    }

    private static async Task<(List<RawPullRequest> PullRequests, List<RawIssue> Issues)> FetchOpenWorkAsync(Options options) {
        var (owner, name) = SplitRepo(options.Repo);
        var prs = await FetchPullRequestsAsync(owner, name, options.MaxPrs).ConfigureAwait(false);
        var issues = await FetchIssuesAsync(owner, name, options.MaxIssues).ConfigureAwait(false);
        return (prs, issues);
    }

    private static async Task<List<RawPullRequest>> FetchPullRequestsAsync(string owner, string name, int maxPrs) {
        var results = new List<RawPullRequest>();
        string? cursor = null;
        var query = """
query($owner: String!, $name: String!, $n: Int!, $cursor: String) {
  repository(owner: $owner, name: $name) {
    pullRequests(states: OPEN, first: $n, after: $cursor, orderBy: { field: UPDATED_AT, direction: DESC }) {
      nodes {
        number
        title
        body
        url
        updatedAt
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

        while (results.Count < maxPrs) {
            var pageSize = Math.Min(100, maxPrs - results.Count);
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
                if (results.Count >= maxPrs) {
                    break;
                }
                if (!TryReadNumber(item, out var number)) {
                    continue;
                }
                var title = ReadString(item, "title");
                var body = ReadString(item, "body");
                var url = ReadString(item, "url");
                var updatedAt = ReadDate(item, "updatedAt");
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
                results.Add(new RawPullRequest(
                    number, title, body, url, updatedAt, isDraft, mergeable, reviewDecision, statusCheckState,
                    changedFiles, additions, deletions, comments, commits, author, labels
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

    private static async Task<List<RawIssue>> FetchIssuesAsync(string owner, string name, int maxIssues) {
        var results = new List<RawIssue>();
        string? cursor = null;
        var query = """
query($owner: String!, $name: String!, $n: Int!, $cursor: String) {
  repository(owner: $owner, name: $name) {
    issues(states: OPEN, first: $n, after: $cursor, orderBy: { field: UPDATED_AT, direction: DESC }) {
      nodes {
        number
        title
        body
        url
        updatedAt
        comments { totalCount }
        author { login }
        labels(first: 30) { nodes { name } }
      }
      pageInfo { hasNextPage endCursor }
    }
  }
}
""";

        while (results.Count < maxIssues) {
            var pageSize = Math.Min(100, maxIssues - results.Count);
            var root = await QueryGraphQlAsync(
                query,
                ("owner", owner),
                ("name", name),
                ("n", pageSize.ToString(CultureInfo.InvariantCulture)),
                ("cursor", cursor)
            ).ConfigureAwait(false);

            if (!TryGetProperty(root, "data", out var data) ||
                !TryGetProperty(data, "repository", out var repository) ||
                !TryGetProperty(repository, "issues", out var issues) ||
                !TryGetProperty(issues, "nodes", out var nodes) ||
                nodes.ValueKind != JsonValueKind.Array) {
                break;
            }

            foreach (var item in nodes.EnumerateArray()) {
                if (results.Count >= maxIssues) {
                    break;
                }
                if (!TryReadNumber(item, out var number)) {
                    continue;
                }
                var title = ReadString(item, "title");
                var body = ReadString(item, "body");
                var url = ReadString(item, "url");
                var updatedAt = ReadDate(item, "updatedAt");
                var comments = ReadNestedInt(item, "comments", "totalCount");
                var author = ReadNestedString(item, "author", "login");
                var labels = ReadLabels(item);
                results.Add(new RawIssue(number, title, body, url, updatedAt, comments, author, labels));
            }

            if (!TryGetProperty(issues, "pageInfo", out var pageInfo) ||
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

        var (code, stdout, stderr) = await GhCli.RunAsync(TimeSpan.FromSeconds(90), args.ToArray()).ConfigureAwait(false);
        if (code != 0) {
            throw new InvalidOperationException(stderr.Trim().Length > 0 ? stderr.Trim() : "Failed to query GitHub GraphQL API.");
        }

        using var doc = JsonDocument.Parse(stdout);
        var root = doc.RootElement.Clone();
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

}
