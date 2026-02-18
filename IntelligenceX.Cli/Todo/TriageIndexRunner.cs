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

internal static class TriageIndexRunner {
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

    private static List<TriageIndexItem> BuildItems(IReadOnlyList<RawPullRequest> pullRequests, IReadOnlyList<RawIssue> issues) {
        var items = new List<TriageIndexItem>(pullRequests.Count + issues.Count);
        foreach (var pr in pullRequests) {
            var normalizedTitle = NormalizeText(pr.Title);
            var titleTokens = Tokenize(pr.Title);
            var contextTokens = Tokenize($"{pr.Title}\n{pr.Body}");
            var signals = new PullRequestSignals(pr.IsDraft, pr.Mergeable, pr.ReviewDecision, pr.StatusCheckState, pr.ChangedFiles,
                pr.Additions, pr.Deletions, pr.Comments, pr.Commits, pr.Author);
            items.Add(new TriageIndexItem(
                Id: $"pr#{pr.Number}",
                Kind: "pull_request",
                Number: pr.Number,
                Title: pr.Title,
                Url: pr.Url,
                UpdatedAtUtc: pr.UpdatedAtUtc,
                Labels: pr.Labels,
                NormalizedTitle: normalizedTitle,
                TitleTokens: titleTokens,
                ContextTokens: contextTokens,
                PullRequest: signals,
                Issue: null
            ));
        }

        foreach (var issue in issues) {
            var normalizedTitle = NormalizeText(issue.Title);
            var titleTokens = Tokenize(issue.Title);
            var contextTokens = Tokenize($"{issue.Title}\n{issue.Body}");
            var signals = new IssueSignals(issue.Comments, issue.Author);
            items.Add(new TriageIndexItem(
                Id: $"issue#{issue.Number}",
                Kind: "issue",
                Number: issue.Number,
                Title: issue.Title,
                Url: issue.Url,
                UpdatedAtUtc: issue.UpdatedAtUtc,
                Labels: issue.Labels,
                NormalizedTitle: normalizedTitle,
                TitleTokens: titleTokens,
                ContextTokens: contextTokens,
                PullRequest: null,
                Issue: signals
            ));
        }

        return items;
    }

