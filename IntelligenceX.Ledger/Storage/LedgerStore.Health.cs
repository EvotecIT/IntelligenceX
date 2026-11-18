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
                ["native"] = session.SourceSessionId?.ToString(),
                ["actor"] = item.Provider + ":" + session.SourceSessionId,
                ["ownerActor"] = item.Provider + ":" + session.SourceSessionId + ":owner-driven" };
            var reservations = await db.QueryAsListAsync("""
                SELECT json FROM reservations WHERE holder IN (@item,@native,@actor,@ownerActor)
                """, row => Read<Lease>(row.GetString(0)), args, cancellationToken: token);
            foreach (var lease in reservations.Where(lease => lease.AcquiredAtUtc is not null))
                await RememberLeaseAsync(db, lease, archivedAt, token);
            await db.ExecuteNonQueryAsync("DELETE FROM reservations WHERE holder IN (@item,@native,@actor,@ownerActor)", args, token);
        }
        return changed;
    }

    /// <summary>Events are kept for 7 days. Transitions and their review marks feed the daily reports, which are also
    /// written to files, so they are kept for 30.</summary>
    internal static readonly TimeSpan EventRetention = TimeSpan.FromDays(7), TransitionRetention = TimeSpan.FromDays(30);
    /// <summary>Pages returned to the file system per maintenance tick, about 100 MB, so one tick stays short.</summary>
    private const int VacuumPagesPerTick = 25600;

    /// <summary>
    /// One bounded retention pass per maintenance tick: old events, then old transitions with their review marks.
    /// Projections are kept. Freed pages go back to the file system when the database uses incremental vacuum.
    /// </summary>
    public async Task<int> PruneEventsAsync(DateTimeOffset now, int limit = 10000, CancellationToken token = default) {
        await MaintainLeasesAsync(now, token);
        await _gate.WaitAsync(token);
        try {
            await using var db = await _database.OpenSessionAsync(path, token);
            var bound = Math.Clamp(limit, 1, 10000);
            var removed = await db.RunInTransactionAsync(async (tx, ct) => {
                var events = await tx.ExecuteNonQueryAsync("""
                    DELETE FROM events WHERE ordinal IN (SELECT ordinal FROM events WHERE received<@cutoff ORDER BY received LIMIT @limit)
                    """, new Dictionary<string, object?> { ["cutoff"] = (now - EventRetention).ToString("O"), ["limit"] = bound }, ct);
                var expired = new Dictionary<string, object?> { ["cutoff"] = (now - TransitionRetention).ToString("O"), ["limit"] = bound };
                await tx.ExecuteNonQueryAsync("""
                    DELETE FROM reviews WHERE transition IN (SELECT ordinal FROM transitions WHERE recorded<@cutoff ORDER BY ordinal LIMIT @limit)
                    """, expired, ct);
                return events + await tx.ExecuteNonQueryAsync("""
                    DELETE FROM transitions WHERE ordinal IN (SELECT ordinal FROM transitions WHERE recorded<@cutoff ORDER BY ordinal LIMIT @limit)
                    """, expired, ct);
            }, token);
            if (Convert.ToInt32(await db.ExecuteScalarAsync("PRAGMA auto_vacuum", cancellationToken: token)) == 2)
                await db.ExecuteNonQueryAsync("PRAGMA incremental_vacuum(" + VacuumPagesPerTick + ")", cancellationToken: token);
            return removed;
        } finally { _gate.Release(); }
    }

    /// <summary>
    /// Rewrites the database without its free pages and switches it to incremental vacuum, so later retention
    /// passes return space as they go. The caller must hold the service writer lease: the service has to be stopped.
    /// </summary>
    public static async Task<(long Before, long After)> CompactAsync(string databasePath, CancellationToken token = default) {
        var before = new FileInfo(databasePath).Length;
        using var database = new SQLite();
        await using (var db = await database.OpenSessionAsync(databasePath, token)) {
            await db.ExecuteNonQueryAsync("PRAGMA auto_vacuum=INCREMENTAL", cancellationToken: token);
            await db.ExecuteNonQueryAsync("VACUUM", cancellationToken: token);
            await db.ExecuteNonQueryAsync("PRAGMA wal_checkpoint(TRUNCATE)", cancellationToken: token);
        }
        return (before, new FileInfo(databasePath).Length);
    }
}
