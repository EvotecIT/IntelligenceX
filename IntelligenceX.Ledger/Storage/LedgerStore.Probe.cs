using System.Diagnostics;

namespace IntelligenceX.Ledger.Storage;

/// <summary>One step of service startup, with optional progress through a counted pass.</summary>
public sealed record StartupStep(string Phase, int? Done = null, int? Total = null);

/// <summary>How long the write path took to answer a health probe.</summary>
public sealed record StoreProbe(bool Healthy, string? Reason, long GateWaitMs, long? QueryMs);

public sealed partial class LedgerStore {
    /// <summary>
    /// Takes the write gate and reads one row, the same path every write takes, and answers within one budget measured
    /// from the call. A writer stuck behind a slow disk or a long maintenance pass shows up here as a gate that doesn't
    /// free up in time, even while plain reads still answer.
    /// </summary>
    public async Task<StoreProbe> ProbeAsync(TimeSpan budget, CancellationToken token = default) {
        var clock = Stopwatch.StartNew();
        if (!await _gate.WaitAsync(budget, token)) return new(false, "write-gate-busy", clock.ElapsedMilliseconds, null);
        var gateWait = clock.ElapsedMilliseconds;
        // SQLite statements run synchronously and can sit in disk I/O where cancellation doesn't reach them, so the read
        // runs on its own and releases the gate when it finishes; the probe answers by the deadline either way.
        var query = Task.Run(async () => {
            try {
                await using var db = await _database.OpenSessionAsync(path, CancellationToken.None);
                await db.ExecuteScalarAsync("SELECT reporter FROM heartbeats LIMIT 1", cancellationToken: CancellationToken.None);
            } finally { _gate.Release(); }
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
