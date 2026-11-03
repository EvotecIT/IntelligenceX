using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using IntelligenceX.Telemetry.Usage;
using IntelligenceX.Telemetry.Usage.Claude;
using IntelligenceX.Telemetry.Usage.Codex;
using static IntelligenceX.Telemetry.Agents.AgentSessionLogReader;

namespace IntelligenceX.Telemetry.Agents;

/// <summary>Reads provider metadata locally without launching clients, opening messages, or modifying provider state.</summary>
public static class LocalAgentWorkStateService {
    /// <summary>Reuses telemetry discovery, including configured profiles and recovered roots.</summary>
    public static IReadOnlyList<SourceRootRecord> DiscoverRoots() =>
        new CodexDefaultSourceRootDiscovery().DiscoverRoots().Concat(new ClaudeDefaultSourceRootDiscovery().DiscoverRoots()).ToArray();

    /// <summary>Reads local logs and title indexes. Explicit roots replace automatic discovery.</summary>
    /// <param name="roots">Optional telemetry source roots. An empty list reads nothing.</param>
    /// <param name="nowUtc">Evaluation time; defaults to UTC now after all local sources have been read.</param>
    /// <param name="activeWindow">Maximum quiet interval for an inferred running turn; defaults to five minutes.</param>
    /// <param name="codexMetadata">Optional read-only Codex database metadata from the SQLite companion library.</param>
    /// <param name="cancellationToken">Cancels enumeration and parsing.</param>
    /// <param name="options">Optional activity cutoff and parsed observation cache.</param>
    public static AgentWorkSnapshot ReadLocal(IEnumerable<SourceRootRecord>? roots = null, DateTimeOffset? nowUtc = null,
        TimeSpan? activeWindow = null, IEnumerable<AgentSessionState>? codexMetadata = null,
        CancellationToken cancellationToken = default, AgentWorkOptions? options = null) {
        options ??= new AgentWorkOptions();
        options.Validate();
        var window = activeWindow ?? options.ActiveTurnWindow;
        if (window <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(activeWindow));
        var collection = new AgentLocalCollection(options);
        collection.Reconcile((roots ?? DiscoverRoots()).ToArray(), nowUtc, cancellationToken);
        return collection.Snapshot(codexMetadata ?? Array.Empty<AgentSessionState>(), nowUtc, window);
    }

    internal static AgentWorkSnapshot BuildSnapshot(IEnumerable<AgentSessionState> sessions,
        IEnumerable<AgentSessionState> metadataRows, HashSet<string> runningRoots, DateTimeOffset now,
        DateTimeOffset? since, TimeSpan window, TimeSpan staleAfter, IReadOnlyList<AgentSourceDiagnostic> diagnostics, IReadOnlyList<AgentPullRequestState> pullRequests,
        AgentWorkScanStatistics statistics) {
        using var measurement = AgentWorkMeasurement.Begin("snapshot-build");
        var metadataKeys = new HashSet<string>(StringComparer.Ordinal);
        // Merge each root before cross-root deduplication: an old recovered copy must not hide
        // a local log whose newer activity is only available in that local root's Desktop database.
        var observations = new Dictionary<string, AgentSessionState>(StringComparer.Ordinal);
        foreach (var session in sessions) KeepLatest(observations, OriginKey(session), session);
        foreach (var metadata in metadataRows.OrderByDescending(static s => s.LastActivityAtUtc)) {
            var key = OriginKey(metadata);
            if (metadataKeys.Contains(key)) continue;
            if (!observations.TryGetValue(key, out var log)) {
                observations[key] = metadata;
                metadataKeys.Add(key);
                continue;
            }
            metadataKeys.Add(key);
            observations[key] = log with {
                Title = log.Title ?? metadata.Title, WorkingDirectory = metadata.WorkingDirectory ?? log.WorkingDirectory,
                Repository = metadata.Repository ?? log.Repository, Branch = metadata.Branch ?? log.Branch,
                IsArchived = metadata.IsArchived,
                State = metadata.IsArchived ? AgentWorkState.Finished
                    : log.IsArchived && log.State == AgentWorkState.Finished ? AgentWorkState.Idle : log.State,
                StateIsInferred = !metadata.IsArchived && log.StateIsInferred,
                StartedAtUtc = log.StartedAtUtc ?? metadata.StartedAtUtc,
                LastActivityAtUtc = log.LastActivityAtUtc is null || metadata.LastActivityAtUtc > log.LastActivityAtUtc
                    ? metadata.LastActivityAtUtc : log.LastActivityAtUtc,
                ObservationSources = log.ObservationSources | metadata.ObservationSources,
                ObservedAtUtc = log.ObservedAtUtc is null || metadata.ObservedAtUtc > log.ObservedAtUtc ? metadata.ObservedAtUtc : log.ObservedAtUtc
            };
        }
        var resolved = new Dictionary<string, AgentSessionState>(StringComparer.Ordinal);
        foreach (var session in observations.Values) {
            var value = ResolveState(session, now, window, session.SourceRootId is not null && runningRoots.Contains(session.SourceRootId));
            KeepLatest(resolved, Key(value), value);
        }
        return AgentWorkStateRules.Evaluate(resolved.Values.Where(session => since is null || session.LastActivityAtUtc >= since || AgentInputRequests.IsUnresolved(session)), pullRequests, now, staleAfter,
            diagnostics) with { Scan = statistics };
    }

