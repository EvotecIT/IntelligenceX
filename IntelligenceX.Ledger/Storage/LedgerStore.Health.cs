using DBAClientX;
using IntelligenceX.Ledger.Contracts;

namespace IntelligenceX.Ledger.Storage;

public sealed partial class LedgerStore {
    public async Task UpdateReporterHealthAsync(ReporterHealth health, CancellationToken token = default, DateTimeOffset? now = null) {
        var policy = await ReadPolicyAsync(token);
        // GitHub tallies have their own table; the health row keeps only the reporter's own state.
        var usage = health.GitHubUsage ?? [];
        var auth = health.Auth ?? [];
        health = health with { GitHubUsage = null, Auth = null };
        await _gate.WaitAsync(token);
        try {
            await using var db = await _database.OpenSessionAsync(path, token);
            await db.RunInTransactionAsync(async (tx, ct) => {
                await UpsertReporterHealthAsync(tx, health, preserveCoverage: false, ct);
                await UpdateLoadAsync(tx, health, policy, ct);
                if (usage.Length > 0) await RecordGitHubUsageAsync(tx, health.MachineId, usage, DateTimeOffset.UtcNow, ct);
                if (auth.Length > 0) await RecordAuthAsync(tx, health.MachineId, auth, now ?? DateTimeOffset.UtcNow, ct);
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
        if (session.SourceSessionId is { } child && session.StartedBySessionId is { } parent && session.StartedByProvider is { } parentProvider &&
            session.StartedVia is { } via && child != parent)
            await RecordLinkAsync(db, new(parentProvider, parent.ToString(), item.Provider, child.ToString(), via,
                session.CreatedAtUtc ?? item.FirstObservedAtUtc), token);
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
    /// <summary>
    /// Pages returned to the file system per maintenance tick, about 16 MB. The write gate is held meanwhile, so a tick
    /// has to stay short even on a busy disk; freed space comes back over several ticks.
    /// </summary>
    private const int VacuumPagesPerTick = 4096;

    /// <summary>
    /// Rows removed per table per tick. A tick runs every five minutes, so this still clears over 500,000 events a day,
    /// several times the busiest day seen, while keeping each delete short.
    /// </summary>
    internal const int PruneBatch = 2000;

    /// <summary>
    /// One bounded retention pass per maintenance tick: old events, then old transitions with their review marks.
    /// Projections are kept. Freed pages go back to the file system when the database uses incremental vacuum.
    /// </summary>
    public async Task<int> PruneEventsAsync(DateTimeOffset now, int limit = PruneBatch, CancellationToken token = default) {
        await MaintainLeasesAsync(now, token);
        await _gate.WaitAsync(token);
        try {
            await using var db = await _database.OpenSessionAsync(path, token);
            var bound = Math.Clamp(limit, 1, 10000);
            var removed = await db.RunInTransactionAsync(async (tx, ct) => {
                var recent = new Dictionary<string, object?> { ["cutoff"] = (now - EventRetention).ToString("O"), ["limit"] = bound };
                var events = await tx.ExecuteNonQueryAsync("""
                    DELETE FROM events WHERE ordinal IN (SELECT ordinal FROM events WHERE received<@cutoff ORDER BY received LIMIT @limit)
                    """, recent, ct);
                events += await tx.ExecuteNonQueryAsync("""
                    DELETE FROM receipts WHERE id IN (SELECT id FROM receipts WHERE received<@cutoff ORDER BY received LIMIT @limit)
                    """, recent, ct);
                var expired = new Dictionary<string, object?> { ["cutoff"] = (now - TransitionRetention).ToString("O"), ["limit"] = bound };
                await tx.ExecuteNonQueryAsync("""
                    DELETE FROM reviews WHERE transition IN (SELECT ordinal FROM transitions WHERE recorded<@cutoff ORDER BY ordinal LIMIT @limit)
                    """, expired, ct);
                // Sign-in history ages out here too, so it shrinks even when no reporter sends sign-in facts any more.
                await tx.ExecuteNonQueryAsync("""
                    DELETE FROM auth_changes WHERE id IN (SELECT id FROM auth_changes WHERE at<@cutoff ORDER BY id LIMIT @limit)
                    """, new Dictionary<string, object?> { ["cutoff"] = (now - AuthChangeRetention).ToString("O"), ["limit"] = bound }, ct);
                return events + await tx.ExecuteNonQueryAsync("""
                    DELETE FROM transitions WHERE ordinal IN (SELECT ordinal FROM transitions WHERE recorded<@cutoff ORDER BY ordinal LIMIT @limit)
                    """, expired, ct);
            }, token);
            // The deletes are committed; returning their pages can wait for a later pass if the disk is full or busy.
            try {
                if (Convert.ToInt32(await db.ExecuteScalarAsync("PRAGMA auto_vacuum", cancellationToken: token)) == 2)
                    await db.ExecuteNonQueryAsync("PRAGMA incremental_vacuum(" + VacuumPagesPerTick + ")", cancellationToken: token);
            } catch (Microsoft.Data.Sqlite.SqliteException) { }
            return removed;
        } finally { _gate.Release(); }
    }

    /// <summary>
    /// Rewrites the database without its free pages and switches it to incremental vacuum, so later retention
    /// passes return space as they go. The caller must hold the service writer lease: the service has to be stopped.
    /// </summary>
    public static async Task<(long Before, long After, bool Checkpointed)> CompactAsync(string databasePath, CancellationToken token = default) {
        var before = new FileInfo(databasePath).Length;
        using var database = new SQLite();
        var checkpointed = false;
        await using (var db = await database.OpenSessionAsync(databasePath, token)) {
            await db.ExecuteNonQueryAsync("PRAGMA auto_vacuum=INCREMENTAL", cancellationToken: token);
            await db.ExecuteNonQueryAsync("VACUUM", cancellationToken: token);
            // Another reader (an online backup, say) can hold the old snapshot; the file shrinks once it lets go.
            for (var attempt = 0; attempt < 10 && !checkpointed; attempt++) {
                if (attempt > 0) await Task.Delay(TimeSpan.FromSeconds(1), token);
                checkpointed = (await db.QueryAsListAsync("PRAGMA wal_checkpoint(TRUNCATE)", row => row.GetInt64(0), cancellationToken: token))
                    .FirstOrDefault() == 0;
            }
        }
        return (before, new FileInfo(databasePath).Length, checkpointed);
    }
}
