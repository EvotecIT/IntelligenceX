using System.Collections.Concurrent;
using System.Text.Json;
using IntelligenceX.Ledger.Agents;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Reporting;
using IntelligenceX.Ledger.Rules;

namespace IntelligenceX.Ledger.Hosting;

/// <summary>Observations collected alongside the shadow report. Work requests never start an app server or read profile files.</summary>
internal sealed class WorkAccountLimits {
    private readonly string _machine = AccountLimits.LocalMachine;
    private readonly ConcurrentDictionary<string, AccountLimit> _observed = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _cache;
    private DateTimeOffset _lastCodexProbe;

    private sealed record StoredObservation(string AccountId, AccountLimit Limit,
        DateTimeOffset? WindowObservedAtUtc, DateTimeOffset? ShortWindowObservedAtUtc, DateTimeOffset? AvailableAtUtc);

    internal WorkAccountLimits(string dataDirectory) {
        _cache = Path.Combine(dataDirectory, "account-limit-observations.json");
        try {
            if (new FileInfo(_cache) is not { Exists: true, Length: <= 1048576 }) return;
            var rows = JsonSerializer.Deserialize<StoredObservation[]>(File.ReadAllText(_cache));
            foreach (var row in rows ?? []) {
                if (row is null || string.IsNullOrEmpty(row.AccountId) || row.Limit?.Machine != _machine) continue;
                _observed[row.AccountId] = row.Limit with { AccountId = row.AccountId,
                    WindowObservedAtUtc = row.WindowObservedAtUtc, ShortWindowObservedAtUtc = row.ShortWindowObservedAtUtc,
                    AvailableAtUtc = row.AvailableAtUtc };
            }
        } catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException) { }
    }

    internal AccountLimit? ForItem(LedgerItem item) => item.MachineId == _machine && item.Session?.AccountId is { } account
        ? _observed.GetValueOrDefault(account)
        : null;

    internal void ObserveClaude(string snapshot, DateTimeOffset now) {
        if (AccountLimits.Claude(snapshot, now) is not { } limit || limit.UsedPercent is null && limit.ShortUsedPercent is null) return;
        if (limit.AccountId is null && ClaudeAccountAt(snapshot, limit.ObservedAtUtc) is { } account)
            limit = limit with { AccountId = "claude:" + account };
        Remember(limit);
    }

    private static string? ClaudeAccountAt(string snapshot, DateTimeOffset observed) {
        // The legacy status line omits account identity. Its profile must already have existed at observation time.
        var config = Path.Combine(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR"))
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) : Path.GetDirectoryName(snapshot)!, ".claude.json");
        var (id, written) = ClaudeDesktopAccounts.ReadCliAccount(config);
        return written <= observed ? id : null;
    }

    internal async Task ObserveCodexAsync(LedgerItem[] items, DateTimeOffset now, CancellationToken token) {
        var cutoff = now.AddDays(-7);
        var changed = false;
        foreach (var (account, observation) in _observed)
            if (observation.ObservedAtUtc < cutoff) changed |= _observed.TryRemove(account, out _);
        if (changed) Save();
        if (!items.Any(item => NeedsCodexProbe(item, now, cutoff))) return;
        _lastCodexProbe = now;
        if (await AccountLimits.CodexAsync(token, qualifyAccount: true) is { Note: null } limit) Remember(limit);
    }

    private void Remember(AccountLimit limit) {
        if (limit.AccountId is not { } identity || _observed.TryGetValue(identity, out var prior) && prior.ObservedAtUtc >= limit.ObservedAtUtc)
            return;
        // A retained reset keeps its original clock, so it cannot be attached to a more recent stop.
        if (prior is not null && RetainWindow(limit.UsedPercent, limit.ResetsAtUtc, prior.UsedPercent, prior.ResetsAtUtc))
            limit = limit with { UsedPercent = prior.UsedPercent, WindowMinutes = prior.WindowMinutes, ResetsAtUtc = prior.ResetsAtUtc,
                WindowObservedAtUtc = prior.WindowObservedAtUtc ?? prior.ObservedAtUtc };
        if (prior is not null && RetainWindow(limit.ShortUsedPercent, limit.ShortResetsAtUtc, prior.ShortUsedPercent, prior.ShortResetsAtUtc))
            limit = limit with { ShortUsedPercent = prior.ShortUsedPercent, ShortResetsAtUtc = prior.ShortResetsAtUtc,
                ShortWindowObservedAtUtc = prior.ShortWindowObservedAtUtc ?? prior.ObservedAtUtc };
        var available = LedgerRules.AccountAvailableAt(limit);
        if (prior?.AvailableAtUtc is { } earlier && !(available > earlier)) available = earlier;
        _observed[identity] = limit with { Machine = _machine, AvailableAtUtc = available };
        Save();
    }

    private static bool RetainWindow(double? used, DateTimeOffset? reset, double? priorUsed, DateTimeOffset? priorReset) =>
        reset is null && (used is null || used >= 100 && priorUsed >= 100 && priorReset is not null);

    private void Save() {
        try {
            // This data is local to the host. Account identity is excluded from the budget response.
            LocalFiles.WriteAtomic(_cache, JsonSerializer.Serialize(_observed.Select(pair =>
                new StoredObservation(pair.Key, pair.Value, pair.Value.WindowObservedAtUtc, pair.Value.ShortWindowObservedAtUtc,
                    pair.Value.AvailableAtUtc))));
        } catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    private bool NeedsCodexProbe(LedgerItem item, DateTimeOffset now, DateTimeOffset cutoff) {
        if (item.MachineId != _machine || item.Provider != "codex" ||
            item.Session is not { AccountId: { } account, UsageLimit: true, Archived: not true } ||
            !account.StartsWith("codex:", StringComparison.Ordinal) || item.Current?.UsageLimit is not { } stop ||
            stop.RecordedAtUtc < cutoff) return false;
        var cached = _observed.GetValueOrDefault(account);
        return stop.InForce || (cached is null || cached.ObservedAtUtc <= stop.RecordedAtUtc) &&
            (_lastCodexProbe < stop.RecordedAtUtc || now - _lastCodexProbe >= TimeSpan.FromHours(1));
    }
}
