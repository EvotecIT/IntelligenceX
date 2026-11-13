using System.Globalization;
using System.Text;
using IntelligenceX.Ledger.Contracts;

namespace IntelligenceX.Ledger.Reporting;

/// <summary>Pure comparison of observed branches, PRs, exact changed paths and lexical Unicode title tokens.</summary>
internal static class RepositoryOverlapEvaluator {
    internal const int FilePreviewLimit = 5;
    private const int SessionTitleLimit = 80;

    internal static string NormalizeRepo(string repo) {
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);
        return repo.Trim().ToLowerInvariant();
    }

    internal static RepositoryWorktree[] GroupWorktrees(IEnumerable<RepositorySession> sessions) => sessions
        .GroupBy(session => (session.Machine, Path: WorktreeKey(session.Worktree) ?? session.AgentId))
        .Select(group => {
            var latest = group.OrderByDescending(session => session.ObservedAtUtc ?? session.LastActivityAtUtc)
                .ThenByDescending(session => session.Live).ThenBy(session => session.AgentId, StringComparer.Ordinal).First();
            var files = latest.GitState == "workspace-missing" ? null : latest.Files?.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            var count = latest.GitState == "workspace-missing" ? null : latest.FileCount ?? (latest.FilesCapped ? null : files?.Length);
            var compact = group.OrderByDescending(session => session.Live).ThenByDescending(session => session.LastActivityAtUtc)
                .ThenBy(session => session.AgentId, StringComparer.Ordinal)
                .Select(session => new RepositoryWorktreeSession(session.AgentId, session.Provider,
                    session.Title is { Length: > SessionTitleLimit } title ? title[..SessionTitleLimit] : session.Title,
                    session.LastActivityAtUtc, session.Live)).ToArray();
            return new RepositoryWorktree(latest.Machine, latest.Worktree, latest.Branch, compact,
                group.SelectMany(session => session.PullRequests).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                files?.Take(FilePreviewLimit).ToArray(), count, files is not null && (latest.FilesCapped || files.Length > FilePreviewLimit),
                latest.BaseRef, latest.GitState, group.All(session => session.PullRequestsKnown == true) ? true : null) {
                ComparisonFiles = files, ComparisonSessions = group.ToArray()
            };
        })
        .OrderByDescending(worktree => worktree.Sessions.Any(session => session.Live))
        .ThenBy(worktree => worktree.Machine, StringComparer.Ordinal).ThenBy(worktree => worktree.Worktree, StringComparer.Ordinal).ToArray();

    private static string? WorktreeKey(string? path) {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var normalized = path.Replace('\\', '/').TrimEnd('/');
        return normalized.StartsWith("//", StringComparison.Ordinal) || normalized.Length > 1 && normalized[1] == ':'
            ? normalized.ToUpperInvariant() : normalized;
    }

    internal static string Summary(RepositoryWorktree[] worktrees) {
        var live = worktrees.Sum(worktree => worktree.Sessions.Count(session => session.Live));
        var recent = worktrees.Sum(worktree => worktree.Sessions.Count(session => !session.Live));
        var labels = worktrees.Where(worktree => worktree.Sessions.Any(session => session.Live)).Select(worktree => {
            var label = worktree.Branch?.Split('/').LastOrDefault() ?? worktree.Worktree?.Replace('\\', '/').Split('/').LastOrDefault() ?? "unknown worktree";
            var providers = string.Join("/", worktree.Sessions.Where(session => session.Live).Select(session => session.Provider).Distinct().Order(StringComparer.Ordinal));
            return $"{label.Replace('\r', ' ').Replace('\n', ' ')} ({worktree.Machine}, {providers})";
        });
        return live > 0 ? $"{live} live: {string.Join(", ", labels)}. {recent} recent." : $"0 live. {recent} recent in {worktrees.Length} worktrees.";
    }

    /// <summary>Missing observations remain possible. Positive live branch/file evidence establishes conflict.</summary>
    internal static RepositoryOverlap Evaluate(RepositoryActivity activity, string? branch, string[]? files, string? text) {
        var coverage = new HashSet<string>(activity.Coverage, StringComparer.Ordinal);
        var requestedFiles = new HashSet<string>(files ?? [], StringComparer.Ordinal);
        var requestedTokens = Tokens(text);
        var hasBranch = !string.IsNullOrWhiteSpace(branch);
        var branchPrefix = "branch:" + NormalizeRepo(activity.Repo) + ":";
        var branchLeases = activity.Leases.Where(lease => lease.Key.StartsWith(branchPrefix, StringComparison.OrdinalIgnoreCase)).ToArray();
        var repoLeases = activity.Leases.Where(lease => lease.Key.Equals("repo:" + NormalizeRepo(activity.Repo), StringComparison.OrdinalIgnoreCase) ||
            lease.Key.Equals("build:" + NormalizeRepo(activity.Repo), StringComparison.OrdinalIgnoreCase)).ToArray();
        if (repoLeases.Length > 0) coverage.Add("repository-lease-held");
        var inferredPullRequests = activity.Worktrees.SelectMany(worktree => worktree.ComparisonSessions ?? [])
            .Where(session => hasBranch && session.Branch == branch)
            .SelectMany(session => session.PullRequests).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var conflict = hasBranch && branchLeases.Any(lease => lease.Key[branchPrefix.Length..] == branch);
        var matches = new List<OverlapMatch>();
        foreach (var worktree in activity.Worktrees) {
            var reasons = new List<string>();
            var sessions = worktree.ComparisonSessions ?? [];
            var matchingBranch = sessions.Where(session => hasBranch && session.Branch == branch).ToArray();
            var sameBranch = matchingBranch.Length > 0;
            var matchingPullRequests = sessions.Where(session => session.PullRequests.Any(inferredPullRequests.Contains)).ToArray();
            var samePullRequest = matchingPullRequests.Length > 0;
            var matchingFiles = sessions.Where(session => session.Files?.Any(requestedFiles.Contains) == true).ToArray();
            var currentShared = (worktree.ComparisonFiles ?? worktree.Files ?? []).Where(requestedFiles.Contains).ToArray();
            var shared = matchingFiles.SelectMany(session => session.Files ?? []).Concat(currentShared)
                .Where(requestedFiles.Contains).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            var matchedBranches = matchingBranch.Concat(matchingPullRequests).Concat(matchingFiles)
                .Select(session => session.Branch).OfType<string>()
                .Concat(currentShared.Length > 0 && worktree.Branch is { } currentBranch ? [currentBranch] : [])
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            var branchHeld = branchLeases.Any(lease => matchedBranches.Contains(lease.Key[branchPrefix.Length..], StringComparer.Ordinal));
            if (sameBranch) reasons.Add("same-branch");
            if (samePullRequest) reasons.Add("same-pull-request-inferred-from-branch");
            if (shared.Length > 0) reasons.Add("shared-changed-files");
            if (branchHeld && (sameBranch || samePullRequest || shared.Length > 0)) reasons.Add("branch-lease-held");
            if (sessions.Any(session => TitleOverlap(requestedTokens, Tokens(session.Title)))) reasons.Add("lexical-title-token-overlap");
            if (repoLeases.Any(lease => worktree.Sessions.Any(session => lease.Holder == session.AgentId))) reasons.Add("repository-lease-held");
            if (requestedTokens.Count > 0 && worktree.Sessions.Any(session => string.IsNullOrWhiteSpace(session.Title))) coverage.Add("session-title-unknown");
            if (reasons.Count == 0) continue;
            matches.Add(new(worktree, reasons.ToArray(), shared.Take(FilePreviewLimit).ToArray(), shared.Length) {
                MatchedBranches = matchedBranches.Length > 0 ? matchedBranches : null
            });
            if (shared.Length > FilePreviewLimit) coverage.Add("shared-files-capped");
            var liveMatch = matchingBranch.Any(session => session.Live) ||
                matchingPullRequests.Any(session => session.Live) ||
                currentShared.Length > 0 && sessions.Any(session => session.Live);
            if ((sameBranch || samePullRequest || shared.Length > 0) && (liveMatch || branchHeld)) conflict = true;
        }

        if (!hasBranch && requestedFiles.Count == 0 && requestedTokens.Count == 0) coverage.Add("overlap-target-unspecified");
        var verdict = conflict ? "conflict" : matches.Count > 0 || coverage.Count > 0 ? "possible" : "none";
        return new(activity.Summary, activity.Repo, verdict, matches.ToArray(), activity.Leases, coverage.Order(StringComparer.Ordinal).ToArray());
    }

    private static bool TitleOverlap(HashSet<string> requested, HashSet<string> title) {
        var shared = requested.Count(title.Contains);
        if (shared >= 2 && shared * 3 >= requested.Count) return true;
        return requested.Count == 1 && shared == 1 && requested.Single().EnumerateRunes().Count() >= 3;
    }

    private static HashSet<string> Tokens(string? text) {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(text)) return tokens;
        var token = new StringBuilder();
        foreach (var rune in text.Normalize(NormalizationForm.FormC).EnumerateRunes()) {
            var category = Rune.GetUnicodeCategory(rune);
            if (Rune.IsLetterOrDigit(rune) || category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark) {
                token.Append(rune.ToString());
            } else if (token.Length > 0) {
                tokens.Add(token.ToString());
                token.Clear();
            }
        }
        if (token.Length > 0) tokens.Add(token.ToString());
        return tokens;
    }
}
