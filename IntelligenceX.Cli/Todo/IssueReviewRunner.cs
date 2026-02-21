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

internal static partial class IssueReviewRunner {
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
        IReadOnlyList<string> Labels,
        string ProposedAction = "needs-human-review",
        int ActionConfidence = 0,
        IReadOnlyList<string>? ConfidenceSignals = null,
        int ReopenedCount = 0
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
        public int MinAutoCloseConfidence { get; set; } = 80;
        public string? StatePath { get; set; } = Path.Combine("artifacts", "triage", "ix-issue-review-state.json");
        public List<string> AutoCloseAllowLabels { get; } = new();
        public List<string> AutoCloseDenyLabels { get; } = new();
        public bool ProposalOnly { get; set; }
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
                case "--min-auto-close-confidence":
                    if (i + 1 < args.Length &&
                        int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minAutoCloseConfidence)) {
                        options.MinAutoCloseConfidence = Math.Max(0, Math.Min(minAutoCloseConfidence, 100));
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
                case "--proposal-only":
                    options.ProposalOnly = true;
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
        if (options.ApplyClose && options.ProposalOnly) {
            Console.Error.WriteLine("`--apply-close` and `--proposal-only` cannot be used together.");
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
        Console.WriteLine("  --min-auto-close-confidence <0-100>  Minimum confidence required for auto-close (default: 80)");
        Console.WriteLine("  --state-path <path>         Candidate state path for consecutive tracking (default: artifacts/triage/ix-issue-review-state.json)");
        Console.WriteLine("  --no-state                  Disable candidate streak persistence");
        Console.WriteLine("  --allow-label <label>       Require at least one of these labels for auto-close (repeatable)");
        Console.WriteLine("  --deny-label <label>        Never auto-close issues with these labels (repeatable)");
        Console.WriteLine("  --proposal-only             Force advisory output mode (never close issues)");
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
        var issueByNumber = issues.ToDictionary(value => value.Number);
        var enrichedAssessments = new List<IssueReviewAssessment>(assessments.Count);
        foreach (var assessment in assessments) {
            if (!issueByNumber.TryGetValue(assessment.Number, out var issue)) {
                enrichedAssessments.Add(assessment);
                continue;
            }

            int? reopenedCount = null;
            if (assessment.IsInfraBlocker &&
                (assessment.Classification.Equals("no-longer-applicable", StringComparison.OrdinalIgnoreCase) ||
                 assessment.Classification.Equals("needs-review", StringComparison.OrdinalIgnoreCase))) {
                reopenedCount = await TryFetchReopenedCountAsync(options.Repo, assessment.Number).ConfigureAwait(false);
            }

            var enriched = EnrichWithActionSignals(
                assessment,
                issue,
                pullRequestsByNumber,
                nowUtc,
                options.MinAutoCloseConfidence,
                reopenedCount);
            enrichedAssessments.Add(enriched);
        }
        assessments = enrichedAssessments;

        assessments = assessments
            .OrderBy(value => ClassificationRank(value.Classification))
            .ThenBy(value => ProposedActionRank(value.ProposedAction))
            .ThenByDescending(value => value.ActionConfidence)
            .ThenByDescending(value => value.CandidateStreak)
            .ThenByDescending(value => value.AgeDays)
            .ThenBy(value => value.Number)
            .ToList();

        var noLongerApplicableCandidates = assessments
            .Where(value => value.Classification.Equals("no-longer-applicable", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var autoCloseCandidates = assessments
            .Where(value =>
                value.EligibleForAutoClose &&
                value.ProposedAction.Equals("close", StringComparison.OrdinalIgnoreCase) &&
                value.ActionConfidence >= options.MinAutoCloseConfidence)
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
        Console.WriteLine($"Min auto-close confidence: {options.MinAutoCloseConfidence}");
        Console.WriteLine(options.StatePath is null
            ? "State persistence: disabled."
            : $"State persistence: {options.StatePath}");
        Console.WriteLine(options.ProposalOnly
            ? "Proposal-only mode: close operations disabled."
            : options.ApplyClose
            ? $"Closed by automation: {closedIssueNumbers.Count}"
            : "Dry-run mode: no issues were closed (use --apply-close to close candidates).");
        return 0;
    }

}
