using DBAClientX;
using IntelligenceX.Ledger.Contracts;

namespace IntelligenceX.Ledger.Storage;

public sealed partial class LedgerStore {
    private sealed record Reservation(Lease Lease, string State, int TtlSeconds, long Order);

    private static async Task InitializeReservationsAsync(SQLiteAsyncSession db, CancellationToken token) {
        await db.RunInTransactionAsync(async (tx, ct) => {
            await tx.ExecuteNonQueryAsync("""
                CREATE TABLE IF NOT EXISTS reservations(ordinal INTEGER PRIMARY KEY AUTOINCREMENT,
                  id TEXT NOT NULL UNIQUE,key TEXT NOT NULL,holder TEXT NOT NULL,machine TEXT NOT NULL,
                  state TEXT NOT NULL,expires TEXT NOT NULL,ttl INTEGER NOT NULL,json TEXT NOT NULL);
                CREATE INDEX IF NOT EXISTS reservations_key ON reservations(key,state,expires);
                CREATE INDEX IF NOT EXISTS reservations_machine ON reservations(machine,state,expires);
                CREATE TABLE IF NOT EXISTS lease_history(id TEXT PRIMARY KEY,key TEXT NOT NULL,
                  machine TEXT NOT NULL,started TEXT NOT NULL,ended TEXT NOT NULL,json TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS machine_load(machine TEXT PRIMARY KEY,json TEXT NOT NULL);
                """, cancellationToken: ct);
            var legacy = await tx.QueryAsListAsync("SELECT json FROM leases", row => Read<Lease>(row.GetString(0)), cancellationToken: ct);
            var observed = DateTimeOffset.UtcNow;
            foreach (var lease in legacy) await SaveReservationAsync(tx, lease with { BudgetObservedAtUtc = observed }, "active", 0, ct);
            await tx.ExecuteNonQueryAsync("DELETE FROM leases", cancellationToken: ct);
        }, token);
    }

    private static string KeyClass(string key) => key.Split(':', 2)[0].ToLowerInvariant();
    private static double MaximumTtlSeconds(string key, CoordinationPolicy policy) {
        var maximum = policy.Lease.MaximumTtlMinutes.GetValueOrDefault(KeyClass(key), policy.Lease.MaximumTtlMinutes["other"]) * 60;
        if (KeyClass(key) == "bench") maximum = Math.Min(maximum, Math.Min(policy.Bench.MaxContinuousMinutes * 60, policy.Bench.MaxPerDayHours * 3600));
        return maximum;
    }

    private static int DefaultLeaseTtl(string key, CoordinationPolicy policy) =>
        (int)Math.Min(policy.Lease.DefaultTtlSeconds, MaximumTtlSeconds(key, policy));
    private static void ValidateTtl(string key, int seconds, CoordinationPolicy policy) {
        if (seconds <= 0 || seconds > MaximumTtlSeconds(key, policy))
            throw new ArgumentException("Lease TTL exceeds policy.");
    }

    private static async Task<List<Reservation>> ReservationsAsync(SQLiteAsyncSession db, DateTimeOffset now, CancellationToken token) =>
        (await db.QueryAsListAsync("SELECT json,state,ttl,ordinal FROM reservations WHERE expires>@now ORDER BY ordinal",
            row => new Reservation(Read<Lease>(row.GetString(0)), row.GetString(1), row.GetInt32(2), row.GetInt64(3)),
            new Dictionary<string, object?> { ["now"] = now.ToString("O") }, cancellationToken: token)).ToList();

    private static async Task SaveReservationAsync(SQLiteAsyncSession db, Lease lease, string state, int ttl, CancellationToken token) {
        await db.ExecuteNonQueryAsync("""
            INSERT INTO reservations(id,key,holder,machine,state,expires,ttl,json)
            VALUES(@id,@key,@holder,@machine,@state,@expires,@ttl,@json)
            ON CONFLICT(id) DO UPDATE SET state=excluded.state,expires=excluded.expires,ttl=excluded.ttl,json=excluded.json
            """, new Dictionary<string, object?> { ["id"] = lease.LeaseId, ["key"] = lease.Key, ["holder"] = lease.Holder,
            ["machine"] = lease.Machine, ["state"] = state, ["expires"] = lease.ExpiresAtUtc.ToString("O"), ["ttl"] = ttl, ["json"] = Json(lease) }, token);
        if (state == "active") await RememberLeaseAsync(db, lease, lease.ExpiresAtUtc, token);
    }

    private static Task<int> RememberLeaseAsync(SQLiteAsyncSession db, Lease lease, DateTimeOffset ended, CancellationToken token) =>
        db.ExecuteNonQueryAsync("""
            INSERT INTO lease_history(id,key,machine,started,ended,json) VALUES(@id,@key,@machine,@started,@ended,@json)
            ON CONFLICT(id) DO UPDATE SET ended=excluded.ended,json=excluded.json
            """, new Dictionary<string, object?> { ["id"] = lease.LeaseId, ["key"] = lease.Key, ["machine"] = lease.Machine,
            ["started"] = (lease.AcquiredAtUtc ?? lease.BudgetObservedAtUtc ?? ended).ToString("O"), ["ended"] = ended.ToString("O"), ["json"] = Json(lease) }, token);

