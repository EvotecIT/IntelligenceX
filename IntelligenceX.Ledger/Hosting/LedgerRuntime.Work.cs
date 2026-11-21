using IntelligenceX.Ledger.Context;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Rules;

namespace IntelligenceX.Ledger.Hosting;

internal sealed partial class LedgerRuntime {
    private readonly SemaphoreSlim _workGate = new(1, 1);
    /// <summary>The refresh gate, so tests can hold a refresh in progress.</summary>
    internal SemaphoreSlim WorkGate => _workGate;
    private long _workRevision;
    private WorkSnapshot? _workSnapshot;
    private static readonly TimeSpan OpenLookback = TimeSpan.FromDays(7);
    /// <summary>
    /// A session with no sign of life for this long is history, not current work: its client ended without recording the
    /// end of its last turn, or it was archived while a request still pointed at it. It leaves the current view (and the
    /// stuck count) unless something still holds an active claim or an unresolved declaration on it, and stays readable
    /// through its item. A session stopped at a usage limit stays longer, until the weekly limit has surely reset, so it
    /// can still be resumed from the view.
    /// </summary>
    internal static readonly TimeSpan DormantAfter = TimeSpan.FromDays(3), DormantAfterUsageLimit = TimeSpan.FromDays(7);
    private sealed record WorkSnapshot(DateTimeOffset At, long Revision, LedgerItem[] Current, LedgerItem[] Open,
        MachineRow[] Machines, int HistoricalDeliveries, WorkRelationships Relationships, int DormantSessions = 0) {
        public long BuiltAtTicks { get; } = Environment.TickCount64;
    }

    /// <summary>
    /// A refresh evaluates every item and takes seconds, and every ingest invalidates the view, so on a busy Ledger nearly
    /// every read refreshed. Callers that arrive while one refresh runs get the previous view (its as-of time says how old)
    /// instead of queueing behind it, as long as it was built within this long.
    /// </summary>
    internal static readonly TimeSpan ServeWhileRefreshing = TimeSpan.FromMinutes(2);

    internal void InvalidateWork() => Interlocked.Increment(ref _workRevision);

    internal async Task<LedgerWorkView> WorkAsync(ProjectContextService context, DateTimeOffset now, CancellationToken token,
        string? verdict = null, int limit = 200, bool includeOpen = false) {
        var labels = await context.LabelsAsync(token);
        WorkSnapshot snapshot;
        var refreshing = !await _workGate.WaitAsync(0, token);
        if (refreshing && Volatile.Read(ref _workSnapshot) is { } previous &&
            Environment.TickCount64 - previous.BuiltAtTicks < (long)ServeWhileRefreshing.TotalMilliseconds)
            return View(previous, labels, verdict, limit, includeOpen, servedWhileRefreshing: true);
        if (refreshing) await _workGate.WaitAsync(token);
        try {
            var revision = Interlocked.Read(ref _workRevision);
            if (_workSnapshot is { } cached && cached.Revision == revision && now >= cached.At && now - cached.At < TimeSpan.FromSeconds(10)) snapshot = cached;
            else {
                snapshot = await RefreshWorkAsync(now, revision, token);
                Volatile.Write(ref _workSnapshot, snapshot);
            }
        } finally { _workGate.Release(); }
        return View(snapshot, labels, verdict, limit, includeOpen, servedWhileRefreshing: false);
    }

    private async Task<WorkSnapshot> RefreshWorkAsync(DateTimeOffset now, long revision, CancellationToken token) {
        var machines = await MachinesAsync(now, token);
        var items = await ItemsAsync(now, machines, token);
        var regular = new List<LedgerItem>();
        var recentlyEnded = new List<LedgerItem>();
        var historical = 0;
        var dormant = 0;
        var openSince = now - OpenLookback;
        foreach (var item in items) {
            if (IsHistoricalDelivery(item, now)) {
                historical++;
            } else if (LedgerRules.IsNormallyEnded(item)) {
                if (item.Session is { Archived: not true, LastActivityAtUtc: { } activity } && activity >= openSince)
                    recentlyEnded.Add(item);
            } else if (!LedgerRules.IsSessionCatalog(item) && IsCurrent(item, now)) {
                // Counted only when the session would otherwise be listed, so the count says how many rows it hid.
                if (IsDormantSession(item, now)) dormant++;
                else regular.Add(item);
            }
        }
        var candidates = regular.Concat(recentlyEnded).ToArray();
        var asks = _coordination is null ? [] : await _coordination.WorkAsksAsync(
            SessionKeys(items.Where(item => item.Session?.Archived != true)), SessionKeys(candidates), token);
        var parents = await _store.NativeParentsAsync(candidates.Where(item =>
                item is { Kind: "session", Session: { SourceSessionId: not null, StartedBySessionId: null } })
            .Select(item => (item.Provider, item.Session!.SourceSessionId!.Value.ToString())).ToArray(), token);
        var relationships = new WorkRelationships(items, asks, parents);
        var helperRows = items.Select(item => (Item: item, Ask: relationships.Ask(item))).Where(row => row.Ask is { Finished: false }).ToArray();
        // A helper session the loop didn't already count (one that ended normally) is counted here when it goes dormant.
        dormant += helperRows.Count(row => IsDormantSession(row.Item, now) && LedgerRules.IsNormallyEnded(row.Item));
        var activeHelpers = helperRows.Where(row => !IsDormantSession(row.Item, now)).Select(row => ApplyAskVerdict(row.Item, row.Ask!, now));
        var current = regular.Where(item => relationships.Ask(item) is null).Concat(activeHelpers);
        var open = recentlyEnded.Select(item => (Item: item, Ask: relationships.Ask(item)))
            .Where(row => row.Ask is null or { Finished: true })
            .Select(row => row.Ask is null ? row.Item : ApplyAskVerdict(row.Item, row.Ask, now));
        return new(now, revision, current.OrderBy(item => Priority(item.Current!.Value))
                .ThenByDescending(item => item.Current!.LastProgressAtUtc).ThenBy(item => item.Id, StringComparer.Ordinal).ToArray(),
            open.OrderByDescending(item => item.Session!.LastActivityAtUtc).ThenBy(item => item.Id, StringComparer.Ordinal).ToArray(),
            machines, historical, relationships, dormant);
    }

