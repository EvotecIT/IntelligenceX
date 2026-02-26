namespace IntelligenceX.Reviewer;

public static partial class ReviewerApp {

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
            var evidence = thread.GetString("evidence") ?? string.Empty;
            result.Add(new ThreadAssessment(id.Trim(), action, reason.Trim(), evidence.Trim()));
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

    internal sealed record ThreadAssessment(string Id, string Action, string Reason, string Evidence);

    private static bool HasValidResolveEvidence(string? evidence, PullRequestReviewThread thread,
        Dictionary<string, List<PatchLine>> patchIndex, IReadOnlyDictionary<string, string> patchLookup,
        int maxPatchChars) {
        if (string.IsNullOrWhiteSpace(evidence)) {
            return false;
        }
        var normalizedEvidence = NormalizeResolveEvidence(evidence);
        if (normalizedEvidence.Length == 0) {
            return false;
        }
        string? preferredPath = null;
        if (TryGetThreadLocation(thread, out var path, out var line)) {
            preferredPath = path;
            var context = BuildThreadDiffContext(patchIndex, patchLookup, path, line, maxPatchChars);
            if (!IsUnavailableDiffContext(context)) {
                return HasEvidenceInContext(context, normalizedEvidence);
            }
            if (!thread.IsOutdated) {
                return false;
            }
        } else if (!thread.IsOutdated) {
            return false;
        }
        return HasEvidenceInAnyDiffContext(patchIndex, patchLookup, normalizedEvidence, preferredPath);
    }

    private static string NormalizeResolveEvidence(string evidence) {
        var normalized = evidence.Trim();
        if (normalized.Length < 2) {
            return normalized;
        }
        normalized = TryTrimSingleWrapper(normalized, '"')
                     ?? TryTrimSingleWrapper(normalized, '\'')
                     ?? TryTrimSingleWrapper(normalized, '`')
                     ?? normalized;
        return normalized;
    }

    private static string? TryTrimSingleWrapper(string value, char wrapper) {
        if (value.Length < 2 || value[0] != wrapper || value[^1] != wrapper) {
            return null;
        }
        return value.Substring(1, value.Length - 2).Trim();
    }

    private static bool IsUnavailableDiffContext(string? context) {
        return string.IsNullOrWhiteSpace(context) || context.Equals("<unavailable>", StringComparison.Ordinal);
    }

    private static bool HasEvidenceInAnyDiffContext(Dictionary<string, List<PatchLine>> patchIndex,
        IReadOnlyDictionary<string, string> patchLookup, string evidence, string? preferredPath = null,
        List<string>? scannedPaths = null) {
        var preferred = string.IsNullOrWhiteSpace(preferredPath) ? string.Empty : NormalizePath(preferredPath);
        if (!string.IsNullOrWhiteSpace(preferred) &&
            TryGetPatchContextForPath(preferred, patchIndex, patchLookup, out var preferredContext)) {
            scannedPaths?.Add(preferred);
            if (HasEvidenceInContext(preferredContext, evidence)) {
                return true;
            }
        }

        var visitedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(preferred)) {
            visitedPaths.Add(preferred);
        }

        foreach (var (path, lines) in patchIndex) {
            if (!visitedPaths.Add(path) || lines.Count == 0) {
                continue;
            }
            scannedPaths?.Add(path);
            var context = string.Join("\n", lines.Select(static line => $"{line.LineNumber}: {line.Text}"));
            if (HasEvidenceInContext(context, evidence)) {
                return true;
            }
        }

