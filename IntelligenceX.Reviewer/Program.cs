using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.OpenAI.Auth;

namespace IntelligenceX.Reviewer;

public static class ReviewerApp {
    public static async Task<int> RunAsync(string[] args) {
        try {
            TryWriteAuthFromEnv();
            var settings = ReviewSettings.Load();
            var token = Environment.GetEnvironmentVariable("INTELLIGENCEX_GITHUB_TOKEN")
                ?? Environment.GetEnvironmentVariable("GITHUB_TOKEN");

            if (string.IsNullOrWhiteSpace(token)) {
                Console.Error.WriteLine("Missing GitHub token (INTELLIGENCEX_GITHUB_TOKEN or GITHUB_TOKEN).");
                return 1;
            }

            using var github = new GitHubClient(token);
            PullRequestContext? context = null;
            var eventPath = Environment.GetEnvironmentVariable("GITHUB_EVENT_PATH");
            if (!string.IsNullOrWhiteSpace(eventPath) && File.Exists(eventPath)) {
                var json = await File.ReadAllTextAsync(eventPath).ConfigureAwait(false);
                var rootValue = JsonLite.Parse(json);
                var root = rootValue?.AsObject();
                if (root is null) {
                    Console.Error.WriteLine("Invalid GitHub event payload.");
                    return 1;
                }
                context = GitHubEventParser.TryParsePullRequest(root);
            }
            if (context is null) {
                var repoName = GetInput("repo") ?? GetInput("repository") ?? Environment.GetEnvironmentVariable("GITHUB_REPOSITORY");
                var prNumber = GetInputInt("pr_number") ?? GetInputInt("pull_request") ?? GetInputInt("number");
                if (string.IsNullOrWhiteSpace(repoName) || !prNumber.HasValue) {
                    Console.Error.WriteLine("Missing pull_request data. Provide inputs: repo and pr_number.");
                    return 1;
                }
                var (owner, repo) = SplitRepo(repoName!);
                context = await github.GetPullRequestAsync(owner, repo, prNumber.Value, CancellationToken.None)
                    .ConfigureAwait(false);
            }

            if (settings.SkipDraft && context.Draft) {
                Console.WriteLine("Skipping draft pull request.");
                return 0;
            }
            if (ShouldSkipByTitle(context.Title, settings.SkipTitles)) {
                Console.WriteLine("Skipping pull request due to title filter.");
                return 0;
            }
            if (ShouldSkipByLabels(context.Labels, settings.SkipLabels)) {
                Console.WriteLine("Skipping pull request due to label filter.");
                return 0;
            }

            context = await CleanupService.RunAsync(github, context, settings, CancellationToken.None)
                .ConfigureAwait(false);

            var progress = new ReviewProgress {
                StatusLine = "Starting review."
            };
            var files = await github.GetPullRequestFilesAsync(context.Owner, context.Repo, context.Number, CancellationToken.None)
                .ConfigureAwait(false);

            if (files.Count == 0) {
                Console.WriteLine("No files to review.");
                return 0;
            }

            if (ShouldSkipByPaths(files, settings.SkipPaths)) {
                Console.WriteLine("Skipping pull request due to path filter.");
                return 0;
            }

            progress.Context = ReviewProgressState.Complete;
            progress.Files = ReviewProgressState.Complete;
            progress.StatusLine = "Analyzed changed files.";

            var extras = await BuildExtrasAsync(github, context, settings, CancellationToken.None)
                .ConfigureAwait(false);
            var limitedFiles = PrepareFiles(files, settings.MaxFiles, settings.MaxPatchChars);
            var prompt = PromptBuilder.Build(context, limitedFiles, settings, extras);
            if (settings.RedactPii) {
                prompt = Redaction.Apply(prompt, settings.RedactionPatterns, settings.RedactionReplacement);
            }

            long? commentId = null;
            if (settings.ProgressUpdates) {
                var progressBody = ReviewFormatter.BuildProgressComment(context, settings, progress, null, inlineSupported: false);
                commentId = await CreateOrUpdateProgressCommentAsync(github, context, settings, progressBody, CancellationToken.None)
                    .ConfigureAwait(false);
            }

            var runner = new ReviewRunner(settings);
            progress.Review = ReviewProgressState.InProgress;
            progress.StatusLine = "Generating review findings.";

            Func<string, Task>? onPartial = null;
            if (settings.ProgressUpdates && commentId.HasValue) {
                onPartial = async partial => {
                    var body = ReviewFormatter.BuildProgressComment(context, settings, progress, partial, inlineSupported: false);
                    await github.UpdateIssueCommentAsync(context.Owner, context.Repo, commentId.Value, body, CancellationToken.None)
                        .ConfigureAwait(false);
                };
            }

            var reviewBody = await runner.RunAsync(prompt, onPartial, TimeSpan.FromSeconds(settings.ProgressUpdateSeconds),
                CancellationToken.None).ConfigureAwait(false);

            var commentBody = ReviewFormatter.BuildComment(context, reviewBody, settings, inlineSupported: false);
            progress.Review = ReviewProgressState.Complete;
            progress.Finalize = ReviewProgressState.InProgress;
            progress.StatusLine = "Finalizing summary.";

            if (commentId.HasValue) {
                await github.UpdateIssueCommentAsync(context.Owner, context.Repo, commentId.Value, commentBody, CancellationToken.None)
                    .ConfigureAwait(false);
                Console.WriteLine("Updated review comment.");
            } else if (settings.OverwriteSummary && settings.CommentMode == ReviewCommentMode.Sticky) {
                var existing = await FindExistingSummaryAsync(github, context, CancellationToken.None).ConfigureAwait(false);
                if (existing is not null) {
                    await github.UpdateIssueCommentAsync(context.Owner, context.Repo, existing.Id, commentBody, CancellationToken.None)
                        .ConfigureAwait(false);
                    Console.WriteLine("Updated existing review comment.");
                    return 0;
                }
                await github.CreateIssueCommentAsync(context.Owner, context.Repo, context.Number, commentBody, CancellationToken.None)
                    .ConfigureAwait(false);
                Console.WriteLine("Posted review comment.");
            } else {
                await github.CreateIssueCommentAsync(context.Owner, context.Repo, context.Number, commentBody, CancellationToken.None)
                    .ConfigureAwait(false);
                Console.WriteLine("Posted review comment.");
            }

            return 0;
        } catch (Exception ex) {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static void TryWriteAuthFromEnv() {
        var authJson = Environment.GetEnvironmentVariable("INTELLIGENCEX_AUTH_JSON");
        var authB64 = Environment.GetEnvironmentVariable("INTELLIGENCEX_AUTH_B64");
        if (string.IsNullOrWhiteSpace(authJson) && string.IsNullOrWhiteSpace(authB64)) {
            return;
        }

        string content;
        if (!string.IsNullOrWhiteSpace(authJson)) {
            content = authJson!;
        } else {
            try {
                var bytes = Convert.FromBase64String(authB64!);
                content = Encoding.UTF8.GetString(bytes);
            } catch {
                Console.Error.WriteLine("Failed to decode INTELLIGENCEX_AUTH_B64.");
                return;
            }
        }

        var path = AuthPaths.ResolveAuthPath();
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir)) {
            Directory.CreateDirectory(dir);
        }
        File.WriteAllText(path, content);
    }

