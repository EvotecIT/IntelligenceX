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

internal static class IssueReviewRunner {
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly Regex PullRequestUrlRef = new(
        @"(?i)\bhttps?://github\.com/(?<owner>[A-Za-z0-9_.-]+)/(?<repo>[A-Za-z0-9_.-]+)/pull/(?<num>\d+)\b",
        RegexOptions.Compiled
    );
    private static readonly Regex PullRequestRef = new(
        @"(?i)\b(?:pr|pull\s*request|pull)\s*#(?<num>\d+)\b",
        RegexOptions.Compiled
    );
    private static readonly Regex RepoPullRequestRef = new(
        @"(?i)\b(?:pr|pull\s*request|pull)\s+(?<owner>[A-Za-z0-9_.-]+)/(?<repo>[A-Za-z0-9_.-]+)#(?<num>\d+)\b",
        RegexOptions.Compiled
    );
    private static readonly HashSet<string> ProtectedLabels = new(StringComparer.OrdinalIgnoreCase) {
        "do-not-close",
        "keep-open",
        "pinned",
        "ix/decision:accept"
    };
    private static readonly string[] InfraKeywords = {
        "infra blocker",
        "infrastructure blocker",
        "ci blocker",
        "build blocker",
        "runner blocker"
    };

    internal const string ManagedCommentMarker = "<!-- intelligencex:issue-review -->";
    private const string DefaultRepo = "EvotecIT/IntelligenceX";

    internal sealed record IssueReviewCandidateIssue(
        int Number,
        string Title,
        string Body,
        string Url,
        DateTimeOffset UpdatedAtUtc,
        IReadOnlyList<string> Labels
    );

    internal sealed record PullRequestReference(
        int Number,
        string Title,
        string Url,
        string State,
        DateTimeOffset? MergedAtUtc,
        DateTimeOffset? ClosedAtUtc
    );

    internal sealed record IssueReviewAssessment(
        int Number,
        string Title,
        string Url,
        bool IsInfraBlocker,
        string Classification,
        bool EligibleForAutoClose,
        int CandidateStreak,
        double AgeDays,
        IReadOnlyList<int> LinkedPullRequests,
        IReadOnlyList<string> LinkedPullRequestStates,
        string Reason,
        IReadOnlyList<string> Labels
    );

    internal sealed record IssueReviewPolicy(
        IReadOnlySet<string> AutoCloseAllowLabels,
        IReadOnlySet<string> AutoCloseDenyLabels
    );

    private sealed class IssueReviewState {
        public string Schema { get; set; } = "intelligencex.issue-review.state.v1";
        public string Repo { get; set; } = string.Empty;
        public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
        public Dictionary<int, int> CandidateStreaks { get; set; } = new();
    }