    private static async Task<SessionFact?> HolderSessionAsync(SQLiteAsyncSession db, Lease lease, CancellationToken token) {
        var parts = lease.Holder.Split(':');
        var qualified = parts.Length is 2 or 3 && parts[0] is "codex" or "claude" &&
            Guid.TryParse(parts[1], out _) && (parts.Length == 2 || parts[2] == "owner-driven");
        var found = await db.ExecuteScalarAsync("""
            SELECT i.json FROM session_lookup s JOIN items i ON s.item=i.id
            WHERE s.item=@holder OR (s.native=@native AND (@provider IS NULL OR s.provider=@provider))
            ORDER BY json_extract(i.json,'$.session.lastActivityAtUtc') DESC LIMIT 1
            """, new Dictionary<string, object?> { ["holder"] = lease.Holder,
                ["native"] = qualified ? Guid.Parse(parts[1]).ToString() : lease.Holder,
                ["provider"] = qualified ? parts[0] : null }, token);
        return found is string json ? Read<LedgerItem>(json).Session : null;
    }

    private static async Task<(DateTimeOffset? Activity, DateTimeOffset? Takeover)> TakeoverAsync(
        SQLiteAsyncSession db, Lease lease, CoordinationPolicy policy, CancellationToken token) {
        var session = await HolderSessionAsync(db, lease, token);
        var activity = session?.LastActivityAtUtc;
        var minutes = session?.Origin == "agent" ? policy.Lease.TakeoverIdleMinutes : policy.Lease.TakeoverIdleMinutesHuman;
        return (activity, activity?.AddMinutes(minutes));
    }

    private static int Capacity(Lease lease, CoordinationPolicy policy) {
        var kind = KeyClass(lease.Key);
        var machine = policy.Machines.FirstOrDefault(m => m.Id == lease.Machine);
        if (kind == "build" && lease.Key == "build:" + machine?.Id) return machine!.BuildSlots;
        if (kind == "test" && lease.Key == "test:" + machine?.Id) return machine!.TestSlots ?? machine.BuildSlots;
        return 1;
    }

    private static string? Availability(Lease lease, IEnumerable<Reservation> all, CoordinationPolicy policy) {
        var active = all.Where(r => r.State == "active").Select(r => r.Lease).ToArray();
        var kind = KeyClass(lease.Key);
        var heavy = kind is "bench" or "build" or "test";
        var local = active.Where(value => value.Machine == lease.Machine).ToArray();
        var capacity = policy.Machines.FirstOrDefault(value => value.Id == lease.Machine);
        if (capacity is not null && kind == "build" && local.Count(value => KeyClass(value.Key) == "build") >= capacity.BuildSlots) return "slots-full";
        if (capacity is not null && kind == "test" && local.Count(value => KeyClass(value.Key) == "test") >= (capacity.TestSlots ?? capacity.BuildSlots)) return "slots-full";
        if (kind == "bench" && local.Any(value => KeyClass(value.Key) is "bench" or "build" or "test")) return "heavy-work-active";
        if (heavy && kind != "bench" && local.Any(value => KeyClass(value.Key) == "bench") &&
            local.Count(value => KeyClass(value.Key) is "build" or "test") >= policy.Bench.HeavySlotsWhileActive) return "benchmark-active";
        return active.Count(value => value.Key == lease.Key) >= Capacity(lease, policy) ? "slots-full" : null;
    }

    private static async Task<string?> AdmissionReasonAsync(SQLiteAsyncSession db, Lease lease,
        List<Reservation> all, CoordinationPolicy policy, DateTimeOffset now, CancellationToken token) {
        var reason = Availability(lease, all, policy);
        if (reason is not null || KeyClass(lease.Key) is not ("bench" or "build" or "test")) return reason;
        var health = await db.QueryAsListAsync("SELECT json FROM reporter_health WHERE machine=@machine ORDER BY observed DESC",
            row => Read<ReporterHealth>(row.GetString(0)), new Dictionary<string, object?> { ["machine"] = lease.Machine }, cancellationToken: token);
        var probe = health.FirstOrDefault(value => value.Probe is not null)?.Probe ?? new ProbeAvailability("unavailable");
        var status = await MachineResourcesAsync(db, lease.Machine, probe, policy, now, token);
        return status.Reason is "sustained-cpu" or "memory-pressure" or "machine-disabled" ? status.Reason : null;
    }

