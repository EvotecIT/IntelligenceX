using System.Text.Json;
using IntelligenceX.Ledger.Contracts;

namespace IntelligenceX.Ledger.Reporting;

/// <summary>Compact current activity with full live identities and summarized recent membership.</summary>
internal static class RepositoryActivityFormatting {
    internal static RepositoryActivity Compact(RepositoryActivity activity) {
        var worktrees = activity.Worktrees.Select(CompactWorktree).ToArray();
        var result = activity with { Worktrees = worktrees };
        while (JsonSerializer.SerializeToUtf8Bytes(result, LedgerReadJson.Default.RepositoryActivity).Length > 3000) {
            var largest = worktrees.Select((worktree, index) => (worktree, index))
                .Where(row => row.worktree.Files is { Length: > 0 })
                .OrderByDescending(row => row.worktree.Files!.Sum(file => file.Length)).FirstOrDefault();
            if (largest.worktree is null) break;
            worktrees[largest.index] = largest.worktree with {
                Files = largest.worktree.Files![..^1], FilesCapped = true
            };
        }
        return result;
    }

    internal static RepositoryOverlap Compact(RepositoryOverlap overlap) => overlap with {
        Matches = overlap.Matches.Select(match => match with { Worktree = CompactWorktree(match.Worktree) }).ToArray()
    };

    private static RepositoryWorktree CompactWorktree(RepositoryWorktree worktree) {
        var recent = worktree.Sessions.Where(session => !session.Live).ToArray();
        return worktree with {
            Sessions = worktree.Sessions.Where(session => session.Live).ToArray(),
            RecentSessionCount = recent.Length,
            RecentTitles = recent.Where(session => !string.IsNullOrWhiteSpace(session.Title))
                .Select(session => session.Title!.Length > 48 ? session.Title[..48] : session.Title)
                .Distinct(StringComparer.Ordinal).ToArray(),
            BaseRef = null, GitState = worktree.GitState == "observed" ? null : worktree.GitState, PullRequestsKnown = null
        };
    }
}
