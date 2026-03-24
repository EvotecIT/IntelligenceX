using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Reviewer;

namespace IntelligenceX.Cli.Ci;

internal static class CiReviewFailOpenSummaryCommand {
    public static async Task<int> RunAsync(string[] args) {
        var options = ParseArgs(args);
        if (options.ShowHelp) {
            PrintHelp();
            return 0;
        }
        if (options.Error is not null) {
            Console.Error.WriteLine(options.Error);
            return 1;
        }

        var repoFullName = ResolveRepoFullName(options);
        if (string.IsNullOrWhiteSpace(repoFullName) || !TryParseRepo(repoFullName, out var owner, out var repo)) {
            Console.Error.WriteLine("Missing --repo <owner/name> or GITHUB_REPOSITORY.");
            return 1;
        }

        var prNumber = await ResolvePullRequestNumberAsync(options).ConfigureAwait(false);
        if (!prNumber.HasValue || prNumber.Value <= 0) {
            Console.WriteLine("Skipping fail-open summary finalization because no PR number was available.");
            return 0;
        }

        var token = ResolveGitHubToken(options);
        if (string.IsNullOrWhiteSpace(token)) {
            Console.Error.WriteLine("Missing GitHub token. Use --github-token or set GITHUB_TOKEN/GH_TOKEN/INTELLIGENCEX_GITHUB_TOKEN.");
            return 1;
        }

        var logPath = ResolveLogPath(options);
        string logText = string.Empty;
        if (!string.IsNullOrWhiteSpace(logPath) && File.Exists(logPath)) {
            logText = await File.ReadAllTextAsync(logPath).ConfigureAwait(false);
        }

        using var github = new GitHubClient(token!, options.GitHubBaseUrl);
        var context = await github.GetPullRequestAsync(owner, repo, prNumber.Value, CancellationToken.None).ConfigureAwait(false);
        var failure = ReviewDiagnostics.ClassifyWorkflowFailureLog(logText);
        var remediationRepo = repoFullName;
        var body = ReviewDiagnostics.BuildWorkflowFailOpenSummaryBody(context, options.ReviewerSource, remediationRepo, failure);

        var existing = await FindExistingSummaryCommentAsync(github, owner, repo, prNumber.Value, options.CommentSearchLimit)
            .ConfigureAwait(false);
        if (existing is not null) {
            await github.UpdateIssueCommentAsync(owner, repo, existing.Id, body, CancellationToken.None).ConfigureAwait(false);
            Console.WriteLine($"Updated fail-open reviewer summary on {owner}/{repo}#{prNumber.Value}.");
        } else {
            await github.CreateIssueCommentAsync(owner, repo, prNumber.Value, body, CancellationToken.None).ConfigureAwait(false);
            Console.WriteLine($"Created fail-open reviewer summary on {owner}/{repo}#{prNumber.Value}.");
        }

        return 0;
    }

    private static async Task<IssueComment?> FindExistingSummaryCommentAsync(GitHubClient github, string owner, string repo,
        int prNumber, int limit) {
        var comments = await github.ListIssueCommentsAsync(owner, repo, prNumber, Math.Max(1, limit), CancellationToken.None)
            .ConfigureAwait(false);
        return comments.FirstOrDefault(ReviewDiagnostics.IsOwnedWorkflowSummaryComment);
    }

    private static string? ResolveLogPath(Options options) {
        if (options.ReviewerSource.Equals("source", StringComparison.OrdinalIgnoreCase)) {
            return FirstExistingPath(options.SourceLogPath);
        }

        return FirstExistingPath(options.ReleaseUnixLogPath, options.ReleaseWindowsLogPath, options.SourceLogPath);
    }

    private static string? FirstExistingPath(params string?[] candidates) {
        foreach (var candidate in candidates) {
            if (string.IsNullOrWhiteSpace(candidate)) {
                continue;
            }
            var fullPath = Path.GetFullPath(candidate);
            if (File.Exists(fullPath)) {
                return fullPath;
            }
        }
        return null;
    }

    private static string? ResolveRepoFullName(Options options) {
        if (!string.IsNullOrWhiteSpace(options.Repo)) {
            return options.Repo!.Trim();
        }
        var repo = Environment.GetEnvironmentVariable("GITHUB_REPOSITORY");
        return string.IsNullOrWhiteSpace(repo) ? null : repo.Trim();
    }

    private static async Task<int?> ResolvePullRequestNumberAsync(Options options) {
        if (options.PrNumber.HasValue && options.PrNumber.Value > 0) {
            return options.PrNumber.Value;
        }

        var eventPath = Environment.GetEnvironmentVariable("GITHUB_EVENT_PATH");
        if (string.IsNullOrWhiteSpace(eventPath) || !File.Exists(eventPath)) {
            return null;
        }

        try {
            await using var stream = File.OpenRead(eventPath);
            using var document = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
            if (document.RootElement.TryGetProperty("pull_request", out var pullRequest) &&
                pullRequest.TryGetProperty("number", out var prNumberElement) &&
                prNumberElement.TryGetInt32(out var prNumber) &&
                prNumber > 0) {
                return prNumber;
            }
        } catch (Exception ex) {
            Console.Error.WriteLine($"Warning: failed to parse GITHUB_EVENT_PATH for pull_request.number: {ex.Message}");
        }

        return null;
    }

