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

internal static class Program {
    private static async Task<int> Main(string[] args) {
        try {
            TryWriteAuthFromEnv();
            var settings = ReviewSettings.Load();
            var token = Environment.GetEnvironmentVariable("INTELLIGENCEX_GITHUB_TOKEN");
            if (string.IsNullOrWhiteSpace(token)) {
                token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
            }

            if (string.IsNullOrWhiteSpace(token)) {
                Console.Error.WriteLine("Missing GITHUB_TOKEN.");
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

            var limitedFiles = PrepareFiles(files, settings.MaxFiles, settings.MaxPatchChars);
            var prompt = PromptBuilder.Build(context, limitedFiles, settings);
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
