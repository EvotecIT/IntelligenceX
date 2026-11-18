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

    internal static SessionLink? Link(CoordinationWork work, CoordinationSessionBinding binding) =>
        work.Request.ReturnTo is { } asker
            ? new(asker.Provider, asker.SessionId, binding.Provider, binding.SessionId, "ix-ask",
                work.Stages?.GetValueOrDefault("bound") ?? work.Stages?.GetValueOrDefault("started") ?? work.RequestedAtUtc, work.Request.Id)
            : null;

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
