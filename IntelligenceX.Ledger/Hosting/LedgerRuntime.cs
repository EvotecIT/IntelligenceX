using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Rules;
using IntelligenceX.Ledger.Storage;

namespace IntelligenceX.Ledger.Hosting;

internal sealed partial class LedgerRuntime(LedgerStore store, LedgerSettings settings) {
    private readonly ConcurrentDictionary<string, (DateTimeOffset Time, LedgerItem[] Items)> _snapshots = new();
    internal static bool TokenEquals(string supplied, string expected) => CryptographicOperations.FixedTimeEquals(
        SHA256.HashData(Encoding.UTF8.GetBytes(supplied)), SHA256.HashData(Encoding.UTF8.GetBytes(expected)));

    internal async Task<MachineRow[]> MachinesAsync(DateTimeOffset now, CancellationToken token) {
        var heartbeats = await store.HeartbeatsAsync(token);
        var enrolled = await store.EnrollAsync(settings.ExpectedReporters.Keys, token);
        return settings.ExpectedReporters.Select(pair => {
            var times = pair.Value.Select(r => heartbeats.TryGetValue(r, out var time) ? (DateTimeOffset?)time : null).ToArray();
            var last = times.Where(x => x.HasValue).Select(x => x!.Value).DefaultIfEmpty().Min();
            var state = times.All(x => x is null) ? "not-enrolled" : times.Any(x => x is null || now - x > settings.Rules.ReporterTimeout)
                ? "reporter-silent" : "healthy";
            var since = times.Where(time => time is null || now - time > settings.Rules.ReporterTimeout)
                .Select(time => time ?? enrolled[pair.Key]).DefaultIfEmpty(enrolled[pair.Key]).Min();
            var basis = times.Any(time => time is null) && since == enrolled[pair.Key] ? "first-observation" : "source-event";
            return new MachineRow(pair.Key, state, last == default ? null : last,
                state == "healthy" ? null : new("stuck", state == "not-enrolled" ? "unknown" : "reporter-silent",
                    since, basis, now, LedgerRules.Version, []), pair.Value);
        }).ToArray();
    }

    internal async Task<LedgerItem[]> ItemsAsync(DateTimeOffset now, CancellationToken token, bool includeMessageEvidence = false) {
        var machines = await MachinesAsync(now, token);
        var silent = machines.Where(m => m.State != "healthy").Select(m => m.Id).ToHashSet();
        var items = await store.EvaluateAllAsync(now, token, silent);
        var rules = new LedgerRules(settings.Rules);
        return items.Where(i => includeMessageEvidence || !i.IsMessageEvidence)
            .Select(i => i with { Current = rules.Evaluate(i, now, silent.Contains(i.MachineId)) }).ToArray();
    }

    internal async Task<ItemPage?> PageAsync(string? cursor, int limit, string? verdict, string? machineId,
        string? provider, CancellationToken token) {
        var now = DateTimeOffset.UtcNow;
        foreach (var old in _snapshots.Where(x => now - x.Value.Time > TimeSpan.FromMinutes(5))) _snapshots.TryRemove(old.Key, out _);
        string id;
        int offset;
        (DateTimeOffset Time, LedgerItem[] Items) snapshot;
        if (cursor is null) {
            if (_snapshots.Count >= 64) _snapshots.TryRemove(_snapshots.MinBy(x => x.Value.Time).Key, out _);
            id = Guid.NewGuid().ToString("N"); offset = 0;
            var rows = await ItemsAsync(now, token);
            snapshot = (now, rows.Where(x => (verdict is null || x.Current?.Value == verdict) &&
                (machineId is null || x.MachineId == machineId) && (provider is null || x.Provider == provider))
                .OrderBy(x => x.Current!.SinceUtc).ThenBy(x => x.Id, StringComparer.Ordinal).ToArray());
            _snapshots[id] = snapshot;
        } else {
            var parts = cursor.Split(':');
            if (parts.Length != 2 || !int.TryParse(parts[1], out offset) || offset < 0 ||
                !_snapshots.TryGetValue(parts[0], out snapshot)) return null;
            id = parts[0];
        }
        var page = snapshot.Items.Skip(offset).Take(limit).ToArray();
        return new(id, snapshot.Time, page, offset + page.Length < snapshot.Items.Length ? $"{id}:{offset + page.Length}" : null);
    }
}
