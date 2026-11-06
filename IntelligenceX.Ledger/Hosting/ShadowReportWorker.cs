using System.Text.Json;
using System.Security.Cryptography;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Storage;

namespace IntelligenceX.Ledger.Hosting;

/// <summary>Reevaluates aging rules and writes daily shadow reports locally without waking any client.</summary>
internal sealed class ShadowReportWorker(LedgerStore store, LedgerSettings settings) : BackgroundService {
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        var directory = Path.Combine(settings.DataDirectory, "shadow");
        Directory.CreateDirectory(directory);
        var written = new Dictionary<DateOnly, string>();
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do {
            var now = DateTimeOffset.UtcNow;
            await new LedgerRuntime(store, settings).ItemsAsync(now, stoppingToken);
            foreach (var day in new[] { DateOnly.FromDateTime(now.UtcDateTime).AddDays(-1), DateOnly.FromDateTime(now.UtcDateTime) }) {
                var file = Path.Combine(directory, day.ToString("yyyy-MM-dd") + ".json");
                var temporary = file + ".tmp";
                var bytes = JsonSerializer.SerializeToUtf8Bytes(await store.ReportAsync(day, now, stoppingToken), LedgerJson.Options);
                using var document = JsonDocument.Parse(bytes);
                var closed = day < DateOnly.FromDateTime(now.UtcDateTime);
                var content = JsonSerializer.SerializeToUtf8Bytes(new { transitions = document.RootElement.GetProperty("transitions"),
                    observationCoverage = document.RootElement.GetProperty("observationCoverage"),
                    unknowns = closed ? (JsonElement?)null : document.RootElement.GetProperty("unresolvedUnknowns") }, LedgerJson.Options);
                var fingerprint = Convert.ToHexString(SHA256.HashData(content)) + (closed ? ":closed" : ":open");
                if (written.TryGetValue(day, out var previous) && previous == fingerprint) continue;
                await File.WriteAllBytesAsync(temporary, bytes, stoppingToken);
                File.Move(temporary, file, true);
                written[day] = fingerprint;
            }
            foreach (var old in written.Keys.Where(day => day < DateOnly.FromDateTime(now.UtcDateTime).AddDays(-1)).ToArray()) written.Remove(old);
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