    // Unresolved declarations of the current turn and candidate, the same ones that keep an item current.
    private static bool HasCurrentDeclaration(LedgerItem item) =>
        LedgerScope.CurrentDeclarations(item.Declarations.Values.Where(x =>
            LedgerScope.Applies(x.Fact.Scope, item.Scope, item.Session?.TurnId ?? item.Scope?.TurnId) &&
            (x.Fact.CandidateRef is null || item.CandidateRef is null || x.Fact.CandidateRef == item.CandidateRef)))
            .Any(x => !x.Fact.Resolved);

    internal static bool IsDormantSession(LedgerItem item, DateTimeOffset now) {
        if (item.Kind != "session" || item.Work?.ActiveClaim == true || HasCurrentDeclaration(item)) return false;
        var lastSign = new[] { item.Session?.LastActivityAtUtc, item.Session?.TurnEndedAtUtc, item.Session?.TurnStartedAtUtc }
            .Concat(item.Deliveries.Values.Select(delivery => delivery.LastStageOrderAtUtc))
            .Concat(item.Declarations.Values.Select(declaration => (DateTimeOffset?)declaration.Fact.RecordedAtUtc))
            .Where(at => at is not null).DefaultIfEmpty(item.FirstObservedAtUtc).Max();
        var limited = item.Session?.UsageLimit == true || item.Current?.ReasonCode == "usage-limit";
        return lastSign < now - (limited ? DormantAfterUsageLimit : DormantAfter);
    }

    private static HashSet<(string Machine, string Provider, Guid Session)> SessionKeys(IEnumerable<LedgerItem> items) =>
        items.Where(item => item is { Kind: "session", Session.SourceSessionId: not null })
            .Select(item => (item.MachineId, item.Provider, item.Session!.SourceSessionId!.Value)).ToHashSet();

    private static bool IsCurrent(LedgerItem item, DateTimeOffset now) =>
        item.Work?.ActiveClaim == true || item.Current?.Hold is { Type: not "observation-gap" } ||
        item.Session?.Archived != true && (item.Session?.TurnOpen == true ||
            item.Current?.ReasonCode == "usage-limit" ||
            LedgerScope.CurrentDeclarations(item.Declarations.Values.Where(x =>
                LedgerScope.Applies(x.Fact.Scope, item.Scope, item.Session?.TurnId ?? item.Scope?.TurnId) &&
                (x.Fact.CandidateRef is null || item.CandidateRef is null || x.Fact.CandidateRef == item.CandidateRef)))
                .Any(x => !x.Fact.Resolved) ||
            item.Session?.LastActivityAtUtc >= now.AddDays(-1) ||
            item.Deliveries.Values.Any(x => x.LastStageOrderAtUtc >= now.AddDays(-1)));

    private static LedgerWorkView View(WorkSnapshot snapshot, IReadOnlyDictionary<string, WorkLabel> labels, string? verdict, int limit,
        bool includeOpen, bool servedWhileRefreshing) {
        var selected = snapshot.Current.Where(item => verdict is null || item.Current!.Value == verdict).ToArray();
        var visible = verdict is null ? Interleave(selected).Take(limit) : selected.Take(limit);
        var open = includeOpen ? snapshot.Open.Where(item => verdict is null || item.Current!.Value == verdict).ToArray() : [];
        var rows = visible.Select(item => snapshot.Relationships.Row(item, labels, false))
            .Concat(open.Take(limit).Select(item => snapshot.Relationships.Row(item, labels, true))).ToArray();
        return new(snapshot.At, LedgerRules.Version, snapshot.Machines,
            snapshot.Current.GroupBy(x => x.Current!.Value).ToDictionary(x => x.Key, x => x.Count()), rows,
            ["current-work:unfinished-turn-or-explicit-follow-up", "display-labels:explicit-local-catalog",
             "imported-delivery-history-older-than-24h:" + snapshot.HistoricalDeliveries,
             "dormant-sessions-silent-over-3d:" + snapshot.DormantSessions,
             servedWhileRefreshing ? "work-view-cache:previous-view-while-refreshing" : "work-view-cache:max-age-10s"],
            selected.Length > limit || open.Length > limit) { OpenCount = includeOpen ? snapshot.Open.Length : null };
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