    private static void KeepLatest(Dictionary<string, AgentSessionState> sessions, string key, AgentSessionState value) {
        if (!sessions.TryGetValue(key, out var previous) || Nullable.Compare(value.LastActivityAtUtc, previous.LastActivityAtUtc) > 0
            || (value.LastActivityAtUtc == previous.LastActivityAtUtc && previous.IsArchived && !value.IsArchived)) sessions[key] = value;
    }

    private static AgentSessionState ResolveState(AgentSessionState session, DateTimeOffset now, TimeSpan window, bool local) {
        if (session.IsArchived) return session with { State = AgentWorkState.Finished, StateIsInferred = false };
        var openTurn = session.StateIsInferred && session.LastTurnOutcome == AgentTurnOutcome.Incomplete;
        if (!openTurn) return session;
        var recent = local && session.LastActivityAtUtc.HasValue && session.LastActivityAtUtc <= now
            && now - session.LastActivityAtUtc.Value <= window;
        return session with {
            State = recent ? AgentWorkState.Running : AgentWorkState.Idle,
            StateIsInferred = recent || openTurn,
            LastTurnOutcome = recent ? AgentTurnOutcome.Unknown : session.LastTurnOutcome
        };
    }

    private static string Key(AgentSessionState session) => session.Provider + "\0" + session.Machine + "\0" + session.Id;
    private static string OriginKey(AgentSessionState session) => Key(session) + "\0" + session.SourceRootId;

    internal static bool IsSubagentPath(string path) => path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
        .Any(static part => part == "subagents") || Path.GetFileName(path).StartsWith("agent-", StringComparison.Ordinal);

    internal static void ReadCodexTitles(SourceRootRecord root, List<AgentSessionState> sessions, int firstSession,
        Action<string, string> report, CancellationToken cancellationToken) {
        var titles = new Dictionary<string, string>(StringComparer.Ordinal);
        var indexPath = Path.Combine(root.Path, "session_index.jsonl");
        if (File.Exists(indexPath)) {
            foreach (var line in ReadLines(indexPath, cancellationToken)) {
                try {
                    using var document = JsonDocument.Parse(line);
                    var entry = document.RootElement;
                    var id = Text(entry, "id");
                    var title = Text(entry, "thread_name");
                    if (id is not null && title is not null) titles[id] = title;
                } catch (JsonException) { report("codex", "malformed-index"); }
            }
        }
        // Desktop title caches and threads.title do not establish explicit-name provenance.
        var machine = root.MachineLabel ?? (root.SourceKind == UsageSourceKind.LocalLogs ? Environment.MachineName : null);
        for (var i = firstSession; i < sessions.Count; i++) {
            var session = sessions[i];
            if (session.Provider == "codex" && session.Machine == machine && titles.TryGetValue(session.Id, out var title)) {
                sessions[i] = session with { Title = title };
            }
        }
        foreach (var title in titles) sessions.Add(new AgentSessionState {
            Provider = "codex", Id = title.Key, Machine = machine, Title = title.Value, SourceRootId = root.Id,
            ObservationSources = AgentObservationSources.TitleIndex
        });
    }
}
