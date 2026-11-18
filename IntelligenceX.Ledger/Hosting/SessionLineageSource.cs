using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Coordination;

namespace IntelligenceX.Ledger.Hosting;

/// <summary>
/// Parent-child links and ask steps known to coordination: a request's return session asked, and the session bound to
/// the request helped. Live requests and those archived within the window are read; links of older requests were
/// recorded when they were bound.
/// </summary>
internal static class SessionLineageSource {
    internal static readonly TimeSpan ArchiveWindow = TimeSpan.FromDays(30);

    /// <summary>The asker started the helper, unless the request reused a session that already existed: that one keeps
    /// its own place, and the ask still shows in the trail.</summary>
    internal static SessionLink? Link(CoordinationWork work, CoordinationSessionBinding binding) =>
        work.Request.ReturnTo is { } asker && !work.Request.ReuseSession
            ? new(asker.Provider, asker.SessionId, binding.Provider, binding.SessionId, "ix-ask", BoundAt(work), work.Request.Id)
            : null;

    /// <summary>When the helper was bound (the engine's session-bound stage), else when it started, else when it was asked.</summary>
    private static DateTimeOffset BoundAt(CoordinationWork work) =>
        work.Stages is { } stages && (stages.TryGetValue("session-bound", out var bound) || stages.TryGetValue("started", out bound)) ? bound : work.RequestedAtUtc;

    internal static SessionAskStep? Step(CoordinationWork work, CoordinationSessionBinding? binding, string? resultRef) =>
        work.Request.ReturnTo is { } asker
            ? new(work.Request.Id, asker.Provider, asker.SessionId, binding?.Provider, binding?.SessionId, work.State,
                work.RequestedAtUtc, work.Stages is null ? [] : new(work.Stages), resultRef ?? work.ResultRef)
            : null;

    internal static async Task<(SessionLink[] Links, SessionAskStep[] Asks)> ReadAsync(CoordinationStore? store, DateTimeOffset now, CancellationToken token) {
        if (store is null) return ([], []);
        var live = await store.ReadAsync(state => state.Work.Values.Select(work => (work, state.Bindings.GetValueOrDefault(work.Request.Id),
            state.ResultOutbox.GetValueOrDefault(work.Request.Id)?.ResultRef)).ToArray(), token);
        var archived = await store.ArchivedSinceAsync(now - ArchiveWindow, token);
        var rows = live.Concat(archived.Select(entry => (entry.Work, entry.Binding, entry.Result?.ResultRef)))
            .DistinctBy(row => row.Item1.Request.Id).ToArray();
        return (rows.Where(row => row.Item2 is not null).Select(row => Link(row.Item1, row.Item2!)).OfType<SessionLink>().ToArray(),
            rows.Select(row => Step(row.Item1, row.Item2, row.Item3)).OfType<SessionAskStep>().ToArray());
    }
}

/// <summary>
/// Records ask links from live and recently archived requests every hour, so a link the binding route failed to write
/// becomes durable before its request leaves the archive window. Repeats are harmless: a child keeps its first parent.
/// </summary>
internal sealed class SessionLineageBackfill(CoordinationStore coordination, Storage.LedgerStore ledger) : BackgroundService {
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do {
            try {
                var (links, _) = await SessionLineageSource.ReadAsync(coordination, DateTimeOffset.UtcNow, stoppingToken);
                await ledger.RecordLinksAsync(links, stoppingToken);
            } catch (Exception error) when (error is not OperationCanceledException) {
                // The next pass tries again; the links stay readable from coordination meanwhile.
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
