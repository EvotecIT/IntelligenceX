using IntelligenceX.Ledger.Context;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Rules;

namespace IntelligenceX.Ledger.Hosting;

internal sealed partial class LedgerRuntime {
    private readonly SemaphoreSlim _workGate = new(1, 1);
    private long _workRevision;
    private WorkSnapshot? _workSnapshot;
    private sealed record WorkSnapshot(DateTimeOffset At, long Revision, LedgerItem[] Current, MachineRow[] Machines);

    internal void InvalidateWork() => Interlocked.Increment(ref _workRevision);

    internal async Task<LedgerWorkView> WorkAsync(ProjectContextService context, DateTimeOffset now, CancellationToken token,
        string? verdict = null, int limit = 200) {
        var labels = await context.LabelsAsync(token);
        WorkSnapshot snapshot;
        await _workGate.WaitAsync(token);
        try {
            var revision = Interlocked.Read(ref _workRevision);
            if (_workSnapshot is { } cached && cached.Revision == revision && now - cached.At < TimeSpan.FromSeconds(10)) snapshot = cached;
            else {
                var items = await ItemsAsync(now, token);
                var current = items.Where(item => item.Work?.ActiveClaim == true || item.Current?.Hold is not null ||
                    item.Session?.Archived != true && (item.Session?.TurnOpen == true ||
                item.Declarations.Values.Any(x => !x.Fact.Resolved && LedgerScope.Applies(x.Fact.Scope, item.Scope, item.Session?.TurnId)) ||
                item.Session?.LastActivityAtUtc >= now.AddDays(-1) ||
                item.Deliveries.Values.Any(x => x.LastStageOrderAtUtc >= now.AddDays(-1))))
            .OrderBy(item => Priority(item.Current!.Value)).ThenByDescending(item => item.Current!.LastProgressAtUtc)
                    .ThenBy(item => item.Id, StringComparer.Ordinal).ToArray();
                snapshot = _workSnapshot = new(now, revision, current, await MachinesAsync(now, token));
            }
        } finally { _workGate.Release(); }
        var selected = snapshot.Current.Where(item => verdict is null || item.Current!.Value == verdict).ToArray();
        var rows = selected.Take(limit).Select(item => {
            labels.TryGetValue(item.Id, out var display);
            var label = display?.Label ?? item.Provider + " " + item.Kind + " " + item.Id[..Math.Min(10, item.Id.Length)];
            return new LedgerWorkRow(label, display?.ProjectId, LedgerItemView.From(item));
        }).ToArray();
        return new(snapshot.At, LedgerRules.Version, snapshot.Machines,
            snapshot.Current.GroupBy(x => x.Current!.Value).ToDictionary(x => x.Key, x => x.Count()), rows,
            ["current-work:claimed-open-declared-or-active-within-24h", "display-labels:explicit-local-catalog",
             "work-view-cache:max-age-10s"], selected.Length > rows.Length);
    }

    private static int Priority(string verdict) => verdict switch {
        "needs-you" => 0,
        "stuck" => 1,
        "waiting" => 2,
        "working" => 3,
        _ => 4
    };
}
