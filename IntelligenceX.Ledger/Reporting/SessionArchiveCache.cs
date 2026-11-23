using IntelligenceX.Telemetry.Agents;

namespace IntelligenceX.Ledger.Reporting;

/// <summary>Archive evidence belongs to the selected provider/root/session origin.</summary>
internal sealed class SessionArchiveCache {
    internal sealed record Observation(bool Archived, DateTimeOffset ObservedAtUtc,
        string? WorkingDirectory, string? ThreadSource, DateTimeOffset ReadStartedAtUtc);
    private IReadOnlyDictionary<(string Id, string Root), Observation> _snapshot =
        new Dictionary<(string, string), Observation>();

    internal void Observe(IEnumerable<AgentSessionState> sessions, DateTimeOffset observedAtUtc, DateTimeOffset? readStartedAtUtc = null) {
        var readStarted = readStartedAtUtc ?? observedAtUtc;
        var prior = Volatile.Read(ref _snapshot);
        var next = sessions.Where(session => session.Provider == "codex" && session.ArchiveStateKnown &&
                !string.IsNullOrEmpty(session.SourceRootId))
            .GroupBy(session => (session.Id, Root: session.SourceRootId!))
            .ToDictionary(group => group.Key, group => {
                var native = group.First();
                if (prior.TryGetValue(group.Key, out var previous) && previous.Archived == native.IsArchived)
                    return previous with { WorkingDirectory = native.WorkingDirectory, ThreadSource = native.ThreadSource,
                        ReadStartedAtUtc = readStarted };
                return new Observation(native.IsArchived, observedAtUtc, native.WorkingDirectory, native.ThreadSource, readStarted);
            });
        Volatile.Write(ref _snapshot, next);
    }

    internal Observation? For(AgentSessionState session) => session.Provider == "codex" &&
        session.SourceRootId is { Length: > 0 } root &&
        Volatile.Read(ref _snapshot).TryGetValue((session.Id, root), out var observed) ? observed : null;
}
