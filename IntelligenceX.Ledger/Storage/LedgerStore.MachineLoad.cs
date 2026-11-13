using DBAClientX;
using IntelligenceX.Ledger.Contracts;

namespace IntelligenceX.Ledger.Storage;

public sealed partial class LedgerStore {
    private sealed record LoadWindow(long Revision, DateTimeOffset Observed, DateTimeOffset? HeavySince, DateTimeOffset? AlertSince);

    private static async Task UpdateLoadAsync(SQLiteAsyncSession db, ReporterHealth health, CoordinationPolicy policy, CancellationToken token) {
        var probe = health.Probe;
        if (probe?.State != "available" || probe.CpuUse is null || probe.SampledAtUtc is not { } sampled ||
            health.ObservedAtUtc - sampled > TimeSpan.FromMinutes(policy.Load.SampleFreshMinutes)) return;
        var previous = await ReadLoadAsync(db, health.MachineId, token);
        if (previous is not null && sampled <= previous.Observed) return;
        var continuous = previous?.Revision == policy.Revision && sampled - previous.Observed <= TimeSpan.FromMinutes(policy.Load.SampleFreshMinutes);
        var heavy = probe.CpuUse * 100 > policy.Load.HeavyCpuPercent;
        var alert = probe.CpuUse * 100 > policy.Load.AlertCpuPercent;
        DateTimeOffset? heavySince = null, alertSince = null;
        if (heavy) heavySince = continuous ? previous?.HeavySince ?? sampled : sampled;
        if (alert) alertSince = continuous ? previous?.AlertSince ?? sampled : sampled;
        var window = new LoadWindow(policy.Revision, sampled, heavySince, alertSince);
        await db.ExecuteNonQueryAsync("INSERT INTO machine_load(machine,json) VALUES(@machine,@json) ON CONFLICT(machine) DO UPDATE SET json=excluded.json",
            new Dictionary<string, object?> { ["machine"] = health.MachineId, ["json"] = Json(window) }, token);
    }

    private static async Task<LoadWindow?> ReadLoadAsync(SQLiteAsyncSession db, string machine, CancellationToken token) =>
        await db.ExecuteScalarAsync("SELECT json FROM machine_load WHERE machine=@machine", new Dictionary<string, object?> { ["machine"] = machine }, token)
            is string json ? Read<LoadWindow>(json) : null;

    private static string? FreshAdvice(SessionFact session, CoordinationPolicy policy, DateTimeOffset now) =>
        session.Origin == "agent" && (session.TurnCount >= policy.Session.FreshAfterTurns ||
            session.CreatedAtUtc <= now.AddDays(-policy.Session.FreshAfterDays)) ? "fresh-session-recommended" : null;

    private static async Task<MachineResourceStatus> MachineResourcesAsync(SQLiteAsyncSession db, string machine,
        ProbeAvailability probe, CoordinationPolicy policy, DateTimeOffset now, CancellationToken token) {
        var capacity = policy.Machines.FirstOrDefault(value => value.Id == machine);
        var agents = Convert.ToInt32(await db.ExecuteScalarAsync("""
            SELECT COUNT(*) FROM session_status s JOIN items i ON i.id=s.item
            WHERE s.machine=@machine AND s.open=1 AND s.archived=0
              AND json_extract(i.json,'$.session.origin')='agent'
              AND json_extract(i.json,'$.session.lastActivityAtUtc')>=@cutoff
            """, new Dictionary<string, object?> { ["machine"] = machine,
                ["cutoff"] = now.AddMinutes(-policy.Lease.LiveWithinMinutes).ToString("O") }, token));
        var rows = (await ReservationsAsync(db, now, token)).Where(row => row.Lease.Machine == machine).ToArray();
        var builds = rows.Count(row => row.State == "active" && KeyClass(row.Lease.Key) == "build");
        var tests = rows.Count(row => row.State == "active" && KeyClass(row.Lease.Key) == "test");
        var queued = rows.Count(row => row.State == "queued");
        var window = await ReadLoadAsync(db, machine, token);
        var sampleFresh = probe.State == "available" && probe.SampledAtUtc is { } sampled &&
            now - sampled <= TimeSpan.FromMinutes(policy.Load.SampleFreshMinutes);
        var fresh = sampleFresh && window?.Revision == policy.Revision &&
            now - window.Observed <= TimeSpan.FromMinutes(policy.Load.SampleFreshMinutes);
        var sustained = fresh && now - window!.AlertSince >= TimeSpan.FromMinutes(policy.Load.AlertMinutes);
        var reason = capacity is null ? "machine-capacity-unconfigured" : !capacity.Enabled ? "machine-disabled" :
            builds >= capacity.BuildSlots && tests >= (capacity.TestSlots ?? capacity.BuildSlots) ? "slots-full" :
            rows.Any(row => row.State == "active" && KeyClass(row.Lease.Key) == "bench") && builds + tests >= policy.Bench.HeavySlotsWhileActive ? "benchmark-active" :
            sampleFresh && probe.MemoryPressure is { } pressure && policy.Load.HeavyMemoryPressure.Contains(pressure, StringComparer.OrdinalIgnoreCase) ? "memory-pressure" :
            fresh && now - window!.HeavySince >= TimeSpan.FromMinutes(policy.Load.HeavySustainedMinutes) ? "sustained-cpu" : "capacity-available";
        return new(capacity?.BuildSlots ?? 0, capacity?.TestSlots ?? capacity?.BuildSlots ?? 0, builds, tests, queued,
            reason == "capacity-available" ? "yes" : "no", reason, sustained) {
            AgentSlots = capacity?.AgentSlots ?? 0, LiveAgents = agents
        };
    }
}
