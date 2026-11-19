using System.Diagnostics;

namespace IntelligenceX.Ledger.Storage;

/// <summary>One step of service startup, with optional progress through a counted pass.</summary>
public sealed record StartupStep(string Phase, int? Done = null, int? Total = null);

/// <summary>How long the write path took to answer a health probe.</summary>
public sealed record StoreProbe(bool Healthy, string? Reason, long GateWaitMs, long? QueryMs);

public sealed partial class LedgerStore {
    /// <summary>
    /// Takes the write gate and reads one row, the same path every write takes. A writer stuck behind a slow disk or a
    /// long maintenance pass shows up here as a gate that doesn't free up within the budget, even while plain reads
    /// still answer.
    /// </summary>
    public async Task<StoreProbe> ProbeAsync(TimeSpan budget, CancellationToken token = default) {
        var clock = Stopwatch.StartNew();
        if (!await _gate.WaitAsync(budget, token)) return new(false, "write-gate-busy", clock.ElapsedMilliseconds, null);
        var gateWait = clock.ElapsedMilliseconds;
        try {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
            limit.CancelAfter(budget);
            try {
                await using var db = await _database.OpenSessionAsync(path, limit.Token);
                await db.ExecuteScalarAsync("SELECT reporter FROM heartbeats LIMIT 1", cancellationToken: limit.Token);
            } catch (Exception error) when (limit.IsCancellationRequested && !token.IsCancellationRequested &&
                error is OperationCanceledException or DBAClientX.DbaClientXException) {
                return new(false, "database-slow", gateWait, clock.ElapsedMilliseconds - gateWait);
            }
            return new(true, null, gateWait, clock.ElapsedMilliseconds - gateWait);
        } finally { _gate.Release(); }
    }
}
