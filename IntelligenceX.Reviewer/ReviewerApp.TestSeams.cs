namespace IntelligenceX.Reviewer;

public static partial class ReviewerApp {
    // Test-only forwarders: keep tests compile-time bound to internal behavior without private reflection.

    /// <summary>Test-only forwarder for patch trimming behavior.</summary>
    internal static string TrimPatchForTests(string patch, int maxPatchChars) => TrimPatch(patch, maxPatchChars);

    /// <summary>Test-only forwarder for file preparation limits.</summary>
    internal static (IReadOnlyList<PullRequestFile> Files, string BudgetNote) PrepareFilesForTests(
        IReadOnlyList<PullRequestFile> files, int maxFiles, int maxPatchChars) =>
        PrepareFiles(files, maxFiles, maxPatchChars);

    /// <summary>Test-only forwarder for usage summary formatting.</summary>
    internal static string FormatUsageSummaryForTests(ChatGptUsageSnapshot snapshot) => FormatUsageSummary(snapshot);

    /// <summary>Test-only forwarder for provider-limit usage summary formatting.</summary>
    internal static string FormatUsageSummaryForTests(IntelligenceX.Telemetry.Limits.ProviderLimitSnapshot snapshot) =>
        FormatUsageSummary(snapshot);

    /// <summary>Test-only forwarder for usage budget guard evaluation.</summary>
    internal static string? EvaluateUsageBudgetGuardFailureForTests(ReviewSettings settings, ChatGptUsageSnapshot snapshot) =>
        EvaluateUsageBudgetGuardFailure(settings, snapshot);

    /// <summary>Test-only forwarder for provider-limit usage budget guard evaluation.</summary>
    internal static string? EvaluateUsageBudgetGuardFailureForTests(ReviewSettings settings,
        IntelligenceX.Telemetry.Limits.ProviderLimitSnapshot snapshot) =>
        EvaluateUsageBudgetGuardFailure(settings, snapshot);

    /// <summary>Test-only forwarder for async usage budget guard construction.</summary>
    internal static Task<string?> TryBuildUsageBudgetGuardFailureForTestsAsync(ReviewSettings settings, ReviewProvider provider) =>
        TryBuildUsageBudgetGuardFailureAsync(settings, provider);

    /// <summary>Test-only forwarder for OpenAI account ordering.</summary>
    internal static IReadOnlyList<string> OrderOpenAiAccountsForTests(IReadOnlyList<string> accountIds, string rotation,
        string? stickyAccountId, long rotationSeed) =>
        OrderOpenAiAccounts(accountIds, rotation, stickyAccountId, rotationSeed);

    /// <summary>Test-only forwarder for OpenAI account resolution.</summary>
    internal static Task<(bool Success, string? Error, bool BudgetGuardEvaluated)> TryResolveOpenAiAccountForTestsAsync(
        ReviewSettings settings) =>
        TryResolveOpenAiAccountAsync(settings);

    /// <summary>Test-only forwarder for auth validation.</summary>
    internal static Task<bool> ValidateAuthForTestsAsync(ReviewSettings settings) =>
        ValidateAuthAsync(settings);

    /// <summary>Test-only forwarder for missing-inline auto-resolve gate logic.</summary>
    internal static bool ShouldAutoResolveMissingInlineThreadsForTests(ReviewSettings settings, PullRequestContext context,
        HashSet<string>? inlineKeys, int inlineCommentsCount) =>
        ShouldAutoResolveMissingInlineThreads(settings, context, inlineKeys, inlineCommentsCount);

    /// <summary>Test-only forwarder for review context extra loading.</summary>
    internal static Task<ReviewContextExtras> BuildExtrasForTestsAsync(GitHubClient github, PullRequestContext context,
        ReviewSettings settings, bool forceReviewThreads, CancellationToken cancellationToken = default) {
        var codeHostReader = new GitHubCodeHostReader(github);
        return BuildExtrasAsync(codeHostReader, github, null, context, settings, cancellationToken, forceReviewThreads);
    }

    /// <summary>Test-only forwarder for review context extra loading with explicit fallback GitHub client.</summary>
    internal static Task<ReviewContextExtras> BuildExtrasForTestsAsync(GitHubClient github, GitHubClient? fallbackGithub,
        PullRequestContext context, ReviewSettings settings, bool forceReviewThreads,
        CancellationToken cancellationToken = default) {
        var codeHostReader = new GitHubCodeHostReader(github);
        return BuildExtrasAsync(codeHostReader, github, fallbackGithub, context, settings, cancellationToken, forceReviewThreads);
    }

    /// <summary>Test-only forwarder for missing-inline thread auto-resolution.</summary>
    internal static Task AutoResolveMissingInlineThreadsForTestsAsync(GitHubClient github, PullRequestContext context,
        HashSet<string>? expectedKeys, ReviewSettings settings, CancellationToken cancellationToken = default) {
        var codeHostReader = new GitHubCodeHostReader(github);
        return AutoResolveMissingInlineThreadsAsync(codeHostReader, github, null, context, expectedKeys, settings,
            cancellationToken);
    }

    /// <summary>Test-only forwarder for summary ownership detection.</summary>
    internal static bool IsOwnedSummaryCommentForTests(IssueComment comment) => IsOwnedSummaryComment(comment);

    /// <summary>Test-only forwarder for summary-stability carryover body extraction.</summary>
    internal static string? ExtractSummaryBodyForTests(string? body, int maxChars) => ExtractSummaryBody(body, maxChars);