    private static bool ShouldSkipByTitle(string title, IReadOnlyList<string> skipTitles) {
        if (skipTitles.Count == 0) {
            return false;
        }
        foreach (var skip in skipTitles) {
            if (!string.IsNullOrWhiteSpace(skip) && title.Contains(skip, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }
        return false;
    }

    private static bool ShouldSkipByLabels(IReadOnlyList<string> labels, IReadOnlyList<string> skipLabels) {
        if (labels.Count == 0 || skipLabels.Count == 0) {
            return false;
        }
        foreach (var label in labels) {
            foreach (var skip in skipLabels) {
                if (!string.IsNullOrWhiteSpace(skip) && label.Equals(skip, StringComparison.OrdinalIgnoreCase)) {
                    return true;
                }
            }
        }
        return false;
    }

    private static bool ShouldSkipByPaths(IReadOnlyList<PullRequestFile> files, IReadOnlyList<string> skipPaths) {
        if (files.Count == 0 || skipPaths.Count == 0) {
            return false;
        }
        var allMatch = true;
        foreach (var file in files) {
            var matches = skipPaths.Any(pattern => GlobMatcher.IsMatch(pattern, file.Filename));
            if (!matches) {
                allMatch = false;
                break;
            }
        }
        return allMatch;
    }

    private static IReadOnlyList<PullRequestFile> PrepareFiles(IReadOnlyList<PullRequestFile> files, int maxFiles, int maxPatchChars) {
        var list = new List<PullRequestFile>();
        var count = 0;
        foreach (var file in files) {
            if (count >= maxFiles) {
                break;
            }
            var patch = file.Patch;
            if (!string.IsNullOrWhiteSpace(patch) && patch.Length > maxPatchChars) {
                patch = patch.Substring(0, maxPatchChars) + "\n... (truncated)";
            }
            list.Add(new PullRequestFile(file.Filename, file.Status, patch));
            count++;
        }
        return list;
    }

    private static async Task<ReviewContextExtras> BuildExtrasAsync(GitHubClient github, PullRequestContext context,
        ReviewSettings settings, CancellationToken cancellationToken) {
        var extras = new ReviewContextExtras();
        if (settings.IncludeIssueComments) {
            var comments = await github.ListIssueCommentsAsync(context.Owner, context.Repo, context.Number, cancellationToken)
                .ConfigureAwait(false);
            extras.IssueCommentsSection = BuildIssueCommentsSection(comments, settings);
        }
        if (settings.IncludeReviewComments) {
            var comments = await github.ListPullRequestReviewCommentsAsync(context.Owner, context.Repo, context.Number, cancellationToken)
                .ConfigureAwait(false);
            extras.ReviewCommentsSection = BuildReviewCommentsSection(comments, settings);
        }
        if (settings.IncludeRelatedPrs) {
            var query = ResolveRelatedPrsQuery(context, settings);
            if (!string.IsNullOrWhiteSpace(query)) {
                var related = await github.SearchPullRequestsAsync(query, settings.MaxRelatedPrs, cancellationToken)
                    .ConfigureAwait(false);
                extras.RelatedPrsSection = BuildRelatedPrsSection(context, related);
            }
        }
        return extras;
    }

    private static string BuildIssueCommentsSection(IReadOnlyList<IssueComment> comments, ReviewSettings settings) {
        var filtered = new List<IssueComment>();
        foreach (var comment in comments) {
            if (string.IsNullOrWhiteSpace(comment.Body)) {
                continue;
            }
            if (comment.Body.Contains(ReviewFormatter.SummaryMarker, StringComparison.OrdinalIgnoreCase) ||
                comment.Body.Contains(CleanupFormatter.SummaryMarker, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            if (!ShouldIncludeComment(comment.Author, comment.Body)) {
                continue;
            }
            filtered.Add(comment);
        }
        if (filtered.Count == 0) {
            return string.Empty;
        }
        var recent = TakeLast(filtered, settings.MaxComments);
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("Issue comments (most recent first):");
        for (var i = recent.Count - 1; i >= 0; i--) {
            var comment = recent[i];
            var author = string.IsNullOrWhiteSpace(comment.Author) ? "unknown" : comment.Author;
            var body = TrimComment(comment.Body, settings.MaxCommentChars);
            sb.AppendLine($"- {author}: {body}");
        }
        sb.AppendLine();
        return sb.ToString();
    }

    private static string BuildReviewCommentsSection(IReadOnlyList<PullRequestReviewComment> comments, ReviewSettings settings) {
        if (comments.Count == 0) {
            return string.Empty;
        }
        var filtered = new List<PullRequestReviewComment>();
        foreach (var comment in comments) {
            if (string.IsNullOrWhiteSpace(comment.Body)) {
                continue;
            }
            if (!ShouldIncludeComment(comment.Author, comment.Body)) {
                continue;
            }
            filtered.Add(comment);
        }
        if (filtered.Count == 0) {
            return string.Empty;
        }
        var recent = TakeLast(filtered, settings.MaxComments);
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("Review comments (most recent first):");
        for (var i = recent.Count - 1; i >= 0; i--) {
            var comment = recent[i];
            var author = string.IsNullOrWhiteSpace(comment.Author) ? "unknown" : comment.Author;
            var body = TrimComment(comment.Body, settings.MaxCommentChars);
            var location = string.IsNullOrWhiteSpace(comment.Path)
                ? string.Empty
                : comment.Line.HasValue
                    ? $" ({comment.Path}:{comment.Line.Value})"
                    : $" ({comment.Path})";
            sb.AppendLine($"- {author}{location}: {body}");
        }
        sb.AppendLine();
        return sb.ToString();
    }

    private static string BuildRelatedPrsSection(PullRequestContext context, IReadOnlyList<RelatedPullRequest> related) {
        if (related.Count == 0) {
            return string.Empty;
        }
        var lines = new List<string>();
        foreach (var pr in related) {
            if (pr.RepoFullName.Equals(context.RepoFullName, StringComparison.OrdinalIgnoreCase) &&
                pr.Number == context.Number) {
                continue;
            }
            lines.Add($"- {pr.RepoFullName}#{pr.Number}: {pr.Title} ({pr.Url})");
        }
        if (lines.Count == 0) {
            return string.Empty;
        }
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("Related pull requests (search results):");
        foreach (var line in lines) {
            sb.AppendLine(line);
        }
        sb.AppendLine();
        return sb.ToString();
    }

    private static string ResolveRelatedPrsQuery(PullRequestContext context, ReviewSettings settings) {
        if (string.IsNullOrWhiteSpace(settings.RelatedPrsQuery)) {
            return string.Empty;
        }
        return settings.RelatedPrsQuery!
            .Replace("{repo}", context.RepoFullName, StringComparison.OrdinalIgnoreCase)
            .Replace("{owner}", context.Owner, StringComparison.OrdinalIgnoreCase)
            .Replace("{name}", context.Repo, StringComparison.OrdinalIgnoreCase)
            .Replace("{number}", context.Number.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static List<T> TakeLast<T>(IReadOnlyList<T> items, int maxItems) {
        if (maxItems <= 0 || items.Count <= maxItems) {
            return new List<T>(items);
        }
        var start = Math.Max(0, items.Count - maxItems);
        var list = new List<T>(maxItems);
        for (var i = start; i < items.Count; i++) {
            list.Add(items[i]);
        }
        return list;
    }

    private static string TrimComment(string value, int maxChars) {
        var text = value.Replace("\r", "").Replace("\n", " ").Trim();
        if (string.IsNullOrWhiteSpace(text)) {
            return "<empty>";
        }
        if (text.Length <= maxChars) {
            return text;
        }
        return text.Substring(0, maxChars) + "...";
    }

    private static bool ShouldIncludeComment(string? author, string body) {
        if (string.IsNullOrWhiteSpace(body)) {
            return false;
        }
        if (!string.IsNullOrWhiteSpace(author)) {
            if (IsBotAuthor(author)) {
                return false;
            }
        }
        if (body.Contains("<!-- intelligencex", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }
        return true;
    }

    private static bool IsBotAuthor(string author) {
        if (author.EndsWith("bot", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }
        return author.Equals("github-actions", StringComparison.OrdinalIgnoreCase) ||
            author.Equals("intelligencex-review", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<IssueComment?> FindExistingSummaryAsync(GitHubClient github, PullRequestContext context,
        CancellationToken cancellationToken) {
        var comments = await github.ListIssueCommentsAsync(context.Owner, context.Repo, context.Number, cancellationToken)
            .ConfigureAwait(false);
        foreach (var comment in comments) {
            if (comment.Body.Contains(ReviewFormatter.SummaryMarker, StringComparison.OrdinalIgnoreCase)) {
                return comment;
            }
        }
        return null;
    }

    private static async Task<long?> CreateOrUpdateProgressCommentAsync(GitHubClient github, PullRequestContext context,
        ReviewSettings settings, string body, CancellationToken cancellationToken) {
        IssueComment? existing = null;
        if (settings.CommentMode == ReviewCommentMode.Sticky) {
            existing = await FindExistingSummaryAsync(github, context, cancellationToken).ConfigureAwait(false);
        }

        if (existing is not null) {
            await github.UpdateIssueCommentAsync(context.Owner, context.Repo, existing.Id, body, cancellationToken)
                .ConfigureAwait(false);
            return existing.Id;
        }

        var created = await github.CreateIssueCommentAsync(context.Owner, context.Repo, context.Number, body, cancellationToken)
            .ConfigureAwait(false);
        return created.Id;
    }

    private static string? GetInput(string name) {
        var value = Environment.GetEnvironmentVariable($"INPUT_{name.ToUpperInvariant()}");
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static int? GetInputInt(string name) {
        var value = GetInput(name);
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }
        if (int.TryParse(value, out var parsed) && parsed > 0) {
            return parsed;
        }
        return null;
    }

    private static (string owner, string repo) SplitRepo(string fullName) {
        var parts = fullName.Split('/');
        if (parts.Length != 2) {
            throw new InvalidOperationException($"Invalid repo name '{fullName}'.");
        }
        return (parts[0], parts[1]);
    }
}

internal static class Program {
    private static Task<int> Main(string[] args) => ReviewerApp.RunAsync(args);
}
