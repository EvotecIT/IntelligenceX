using System.Text;
using DBAClientX;
using IntelligenceX.Ledger.Contracts;

namespace IntelligenceX.Ledger.Storage;

public sealed partial class LedgerStore {
    // WAL readers and short lease writes do not wait for the full work projection's application gate.
    private readonly SemaphoreSlim _leaseGate = new(1, 1);

    internal static bool LeaseKey(string? key) => key is { Length: > 0 and <= 160 } &&
        !key.Any(c => char.IsWhiteSpace(c) || char.IsControl(c));
    private static bool Label(string? value, int maximum) => value is { Length: > 0 } && value.Length <= maximum && !value.Any(char.IsControl);
    private static bool Ttl(int seconds) => seconds is >= 1 and <= 86400;

    public async Task<LeaseReply> AcquireLeaseAsync(LeaseAcquire command, DateTimeOffset now, CancellationToken token = default) {
        if (!LeaseKey(command.Key) || !Label(command.Holder, 128) || !Label(command.Machine, 64) || !Label(command.Purpose, 96) || !Ttl(command.TtlSeconds))
            throw new ArgumentException("Invalid lease fields or TTL (1..86400 seconds).");
        await _leaseGate.WaitAsync(token);
        try {
            await using var db = await _database.OpenSessionAsync(path, token);
            return await db.RunInTransactionAsync(async (tx, ct) => {
                var lease = new Lease(command.Key, command.Holder, command.Machine, command.Purpose, Guid.NewGuid().ToString("N"), now.AddSeconds(command.TtlSeconds));
                if (Encoding.UTF8.GetByteCount(Json(lease)) > 900) throw new ArgumentException("Lease metadata exceeds the compact response bound.");
                await tx.ExecuteNonQueryAsync("""
                    INSERT INTO leases(key,id,machine,expires,json) VALUES(@key,@id,@machine,@expires,@json)
                    ON CONFLICT(key) DO UPDATE SET id=excluded.id,machine=excluded.machine,expires=excluded.expires,json=excluded.json
                    WHERE leases.expires<=@now
                    """, LeaseParameters(lease, now), ct);
                var current = await ReadLeaseAsync(tx, command.Key, ct);
                return new LeaseReply(current?.LeaseId == lease.LeaseId, current?.LeaseId == lease.LeaseId ? "acquired" : "busy", current);
            }, cancellationToken: token);
        } finally { _leaseGate.Release(); }
    }

    public async Task<LeaseReply> RenewLeaseAsync(LeaseRenew command, DateTimeOffset now, CancellationToken token = default) {
        if (!LeaseKey(command.Key) || !Guid.TryParseExact(command.LeaseId, "N", out _) || !Ttl(command.TtlSeconds)) throw new ArgumentException("Invalid lease renewal.");
        await _leaseGate.WaitAsync(token);
        try {
            await using var db = await _database.OpenSessionAsync(path, token);
            return await db.RunInTransactionAsync(async (tx, ct) => {
                var lease = await ReadLeaseAsync(tx, command.Key, ct);
                if (lease is null || lease.ExpiresAtUtc <= now || lease.LeaseId != command.LeaseId) return new LeaseReply(false, "not-held");
                lease = lease with { ExpiresAtUtc = now.AddSeconds(command.TtlSeconds) };
                await tx.ExecuteNonQueryAsync("UPDATE leases SET expires=@expires,json=@json WHERE key=@key AND id=@id", LeaseParameters(lease, now), ct);
                return new LeaseReply(true, "renewed", lease);
            }, cancellationToken: token);
        } finally { _leaseGate.Release(); }
    }

    public async Task<LeaseReply> ReleaseLeaseAsync(LeaseRelease command, DateTimeOffset now, CancellationToken token = default) {
        if (!LeaseKey(command.Key) || !Guid.TryParseExact(command.LeaseId, "N", out _)) throw new ArgumentException("Invalid lease release.");
        await _leaseGate.WaitAsync(token);
        try {
            await using var db = await _database.OpenSessionAsync(path, token);
            var changed = await db.ExecuteNonQueryAsync("DELETE FROM leases WHERE key=@key AND id=@id AND expires>@now",
                new Dictionary<string, object?> { ["key"] = command.Key, ["id"] = command.LeaseId, ["now"] = now.ToString("O") }, token);
            return new(changed == 1, changed == 1 ? "released" : "not-held");
        } finally { _leaseGate.Release(); }
    }

