using DBAClientX;
using IntelligenceX.Ledger.Contracts;

namespace IntelligenceX.Ledger.Storage;

public sealed partial class LedgerStore {
    /// <summary>Agent cleanup eligibility and informational personal-session age, using existing observations.</summary>
    public async Task<SessionCleanupPlan> CleanupPlanAsync(DateTimeOffset now, string? machine = null, CancellationToken token = default) {
        if (machine is not null && !Label(machine, 64)) throw new ArgumentException("Invalid machine ID.");
        var policy = await ReadPolicyAsync(token);
        await using var db = await _database.OpenSessionAsync(path, token);
        var args = new Dictionary<string, object?> { ["cutoff"] = now.AddHours(-policy.Session.IdleArchiveHours).ToString("O"), ["machine"] = machine };
        var items = await db.QueryAsListAsync("""
            SELECT i.json FROM session_status s JOIN items i ON i.id=s.item
            WHERE s.archived=0 AND (@machine IS NULL OR s.machine=@machine)
              AND json_extract(i.json,'$.session.lastActivityAtUtc')<=@cutoff
            ORDER BY json_extract(i.json,'$.session.lastActivityAtUtc'),s.item
            """, row => Read<LedgerItem>(row.GetString(0)), args, cancellationToken: token);
        var leases = await ReservationsAsync(db, now, token);
        var ready = new List<SessionCleanupEntry>();
        var held = new List<SessionCleanupEntry>();
        var personal = new List<SessionCleanupEntry>();
        foreach (var item in items) {
            var fact = item.Session!;
            var id = fact.SourceSessionId?.ToString() ?? item.Id;
            var repo = fact.Repository;
            var reason = await CleanupReasonAsync(db, item, leases, now, token);
            var entry = new SessionCleanupEntry(id, item.MachineId, item.Provider, fact.Title ?? repo?.Title, fact.Origin,
                Math.Round((now - fact.LastActivityAtUtc!.Value).TotalDays, 1), repo?.Worktree, repo?.Branch, reason);
            if (fact.Origin != "agent") personal.Add(entry);
            else if (reason is "clean-and-pushed" or "workspace-already-missing") ready.Add(entry);
            else held.Add(entry);
        }
        return new(policy.Revision, now, policy.Session.IdleArchiveHours, ready.ToArray(), held.ToArray(), personal.ToArray());
    }

    /// <summary>Why cleanup may or may not archive one session: clean-and-pushed and workspace-already-missing allow it.</summary>
    private async Task<string> CleanupReasonAsync(SQLiteAsyncSession db, LedgerItem item, List<Reservation> leases, DateTimeOffset now, CancellationToken token) {
        var fact = item.Session!;
        var repo = fact.Repository;
        var holders = Holders(item);
        if (fact.Origin != "agent") return "personal-session-information-only";
        if (leases.Any(row => holders.Contains(row.Lease.Holder))) return "live-lease-or-queue";
        if (fact.Question || await HasOpenQuestionAsync(db, item, now, false, token)) return "open-question";
        if (repo?.Dirty == true) return "dirty-worktree";
        if (repo?.Unpushed == true) return "unpushed-worktree";
        if (repo?.GitState == "workspace-missing") return "workspace-already-missing";
        if (repo?.Dirty != false || repo.Unpushed != false) return "workspace-check-required";
        return "clean-and-pushed";
    }

    /// <summary>Every holder string a session may reserve under: its item id, native id, and provider-qualified forms.</summary>
    private static HashSet<string> Holders(LedgerItem item) {
        var id = item.Session?.SourceSessionId?.ToString() ?? item.Id;
        return new(StringComparer.Ordinal) { item.Id, id, item.Provider + ":" + id, item.Provider + ":" + id + ":owner-driven" };
    }
}
