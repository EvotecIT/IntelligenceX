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
            var reason = "clean-and-pushed";
            if (fact.Origin != "agent") reason = "personal-session-information-only";
            else if (leases.Any(row => row.Lease.Holder == id || row.Lease.Holder == item.Id ||
                row.Lease.Holder == item.Provider + ":" + id || row.Lease.Holder == item.Provider + ":" + id + ":owner-driven")) reason = "live-lease-or-queue";
            else if (fact.Question || await HasOpenQuestionAsync(db, item, now, false, token)) reason = "open-question";
            else if (repo?.Dirty == true) reason = "dirty-worktree";
            else if (repo?.Unpushed == true) reason = "unpushed-worktree";
            else if (repo?.GitState == "workspace-missing") reason = "workspace-already-missing";
            else if (repo?.Dirty != false || repo.Unpushed != false) reason = "workspace-check-required";
            var entry = new SessionCleanupEntry(id, item.MachineId, item.Provider, fact.Title ?? repo?.Title, fact.Origin,
                Math.Round((now - fact.LastActivityAtUtc!.Value).TotalDays, 1), repo?.Worktree, repo?.Branch, reason);
            if (fact.Origin != "agent") personal.Add(entry);
            else if (reason is "clean-and-pushed" or "workspace-already-missing") ready.Add(entry);
            else held.Add(entry);
        }
        return new(policy.Revision, now, policy.Session.IdleArchiveHours, ready.ToArray(), held.ToArray(), personal.ToArray());
    }
}