    /// <summary>Test-only forwarder for stale-thread auto-resolution.</summary>
    internal static Task AutoResolveStaleThreadsForTestsAsync(GitHubClient github, IReadOnlyList<PullRequestReviewThread> threads,
        ReviewSettings settings, CancellationToken cancellationToken = default) =>
        AutoResolveStaleThreadsForTestsCoreAsync(github, null, threads, settings, cancellationToken);

    /// <summary>Test-only forwarder for stale-thread auto-resolution with explicit fallback token client.</summary>
    internal static Task AutoResolveStaleThreadsForTestsAsync(GitHubClient github, GitHubClient? fallbackGithub,
        IReadOnlyList<PullRequestReviewThread> threads, ReviewSettings settings, CancellationToken cancellationToken = default) =>
        AutoResolveStaleThreadsForTestsCoreAsync(github, fallbackGithub, threads, settings, cancellationToken);

    private static async Task AutoResolveStaleThreadsForTestsCoreAsync(GitHubClient github, GitHubClient? fallbackGithub,
        IReadOnlyList<PullRequestReviewThread> threads, ReviewSettings settings, CancellationToken cancellationToken) {
        await AutoResolveStaleThreadsAsync(github, fallbackGithub, threads, settings, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Test-only forwarder for thread assessment prompt rendering.</summary>
    internal static string BuildThreadAssessmentPromptForTests(PullRequestContext context,
        IReadOnlyList<PullRequestReviewThread> threads, IReadOnlyList<PullRequestFile> files, ReviewSettings settings,
        string? diffNote) =>
        BuildThreadAssessmentPrompt(context, threads, files, settings, diffNote);

    /// <summary>Test-only forwarder for no-blockers kept-thread sweep.</summary>
    internal static Task<int> TryResolveKeptBotThreadsAfterNoBlockersForTestsAsync(GitHubClient github,
        GitHubClient? fallbackGithub, IReadOnlyList<PullRequestReviewThread> candidates, List<ThreadAssessment> resolved,
        List<ThreadAssessment> kept, ReviewSettings settings, int maxAdditionalResolves,
        CancellationToken cancellationToken = default) =>
        TryResolveKeptBotThreadsAfterNoBlockersAsync(github, fallbackGithub, candidates, resolved, kept, settings,
            maxAdditionalResolves, cancellationToken);

    /// <summary>Test-only forwarder for resolve evidence validation.</summary>
    internal static bool HasValidResolveEvidenceForTests(string evidence, PullRequestReviewThread thread,
        IReadOnlyList<PullRequestFile> files, int maxPatchChars) {
        var patchIndex = BuildInlinePatchIndex(files);
        var patchLookup = BuildPatchLookup(files, maxPatchChars);
        return HasValidResolveEvidence(evidence, thread, patchIndex, patchLookup, maxPatchChars);
    }

    /// <summary>Test-only forwarder for resolve evidence normalization.</summary>
    internal static string NormalizeResolveEvidenceForTests(string evidence) => NormalizeResolveEvidence(evidence);

    /// <summary>Test-only helper that exposes evidence scan traversal order for dedup regression tests.</summary>
    internal static IReadOnlyList<string> CollectEvidenceScanPathsForTests(IReadOnlyList<PullRequestFile> files,
        string evidence, string? preferredPath = null) {
        var patchIndex = BuildInlinePatchIndex(files);
        var patchLookup = BuildPatchLookup(files, int.MaxValue);
        var scannedPaths = new List<string>();
        _ = HasEvidenceInAnyDiffContext(patchIndex, patchLookup, evidence, preferredPath, scannedPaths);
        return scannedPaths;
    }

    /// <summary>Test-only helper that exposes inline composite matching keys used by missing-inline auto-resolve.</summary>
    internal static IReadOnlySet<string> BuildInlineMatchKeysForTests(string path, int line, string? body, string? snippet,
        string? signatureSource) =>
        BuildInlineMatchKeys(path, line, body, snippet, signatureSource);

    /// <summary>Test-only helper that exposes hidden inline signature marker formatting.</summary>
    internal static string? BuildInlineSignatureMarkerForTests(string path, int line, string? body, string? snippet,
        string? signatureSource) =>
        TryBuildInlineSignatureMarker(path, line, body, snippet, signatureSource);

    /// <summary>Test-only forwarder for diff-range file resolution.</summary>
    internal static Task<(IReadOnlyList<PullRequestFile> Files, string DiffNote)> ResolveDiffRangeFilesForTestsAsync(
        GitHubClient github, PullRequestContext context, string range, IReadOnlyList<PullRequestFile> currentFiles,
        ReviewSettings settings, CancellationToken cancellationToken = default) {
        var codeHostReader = new GitHubCodeHostReader(github);
        return ResolveDiffRangeFilesAsync(codeHostReader, context, range, currentFiles, settings, cancellationToken);
    }

    internal static string AppendConversationResolutionPermissionBlockerForTests(string summaryBody,
        AutoResolvePermissionDiagnostics diagnostics, bool? requiresConversationResolution) =>
        AppendConversationResolutionPermissionBlocker(summaryBody, diagnostics, requiresConversationResolution);

    internal static IReadOnlyList<PullRequestReviewThread> SelectAssessmentCandidatesForTests(
        IReadOnlyList<PullRequestReviewThread> threads, ReviewSettings settings) =>
        SelectAssessmentCandidates(threads, settings);

    internal static Task ReplyToKeptThreadsForTestsAsync(GitHubClient github, PullRequestContext context,
        IReadOnlyList<PullRequestReviewThread> candidates, IReadOnlyDictionary<string, ThreadAssessment> assessments,
        string? headSha, string? diffNote, ReviewSettings settings, CancellationToken cancellationToken = default) =>
        ReplyToKeptThreadsAsync(github, context, candidates, assessments, headSha, diffNote, settings, cancellationToken);
}