    private static string? ResolveGitHubToken(Options options) {
        if (!string.IsNullOrWhiteSpace(options.GitHubToken)) {
            return options.GitHubToken;
        }
        return FirstNonEmptyEnvironment("GITHUB_TOKEN", "GH_TOKEN", "INTELLIGENCEX_GITHUB_TOKEN");
    }

    private static string? FirstNonEmptyEnvironment(params string[] names) {
        foreach (var name in names) {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value)) {
                return value.Trim();
            }
        }
        return null;
    }

    private static bool TryParseRepo(string value, out string owner, out string repo) {
        owner = string.Empty;
        repo = string.Empty;
        var parts = value.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1])) {
            return false;
        }

        owner = parts[0];
        repo = parts[1];
        return true;
    }

    private static Options ParseArgs(string[] args) {
        var options = new Options();
        for (var i = 0; i < args.Length; i++) {
            var arg = args[i];
            switch (arg.ToLowerInvariant()) {
                case "help":
                case "-h":
                case "--help":
                    options.ShowHelp = true;
                    break;
                case "--repo":
                    options.Repo = ReadOptionalValue(args, ref i, arg, options);
                    break;
                case "--pr-number":
                    var prText = ReadOptionalValue(args, ref i, arg, options);
                    if (options.Error is null && !string.IsNullOrWhiteSpace(prText)) {
                        if (!int.TryParse(prText, out var prNumber) || prNumber <= 0) {
                            options.Error = "Invalid value for --pr-number.";
                        } else {
                            options.PrNumber = prNumber;
                        }
                    }
                    break;
                case "--reviewer-source":
                    options.ReviewerSource = ReadRequiredValue(args, ref i, arg, options) ?? "source";
                    if (!options.ReviewerSource.Equals("source", StringComparison.OrdinalIgnoreCase) &&
                        !options.ReviewerSource.Equals("release", StringComparison.OrdinalIgnoreCase)) {
                        options.Error = "Invalid value for --reviewer-source. Use source or release.";
                    }
                    break;
                case "--source-log":
                    options.SourceLogPath = ReadRequiredValue(args, ref i, arg, options);
                    break;
                case "--release-unix-log":
                    options.ReleaseUnixLogPath = ReadRequiredValue(args, ref i, arg, options);
                    break;
                case "--release-windows-log":
                    options.ReleaseWindowsLogPath = ReadRequiredValue(args, ref i, arg, options);
                    break;
                case "--comment-search-limit":
                    var limitText = ReadRequiredValue(args, ref i, arg, options);
                    if (options.Error is null) {
                        if (!int.TryParse(limitText, out var limit) || limit <= 0) {
                            options.Error = "Invalid value for --comment-search-limit.";
                        } else {
                            options.CommentSearchLimit = limit;
                        }
                    }
                    break;
                case "--github-token":
                    options.GitHubToken = ReadRequiredValue(args, ref i, arg, options);
                    break;
                case "--github-base-url":
                    options.GitHubBaseUrl = ReadRequiredValue(args, ref i, arg, options);
                    break;
                default:
                    options.Error = $"Unknown option '{arg}' for review-fail-open-summary.";
                    return options;
            }
        }

        return options;
    }

    private static string? ReadRequiredValue(string[] args, ref int index, string name, Options options) {
        if (index + 1 >= args.Length) {
            options.Error = $"Missing value for {name}.";
            return null;
        }
        index++;
        var value = args[index];
        if (string.IsNullOrWhiteSpace(value)) {
            options.Error = $"Empty value for {name}.";
            return null;
        }
        return value.Trim();
    }

    private static string? ReadOptionalValue(string[] args, ref int index, string name, Options options) {
        if (index + 1 >= args.Length) {
            options.Error = $"Missing value for {name}.";
            return null;
        }
        index++;
        var value = args[index];
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static void PrintHelp() {
        Console.WriteLine("Finalize fail-open reviewer summaries in CI.");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  intelligencex ci review-fail-open-summary --reviewer-source <source|release> [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --repo <owner/name>            Repository (defaults to GITHUB_REPOSITORY)");
        Console.WriteLine("  --pr-number <n>                Pull request number (falls back to GITHUB_EVENT_PATH)");
        Console.WriteLine("  --source-log <path>            Source reviewer log path");
        Console.WriteLine("  --release-unix-log <path>      Release reviewer unix log path");
        Console.WriteLine("  --release-windows-log <path>   Release reviewer windows log path");
        Console.WriteLine("  --comment-search-limit <n>     Existing comment scan limit (default: 100)");
    }

    private sealed class Options {
        public bool ShowHelp { get; set; }
        public string? Error { get; set; }
        public string? Repo { get; set; }
        public int? PrNumber { get; set; }
        public string ReviewerSource { get; set; } = "source";
        public string? SourceLogPath { get; set; }
        public string? ReleaseUnixLogPath { get; set; }
        public string? ReleaseWindowsLogPath { get; set; }
        public int CommentSearchLimit { get; set; } = 100;
        public string? GitHubToken { get; set; }
        public string? GitHubBaseUrl { get; set; }
    }
}
