using System.Globalization;
using System.Text;
using IntelligenceX.Ledger.Contracts;

namespace IntelligenceX.Ledger.Reporting;

/// <summary>Pure comparison of observed branches, PRs, exact changed paths and lexical Unicode title tokens.</summary>
internal static class RepositoryOverlapEvaluator {
    internal static string NormalizeRepo(string repo) {
        ArgumentException.ThrowIfNullOrWhiteSpace(repo);
        return repo.Trim().ToLowerInvariant();
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
        var inferredPullRequests = activity.Sessions.Where(session => hasBranch && session.Branch == branch)
            .SelectMany(session => session.PullRequests).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var conflict = hasBranch && branchLeases.Any(lease => lease.Key[branchPrefix.Length..] == branch);
        var matches = new List<OverlapMatch>();
        foreach (var session in activity.Sessions) {
            var reasons = new List<string>();
            var sameBranch = hasBranch && session.Branch == branch;
            var samePullRequest = session.PullRequests.Any(inferredPullRequests.Contains);
            var shared = (session.Files ?? []).Where(requestedFiles.Contains).Distinct(StringComparer.Ordinal).Take(20).ToArray();
            var branchHeld = session.Branch is not null && branchLeases.Any(lease => lease.Key[branchPrefix.Length..] == session.Branch);
            if (sameBranch) reasons.Add("same-branch");
            if (samePullRequest) reasons.Add("same-pull-request-inferred-from-branch");
            if (shared.Length > 0) reasons.Add("shared-changed-files");
            if (branchHeld && (sameBranch || samePullRequest || shared.Length > 0)) reasons.Add("branch-lease-held");
            if (TitleOverlap(requestedTokens, Tokens(session.Title))) reasons.Add("lexical-title-token-overlap");
            if (repoLeases.Any(lease => lease.Holder == session.AgentId)) reasons.Add("repository-lease-held");
            if (requestedTokens.Count > 0 && string.IsNullOrWhiteSpace(session.Title)) coverage.Add("session-title-unknown");
            if (reasons.Count == 0) continue;
            matches.Add(new(session, reasons.ToArray(), shared));
            if ((sameBranch || samePullRequest || shared.Length > 0) && (session.Live || branchHeld)) conflict = true;
        }

        if (!hasBranch && requestedFiles.Count == 0 && requestedTokens.Count == 0) coverage.Add("overlap-target-unspecified");
        var verdict = conflict ? "conflict" : matches.Count > 0 || coverage.Count > 0 ? "possible" : "none";
        return new(activity.Repo, verdict, matches.ToArray(), activity.Leases, coverage.Order(StringComparer.Ordinal).ToArray());
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