        foreach (var (path, patch) in patchLookup) {
            if (!visitedPaths.Add(path) || string.IsNullOrWhiteSpace(patch)) {
                continue;
            }
            scannedPaths?.Add(path);
            var context = $"<file patch>\n{patch}";
            if (HasEvidenceInContext(context, evidence)) {
                return true;
            }
        }
        return false;
    }

    private static bool TryGetPatchContextForPath(string normalizedPath, Dictionary<string, List<PatchLine>> patchIndex,
        IReadOnlyDictionary<string, string> patchLookup, out string context) {
        if (patchIndex.TryGetValue(normalizedPath, out var lines) && lines.Count > 0) {
            context = string.Join("\n", lines.Select(static line => $"{line.LineNumber}: {line.Text}"));
            return true;
        }
        if (patchLookup.TryGetValue(normalizedPath, out var patch) && !string.IsNullOrWhiteSpace(patch)) {
            context = $"<file patch>\n{patch}";
            return true;
        }
        context = string.Empty;
        return false;
    }

    private static bool HasEvidenceInContext(string context, string evidence) {
        if (context.Contains(evidence, StringComparison.OrdinalIgnoreCase)) {
            return true;
        }
        var normalizedContext = NormalizeSnippetText(context);
        var normalizedEvidence = NormalizeSnippetText(evidence);
        if (string.IsNullOrWhiteSpace(normalizedContext) || string.IsNullOrWhiteSpace(normalizedEvidence)) {
            return false;
        }
        return normalizedContext.Contains(normalizedEvidence, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeThreadAssessmentId(string? id) {
        return id?.Trim() ?? string.Empty;
    }

    private static async Task<int> TryResolveKeptBotThreadsAfterNoBlockersAsync(
        GitHubClient github,
        GitHubClient? fallbackGithub,
        IReadOnlyList<PullRequestReviewThread> candidates,
        List<ThreadAssessment> resolved,
        List<ThreadAssessment> kept,
        ReviewSettings settings,
        CancellationToken cancellationToken) {
        if (kept.Count == 0) {
            return 0;
        }

        var candidateById = new Dictionary<string, PullRequestReviewThread>(StringComparer.OrdinalIgnoreCase);
        foreach (var thread in candidates) {
            var normalizedId = NormalizeThreadAssessmentId(thread.Id);
            if (normalizedId.Length == 0 || candidateById.ContainsKey(normalizedId)) {
                continue;
            }
            candidateById[normalizedId] = thread;
        }

        var resolvedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in resolved) {
            var normalizedId = NormalizeThreadAssessmentId(item.Id);
            if (normalizedId.Length > 0) {
                resolvedIds.Add(normalizedId);
            }
        }

        var sweepResolvedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sweepResolved = 0;
        foreach (var item in kept) {
            var normalizedId = NormalizeThreadAssessmentId(item.Id);
            if (normalizedId.Length == 0 || resolvedIds.Contains(normalizedId) || sweepResolvedIds.Contains(normalizedId)) {
                continue;
            }
            if (!candidateById.TryGetValue(normalizedId, out var thread)) {
                continue;
            }
            if (thread.IsResolved || thread.TotalComments > thread.Comments.Count || !ThreadHasOnlyBotComments(thread, settings)) {
                continue;
            }

            var result = await TryResolveThreadAsync(github, fallbackGithub, thread.Id, cancellationToken).ConfigureAwait(false);
            if (!result.Resolved) {
                Console.Error.WriteLine($"Failed no-blockers sweep resolve for review thread {thread.Id}: {result.Error ?? "unknown error"}");
                continue;
            }

            sweepResolved++;
            sweepResolvedIds.Add(normalizedId);
            resolved.Add(new ThreadAssessment(item.Id, "resolve", FormatNoBlockersSweepReason(item.Reason), item.Evidence));
        }

        if (sweepResolvedIds.Count > 0) {
            kept.RemoveAll(item => sweepResolvedIds.Contains(NormalizeThreadAssessmentId(item.Id)));
        }

        return sweepResolved;
    }

    private static string FormatNoBlockersSweepReason(string reason) {
        const string suffix = "(no-blockers sweep)";
        return string.IsNullOrWhiteSpace(reason) ? suffix : $"{reason} {suffix}";
    }

    private static async Task<(bool Resolved, string? Error)> TryResolveThreadAsync(GitHubClient github,
        GitHubClient? fallbackGithub, string threadId, CancellationToken cancellationToken) {
        Exception? primaryError;
        try {
            await github.ResolveReviewThreadAsync(threadId, cancellationToken).ConfigureAwait(false);
            return (true, null);
        } catch (Exception ex) {
            primaryError = ex;
        }

        Exception? fallbackError = null;
        if (fallbackGithub is not null) {
            try {
                await fallbackGithub.ResolveReviewThreadAsync(threadId, cancellationToken).ConfigureAwait(false);
                return (true, null);
            } catch (Exception ex) {
                fallbackError = ex;
            }
        }

        var confirmedResolved = await TryConfirmThreadResolvedAsync(fallbackGithub ?? github, github, threadId, cancellationToken)
            .ConfigureAwait(false);
        if (confirmedResolved) {
            return (true, null);
        }

        if (IsIntegrationForbidden(primaryError) || (fallbackError is not null && IsIntegrationForbidden(fallbackError))) {
            LogIntegrationForbiddenHint();
        }

        return (false, BuildThreadResolveError(primaryError, fallbackError));
    }

    private static async Task<bool> TryConfirmThreadResolvedAsync(GitHubClient preferredGithub,
        GitHubClient? secondaryGithub, string threadId, CancellationToken cancellationToken) {
        var preferredState = await TryGetThreadResolvedStateAsync(preferredGithub, threadId, cancellationToken)
            .ConfigureAwait(false);
        if (preferredState == true) {
            return true;
        }

        if (secondaryGithub is null || ReferenceEquals(preferredGithub, secondaryGithub)) {
            return false;
        }

        var secondaryState = await TryGetThreadResolvedStateAsync(secondaryGithub, threadId, cancellationToken)
            .ConfigureAwait(false);
        return secondaryState == true;
    }

    private static async Task<bool?> TryGetThreadResolvedStateAsync(GitHubClient github, string threadId,
        CancellationToken cancellationToken) {
        try {
            var thread = await github.GetPullRequestReviewThreadAsync(threadId, maxComments: 1, cancellationToken)
                .ConfigureAwait(false);
            return thread?.IsResolved;
        } catch {
            return null;
        }
    }

    private static string BuildThreadResolveError(Exception primaryError, Exception? fallbackError) {
        if (fallbackError is null) {
            return primaryError.Message;
        }
        return $"primary: {primaryError.Message}; fallback: {fallbackError.Message}";
    }

    internal static bool IsIntegrationForbiddenForTests(Exception ex) => IsIntegrationForbidden(ex);

    internal static string BuildThreadResolveErrorForTests(Exception primaryError, Exception? fallbackError) =>
        BuildThreadResolveError(primaryError, fallbackError);

    private static bool IsIntegrationForbidden(Exception ex) {
        if (ex.Message.Contains("Resource not accessible by integration", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("FORBIDDEN", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("INSUFFICIENT_SCOPES", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("requires one of the following scopes", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }
        return ex.InnerException is not null && IsIntegrationForbidden(ex.InnerException);
    }

    private static void LogIntegrationForbiddenHint() {
        if (Interlocked.Exchange(ref _integrationForbiddenHintLogged, 1) == 1) {
            return;
        }
        // This hint is emitted only from auto-resolve thread resolution flows.
        var lines = new[] {
            "Auto-resolve thread resolution: GitHub returned \"Resource not accessible by integration\".",
            "Hint: This usually means the GitHub App installation token cannot resolve review threads.",
            "Hint: Re-authorize or reinstall the GitHub App after permission changes.",
            "Hint: Confirm the app installation includes this repository.",
            "Hint: Ensure the app has Pull requests: Read & write (and Issues: write if needed).",
            "Hint: Verify INTELLIGENCEX_GITHUB_APP_ID/INTELLIGENCEX_GITHUB_APP_PRIVATE_KEY point to the intended app.",
            "Hint: To bypass the app token, remove INTELLIGENCEX_GITHUB_APP_ID/INTELLIGENCEX_GITHUB_APP_PRIVATE_KEY to use GITHUB_TOKEN.",
            "Hint: GITHUB_TOKEN is provided automatically in GitHub Actions; outside Actions set a PAT in GITHUB_TOKEN."
        };
        Console.Error.WriteLine(string.Join(Environment.NewLine, lines));
    }

    private static bool ThreadHasOnlyBotComments(PullRequestReviewThread thread, ReviewSettings settings) {
        if (thread.Comments.Count == 0) {
            return false;
        }
        foreach (var comment in thread.Comments) {
            if (string.IsNullOrWhiteSpace(comment.Author)) {
                return false;
            }
            if (!IsAutoResolveBot(comment.Author, settings)) {
                return false;
            }
        }
        return true;
    }
}
