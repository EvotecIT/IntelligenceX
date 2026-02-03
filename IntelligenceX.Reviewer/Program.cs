using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Json;
using IntelligenceX.OpenAI.Auth;
using IntelligenceX.OpenAI.Native;
using IntelligenceX.OpenAI.Usage;

namespace IntelligenceX.Reviewer;

/// <summary>
/// Entry point for running the IntelligenceX GitHub review workflow.
/// </summary>
public static class ReviewerApp {
    private const string ThreadReplyMarker = "<!-- intelligencex:thread-reply -->";
    /// <summary>
    /// Executes the reviewer workflow with the provided arguments.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    /// <returns>Process exit code (0 for success).</returns>
    public static async Task<int> RunAsync(string[] args) {
        using var cts = new CancellationTokenSource();
        ConsoleCancelEventHandler? cancelHandler = (_, evt) => {
            evt.Cancel = true;
            cts.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;
        try {
            var cancellationToken = cts.Token;
            if (!await TryWriteAuthFromEnvAsync().ConfigureAwait(false)) {
                return 1;
            }
            var settings = ReviewSettings.Load();
            var validation = ReviewConfigValidator.ValidateCurrent();
            if (validation is not null) {
                if (validation.Warnings.Count > 0) {
                    Console.Error.WriteLine($"Configuration warnings in {validation.ConfigPath}:");
                    foreach (var warning in validation.Warnings) {
                        Console.Error.WriteLine($"- {warning.Path}: {warning.Message}");
                    }
                    Console.Error.WriteLine($"Schema: {validation.SchemaHint}");
                }
                if (validation.Errors.Count > 0) {
                    Console.Error.WriteLine($"Configuration errors in {validation.ConfigPath}:");
                    foreach (var error in validation.Errors) {
                        Console.Error.WriteLine($"- {error.Path}: {error.Message}");
                    }
                    Console.Error.WriteLine($"Schema: {validation.SchemaHint}");
                    return 1;
                }
            }
            if (!await ValidateAuthAsync(settings).ConfigureAwait(false)) {
                return 1;
            }
            var token = Environment.GetEnvironmentVariable("INTELLIGENCEX_GITHUB_TOKEN")
                ?? Environment.GetEnvironmentVariable("GITHUB_TOKEN");

            if (string.IsNullOrWhiteSpace(token)) {
                Console.Error.WriteLine("Missing GitHub token (INTELLIGENCEX_GITHUB_TOKEN or GITHUB_TOKEN).");
                return 1;
            }

            var fallbackToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
            if (string.Equals(token, fallbackToken, StringComparison.Ordinal)) {
                fallbackToken = null;
            }

            using var github = new GitHubClient(token);
            using var fallbackGithub = string.IsNullOrWhiteSpace(fallbackToken) ? null : new GitHubClient(fallbackToken);
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
                context = await github.GetPullRequestAsync(owner, repo, prNumber.Value, cancellationToken)
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

            context = await CleanupService.RunAsync(github, context, settings, cancellationToken)
                .ConfigureAwait(false);

            var progress = new ReviewProgress {
                StatusLine = "Starting review."
            };
            var files = await github.GetPullRequestFilesAsync(context.Owner, context.Repo, context.Number, cancellationToken)
                .ConfigureAwait(false);

            if (files.Count == 0) {
                Console.WriteLine("No files to review.");
                return 0;
            }

            if (ShouldSkipByPaths(files, settings.SkipPaths)) {
                Console.WriteLine("Skipping pull request due to path filter.");
                return 0;
            }

            var allFiles = files;
            var (reviewFiles, diffNote) = await ResolveReviewFilesAsync(github, context, settings, files, cancellationToken)
                .ConfigureAwait(false);
            reviewFiles = FilterFilesByPaths(reviewFiles, settings.IncludePaths, settings.ExcludePaths);
            if (reviewFiles.Count == 0) {
                progress.Context = ReviewProgressState.Complete;
                Console.WriteLine("No files matched include/exclude filters.");
                return 0;
            }
            files = reviewFiles;

            progress.Context = ReviewProgressState.Complete;
            progress.Files = ReviewProgressState.Complete;
            progress.StatusLine = "Analyzed changed files.";

            var extras = await BuildExtrasAsync(github, fallbackGithub, context, settings, cancellationToken, settings.TriageOnly)
                .ConfigureAwait(false);
            if (settings.TriageOnly) {
                var triageRunner = new ReviewRunner(settings);
                var (triageOnlyFiles, triageOnlyNote) = await ResolveThreadTriageFilesAsync(github, context, settings, allFiles, cancellationToken)
                    .ConfigureAwait(false);
                var triageOnlyResult = await MaybeAutoResolveAssessedThreadsAsync(github, fallbackGithub, triageRunner, context, triageOnlyFiles,
                        settings, extras, false, triageOnlyNote, cancellationToken, true, false)
                    .ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(triageOnlyResult.EmbeddedBlock)) {
                    await github.CreateIssueCommentAsync(context.Owner, context.Repo, context.Number, triageOnlyResult.EmbeddedBlock, cancellationToken)
                        .ConfigureAwait(false);
                    Console.WriteLine("Posted thread triage comment.");
                } else {
                    Console.WriteLine("No threads eligible for triage.");
                }
                return 0;
            }
            var inlineSupported = !string.Equals(settings.Mode, "summary", StringComparison.OrdinalIgnoreCase) &&
                                  settings.MaxInlineComments > 0 &&
                                  !string.IsNullOrWhiteSpace(context.HeadSha);
            var limitedFiles = PrepareFiles(files, settings.MaxFiles, settings.MaxPatchChars);
            var prompt = PromptBuilder.Build(context, limitedFiles, settings, diffNote, extras, inlineSupported);
            if (settings.RedactPii) {
                prompt = Redaction.Apply(prompt, settings.RedactionPatterns, settings.RedactionReplacement);
            }

            long? commentId = null;
            if (settings.ProgressUpdates) {
                var progressBody = ReviewFormatter.BuildProgressComment(context, settings, progress, null, inlineSupported);
                commentId = await CreateOrUpdateProgressCommentAsync(github, context, settings, progressBody, cancellationToken)
                    .ConfigureAwait(false);
            }

            var runner = new ReviewRunner(settings);
            progress.Review = ReviewProgressState.InProgress;
            progress.StatusLine = "Generating review findings.";

            Func<string, Task>? onPartial = null;
            if (settings.ProgressUpdates && commentId.HasValue) {
                onPartial = async partial => {
                    var body = ReviewFormatter.BuildProgressComment(context, settings, progress, partial, inlineSupported);
                    await github.UpdateIssueCommentAsync(context.Owner, context.Repo, commentId.Value, body, cancellationToken)
                        .ConfigureAwait(false);
                };
            }

            var reviewBody = await runner.RunAsync(prompt, onPartial, TimeSpan.FromSeconds(settings.ProgressUpdateSeconds),
                cancellationToken).ConfigureAwait(false);

            var reviewFailed = ReviewDiagnostics.IsFailureBody(reviewBody);
            var inlineAllowed = inlineSupported && !reviewFailed;
            var inlineComments = Array.Empty<InlineReviewComment>();
            var summaryBody = reviewBody;
            if (inlineAllowed) {
                var inlineResult = ReviewInlineParser.Extract(reviewBody, settings.MaxInlineComments);
                inlineComments = inlineResult.Comments as InlineReviewComment[] ?? inlineResult.Comments.ToArray();
                if (inlineResult.HadInlineSection && !string.IsNullOrWhiteSpace(inlineResult.Body)) {
                    summaryBody = inlineResult.Body;
                }
            }

            HashSet<string>? inlineKeys = null;
            if (inlineAllowed) {
                inlineKeys = await PostInlineCommentsAsync(github, context, files, settings, inlineComments, cancellationToken)
                    .ConfigureAwait(false);
                if (settings.ReviewThreadsAutoResolveMissingInline &&
                    !string.IsNullOrWhiteSpace(context.HeadSha) &&
                    inlineKeys is not null &&
                    inlineKeys.Count > 0) {
                    await AutoResolveMissingInlineThreadsAsync(github, fallbackGithub, context, inlineKeys, settings, cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            var (triageFiles, triageNote) = await ResolveThreadTriageFilesAsync(github, context, settings, allFiles, cancellationToken)
                .ConfigureAwait(false);
            var triageResult = await MaybeAutoResolveAssessedThreadsAsync(github, fallbackGithub, runner, context, triageFiles,
                    settings, extras, reviewFailed, triageNote, cancellationToken, false,
                    settings.ReviewThreadsAutoResolveAIPostComment)
                .ConfigureAwait(false);
            if (settings.ReviewThreadsAutoResolveAIEmbed && !string.IsNullOrWhiteSpace(triageResult.EmbeddedBlock)) {
                summaryBody = summaryBody.TrimEnd() + "\n\n" + triageResult.EmbeddedBlock.Trim();
            }
            var inlineSuppressed = inlineSupported && !inlineAllowed;
            var autoResolveSummary = settings.ReviewThreadsAutoResolveAISummary ? triageResult.SummaryLine : string.Empty;
            var usageLine = await TryBuildUsageLineAsync(settings).ConfigureAwait(false);
            var commentBody = ReviewFormatter.BuildComment(context, summaryBody, settings, inlineSupported, inlineSuppressed,
                autoResolveSummary, usageLine);
            progress.Review = ReviewProgressState.Complete;
            progress.Finalize = ReviewProgressState.InProgress;
            progress.StatusLine = "Finalizing summary.";

            if (commentId.HasValue) {
                await github.UpdateIssueCommentAsync(context.Owner, context.Repo, commentId.Value, commentBody, cancellationToken)
                    .ConfigureAwait(false);
                Console.WriteLine("Updated review comment.");
            } else if (settings.CommentMode == ReviewCommentMode.Sticky) {
                var shouldSearch = settings.OverwriteSummary || settings.OverwriteSummaryOnNewCommit;
                IssueComment? existing = null;
                if (shouldSearch) {
                    existing = await FindExistingSummaryAsync(github, context, settings, cancellationToken).ConfigureAwait(false);
                }
                var shouldOverwrite = settings.OverwriteSummary ||
                                      (settings.OverwriteSummaryOnNewCommit && IsSummaryOutdated(existing, context.HeadSha));
                if (existing is not null && shouldOverwrite) {
                    await github.UpdateIssueCommentAsync(context.Owner, context.Repo, existing.Id, commentBody, cancellationToken)
                        .ConfigureAwait(false);
                    Console.WriteLine("Updated existing review comment.");
                    return 0;
                }
                await github.CreateIssueCommentAsync(context.Owner, context.Repo, context.Number, commentBody, cancellationToken)
                    .ConfigureAwait(false);
                Console.WriteLine("Posted review comment.");
            } else {
                await github.CreateIssueCommentAsync(context.Owner, context.Repo, context.Number, commentBody, cancellationToken)
                    .ConfigureAwait(false);
                Console.WriteLine("Posted review comment.");
            }

            return 0;
        } catch (Exception ex) {
            Console.Error.WriteLine(ex.Message);
            return 1;
        } finally {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    private static async Task<bool> TryWriteAuthFromEnvAsync() {
        var authJson = Environment.GetEnvironmentVariable("INTELLIGENCEX_AUTH_JSON");
        var authB64 = Environment.GetEnvironmentVariable("INTELLIGENCEX_AUTH_B64");
        if (string.IsNullOrWhiteSpace(authJson) && string.IsNullOrWhiteSpace(authB64)) {
            return true;
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
                return false;
            }
        }

        if (IsEncryptedStore(content) && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("INTELLIGENCEX_AUTH_KEY"))) {
            Console.Error.WriteLine("Auth store is encrypted but INTELLIGENCEX_AUTH_KEY is not set.");
            return false;
        }

        if (HasAuthStoreBundles(content)) {
            WriteAuthStoreContent(content);
            return true;
        }

        var bundle = AuthBundleSerializer.Deserialize(content);
        if (bundle is not null) {
            try {
                var store = new FileAuthBundleStore();
                await store.SaveAsync(bundle).ConfigureAwait(false);
                return true;
            } catch (Exception ex) {
                Console.Error.WriteLine($"Failed to write auth bundle: {ex.Message}");
                return false;
            }
        }

        Console.Error.WriteLine("Auth bundle content is invalid.");
        return false;
    }

    private static void WriteAuthStoreContent(string content) {
        var path = AuthPaths.ResolveAuthPath();
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir)) {
            Directory.CreateDirectory(dir);
        }
        File.WriteAllText(path, content);
    }

    private static bool IsEncryptedStore(string content) {
        return content.TrimStart().StartsWith("{\"encrypted\":", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasAuthStoreBundles(string content) {
        var value = JsonLite.Parse(content);
        var obj = value?.AsObject();
        if (obj is null) {
            return false;
        }
        var bundles = obj.GetObject("bundles");
        if (bundles is null) {
            return false;
        }
        foreach (var entry in bundles) {
            if (entry.Value?.AsObject() is not null) {
                return true;
            }
        }
        return false;
    }

    private static async Task<bool> ValidateAuthAsync(ReviewSettings settings) {
        if (settings.Provider == ReviewProvider.Copilot) {
            return true;
        }

        var authPath = AuthPaths.ResolveAuthPath();
        if (!File.Exists(authPath)) {
            Console.Error.WriteLine("Missing OpenAI auth store.");
            Console.Error.WriteLine("Set INTELLIGENCEX_AUTH_B64 (store export) or run `intelligencex auth login`.");
            return false;
        }

        try {
            var store = new FileAuthBundleStore();
            var bundle = await store.GetAsync("openai-codex").ConfigureAwait(false)
                         ?? await store.GetAsync("openai").ConfigureAwait(false)
                         ?? await store.GetAsync("chatgpt").ConfigureAwait(false);
            if (bundle is null) {
                Console.Error.WriteLine($"No OpenAI auth bundle found in {authPath}.");
                Console.Error.WriteLine("Export a store bundle with `intelligencex auth export --format store-base64`.");
                return false;
            }
            return true;
        } catch (Exception ex) {
            Console.Error.WriteLine($"Failed to load auth store: {ex.Message}");
            if (ex.Message.Contains("INTELLIGENCEX_AUTH_KEY", StringComparison.OrdinalIgnoreCase)) {
                Console.Error.WriteLine("Set INTELLIGENCEX_AUTH_KEY to decrypt the auth store.");
            }
            return false;
        }
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
            if (!string.IsNullOrWhiteSpace(patch)) {
                patch = TrimPatch(patch, maxPatchChars);
            }
            list.Add(new PullRequestFile(file.Filename, file.Status, patch));
            count++;
        }
        return list;
    }

    /// <summary>
    /// Filters pull request files using include/exclude glob patterns.
    /// Include patterns are evaluated first; exclude patterns are applied to the remaining files.
    /// </summary>
    internal static IReadOnlyList<PullRequestFile> FilterFilesByPaths(IReadOnlyList<PullRequestFile> files,
        IReadOnlyList<string> includePaths, IReadOnlyList<string> excludePaths) {
        if (files.Count == 0) {
            return files;
        }
        includePaths ??= Array.Empty<string>();
        excludePaths ??= Array.Empty<string>();
        var hasInclude = includePaths.Count > 0;
        var hasExclude = excludePaths.Count > 0;
        if (!hasInclude && !hasExclude) {
            return files;
        }
        var filtered = new List<PullRequestFile>();
        foreach (var file in files) {
            var filename = file.Filename.Replace('\\', '/');
            if (hasInclude && !includePaths.Any(pattern => GlobMatcher.IsMatch(pattern, filename))) {
                continue;
            }
            if (hasExclude && excludePaths.Any(pattern => GlobMatcher.IsMatch(pattern, filename))) {
                continue;
            }
            filtered.Add(file);
        }
        return filtered;
    }

    private static async Task<(IReadOnlyList<PullRequestFile> Files, string DiffNote)> ResolveReviewFilesAsync(
        GitHubClient github, PullRequestContext context, ReviewSettings settings, IReadOnlyList<PullRequestFile> currentFiles,
        CancellationToken cancellationToken) {
        var range = ReviewSettings.NormalizeDiffRange(settings.ReviewDiffRange, "current");
        return await ResolveDiffRangeFilesAsync(github, context, range, currentFiles, settings, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<ReviewContextExtras> BuildExtrasAsync(GitHubClient github, GitHubClient? fallbackGithub,
        PullRequestContext context, ReviewSettings settings, CancellationToken cancellationToken, bool forceReviewThreads) {
        var extras = new ReviewContextExtras();
        if (settings.IncludeIssueComments) {
            var comments = await github.ListIssueCommentsAsync(context.Owner, context.Repo, context.Number, settings.MaxComments, cancellationToken)
                .ConfigureAwait(false);
            extras.IssueCommentsSection = BuildIssueCommentsSection(comments, settings);
        }
        var loadThreads = forceReviewThreads || settings.IncludeReviewThreads || settings.ReviewThreadsAutoResolveAI;
        if (loadThreads) {
            var threads = await github.ListPullRequestReviewThreadsAsync(context.Owner, context.Repo, context.Number,
                    settings.ReviewThreadsMax, settings.ReviewThreadsMaxComments, cancellationToken)
                .ConfigureAwait(false);
            extras.ReviewThreads = threads;
            if (settings.ReviewThreadsAutoResolveStale) {
                await AutoResolveStaleThreadsAsync(github, fallbackGithub, threads, settings, cancellationToken).ConfigureAwait(false);
            }
            if (settings.IncludeReviewThreads) {
                extras.ReviewThreadsSection = BuildReviewThreadsSection(threads, settings);
            }
        }
        if (settings.IncludeReviewComments && string.IsNullOrEmpty(extras.ReviewThreadsSection)) {
            var comments = await github.ListPullRequestReviewCommentsAsync(context.Owner, context.Repo, context.Number, settings.MaxComments, cancellationToken)
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
        if (threads.Count == 0 || settings.ReviewThreadsMaxComments <= 0) {
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

            var status = thread.IsResolved && thread.IsOutdated
                ? "resolved (stale)"
                : thread.IsResolved
                    ? "resolved"
                    : thread.IsOutdated
                        ? "stale"
                        : "active";
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

    private static async Task AutoResolveStaleThreadsAsync(GitHubClient github, GitHubClient? fallbackGithub,
        IReadOnlyList<PullRequestReviewThread> threads, ReviewSettings settings, CancellationToken cancellationToken) {
        var resolved = 0;
        foreach (var thread in threads) {
            if (resolved >= settings.ReviewThreadsAutoResolveMax) {
                break;
            }
            if (thread.IsResolved || !thread.IsOutdated) {
                continue;
            }
            if (settings.ReviewThreadsAutoResolveBotsOnly && !ThreadHasOnlyBotComments(thread, settings)) {
                continue;
            }
            if (settings.ReviewThreadsAutoResolveBotsOnly && thread.TotalComments > thread.Comments.Count) {
                continue;
            }

            var result = await TryResolveThreadAsync(github, fallbackGithub, thread.Id, cancellationToken).ConfigureAwait(false);
            if (result.Resolved) {
                resolved++;
                continue;
            }
            Console.Error.WriteLine($"Failed to resolve review thread {thread.Id}: {result.Error ?? "unknown error"}");
        }
    }

    private static async Task AutoResolveMissingInlineThreadsAsync(GitHubClient github, GitHubClient? fallbackGithub,
        PullRequestContext context, HashSet<string>? expectedKeys, ReviewSettings settings, CancellationToken cancellationToken) {
        if (settings.ReviewThreadsAutoResolveMax <= 0) {
            return;
        }

        var keys = expectedKeys ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var maxThreads = Math.Max(settings.ReviewThreadsAutoResolveMax, settings.ReviewThreadsMax);
        var maxComments = Math.Max(1, settings.ReviewThreadsMaxComments);
        var threads = await github.ListPullRequestReviewThreadsAsync(context.Owner, context.Repo, context.Number,
                maxThreads, maxComments, cancellationToken)
            .ConfigureAwait(false);

        var resolved = 0;
        foreach (var thread in threads) {
            if (resolved >= settings.ReviewThreadsAutoResolveMax) {
                break;
            }
            if (thread.IsResolved) {
                continue;
            }
            if (settings.ReviewThreadsAutoResolveBotsOnly && thread.TotalComments > thread.Comments.Count) {
                continue;
            }
            if (!TryGetInlineThreadKey(thread, settings, out var key)) {
                continue;
            }
            if (keys.Contains(key)) {
                continue;
            }

            var result = await TryResolveThreadAsync(github, fallbackGithub, thread.Id, cancellationToken).ConfigureAwait(false);
            if (result.Resolved) {
                resolved++;
                continue;
            }
            Console.Error.WriteLine($"Failed to resolve review thread {thread.Id}: {result.Error ?? "unknown error"}");
        }
    }

    private static async Task<(IReadOnlyList<PullRequestFile> Files, string DiffNote)> ResolveThreadTriageFilesAsync(
        GitHubClient github, PullRequestContext context, ReviewSettings settings, IReadOnlyList<PullRequestFile> currentFiles,
        CancellationToken cancellationToken) {
        var range = ReviewSettings.NormalizeDiffRange(settings.ReviewThreadsAutoResolveDiffRange, "current");
        return await ResolveDiffRangeFilesAsync(github, context, range, currentFiles, settings, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<(IReadOnlyList<PullRequestFile> Files, string DiffNote)> ResolveDiffRangeFilesAsync(
        GitHubClient github, PullRequestContext context, string range, IReadOnlyList<PullRequestFile> currentFiles,
        ReviewSettings settings, CancellationToken cancellationToken) {
        if (range == "current") {
            return (currentFiles, "current PR files");
        }
        if (string.IsNullOrWhiteSpace(context.HeadSha)) {
            return (currentFiles, "current PR files (missing head SHA)");
        }

        async Task<(bool Success, IReadOnlyList<PullRequestFile> Files, string Note)> TryCompareAsync(string? baseSha, string label) {
            if (string.IsNullOrWhiteSpace(baseSha)) {
                return (false, Array.Empty<PullRequestFile>(), $"missing {label} commit");
            }
            try {
                var compareFiles = await github.GetCompareFilesAsync(context.Owner, context.Repo, baseSha, context.HeadSha!, cancellationToken)
                    .ConfigureAwait(false);
                if (compareFiles.Count == 0) {
                    return (false, Array.Empty<PullRequestFile>(), $"{label} diff empty");
                }
                var filtered = FilterFilesByPaths(compareFiles, settings.IncludePaths, settings.ExcludePaths);
                var note = $"{label} → head ({ShortSha(baseSha)}..{ShortSha(context.HeadSha)})";
                if (filtered.Count == 0) {
                    note += " (filtered empty)";
                }
                return (true, filtered, note);
            } catch (Exception ex) {
                Console.Error.WriteLine($"Failed to load {label} diff: {ex.Message}");
                return (false, Array.Empty<PullRequestFile>(), $"failed to load {label} diff");
            }
        }

        if (range == "pr-base") {
            var result = await TryCompareAsync(context.BaseSha, "PR base").ConfigureAwait(false);
            return result.Success
                ? (result.Files, result.Note)
                : (currentFiles, $"current PR files ({result.Note})");
        }

        var firstReviewSha = await FindOldestSummaryCommitAsync(github, context, settings, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(firstReviewSha)) {
            var prBaseResult = await TryCompareAsync(context.BaseSha, "PR base fallback").ConfigureAwait(false);
            if (prBaseResult.Success) {
                return (prBaseResult.Files, prBaseResult.Note);
            }
            return (currentFiles, $"current PR files (missing first review commit; {prBaseResult.Note})");
        }

        var firstReviewResult = await TryCompareAsync(firstReviewSha, "first review").ConfigureAwait(false);
        if (firstReviewResult.Success) {
            return (firstReviewResult.Files, firstReviewResult.Note);
        }

        var prBaseFallback = await TryCompareAsync(context.BaseSha, "PR base fallback").ConfigureAwait(false);
        if (prBaseFallback.Success) {
            return (prBaseFallback.Files, prBaseFallback.Note);
        }

        var note = firstReviewResult.Note;
        if (!string.IsNullOrWhiteSpace(prBaseFallback.Note)) {
            note = string.IsNullOrWhiteSpace(note) ? prBaseFallback.Note : $"{note}; {prBaseFallback.Note}";
        }
        return (currentFiles, $"current PR files ({note})");
    }

    private static async Task<ThreadTriageResult> MaybeAutoResolveAssessedThreadsAsync(GitHubClient github, GitHubClient? fallbackGithub,
        ReviewRunner runner, PullRequestContext context, IReadOnlyList<PullRequestFile> files, ReviewSettings settings,
        ReviewContextExtras extras, bool reviewFailed, string? diffNote, CancellationToken cancellationToken,
        bool force, bool allowCommentPost) {
        if ((!settings.ReviewThreadsAutoResolveAI && !force) || reviewFailed) {
            return ThreadTriageResult.Empty;
        }
        if (extras.ReviewThreads.Count == 0) {
            return ThreadTriageResult.Empty;
        }

        var candidates = SelectAssessmentCandidates(extras.ReviewThreads, settings);
        if (candidates.Count == 0) {
            return ThreadTriageResult.Empty;
        }

        var prompt = BuildThreadAssessmentPrompt(context, candidates, files, settings, diffNote);
        if (settings.RedactPii) {
            prompt = Redaction.Apply(prompt, settings.RedactionPatterns, settings.RedactionReplacement);
        }

        var output = await runner.RunAsync(prompt, null, null, cancellationToken).ConfigureAwait(false);
        if (ReviewDiagnostics.IsFailureBody(output)) {
            return ThreadTriageResult.Empty;
        }

        var assessments = ParseThreadAssessments(output);
        if (assessments.Count == 0) {
            Console.Error.WriteLine("Thread assessment returned no usable results.");
            return ThreadTriageResult.Empty;
        }

        var byId = assessments.ToDictionary(a => a.Id, StringComparer.OrdinalIgnoreCase);
        var resolved = new List<ThreadAssessment>();
        var kept = new List<ThreadAssessment>();
        var failed = new List<ThreadAssessment>();
        foreach (var assessment in assessments) {
            switch (assessment.Action) {
                case "comment":
                case "keep":
                    kept.Add(assessment);
                    break;
            }
        }

        var resolvedCount = 0;
        foreach (var thread in candidates) {
            if (resolvedCount >= settings.ReviewThreadsAutoResolveMax) {
                break;
            }
            if (!byId.TryGetValue(thread.Id, out var assessment) || assessment.Action != "resolve") {
                continue;
            }
            var result = await TryResolveThreadAsync(github, fallbackGithub, thread.Id, cancellationToken).ConfigureAwait(false);
            if (result.Resolved) {
                resolvedCount++;
                resolved.Add(assessment);
                continue;
            }
            var error = result.Error ?? "unknown error";
            failed.Add(new ThreadAssessment(assessment.Id, "keep", $"{assessment.Reason} (resolve failed: {error})"));
            Console.Error.WriteLine($"Failed to resolve review thread {thread.Id}: {error}");
        }

        if (failed.Count > 0) {
            kept.AddRange(failed);
        }

        var commentPosted = false;
        var triageBody = BuildThreadAssessmentComment(resolved, kept, context.HeadSha, diffNote);
        if (settings.ReviewThreadsAutoResolveAIReply && kept.Count > 0) {
            await ReplyToKeptThreadsAsync(github, context, candidates, byId, context.HeadSha, diffNote, settings, cancellationToken)
                .ConfigureAwait(false);
        }
        if (allowCommentPost && settings.ReviewThreadsAutoResolveAIPostComment &&
            !settings.ReviewThreadsAutoResolveAIEmbed && kept.Count > 0) {
            var body = triageBody;
            await github.CreateIssueCommentAsync(context.Owner, context.Repo, context.Number, body, cancellationToken)
                .ConfigureAwait(false);
            commentPosted = true;
        }

        if (resolvedCount == 0 && kept.Count == 0) {
            return ThreadTriageResult.Empty;
        }
        var summary = $"Auto-resolve (AI): {resolvedCount} resolved, {kept.Count} kept.";
        if (commentPosted) {
            summary += " Triage comment posted.";
        }
        return new ThreadTriageResult(summary, triageBody);
    }

    private static async Task<string> TryBuildUsageLineAsync(ReviewSettings settings) {
        if (!settings.ReviewUsageSummary) {
            return string.Empty;
        }
        if (settings.Provider != ReviewProvider.OpenAI) {
            return string.Empty;
        }

        try {
            var snapshot = await TryGetUsageSnapshotAsync(settings).ConfigureAwait(false);
            if (snapshot is null) {
                return string.Empty;
            }
            var summary = FormatUsageSummary(snapshot);
            return string.IsNullOrWhiteSpace(summary) ? string.Empty : summary;
        } catch (Exception ex) {
            if (settings.Diagnostics) {
                Console.Error.WriteLine($"Usage summary failed: {ex.Message}");
            }
            return string.Empty;
        }
    }

    private static async Task<ChatGptUsageSnapshot?> TryGetUsageSnapshotAsync(ReviewSettings settings) {
        var cacheMinutes = Math.Max(0, settings.ReviewUsageSummaryCacheMinutes);
        if (cacheMinutes > 0 && ChatGptUsageCache.TryLoad(out var entry) && entry is not null) {
            var age = DateTimeOffset.UtcNow - entry.UpdatedAt;
            if (age <= TimeSpan.FromMinutes(cacheMinutes)) {
                return entry.Snapshot;
            }
        }

        using var cts = new CancellationTokenSource(
            TimeSpan.FromSeconds(Math.Max(1, settings.ReviewUsageSummaryTimeoutSeconds)));
        var options = new OpenAINativeOptions {
            AuthStore = new FileAuthBundleStore()
        };
        using var service = new ChatGptUsageService(options);
        var snapshot = await service.GetUsageSnapshotAsync(cts.Token).ConfigureAwait(false);
        try {
            ChatGptUsageCache.Save(snapshot);
        } catch {
            // Best-effort cache.
        }
        return snapshot;
    }

    private static string FormatUsageSummary(ChatGptUsageSnapshot snapshot) {
        var lines = new List<string>();

        var primary = FormatRateLimitLine(snapshot.RateLimit?.PrimaryWindow, "rate limit");
        if (!string.IsNullOrWhiteSpace(primary)) {
            lines.Add(primary);
        }

        var secondary = FormatRateLimitLine(snapshot.RateLimit?.SecondaryWindow, "rate limit (secondary)");
        if (!string.IsNullOrWhiteSpace(secondary)) {
            lines.Add(secondary);
        }

        if (snapshot.Credits is not null) {
            if (snapshot.Credits.Unlimited) {
                lines.Add("credits: unlimited");
            } else if (snapshot.Credits.Balance.HasValue) {
                lines.Add($"credits: {snapshot.Credits.Balance.Value.ToString("0.####", CultureInfo.InvariantCulture)}");
            } else if (!snapshot.Credits.HasCredits) {
                lines.Add("credits: none");
            }
        }

        if (lines.Count == 0) {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.AppendLine("Usage:");
        foreach (var line in lines) {
            sb.AppendLine($"- {line}");
        }
        return sb.ToString().TrimEnd();
    }

    private static string? FormatRateLimitLine(ChatGptRateLimitWindow? window, string fallbackLabel) {
        if (window is null) {
            return null;
        }
        var remaining = FormatRemainingPercent(window.UsedPercent);
        if (string.IsNullOrWhiteSpace(remaining)) {
            return null;
        }
        var label = FormatWindowLabel(window) ?? fallbackLabel;
        return $"{label}: {remaining}% remaining";
    }

    private static string? FormatWindowLabel(ChatGptRateLimitWindow window) {
        if (!window.LimitWindowSeconds.HasValue) {
            return null;
        }
        var seconds = Math.Max(0, window.LimitWindowSeconds.Value);
        if (IsWithin(seconds, 5 * 3600, 600)) {
            return "5h limit";
        }
        if (IsWithin(seconds, 7 * 24 * 3600, 3600)) {
            return "weekly limit";
        }
        if (IsWithin(seconds, 24 * 3600, 3600)) {
            return "daily limit";
        }
        if (IsWithin(seconds, 3600, 120)) {
            return "hourly limit";
        }
        return $"{FormatDuration(seconds)} limit";
    }

    private static bool IsWithin(long value, long target, long tolerance) {
        return Math.Abs(value - target) <= tolerance;
    }

    private static string FormatDuration(long seconds) {
        if (seconds <= 0) {
            return "0s";
        }
        var span = TimeSpan.FromSeconds(seconds);
        if (span.TotalDays >= 1 && span.TotalDays % 1 == 0) {
            return $"{(int)span.TotalDays}d";
        }
        if (span.TotalHours >= 1 && span.TotalHours % 1 == 0) {
            return $"{(int)span.TotalHours}h";
        }
        if (span.TotalMinutes >= 1) {
            return $"{(int)Math.Round(span.TotalMinutes)}m";
        }
        return $"{(int)Math.Round(span.TotalSeconds)}s";
    }

    private static string? FormatRemainingPercent(double? usedPercent) {
        if (!usedPercent.HasValue) {
            return null;
        }
        var remaining = Math.Max(0, 100 - usedPercent.Value);
        return remaining.ToString("0.#", CultureInfo.InvariantCulture);
    }

    private static async Task<string?> FindOldestSummaryCommitAsync(GitHubClient github, PullRequestContext context,
        ReviewSettings settings, CancellationToken cancellationToken) {
        var limit = Math.Max(0, settings.CommentSearchLimit);
        var comments = await github.ListIssueCommentsAsync(context.Owner, context.Repo, context.Number, limit, cancellationToken)
            .ConfigureAwait(false);
        string? oldest = null;
        foreach (var comment in comments) {
            if (!comment.Body.Contains(ReviewFormatter.SummaryMarker, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }
            var commit = ExtractReviewedCommit(comment.Body);
            if (!string.IsNullOrWhiteSpace(commit)) {
                oldest = commit;
            }
        }
        return oldest;
    }

    private static string ShortSha(string? sha) {
        if (string.IsNullOrWhiteSpace(sha)) {
            return "?";
        }
        return sha.Length > 7 ? sha.Substring(0, 7) : sha;
    }

    private static List<PullRequestReviewThread> SelectAssessmentCandidates(IReadOnlyList<PullRequestReviewThread> threads,
        ReviewSettings settings) {
        var candidates = new List<PullRequestReviewThread>();
        foreach (var thread in threads) {
            if (thread.IsResolved) {
                continue;
            }
            if (settings.ReviewThreadsAutoResolveBotsOnly && !ThreadHasOnlyBotComments(thread, settings)) {
                continue;
            }
            if (settings.ReviewThreadsAutoResolveBotsOnly && thread.TotalComments > thread.Comments.Count) {
                continue;
            }
            candidates.Add(thread);
            if (candidates.Count >= settings.ReviewThreadsAutoResolveMax) {
                break;
            }
        }
        return candidates;
    }

    private static string BuildThreadAssessmentPrompt(PullRequestContext context, IReadOnlyList<PullRequestReviewThread> threads,
        IReadOnlyList<PullRequestFile> files, ReviewSettings settings, string? diffNote) {
        var sb = new StringBuilder();
        sb.AppendLine("You are reviewing existing PR review threads to decide if they should be resolved.");
        sb.AppendLine("Only mark a thread as resolved if the current diff clearly addresses the comment.");
        sb.AppendLine("If a human reply in the thread confirms the issue is fixed/expected/accepted, resolve it.");
        sb.AppendLine("If unclear or still valid, keep it open.");
        sb.AppendLine("If diff context shows `<file patch>`, it is the file-level diff because line-level context was unavailable.");
        sb.AppendLine();
        sb.AppendLine("Return JSON only in this format:");
        sb.AppendLine("{\"threads\":[{\"id\":\"...\",\"action\":\"resolve|keep|comment\",\"reason\":\"...\"}]}");
        sb.AppendLine();
        sb.AppendLine($"PR: {context.Owner}/{context.Repo} #{context.Number}");
        if (!string.IsNullOrWhiteSpace(context.Title)) {
            sb.AppendLine($"Title: {context.Title}");
        }
        if (!string.IsNullOrWhiteSpace(diffNote)) {
            sb.AppendLine($"Diff range: {diffNote}");
        }
        sb.AppendLine();

        var patchIndex = BuildInlinePatchIndex(files);
        var patchLookup = BuildPatchLookup(files, settings.MaxPatchChars);
        var index = 1;
        foreach (var thread in threads) {
            sb.AppendLine($"Thread {index}:");
            sb.AppendLine($"id: {thread.Id}");
            sb.AppendLine($"status: {(thread.IsOutdated ? "stale" : "active")}");
            var location = TryGetThreadLocation(thread, out var path, out var line)
                ? line > 0 ? $"{path}:{line}" : path
                : "<unknown>";
            sb.AppendLine($"location: {location}");
            sb.AppendLine("comments:");
            var commentCount = 0;
            foreach (var comment in thread.Comments) {
                if (commentCount >= settings.ReviewThreadsMaxComments) {
                    break;
                }
                var author = string.IsNullOrWhiteSpace(comment.Author) ? "unknown" : comment.Author!;
                var body = TrimComment(comment.Body, settings.MaxCommentChars);
                sb.AppendLine($"- {author}: {body}");
                commentCount++;
            }
            sb.AppendLine("diff context:");
            sb.AppendLine("```");
            sb.AppendLine(BuildThreadDiffContext(patchIndex, patchLookup, path, line, settings.MaxPatchChars));
            sb.AppendLine("```");
            sb.AppendLine();
            index++;
        }

        return sb.ToString().TrimEnd();
    }

    private static bool TryGetThreadLocation(PullRequestReviewThread thread, out string path, out int line) {
        path = string.Empty;
        line = 0;
        foreach (var comment in thread.Comments) {
            if (string.IsNullOrWhiteSpace(comment.Path)) {
                continue;
            }
            path = comment.Path!;
            if (comment.Line.HasValue) {
                line = (int)comment.Line.Value;
            }
            return true;
        }
        return false;
    }

    private static string BuildThreadDiffContext(Dictionary<string, List<PatchLine>> patchIndex,
        IReadOnlyDictionary<string, string> patchLookup, string path, int lineNumber, int maxPatchChars) {
        if (string.IsNullOrWhiteSpace(path)) {
            return "<unavailable>";
        }
        if (lineNumber <= 0) {
            var normalizedPath = NormalizePath(path);
            return TryBuildFallbackPatch(patchLookup, normalizedPath, maxPatchChars);
        }
        var normalized = NormalizePath(path);
        if (string.IsNullOrWhiteSpace(normalized) || !patchIndex.TryGetValue(normalized, out var lines) || lines.Count == 0) {
            return TryBuildFallbackPatch(patchLookup, normalized, maxPatchChars);
        }
        var context = 3;
        var lower = lineNumber - context;
        var upper = lineNumber + context;
        var snippet = lines.Where(l => l.LineNumber >= lower && l.LineNumber <= upper)
            .Select(l => $"{l.LineNumber}: {l.Text}")
            .ToList();
        if (snippet.Count > 0) {
            return string.Join("\n", snippet);
        }
        return TryBuildFallbackPatch(patchLookup, normalized, maxPatchChars);
    }

    private static Dictionary<string, string> BuildPatchLookup(IReadOnlyList<PullRequestFile> files, int maxPatchChars) {
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files) {
            if (string.IsNullOrWhiteSpace(file.Patch)) {
                continue;
            }
            var normalized = NormalizePath(file.Filename);
            if (string.IsNullOrWhiteSpace(normalized)) {
                continue;
            }
            lookup[normalized] = TrimPatch(file.Patch!, maxPatchChars);
        }
        return lookup;
    }

    private static string TryBuildFallbackPatch(IReadOnlyDictionary<string, string> patchLookup, string normalizedPath,
        int maxPatchChars) {
        if (string.IsNullOrWhiteSpace(normalizedPath)) {
            return "<unavailable>";
        }
        if (!patchLookup.TryGetValue(normalizedPath, out var patch) || string.IsNullOrWhiteSpace(patch)) {
            return "<unavailable>";
        }
        var limited = TrimPatch(patch, maxPatchChars);
        return $"<file patch>\n{limited}";
    }

    private static string TrimPatch(string patch, int maxPatchChars) {
        if (string.IsNullOrWhiteSpace(patch)) {
            return string.Empty;
        }
        if (maxPatchChars <= 0 || patch.Length <= maxPatchChars) {
            return patch;
        }
        var newline = patch.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var normalized = patch.Replace("\r\n", "\n");
        var lines = normalized.Split('\n');
        var headerLines = new List<string>();
        var hunks = new List<List<string>>();
        List<string>? current = null;

        foreach (var line in lines) {
            if (line.StartsWith("@@", StringComparison.Ordinal)) {
                if (current is not null) {
                    hunks.Add(current);
                }
                current = new List<string> { line };
                continue;
            }
            if (current is null) {
                headerLines.Add(line);
            } else {
                current.Add(line);
            }
        }

        if (current is not null) {
            hunks.Add(current);
        }

        if (hunks.Count == 0) {
            return TrimHard(normalized, maxPatchChars, newline);
        }

        var header = string.Join(newline, headerLines);
        if (header.Length > maxPatchChars) {
            return TrimHard(header, maxPatchChars, newline);
        }

        // Extra capacity helps when appending newlines and truncation markers.
        var sb = new StringBuilder(maxPatchChars + 32);
        if (!string.IsNullOrEmpty(header)) {
            sb.Append(header);
        }

        foreach (var hunk in hunks) {
            var hunkText = string.Join(newline, hunk);
            if (!TryAppendSegment(sb, hunkText, maxPatchChars, newline)) {
                TryAppendSegment(sb, "... (truncated) ...", maxPatchChars, newline);
                break;
            }
        }

        return sb.ToString();
    }

    private static bool TryAppendSegment(StringBuilder sb, string segment, int maxChars, string newline) {
        if (string.IsNullOrEmpty(segment)) {
            return true;
        }
        var extra = segment.Length + (sb.Length > 0 ? newline.Length : 0);
        if (sb.Length + extra > maxChars) {
            return false;
        }
        if (sb.Length > 0) {
            sb.Append(newline);
        }
        sb.Append(segment);
        return true;
    }

    private static string TrimHard(string text, int maxChars, string newline) {
        if (text.Length <= maxChars) {
            return text;
        }
        var suffix = $"{newline}... (truncated)";
        if (suffix.Length >= maxChars) {
            return text.Substring(0, maxChars);
        }
        var take = Math.Max(0, maxChars - suffix.Length);
        return text.Substring(0, take) + suffix;
    }

    private static string BuildThreadAssessmentComment(IReadOnlyList<ThreadAssessment> resolved,
        IReadOnlyList<ThreadAssessment> kept, string? headSha, string? diffNote) {
        var sb = new StringBuilder();
        sb.AppendLine("<!-- intelligencex:thread-triage -->");
        sb.AppendLine("### IntelligenceX thread triage");
        if (!string.IsNullOrWhiteSpace(headSha)) {
            var trimmed = headSha.Length > 7 ? headSha.Substring(0, 7) : headSha;
            sb.AppendLine($"_Assessed commit: `{trimmed}`_");
            sb.AppendLine();
        }
        if (!string.IsNullOrWhiteSpace(diffNote)) {
            sb.AppendLine($"_Diff range: {diffNote}_");
            sb.AppendLine();
        }
        if (resolved.Count > 0) {
            sb.AppendLine();
            sb.AppendLine("Resolved:");
            foreach (var item in resolved) {
                sb.AppendLine($"- {item.Id}: {item.Reason}");
            }
        }
        if (kept.Count > 0) {
            sb.AppendLine();
            sb.AppendLine("Needs attention:");
            foreach (var item in kept) {
                sb.AppendLine($"- {item.Id}: {item.Reason}");
            }
        }
        return sb.ToString().TrimEnd();
    }

    private static async Task ReplyToKeptThreadsAsync(GitHubClient github, PullRequestContext context,
        IReadOnlyList<PullRequestReviewThread> candidates, IReadOnlyDictionary<string, ThreadAssessment> assessments,
        string? headSha, string? diffNote, ReviewSettings settings, CancellationToken cancellationToken) {
        var replies = 0;
        foreach (var thread in candidates) {
            if (replies >= settings.ReviewThreadsAutoResolveMax) {
                break;
            }
            if (!assessments.TryGetValue(thread.Id, out var assessment)) {
                continue;
            }
            if (assessment.Action == "resolve") {
                continue;
            }
            if (ThreadHasAutoReply(thread)) {
                continue;
            }
            var target = GetReplyTargetComment(thread);
            if (target?.DatabaseId is null) {
                continue;
            }
            var body = BuildThreadReply(assessment, headSha, diffNote);
            try {
                await github.CreatePullRequestReviewCommentReplyAsync(context.Owner, context.Repo, context.Number,
                        target.DatabaseId.Value, body, cancellationToken)
                    .ConfigureAwait(false);
                replies++;
            } catch (Exception ex) {
                Console.Error.WriteLine($"Failed to reply to thread {thread.Id}: {ex.Message}");
            }
        }
    }

    private static bool ThreadHasAutoReply(PullRequestReviewThread thread) {
        foreach (var comment in thread.Comments) {
            if (!string.IsNullOrWhiteSpace(comment.Body) &&
                comment.Body.Contains(ThreadReplyMarker, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }
        return false;
    }

    private static PullRequestReviewThreadComment? GetReplyTargetComment(PullRequestReviewThread thread) {
        if (thread.Comments.Count == 0) {
            return null;
        }
        var withDates = thread.Comments.Where(c => c.CreatedAt.HasValue).ToList();
        if (withDates.Count > 0) {
            return withDates.OrderByDescending(c => c.CreatedAt).FirstOrDefault();
        }
        return thread.Comments[^1];
    }

    private static string BuildThreadReply(ThreadAssessment assessment, string? headSha, string? diffNote) {
        var sb = new StringBuilder();
        sb.AppendLine(ThreadReplyMarker);
        sb.AppendLine($"IntelligenceX triage: {assessment.Reason}");
        if (!string.IsNullOrWhiteSpace(headSha)) {
            var trimmed = headSha.Length > 7 ? headSha.Substring(0, 7) : headSha;
            sb.AppendLine();
            sb.AppendLine($"_Assessed commit: `{trimmed}`_");
        }
        if (!string.IsNullOrWhiteSpace(diffNote)) {
            sb.AppendLine();
            sb.AppendLine($"_Diff range: {diffNote}_");
        }
        return sb.ToString().TrimEnd();
    }

    private readonly record struct ThreadTriageResult(string SummaryLine, string EmbeddedBlock) {
        public static ThreadTriageResult Empty => new(string.Empty, string.Empty);
    }

    private static List<ThreadAssessment> ParseThreadAssessments(string output) {
        var result = new List<ThreadAssessment>();
        var obj = TryParseJsonObject(output);
        var array = obj?.GetArray("threads");
        if (array is null) {
            return result;
        }
        foreach (var item in array) {
            var thread = item?.AsObject();
            if (thread is null) {
                continue;
            }
            var id = thread.GetString("id");
            var actionRaw = thread.GetString("action");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(actionRaw)) {
                continue;
            }
            var action = actionRaw.Trim().ToLowerInvariant();
            if (action is not "resolve" and not "keep" and not "comment") {
                action = "keep";
            }
            var reason = thread.GetString("reason") ?? string.Empty;
            result.Add(new ThreadAssessment(id.Trim(), action, reason.Trim()));
        }
        return result;
    }

    private static JsonObject? TryParseJsonObject(string output) {
        if (string.IsNullOrWhiteSpace(output)) {
            return null;
        }
        var trimmed = output.Trim();
        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        if (start < 0 || end <= start) {
            return null;
        }
        var json = trimmed.Substring(start, end - start + 1);
        var value = JsonLite.Parse(json);
        return value?.AsObject();
    }

    private sealed record ThreadAssessment(string Id, string Action, string Reason);

    private static async Task<(bool Resolved, string? Error)> TryResolveThreadAsync(GitHubClient github,
        GitHubClient? fallbackGithub, string threadId, CancellationToken cancellationToken) {
        try {
            await github.ResolveReviewThreadAsync(threadId, cancellationToken).ConfigureAwait(false);
            return (true, null);
        } catch (Exception ex) {
            if (fallbackGithub is not null && IsIntegrationForbidden(ex)) {
                try {
                    await fallbackGithub.ResolveReviewThreadAsync(threadId, cancellationToken).ConfigureAwait(false);
                    return (true, null);
                } catch (Exception fallbackEx) {
                    return (false, fallbackEx.Message);
                }
            }
            return (false, ex.Message);
        }
    }

    private static bool IsIntegrationForbidden(Exception ex) {
        if (ex.Message.Contains("Resource not accessible by integration", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("\"FORBIDDEN\"", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }
        return ex.InnerException is not null && IsIntegrationForbidden(ex.InnerException);
    }

    private static bool ThreadHasOnlyBotComments(PullRequestReviewThread thread, ReviewSettings settings) {
        if (thread.Comments.Count == 0) {
            return false;
        }
        foreach (var comment in thread.Comments) {
            if (string.IsNullOrWhiteSpace(comment.Author)) {
                return false;
            }
            if (!IsBotAuthor(comment.Author, settings)) {
                return false;
            }
        }
        return true;
    }

    internal static bool TryGetInlineThreadKey(PullRequestReviewThread thread, ReviewSettings settings, out string key) {
        key = string.Empty;
        if (thread.Comments.Count == 0) {
            return false;
        }
        if (settings.ReviewThreadsAutoResolveBotsOnly && !ThreadHasOnlyBotComments(thread, settings)) {
            return false;
        }

        var marker = thread.Comments.FirstOrDefault(comment =>
            comment.Body.Contains(ReviewFormatter.InlineMarker, StringComparison.OrdinalIgnoreCase));
        if (marker is null || string.IsNullOrWhiteSpace(marker.Path) || !marker.Line.HasValue) {
            return false;
        }

        key = BuildInlineKey(marker.Path!, marker.Line.Value);
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
            if (IsBotAuthor(author, settings)) {
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
            if (IsBotAuthor(author, settings)) {
                return false;
            }
        }
        if (body.Contains("<!-- intelligencex", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }
        return true;
    }

    private static bool IsBotAuthor(string author, ReviewSettings settings) {
        if (string.IsNullOrWhiteSpace(author)) {
            return false;
        }
        var trimmedAuthor = author.Trim();
        var normalizedAuthor = NormalizeBotLogin(trimmedAuthor);
        if (settings.ReviewThreadsAutoResolveBotLogins.Count > 0) {
            foreach (var login in settings.ReviewThreadsAutoResolveBotLogins) {
                if (string.IsNullOrWhiteSpace(login)) {
                    continue;
                }
                var normalizedLogin = NormalizeBotLogin(login);
                if (string.IsNullOrWhiteSpace(normalizedLogin)) {
                    continue;
                }
                if (string.Equals(normalizedAuthor, normalizedLogin, StringComparison.OrdinalIgnoreCase)) {
                    return true;
                }
            }
        }
        if (trimmedAuthor.EndsWith("[bot]", StringComparison.OrdinalIgnoreCase) ||
            trimmedAuthor.EndsWith("bot", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }
        return trimmedAuthor.Equals("github-actions", StringComparison.OrdinalIgnoreCase) ||
            trimmedAuthor.Equals("intelligencex-review", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeBotLogin(string login) {
        var trimmed = login.Trim();
        if (trimmed.EndsWith("[bot]", StringComparison.OrdinalIgnoreCase)) {
            trimmed = trimmed.Substring(0, trimmed.Length - "[bot]".Length).TrimEnd();
        }
        return trimmed;
    }

    private static async Task<HashSet<string>?> PostInlineCommentsAsync(GitHubClient github, PullRequestContext context,
        IReadOnlyList<PullRequestFile> files, ReviewSettings settings, IReadOnlyList<InlineReviewComment> inlineComments,
        CancellationToken cancellationToken) {
        var expectedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (inlineComments.Count == 0 || string.IsNullOrWhiteSpace(context.HeadSha)) {
            return expectedKeys;
        }

        var lineMap = BuildInlineLineMap(files);
        var patchIndex = BuildInlinePatchIndex(files);
        if (lineMap.Count == 0) {
            return null;
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
            var allowPost = posted < settings.MaxInlineComments;
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
            var body = inline.Body.Trim();
            if (string.IsNullOrWhiteSpace(body)) {
                continue;
            }
            var key = BuildInlineKey(normalizedPath, lineNumber);
            expectedKeys.Add(key);
            if (!allowPost || existingKeys.Contains(key) || !seen.Add(key)) {
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
        return expectedKeys;
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

    private static bool IsSummaryOutdated(IssueComment? summary, string? headSha) {
        if (summary is null || string.IsNullOrWhiteSpace(headSha)) {
            return false;
        }
        var reviewedCommit = ExtractReviewedCommit(summary.Body);
        if (string.IsNullOrWhiteSpace(reviewedCommit)) {
            return true;
        }
        return !headSha.StartsWith(reviewedCommit, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExtractReviewedCommit(string body) {
        return ReviewSummaryParser.TryGetReviewedCommit(body, out var commit) ? commit : null;
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