    private static async Task<string?> BenchmarkBudgetReasonAsync(SQLiteAsyncSession db, Lease lease, int ttl,
        CoordinationPolicy policy, DateTimeOffset now, CancellationToken token) {
        if (KeyClass(lease.Key) != "bench") return null;
        var history = await db.QueryAsListAsync("""
            SELECT started,ended,json FROM lease_history
            WHERE machine=@machine AND substr(key,1,6)='bench:' COLLATE NOCASE AND ended>@cutoff ORDER BY ended DESC
            """, row => (Start: DateTimeOffset.Parse(row.GetString(0)), End: DateTimeOffset.Parse(row.GetString(1)), Lease: Read<Lease>(row.GetString(2))),
            new Dictionary<string, object?> { ["machine"] = lease.Machine, ["cutoff"] = now.AddDays(-1).ToString("O") }, cancellationToken: token);
        if (history.Any(row => row.Lease.AcquiredAtUtc is null)) return "benchmark-history-unqualified";
        var cutoff = now.AddDays(-1);
        var used = history.Sum(row => Math.Max(0, ((row.End < now ? row.End : now) - (row.Start > cutoff ? row.Start : cutoff)).TotalSeconds));
        var continuousStart = now;
        foreach (var row in history) {
            if (continuousStart - row.End > TimeSpan.FromMinutes(policy.Bench.ContinuityGapMinutes)) break;
            if (row.Start < continuousStart) continuousStart = row.Start;
        }
        return (now - continuousStart).TotalSeconds + ttl <= policy.Bench.MaxContinuousMinutes * 60 &&
            used + ttl <= policy.Bench.MaxPerDayHours * 3600 ? null : "benchmark-budget";
    }

    private static bool Conflicts(Lease first, Lease second) => first.Key == second.Key || first.Machine == second.Machine &&
        (KeyClass(first.Key) == KeyClass(second.Key) && KeyClass(first.Key) is "build" or "test" ||
         KeyClass(first.Key) == "bench" && KeyClass(second.Key) is "bench" or "build" or "test" ||
         KeyClass(second.Key) == "bench" && KeyClass(first.Key) is "build" or "test");

    private static int Priority(Lease lease, CoordinationPolicy policy) => policy.Priority.GetValueOrDefault(
        KeyClass(lease.Key) == "bench" ? "benchmark" : KeyClass(lease.Key), "normal") switch { "high" => 0, "low" => 2, _ => 1 };

    private static IEnumerable<Reservation> OrderedQueue(List<Reservation> all, CoordinationPolicy policy) =>
        all.Where(row => row.State == "queued").OrderBy(row => Priority(row.Lease, policy)).ThenBy(row => row.Order);

    private static int QueuedTtl(Reservation row, CoordinationPolicy policy) =>
        Math.Max(1, (int)Math.Min(row.TtlSeconds, MaximumTtlSeconds(row.Lease.Key, policy)));

    private static async Task<bool> EarlierWaiterAsync(SQLiteAsyncSession db, Lease lease, List<Reservation> all,
        CoordinationPolicy policy, DateTimeOffset now, CancellationToken token) {
        foreach (var row in OrderedQueue(all, policy))
            if (Priority(row.Lease, policy) <= Priority(lease, policy) && Conflicts(row.Lease, lease) &&
                await BenchmarkBudgetReasonAsync(db, row.Lease, QueuedTtl(row, policy), policy, now, token) is null) return true;
        return false;
    }

    private static async Task ScheduleReservationsAsync(SQLiteAsyncSession db, CoordinationPolicy policy, DateTimeOffset now, CancellationToken token) {
        await db.ExecuteNonQueryAsync("DELETE FROM reservations WHERE expires<=@now", new Dictionary<string, object?> { ["now"] = now.ToString("O") }, token);
        var all = await ReservationsAsync(db, now, token);
        var blocked = new List<Lease>();
        foreach (var waiting in OrderedQueue(all, policy).ToArray()) {
            var ttl = QueuedTtl(waiting, policy);
            if (await BenchmarkBudgetReasonAsync(db, waiting.Lease, ttl, policy, now, token) is not null) continue;
            if (blocked.Any(lease => Conflicts(lease, waiting.Lease)) ||
                await AdmissionReasonAsync(db, waiting.Lease, all, policy, now, token) is not null) {
                blocked.Add(waiting.Lease);
                continue;
            }
            var granted = waiting.Lease with { AcquiredAtUtc = now, ExpiresAtUtc = now.AddSeconds(ttl), TtlAdjustedToPolicy = ttl != waiting.TtlSeconds };
            await SaveReservationAsync(db, granted, "active", ttl, token);
            all[all.IndexOf(waiting)] = waiting with { Lease = granted, State = "active", TtlSeconds = ttl };
        }
    }

    public async Task MaintainLeasesAsync(DateTimeOffset now, CancellationToken token = default) {
        var policy = await ReadPolicyAsync(token);
        await _leaseGate.WaitAsync(token);
        try {
            await using var db = await _database.OpenSessionAsync(path, token);
            await db.RunInTransactionAsync((tx, ct) => ScheduleReservationsAsync(tx, policy, now, ct), token);
        } finally { _leaseGate.Release(); }
    }
}
