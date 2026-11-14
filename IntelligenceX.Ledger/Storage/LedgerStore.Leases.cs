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
    public async Task<LeaseReply> AcquireLeaseAsync(LeaseAcquire command, DateTimeOffset now, CancellationToken token = default) {
        if (!LeaseKey(command.Key) || !Label(command.Holder, 128) || !Label(command.Machine, 64) || !Label(command.Purpose, 96))
            throw new ArgumentException("Invalid lease fields.");
        ValidateHandoff(command);
        var policy = await ReadPolicyAsync(token);
        if (command.TtlSeconds == 0) command = command with { TtlSeconds = DefaultLeaseTtl(command.Key, policy) };
        ValidateTtl(command.Key, command.TtlSeconds, policy);
        await _leaseGate.WaitAsync(token);
        try {
            await using var db = await _database.OpenSessionAsync(path, token);
            return await db.RunInTransactionAsync(async (tx, ct) => {
                await ScheduleReservationsAsync(tx, policy, now, ct);
                var all = await ReservationsAsync(tx, now, ct);
                var existing = all.FirstOrDefault(r => r.Lease.Key == command.Key && r.Lease.Holder == command.Holder && r.Lease.Machine == command.Machine);
                if (existing is not null) {
                    if (command.TransferLeaseId is not null && (existing.Lease.PreviousLeaseId != command.TransferLeaseId || Json(existing.Lease.Handoff) != Json(command.Handoff)))
                        return new(false, "lease-conflict", existing.Lease);
                    return ReservationReply(existing, all, policy, now);
                }
                var lease = new Lease(command.Key, command.Holder, command.Machine, command.Purpose, Guid.NewGuid().ToString("N"), now.AddSeconds(command.TtlSeconds));
                if (Encoding.UTF8.GetByteCount(Json(lease)) > 900) throw new ArgumentException("Lease metadata exceeds the compact response bound.");
                if (KeyClass(lease.Key) == "branch" && all.FirstOrDefault(r => r.Lease.Key == lease.Key && r.State == "active") is { } previous) {
                    var holder = await TakeoverAsync(tx, previous.Lease, policy, ct);
                    if (command.TransferLeaseId is not null && previous.Lease.LeaseId != command.TransferLeaseId) return new(false, "not-held");
                    if (command.Handoff is not null && !await PushedCheckpointAsync(tx, previous.Lease, command.Handoff, ct))
                        return new(false, "checkpoint-unverified", previous.Lease);
                    if (command.TransferLeaseId is null && (holder.Takeover is null || holder.Takeover > now))
                        return new LeaseReply(false, "busy", previous.Lease) { HolderLastActivityAtUtc = holder.Activity, TakeoverAtUtc = holder.Takeover };
                    await RememberLeaseAsync(tx, previous.Lease, now, ct);
                    await tx.ExecuteNonQueryAsync("DELETE FROM reservations WHERE id=@id", new Dictionary<string, object?> { ["id"] = previous.Lease.LeaseId }, ct);
                    all.Remove(previous);
                    lease = lease with { PreviousHolder = previous.Lease.Holder, TakenOverAtUtc = now,
                        PreviousLeaseId = previous.Lease.LeaseId, Handoff = command.Handoff };
                }
                else if (command.TransferLeaseId is not null) return new(false, "not-held");
                var reason = await AdmissionReasonAsync(tx, lease, all, policy, now, ct);
                if (reason is null && await EarlierWaiterAsync(tx, lease, all, policy, now, ct)) reason = "earlier-waiter";
                reason = await BenchmarkBudgetReasonAsync(tx, lease, command.TtlSeconds, policy, now, ct) ?? reason;
                if (reason is not null && KeyClass(lease.Key) is not ("build" or "test" or "bench"))
                    return new(false, "busy", all.FirstOrDefault(r => r.Lease.Key == lease.Key)?.Lease);
                var queued = reason is not null;
                if (queued && all.Count(r => r.State == "queued") >= policy.MaximumQueued) return new(false, "queue-full");
                lease = lease with { AcquiredAtUtc = queued ? null : now,
                    ExpiresAtUtc = queued ? now.AddMinutes(policy.Lease.QueueTtlMinutes) : lease.ExpiresAtUtc };
                await SaveReservationAsync(tx, lease, queued ? "queued" : "active", command.TtlSeconds, ct);
                all = await ReservationsAsync(tx, now, ct);
                return ReservationReply(all.Single(r => r.Lease.LeaseId == lease.LeaseId), all, policy, now) with { Reason = reason };
            }, cancellationToken: token);
        } finally { _leaseGate.Release(); }
    }

    private static LeaseReply ReservationReply(Reservation row, List<Reservation> all, CoordinationPolicy policy, DateTimeOffset now) {
        if (row.State == "active") return new(true, "acquired", row.Lease) { Reason = row.Lease.TtlAdjustedToPolicy ? "ttl-adjusted-to-policy" : null };
        var ahead = OrderedQueue(all, policy).TakeWhile(value => value.Lease.LeaseId != row.Lease.LeaseId)
            .Count(value => Conflicts(value.Lease, row.Lease)) + 1;
        var deadlines = all.Where(r => r.State == "active" && Conflicts(r.Lease, row.Lease)).Select(r => r.Lease.ExpiresAtUtc).ToArray();
        DateTimeOffset? deadline = deadlines.Length == 0 ? null : KeyClass(row.Lease.Key) == "bench" ? deadlines.Max() : deadlines.Min();
        return new(false, "queued", row.Lease) { QueuePosition = ahead,
            EstimatedWaitSeconds = deadline is null ? null : (int)Math.Ceiling(Math.Max(0, (deadline.Value - now).TotalSeconds)) };
    }

    public async Task<LeaseReply> RenewLeaseAsync(LeaseRenew command, DateTimeOffset now, CancellationToken token = default) {
        if (!LeaseKey(command.Key) || !Guid.TryParseExact(command.LeaseId, "N", out _)) throw new ArgumentException("Invalid lease renewal.");
        var policy = await ReadPolicyAsync(token);
        if (command.TtlSeconds == 0) command = command with { TtlSeconds = DefaultLeaseTtl(command.Key, policy) };
        ValidateTtl(command.Key, command.TtlSeconds, policy);
        await _leaseGate.WaitAsync(token);
        try {
            await using var db = await _database.OpenSessionAsync(path, token);
            return await db.RunInTransactionAsync(async (tx, ct) => {
                await ScheduleReservationsAsync(tx, policy, now, ct);
                var row = (await ReservationsAsync(tx, now, ct)).FirstOrDefault(r => r.Lease.Key == command.Key && r.Lease.LeaseId == command.LeaseId && r.State == "active");
                var lease = row?.Lease;
                if (lease is null || lease.ExpiresAtUtc <= now || lease.LeaseId != command.LeaseId) return new LeaseReply(false, "not-held");
                var session = await HolderSessionAsync(tx, lease, ct);
                if (session is null || session.Archived == true || session.LastActivityAtUtc is not { } activity || now - activity > TimeSpan.FromMinutes(policy.Lease.LiveWithinMinutes))
                    return new LeaseReply(false, "holder-not-live", lease);
                if (KeyClass(lease.Key) == "bench") {
                    if (lease.AcquiredAtUtc is null) {
                        if (now.AddSeconds(command.TtlSeconds) > lease.ExpiresAtUtc) return new(false, "lease-start-unobserved", lease);
                    } else if (await BenchmarkBudgetReasonAsync(tx, lease, command.TtlSeconds, policy, now, ct) is { } reason)
                        return new(false, reason, lease);
                }
                lease = lease with { ExpiresAtUtc = now.AddSeconds(command.TtlSeconds) };
                await SaveReservationAsync(tx, lease, "active", command.TtlSeconds, ct);
                return new LeaseReply(true, "renewed", lease);
            }, cancellationToken: token);
        } finally { _leaseGate.Release(); }
    }

    public async Task<LeaseReply> ReleaseLeaseAsync(LeaseRelease command, DateTimeOffset now, CancellationToken token = default) {
        if (!LeaseKey(command.Key) || !Guid.TryParseExact(command.LeaseId, "N", out _)) throw new ArgumentException("Invalid lease release.");
        var policy = await ReadPolicyAsync(token);
        await _leaseGate.WaitAsync(token);
        try {
            await using var db = await _database.OpenSessionAsync(path, token);
            return await db.RunInTransactionAsync(async (tx, ct) => {
                var row = (await ReservationsAsync(tx, now, ct)).FirstOrDefault(r => r.Lease.Key == command.Key && r.Lease.LeaseId == command.LeaseId);
                if (row is null) return new LeaseReply(false, "not-held");
                if (row.State == "active") await RememberLeaseAsync(tx, row.Lease, now, ct);
                await tx.ExecuteNonQueryAsync("DELETE FROM reservations WHERE id=@id", new Dictionary<string, object?> { ["id"] = command.LeaseId }, ct);
                await ScheduleReservationsAsync(tx, policy, now, ct);
                return new LeaseReply(true, "released");
            }, token);
        } finally { _leaseGate.Release(); }
    }

    public async Task<LeaseQuery> WhoAsync(string key, bool prefix, DateTimeOffset now, CancellationToken token = default) {
        if (!LeaseKey(key)) throw new ArgumentException("Invalid lease key.");
        await using var db = await _database.OpenSessionAsync(path, token);
        // substr avoids treating '%' or '_' in a key as SQL wildcard syntax.
        var predicate = prefix ? "substr(key,1,length(@key))=@key" : "key=@key";
        var args = new Dictionary<string, object?> { ["key"] = key, ["now"] = now.ToString("O") };
        var total = Convert.ToInt32(await db.ExecuteScalarAsync("SELECT COUNT(*) FROM reservations WHERE state='active' AND expires>@now AND " + predicate, args, token));
        var rows = await db.QueryAsListAsync("SELECT json FROM reservations WHERE state='active' AND expires>@now AND " + predicate + " ORDER BY key,ordinal LIMIT 4",
            r => Read<Lease>(r.GetString(0)), args, cancellationToken: token);
        return Compact(new LeaseQuery(key, rows.ToArray(), total), result => result with { Leases = result.Leases[..^1] }, result => result.Leases.Length);
    }

    public async Task<MachineStatus> MachineStatusAsync(string machine, string reporterState, DateTimeOffset now, CancellationToken token = default) {
        if (!Label(machine, 64)) throw new ArgumentException("Invalid machine ID.");
        var policy = await ReadPolicyAsync(token);
        await using var db = await _database.OpenSessionAsync(path, token);
        var args = new Dictionary<string, object?> { ["machine"] = machine, ["now"] = now.ToString("O") };
        args["liveCutoff"] = now.AddMinutes(-policy.Lease.LiveWithinMinutes).ToString("O");
        var counts = await db.QueryAsListAsync("""
            SELECT COALESCE(SUM(s.open=1 AND json_extract(i.json,'$.session.lastActivityAtUtc')>=@liveCutoff),0),
              COALESCE(SUM(s.limited=1),0) FROM session_status s JOIN items i ON i.id=s.item WHERE s.machine=@machine AND s.archived=0
            """, r => (r.GetInt32(0), r.GetInt32(1)), args, cancellationToken: token);
        var total = Convert.ToInt32(await db.ExecuteScalarAsync("SELECT COUNT(*) FROM reservations WHERE state='active' AND machine=@machine AND expires>@now", args, token));
        var leases = await db.QueryAsListAsync("SELECT json FROM reservations WHERE state='active' AND machine=@machine AND expires>@now ORDER BY key,ordinal LIMIT 4",
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
        var sessions = await db.QueryAsListAsync("""
            SELECT i.json FROM session_status s JOIN items i ON i.id=s.item
            WHERE s.machine=@machine AND s.open=1 AND s.archived=0 AND json_extract(i.json,'$.session.lastActivityAtUtc')>=@liveCutoff
            ORDER BY json_extract(i.json,'$.session.lastActivityAtUtc') DESC LIMIT 4
            """, row => Read<LedgerItem>(row.GetString(0)), args, cancellationToken: token);
        var compact = sessions.Select(item => new MachineSession(item.Session!.SourceSessionId?.ToString() ?? item.Id,
            item.Provider, item.Session.Title ?? item.Session.Repository?.Title, item.Session.Origin, FreshAdvice(item.Session, policy, now))).ToArray();
        return Compact(new MachineStatus(machine, reporterState, counts.Single().Item1, counts.Single().Item2, leases.ToArray(), total) {
                Probe = probe, Sessions = compact, Resources = await MachineResourcesAsync(db, machine, probe, policy, now, token)
            },
            result => result.Probe.Volumes is { Length: > 0 } volumes
                ? result with { Probe = result.Probe with { Volumes = volumes[..^1], VolumesCapped = true } }
                : result.Sessions.Length > 0 ? result with { Sessions = result.Sessions[..^1] }
                : result with { Leases = result.Leases[..^1] },
            result => result.Leases.Length + result.Sessions.Length + (result.Probe.Volumes?.Length ?? 0));
    }

    public async Task<BudgetStatus> BudgetStatusAsync(CancellationToken token = default) {
        var policy = await ReadPolicyAsync(token);
        await using var db = await _database.OpenSessionAsync(path, token);
        const string groups = "FROM session_status WHERE limited=1 AND archived=0 GROUP BY machine,provider,account";
        var total = Convert.ToInt32(await db.ExecuteScalarAsync("SELECT COUNT(*) FROM (SELECT 1 " + groups + ")", cancellationToken: token));
        var rows = await db.QueryAsListAsync("SELECT machine,provider,account,COUNT(*) " + groups + " ORDER BY machine,provider,account LIMIT 8",
            r => new BudgetLimit(r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.GetInt32(3)), cancellationToken: token);
        var limitedProviders = await db.QueryAsListAsync("SELECT DISTINCT provider FROM session_status WHERE limited=1 AND archived=0",
            row => row.GetString(0), cancellationToken: token);
        var suggestions = policy.Routing.Where(pair => limitedProviders.Contains(pair.Value[0]))
            .ToDictionary(pair => pair.Key, pair => pair.Value.Skip(1).FirstOrDefault(), StringComparer.Ordinal);
        return Compact(new BudgetStatus(rows.ToArray(), total, "observed-session-limits;credits-and-reset-unknown;routing-suggestions:limited-session-only") {
            RoutingSuggestions = suggestions
        }, result => result with { Limits = result.Limits[..^1] }, result => result.Limits.Length);
    }

    private static T Compact<T>(T value, Func<T, T> trim, Func<T, int> count) {
        while (count(value) > 0 && Encoding.UTF8.GetByteCount(Json(value)) > 1000) value = trim(value);
        return value;
    }

}
