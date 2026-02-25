namespace IntelligenceX.Reviewer;

public static partial class ReviewerApp {
    internal static string BuildBudgetNote(int totalFiles, int includedFiles, int truncatedPatches, int maxPatchChars) {
        var parts = new List<string>();
        if (totalFiles > includedFiles) {
            parts.Add($"showing first {includedFiles} of {totalFiles} files");
        }
        if (truncatedPatches > 0) {
            var label = truncatedPatches == 1 ? "patch" : "patches";
            parts.Add($"{truncatedPatches} {label} trimmed to {maxPatchChars} chars");
        }
        if (parts.Count == 0) {
            return string.Empty;
        }
        return
            $"Review context truncated: {string.Join("; ", parts)}. " +
            "Impact: review covers only included diff context; issues outside it may be missed. " +
            "Increase review.maxFiles/review.maxPatchChars for wider coverage.";
    }

    private static string ApplyEmbedPlacement(string reviewBody, string embedBlock, string placement) {
        var embed = embedBlock.Trim();
        if (embed.Length == 0) {
            return reviewBody;
        }
        var body = string.IsNullOrWhiteSpace(reviewBody) ? string.Empty : reviewBody.Trim();
        var normalizedPlacement = ReviewSettings.NormalizeEmbedPlacement(placement, "bottom");
        if (normalizedPlacement == "top") {
            return string.IsNullOrWhiteSpace(body) ? embed : embed + "\n\n" + body;
        }
        return string.IsNullOrWhiteSpace(body) ? embed : body + "\n\n" + embed;
    }