    public async Task<LeaseQuery> WhoAsync(string key, bool prefix, DateTimeOffset now, CancellationToken token = default) {
        if (!LeaseKey(key)) throw new ArgumentException("Invalid lease key.");
        await using var db = await _database.OpenSessionAsync(path, token);
        // substr avoids treating '%' or '_' in a key as SQL wildcard syntax.
        var predicate = prefix ? "substr(key,1,length(@key))=@key" : "key=@key";
        var args = new Dictionary<string, object?> { ["key"] = key, ["now"] = now.ToString("O") };
        var total = Convert.ToInt32(await db.ExecuteScalarAsync("SELECT COUNT(*) FROM leases WHERE expires>@now AND " + predicate, args, token));
        var rows = await db.QueryAsListAsync("SELECT json FROM leases WHERE expires>@now AND " + predicate + " ORDER BY key LIMIT 4",
            r => Read<Lease>(r.GetString(0)), args, cancellationToken: token);
        return Compact(new LeaseQuery(key, rows.ToArray(), total), result => result with { Leases = result.Leases[..^1] }, result => result.Leases.Length);
    }

    public async Task<MachineStatus> MachineStatusAsync(string machine, string reporterState, DateTimeOffset now, CancellationToken token = default) {
        if (!Label(machine, 64)) throw new ArgumentException("Invalid machine ID.");
        await using var db = await _database.OpenSessionAsync(path, token);
        var args = new Dictionary<string, object?> { ["machine"] = machine, ["now"] = now.ToString("O") };
        var counts = await db.QueryAsListAsync("""
            SELECT COALESCE(SUM(open=1),0),COALESCE(SUM(limited=1),0) FROM session_status WHERE machine=@machine AND archived=0
            """, r => (r.GetInt32(0), r.GetInt32(1)), args, cancellationToken: token);
        var total = Convert.ToInt32(await db.ExecuteScalarAsync("SELECT COUNT(*) FROM leases WHERE machine=@machine AND expires>@now", args, token));
        var leases = await db.QueryAsListAsync("SELECT json FROM leases WHERE machine=@machine AND expires>@now ORDER BY key LIMIT 4",
            r => Read<Lease>(r.GetString(0)), args, cancellationToken: token);
        var observations = await db.QueryAsListAsync("SELECT json FROM reporter_health WHERE machine=@machine ORDER BY observed DESC",
            r => Read<ReporterHealth>(r.GetString(0)), args, cancellationToken: token);
        var observation = observations.FirstOrDefault(health => health.Probe is not null);
        var probe = observation?.Probe ?? new ProbeAvailability("unavailable", "not-reported");
        if (probe.State is "available" or "paused" && (reporterState != "healthy" ||
            observation is null || now - observation.ObservedAtUtc > TimeSpan.FromMinutes(2)))
            probe = new("unavailable", "reporter-silent") { ObservedAtUtc = probe.ObservedAtUtc, SampledAtUtc = probe.SampledAtUtc };
        if (probe.State is "available" or "paused" && (probe.SampledAtUtc is null || now - probe.SampledAtUtc > TimeSpan.FromMinutes(2)))
            probe = new("unavailable", "probe-sample-stale") { ObservedAtUtc = probe.ObservedAtUtc, SampledAtUtc = probe.SampledAtUtc };
        return Compact(new MachineStatus(machine, reporterState, counts.Single().Item1, counts.Single().Item2, leases.ToArray(), total) { Probe = probe },
            result => result.Probe.Volumes is { Length: > 0 } volumes
                ? result with { Probe = result.Probe with { Volumes = volumes[..^1], VolumesCapped = true } }
                : result with { Leases = result.Leases[..^1] },
            result => result.Leases.Length + (result.Probe.Volumes?.Length ?? 0));
    }

    public async Task<BudgetStatus> BudgetStatusAsync(CancellationToken token = default) {
        await using var db = await _database.OpenSessionAsync(path, token);
        const string groups = "FROM session_status WHERE limited=1 AND archived=0 GROUP BY machine,provider,account";
        var total = Convert.ToInt32(await db.ExecuteScalarAsync("SELECT COUNT(*) FROM (SELECT 1 " + groups + ")", cancellationToken: token));
        var rows = await db.QueryAsListAsync("SELECT machine,provider,account,COUNT(*) " + groups + " ORDER BY machine,provider,account LIMIT 8",
            r => new BudgetLimit(r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.GetInt32(3)), cancellationToken: token);
        return Compact(new BudgetStatus(rows.ToArray(), total), result => result with { Limits = result.Limits[..^1] }, result => result.Limits.Length);
    }

    private static T Compact<T>(T value, Func<T, T> trim, Func<T, int> count) {
        while (count(value) > 0 && Encoding.UTF8.GetByteCount(Json(value)) > 1000) value = trim(value);
        return value;
    }

    private static Dictionary<string, object?> LeaseParameters(Lease lease, DateTimeOffset now) => new() {
        ["key"] = lease.Key, ["id"] = lease.LeaseId, ["machine"] = lease.Machine,
        ["expires"] = lease.ExpiresAtUtc.ToString("O"), ["json"] = Json(lease), ["now"] = now.ToString("O")
    };
    private static async Task<Lease?> ReadLeaseAsync(SQLiteAsyncSession db, string key, CancellationToken token) =>
        await db.ExecuteScalarAsync("SELECT json FROM leases WHERE key=@key", new Dictionary<string, object?> { ["key"] = key }, token) is string json ? Read<Lease>(json) : null;
}
