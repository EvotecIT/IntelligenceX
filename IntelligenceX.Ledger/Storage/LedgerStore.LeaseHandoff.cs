using DBAClientX;
using IntelligenceX.Ledger.Contracts;

namespace IntelligenceX.Ledger.Storage;

public sealed partial class LedgerStore {
    private static void ValidateHandoff(LeaseAcquire command) {
        if (command.TransferLeaseId is null && command.Handoff is null) return;
        var handoff = command.Handoff;
        if (KeyClass(command.Key) != "branch" || !Guid.TryParseExact(command.TransferLeaseId, "N", out _) ||
            command.Key.Split(':', 3).Length != 3 || handoff is null || handoff.Commit is not { Length: 40 or 64 } ||
            !handoff.Commit.All(Uri.IsHexDigit) || !Label(handoff.NextStep, 320) ||
            handoff.PullRequest is not null && (!Uri.TryCreate(handoff.PullRequest, UriKind.Absolute, out var pr) ||
                pr.Scheme != "https" || pr.UserInfo.Length > 0 || pr.Query.Length > 0 || pr.Fragment.Length > 0 || handoff.PullRequest.Length > 512))
            throw new ArgumentException("A branch transfer requires the held lease ID and a pushed checkpoint with a short next step.");
    }

    private static async Task<bool> PushedCheckpointAsync(SQLiteAsyncSession db, Lease previous, LeaseHandoff handoff, CancellationToken token) {
        var session = await HolderSessionAsync(db, previous, token);
        var repo = session?.Repository;
        var key = previous.Key.Split(':', 3);
        return repo is { Dirty: false, Unpushed: false, GitState: "observed" } &&
            string.Equals(repo.Repo, key[1], StringComparison.OrdinalIgnoreCase) && repo.Branch == key[2] &&
            string.Equals(repo.Commit, handoff.Commit, StringComparison.OrdinalIgnoreCase);
    }
}
