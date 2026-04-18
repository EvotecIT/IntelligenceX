using IntelligenceX.GitHub;

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
        if (settings.CiContext.Enabled && settings.CodeHost == ReviewCodeHost.GitHub) {
            try {
                var readGithub = fallbackGithub ?? github;
                var checkSnapshot = await readGithub.GetCheckSnapshotAsync(context.Owner, context.Repo, context.HeadSha, cancellationToken)
                    .ConfigureAwait(false);
                var failedRuns = settings.CiContext.IncludeFailedRuns
                    ? await readGithub.GetFailedWorkflowRunsAsync(context.Owner, context.Repo, context.HeadSha,
                            settings.CiContext.MaxFailedRuns, cancellationToken)
                        .ConfigureAwait(false)
                    : Array.Empty<ReviewWorkflowRun>();
                var failureEvidence = await LoadCiFailureEvidenceAsync(readGithub, context, settings, failedRuns, cancellationToken)
                    .ConfigureAwait(false);
                extras.CiContextSection = BuildCiContextSection(context, settings, checkSnapshot, failedRuns, failureEvidence);
            } catch (Exception ex) {
                // CI/check context is supplemental; avoid failing the whole review on rate limits or permission gaps.
                Console.Error.WriteLine($"Failed to load CI/check context: {ex.Message}");
            }
        }
        var historyNeedsIssueComments = settings.History.Enabled &&
                                        (settings.History.IncludeIxSummaryHistory ||
                                         settings.History.IncludeExternalBotSummaries);
        var loadIssueComments = settings.IncludeIssueComments || historyNeedsIssueComments;
        if (loadIssueComments) {
            try {
                var commentLimit = settings.IncludeIssueComments && !historyNeedsIssueComments
                    ? settings.MaxComments
                    : Math.Max(settings.MaxComments, settings.CommentSearchLimit);
                var comments = await codeHostReader.ListIssueCommentsAsync(context, commentLimit, cancellationToken)
                    .ConfigureAwait(false);
                extras.IssueComments = comments;
                if (settings.IncludeIssueComments) {
                    extras.IssueCommentsSection = BuildIssueCommentsSection(comments, settings);
                }
            } catch (Exception ex) {
                // Issue comments are supplemental context; avoid failing the whole review on GitHub rate limits.
                Console.Error.WriteLine($"Failed to load issue comments: {ex.Message}");
            }
        }
        var loadThreads = forceReviewThreads ||
                          settings.IncludeReviewThreads ||
                          settings.ReviewThreadsAutoResolveAI ||
                          (settings.History.Enabled && settings.History.IncludeReviewThreads);
        if (loadThreads) {
            try {
                var threadCommentFetchLimit = ResolveThreadCommentFetchLimit(settings);
                var threads = await codeHostReader.ListPullRequestReviewThreadsAsync(context, settings.ReviewThreadsMax,
                        threadCommentFetchLimit, cancellationToken)
                    .ConfigureAwait(false);
                threads = await HydratePartialThreadsForBotsOnlyAsync(github, fallbackGithub, threads, settings, cancellationToken)
                    .ConfigureAwait(false);
                extras.ReviewThreads = threads;
                if (settings.ReviewThreadsAutoResolveStale) {
                    extras.StaleThreadAutoResolvePermissions =
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

    private static string BuildCiContextSection(PullRequestContext context, ReviewSettings settings, ReviewCheckSnapshot snapshot,
        IReadOnlyList<ReviewWorkflowRun> failedRuns, IReadOnlyDictionary<string, GitHubWorkflowFailureEvidence> failureEvidence) {
        if (!settings.CiContext.Enabled) {
            return string.Empty;
        }

        var lines = new List<string>();
        if (settings.CiContext.IncludeCheckSummary && snapshot.HasData) {
            lines.Add($"- Head SHA {ShortSha(context.HeadSha)} check-runs: passed {snapshot.PassedCount}, failed {snapshot.FailedCount}, pending {snapshot.PendingCount}.");
            if (snapshot.FailedChecks.Count > 0) {
                var failedChecks = string.Join("; ", snapshot.FailedChecks
                    .Take(5)
                    .Select(item => $"{item.Name} ({item.Conclusion ?? item.Status})"));
                lines.Add($"- Failing check-runs: {failedChecks}.");
            }
        }

        if (settings.CiContext.IncludeFailedRuns && failedRuns.Count > 0) {
            lines.Add("- Failed workflow runs on the current head SHA:");
            foreach (var run in failedRuns.Take(settings.CiContext.MaxFailedRuns)) {
                var conclusion = string.IsNullOrWhiteSpace(run.Conclusion) ? run.Status : run.Conclusion;
                var url = string.IsNullOrWhiteSpace(run.Url) ? string.Empty : $" {run.Url}";
                var evidence = TryGetFailureEvidence(run, failureEvidence);
                var classification = DescribeCiFailureKind(evidence?.Kind, settings.CiContext.ClassifyInfraFailures);
                var snippet = ResolveCiFailureSnippet(evidence, settings);
                var detail = string.IsNullOrWhiteSpace(snippet) ? string.Empty : $": {snippet}";
                lines.Add($"  - {run.Name} ({conclusion}){url}{classification}{detail}".TrimEnd());
            }
        }

        if (lines.Count == 0) {
            return string.Empty;
        }

        if (settings.CiContext.ClassifyInfraFailures &&
            failedRuns.Any(run => GitHubCiSignals.IsPotentiallyOperationalConclusion(run.Conclusion))) {
            lines.Add("- Note: cancelled or timed-out workflow runs may be operational rather than code failures; confirm before treating them as merge blockers.");
        }

        if (ShouldIncludeAnyFailureSnippets(settings, failureEvidence)) {
            lines.Add("- Failure evidence is summarized from failed GitHub Actions jobs/steps only; it is bounded context, not a raw log dump.");
        }

        lines.Add("- Treat CI/check context as supporting operational evidence only. Do not merely restate failing checks; connect them to the diff only when evidence supports that link.");

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("CI / checks context:");
        foreach (var line in lines) {
            sb.AppendLine(line);
        }
        sb.AppendLine();
        return sb.ToString();
    }

    private static async Task<IReadOnlyDictionary<string, GitHubWorkflowFailureEvidence>> LoadCiFailureEvidenceAsync(
        GitHubClient github, PullRequestContext context, ReviewSettings settings, IReadOnlyList<ReviewWorkflowRun> failedRuns,
        CancellationToken cancellationToken) {
        if (!settings.CiContext.Enabled ||
            string.Equals(settings.CiContext.IncludeFailureSnippets, "off", StringComparison.OrdinalIgnoreCase) ||
            settings.CiContext.MaxSnippetCharsPerRun <= 0 ||
            failedRuns.Count == 0) {
            return new Dictionary<string, GitHubWorkflowFailureEvidence>(StringComparer.OrdinalIgnoreCase);
        }

        var map = new Dictionary<string, GitHubWorkflowFailureEvidence>(StringComparer.OrdinalIgnoreCase);
        foreach (var run in failedRuns.Take(settings.CiContext.MaxFailedRuns)) {
            if (string.IsNullOrWhiteSpace(run.RunId)) {
                continue;
            }

            try {
                var evidence = await github.GetWorkflowFailureEvidenceAsync(context.Owner, context.Repo, run.RunId,
                        settings.CiContext.MaxSnippetCharsPerRun, cancellationToken)
                    .ConfigureAwait(false);
                if (evidence is not null && evidence.HasData) {
                    map[run.RunId] = evidence;
                }
            } catch (Exception ex) {
                Console.Error.WriteLine(
                    $"Failed to load CI failure evidence for workflow run {run.RunId}: {ex.Message}");
            }
        }

        return map;
    }

    private static GitHubWorkflowFailureEvidence? TryGetFailureEvidence(ReviewWorkflowRun run,
        IReadOnlyDictionary<string, GitHubWorkflowFailureEvidence> failureEvidence) {
        if (string.IsNullOrWhiteSpace(run.RunId)) {
            return null;
        }

        return failureEvidence.TryGetValue(run.RunId, out var evidence) ? evidence : null;
    }

    private static string DescribeCiFailureKind(GitHubWorkflowFailureKind? kind, bool classifyInfraFailures) {
        if (!classifyInfraFailures || kind is null) {
            return string.Empty;
        }

        return kind.Value switch {
            GitHubWorkflowFailureKind.Actionable => " [likely code/test]",
            GitHubWorkflowFailureKind.Operational => " [likely operational/infra]",
            GitHubWorkflowFailureKind.Mixed => " [mixed operational + code/test]",
            _ => string.Empty
        };
    }

    private static string ResolveCiFailureSnippet(GitHubWorkflowFailureEvidence? evidence, ReviewSettings settings) {
        if (evidence is null || !evidence.HasData) {
            return string.Empty;
        }

        var mode = ReviewSettings.NormalizeCiContextFailureSnippets(settings.CiContext.IncludeFailureSnippets, "off");
        if (string.Equals(mode, "off", StringComparison.OrdinalIgnoreCase)) {
            return string.Empty;
        }

        if (string.Equals(mode, "auto", StringComparison.OrdinalIgnoreCase) &&
            settings.CiContext.ClassifyInfraFailures &&
            evidence.Kind == GitHubWorkflowFailureKind.Operational) {
            return string.Empty;
        }

        return TrimComment(evidence.Summary, settings.CiContext.MaxSnippetCharsPerRun);
    }

    private static bool ShouldIncludeAnyFailureSnippets(ReviewSettings settings,
        IReadOnlyDictionary<string, GitHubWorkflowFailureEvidence> failureEvidence) {
        if (failureEvidence.Count == 0) {
            return false;
        }

        foreach (var evidence in failureEvidence.Values) {
            if (!string.IsNullOrWhiteSpace(ResolveCiFailureSnippet(evidence, settings))) {
                return true;
            }
        }

        return false;
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

    private static async Task<AutoResolvePermissionDiagnostics> AutoResolveStaleThreadsAsync(GitHubClient github, GitHubClient? fallbackGithub,
        IReadOnlyList<PullRequestReviewThread> threads, ReviewSettings settings, CancellationToken cancellationToken) {
        var resolved = 0;
        var scanned = 0;
        var skippedResolved = 0;
        var skippedNotOutdated = 0;
        var skippedNonBot = 0;
        var skippedPartialBotView = 0;
        var failed = 0;
        var permissionDeniedCount = 0;
        var permissionDeniedCredentials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
            if (result.PermissionDenied) {
                permissionDeniedCount++;
                foreach (var label in result.PermissionDeniedCredentialLabels) {
                    permissionDeniedCredentials.Add(label);
                }
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
        return AutoResolvePermissionDiagnostics.From(permissionDeniedCount, permissionDeniedCredentials);
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
        threads = await HydratePartialThreadsForBotsOnlyAsync(github, fallbackGithub, threads, settings, cancellationToken)
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
        GitHubClient? fallbackGithub, IReadOnlyList<PullRequestReviewThread> threads, ReviewSettings settings,
        CancellationToken cancellationToken) {
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
            var preferredGithub = fallbackGithub ?? github;
            var secondaryGithub = ReferenceEquals(preferredGithub, github) ? fallbackGithub : github;
            try {
                expanded = await preferredGithub.GetPullRequestReviewThreadAsync(thread.Id, maxHydratedComments, cancellationToken)
                    .ConfigureAwait(false);
            } catch (Exception primaryEx) {
                if (secondaryGithub is not null) {
                    try {
                        expanded = await secondaryGithub.GetPullRequestReviewThreadAsync(thread.Id, maxHydratedComments,
                                cancellationToken)
                            .ConfigureAwait(false);
                    } catch (Exception secondaryEx) {
                        Console.Error.WriteLine(
                            $"Failed to hydrate review thread {thread.Id}: primary={primaryEx.Message}; fallback={secondaryEx.Message}");
                    }
                } else {
                    Console.Error.WriteLine($"Failed to hydrate review thread {thread.Id}: {primaryEx.Message}");
                }
            }

            hydrated.Add(expanded ?? thread);
        }

        return hydrated;
    }

}
