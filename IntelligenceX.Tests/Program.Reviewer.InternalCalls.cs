namespace IntelligenceX.Tests;

#if INTELLIGENCEX_REVIEWER
internal static partial class Program {
    private static ReviewConfigValidationResult? RunConfigValidation(string json) {
        var previousPath = Environment.GetEnvironmentVariable("REVIEW_CONFIG_PATH");
        var tempDir = Path.Combine(Path.GetTempPath(), $"ix-config-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var configPath = Path.Combine(tempDir, "reviewer.json");
        File.WriteAllText(configPath, json);
        Environment.SetEnvironmentVariable("REVIEW_CONFIG_PATH", configPath);
        try {
            return ReviewConfigValidator.ValidateCurrent();
        } finally {
            Environment.SetEnvironmentVariable("REVIEW_CONFIG_PATH", previousPath);
            try {
                DeleteDirectoryIfExistsWithRetries(tempDir);
            } catch {
                // best-effort cleanup
            }
        }
    }

    private static string CallTrimPatch(string patch, int maxChars) {
        return ReviewerApp.TrimPatchForTests(patch, maxChars);
    }

    private static (IReadOnlyList<PullRequestFile> Files, string BudgetNote) CallPrepareFiles(IReadOnlyList<PullRequestFile> files,
        int maxFiles, int maxPatchChars) {
        return ReviewerApp.PrepareFilesForTests(files, maxFiles, maxPatchChars);
    }

    private static string CallFormatUsageSummary(ChatGptUsageSnapshot snapshot) {
        return ReviewerApp.FormatUsageSummaryForTests(snapshot);
    }

    private static string? CallEvaluateUsageBudgetGuardFailure(ReviewSettings settings, ChatGptUsageSnapshot snapshot) {
        return ReviewerApp.EvaluateUsageBudgetGuardFailureForTests(settings, snapshot);
    }

    private static string? CallTryBuildUsageBudgetGuardFailure(ReviewSettings settings, ReviewProvider provider) {
        return ReviewerApp.TryBuildUsageBudgetGuardFailureForTestsAsync(settings, provider).GetAwaiter().GetResult();
    }

    private static IReadOnlyList<string> CallOrderOpenAiAccounts(
        IReadOnlyList<string> accountIds,
        string rotation,
        string? stickyAccountId,
        long rotationSeed) {
        return ReviewerApp.OrderOpenAiAccountsForTests(accountIds, rotation, stickyAccountId, rotationSeed);
    }

    private static (bool Success, string? Error, bool BudgetGuardEvaluated) CallTryResolveOpenAiAccount(ReviewSettings settings) {
        return ReviewerApp.TryResolveOpenAiAccountForTestsAsync(settings).GetAwaiter().GetResult();
    }

    private static void CallPreflightNativeConnectivity(OpenAINativeOptions options, TimeSpan timeout) {
        var runner = new ReviewRunner(new ReviewSettings());
        runner.PreflightNativeConnectivityForTestsAsync(options, timeout, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    private static Exception? CallMapPreflightConnectivityException(HttpRequestException ex, string host, TimeSpan timeout,
        bool cancellationRequested) {
        return ReviewRunner.MapPreflightConnectivityExceptionForTests(ex, host, timeout, cancellationRequested);
    }

    private static bool CallShouldAutoResolveMissingInlineThreads(ReviewSettings settings, PullRequestContext context,
        HashSet<string>? inlineKeys, int inlineCommentsCount) {
        return ReviewerApp.ShouldAutoResolveMissingInlineThreadsForTests(settings, context, inlineKeys, inlineCommentsCount);
    }

    private static ReviewContextExtras CallBuildExtrasAsync(GitHubClient github, PullRequestContext context,
        ReviewSettings settings, bool forceReviewThreads) {
        return ReviewerApp.BuildExtrasForTestsAsync(github, context, settings, forceReviewThreads, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    private static ReviewContextExtras CallBuildExtrasAsync(GitHubClient github, GitHubClient? fallbackGithub,
        PullRequestContext context, ReviewSettings settings, bool forceReviewThreads) {
        return ReviewerApp.BuildExtrasForTestsAsync(github, fallbackGithub, context, settings, forceReviewThreads,
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    private static void CallAutoResolveMissingInlineThreads(GitHubClient github, PullRequestContext context,
        HashSet<string>? expectedKeys, ReviewSettings settings) {
        ReviewerApp.AutoResolveMissingInlineThreadsForTestsAsync(github, context, expectedKeys, settings, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    private static void CallAutoResolveStaleThreads(GitHubClient github, IReadOnlyList<PullRequestReviewThread> threads,
        ReviewSettings settings) {
        ReviewerApp.AutoResolveStaleThreadsForTestsAsync(github, threads, settings, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    private static void CallAutoResolveStaleThreads(GitHubClient github, GitHubClient? fallbackGithub,
        IReadOnlyList<PullRequestReviewThread> threads, ReviewSettings settings) {
        ReviewerApp.AutoResolveStaleThreadsForTestsAsync(github, fallbackGithub, threads, settings, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    private static string CallBuildThreadAssessmentPrompt(PullRequestContext context,
        IReadOnlyList<PullRequestReviewThread> threads, IReadOnlyList<PullRequestFile> files, ReviewSettings settings,
        string? diffNote) {
        return ReviewerApp.BuildThreadAssessmentPromptForTests(context, threads, files, settings, diffNote);
    }

    private static int CallTryResolveKeptBotThreadsAfterNoBlockers(GitHubClient github, GitHubClient? fallbackGithub,
        IReadOnlyList<PullRequestReviewThread> candidates, List<ReviewerApp.ThreadAssessment> resolved,
        List<ReviewerApp.ThreadAssessment> kept,
        ReviewSettings settings, int maxAdditionalResolves) {
        return ReviewerApp.TryResolveKeptBotThreadsAfterNoBlockersForTestsAsync(github, fallbackGithub, candidates, resolved, kept,
                settings, maxAdditionalResolves, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    private static bool CallHasValidResolveEvidence(string evidence, PullRequestReviewThread thread,
        IReadOnlyList<PullRequestFile> files, int maxPatchChars) {
        return ReviewerApp.HasValidResolveEvidenceForTests(evidence, thread, files, maxPatchChars);
    }

    private static string CallNormalizeResolveEvidence(string evidence) {
        return ReviewerApp.NormalizeResolveEvidenceForTests(evidence);
    }

    private static IReadOnlyList<string> CallCollectEvidenceScanPaths(IReadOnlyList<PullRequestFile> files, string evidence,
        string? preferredPath = null) {
        return ReviewerApp.CollectEvidenceScanPathsForTests(files, evidence, preferredPath);
    }

    private static IReadOnlySet<string> CallBuildInlineMatchKeys(string path, int line, string? body = null,
        string? snippet = null, string? signatureSource = null) {
        return ReviewerApp.BuildInlineMatchKeysForTests(path, line, body, snippet, signatureSource);
    }

    private static string? CallBuildInlineSignatureMarker(string path, int line, string? body = null,
        string? snippet = null, string? signatureSource = null) {
        return ReviewerApp.BuildInlineSignatureMarkerForTests(path, line, body, snippet, signatureSource);
    }

    private static (IReadOnlyList<PullRequestFile> Files, string Note) CallResolveDiffRangeFiles(GitHubClient github,
        PullRequestContext context, string range, IReadOnlyList<PullRequestFile> currentFiles, ReviewSettings settings) {
        var result = ReviewerApp.ResolveDiffRangeFilesForTestsAsync(github, context, range, currentFiles, settings,
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        return (result.Files, result.DiffNote);
    }

    private static bool CallIsIntegrationForbidden(Exception ex) {
        return ReviewerApp.IsIntegrationForbiddenForTests(ex);
    }

    private static string CallBuildThreadResolveError(Exception primaryError, Exception? fallbackError) {
        return ReviewerApp.BuildThreadResolveErrorForTests(primaryError, fallbackError);
    }

    private static string CallBuildAutoResolvePermissionNote(int permissionDeniedCount, params string[] credentialLabels) {
        return ReviewerApp.BuildAutoResolvePermissionNoteForTests(permissionDeniedCount, credentialLabels);
    }

    private static string CallAppendConversationResolutionPermissionBlocker(string summaryBody, int deniedThreadCount,
        bool? requiresConversationResolution, params string[] credentialLabels) {
        return ReviewerApp.AppendConversationResolutionPermissionBlockerForTests(summaryBody,
            AutoResolvePermissionDiagnostics.From(deniedThreadCount, credentialLabels), requiresConversationResolution);
    }

    private static bool CallIsOwnedSummaryComment(IssueComment comment) {
        return ReviewerApp.IsOwnedSummaryCommentForTests(comment);
    }
}
#endif
