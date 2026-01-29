using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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
            var inlineSupported = !string.Equals(settings.Mode, "summary", StringComparison.OrdinalIgnoreCase) &&
                                  settings.MaxInlineComments > 0 &&
                                  !string.IsNullOrWhiteSpace(context.HeadSha);
            var limitedFiles = PrepareFiles(files, settings.MaxFiles, settings.MaxPatchChars);
            var prompt = PromptBuilder.Build(context, limitedFiles, settings, extras, inlineSupported);
            if (settings.RedactPii) {
                prompt = Redaction.Apply(prompt, settings.RedactionPatterns, settings.RedactionReplacement);
            }

            long? commentId = null;
            if (settings.ProgressUpdates) {
                var progressBody = ReviewFormatter.BuildProgressComment(context, settings, progress, null, inlineSupported);
                commentId = await CreateOrUpdateProgressCommentAsync(github, context, settings, progressBody, CancellationToken.None)
                    .ConfigureAwait(false);
            }

            var runner = new ReviewRunner(settings);
            progress.Review = ReviewProgressState.InProgress;
            progress.StatusLine = "Generating review findings.";

            Func<string, Task>? onPartial = null;
            if (settings.ProgressUpdates && commentId.HasValue) {
                onPartial = async partial => {
                    var body = ReviewFormatter.BuildProgressComment(context, settings, progress, partial, inlineSupported);
                    await github.UpdateIssueCommentAsync(context.Owner, context.Repo, commentId.Value, body, CancellationToken.None)
                        .ConfigureAwait(false);
                };
            }

            var reviewBody = await runner.RunAsync(prompt, onPartial, TimeSpan.FromSeconds(settings.ProgressUpdateSeconds),
                CancellationToken.None).ConfigureAwait(false);

            var inlineComments = Array.Empty<InlineReviewComment>();
            var summaryBody = reviewBody;
            if (inlineSupported) {
                var inlineResult = ReviewInlineParser.Extract(reviewBody, settings.MaxInlineComments);
                inlineComments = inlineResult.Comments as InlineReviewComment[] ?? inlineResult.Comments.ToArray();
                if (inlineResult.HadInlineSection && !string.IsNullOrWhiteSpace(inlineResult.Body)) {
                    summaryBody = inlineResult.Body;
                }
            }

            if (inlineSupported && inlineComments.Length > 0) {
                await PostInlineCommentsAsync(github, context, files, settings, inlineComments, CancellationToken.None)
                    .ConfigureAwait(false);
            }

            var commentBody = ReviewFormatter.BuildComment(context, summaryBody, settings, inlineSupported);
            progress.Review = ReviewProgressState.Complete;
            progress.Finalize = ReviewProgressState.InProgress;
            progress.StatusLine = "Finalizing summary.";

            if (commentId.HasValue) {
                await github.UpdateIssueCommentAsync(context.Owner, context.Repo, commentId.Value, commentBody, CancellationToken.None)
                    .ConfigureAwait(false);
                Console.WriteLine("Updated review comment.");
            } else if (settings.OverwriteSummary && settings.CommentMode == ReviewCommentMode.Sticky) {
                var existing = await FindExistingSummaryAsync(github, context, settings, CancellationToken.None).ConfigureAwait(false);
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
                var headSize = Math.Max(0, maxPatchChars / 2);
                var tailSize = Math.Max(0, maxPatchChars - headSize);
                if (headSize + tailSize > patch.Length) {
                    patch = patch.Substring(0, maxPatchChars) + "\n... (truncated)";
                } else {
                    var head = patch.Substring(0, headSize);
                    var tail = patch.Substring(patch.Length - tailSize);
                    patch = head + "\n... (truncated) ...\n" + tail;
                }
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
            var comments = await github.ListIssueCommentsAsync(context.Owner, context.Repo, context.Number, settings.MaxComments, cancellationToken)
                .ConfigureAwait(false);
            extras.IssueCommentsSection = BuildIssueCommentsSection(comments, settings);
        }
        if (settings.IncludeReviewComments) {
            var comments = await github.ListPullRequestReviewCommentsAsync(context.Owner, context.Repo, context.Number, settings.MaxComments, cancellationToken)
                .ConfigureAwait(false);
            extras.ReviewCommentsSection = BuildReviewCommentsSection(comments, settings);
        }
        if (settings.IncludeReviewThreads) {
            var threads = await github.ListPullRequestReviewThreadsAsync(context.Owner, context.Repo, context.Number,
                    settings.ReviewThreadsMax, settings.ReviewThreadsMaxComments, cancellationToken)
                .ConfigureAwait(false);
            if (settings.ReviewThreadsAutoResolveStale) {
                await AutoResolveStaleThreadsAsync(github, threads, settings, cancellationToken).ConfigureAwait(false);
            }
            extras.ReviewThreadsSection = BuildReviewThreadsSection(threads, settings);
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
            if (!ShouldIncludeComment(comment.Author, comment.Body, settings)) {
                continue;
            }
            filtered.Add(comment);
        }
        if (filtered.Count == 0) {
            return string.Empty;
        }
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("Issue comments (most recent first):");
        foreach (var comment in filtered) {
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
            if (!ShouldIncludeComment(comment.Author, comment.Body, settings)) {
                continue;
            }
            filtered.Add(comment);
        }
        if (filtered.Count == 0) {
            return string.Empty;
        }
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("Review comments (most recent first):");
        foreach (var comment in filtered) {
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

    private static string BuildReviewThreadsSection(IReadOnlyList<PullRequestReviewThread> threads, ReviewSettings settings) {
        if (threads.Count == 0) {
            return string.Empty;
        }

        var lines = new List<string>();
        foreach (var thread in threads) {
            if (!settings.ReviewThreadsIncludeResolved && thread.IsResolved) {
                continue;
            }
            if (!settings.ReviewThreadsIncludeOutdated && thread.IsOutdated) {
                continue;
            }

            var status = thread.IsOutdated ? "stale" : thread.IsResolved ? "resolved" : "active";
            var perThread = 0;
            foreach (var comment in thread.Comments) {
                if (perThread >= settings.ReviewThreadsMaxComments) {
                    break;
                }
                if (!ShouldIncludeThreadComment(comment.Author, comment.Body, settings)) {
                    continue;
                }
                var author = string.IsNullOrWhiteSpace(comment.Author) ? "unknown" : comment.Author!;
                var body = TrimComment(comment.Body, settings.MaxCommentChars);
                var location = string.IsNullOrWhiteSpace(comment.Path)
                    ? string.Empty
                    : comment.Line.HasValue
                        ? $" ({comment.Path}:{comment.Line.Value})"
                        : $" ({comment.Path})";
                lines.Add($"- [{status}] {author}{location}: {body}");
                perThread++;
            }
        }

        if (lines.Count == 0) {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("Other reviewer threads (status: active/resolved/stale):");
        foreach (var line in lines) {
            sb.AppendLine(line);
        }
        sb.AppendLine();
        return sb.ToString();
    }

    private static async Task AutoResolveStaleThreadsAsync(GitHubClient github, IReadOnlyList<PullRequestReviewThread> threads,
        ReviewSettings settings, CancellationToken cancellationToken) {
        var resolved = 0;
        foreach (var thread in threads) {
            if (resolved >= settings.ReviewThreadsAutoResolveMax) {
                break;
            }
            if (thread.IsResolved || !thread.IsOutdated) {
                continue;
            }
            if (settings.ReviewThreadsAutoResolveBotsOnly && !ThreadHasOnlyBotComments(thread)) {
                continue;
            }

            try {
                await github.ResolveReviewThreadAsync(thread.Id, cancellationToken).ConfigureAwait(false);
                resolved++;
            } catch (Exception ex) {
                Console.Error.WriteLine($"Failed to resolve review thread {thread.Id}: {ex.Message}");
            }
        }
    }

    private static bool ThreadHasOnlyBotComments(PullRequestReviewThread thread) {
        if (thread.Comments.Count == 0) {
            return false;
        }
        foreach (var comment in thread.Comments) {
            if (string.IsNullOrWhiteSpace(comment.Author)) {
                return false;
            }
            if (!IsBotAuthor(comment.Author)) {
                return false;
            }
        }
        return true;
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

    private static bool ShouldIncludeComment(string? author, string body, ReviewSettings settings) {
        if (string.IsNullOrWhiteSpace(body)) {
            return false;
        }
        if (settings.ContextDenyEnabled && settings.ContextDenyPatterns.Count > 0) {
            if (ContextDenyMatcher.Matches(body, settings.ContextDenyPatterns)) {
                return false;
            }
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

    private static bool ShouldIncludeThreadComment(string? author, string body, ReviewSettings settings) {
        if (string.IsNullOrWhiteSpace(body)) {
            return false;
        }
        if (settings.ContextDenyEnabled && settings.ContextDenyPatterns.Count > 0) {
            if (ContextDenyMatcher.Matches(body, settings.ContextDenyPatterns)) {
                return false;
            }
        }
        if (!settings.ReviewThreadsIncludeBots && !string.IsNullOrWhiteSpace(author)) {
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
        if (author.EndsWith("[bot]", StringComparison.OrdinalIgnoreCase) ||
            author.EndsWith("bot", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }
        return author.Equals("github-actions", StringComparison.OrdinalIgnoreCase) ||
            author.Equals("intelligencex-review", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task PostInlineCommentsAsync(GitHubClient github, PullRequestContext context,
        IReadOnlyList<PullRequestFile> files, ReviewSettings settings, IReadOnlyList<InlineReviewComment> inlineComments,
        CancellationToken cancellationToken) {
        if (inlineComments.Count == 0 || string.IsNullOrWhiteSpace(context.HeadSha)) {
            return;
        }

        var lineMap = BuildInlineLineMap(files);
        var patchIndex = BuildInlinePatchIndex(files);
        if (lineMap.Count == 0) {
            return;
        }

        var limit = Math.Max(0, settings.CommentSearchLimit);
        var existing = await github.ListPullRequestReviewCommentsAsync(context.Owner, context.Repo, context.Number, limit, cancellationToken)
            .ConfigureAwait(false);
        var existingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var comment in existing) {
            if (string.IsNullOrWhiteSpace(comment.Path) || !comment.Line.HasValue) {
                continue;
            }
            if (!comment.Body.Contains(ReviewFormatter.InlineMarker, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            existingKeys.Add(BuildInlineKey(comment.Path!, comment.Line.Value));
        }

        var posted = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var inline in inlineComments) {
            if (posted >= settings.MaxInlineComments) {
                break;
            }
            var normalizedPath = NormalizePath(inline.Path);
            var lineNumber = inline.Line;
            if ((string.IsNullOrWhiteSpace(normalizedPath) || lineNumber <= 0) &&
                !string.IsNullOrWhiteSpace(inline.Snippet)) {
                if (!TryResolveSnippet(inline.Snippet!, patchIndex, normalizedPath, out normalizedPath, out lineNumber)) {
                    continue;
                }
            }
            if (string.IsNullOrWhiteSpace(normalizedPath) || lineNumber <= 0) {
                continue;
            }
            if (!lineMap.TryGetValue(normalizedPath, out var allowedLines) ||
                !allowedLines.Contains(lineNumber)) {
                continue;
            }
            var key = BuildInlineKey(normalizedPath, lineNumber);
            if (existingKeys.Contains(key) || !seen.Add(key)) {
                continue;
            }

            var body = inline.Body.Trim();
            if (string.IsNullOrWhiteSpace(body)) {
                continue;
            }
            body = $"{ReviewFormatter.InlineMarker}\n{body}";

            try {
                await github.CreatePullRequestReviewCommentAsync(context.Owner, context.Repo, context.Number, body,
                        context.HeadSha!, normalizedPath, lineNumber, cancellationToken)
                    .ConfigureAwait(false);
                posted++;
            } catch (Exception ex) {
                Console.Error.WriteLine($"Inline comment failed for {normalizedPath}:{lineNumber} - {ex.Message}");
            }
        }
    }

    private static Dictionary<string, HashSet<int>> BuildInlineLineMap(IReadOnlyList<PullRequestFile> files) {
        var map = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files) {
            if (string.IsNullOrWhiteSpace(file.Patch)) {
                continue;
            }
            var normalizedPath = NormalizePath(file.Filename);
            if (string.IsNullOrWhiteSpace(normalizedPath)) {
                continue;
            }
            var allowed = ParsePatchLines(file.Patch!);
            if (allowed.Count > 0) {
                map[normalizedPath] = allowed;
            }
        }
        return map;
    }

    private sealed record PatchLine(int LineNumber, string Text, string NormalizedText);

    private static Dictionary<string, List<PatchLine>> BuildInlinePatchIndex(IReadOnlyList<PullRequestFile> files) {
        var index = new Dictionary<string, List<PatchLine>>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files) {
            if (string.IsNullOrWhiteSpace(file.Patch)) {
                continue;
            }
            var normalizedPath = NormalizePath(file.Filename);
            if (string.IsNullOrWhiteSpace(normalizedPath)) {
                continue;
            }
            var lines = ParsePatchContent(file.Patch!);
            if (lines.Count > 0) {
                index[normalizedPath] = lines;
            }
        }
        return index;
    }

    private static HashSet<int> ParsePatchLines(string patch) {
        var allowed = new HashSet<int>();
        var lines = patch.Replace("\r\n", "\n").Split('\n');
        var oldLine = 0;
        var newLine = 0;
        foreach (var line in lines) {
            if (line.StartsWith("@@", StringComparison.Ordinal)) {
                var match = Regex.Match(line, @"@@ -(\d+)(?:,\d+)? \+(\d+)(?:,\d+)? @@");
                if (match.Success) {
                    oldLine = int.Parse(match.Groups[1].Value) - 1;
                    newLine = int.Parse(match.Groups[2].Value) - 1;
                }
                continue;
            }
            if (line.StartsWith("+", StringComparison.Ordinal) && !line.StartsWith("+++", StringComparison.Ordinal)) {
                newLine++;
                allowed.Add(newLine);
                continue;
            }
            if (line.StartsWith("-", StringComparison.Ordinal) && !line.StartsWith("---", StringComparison.Ordinal)) {
                oldLine++;
                continue;
            }
            if (line.StartsWith(" ", StringComparison.Ordinal)) {
                oldLine++;
                newLine++;
                allowed.Add(newLine);
            }
        }
        return allowed;
    }

    private static List<PatchLine> ParsePatchContent(string patch) {
        var results = new List<PatchLine>();
        var lines = patch.Replace("\r\n", "\n").Split('\n');
        var oldLine = 0;
        var newLine = 0;
        foreach (var line in lines) {
            if (line.StartsWith("@@", StringComparison.Ordinal)) {
                var match = Regex.Match(line, @"@@ -(\d+)(?:,\d+)? \+(\d+)(?:,\d+)? @@");
                if (match.Success) {
                    oldLine = int.Parse(match.Groups[1].Value) - 1;
                    newLine = int.Parse(match.Groups[2].Value) - 1;
                }
                continue;
            }
            if (line.StartsWith("+", StringComparison.Ordinal) && !line.StartsWith("+++", StringComparison.Ordinal)) {
                newLine++;
                AddPatchLine(results, newLine, line.Substring(1));
                continue;
            }
            if (line.StartsWith("-", StringComparison.Ordinal) && !line.StartsWith("---", StringComparison.Ordinal)) {
                oldLine++;
                continue;
            }
            if (line.StartsWith(" ", StringComparison.Ordinal)) {
                oldLine++;
                newLine++;
                AddPatchLine(results, newLine, line.Substring(1));
            }
        }
        return results;
    }

    private static void AddPatchLine(List<PatchLine> results, int lineNumber, string text) {
        var normalized = NormalizeSnippetText(text);
        if (string.IsNullOrWhiteSpace(normalized)) {
            return;
        }
        results.Add(new PatchLine(lineNumber, text, normalized));
    }

    private static bool TryResolveSnippet(string snippet, Dictionary<string, List<PatchLine>> patchIndex, string? preferredPath,
        out string path, out int lineNumber) {
        path = string.Empty;
        lineNumber = 0;
        var normalizedSnippet = NormalizeSnippetText(snippet);
        if (string.IsNullOrWhiteSpace(normalizedSnippet) || normalizedSnippet.Length < 3) {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(preferredPath)) {
            var normalizedPreferred = NormalizePath(preferredPath);
            if (patchIndex.TryGetValue(normalizedPreferred, out var lines) &&
                TryResolveSnippetInLines(normalizedSnippet, normalizedPreferred, lines, out path, out lineNumber)) {
                return true;
            }
            return false;
        }

        var candidates = new List<(string path, int line, string normalized)>();
        foreach (var (filePath, lines) in patchIndex) {
            foreach (var line in lines) {
                if (line.NormalizedText.Contains(normalizedSnippet, StringComparison.Ordinal)) {
                    candidates.Add((filePath, line.LineNumber, line.NormalizedText));
                }
            }
        }

        if (candidates.Count == 1) {
            path = candidates[0].path;
            lineNumber = candidates[0].line;
            return true;
        }

        if (candidates.Count > 1) {
            var exact = candidates.Where(candidate => candidate.normalized.Equals(normalizedSnippet, StringComparison.Ordinal)).ToList();
            if (exact.Count == 1) {
                path = exact[0].path;
                lineNumber = exact[0].line;
                return true;
            }
        }

        return false;
    }

    private static bool TryResolveSnippetInLines(string normalizedSnippet, string path,
        IReadOnlyList<PatchLine> lines, out string resolvedPath, out int resolvedLine) {
        resolvedPath = string.Empty;
        resolvedLine = 0;
        var candidates = new List<PatchLine>();
        foreach (var line in lines) {
            if (line.NormalizedText.Contains(normalizedSnippet, StringComparison.Ordinal)) {
                candidates.Add(line);
            }
        }

        if (candidates.Count == 1) {
            resolvedPath = path;
            resolvedLine = candidates[0].LineNumber;
            return true;
        }

        if (candidates.Count > 1) {
            var exact = candidates.Where(line => line.NormalizedText.Equals(normalizedSnippet, StringComparison.Ordinal)).ToList();
            if (exact.Count == 1) {
                resolvedPath = path;
                resolvedLine = exact[0].LineNumber;
                return true;
            }
        }

        return false;
    }

    private static string NormalizeSnippetText(string text) {
        if (string.IsNullOrWhiteSpace(text)) {
            return string.Empty;
        }
        var trimmed = text.Trim();
        if (trimmed.Length == 0) {
            return string.Empty;
        }
        var buffer = new System.Text.StringBuilder(trimmed.Length);
        var inWhitespace = false;
        foreach (var ch in trimmed) {
            if (char.IsWhiteSpace(ch)) {
                if (!inWhitespace) {
                    buffer.Append(' ');
                    inWhitespace = true;
                }
            } else {
                buffer.Append(ch);
                inWhitespace = false;
            }
        }
        return buffer.ToString();
    }

    private static string NormalizePath(string path) {
        var normalized = path.Replace('\\', '/').Trim();
        if (normalized.StartsWith("./", StringComparison.Ordinal)) {
            normalized = normalized.Substring(2);
        }
        return normalized;
    }

    private static string BuildInlineKey(string path, int line) {
        return $"{NormalizePath(path)}:{line}";
    }

    private static async Task<IssueComment?> FindExistingSummaryAsync(GitHubClient github, PullRequestContext context,
        ReviewSettings settings, CancellationToken cancellationToken) {
        var limit = Math.Max(0, settings.CommentSearchLimit);
        var comments = await github.ListIssueCommentsAsync(context.Owner, context.Repo, context.Number, limit, cancellationToken)
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
            existing = await FindExistingSummaryAsync(github, context, settings, cancellationToken).ConfigureAwait(false);
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