    private sealed class Options {
        public string Repo { get; set; } = DefaultRepo;
        public int MaxIssues { get; set; } = 300;
        public int StaleDays { get; set; } = 14;
        public int MinConsecutiveCandidatesForClose { get; set; } = 1;
        public string? StatePath { get; set; } = Path.Combine("artifacts", "triage", "ix-issue-review-state.json");
        public List<string> AutoCloseAllowLabels { get; } = new();
        public List<string> AutoCloseDenyLabels { get; } = new();
        public bool ApplyClose { get; set; }
        public string CloseReason { get; set; } = "completed";
        public bool CommentOnClose { get; set; } = true;
        public string OutputPath { get; set; } = Path.Combine("artifacts", "triage", "ix-issue-review.json");
        public string SummaryPath { get; set; } = Path.Combine("artifacts", "triage", "ix-issue-review.md");
        public bool ShowHelp { get; set; }
        public bool ParseFailed { get; set; }
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
                case "--max-issues":
                    if (i + 1 < args.Length &&
                        int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var maxIssues)) {
                        options.MaxIssues = Math.Max(1, Math.Min(maxIssues, 2000));
                    } else {
                        options.ParseFailed = true;
                        options.ShowHelp = true;
                    }
                    break;
                case "--stale-days":
                    if (i + 1 < args.Length &&
                        int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var staleDays)) {
                        options.StaleDays = Math.Max(1, Math.Min(staleDays, 365));
                    } else {
                        options.ParseFailed = true;
                        options.ShowHelp = true;
                    }
                    break;
                case "--min-consecutive-candidates":
                    if (i + 1 < args.Length &&
                        int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minConsecutiveCandidates)) {
                        options.MinConsecutiveCandidatesForClose = Math.Max(1, Math.Min(minConsecutiveCandidates, 20));
                    } else {
                        options.ParseFailed = true;
                        options.ShowHelp = true;
                    }
                    break;
                case "--state-path":
                    if (i + 1 < args.Length) {
                        options.StatePath = args[++i];
                    } else {
                        options.ParseFailed = true;
                        options.ShowHelp = true;
                    }
                    break;
                case "--no-state":
                    options.StatePath = null;
                    break;
                case "--allow-label":
                    if (i + 1 < args.Length) {
                        var value = args[++i].Trim();
                        if (string.IsNullOrWhiteSpace(value)) {
                            options.ParseFailed = true;
                            options.ShowHelp = true;
                        } else {
                            options.AutoCloseAllowLabels.Add(value);
                        }
                    } else {
                        options.ParseFailed = true;
                        options.ShowHelp = true;
                    }
                    break;
                case "--deny-label":
                    if (i + 1 < args.Length) {
                        var value = args[++i].Trim();
                        if (string.IsNullOrWhiteSpace(value)) {
                            options.ParseFailed = true;
                            options.ShowHelp = true;
                        } else {
                            options.AutoCloseDenyLabels.Add(value);
                        }
                    } else {
                        options.ParseFailed = true;
                        options.ShowHelp = true;
                    }
                    break;
                case "--apply-close":
                    options.ApplyClose = true;
                    break;
                case "--close-reason":
                    if (i + 1 < args.Length) {
                        var reason = NormalizeCloseReason(args[++i]);
                        if (reason is null) {
                            options.ParseFailed = true;
                            options.ShowHelp = true;
                        } else {
                            options.CloseReason = reason;
                        }
                    } else {
                        options.ParseFailed = true;
                        options.ShowHelp = true;
                    }
                    break;
                case "--no-comment":
                    options.CommentOnClose = false;
                    break;
                case "--out":
                    if (i + 1 < args.Length) {
                        options.OutputPath = args[++i];
                    } else {
                        options.ParseFailed = true;
                        options.ShowHelp = true;
                    }
                    break;
                case "--summary":
                    if (i + 1 < args.Length) {
                        options.SummaryPath = args[++i];
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

        return options;
    }

    private static string? NormalizeCloseReason(string value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }

        return value.Trim().ToLowerInvariant() switch {
            "completed" => "completed",
            "not-planned" => "not_planned",
            "not_planned" => "not_planned",
            _ => null
        };
    }

    private static void PrintHelp() {
        Console.WriteLine("Usage:");
        Console.WriteLine("  intelligencex todo issue-review [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --repo <owner/name>         Repository to scan (default: EvotecIT/IntelligenceX)");
        Console.WriteLine("  --max-issues <n>            Open issues to scan (1-2000, default: 300)");
        Console.WriteLine("  --stale-days <n>            Age threshold for stale infra issues without PR links (default: 14)");
        Console.WriteLine("  --min-consecutive-candidates <n>  Consecutive no-longer-applicable runs required for auto-close (default: 1)");
        Console.WriteLine("  --state-path <path>         Candidate state path for consecutive tracking (default: artifacts/triage/ix-issue-review-state.json)");
        Console.WriteLine("  --no-state                  Disable candidate streak persistence");
        Console.WriteLine("  --allow-label <label>       Require at least one of these labels for auto-close (repeatable)");
        Console.WriteLine("  --deny-label <label>        Never auto-close issues with these labels (repeatable)");
        Console.WriteLine("  --apply-close               Close no-longer-applicable issues (default: dry-run)");
        Console.WriteLine("  --close-reason <completed|not-planned>  Reason used when closing issues (default: completed)");
        Console.WriteLine("  --no-comment                Do not post a managed IX close note");
        Console.WriteLine("  --out <path>                JSON output path (default: artifacts/triage/ix-issue-review.json)");
        Console.WriteLine("  --summary <path>            Markdown summary path (default: artifacts/triage/ix-issue-review.md)");
    }

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

        List<IssueReviewCandidateIssue> issues;
        try {
            issues = await FetchOpenIssuesAsync(options.Repo, options.MaxIssues).ConfigureAwait(false);
        } catch (Exception ex) {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }

        var pullRequestNumbers = new HashSet<int>();
        foreach (var issue in issues) {
            if (!IsInfraBlocker(issue)) {
                continue;
            }

            var refs = ExtractPullRequestReferences(options.Repo, $"{issue.Title}\n{issue.Body}");
            foreach (var value in refs) {
                pullRequestNumbers.Add(value);
            }
        }

        var pullRequestsByNumber = new Dictionary<int, PullRequestReference>();
        foreach (var number in pullRequestNumbers.OrderBy(value => value)) {
            var reference = await TryFetchPullRequestReferenceAsync(options.Repo, number).ConfigureAwait(false);
            if (reference is not null) {
                pullRequestsByNumber[number] = reference;
            }
        }

        var priorState = LoadState(options.StatePath, options.Repo);
        var policy = BuildPolicy(options.AutoCloseAllowLabels, options.AutoCloseDenyLabels);
        var nowUtc = DateTimeOffset.UtcNow;
        var assessments = new List<IssueReviewAssessment>(issues.Count);
        foreach (var issue in issues) {
            priorState.CandidateStreaks.TryGetValue(issue.Number, out var previousCandidateStreak);
            var assessment = AssessIssueForApplicability(
                issue,
                options.Repo,
                pullRequestsByNumber,
                nowUtc,
                options.StaleDays,
                policy,
                previousCandidateStreak,
                options.MinConsecutiveCandidatesForClose);
            assessments.Add(assessment);
        }
        assessments = assessments
            .OrderBy(value => ClassificationRank(value.Classification))
            .ThenByDescending(value => value.CandidateStreak)
            .ThenByDescending(value => value.AgeDays)
            .ThenBy(value => value.Number)
            .ToList();

        var noLongerApplicableCandidates = assessments
            .Where(value => value.Classification.Equals("no-longer-applicable", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var autoCloseCandidates = assessments
            .Where(value => value.EligibleForAutoClose)
            .ToList();

        var closedIssueNumbers = new List<int>();
        if (options.ApplyClose) {
            foreach (var assessment in autoCloseCandidates) {
                var closeSuccess = await TryCloseIssueAsync(options.Repo, assessment.Number, options.CloseReason).ConfigureAwait(false);
                if (!closeSuccess) {
                    continue;
                }

                closedIssueNumbers.Add(assessment.Number);
                if (options.CommentOnClose) {
                    await TryCommentOnClosedIssueAsync(options.Repo, assessment, options.CloseReason).ConfigureAwait(false);
                }
            }
        }

        var updatedState = BuildUpdatedState(options.Repo, nowUtc, assessments);
        SaveState(options.StatePath, updatedState);
        var report = BuildReport(options, nowUtc, assessments, closedIssueNumbers);
        var summary = BuildMarkdownSummary(options, nowUtc, assessments, closedIssueNumbers);
        WriteText(options.OutputPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        WriteText(options.SummaryPath, summary);

        Console.WriteLine($"Generated issue review report: {options.OutputPath}");
        Console.WriteLine($"Generated issue review summary: {options.SummaryPath}");
        Console.WriteLine($"Open issues scanned: {assessments.Count}");
        Console.WriteLine($"Infra blockers detected: {assessments.Count(value => value.IsInfraBlocker)}");
        Console.WriteLine($"No-longer-applicable candidates: {noLongerApplicableCandidates.Count}");
        Console.WriteLine($"Auto-close eligible this run: {autoCloseCandidates.Count}");
        Console.WriteLine($"Min consecutive candidates required: {options.MinConsecutiveCandidatesForClose}");
        Console.WriteLine(options.StatePath is null
            ? "State persistence: disabled."
            : $"State persistence: {options.StatePath}");
        Console.WriteLine(options.ApplyClose
            ? $"Closed by automation: {closedIssueNumbers.Count}"
            : "Dry-run mode: no issues were closed (use --apply-close to close candidates).");
        return 0;
    }

    private static async Task<List<IssueReviewCandidateIssue>> FetchOpenIssuesAsync(string repo, int maxIssues) {
        var (code, stdout, stderr) = await GhCli.RunAsync(
            TimeSpan.FromSeconds(90),
            "issue", "list",
            "--repo", repo,
            "--state", "open",
            "--limit", maxIssues.ToString(CultureInfo.InvariantCulture),
            "--json", "number,title,body,url,updatedAt,labels").ConfigureAwait(false);
        if (code != 0) {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(stderr)
                    ? "Failed to query open issues with gh issue list."
                    : stderr.Trim());
        }

        using var doc = JsonDocument.Parse(stdout);
        var values = new List<IssueReviewCandidateIssue>();
        if (doc.RootElement.ValueKind != JsonValueKind.Array) {
            return values;
        }

        foreach (var issue in doc.RootElement.EnumerateArray()) {
            var number = ReadInt(issue, "number");
            if (number <= 0) {
                continue;
            }
            var title = ReadString(issue, "title");
            var body = ReadString(issue, "body");
            var url = ReadString(issue, "url");
            var updatedAt = ReadDate(issue, "updatedAt");
            var labels = ReadLabels(issue);
            values.Add(new IssueReviewCandidateIssue(number, title, body, url, updatedAt, labels));
        }
        return values;
    }

    private static async Task<PullRequestReference?> TryFetchPullRequestReferenceAsync(string repo, int number) {
        var (code, stdout, stderr) = await GhCli.RunAsync(
            TimeSpan.FromSeconds(60),
            "pr", "view",
            number.ToString(CultureInfo.InvariantCulture),
            "--repo", repo,
            "--json", "number,title,url,state,mergedAt,closedAt").ConfigureAwait(false);
        if (code != 0) {
            if (!string.IsNullOrWhiteSpace(stderr) &&
                stderr.IndexOf("Not Found", StringComparison.OrdinalIgnoreCase) >= 0) {
                return null;
            }
            Console.Error.WriteLine(
                $"Warning: failed to fetch PR #{number} for {repo}: {(string.IsNullOrWhiteSpace(stderr) ? "unknown error" : stderr.Trim())}");
            return null;
        }

        using var doc = JsonDocument.Parse(stdout);
        var root = doc.RootElement;
        var parsedNumber = ReadInt(root, "number");
        if (parsedNumber <= 0) {
            return null;
        }

        var title = ReadString(root, "title");
        var url = ReadString(root, "url");
        var state = ReadString(root, "state");
        var mergedAt = ReadNullableDate(root, "mergedAt");
        var closedAt = ReadNullableDate(root, "closedAt");
        return new PullRequestReference(parsedNumber, title, url, state, mergedAt, closedAt);
    }

    internal static IReadOnlyList<int> ExtractPullRequestReferences(string repo, string text) {
        if (string.IsNullOrWhiteSpace(text)) {
            return Array.Empty<int>();
        }

        var numbers = new HashSet<int>();
        var (owner, name) = SplitRepo(repo);
        foreach (Match match in PullRequestRef.Matches(text)) {
            if (TryExtractNumber(match, "num", out var value)) {
                numbers.Add(value);
            }
        }

        foreach (Match match in RepoPullRequestRef.Matches(text)) {
            var refOwner = match.Groups["owner"].Value;
            var refName = match.Groups["repo"].Value;
            if (!refOwner.Equals(owner, StringComparison.OrdinalIgnoreCase) ||
                !refName.Equals(name, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            if (TryExtractNumber(match, "num", out var value)) {
                numbers.Add(value);
            }
        }

        foreach (Match match in PullRequestUrlRef.Matches(text)) {
            var refOwner = match.Groups["owner"].Value;
            var refName = match.Groups["repo"].Value;
            if (!refOwner.Equals(owner, StringComparison.OrdinalIgnoreCase) ||
                !refName.Equals(name, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            if (TryExtractNumber(match, "num", out var value)) {
                numbers.Add(value);
            }
        }

        return numbers
            .OrderBy(value => value)
            .ToList();
    }

    internal static IssueReviewAssessment AssessIssueForApplicability(
        IssueReviewCandidateIssue issue,
        string repo,
        IReadOnlyDictionary<int, PullRequestReference> pullRequestsByNumber,
        DateTimeOffset nowUtc,
        int staleDays) {
        var defaultPolicy = BuildPolicy(Array.Empty<string>(), Array.Empty<string>());
        return AssessIssueForApplicability(
            issue,
            repo,
            pullRequestsByNumber,
            nowUtc,
            staleDays,
            defaultPolicy,
            previousCandidateStreak: 0,
            minConsecutiveCandidatesForClose: 1);
    }

    internal static IssueReviewAssessment AssessIssueForApplicability(
        IssueReviewCandidateIssue issue,
        string repo,
        IReadOnlyDictionary<int, PullRequestReference> pullRequestsByNumber,
        DateTimeOffset nowUtc,
        int staleDays,
        IssueReviewPolicy policy,
        int previousCandidateStreak,
        int minConsecutiveCandidatesForClose) {
        var linkedPullRequests = ExtractPullRequestReferences(repo, $"{issue.Title}\n{issue.Body}");
        var linkedStates = new List<string>();
        foreach (var number in linkedPullRequests) {
            if (pullRequestsByNumber.TryGetValue(number, out var pullRequest)) {
                var state = NormalizePullRequestState(pullRequest);
                linkedStates.Add($"#{number}:{state}");
            } else {
                linkedStates.Add($"#{number}:unknown");
            }
        }

        var ageDays = Math.Round(Math.Max(0, (nowUtc - issue.UpdatedAtUtc).TotalDays), 1, MidpointRounding.AwayFromZero);
        var isInfra = IsInfraBlocker(issue);

        if (!isInfra) {
            return new IssueReviewAssessment(
                issue.Number,
                issue.Title,
                issue.Url,
                false,
                "out-of-scope",
                false,
                0,
                ageDays,
                linkedPullRequests,
                linkedStates,
                "Issue is not classified as infra blocker.",
                issue.Labels);
        }

        if (issue.Labels.Any(label => policy.AutoCloseDenyLabels.Contains(label))) {
            return new IssueReviewAssessment(
                issue.Number,
                issue.Title,
                issue.Url,
                true,
                "needs-review",
                false,
                0,
                ageDays,
                linkedPullRequests,
                linkedStates,
                "Denied/protected label present; leaving for maintainer review.",
                issue.Labels);
        }

        if (linkedPullRequests.Count == 0) {
            if (ageDays >= staleDays) {
                return new IssueReviewAssessment(
                    issue.Number,
                    issue.Title,
                    issue.Url,
                    true,
                    "needs-review",
                    false,
                    0,
                    ageDays,
                    linkedPullRequests,
                    linkedStates,
                    $"Infra blocker is stale ({ageDays.ToString("0.0", CultureInfo.InvariantCulture)}d) with no linked PR reference.",
                    issue.Labels);
            }

            return new IssueReviewAssessment(
                issue.Number,
                issue.Title,
                issue.Url,
                true,
                "active",
                false,
                0,
                ageDays,
                linkedPullRequests,
                linkedStates,
                "Infra blocker has no linked PR reference yet.",
                issue.Labels);
        }

        var missingReferences = linkedPullRequests
            .Where(number => !pullRequestsByNumber.ContainsKey(number))
            .ToList();
        if (missingReferences.Count > 0) {
            return new IssueReviewAssessment(
                issue.Number,
                issue.Title,
                issue.Url,
                true,
                "needs-review",
                false,
                0,
                ageDays,
                linkedPullRequests,
                linkedStates,
                $"Linked PR references could not be resolved: {string.Join(", ", missingReferences.Select(value => $"#{value}"))}.",
                issue.Labels);
        }

        var linkedReferences = linkedPullRequests
            .Select(number => pullRequestsByNumber[number])
            .ToList();
        var hasOpenPr = linkedReferences.Any(reference =>
            NormalizePullRequestState(reference).Equals("open", StringComparison.OrdinalIgnoreCase));
        if (hasOpenPr) {
            return new IssueReviewAssessment(
                issue.Number,
                issue.Title,
                issue.Url,
                true,
                "active",
                false,
                0,
                ageDays,
                linkedPullRequests,
                linkedStates,
                "At least one linked PR is still open.",
                issue.Labels);
        }

        var allResolved = linkedReferences.All(reference => {
            var state = NormalizePullRequestState(reference);
            return state is "merged" or "closed";
        });
        if (allResolved) {
            var candidateStreak = Math.Max(0, previousCandidateStreak) + 1;
            var reason = "All linked PRs are resolved (merged/closed).";
            var eligibleForAutoClose = true;
            if (policy.AutoCloseAllowLabels.Count > 0 &&
                !issue.Labels.Any(label => policy.AutoCloseAllowLabels.Contains(label))) {
                eligibleForAutoClose = false;
                reason += $" Missing allow label for auto-close ({string.Join(", ", policy.AutoCloseAllowLabels.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))}).";
            }
            if (candidateStreak < minConsecutiveCandidatesForClose) {
                eligibleForAutoClose = false;
                reason += $" Candidate streak {candidateStreak}/{minConsecutiveCandidatesForClose}.";
            }

            return new IssueReviewAssessment(
                issue.Number,
                issue.Title,
                issue.Url,
                true,
                "no-longer-applicable",
                eligibleForAutoClose,
                candidateStreak,
                ageDays,
                linkedPullRequests,
                linkedStates,
                reason,
                issue.Labels);
        }

        return new IssueReviewAssessment(
            issue.Number,
            issue.Title,
            issue.Url,
            true,
            "needs-review",
            false,
            0,
            ageDays,
            linkedPullRequests,
            linkedStates,
            "Linked PR states are inconclusive.",
            issue.Labels);
    }

    private static async Task<bool> TryCloseIssueAsync(string repo, int issueNumber, string closeReason) {
        var (code, _, err) = await GhCli.RunAsync(
            TimeSpan.FromSeconds(45),
            "api",
            "--method", "PATCH",
            $"repos/{repo}/issues/{issueNumber.ToString(CultureInfo.InvariantCulture)}",
            "-f", "state=closed",
            "-f", $"state_reason={closeReason}").ConfigureAwait(false);
        if (code == 0) {
            return true;
        }

        Console.Error.WriteLine(
            $"Warning: failed to close issue #{issueNumber} ({repo}): {(string.IsNullOrWhiteSpace(err) ? "unknown error" : err.Trim())}");
        return false;
    }

    private static async Task TryCommentOnClosedIssueAsync(string repo, IssueReviewAssessment assessment, string closeReason) {
        var body = new StringBuilder();
        body.AppendLine(ManagedCommentMarker);
        body.AppendLine("Closed by IntelligenceX issue-review automation.");
        body.AppendLine();
        body.AppendLine($"- Classification: `{assessment.Classification}`");
        body.AppendLine($"- Reason: {assessment.Reason}");
        body.AppendLine($"- Close reason: `{closeReason}`");
        if (assessment.LinkedPullRequestStates.Count > 0) {
            body.AppendLine($"- Linked PR states: {string.Join(", ", assessment.LinkedPullRequestStates)}");
        }

        var (code, _, err) = await GhCli.RunAsync(
            TimeSpan.FromSeconds(45),
            "api",
            "--method", "POST",
            $"repos/{repo}/issues/{assessment.Number.ToString(CultureInfo.InvariantCulture)}/comments",
            "-f", $"body={body.ToString().TrimEnd()}").ConfigureAwait(false);
        if (code != 0) {
            Console.Error.WriteLine(
                $"Warning: failed to add managed close note to issue #{assessment.Number}: {(string.IsNullOrWhiteSpace(err) ? "unknown error" : err.Trim())}");
        }
    }

    private static object BuildReport(
        Options options,
        DateTimeOffset generatedAtUtc,
        IReadOnlyList<IssueReviewAssessment> assessments,
        IReadOnlyList<int> closedIssueNumbers) {
        var infra = assessments.Where(value => value.IsInfraBlocker).ToList();
        return new {
            schema = "intelligencex.issue-review.v1",
            generatedAtUtc = generatedAtUtc.UtcDateTime.ToString("o", CultureInfo.InvariantCulture),
            repo = options.Repo,
            settings = new {
                maxIssues = options.MaxIssues,
                staleDays = options.StaleDays,
                minConsecutiveCandidatesForClose = options.MinConsecutiveCandidatesForClose,
                statePath = options.StatePath,
                autoCloseAllowLabels = options.AutoCloseAllowLabels,
                autoCloseDenyLabels = options.AutoCloseDenyLabels,
                applyClose = options.ApplyClose,
                closeReason = options.CloseReason
            },
            summary = new {
                openIssuesScanned = assessments.Count,
                infraBlockers = infra.Count,
                noLongerApplicable = infra.Count(value => value.Classification.Equals("no-longer-applicable", StringComparison.OrdinalIgnoreCase)),
                autoCloseEligible = infra.Count(value => value.EligibleForAutoClose),
                needsReview = infra.Count(value => value.Classification.Equals("needs-review", StringComparison.OrdinalIgnoreCase)),
                active = infra.Count(value => value.Classification.Equals("active", StringComparison.OrdinalIgnoreCase)),
                closedByAutomation = closedIssueNumbers.Count
            },
            closedIssueNumbers = closedIssueNumbers,
            items = assessments.Select(value => new {
                number = value.Number,
                title = value.Title,
                url = value.Url,
                isInfraBlocker = value.IsInfraBlocker,
                classification = value.Classification,
                eligibleForAutoClose = value.EligibleForAutoClose,
                candidateStreak = value.CandidateStreak,
                ageDays = value.AgeDays,
                linkedPullRequests = value.LinkedPullRequests,
                linkedPullRequestStates = value.LinkedPullRequestStates,
                labels = value.Labels,
                reason = value.Reason
            }).ToList()
        };
    }

    private static string BuildMarkdownSummary(
        Options options,
        DateTimeOffset generatedAtUtc,
        IReadOnlyList<IssueReviewAssessment> assessments,
        IReadOnlyList<int> closedIssueNumbers) {
        var infra = assessments.Where(value => value.IsInfraBlocker).ToList();
        var noLongerApplicable = infra
            .Where(value => value.Classification.Equals("no-longer-applicable", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var needsReview = infra
            .Where(value => value.Classification.Equals("needs-review", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var active = infra
            .Where(value => value.Classification.Equals("active", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var builder = new StringBuilder();
        builder.AppendLine("# Issue Review");
        builder.AppendLine();
        builder.AppendLine($"- Generated: {generatedAtUtc.UtcDateTime:yyyy-MM-dd HH:mm:ss} UTC");
        builder.AppendLine($"- Repo: `{options.Repo}`");
        builder.AppendLine($"- Open issues scanned: {assessments.Count}");
        builder.AppendLine($"- Infra blockers detected: {infra.Count}");
        builder.AppendLine($"- No-longer-applicable: {noLongerApplicable.Count}");
        builder.AppendLine($"- Auto-close eligible: {noLongerApplicable.Count(value => value.EligibleForAutoClose)}");
        builder.AppendLine($"- Needs review: {needsReview.Count}");
        builder.AppendLine($"- Active infra blockers: {active.Count}");
        builder.AppendLine($"- Min consecutive candidates for close: {options.MinConsecutiveCandidatesForClose}");
        builder.AppendLine(options.StatePath is null
            ? "- State path: disabled"
            : $"- State path: `{options.StatePath}`");
        if (options.AutoCloseAllowLabels.Count > 0) {
            builder.AppendLine($"- Auto-close allow labels: `{string.Join("`, `", options.AutoCloseAllowLabels)}`");
        }
        if (options.AutoCloseDenyLabels.Count > 0) {
            builder.AppendLine($"- Auto-close deny labels: `{string.Join("`, `", options.AutoCloseDenyLabels)}`");
        }
        builder.AppendLine(options.ApplyClose
            ? $"- Closed by automation: {closedIssueNumbers.Count}"
            : "- Dry-run mode: no issues were closed.");
        builder.AppendLine();
        AppendSection(builder, "No-Longer-Applicable Candidates", noLongerApplicable);
        AppendSection(builder, "Needs Review", needsReview);
        AppendSection(builder, "Active Infra Blockers", active);
        return builder.ToString().TrimEnd() + Environment.NewLine;
    }

    private static void AppendSection(StringBuilder builder, string title, IReadOnlyList<IssueReviewAssessment> items) {
        builder.AppendLine($"## {title}");
        builder.AppendLine();
        if (items.Count == 0) {
            builder.AppendLine("None.");
            builder.AppendLine();
            return;
        }

        foreach (var item in items) {
            var linked = item.LinkedPullRequestStates.Count == 0
                ? "none"
                : string.Join(", ", item.LinkedPullRequestStates);
            var streak = item.CandidateStreak > 0
                ? $" | streak {item.CandidateStreak}"
                : string.Empty;
            var eligibility = item.EligibleForAutoClose
                ? " | eligible auto-close"
                : string.Empty;
            builder.AppendLine(
                $"- #{item.Number} [{item.Title}]({item.Url}) | age {item.AgeDays.ToString("0.0", CultureInfo.InvariantCulture)}d{streak}{eligibility} | linked PRs: {linked} | {item.Reason}");
        }
        builder.AppendLine();
    }

    private static int ClassificationRank(string classification) {
        return classification.ToLowerInvariant() switch {
            "no-longer-applicable" => 0,
            "needs-review" => 1,
            "active" => 2,
            _ => 3
        };
    }

    internal static IssueReviewPolicy BuildPolicy(
        IReadOnlyCollection<string> autoCloseAllowLabels,
        IReadOnlyCollection<string> autoCloseDenyLabels) {
        var allow = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in autoCloseAllowLabels) {
            if (!string.IsNullOrWhiteSpace(value)) {
                allow.Add(value.Trim());
            }
        }

        var deny = new HashSet<string>(ProtectedLabels, StringComparer.OrdinalIgnoreCase);
        foreach (var value in autoCloseDenyLabels) {
            if (!string.IsNullOrWhiteSpace(value)) {
                deny.Add(value.Trim());
            }
        }

        return new IssueReviewPolicy(allow, deny);
    }

    private static IssueReviewState LoadState(string? path, string repo) {
        if (string.IsNullOrWhiteSpace(path)) {
            return new IssueReviewState { Repo = repo };
        }

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) {
            return new IssueReviewState { Repo = repo };
        }

        try {
            var content = File.ReadAllText(fullPath);
            var state = JsonSerializer.Deserialize<IssueReviewState>(content);
            if (state is null) {
                return new IssueReviewState { Repo = repo };
            }

            if (!string.IsNullOrWhiteSpace(state.Repo) &&
                !state.Repo.Equals(repo, StringComparison.OrdinalIgnoreCase)) {
                return new IssueReviewState { Repo = repo };
            }

            state.Repo = repo;
            state.CandidateStreaks ??= new Dictionary<int, int>();
            return state;
        } catch (Exception ex) {
            Console.Error.WriteLine($"Warning: failed to load issue-review state from '{path}': {ex.Message}");
            return new IssueReviewState { Repo = repo };
        }
    }

    private static IssueReviewState BuildUpdatedState(
        string repo,
        DateTimeOffset updatedAtUtc,
        IReadOnlyList<IssueReviewAssessment> assessments) {
        var state = new IssueReviewState {
            Repo = repo,
            UpdatedAtUtc = updatedAtUtc
        };

        foreach (var assessment in assessments) {
            if (!assessment.Classification.Equals("no-longer-applicable", StringComparison.OrdinalIgnoreCase) ||
                assessment.CandidateStreak <= 0) {
                continue;
            }
            state.CandidateStreaks[assessment.Number] = assessment.CandidateStreak;
        }

        return state;
    }

    private static void SaveState(string? path, IssueReviewState state) {
        if (string.IsNullOrWhiteSpace(path)) {
            return;
        }

        try {
            WriteText(path, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        } catch (Exception ex) {
            Console.Error.WriteLine($"Warning: failed to write issue-review state to '{path}': {ex.Message}");
        }
    }

    private static bool IsInfraBlocker(IssueReviewCandidateIssue issue) {
        foreach (var label in issue.Labels) {
            if (label.Equals("infra", StringComparison.OrdinalIgnoreCase) ||
                label.Equals("infra-blocker", StringComparison.OrdinalIgnoreCase) ||
                label.Equals("infrastructure", StringComparison.OrdinalIgnoreCase) ||
                label.Equals("blocker", StringComparison.OrdinalIgnoreCase) ||
                label.Equals("ci", StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        var text = $"{issue.Title}\n{issue.Body}";
        foreach (var keyword in InfraKeywords) {
            if (text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0) {
                return true;
            }
        }

        return false;
    }

    private static string NormalizePullRequestState(PullRequestReference reference) {
        if (reference.MergedAtUtc.HasValue) {
            return "merged";
        }

        return reference.State.Trim().ToUpperInvariant() switch {
            "OPEN" => "open",
            "CLOSED" => "closed",
            "MERGED" => "merged",
            _ => "unknown"
        };
    }

    private static bool TryExtractNumber(Match match, string groupName, out int value) {
        value = 0;
        var raw = match.Groups[groupName].Value;
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value > 0;
    }

    private static (string Owner, string Name) SplitRepo(string repo) {
        var parts = repo.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2) {
            throw new InvalidOperationException($"Invalid repo value: '{repo}'. Expected owner/name.");
        }
        return (parts[0], parts[1]);
    }

    private static int ReadInt(JsonElement element, string name) {
        if (!element.TryGetProperty(name, out var value)) {
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

    private static string ReadString(JsonElement element, string name) {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static DateTimeOffset ReadDate(JsonElement element, string name) {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String) {
            return DateTimeOffset.UtcNow;
        }
        return DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed)
            ? parsed
            : DateTimeOffset.UtcNow;
    }

    private static DateTimeOffset? ReadNullableDate(JsonElement element, string name) {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) {
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

    private static IReadOnlyList<string> ReadLabels(JsonElement issue) {
        if (!issue.TryGetProperty("labels", out var labelsElement) || labelsElement.ValueKind != JsonValueKind.Array) {
            return Array.Empty<string>();
        }

        var labels = new List<string>();
        foreach (var label in labelsElement.EnumerateArray()) {
            if (label.ValueKind == JsonValueKind.Object &&
                label.TryGetProperty("name", out var nameProp) &&
                nameProp.ValueKind == JsonValueKind.String) {
                var value = nameProp.GetString();
                if (!string.IsNullOrWhiteSpace(value)) {
                    labels.Add(value.Trim());
                }
                continue;
            }

            if (label.ValueKind == JsonValueKind.String) {
                var value = label.GetString();
                if (!string.IsNullOrWhiteSpace(value)) {
                    labels.Add(value.Trim());
                }
            }
        }

        return labels;
    }

    private static void WriteText(string path, string content) {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory)) {
            Directory.CreateDirectory(directory);
        }
        File.WriteAllText(fullPath, content, Utf8NoBom);
    }
}
