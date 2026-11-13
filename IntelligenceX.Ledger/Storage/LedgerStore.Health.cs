using DBAClientX;
using IntelligenceX.Ledger.Contracts;

namespace IntelligenceX.Ledger.Storage;

public sealed partial class LedgerStore {
    public async Task UpdateReporterHealthAsync(ReporterHealth health, CancellationToken token = default) {
        var policy = await ReadPolicyAsync(token);
        await _gate.WaitAsync(token);
        try {
            await using var db = await _database.OpenSessionAsync(path, token);
            await db.RunInTransactionAsync(async (tx, ct) => {
                await UpsertReporterHealthAsync(tx, health, preserveCoverage: false, ct);
                await UpdateLoadAsync(tx, health, policy, ct);
                foreach (var progress in health.Progress ?? []) {
                    var json = await tx.ExecuteScalarAsync("SELECT json FROM items WHERE id=@id",
                        new Dictionary<string, object?> { ["id"] = progress.ItemId }, ct);
                    if (json is not string value) continue;
                    var item = Read<LedgerItem>(value);
                    if (item.MachineId != health.MachineId || item.Session?.TurnOpen != true || item.Session.TurnId != progress.TurnId) continue;
                    // The state event still owns turn identity. The health row supplies only later activity.
                    await ApplyProgressAsync(tx, item, progress,
                        new(Guid.Empty, "reporter-health", health.SourceVersion, health.ObservedAtUtc, DateTimeOffset.UtcNow), DateTimeOffset.UtcNow, ct);
                }
            }, token);
        } finally { _gate.Release(); }
    }

    private async Task ApplyProgressAsync(SQLiteAsyncSession db, LedgerItem item, ReporterProgress progress, EvidenceRef evidence,
        DateTimeOffset now, CancellationToken token) {
        if (item.Session?.LastActivityAtUtc >= progress.LastActivityAtUtc) return;
        item = item with { Session = item.Session! with { LastActivityAtUtc = progress.LastActivityAtUtc }, SessionEvidence = evidence };
        item = await EvaluateAsync(db, item, now, token, item.Current?.LastKnown == true);
        await db.ExecuteNonQueryAsync("UPDATE items SET json=@json WHERE id=@id",
            new Dictionary<string, object?> { ["id"] = item.Id, ["json"] = Json(item) }, token);
        if (item.Kind == "session") await UpsertRepositorySessionAsync(db, item, token);
    }

    private static Task<int> UpsertReporterHealthAsync(SQLiteAsyncSession db, ReporterHealth health, bool preserveCoverage, CancellationToken token) =>
        db.ExecuteNonQueryAsync("""
            INSERT INTO reporter_health(reporter,machine,observed,json) VALUES(@reporter,@machine,@observed,@json)
            ON CONFLICT(reporter) DO UPDATE SET observed=MAX(reporter_health.observed,excluded.observed),
              json=CASE WHEN @preserve=1 OR excluded.observed<reporter_health.observed THEN reporter_health.json ELSE excluded.json END
            """, new Dictionary<string, object?> {
                ["reporter"] = health.ReporterId, ["machine"] = health.MachineId, ["observed"] = health.ObservedAtUtc.ToString("O"),
                ["json"] = Json(health), ["preserve"] = preserveCoverage ? 1 : 0
            }, token);

    private static async Task<int> UpsertSessionStatusAsync(SQLiteAsyncSession db, LedgerItem item, SessionFact session, CancellationToken token) {
        var changed = await db.ExecuteNonQueryAsync("""
            INSERT INTO session_status(item,machine,provider,open,limited,archived,account)
            VALUES(@item,@machine,@provider,@open,@limited,@archived,@account)
            ON CONFLICT(item) DO UPDATE SET open=excluded.open,limited=excluded.limited,archived=excluded.archived,account=excluded.account
            """, new Dictionary<string, object?> {
                ["item"] = item.Id, ["machine"] = item.MachineId, ["provider"] = item.Provider,
                ["open"] = session.TurnOpen == true ? 1 : 0, ["limited"] = session.UsageLimit ? 1 : 0,
                ["archived"] = session.Archived == true ? 1 : 0, ["account"] = session.AccountId
            }, token);
        await UpsertSessionLookupAsync(db, item, session, token);
        if (session.Archived == true && session.ArchiveObservedAtUtc is { } archivedAt) {
            var args = new Dictionary<string, object?> { ["item"] = item.Id,
                ["native"] = session.SourceSessionId?.ToString(), ["machine"] = item.MachineId,
                ["actor"] = item.Provider + ":" + session.SourceSessionId,
                ["ownerActor"] = item.Provider + ":" + session.SourceSessionId + ":owner-driven" };
            var reservations = await db.QueryAsListAsync("""
                SELECT json FROM reservations WHERE machine=@machine AND holder IN (@item,@native,@actor,@ownerActor)
                """, row => Read<Lease>(row.GetString(0)), args, cancellationToken: token);
            foreach (var lease in reservations.Where(lease => lease.AcquiredAtUtc is not null))
                await RememberLeaseAsync(db, lease, archivedAt, token);
            await db.ExecuteNonQueryAsync("DELETE FROM reservations WHERE machine=@machine AND holder IN (@item,@native,@actor,@ownerActor)", args, token);
        }
        return changed;
    }

    /// <summary>One bounded retention transaction per maintenance tick. Projections and transitions are retained.</summary>
    public async Task<int> PruneEventsAsync(DateTimeOffset now, int limit = 10000, CancellationToken token = default) {
        await MaintainLeasesAsync(now, token);
        await _gate.WaitAsync(token);
        try {
            await using var db = await _database.OpenSessionAsync(path, token);
            return await db.ExecuteNonQueryAsync("""
                DELETE FROM events WHERE ordinal IN (SELECT ordinal FROM events WHERE received<@cutoff ORDER BY received LIMIT @limit)
                """, new Dictionary<string, object?> { ["cutoff"] = now.AddDays(-7).ToString("O"), ["limit"] = Math.Clamp(limit, 1, 10000) }, token);
        } finally { _gate.Release(); }
    }
}