    /// <summary>
    /// Filters pull request files using include/exclude glob patterns.
    /// Binary/generated filters are applied first, then include patterns, and exclude patterns are applied last.
    /// </summary>
    internal static IReadOnlyList<PullRequestFile> FilterFilesByPaths(IReadOnlyList<PullRequestFile> files,
        IReadOnlyList<string> includePaths, IReadOnlyList<string> excludePaths,
        bool skipBinaryFiles = false, bool skipGeneratedFiles = false,
        IReadOnlyList<string>? generatedFileGlobs = null) {
        if (files.Count == 0) {
            return files;
        }
        includePaths ??= Array.Empty<string>();
        excludePaths ??= Array.Empty<string>();
        var hasInclude = includePaths.Count > 0;
        var hasExclude = excludePaths.Count > 0;
        if (!hasInclude && !hasExclude && !skipBinaryFiles && !skipGeneratedFiles) {
            return files;
        }
        var effectiveGeneratedGlobs = skipGeneratedFiles
            ? ResolveGeneratedGlobs(generatedFileGlobs)
            : Array.Empty<string>();
        var filtered = new List<PullRequestFile>();
        foreach (var file in files) {
            var filename = file.Filename.Replace('\\', '/');
            if (skipBinaryFiles && IsBinaryFile(filename)) {
                continue;
            }
            if (skipGeneratedFiles && IsGeneratedFile(filename, effectiveGeneratedGlobs)) {
                continue;
            }
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

    private static bool IsBinaryFile(string path) {
        if (string.IsNullOrWhiteSpace(path)) {
            return false;
        }
        var extension = Path.GetExtension(path);
        if (string.IsNullOrWhiteSpace(extension)) {
            return false;
        }
        return BinaryExtensions.Contains(extension);
    }

    private static IReadOnlyList<string> ResolveGeneratedGlobs(IReadOnlyList<string>? generatedFileGlobs) {
        if (generatedFileGlobs is null || generatedFileGlobs.Count == 0) {
            return GeneratedGlobs;
        }
        var combined = new List<string>(GeneratedGlobs.Count + generatedFileGlobs.Count);
        combined.AddRange(GeneratedGlobs);
        combined.AddRange(generatedFileGlobs);
        return combined;
    }

    private static bool IsGeneratedFile(string path, IReadOnlyList<string> generatedGlobs) {
        if (string.IsNullOrWhiteSpace(path)) {
            return false;
        }
        return generatedGlobs.Any(pattern => GlobMatcher.IsMatch(pattern, path));
    }

    private static async Task<(IReadOnlyList<PullRequestFile> Files, string DiffNote)> ResolveReviewFilesAsync(
        IReviewCodeHostReader codeHostReader, PullRequestContext context, ReviewSettings settings, IReadOnlyList<PullRequestFile> currentFiles,
        CancellationToken cancellationToken) {
        var range = ReviewSettings.NormalizeDiffRange(settings.ReviewDiffRange, "current");
        return await ResolveDiffRangeFilesAsync(codeHostReader, context, range, currentFiles, settings, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<ReviewContextExtras> BuildExtrasAsync(IReviewCodeHostReader codeHostReader, GitHubClient github,
        GitHubClient? fallbackGithub,
        PullRequestContext context, ReviewSettings settings, CancellationToken cancellationToken, bool forceReviewThreads) {
        var extras = new ReviewContextExtras();
        if (settings.IncludeIssueComments) {
            try {
                var comments = await codeHostReader.ListIssueCommentsAsync(context, settings.MaxComments, cancellationToken)
                    .ConfigureAwait(false);
                extras.IssueCommentsSection = BuildIssueCommentsSection(comments, settings);
            } catch (Exception ex) {
                // Issue comments are supplemental context; avoid failing the whole review on GitHub rate limits.
                Console.Error.WriteLine($"Failed to load issue comments: {ex.Message}");
            }
        }
        var loadThreads = forceReviewThreads || settings.IncludeReviewThreads || settings.ReviewThreadsAutoResolveAI;
        if (loadThreads) {
            try {
                var threadCommentFetchLimit = ResolveThreadCommentFetchLimit(settings);
                var threads = await codeHostReader.ListPullRequestReviewThreadsAsync(context, settings.ReviewThreadsMax,
                        threadCommentFetchLimit, cancellationToken)
                    .ConfigureAwait(false);
                threads = await HydratePartialThreadsForBotsOnlyAsync(github, threads, settings, cancellationToken)
                    .ConfigureAwait(false);
                extras.ReviewThreads = threads;
                if (settings.ReviewThreadsAutoResolveStale) {
                    await AutoResolveStaleThreadsAsync(github, fallbackGithub, threads, settings, cancellationToken).ConfigureAwait(false);
                }
                if (settings.IncludeReviewThreads) {
                    extras.ReviewThreadsSection = BuildReviewThreadsSection(threads, settings);
                }
            } catch (Exception ex) {
                // Review threads are supplemental context; avoid failing the whole review on GitHub GraphQL rate limits.
                Console.Error.WriteLine($"Failed to load review threads: {ex.Message}");
                extras.ReviewThreads = Array.Empty<PullRequestReviewThread>();
            }
        }
        if (settings.IncludeReviewComments && string.IsNullOrEmpty(extras.ReviewThreadsSection)) {
            try {
                var comments = await codeHostReader.ListPullRequestReviewCommentsAsync(context, settings.MaxComments, cancellationToken)
                    .ConfigureAwait(false);
                extras.ReviewCommentsSection = BuildReviewCommentsSection(comments, settings);
            } catch (Exception ex) {
                // Review comments are supplemental context; avoid failing the whole review on GitHub rate limits.
                Console.Error.WriteLine($"Failed to load review comments: {ex.Message}");
            }
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
        var scanned = 0;
        var skippedResolved = 0;
        var skippedNotOutdated = 0;
        var skippedNonBot = 0;
        var skippedPartialBotView = 0;
        var failed = 0;
        foreach (var thread in threads) {
            scanned++;
            if (resolved >= settings.ReviewThreadsAutoResolveMax) {
                break;
            }
            if (thread.IsResolved) {
                skippedResolved++;
                continue;
            }
            if (!thread.IsOutdated) {
                skippedNotOutdated++;
                continue;
            }
            if (settings.ReviewThreadsAutoResolveBotsOnly && !ThreadHasOnlyBotComments(thread, settings)) {
                skippedNonBot++;
                continue;
            }
            if (settings.ReviewThreadsAutoResolveBotsOnly && thread.TotalComments > thread.Comments.Count) {
                skippedPartialBotView++;
                continue;
            }

            var result = await TryResolveThreadAsync(github, fallbackGithub, thread.Id, cancellationToken).ConfigureAwait(false);
            if (result.Resolved) {
                resolved++;
                continue;
            }
            failed++;
            Console.Error.WriteLine($"Failed to resolve review thread {thread.Id}: {result.Error ?? "unknown error"}");
        }

        if (scanned > 0) {
            Console.Error.WriteLine(
                $"Thread auto-resolve (stale): scanned={scanned}; resolved={resolved}; failed={failed}; " +
                $"skip_resolved={skippedResolved}; skip_not_outdated={skippedNotOutdated}; " +
                $"skip_non_bot={skippedNonBot}; skip_partial_view={skippedPartialBotView}.");
        }
    }

    private static async Task AutoResolveMissingInlineThreadsAsync(IReviewCodeHostReader codeHostReader, GitHubClient github,
        GitHubClient? fallbackGithub, PullRequestContext context, HashSet<string>? expectedKeys, ReviewSettings settings,
        CancellationToken cancellationToken) {
        if (settings.ReviewThreadsAutoResolveMax <= 0) {
            return;
        }

        var keys = expectedKeys ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var maxThreads = Math.Max(settings.ReviewThreadsAutoResolveMax, settings.ReviewThreadsMax);
        var maxComments = ResolveThreadCommentFetchLimit(settings);
        var threads = await codeHostReader.ListPullRequestReviewThreadsAsync(context, maxThreads, maxComments, cancellationToken)
            .ConfigureAwait(false);
        threads = await HydratePartialThreadsForBotsOnlyAsync(github, threads, settings, cancellationToken)
            .ConfigureAwait(false);

        var resolved = 0;
        var scanned = 0;
        var skippedResolved = 0;
        var skippedPartialBotView = 0;
        var skippedNonBot = 0;
        var skippedNoInlineKey = 0;
        var skippedExpectedMatch = 0;
        var failed = 0;
        foreach (var thread in threads) {
            scanned++;
            if (resolved >= settings.ReviewThreadsAutoResolveMax) {
                break;
            }
            if (thread.IsResolved) {
                skippedResolved++;
                continue;
            }
            if (settings.ReviewThreadsAutoResolveBotsOnly && thread.TotalComments > thread.Comments.Count) {
                skippedPartialBotView++;
                continue;
            }
            if (settings.ReviewThreadsAutoResolveBotsOnly && !ThreadHasOnlyBotComments(thread, settings)) {
                skippedNonBot++;
                continue;
            }
            if (!TryGetInlineThreadMatchKeys(thread, settings, out var threadKeys) || threadKeys.Count == 0) {
                skippedNoInlineKey++;
                continue;
            }
            if (threadKeys.Any(keys.Contains)) {
                skippedExpectedMatch++;
                continue;
            }

            var result = await TryResolveThreadAsync(github, fallbackGithub, thread.Id, cancellationToken).ConfigureAwait(false);
            if (result.Resolved) {
                resolved++;
                continue;
            }
            failed++;
            Console.Error.WriteLine($"Failed to resolve review thread {thread.Id}: {result.Error ?? "unknown error"}");
        }

        if (scanned > 0) {
            Console.Error.WriteLine(
                $"Thread auto-resolve (missing-inline): expected_keys={keys.Count}; scanned={scanned}; resolved={resolved}; failed={failed}; " +
                $"skip_resolved={skippedResolved}; skip_partial_view={skippedPartialBotView}; skip_non_bot={skippedNonBot}; " +
                $"skip_no_inline_key={skippedNoInlineKey}; skip_expected_match={skippedExpectedMatch}.");
        }
    }

    private static async Task<(IReadOnlyList<PullRequestFile> Files, string DiffNote)> ResolveThreadTriageFilesAsync(
        IReviewCodeHostReader codeHostReader, PullRequestContext context, ReviewSettings settings,
        IReadOnlyList<PullRequestFile> currentFiles, CancellationToken cancellationToken) {
        var range = ReviewSettings.NormalizeDiffRange(settings.ReviewThreadsAutoResolveDiffRange, "current");
        return await ResolveDiffRangeFilesAsync(codeHostReader, context, range, currentFiles, settings, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<(IReadOnlyList<PullRequestFile> Files, string DiffNote)> ResolveDiffRangeFilesAsync(
        IReviewCodeHostReader codeHostReader, PullRequestContext context, string range, IReadOnlyList<PullRequestFile> currentFiles,
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
                var compareResult = await codeHostReader.GetCompareFilesAsync(context, baseSha, context.HeadSha!, cancellationToken)
                    .ConfigureAwait(false);
                if (compareResult.IsTruncated) {
                    return (false, Array.Empty<PullRequestFile>(), $"{label} diff truncated");
                }
                var compareFiles = compareResult.Files;
                if (compareFiles.Count == 0) {
                    return (false, Array.Empty<PullRequestFile>(), $"{label} diff empty");
                }
                var filtered = FilterFilesByPaths(compareFiles, settings.IncludePaths, settings.ExcludePaths,
                    settings.SkipBinaryFiles, settings.SkipGeneratedFiles, settings.GeneratedFileGlobs);
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

        var firstReviewSha = await FindOldestSummaryCommitAsync(codeHostReader, context, settings, cancellationToken).ConfigureAwait(false);
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

    private static int ResolveThreadCommentFetchLimit(ReviewSettings settings) {
        // Keep prompt/context limits independent from fetch limits so auto-resolve logic can inspect full thread authorship.
        return Math.Max(Math.Max(1, settings.ReviewThreadsMaxComments), 20);
    }

    private static async Task<IReadOnlyList<PullRequestReviewThread>> HydratePartialThreadsForBotsOnlyAsync(GitHubClient github,
        IReadOnlyList<PullRequestReviewThread> threads, ReviewSettings settings, CancellationToken cancellationToken) {
        if (!settings.ReviewThreadsAutoResolveBotsOnly || threads.Count == 0) {
            return threads;
        }

        const int maxHydratedComments = 500;
        var hydrated = new List<PullRequestReviewThread>(threads.Count);
        foreach (var thread in threads) {
            if (thread.TotalComments <= thread.Comments.Count || string.IsNullOrWhiteSpace(thread.Id)) {
                hydrated.Add(thread);
                continue;
            }

            PullRequestReviewThread? expanded = null;
            try {
                expanded = await github.GetPullRequestReviewThreadAsync(thread.Id, maxHydratedComments, cancellationToken)
                    .ConfigureAwait(false);
            } catch (Exception ex) {
                Console.Error.WriteLine($"Failed to hydrate review thread {thread.Id}: {ex.Message}");
            }

            hydrated.Add(expanded ?? thread);
        }

        return hydrated;
    }

}
