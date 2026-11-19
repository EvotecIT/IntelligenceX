using System.Diagnostics;

namespace IntelligenceX.Ledger.Storage;

/// <summary>One step of service startup, with optional progress through a counted pass.</summary>
public sealed record StartupStep(string Phase, int? Done = null, int? Total = null);

/// <summary>How long the write path took to answer a health probe.</summary>
public sealed record StoreProbe(bool Healthy, string? Reason, long GateWaitMs, long? QueryMs);

public sealed partial class LedgerStore {
    /// <summary>
    /// Takes the write gate, the path every write waits on, then reads one row, all within one budget measured from the
    /// call. A writer stuck behind a slow disk or a long maintenance pass shows up as a gate that doesn't free up in
    /// time, even while plain reads still answer; a stalled disk shows up as a read that doesn't finish.
    /// </summary>
    public async Task<StoreProbe> ProbeAsync(TimeSpan budget, CancellationToken token = default) {
        var clock = Stopwatch.StartNew();
        if (!await _gate.WaitAsync(budget, token)) return new(false, "write-gate-busy", clock.ElapsedMilliseconds, null);
        // Getting the gate is the writer check. The read needs no writer exclusion, so a stalled read never holds up ingest.
        _gate.Release();
        var gateWait = clock.ElapsedMilliseconds;
        // SQLite statements run synchronously and can sit in disk I/O where cancellation doesn't reach them, so the read
        // runs on its own and the probe answers by the deadline either way.
        var query = Task.Run(async () => {
            await using var db = await _database.OpenSessionAsync(path, CancellationToken.None);
            await db.ExecuteScalarAsync("SELECT reporter FROM heartbeats LIMIT 1", cancellationToken: CancellationToken.None);
        }, CancellationToken.None);
        var remaining = budget - clock.Elapsed;
        try {
            if (remaining <= TimeSpan.Zero) throw new TimeoutException();
            await query.WaitAsync(remaining, token);
        } catch (TimeoutException) {
            _ = query.ContinueWith(static finished => _ = finished.Exception, TaskContinuationOptions.OnlyOnFaulted);
            return new(false, "database-slow", gateWait, clock.ElapsedMilliseconds - gateWait);
        } catch (DBAClientX.DbaClientXException) {
            return new(false, "database-error", gateWait, clock.ElapsedMilliseconds - gateWait);
        }
        return new(true, null, gateWait, clock.ElapsedMilliseconds - gateWait);
    }
}