    private static Dictionary<string, ItemEnrichment> BuildItemEnrichments(
        string repo,
        IReadOnlyList<TriageIndexItem> items,
        IReadOnlyList<RawPullRequest> pullRequests,
        IReadOnlyList<RawIssue> issues) {
        var enrichments = new Dictionary<string, ItemEnrichment>(StringComparer.OrdinalIgnoreCase);
        var issueItems = items
            .Where(item => item.Kind.Equals("issue", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var pullRequestItems = items
            .Where(item => item.Kind.Equals("pull_request", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var rawPrByNumber = pullRequests.ToDictionary(pr => pr.Number);
        var rawIssueByNumber = issues.ToDictionary(issue => issue.Number);

        foreach (var item in items) {
            var inference = InferCategoryAndTagsWithConfidence(item);

            IReadOnlyList<RelatedIssueCandidate> relatedIssues = Array.Empty<RelatedIssueCandidate>();
            IReadOnlyList<RelatedPullRequestCandidate> relatedPullRequests = Array.Empty<RelatedPullRequestCandidate>();
            string? matchedIssueUrl = null;
            double? matchedIssueConfidence = null;
            string? matchedPullRequestUrl = null;
            double? matchedPullRequestConfidence = null;

            if (item.Kind.Equals("pull_request", StringComparison.OrdinalIgnoreCase)) {
                if (rawPrByNumber.TryGetValue(item.Number, out var rawPr)) {
                    relatedIssues = MatchPullRequestToIssues(repo, rawPr.Title, rawPr.Body, issueItems);
                } else {
                    relatedIssues = MatchPullRequestToIssues(repo, item.Title, string.Join(' ', item.ContextTokens), issueItems);
                }

                if (relatedIssues.Count > 0) {
                    matchedIssueUrl = relatedIssues[0].Url;
                    matchedIssueConfidence = relatedIssues[0].Confidence;
                }
            } else if (item.Kind.Equals("issue", StringComparison.OrdinalIgnoreCase)) {
                if (rawIssueByNumber.TryGetValue(item.Number, out var rawIssue)) {
                    relatedPullRequests = MatchIssueToPullRequests(repo, rawIssue.Title, rawIssue.Body, pullRequestItems);
                } else {
                    relatedPullRequests = MatchIssueToPullRequests(repo, item.Title, string.Join(' ', item.ContextTokens), pullRequestItems);
                }

                if (relatedPullRequests.Count > 0) {
                    matchedPullRequestUrl = relatedPullRequests[0].Url;
                    matchedPullRequestConfidence = relatedPullRequests[0].Confidence;
                }
            }

            enrichments[item.Id] = new ItemEnrichment(
                Category: inference.Category,
                CategoryConfidence: inference.CategoryConfidence,
                Tags: inference.Tags,
                TagConfidences: inference.TagConfidences,
                MatchedIssueUrl: matchedIssueUrl,
                MatchedIssueConfidence: matchedIssueConfidence,
                RelatedIssues: relatedIssues,
                MatchedPullRequestUrl: matchedPullRequestUrl,
                MatchedPullRequestConfidence: matchedPullRequestConfidence,
                RelatedPullRequests: relatedPullRequests
            );
        }

        return enrichments;
    }

    internal sealed record CategoryTagInference(
        string Category,
        double CategoryConfidence,
        IReadOnlyList<string> Tags,
        IReadOnlyDictionary<string, double> TagConfidences
    );

    internal static (string Category, IReadOnlyList<string> Tags) InferCategoryAndTags(TriageIndexItem item) {
        var inference = InferCategoryAndTagsWithConfidence(item);
        return (inference.Category, inference.Tags);
    }

    internal static SignalQualityAssessment AssessSignalQuality(TriageIndexItem item, ItemEnrichment? enrichment) {
        var score = 40.0;
        var reasons = new List<string>();

        var titleTokenCount = item.TitleTokens.Count;
        if (titleTokenCount >= 6) {
            score += 18;
            reasons.Add("Title contains strong intent detail.");
        } else if (titleTokenCount >= 3) {
            score += 10;
            reasons.Add("Title contains basic intent detail.");
        } else {
            score -= 10;
            reasons.Add("Title is too short for reliable intent inference.");
        }

        var contextTokenCount = item.ContextTokens.Count;
        if (contextTokenCount >= 20) {
            score += 18;
            reasons.Add("Description/context is detailed.");
        } else if (contextTokenCount >= 10) {
            score += 10;
            reasons.Add("Description/context has moderate detail.");
        } else {
            score -= 12;
            reasons.Add("Description/context is sparse.");
        }

        if (item.Labels.Count >= 2) {
            score += 8;
            reasons.Add("Labels provide extra classification evidence.");
        } else if (item.Labels.Count == 1) {
            score += 4;
            reasons.Add("Single label provides limited evidence.");
        } else {
            score -= 5;
            reasons.Add("No labels present.");
        }

        if (item.PullRequest is not null) {
            if (item.PullRequest.ChangedFiles > 0) {
                score += 4;
                reasons.Add("PR change metadata is present.");
            } else {
                score -= 6;
                reasons.Add("PR change metadata is missing.");
            }

            if (!string.IsNullOrWhiteSpace(item.PullRequest.ReviewDecision)) {
                score += 4;
            } else {
                score -= 3;
            }

            if (!string.IsNullOrWhiteSpace(item.PullRequest.StatusCheckState)) {
                score += 4;
            } else {
                score -= 3;
            }

            if (item.PullRequest.Commits > 0) {
                score += 3;
            } else {
                score -= 2;
            }
        } else if (item.Issue is not null) {
            if (item.Issue.Comments >= 2) {
                score += 6;
                reasons.Add("Issue discussion provides additional context.");
            } else if (item.Issue.Comments == 0) {
                score -= 3;
                reasons.Add("Issue has no discussion context.");
            }
        }

        if (enrichment is not null) {
            if (enrichment.CategoryConfidence >= 0.75) {
                score += 8;
                reasons.Add("Category confidence is high.");
            } else if (enrichment.CategoryConfidence >= 0.60) {
                score += 4;
            } else if (enrichment.CategoryConfidence < 0.50) {
                score -= 5;
                reasons.Add("Category confidence is low.");
            }

            var topMatchConfidence = item.Kind.Equals("pull_request", StringComparison.OrdinalIgnoreCase)
                ? enrichment.MatchedIssueConfidence
                : enrichment.MatchedPullRequestConfidence;
            if (topMatchConfidence.HasValue) {
                if (topMatchConfidence.Value >= 0.80) {
                    score += 8;
                    reasons.Add("Top related-link confidence is high.");
                } else if (topMatchConfidence.Value >= 0.55) {
                    score += 4;
                }
            }
        }

        score = Math.Round(Math.Clamp(score, 0, 100), 2, MidpointRounding.AwayFromZero);
        var level = score >= 75
            ? "high"
            : score >= 50
                ? "medium"
                : "low";

        return new SignalQualityAssessment(level, score, reasons);
    }

    internal static PullRequestOperationalSignals? AssessPullRequestOperationalSignals(
        TriageIndexItem item,
        DateTimeOffset nowUtc) {
        if (item.PullRequest is null ||
            !item.Kind.Equals("pull_request", StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        var signals = item.PullRequest;
        var changedFiles = Math.Max(0, signals.ChangedFiles);
        var changeVolume = Math.Max(0, signals.Additions) + Math.Max(0, signals.Deletions);
        var ageDays = Math.Max(0, (nowUtc - item.UpdatedAtUtc).TotalDays);

        var sizeBand = (changedFiles, changeVolume) switch {
            (<= 3, <= 80) => "xsmall",
            (<= 10, <= 300) => "small",
            (<= 30, <= 900) => "medium",
            (<= 80, <= 2500) => "large",
            _ => "xlarge"
        };

        var blocked = signals.IsDraft ||
                      signals.Mergeable.Equals("CONFLICTING", StringComparison.OrdinalIgnoreCase) ||
                      signals.Mergeable.Equals("UNKNOWN", StringComparison.OrdinalIgnoreCase) ||
                      signals.ReviewDecision.Equals("CHANGES_REQUESTED", StringComparison.OrdinalIgnoreCase) ||
                      signals.StatusCheckState.Equals("FAILURE", StringComparison.OrdinalIgnoreCase) ||
                      signals.StatusCheckState.Equals("ERROR", StringComparison.OrdinalIgnoreCase);

        var mergeReadiness = blocked
            ? "blocked"
            : signals.Mergeable.Equals("MERGEABLE", StringComparison.OrdinalIgnoreCase) &&
              signals.ReviewDecision.Equals("APPROVED", StringComparison.OrdinalIgnoreCase) &&
              signals.StatusCheckState.Equals("SUCCESS", StringComparison.OrdinalIgnoreCase)
                ? "ready"
                : "needs-review";

        var churnRisk = changedFiles >= ChurnHighChangedFilesThreshold ||
                        changeVolume >= ChurnHighChangeVolumeThreshold ||
                        signals.Comments >= ChurnHighCommentsThreshold ||
                        signals.Commits >= ChurnHighCommitsThreshold
            ? "high"
            : changedFiles >= ChurnMediumChangedFilesThreshold ||
              changeVolume >= ChurnMediumChangeVolumeThreshold ||
              signals.Comments >= ChurnMediumCommentsThreshold ||
              signals.Commits >= ChurnMediumCommitsThreshold
                ? "medium"
                : "low";
        if ((signals.Mergeable.Equals("CONFLICTING", StringComparison.OrdinalIgnoreCase) ||
             signals.Mergeable.Equals("UNKNOWN", StringComparison.OrdinalIgnoreCase)) &&
            churnRisk.Equals("low", StringComparison.OrdinalIgnoreCase)) {
            churnRisk = "medium";
        }

        var freshness = ageDays switch {
            <= 1 => "fresh",
            <= 7 => "recent",
            <= 30 => "aging",
            _ => "stale"
        };

        var checkHealth = signals.StatusCheckState.Trim().ToUpperInvariant() switch {
            "SUCCESS" => "healthy",
            "NEUTRAL" => "healthy",
            "SKIPPED" => "healthy",
            "PENDING" => "pending",
            "EXPECTED" => "pending",
            "IN_PROGRESS" => "pending",
            "QUEUED" => "pending",
            "FAILURE" => "failing",
            "ERROR" => "failing",
            "CANCELLED" => "failing",
            "TIMED_OUT" => "failing",
            "ACTION_REQUIRED" => "failing",
            "STARTUP_FAILURE" => "failing",
            _ => "unknown"
        };

        var reviewLatency = ageDays switch {
            <= ReviewLatencyLowAgeDaysThreshold => "low",
            <= ReviewLatencyMediumAgeDaysThreshold => "medium",
            _ => "high"
        };
        if (mergeReadiness.Equals("ready", StringComparison.OrdinalIgnoreCase)) {
            reviewLatency = ageDays switch {
                <= ReadyReviewLatencyLowAgeDaysThreshold => "low",
                <= ReadyReviewLatencyMediumAgeDaysThreshold => "medium",
                _ => "high"
            };
        } else if (mergeReadiness.Equals("blocked", StringComparison.OrdinalIgnoreCase) &&
                   reviewLatency.Equals("low", StringComparison.OrdinalIgnoreCase) &&
                   ageDays > 2) {
            reviewLatency = "medium";
        }
        if (checkHealth.Equals("pending", StringComparison.OrdinalIgnoreCase) &&
            ageDays > PendingReviewLatencyHighAgeDaysThreshold) {
            reviewLatency = "high";
        }
        if ((signals.Comments >= ChurnHighCommentsThreshold || signals.Commits >= ChurnHighCommitsThreshold) &&
            !reviewLatency.Equals("high", StringComparison.OrdinalIgnoreCase)) {
            reviewLatency = "high";
        } else if ((signals.Comments >= ChurnMediumCommentsThreshold || signals.Commits >= ChurnMediumCommitsThreshold) &&
                   reviewLatency.Equals("low", StringComparison.OrdinalIgnoreCase)) {
            reviewLatency = "medium";
        }

        var mergeConflictRisk = signals.Mergeable.Trim().ToUpperInvariant() switch {
            "CONFLICTING" => "high",
            "UNKNOWN" => sizeBand is "xlarge" ||
                         churnRisk.Equals("high", StringComparison.OrdinalIgnoreCase) ||
                         ageDays > ConflictRiskHighAgeDaysThreshold
                ? "high"
                : sizeBand is "large" ||
                  ageDays > 7 ||
                  checkHealth is "pending" or "failing"
                    ? "medium"
                    : "low",
            "MERGEABLE" => sizeBand is "xlarge" &&
                           churnRisk.Equals("high", StringComparison.OrdinalIgnoreCase) &&
                           ageDays > ConflictRiskHighAgeDaysThreshold
                ? "high"
                : sizeBand is "large" or "xlarge" ||
                  churnRisk is "medium" or "high" ||
                  ageDays > ConflictRiskMediumAgeDaysThreshold ||
                  checkHealth is "pending" or "failing"
                    ? "medium"
                    : "low",
            _ => sizeBand is "xlarge" || churnRisk.Equals("high", StringComparison.OrdinalIgnoreCase)
                ? "high"
                : sizeBand is "large" || ageDays > 10
                    ? "medium"
                    : "low"
        };

        return new PullRequestOperationalSignals(sizeBand, churnRisk, mergeReadiness, freshness, checkHealth, reviewLatency,
            mergeConflictRisk);
    }

    internal static CategoryTagInference InferCategoryAndTagsWithConfidence(TriageIndexItem item) {
        var tokens = new HashSet<string>(item.ContextTokens, StringComparer.OrdinalIgnoreCase);
        var titleTokens = new HashSet<string>(item.TitleTokens, StringComparer.OrdinalIgnoreCase);
        var labelTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in item.TitleTokens) {
            tokens.Add(token);
        }

        foreach (var label in item.Labels) {
            foreach (var token in Tokenize(label)) {
                tokens.Add(token);
                labelTokens.Add(token);
            }
        }

        static int CountMatches(HashSet<string> source, IReadOnlyList<string> candidates) {
            var matches = 0;
            foreach (var candidate in candidates) {
                if (source.Contains(candidate)) {
                    matches++;
                }
            }
            return matches;
        }

        var securityTokens = new[] { "security", "vulnerability", "vulnerabilities", "auth", "authorization", "xss", "injection", "cve", "secret", "secrets" };
        var bugTokens = new[] { "bug", "bugs", "error", "errors", "failure", "failures", "exception", "exceptions", "crash", "regression", "defect", "defects" };
        var performanceTokens = new[] { "performance", "perf", "latency", "throughput", "memory", "cpu" };
        var docsTokens = new[] { "docs", "doc", "documentation", "readme", "wiki", "changelog" };
        var testingTokens = new[] { "test", "tests", "testing", "unittest", "integration", "e2e" };
        var ciTokens = new[] { "ci", "pipeline", "workflows", "workflow", "actions", "github", "build" };
        var maintenanceTokens = new[] { "refactor", "cleanup", "chore", "maintenance", "bump", "upgrade", "dependency", "dependencies", "deps" };
        var featureTokens = new[] { "feature", "features", "enhancement", "enhancements" };
        var apiTokens = new[] { "api", "apis" };
        var uxTokens = new[] { "ui", "ux", "frontend", "website" };
        var dependencyTokens = new[] { "dependency", "dependencies", "deps", "nuget", "package", "packages" };

        var securityMatches = CountMatches(tokens, securityTokens);
        var bugMatches = CountMatches(tokens, bugTokens);
        var performanceMatches = CountMatches(tokens, performanceTokens);
        var docsMatches = CountMatches(tokens, docsTokens);
        var testingMatches = CountMatches(tokens, testingTokens);
        var ciMatches = CountMatches(tokens, ciTokens);
        var maintenanceMatches = CountMatches(tokens, maintenanceTokens);
        var featureMatches = CountMatches(tokens, featureTokens);
        var apiMatches = CountMatches(tokens, apiTokens);
        var uxMatches = CountMatches(tokens, uxTokens);
        var dependencyMatches = CountMatches(tokens, dependencyTokens);

        var isSecurity = securityMatches > 0;
        var isBug = bugMatches > 0;
        var isPerformance = performanceMatches > 0;
        var isDocs = docsMatches > 0;
        var isTesting = testingMatches > 0;
        var isCi = ciMatches > 0;
        var isMaintenance = maintenanceMatches > 0;

        var category = isSecurity ? "security"
            : isBug ? "bug"
            : isPerformance ? "performance"
            : isDocs ? "documentation"
            : isTesting ? "testing"
            : isCi ? "ci"
            : isMaintenance ? "maintenance"
            : featureMatches > 0 ? "feature"
            : "feature";

        double ComputeConfidence(int matchCount, bool hasLabelEvidence, bool hasTitleEvidence, double baseConfidence) {
            var confidence = baseConfidence;
            if (matchCount > 0) {
                confidence += 0.08;
                confidence += Math.Min(0.16, (matchCount - 1) * 0.04);
            }
            if (hasLabelEvidence) {
                confidence += 0.10;
            }
            if (hasTitleEvidence) {
                confidence += 0.07;
            }

            return Math.Round(Math.Clamp(confidence, 0.35, 0.98), 2, MidpointRounding.AwayFromZero);
        }

        int ResolveCategoryMatches() {
            return category switch {
                "security" => securityMatches,
                "bug" => bugMatches,
                "performance" => performanceMatches,
                "documentation" => docsMatches,
                "testing" => testingMatches,
                "ci" => ciMatches,
                "maintenance" => maintenanceMatches,
                _ => featureMatches
            };
        }

        IReadOnlyList<string> ResolveCategoryCandidates() {
            return category switch {
                "security" => securityTokens,
                "bug" => bugTokens,
                "performance" => performanceTokens,
                "documentation" => docsTokens,
                "testing" => testingTokens,
                "ci" => ciTokens,
                "maintenance" => maintenanceTokens,
                _ => featureTokens
            };
        }

        var categoryCandidates = ResolveCategoryCandidates();
        var categoryConfidence = ComputeConfidence(
            ResolveCategoryMatches(),
            CountMatches(labelTokens, categoryCandidates) > 0,
            CountMatches(titleTokens, categoryCandidates) > 0,
            category.Equals("feature", StringComparison.OrdinalIgnoreCase) ? 0.48 : 0.60);

        var tags = new List<string>();
        var tagConfidenceByName = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        void AddTag(string tag, int matchCount, IReadOnlyList<string> evidenceTokens, double baseConfidence) {
            tags.Add(tag);
            var confidence = ComputeConfidence(
                matchCount,
                CountMatches(labelTokens, evidenceTokens) > 0,
                CountMatches(titleTokens, evidenceTokens) > 0,
                baseConfidence);

            if (tagConfidenceByName.TryGetValue(tag, out var existing)) {
                tagConfidenceByName[tag] = Math.Max(existing, confidence);
            } else {
                tagConfidenceByName[tag] = confidence;
            }
        }

        if (isSecurity) {
            AddTag("security", securityMatches, securityTokens, 0.58);
        }
        if (isBug) {
            AddTag("bugfix", bugMatches, bugTokens, 0.58);
        }
        if (isPerformance) {
            AddTag("performance", performanceMatches, performanceTokens, 0.58);
        }
        if (isDocs) {
            AddTag("docs", docsMatches, docsTokens, 0.58);
        }
        if (isTesting) {
            AddTag("testing", testingMatches, testingTokens, 0.58);
        }
        if (isCi) {
            AddTag("ci", ciMatches, ciTokens, 0.58);
        }
        if (isMaintenance) {
            AddTag("maintenance", maintenanceMatches, maintenanceTokens, 0.58);
        }
        if (apiMatches > 0) {
            AddTag("api", apiMatches, apiTokens, 0.56);
        }
        if (uxMatches > 0) {
            AddTag("ux", uxMatches, uxTokens, 0.56);
        }
        if (dependencyMatches > 0) {
            AddTag("dependencies", dependencyMatches, dependencyTokens, 0.56);
        }
        if (tags.Count == 0) {
            AddTag(category, ResolveCategoryMatches(), categoryCandidates, Math.Max(0.44, categoryConfidence - 0.08));
        }

        var normalizedTags = tags
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();
        var normalizedTagConfidences = normalizedTags
            .ToDictionary(
                tag => tag,
                tag => tagConfidenceByName.TryGetValue(tag, out var confidence) ? confidence : 0.50,
                StringComparer.OrdinalIgnoreCase);

        return new CategoryTagInference(
            Category: category,
            CategoryConfidence: categoryConfidence,
            Tags: normalizedTags,
            TagConfidences: normalizedTagConfidences
        );
    }

    internal static IReadOnlyList<RelatedIssueCandidate> MatchPullRequestToIssues(
        string repo,
        string prTitle,
        string prBody,
        IReadOnlyList<TriageIndexItem> issueItems) {
        if (issueItems.Count == 0) {
            return Array.Empty<RelatedIssueCandidate>();
        }

        var (owner, name) = SplitRepo(repo);
        var issueByNumber = issueItems.ToDictionary(item => item.Number);
        var matchesByNumber = new Dictionary<int, RelatedIssueCandidate>();

        foreach (var issueHint in ParseExplicitIssueReferences(prTitle, prBody, owner, name)) {
            if (!issueByNumber.TryGetValue(issueHint.Number, out var issue)) {
                continue;
            }

            matchesByNumber[issue.Number] = new RelatedIssueCandidate(
                Number: issue.Number,
                Url: issue.Url,
                Confidence: issueHint.Confidence,
                Reason: issueHint.Reason
            );
        }

        var prTitleTokens = Tokenize(prTitle);
        var prContextTokens = Tokenize($"{prTitle}\n{prBody}");

        foreach (var issue in issueItems) {
            if (matchesByNumber.ContainsKey(issue.Number)) {
                continue;
            }

            var titleScore = Jaccard(prTitleTokens, issue.TitleTokens);
            var contextScore = Jaccard(prContextTokens, issue.ContextTokens);
            var blended = Math.Round((titleScore * 0.55) + (contextScore * 0.45), 4, MidpointRounding.AwayFromZero);
            if (blended < 0.34) {
                continue;
            }

            var reason = $"token similarity title={titleScore.ToString("0.00", CultureInfo.InvariantCulture)}, context={contextScore.ToString("0.00", CultureInfo.InvariantCulture)}";
            matchesByNumber[issue.Number] = new RelatedIssueCandidate(
                Number: issue.Number,
                Url: issue.Url,
                Confidence: blended,
                Reason: reason
            );
        }

        return matchesByNumber.Values
            .OrderByDescending(match => match.Confidence)
            .ThenBy(match => match.Number)
            .Take(3)
            .ToList();
    }

    internal static IReadOnlyList<RelatedPullRequestCandidate> MatchIssueToPullRequests(
        string repo,
        string issueTitle,
        string issueBody,
        IReadOnlyList<TriageIndexItem> pullRequestItems) {
        if (pullRequestItems.Count == 0) {
            return Array.Empty<RelatedPullRequestCandidate>();
        }

        var (owner, name) = SplitRepo(repo);
        var pullRequestByNumber = pullRequestItems.ToDictionary(item => item.Number);
        var matchesByNumber = new Dictionary<int, RelatedPullRequestCandidate>();

        foreach (var pullRequestHint in ParseExplicitPullRequestReferences(issueTitle, issueBody, owner, name)) {
            if (!pullRequestByNumber.TryGetValue(pullRequestHint.Number, out var pullRequest)) {
                continue;
            }

            matchesByNumber[pullRequest.Number] = new RelatedPullRequestCandidate(
                Number: pullRequest.Number,
                Url: pullRequest.Url,
                Confidence: pullRequestHint.Confidence,
                Reason: pullRequestHint.Reason
            );
        }

        var issueTitleTokens = Tokenize(issueTitle);
        var issueContextTokens = Tokenize($"{issueTitle}\n{issueBody}");

        foreach (var pullRequest in pullRequestItems) {
            if (matchesByNumber.ContainsKey(pullRequest.Number)) {
                continue;
            }

            var titleScore = Jaccard(issueTitleTokens, pullRequest.TitleTokens);
            var contextScore = Jaccard(issueContextTokens, pullRequest.ContextTokens);
            var blended = Math.Round((titleScore * 0.55) + (contextScore * 0.45), 4, MidpointRounding.AwayFromZero);
            if (blended < 0.34) {
                continue;
            }

            var reason = $"token similarity title={titleScore.ToString("0.00", CultureInfo.InvariantCulture)}, context={contextScore.ToString("0.00", CultureInfo.InvariantCulture)}";
            matchesByNumber[pullRequest.Number] = new RelatedPullRequestCandidate(
                Number: pullRequest.Number,
                Url: pullRequest.Url,
                Confidence: blended,
                Reason: reason
            );
        }

        return matchesByNumber.Values
            .OrderByDescending(match => match.Confidence)
            .ThenBy(match => match.Number)
            .Take(3)
            .ToList();
    }

    private static IReadOnlyList<IssueReferenceHint> ParseExplicitIssueReferences(
        string prTitle,
        string prBody,
        string owner,
        string repoName) {
        var text = $"{prTitle}\n{prBody}";
        var results = new Dictionary<int, IssueReferenceHint>();

        static void AddHint(
            IDictionary<int, IssueReferenceHint> map,
            int number,
            double confidence,
            string reason) {
            if (!map.TryGetValue(number, out var existing) ||
                confidence > existing.Confidence) {
                map[number] = new IssueReferenceHint(number, confidence, reason);
            }
        }

        foreach (Match match in ExplicitIssueRef.Matches(text)) {
            if (TryReadIssueNumber(match, out var number)) {
                AddHint(results, number, 0.98, "explicit issue reference in PR title/body");
            }
        }

        foreach (Match match in ExplicitRepoIssueRef.Matches(text)) {
            var refOwner = match.Groups["owner"].Value;
            var refRepo = match.Groups["repo"].Value;
            if (!refOwner.Equals(owner, StringComparison.OrdinalIgnoreCase) ||
                !refRepo.Equals(repoName, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            if (TryReadIssueNumber(match, out var number)) {
                AddHint(results, number, 0.98, "explicit issue reference in PR title/body");
            }
        }

        foreach (Match match in ExplicitIssueUrlRef.Matches(text)) {
            var refOwner = match.Groups["owner"].Value;
            var refRepo = match.Groups["repo"].Value;
            if (!refOwner.Equals(owner, StringComparison.OrdinalIgnoreCase) ||
                !refRepo.Equals(repoName, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            if (TryReadIssueNumber(match, out var number)) {
                AddHint(results, number, 0.98, "explicit issue reference in PR title/body");
            }
        }

        foreach (Match match in DirectIssueRef.Matches(text)) {
            if (TryReadIssueNumber(match, out var number)) {
                AddHint(results, number, 0.92, "direct issue reference in PR title/body");
            }
        }

        foreach (Match match in DirectRepoIssueRef.Matches(text)) {
            var refOwner = match.Groups["owner"].Value;
            var refRepo = match.Groups["repo"].Value;
            if (!refOwner.Equals(owner, StringComparison.OrdinalIgnoreCase) ||
                !refRepo.Equals(repoName, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            if (TryReadIssueNumber(match, out var number)) {
                AddHint(results, number, 0.92, "direct issue reference in PR title/body");
            }
        }

        foreach (Match match in DirectIssueUrlRef.Matches(text)) {
            var refOwner = match.Groups["owner"].Value;
            var refRepo = match.Groups["repo"].Value;
            if (!refOwner.Equals(owner, StringComparison.OrdinalIgnoreCase) ||
                !refRepo.Equals(repoName, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            if (TryReadIssueNumber(match, out var number)) {
                AddHint(results, number, 0.92, "direct issue reference in PR title/body");
            }
        }

        foreach (Match match in BareIssueUrlRef.Matches(text)) {
            var refOwner = match.Groups["owner"].Value;
            var refRepo = match.Groups["repo"].Value;
            if (!refOwner.Equals(owner, StringComparison.OrdinalIgnoreCase) ||
                !refRepo.Equals(repoName, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            if (TryReadIssueNumber(match, out var number)) {
                AddHint(results, number, 0.90, "issue URL reference in PR title/body");
            }
        }

        return results.Values
            .OrderByDescending(value => value.Confidence)
            .ThenBy(value => value.Number)
            .ToList();
    }

    private static IReadOnlyList<PullRequestReferenceHint> ParseExplicitPullRequestReferences(
        string issueTitle,
        string issueBody,
        string owner,
        string repoName) {
        var text = $"{issueTitle}\n{issueBody}";
        var results = new Dictionary<int, PullRequestReferenceHint>();

        static void AddHint(
            IDictionary<int, PullRequestReferenceHint> map,
            int number,
            double confidence,
            string reason) {
            if (!map.TryGetValue(number, out var existing) ||
                confidence > existing.Confidence) {
                map[number] = new PullRequestReferenceHint(number, confidence, reason);
            }
        }

        foreach (Match match in ExplicitPullRequestRef.Matches(text)) {
            if (TryReadPullRequestNumber(match, out var number)) {
                AddHint(results, number, 0.98, "explicit pull request reference in issue title/body");
            }
        }

        foreach (Match match in DirectPullRequestRef.Matches(text)) {
            if (TryReadPullRequestNumber(match, out var number)) {
                AddHint(results, number, 0.93, "direct pull request reference in issue title/body");
            }
        }

        foreach (Match match in DirectRepoPullRequestRef.Matches(text)) {
            var refOwner = match.Groups["owner"].Value;
            var refRepo = match.Groups["repo"].Value;
            if (!refOwner.Equals(owner, StringComparison.OrdinalIgnoreCase) ||
                !refRepo.Equals(repoName, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            if (TryReadPullRequestNumber(match, out var number)) {
                AddHint(results, number, 0.93, "direct pull request reference in issue title/body");
            }
        }

        foreach (Match match in DirectPullRequestUrlRef.Matches(text)) {
            var refOwner = match.Groups["owner"].Value;
            var refRepo = match.Groups["repo"].Value;
            if (!refOwner.Equals(owner, StringComparison.OrdinalIgnoreCase) ||
                !refRepo.Equals(repoName, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            if (TryReadPullRequestNumber(match, out var number)) {
                AddHint(results, number, 0.93, "direct pull request reference in issue title/body");
            }
        }

        foreach (Match match in BarePullRequestUrlRef.Matches(text)) {
            var refOwner = match.Groups["owner"].Value;
            var refRepo = match.Groups["repo"].Value;
            if (!refOwner.Equals(owner, StringComparison.OrdinalIgnoreCase) ||
                !refRepo.Equals(repoName, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            if (TryReadPullRequestNumber(match, out var number)) {
                AddHint(results, number, 0.90, "pull request URL reference in issue title/body");
            }
        }

        return results.Values
            .OrderByDescending(value => value.Confidence)
            .ThenBy(value => value.Number)
            .ToList();
    }

    private static bool TryReadIssueNumber(Match match, out int number) {
        number = 0;
        return match.Groups["num"].Success &&
               int.TryParse(match.Groups["num"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number) &&
               number > 0;
    }

    private static bool TryReadPullRequestNumber(Match match, out int number) {
        number = 0;
        return match.Groups["num"].Success &&
               int.TryParse(match.Groups["num"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number) &&
               number > 0;
    }

    internal static string NormalizeText(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return string.Empty;
        }
        var lowered = value.ToLowerInvariant();
        var sb = new StringBuilder(lowered.Length);
        var inWhitespace = false;
        foreach (var c in lowered) {
            if (char.IsLetterOrDigit(c) || c == '_' || c == '-') {
                sb.Append(c);
                inWhitespace = false;
                continue;
            }
            if (char.IsWhiteSpace(c)) {
                if (!inWhitespace) {
                    sb.Append(' ');
                    inWhitespace = true;
                }
                continue;
            }
            if (!inWhitespace) {
                sb.Append(' ');
                inWhitespace = true;
            }
        }
        return sb.ToString().Trim();
    }

    internal static IReadOnlyList<string> Tokenize(string? value) {
        var normalized = NormalizeText(value);
        if (string.IsNullOrWhiteSpace(normalized)) {
            return Array.Empty<string>();
        }
        var tokens = normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length > 1 && !StopWords.Contains(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(token => token, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return tokens;
    }

    internal static double Jaccard(IReadOnlyList<string> left, IReadOnlyList<string> right) {
        if (left.Count == 0 && right.Count == 0) {
            return 1.0;
        }
        if (left.Count == 0 || right.Count == 0) {
            return 0.0;
        }

        var leftSet = new HashSet<string>(left, StringComparer.OrdinalIgnoreCase);
        var rightSet = new HashSet<string>(right, StringComparer.OrdinalIgnoreCase);
        var union = new HashSet<string>(leftSet, StringComparer.OrdinalIgnoreCase);
        union.UnionWith(rightSet);
        if (union.Count == 0) {
            return 0.0;
        }
        leftSet.IntersectWith(rightSet);
        return Math.Round(leftSet.Count / (double)union.Count, 4, MidpointRounding.AwayFromZero);
    }

    private static bool IsLikelyPrefixTitle(TriageIndexItem left, TriageIndexItem right) {
        if (string.IsNullOrWhiteSpace(left.NormalizedTitle) || string.IsNullOrWhiteSpace(right.NormalizedTitle)) {
            return false;
        }
        return left.NormalizedTitle.Contains(right.NormalizedTitle, StringComparison.Ordinal) ||
               right.NormalizedTitle.Contains(left.NormalizedTitle, StringComparison.Ordinal);
    }

    private static double ComputeDuplicateScore(TriageIndexItem left, TriageIndexItem right) {
        if (string.Equals(left.Id, right.Id, StringComparison.OrdinalIgnoreCase)) {
            return 1.0;
        }
        if (!string.IsNullOrWhiteSpace(left.NormalizedTitle) &&
            left.NormalizedTitle.Equals(right.NormalizedTitle, StringComparison.OrdinalIgnoreCase)) {
            return 1.0;
        }

        var titleScore = Jaccard(left.TitleTokens, right.TitleTokens);
        var contextScore = Jaccard(left.ContextTokens, right.ContextTokens);
        var blended = Math.Round((titleScore * 0.80) + (contextScore * 0.20), 4, MidpointRounding.AwayFromZero);

        if (IsLikelyPrefixTitle(left, right) && titleScore >= 0.55) {
            blended = Math.Max(blended, 0.86);
        }

        if (Math.Min(left.TitleTokens.Count, right.TitleTokens.Count) < 3 && titleScore < 0.95) {
            blended = Math.Min(blended, 0.79);
        }
        return blended;
    }

    internal static IReadOnlyList<DuplicateCluster> BuildDuplicateClusters(IReadOnlyList<TriageIndexItem> items, double threshold) {
        if (items.Count < 2) {
            return Array.Empty<DuplicateCluster>();
        }

        var normalizedThreshold = Math.Clamp(threshold, 0.50, 1.0);
        var parent = Enumerable.Range(0, items.Count).ToArray();
        var rank = new int[items.Count];
        var pairScores = new Dictionary<(int A, int B), double>();

        int Find(int i) {
            if (parent[i] != i) {
                parent[i] = Find(parent[i]);
            }
            return parent[i];
        }

        void Union(int a, int b) {
            var rootA = Find(a);
            var rootB = Find(b);
            if (rootA == rootB) {
                return;
            }
            if (rank[rootA] < rank[rootB]) {
                parent[rootA] = rootB;
                return;
            }
            if (rank[rootA] > rank[rootB]) {
                parent[rootB] = rootA;
                return;
            }
            parent[rootB] = rootA;
            rank[rootA]++;
        }

        for (var i = 0; i < items.Count; i++) {
            for (var j = i + 1; j < items.Count; j++) {
                var score = ComputeDuplicateScore(items[i], items[j]);
                if (score >= normalizedThreshold) {
                    Union(i, j);
                    pairScores[(i, j)] = score;
                }
            }
        }

        var groups = new Dictionary<int, List<int>>();
        for (var i = 0; i < items.Count; i++) {
            var root = Find(i);
            if (!groups.TryGetValue(root, out var list)) {
                list = new List<int>();
                groups[root] = list;
            }
            list.Add(i);
        }

        var clusterIndex = 0;
        var results = new List<DuplicateCluster>();
        foreach (var memberIndexes in groups.Values.Where(group => group.Count > 1)) {
            clusterIndex++;
            var confidence = 0.0;
            foreach (var first in memberIndexes) {
                foreach (var second in memberIndexes) {
                    if (first >= second) {
                        continue;
                    }
                    if (pairScores.TryGetValue((first, second), out var score) ||
                        pairScores.TryGetValue((second, first), out score)) {
                        confidence = Math.Max(confidence, score);
                    }
                }
            }

            var members = memberIndexes
                .Select(index => items[index])
                .OrderByDescending(item => item.Kind == "pull_request")
                .ThenByDescending(item => item.UpdatedAtUtc)
                .ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var canonical = members[0];
            results.Add(new DuplicateCluster(
                Id: $"cluster-{clusterIndex:000}",
                Confidence: Math.Round(confidence, 4, MidpointRounding.AwayFromZero),
                CanonicalItemId: canonical.Id,
                ItemIds: members.Select(member => member.Id).ToList(),
                Reason: $"token similarity >= {normalizedThreshold.ToString("0.00", CultureInfo.InvariantCulture)}"
            ));
        }

        return results
            .OrderByDescending(cluster => cluster.Confidence)
            .ThenBy(cluster => cluster.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static Dictionary<string, string> BuildClusterLookup(IReadOnlyList<DuplicateCluster> clusters) {
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var cluster in clusters) {
            foreach (var itemId in cluster.ItemIds) {
                lookup[itemId] = cluster.Id;
            }
        }
        return lookup;
    }

    internal static double ScorePullRequest(TriageIndexItem item, DateTimeOffset nowUtc, out List<string> reasons) {
        reasons = new List<string>();
        if (item.PullRequest is null) {
            reasons.Add("Not a pull request.");
            return 0;
        }

        var signals = item.PullRequest;
        var score = 50.0;

        if (signals.IsDraft) {
            score -= 20;
            reasons.Add("Draft PR penalty.");
        } else {
            score += 4;
            reasons.Add("Ready-for-review bonus.");
        }

        if (signals.Mergeable.Equals("MERGEABLE", StringComparison.OrdinalIgnoreCase)) {
            score += 12;
            reasons.Add("Mergeable status bonus.");
        } else if (signals.Mergeable.Equals("CONFLICTING", StringComparison.OrdinalIgnoreCase)) {
            score -= 12;
            reasons.Add("Conflicting branch penalty.");
        } else {
            score -= 4;
            reasons.Add("Unknown mergeability penalty.");
        }

        if (signals.ReviewDecision.Equals("APPROVED", StringComparison.OrdinalIgnoreCase)) {
            score += 15;
            reasons.Add("Approved review decision bonus.");
        } else if (signals.ReviewDecision.Equals("CHANGES_REQUESTED", StringComparison.OrdinalIgnoreCase)) {
            score -= 15;
            reasons.Add("Changes requested penalty.");
        } else if (signals.ReviewDecision.Equals("REVIEW_REQUIRED", StringComparison.OrdinalIgnoreCase)) {
            score -= 6;
            reasons.Add("Review required penalty.");
        }

        if (signals.StatusCheckState.Equals("SUCCESS", StringComparison.OrdinalIgnoreCase)) {
            score += 8;
            reasons.Add("Status checks success bonus.");
        } else if (signals.StatusCheckState.Equals("FAILURE", StringComparison.OrdinalIgnoreCase) ||
                   signals.StatusCheckState.Equals("ERROR", StringComparison.OrdinalIgnoreCase)) {
            score -= 12;
            reasons.Add("Failing status checks penalty.");
        } else if (signals.StatusCheckState.Equals("PENDING", StringComparison.OrdinalIgnoreCase) ||
                   signals.StatusCheckState.Equals("EXPECTED", StringComparison.OrdinalIgnoreCase)) {
            score -= 4;
            reasons.Add("Pending status checks penalty.");
        } else if (!string.IsNullOrWhiteSpace(signals.StatusCheckState)) {
            score -= 2;
            reasons.Add($"Unknown status check state penalty ({signals.StatusCheckState}).");
        }

        var changeVolume = Math.Max(0, signals.Additions) + Math.Max(0, signals.Deletions);
        if (signals.ChangedFiles > 200) {
            score -= 16;
            reasons.Add("Large changed-file count penalty (>200).");
        } else if (signals.ChangedFiles > 80) {
            score -= 10;
            reasons.Add("Large changed-file count penalty (>80).");
        } else if (signals.ChangedFiles > 30) {
            score -= 5;
            reasons.Add("Medium changed-file count penalty (>30).");
        } else {
            score += 2;
            reasons.Add("Focused change-set bonus.");
        }

        if (changeVolume > 5000) {
            score -= 12;
            reasons.Add("Very high churn penalty (>5000 lines).");
        } else if (changeVolume > 2000) {
            score -= 7;
            reasons.Add("High churn penalty (>2000 lines).");
        } else if (changeVolume > 800) {
            score -= 3;
            reasons.Add("Moderate churn penalty (>800 lines).");
        } else {
            score += 3;
            reasons.Add("Low churn bonus.");
        }

        if (signals.Commits > 40) {
            score -= 5;
            reasons.Add("Many commits penalty (>40).");
        }

        if (signals.Comments > 25) {
            score -= 4;
            reasons.Add("High discussion load penalty (>25 comments).");
        } else if (signals.Comments == 0) {
            score += 1;
            reasons.Add("No outstanding discussion bonus.");
        }

        if (item.TitleTokens.Count < 3) {
            score -= 6;
            reasons.Add("Sparse-title confidence penalty.");
        }

        if (item.ContextTokens.Count < 10) {
            score -= 8;
            reasons.Add("Sparse-description confidence penalty.");
        }

        var ageDays = Math.Max(0, (nowUtc - item.UpdatedAtUtc).TotalDays);
        if (ageDays <= 1) {
            score += 8;
            reasons.Add("Fresh activity bonus (<=1 day).");
        } else if (ageDays <= 3) {
            score += 5;
            reasons.Add("Recent activity bonus (<=3 days).");
        } else if (ageDays <= 7) {
            score += 2;
            reasons.Add("Active this week bonus.");
        } else if (ageDays > 30) {
            score -= 6;
            reasons.Add("Stale PR penalty (>30 days).");
        }

        foreach (var label in item.Labels) {
            if (label.Equals("blocked", StringComparison.OrdinalIgnoreCase) ||
                label.Equals("do-not-merge", StringComparison.OrdinalIgnoreCase)) {
                score -= 15;
                reasons.Add($"Blocking label penalty ({label}).");
            } else if (label.Equals("ready", StringComparison.OrdinalIgnoreCase) ||
                       label.Equals("ready-to-merge", StringComparison.OrdinalIgnoreCase)) {
                score += 8;
                reasons.Add($"Ready label bonus ({label}).");
            } else if (label.Equals("wip", StringComparison.OrdinalIgnoreCase)) {
                score -= 10;
                reasons.Add("WIP label penalty.");
            }
        }

        return Math.Round(Math.Clamp(score, 0, 100), 2, MidpointRounding.AwayFromZero);
    }

    private static IReadOnlyList<BestPullRequest> BuildBestPullRequests(
        IReadOnlyList<ItemWithScore> scoredItems,
        IReadOnlyList<DuplicateCluster> clusters,
        int limit) {
        var canonicalByCluster = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var clusterMap = clusters.ToDictionary(cluster => cluster.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var cluster in clusters) {
            var candidate = scoredItems
                .Where(item => item.DuplicateClusterId != null &&
                               item.DuplicateClusterId.Equals(cluster.Id, StringComparison.OrdinalIgnoreCase) &&
                               item.Item.Kind == "pull_request")
                .OrderByDescending(item => item.Score ?? 0)
                .ThenByDescending(item => item.Item.UpdatedAtUtc)
                .Select(item => item.Item.Id)
                .FirstOrDefault();
            canonicalByCluster[cluster.Id] = string.IsNullOrWhiteSpace(candidate) ? cluster.CanonicalItemId : candidate;
        }

        return scoredItems
            .Where(item => item.Item.Kind == "pull_request")
            .Where(item => item.DuplicateClusterId is null ||
                           (canonicalByCluster.TryGetValue(item.DuplicateClusterId, out var canonical) &&
                            canonical.Equals(item.Item.Id, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(item => item.Score ?? 0)
            .ThenByDescending(item => item.Item.UpdatedAtUtc)
            .Take(Math.Max(1, limit))
            .Select(item => {
                var reasons = item.ScoreReasons.ToList();
                if (item.DuplicateClusterId is not null && clusterMap.TryGetValue(item.DuplicateClusterId, out var cluster)) {
                    reasons.Add($"Cluster representative for {cluster.Id} ({cluster.ItemIds.Count} related items).");
                }
                return new BestPullRequest(
                    Id: item.Item.Id,
                    Number: item.Item.Number,
                    Title: item.Item.Title,
                    Url: item.Item.Url,
                    Score: item.Score ?? 0,
                    Reasons: reasons,
                    DuplicateClusterId: item.DuplicateClusterId
                );
            })
            .ToList();
    }

    private static object BuildReport(
        Options options,
        DateTimeOffset nowUtc,
        IReadOnlyList<ItemWithScore> scoredItems,
        IReadOnlyList<DuplicateCluster> clusters,
        IReadOnlyList<BestPullRequest> bestPullRequests,
        IReadOnlyDictionary<string, ItemEnrichment> enrichments) {
        var duplicateIds = new HashSet<string>(
            clusters.SelectMany(cluster => cluster.ItemIds),
            StringComparer.OrdinalIgnoreCase
        );
        var signalAssessmentsById = scoredItems
            .ToDictionary(
                item => item.Item.Id,
                item => AssessSignalQuality(item.Item, enrichments.TryGetValue(item.Item.Id, out var enrichment) ? enrichment : null),
                StringComparer.OrdinalIgnoreCase);
        var pullRequestOperationalSignalsById = scoredItems
            .ToDictionary(
                item => item.Item.Id,
                item => AssessPullRequestOperationalSignals(item.Item, nowUtc),
                StringComparer.OrdinalIgnoreCase);
        var highSignalCount = signalAssessmentsById.Values.Count(value => value.Level == "high");
        var mediumSignalCount = signalAssessmentsById.Values.Count(value => value.Level == "medium");
        var lowSignalCount = signalAssessmentsById.Values.Count(value => value.Level == "low");
        var pullRequestSignalValues = pullRequestOperationalSignalsById.Values
            .Where(value => value is not null)
            .Select(value => value!)
            .ToList();

        var items = scoredItems
            .OrderByDescending(item => item.Item.UpdatedAtUtc)
            .ThenBy(item => item.Item.Kind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Item.Number)
            .Select(item => {
                enrichments.TryGetValue(item.Item.Id, out var enrichment);
                signalAssessmentsById.TryGetValue(item.Item.Id, out var signalQuality);
                pullRequestOperationalSignalsById.TryGetValue(item.Item.Id, out var operationalSignals);
                return new {
                    id = item.Item.Id,
                    kind = item.Item.Kind,
                    number = item.Item.Number,
                    title = item.Item.Title,
                    url = item.Item.Url,
                    updatedAtUtc = item.Item.UpdatedAtUtc.UtcDateTime.ToString("o", CultureInfo.InvariantCulture),
                    labels = item.Item.Labels,
                    dedupeKey = string.Join("-", item.Item.TitleTokens.Take(8)),
                    duplicateClusterId = item.DuplicateClusterId,
                    category = enrichment?.Category,
                    categoryConfidence = enrichment?.CategoryConfidence,
                    tags = enrichment?.Tags ?? Array.Empty<string>(),
                    tagConfidences = enrichment?.TagConfidences ?? new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase),
                    matchedIssueUrl = enrichment?.MatchedIssueUrl,
                    matchedIssueConfidence = enrichment?.MatchedIssueConfidence,
                    matchedPullRequestUrl = enrichment?.MatchedPullRequestUrl,
                    matchedPullRequestConfidence = enrichment?.MatchedPullRequestConfidence,
                    relatedIssues = enrichment?.RelatedIssues
                        .Select(related => new {
                            number = related.Number,
                            url = related.Url,
                            confidence = related.Confidence,
                            reason = related.Reason
                        })
                        .Cast<object>()
                        .ToList() ?? new List<object>(),
                    relatedPullRequests = enrichment?.RelatedPullRequests
                        .Select(related => new {
                            number = related.Number,
                            url = related.Url,
                            confidence = related.Confidence,
                            reason = related.Reason
                        })
                        .Cast<object>()
                        .ToList() ?? new List<object>(),
                    signalQuality = signalQuality?.Level ?? "low",
                    signalQualityScore = signalQuality?.Score ?? 0,
                    signalQualityReasons = signalQuality?.Reasons ?? Array.Empty<string>(),
                    prSizeBand = operationalSignals?.SizeBand,
                    prChurnRisk = operationalSignals?.ChurnRisk,
                    prMergeReadiness = operationalSignals?.MergeReadiness,
                    prFreshness = operationalSignals?.Freshness,
                    prCheckHealth = operationalSignals?.CheckHealth,
                    prReviewLatency = operationalSignals?.ReviewLatency,
                    prMergeConflictRisk = operationalSignals?.MergeConflictRisk,
                    score = item.Score,
                    scoreReasons = item.ScoreReasons,
                    signals = new {
                        pullRequest = item.Item.PullRequest,
                        issue = item.Item.Issue
                    }
                };
            })
            .ToList();

        var duplicates = clusters.Select(cluster => new {
            id = cluster.Id,
            confidence = cluster.Confidence,
            canonicalItemId = cluster.CanonicalItemId,
            itemIds = cluster.ItemIds,
            reason = cluster.Reason
        }).ToList();

        var best = bestPullRequests.Select(candidate => new {
            id = candidate.Id,
            number = candidate.Number,
            title = candidate.Title,
            url = candidate.Url,
            score = candidate.Score,
            duplicateClusterId = candidate.DuplicateClusterId,
            reasons = candidate.Reasons
        }).ToList();

        return new {
            schema = "intelligencex.triage-index.v1",
            generatedAtUtc = nowUtc.UtcDateTime.ToString("o", CultureInfo.InvariantCulture),
            repo = options.Repo,
            settings = new {
                maxPrs = options.MaxPrs,
                maxIssues = options.MaxIssues,
                duplicateThreshold = options.DuplicateThreshold,
                bestLimit = options.BestLimit
            },
            summary = new {
                totalItems = scoredItems.Count,
                pullRequests = scoredItems.Count(item => item.Item.Kind == "pull_request"),
                issues = scoredItems.Count(item => item.Item.Kind == "issue"),
                duplicateClusters = clusters.Count,
                duplicateItems = duplicateIds.Count,
                bestPullRequestCandidates = bestPullRequests.Count,
                pullRequestsWithMatchedIssue = enrichments.Values.Count(value => !string.IsNullOrWhiteSpace(value.MatchedIssueUrl)),
                issuesWithMatchedPullRequest = enrichments.Values.Count(value => !string.IsNullOrWhiteSpace(value.MatchedPullRequestUrl)),
                signalQuality = new {
                    high = highSignalCount,
                    medium = mediumSignalCount,
                    low = lowSignalCount
                },
                pullRequestSignals = new {
                    size = new {
                        xsmall = pullRequestSignalValues.Count(value => value.SizeBand.Equals("xsmall", StringComparison.OrdinalIgnoreCase)),
                        small = pullRequestSignalValues.Count(value => value.SizeBand.Equals("small", StringComparison.OrdinalIgnoreCase)),
                        medium = pullRequestSignalValues.Count(value => value.SizeBand.Equals("medium", StringComparison.OrdinalIgnoreCase)),
                        large = pullRequestSignalValues.Count(value => value.SizeBand.Equals("large", StringComparison.OrdinalIgnoreCase)),
                        xlarge = pullRequestSignalValues.Count(value => value.SizeBand.Equals("xlarge", StringComparison.OrdinalIgnoreCase))
                    },
                    churnRisk = new {
                        low = pullRequestSignalValues.Count(value => value.ChurnRisk.Equals("low", StringComparison.OrdinalIgnoreCase)),
                        medium = pullRequestSignalValues.Count(value => value.ChurnRisk.Equals("medium", StringComparison.OrdinalIgnoreCase)),
                        high = pullRequestSignalValues.Count(value => value.ChurnRisk.Equals("high", StringComparison.OrdinalIgnoreCase))
                    },
                    mergeReadiness = new {
                        ready = pullRequestSignalValues.Count(value => value.MergeReadiness.Equals("ready", StringComparison.OrdinalIgnoreCase)),
                        needsReview = pullRequestSignalValues.Count(value => value.MergeReadiness.Equals("needs-review", StringComparison.OrdinalIgnoreCase)),
                        blocked = pullRequestSignalValues.Count(value => value.MergeReadiness.Equals("blocked", StringComparison.OrdinalIgnoreCase))
                    },
                    freshness = new {
                        fresh = pullRequestSignalValues.Count(value => value.Freshness.Equals("fresh", StringComparison.OrdinalIgnoreCase)),
                        recent = pullRequestSignalValues.Count(value => value.Freshness.Equals("recent", StringComparison.OrdinalIgnoreCase)),
                        aging = pullRequestSignalValues.Count(value => value.Freshness.Equals("aging", StringComparison.OrdinalIgnoreCase)),
                        stale = pullRequestSignalValues.Count(value => value.Freshness.Equals("stale", StringComparison.OrdinalIgnoreCase))
                    },
                    checkHealth = new {
                        healthy = pullRequestSignalValues.Count(value => value.CheckHealth.Equals("healthy", StringComparison.OrdinalIgnoreCase)),
                        pending = pullRequestSignalValues.Count(value => value.CheckHealth.Equals("pending", StringComparison.OrdinalIgnoreCase)),
                        failing = pullRequestSignalValues.Count(value => value.CheckHealth.Equals("failing", StringComparison.OrdinalIgnoreCase)),
                        unknown = pullRequestSignalValues.Count(value => value.CheckHealth.Equals("unknown", StringComparison.OrdinalIgnoreCase))
                    },
                    reviewLatency = new {
                        low = pullRequestSignalValues.Count(value => value.ReviewLatency.Equals("low", StringComparison.OrdinalIgnoreCase)),
                        medium = pullRequestSignalValues.Count(value => value.ReviewLatency.Equals("medium", StringComparison.OrdinalIgnoreCase)),
                        high = pullRequestSignalValues.Count(value => value.ReviewLatency.Equals("high", StringComparison.OrdinalIgnoreCase))
                    },
                    mergeConflictRisk = new {
                        low = pullRequestSignalValues.Count(value => value.MergeConflictRisk.Equals("low", StringComparison.OrdinalIgnoreCase)),
                        medium = pullRequestSignalValues.Count(value => value.MergeConflictRisk.Equals("medium", StringComparison.OrdinalIgnoreCase)),
                        high = pullRequestSignalValues.Count(value => value.MergeConflictRisk.Equals("high", StringComparison.OrdinalIgnoreCase))
                    }
                }
            },
            bestPullRequests = best,
            duplicateClusters = duplicates,
            items
        };
    }

    private static string BuildMarkdownSummary(
        Options options,
        DateTimeOffset nowUtc,
        IReadOnlyList<ItemWithScore> scoredItems,
        IReadOnlyList<DuplicateCluster> clusters,
        IReadOnlyList<BestPullRequest> bestPullRequests,
        IReadOnlyDictionary<string, ItemEnrichment> enrichments) {
        var duplicateIds = new HashSet<string>(
            clusters.SelectMany(cluster => cluster.ItemIds),
            StringComparer.OrdinalIgnoreCase
        );
        var signalAssessmentsById = scoredItems
            .ToDictionary(
                item => item.Item.Id,
                item => AssessSignalQuality(item.Item, enrichments.TryGetValue(item.Item.Id, out var enrichment) ? enrichment : null),
                StringComparer.OrdinalIgnoreCase);
        var pullRequestOperationalSignalsById = scoredItems
            .ToDictionary(
                item => item.Item.Id,
                item => AssessPullRequestOperationalSignals(item.Item, nowUtc),
                StringComparer.OrdinalIgnoreCase);
        var highSignalCount = signalAssessmentsById.Values.Count(value => value.Level == "high");
        var mediumSignalCount = signalAssessmentsById.Values.Count(value => value.Level == "medium");
        var lowSignalCount = signalAssessmentsById.Values.Count(value => value.Level == "low");
        var pullRequestSignalValues = pullRequestOperationalSignalsById.Values
            .Where(value => value is not null)
            .Select(value => value!)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("# IntelligenceX Triage Index");
        sb.AppendLine();
        sb.AppendLine($"- Generated: {nowUtc.UtcDateTime:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine($"- Repo: `{options.Repo}`");
        sb.AppendLine($"- Scope: {options.MaxPrs} PRs + {options.MaxIssues} issues");
        sb.AppendLine($"- Duplicate threshold: {options.DuplicateThreshold.ToString("0.00", CultureInfo.InvariantCulture)}");
        sb.AppendLine();
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine($"- Total items: {scoredItems.Count}");
        sb.AppendLine($"- PRs: {scoredItems.Count(item => item.Item.Kind == "pull_request")}");
        sb.AppendLine($"- Issues: {scoredItems.Count(item => item.Item.Kind == "issue")}");
        sb.AppendLine($"- Duplicate clusters: {clusters.Count}");
        sb.AppendLine($"- Duplicate items: {duplicateIds.Count}");
        sb.AppendLine($"- PRs with matched issue: {enrichments.Values.Count(value => !string.IsNullOrWhiteSpace(value.MatchedIssueUrl))}");
        sb.AppendLine($"- Issues with matched PR: {enrichments.Values.Count(value => !string.IsNullOrWhiteSpace(value.MatchedPullRequestUrl))}");
        sb.AppendLine($"- Signal quality (high/medium/low): {highSignalCount}/{mediumSignalCount}/{lowSignalCount}");
        sb.AppendLine(
            $"- PR size (xsmall/small/medium/large/xlarge): " +
            $"{pullRequestSignalValues.Count(value => value.SizeBand.Equals("xsmall", StringComparison.OrdinalIgnoreCase))}/" +
            $"{pullRequestSignalValues.Count(value => value.SizeBand.Equals("small", StringComparison.OrdinalIgnoreCase))}/" +
            $"{pullRequestSignalValues.Count(value => value.SizeBand.Equals("medium", StringComparison.OrdinalIgnoreCase))}/" +
            $"{pullRequestSignalValues.Count(value => value.SizeBand.Equals("large", StringComparison.OrdinalIgnoreCase))}/" +
            $"{pullRequestSignalValues.Count(value => value.SizeBand.Equals("xlarge", StringComparison.OrdinalIgnoreCase))}");
        sb.AppendLine(
            $"- PR merge readiness (ready/needs-review/blocked): " +
            $"{pullRequestSignalValues.Count(value => value.MergeReadiness.Equals("ready", StringComparison.OrdinalIgnoreCase))}/" +
            $"{pullRequestSignalValues.Count(value => value.MergeReadiness.Equals("needs-review", StringComparison.OrdinalIgnoreCase))}/" +
            $"{pullRequestSignalValues.Count(value => value.MergeReadiness.Equals("blocked", StringComparison.OrdinalIgnoreCase))}");
        sb.AppendLine(
            $"- PR check health (healthy/pending/failing/unknown): " +
            $"{pullRequestSignalValues.Count(value => value.CheckHealth.Equals("healthy", StringComparison.OrdinalIgnoreCase))}/" +
            $"{pullRequestSignalValues.Count(value => value.CheckHealth.Equals("pending", StringComparison.OrdinalIgnoreCase))}/" +
            $"{pullRequestSignalValues.Count(value => value.CheckHealth.Equals("failing", StringComparison.OrdinalIgnoreCase))}/" +
            $"{pullRequestSignalValues.Count(value => value.CheckHealth.Equals("unknown", StringComparison.OrdinalIgnoreCase))}");
        sb.AppendLine(
            $"- PR review latency (low/medium/high): " +
            $"{pullRequestSignalValues.Count(value => value.ReviewLatency.Equals("low", StringComparison.OrdinalIgnoreCase))}/" +
            $"{pullRequestSignalValues.Count(value => value.ReviewLatency.Equals("medium", StringComparison.OrdinalIgnoreCase))}/" +
            $"{pullRequestSignalValues.Count(value => value.ReviewLatency.Equals("high", StringComparison.OrdinalIgnoreCase))}");
        sb.AppendLine(
            $"- PR merge conflict risk (low/medium/high): " +
            $"{pullRequestSignalValues.Count(value => value.MergeConflictRisk.Equals("low", StringComparison.OrdinalIgnoreCase))}/" +
            $"{pullRequestSignalValues.Count(value => value.MergeConflictRisk.Equals("medium", StringComparison.OrdinalIgnoreCase))}/" +
            $"{pullRequestSignalValues.Count(value => value.MergeConflictRisk.Equals("high", StringComparison.OrdinalIgnoreCase))}");
        sb.AppendLine();
        sb.AppendLine("## Best PR Candidates");
        sb.AppendLine();

        if (bestPullRequests.Count == 0) {
            sb.AppendLine("None.");
        } else {
            var rank = 0;
            foreach (var candidate in bestPullRequests) {
                rank++;
                sb.AppendLine($"{rank}. #{candidate.Number} ({candidate.Score.ToString("0.00", CultureInfo.InvariantCulture)}) - [{candidate.Title}]({candidate.Url})");
            }
        }

        sb.AppendLine();
        sb.AppendLine("## Duplicate Clusters");
        sb.AppendLine();
        if (clusters.Count == 0) {
            sb.AppendLine("None.");
        } else {
            foreach (var cluster in clusters.Take(30)) {
                sb.AppendLine($"- {cluster.Id} (confidence: {cluster.Confidence.ToString("0.00", CultureInfo.InvariantCulture)}; items: {cluster.ItemIds.Count}; canonical: `{cluster.CanonicalItemId}`)");
            }
        }

        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    private static void WriteText(string path, string content) {
        if (string.IsNullOrWhiteSpace(path)) {
            return;
        }
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory)) {
            Directory.CreateDirectory(directory);
        }
        File.WriteAllText(fullPath, content, Utf8NoBom);
    }

    private static (string Owner, string Name) SplitRepo(string repo) {
        var parts = repo.Split('/', 2);
        return (parts[0], parts[1]);
    }

    private static bool TryGetProperty(JsonElement obj, string name, out JsonElement value) {
        value = default;
        if (obj.ValueKind != JsonValueKind.Object) {
            return false;
        }
        return obj.TryGetProperty(name, out value);
    }

    private static bool TryReadNumber(JsonElement obj, out int number) {
        number = 0;
        if (!TryGetProperty(obj, "number", out var prop) || prop.ValueKind != JsonValueKind.Number) {
            return false;
        }
        return prop.TryGetInt32(out number);
    }

    private static string ReadString(JsonElement obj, string name) {
        if (!TryGetProperty(obj, name, out var prop) || prop.ValueKind != JsonValueKind.String) {
            return string.Empty;
        }
        return prop.GetString() ?? string.Empty;
    }

    private static bool ReadBoolean(JsonElement obj, string name) {
        if (!TryGetProperty(obj, name, out var prop) || prop.ValueKind != JsonValueKind.True && prop.ValueKind != JsonValueKind.False) {
            return false;
        }
        return prop.GetBoolean();
    }

    private static int ReadInt(JsonElement obj, string name) {
        if (!TryGetProperty(obj, name, out var prop) || prop.ValueKind != JsonValueKind.Number || !prop.TryGetInt32(out var value)) {
            return 0;
        }
        return value;
    }

    private static int ReadNestedInt(JsonElement obj, string parentName, string childName) {
        if (!TryGetProperty(obj, parentName, out var parent) || parent.ValueKind != JsonValueKind.Object) {
            return 0;
        }
        if (!TryGetProperty(parent, childName, out var child) || child.ValueKind != JsonValueKind.Number || !child.TryGetInt32(out var value)) {
            return 0;
        }
        return value;
    }

    private static string ReadNestedString(JsonElement obj, string parentName, string childName) {
        if (!TryGetProperty(obj, parentName, out var parent) || parent.ValueKind != JsonValueKind.Object) {
            return string.Empty;
        }
        if (!TryGetProperty(parent, childName, out var child) || child.ValueKind != JsonValueKind.String) {
            return string.Empty;
        }
        return child.GetString() ?? string.Empty;
    }

    private static string ReadNestedNestedString(JsonElement obj, string parentName, string arrayName, int index,
        string objectName, string nestedObjectName, string childName) {
        if (!TryGetProperty(obj, parentName, out var parent) || parent.ValueKind != JsonValueKind.Object) {
            return string.Empty;
        }
        if (!TryGetProperty(parent, arrayName, out var array) || array.ValueKind != JsonValueKind.Array) {
            return string.Empty;
        }
        if (index < 0 || index >= array.GetArrayLength()) {
            return string.Empty;
        }
        var node = array[index];
        if (!TryGetProperty(node, objectName, out var nested) || nested.ValueKind != JsonValueKind.Object) {
            return string.Empty;
        }
        if (!TryGetProperty(nested, nestedObjectName, out var nestedObject) || nestedObject.ValueKind != JsonValueKind.Object) {
            return string.Empty;
        }
        if (!TryGetProperty(nestedObject, childName, out var value) || value.ValueKind != JsonValueKind.String) {
            return string.Empty;
        }
        return value.GetString() ?? string.Empty;
    }

    private static DateTimeOffset ReadDate(JsonElement obj, string name) {
        var raw = ReadString(obj, name);
        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)) {
            return parsed;
        }
        return DateTimeOffset.MinValue;
    }

    private static IReadOnlyList<string> ReadLabels(JsonElement obj) {
        var labels = new List<string>();
        if (!TryGetProperty(obj, "labels", out var labelsObj) ||
            labelsObj.ValueKind != JsonValueKind.Object ||
            !TryGetProperty(labelsObj, "nodes", out var nodes) ||
            nodes.ValueKind != JsonValueKind.Array) {
            return labels;
        }

        foreach (var node in nodes.EnumerateArray()) {
            var name = ReadString(node, "name");
            if (!string.IsNullOrWhiteSpace(name)) {
                labels.Add(name);
            }
        }
        return labels;
    }
}
