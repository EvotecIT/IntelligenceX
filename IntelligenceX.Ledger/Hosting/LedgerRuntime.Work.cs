using IntelligenceX.Ledger.Context;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Rules;

namespace IntelligenceX.Ledger.Hosting;

internal sealed partial class LedgerRuntime {
    private readonly SemaphoreSlim _workGate = new(1, 1);
    private long _workRevision;
    private WorkSnapshot? _workSnapshot;
    private sealed record WorkSnapshot(DateTimeOffset At, long Revision, LedgerItem[] Current, MachineRow[] Machines, int HistoricalDeliveries);

    internal void InvalidateWork() => Interlocked.Increment(ref _workRevision);

    internal async Task<LedgerWorkView> WorkAsync(ProjectContextService context, DateTimeOffset now, CancellationToken token,
        string? verdict = null, int limit = 200) {
        var labels = await context.LabelsAsync(token);
        WorkSnapshot snapshot;
        await _workGate.WaitAsync(token);
        try {
            var revision = Interlocked.Read(ref _workRevision);
            if (_workSnapshot is { } cached && cached.Revision == revision && now >= cached.At && now - cached.At < TimeSpan.FromSeconds(10)) snapshot = cached;
            else {
                var items = await ItemsAsync(now, token);
                var historicalDeliveries = items.Where(item => IsHistoricalDelivery(item, now)).Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
                var current = items.Where(item => !LedgerRules.IsNormallyEnded(item) && !LedgerRules.IsSessionCatalog(item) &&
                    !historicalDeliveries.Contains(item.Id) &&
                    (item.Work?.ActiveClaim == true || item.Current?.Hold is { Type: not "observation-gap" } ||
                    item.Session?.Archived != true && (item.Session?.TurnOpen == true ||
                        LedgerScope.CurrentDeclarations(item.Declarations.Values.Where(x =>
                            LedgerScope.Applies(x.Fact.Scope, item.Scope, item.Session?.TurnId ?? item.Scope?.TurnId) &&
                            (x.Fact.CandidateRef is null || item.CandidateRef is null || x.Fact.CandidateRef == item.CandidateRef)))
                            .Any(x => !x.Fact.Resolved) ||
                        item.Session?.LastActivityAtUtc >= now.AddDays(-1) ||
                        item.Deliveries.Values.Any(x => x.LastStageOrderAtUtc >= now.AddDays(-1)))))
                    .OrderBy(item => Priority(item.Current!.Value)).ThenByDescending(item => item.Current!.LastProgressAtUtc)
                    .ThenBy(item => item.Id, StringComparer.Ordinal).ToArray();
                snapshot = _workSnapshot = new(now, revision, current, await MachinesAsync(now, token), historicalDeliveries.Count);
            }
        } finally { _workGate.Release(); }
        var selected = snapshot.Current.Where(item => verdict is null || item.Current!.Value == verdict).ToArray();
        var visible = verdict is null ? Interleave(selected).Take(limit) : selected.Take(limit);
        var rows = visible.Select(item => {
            labels.TryGetValue(item.Id, out var display);
            var label = display?.Label ?? item.Provider + " " + item.Kind + " " + item.Id[..Math.Min(10, item.Id.Length)];
            return new LedgerWorkRow(label, display?.ProjectId, LedgerItemView.From(item));
        }).ToArray();
        return new(snapshot.At, LedgerRules.Version, snapshot.Machines,
            snapshot.Current.GroupBy(x => x.Current!.Value).ToDictionary(x => x.Key, x => x.Count()), rows,
            ["current-work:unfinished-turn-or-explicit-follow-up", "display-labels:explicit-local-catalog",
             "imported-delivery-history-older-than-24h:" + snapshot.HistoricalDeliveries,
             "work-view-cache:max-age-10s"], selected.Length > rows.Length);
    }

    private static bool IsHistoricalDelivery(LedgerItem item, DateTimeOffset now) =>
        item.Kind == "delivery" && item.Session is null && item.Work?.ActiveClaim != true &&
        !item.Declarations.Values.Any(declaration => !declaration.Fact.Resolved && declaration.Fact.Blocking) &&
        item.Deliveries.Count > 0 && item.Deliveries.Values.SelectMany(delivery => delivery.Stages.Values)
            .SelectMany(stage => stage.Sources()).All(evidence => evidence.Source == "evidence" &&
                evidence.ObservedAtUtc < now.AddDays(-1));

    private static IEnumerable<LedgerItem> Interleave(LedgerItem[] items) {
        var groups = items.GroupBy(item => item.Current!.Value).Select(group => group.ToArray()).ToArray();
        var longest = groups.Select(group => group.Length).DefaultIfEmpty().Max();
        for (var index = 0; index < longest; index++)
            foreach (var group in groups)
                if (index < group.Length) yield return group[index];
    }

    private static int Priority(string verdict) => verdict switch {
        "needs-you" => 0,
        "stuck" => 1,
        "waiting" => 2,
        "working" => 3,
        _ => 4
    };
}
