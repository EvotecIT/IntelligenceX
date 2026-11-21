using System.Text.Json;
using IntelligenceX.Ledger.Contracts;

namespace IntelligenceX.Ledger.Reporting;

/// <summary>Compact current activity with full live identities and summarized recent membership.</summary>
internal static class RepositoryActivityFormatting {
    internal static RepositoryActivity Compact(RepositoryActivity activity) {
        var worktrees = activity.Worktrees.Select(CompactWorktree).ToArray();
        var result = activity with { Worktrees = worktrees, Checkouts = activity.Checkouts?.Select(CompactCheckout).ToArray() };
        while (JsonSerializer.SerializeToUtf8Bytes(result, LedgerReadJson.Default.RepositoryActivity).Length > 3000) {
            var largest = worktrees.Select((worktree, index) => (worktree, index))
                .Where(row => row.worktree.Files is { Length: > 0 })
                .OrderByDescending(row => row.worktree.Files!.Sum(file => file.Length)).FirstOrDefault();
            if (largest.worktree is null) break;
            worktrees[largest.index] = largest.worktree with {
                Files = largest.worktree.Files![..^1], FilesCapped = true
            };
        }
        // Then checkout rows: clean, fully read worktrees go first, then clean main checkouts, then the rest from the end.
        // The summary already names every checkout with pending or unread work.
        var checkouts = result.Checkouts?.ToList();
        var omitted = 0;
        while (checkouts is { Count: > 0 } &&
               JsonSerializer.SerializeToUtf8Bytes(result with { Checkouts = checkouts.ToArray() }, LedgerReadJson.Default.RepositoryActivity).Length > 3000) {
            var index = checkouts.FindLastIndex(row => IsClean(row.Checkout) && !row.Checkout.Primary);
            if (index < 0) index = checkouts.FindLastIndex(row => IsClean(row.Checkout));
            if (index < 0) index = checkouts.Count - 1;
            checkouts.RemoveAt(index);
            omitted++;
        }
        return omitted == 0 ? result : result with { Checkouts = checkouts!.ToArray(), CheckoutsOmitted = omitted };
    }

    internal static RepositoryOverlap Compact(RepositoryOverlap overlap) => overlap with {
        Matches = overlap.Matches.Select(match => match with { Worktree = CompactWorktree(match.Worktree) }).ToArray()
    };

    // Zero counts and the commit add nothing to a quick answer; details keeps them.
    private static RepositoryCheckout CompactCheckout(RepositoryCheckout row) => row with {
        Checkout = row.Checkout with {
            Commit = null, Ahead = Positive(row.Checkout.Ahead), Behind = Positive(row.Checkout.Behind),
            ChangedFiles = Positive(row.Checkout.ChangedFiles), UntrackedFiles = Positive(row.Checkout.UntrackedFiles),
            Stashes = Positive(row.Checkout.Stashes), UnpushedBranches = row.Checkout.UnpushedBranches is { Length: > 0 } b ? b : null
        }
    };

    private static int? Positive(int? value) => value > 0 ? value : null;

    private static bool IsClean(CheckoutObservation checkout) => checkout.State == "observed" && checkout.Ahead is null or 0 &&
        checkout.ChangedFiles is null or 0 && checkout.UntrackedFiles is null or 0 && checkout.Stashes is null or 0 &&
        checkout.UnpushedBranches is null or { Length: 0 };

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
