namespace IntelligenceX.Reviewer;

public static partial class ReviewerApp {
    // Compile-time test seam to avoid brittle reflection against private methods.
    internal static string TrimPatchForTests(string patch, int maxPatchChars) => TrimPatch(patch, maxPatchChars);

    internal static (IReadOnlyList<PullRequestFile> Files, string BudgetNote) PrepareFilesForTests(
        IReadOnlyList<PullRequestFile> files, int maxFiles, int maxPatchChars) =>
        PrepareFiles(files, maxFiles, maxPatchChars);

    internal static string FormatUsageSummaryForTests(ChatGptUsageSnapshot snapshot) => FormatUsageSummary(snapshot);

    internal static string? EvaluateUsageBudgetGuardFailureForTests(ReviewSettings settings, ChatGptUsageSnapshot snapshot) =>
        EvaluateUsageBudgetGuardFailure(settings, snapshot);

    internal static Task<string?> TryBuildUsageBudgetGuardFailureForTestsAsync(ReviewSettings settings, ReviewProvider provider) =>
        TryBuildUsageBudgetGuardFailureAsync(settings, provider);

    internal static IReadOnlyList<string> OrderOpenAiAccountsForTests(IReadOnlyList<string> accountIds, string rotation,
        string? stickyAccountId, long rotationSeed) =>
        OrderOpenAiAccounts(accountIds, rotation, stickyAccountId, rotationSeed);

    internal static Task<(bool Success, string? Error, bool BudgetGuardEvaluated)> TryResolveOpenAiAccountForTestsAsync(
        ReviewSettings settings) =>
        TryResolveOpenAiAccountAsync(settings);

    internal static bool ShouldAutoResolveMissingInlineThreadsForTests(ReviewSettings settings, PullRequestContext context,
        HashSet<string>? inlineKeys, int inlineCommentsCount) =>
        ShouldAutoResolveMissingInlineThreads(settings, context, inlineKeys, inlineCommentsCount);

    internal static Task<ReviewContextExtras> BuildExtrasForTestsAsync(GitHubClient github, PullRequestContext context,
        ReviewSettings settings, bool forceReviewThreads, CancellationToken cancellationToken = default) {
        var codeHostReader = new GitHubCodeHostReader(github);
        return BuildExtrasAsync(codeHostReader, github, null, context, settings, cancellationToken, forceReviewThreads);
    }

    internal static Task AutoResolveMissingInlineThreadsForTestsAsync(GitHubClient github, PullRequestContext context,
        HashSet<string>? expectedKeys, ReviewSettings settings, CancellationToken cancellationToken = default) {
        var codeHostReader = new GitHubCodeHostReader(github);
        return AutoResolveMissingInlineThreadsAsync(codeHostReader, github, null, context, expectedKeys, settings,
            cancellationToken);
    }

    internal static Task AutoResolveStaleThreadsForTestsAsync(GitHubClient github, IReadOnlyList<PullRequestReviewThread> threads,
        ReviewSettings settings, CancellationToken cancellationToken = default) =>
        AutoResolveStaleThreadsAsync(github, null, threads, settings, cancellationToken);

    internal static string BuildThreadAssessmentPromptForTests(PullRequestContext context,
        IReadOnlyList<PullRequestReviewThread> threads, IReadOnlyList<PullRequestFile> files, ReviewSettings settings,
        string? diffNote) =>
        BuildThreadAssessmentPrompt(context, threads, files, settings, diffNote);

    internal static bool HasValidResolveEvidenceForTests(string evidence, PullRequestReviewThread thread,
        IReadOnlyList<PullRequestFile> files, int maxPatchChars) {
        var patchIndex = BuildInlinePatchIndex(files);
        var patchLookup = BuildPatchLookup(files, maxPatchChars);
        return HasValidResolveEvidence(evidence, thread, patchIndex, patchLookup, maxPatchChars);
    }

    internal static string NormalizeResolveEvidenceForTests(string evidence) => NormalizeResolveEvidence(evidence);

    internal static Task<(IReadOnlyList<PullRequestFile> Files, string DiffNote)> ResolveDiffRangeFilesForTestsAsync(
        GitHubClient github, PullRequestContext context, string range, IReadOnlyList<PullRequestFile> currentFiles,
        ReviewSettings settings, CancellationToken cancellationToken = default) {
        var codeHostReader = new GitHubCodeHostReader(github);
        return ResolveDiffRangeFilesAsync(codeHostReader, context, range, currentFiles, settings, cancellationToken);
    }
}
