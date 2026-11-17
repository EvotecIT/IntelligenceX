using System.Text.Json;
using System.Security.Cryptography;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Storage;

namespace IntelligenceX.Ledger.Hosting;

/// <summary>Reevaluates aging rules and writes daily shadow reports locally without waking any client.</summary>
internal sealed class ShadowReportWorker(LedgerStore store, LedgerSettings settings, TimeProvider? timeProvider = null) : BackgroundService {
    /// <summary>
    /// Every pass re-evaluates every item and rereads two days of report; readers rebuild what they need themselves,
    /// so this only has to keep aging rules and the daily file current, which five minutes does.
    /// </summary>
    internal static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    /// <summary>Queued lease waiters are promoted when a holder's lease lapses; reads don't do that, so it runs every minute.</summary>
    internal static readonly TimeSpan LeaseInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        var clock = timeProvider ?? TimeProvider.System;
        // Either loop failing ends the worker (and with it the host), instead of the other loop hiding the failure.
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var loops = new[] { ReportAsync(clock, stop.Token), LeasesAsync(clock, stop.Token) };
        var first = await Task.WhenAny(loops);
        await stop.CancelAsync();
        try { await Task.WhenAll(loops); }
        catch (OperationCanceledException) when (!first.IsFaulted) { }
        await first;
    }

    private async Task LeasesAsync(TimeProvider clock, CancellationToken stoppingToken) {
        using var timer = new PeriodicTimer(LeaseInterval, clock);
        while (await timer.WaitForNextTickAsync(stoppingToken)) await store.MaintainLeasesAsync(clock.GetUtcNow(), stoppingToken);
    }

    private async Task ReportAsync(TimeProvider clock, CancellationToken stoppingToken) {
        var directory = Path.Combine(settings.DataDirectory, "shadow");
        Directory.CreateDirectory(directory);
        var written = new Dictionary<DateOnly, string>();
        using var timer = new PeriodicTimer(Interval, clock);
        do {
            var now = clock.GetUtcNow();
            await store.PruneEventsAsync(now, token: stoppingToken);
            await new LedgerRuntime(store, settings).ItemsAsync(now, stoppingToken);
            foreach (var day in new[] { DateOnly.FromDateTime(now.UtcDateTime).AddDays(-1), DateOnly.FromDateTime(now.UtcDateTime) }) {
                var file = Path.Combine(directory, day.ToString("yyyy-MM-dd") + ".json");
                var temporary = file + ".tmp";
                var bytes = JsonSerializer.SerializeToUtf8Bytes(await store.ReportAsync(day, now, stoppingToken), LedgerJson.Options);
                using var document = JsonDocument.Parse(bytes);
                var closed = day < DateOnly.FromDateTime(now.UtcDateTime);
                var content = JsonSerializer.SerializeToUtf8Bytes(new { transitions = document.RootElement.GetProperty("transitions"),
                    observationCoverage = document.RootElement.GetProperty("observationCoverage"),
                    reporterHealth = closed ? (JsonElement?)null : document.RootElement.GetProperty("reporterHealth"),
                    unknowns = closed ? (JsonElement?)null : document.RootElement.GetProperty("unresolvedUnknowns") }, LedgerJson.Options);
                var fingerprint = Convert.ToHexString(SHA256.HashData(content)) + (closed ? ":closed" : ":open");
                if (written.TryGetValue(day, out var previous) && previous == fingerprint) continue;
                await File.WriteAllBytesAsync(temporary, bytes, stoppingToken);
                if (File.Exists(file)) File.Replace(temporary, file, null);
                else File.Move(temporary, file);
                written[day] = fingerprint;
            }
            foreach (var old in written.Keys.Where(day => day < DateOnly.FromDateTime(now.UtcDateTime).AddDays(-1)).ToArray()) written.Remove(old);
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
