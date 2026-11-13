using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Storage;
using IntelligenceX.Ledger.Reporting;

namespace IntelligenceX.Ledger.Hosting;

internal static class LeaseEndpoints {
    internal static void Map(WebApplication app, LedgerStore store, LedgerSettings settings) {
        const string api = "/api/ledger/v1";
        app.MapGet(api + "/leases", async (string key, bool? prefix, CancellationToken ct) =>
            Results.Ok(await store.WhoAsync(key, prefix ?? false, DateTimeOffset.UtcNow, ct)));
        app.MapPost(api + "/leases/acquire", async (LeaseAcquire value, CancellationToken ct) =>
            Results.Ok(await store.AcquireLeaseAsync(value, DateTimeOffset.UtcNow, ct)));
        app.MapPost(api + "/leases/renew", async (LeaseRenew value, CancellationToken ct) =>
            Results.Ok(await store.RenewLeaseAsync(value, DateTimeOffset.UtcNow, ct)));
        app.MapPost(api + "/leases/release", async (LeaseRelease value, CancellationToken ct) =>
            Results.Ok(await store.ReleaseLeaseAsync(value, DateTimeOffset.UtcNow, ct)));
        app.MapGet(api + "/machine-status/{id}", async (string id, CancellationToken ct) => {
            if (!settings.ExpectedReporters.TryGetValue(id, out var expected)) return Results.NotFound();
            var health = await store.HeartbeatsAsync(ct);
            var now = DateTimeOffset.UtcNow;
            var state = expected.All(health.ContainsKey) && expected.All(r => now - health[r] <= settings.Rules.ReporterTimeout)
                ? "healthy" : expected.Any(health.ContainsKey) ? "reporter-silent" : "not-enrolled";
            return Results.Ok(await store.MachineStatusAsync(id, state, now, ct));
        });
        app.MapGet(api + "/budget-status", async (CancellationToken ct) => Results.Ok(await store.BudgetStatusAsync(ct)));
        async Task<string[]> RepositoryCoverage(CancellationToken ct) {
            var reporting = await store.ReportedRepositoryMachinesAsync(ct);
            var health = await store.HeartbeatsAsync(ct);
            var now = DateTimeOffset.UtcNow;
            var coverage = settings.ExpectedReporters.Keys.Except(reporting, StringComparer.Ordinal)
                .Select(machine => "repository-metadata-unobserved:" + machine).ToList();
            foreach (var pair in settings.ExpectedReporters) {
                if (pair.Value.All(reporter => !health.ContainsKey(reporter))) coverage.Add("reporter-not-enrolled:" + pair.Key);
                else if (pair.Value.Any(reporter => !health.TryGetValue(reporter, out var at) || now - at > settings.Rules.ReporterTimeout))
                    coverage.Add("reporter-silent:" + pair.Key);
            }
            return coverage.ToArray();
        }
        app.MapGet(api + "/repo-activity", async (string repo, CancellationToken ct) => {
            var result = await store.RepositoryActivityAsync(repo, DateTimeOffset.UtcNow, ct);
            return Results.Ok(result with { Coverage = result.Coverage.Concat(await RepositoryCoverage(ct)).ToArray() });
        });
        app.MapGet(api + "/overlap", async (string repo, string? branch, string[]? files, string? text, CancellationToken ct) => {
            var activity = await store.RepositoryActivityAsync(repo, DateTimeOffset.UtcNow, ct);
            activity = activity with { Coverage = activity.Coverage.Concat(await RepositoryCoverage(ct)).ToArray() };
            return Results.Ok(RepositoryOverlapEvaluator.Evaluate(activity, branch, files, text));
        });
        app.MapPost(api + "/project-evidence", async (LedgerEvent value, CancellationToken ct) => {
            var now = DateTimeOffset.UtcNow;
            if (!EventAdmission.Valid(value, settings, now) || value.GlobalWork is null) return Results.BadRequest();
            await store.IngestAsync([value], now, ct, journal: false);
            return Results.Ok(new { ok = true });
        });
        app.MapPost(api + "/reporters/health", async (ReporterHealth value, CancellationToken ct) => {
            var now = DateTimeOffset.UtcNow;
            if (value is null || !settings.ExpectedReporters.TryGetValue(value.MachineId ?? "", out var reporters) ||
                !reporters.Contains(value.ReporterId) || !LedgerStore.LeaseKey(value.SourceVersion) ||
                value.ObservedAtUtc.Offset != TimeSpan.Zero || value.ObservedAtUtc > now.AddMinutes(2) ||
                value.Coverage is null || value.Coverage.Length > 32 || value.Coverage.Any(c => !LedgerStore.LeaseKey(c)) ||
                value.Progress?.Length > 128 || value.Progress?.Any(p => p is null || !LedgerStore.LeaseKey(p.ItemId) || !LedgerStore.LeaseKey(p.TurnId) ||
                    p.LastActivityAtUtc.Offset != TimeSpan.Zero || p.LastActivityAtUtc > value.ObservedAtUtc) == true)
                return Results.BadRequest(new { error = "invalid-health" });
            if (!ValidProbe(value.Probe, now)) value = value with { Probe = new("unavailable", "invalid-source-data") { ObservedAtUtc = now } };
            await store.UpdateReporterHealthAsync(value, ct);
            return Results.Ok(new { ok = true });
        });
    }

    private static bool ValidProbe(ProbeAvailability? probe, DateTimeOffset now) => probe is null ||
        probe.State is "available" or "unavailable" or "paused" &&
        (probe.Reason is null || probe.Reason.Length <= 128 && LedgerStore.LeaseKey(probe.Reason)) &&
        (probe.CpuUse is null || double.IsFinite(probe.CpuUse.Value) && probe.CpuUse is >= 0 and <= 1) &&
        probe.MemoryPressure?.Length is not > 64 && probe.MemoryUsedBytes is not < 0 && probe.MemoryTotalBytes is not < 0 && probe.SwapUsedBytes is not < 0 &&
        (probe.LoadAverage is null || probe.LoadAverage.Length <= 3 && probe.LoadAverage.All(n => double.IsFinite(n) && n >= 0)) &&
        (probe.Volumes is null || probe.Volumes.Length <= 8 && probe.Volumes.All(v => v is not null && v.Mount is { Length: <= 256 } && v.FreeBytes >= 0 && v.TotalBytes >= 0)) &&
        (probe.ObservedAtUtc is null || probe.ObservedAtUtc <= now.AddMinutes(2)) &&
        (probe.SampledAtUtc is null || probe.SampledAtUtc <= now.AddMinutes(2));
}
