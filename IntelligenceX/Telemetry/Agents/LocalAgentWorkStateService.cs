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
        var since = options.ActiveWithin.HasValue ? (nowUtc ?? DateTimeOffset.UtcNow) - options.ActiveWithin.Value : (DateTimeOffset?)null;
        var window = activeWindow ?? options.ActiveTurnWindow;
        if (window <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(activeWindow));
        var sessions = new List<AgentSessionState>();
        var metadataRows = (codexMetadata ?? Array.Empty<AgentSessionState>()).Where(static s => s.Provider == "codex").ToArray();
        var runningRoots = new HashSet<string>(StringComparer.Ordinal);
        var metadataKeys = new HashSet<string>(StringComparer.Ordinal);
        var scans = new List<AgentSessionScan>();
        var diagnostics = new Dictionary<string, int>(StringComparer.Ordinal);
        void Report(string provider, string code) {
            var key = provider + ":" + code;
            diagnostics.TryGetValue(key, out var count);
            diagnostics[key] = count + 1;
        }
        foreach (var root in roots ?? DiscoverRoots()) {
            cancellationToken.ThrowIfCancellationRequested();
            if (!root.Enabled || (root.ProviderId != "codex" && root.ProviderId != "claude")) continue;
            if (!Directory.Exists(root.Path) && !File.Exists(root.Path)) {
                Report(root.ProviderId, "missing-source");
                continue;
            }
            try {
                var local = AgentRepositoryIdentity.CanResolveWorkspace(root);
                var machine = root.MachineLabel ?? (root.SourceKind == UsageSourceKind.LocalLogs ? Environment.MachineName : null);
                // WSL logs are local activity even though their workspace paths are not Windows paths.
                if (root.SourceKind == UsageSourceKind.LocalLogs) runningRoots.Add(root.Id);
                var scan = new AgentSessionScan(root, options.Cache, code => Report(root.ProviderId, code));
                scans.Add(scan);
                var firstSession = sessions.Count;
                var files = root.ProviderId == "codex"
                    ? CodexSessionImportSupport.EnumerateCandidateFiles(root.Path, preferRecentArtifacts: false)
                    : ClaudeSessionImportSupport.EnumerateCandidateFiles(root.Path, preferRecentArtifacts: false);
                foreach (var path in files) {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (root.ProviderId == "claude" && IsSubagentPath(path)) continue;
                    try {
                        var session = scan.Read(path, since, cancellationToken);
                        if (session is not null) {
                            session = session with { Machine = machine };
                            session = local ? AgentRepositoryIdentity.Enrich(session) : session;
                            sessions.Add(session);
                        }
                    } catch (IOException) { Report(root.ProviderId, "unreadable-source");
                    } catch (UnauthorizedAccessException) { Report(root.ProviderId, "unreadable-source"); }
                }
                if (root.ProviderId == "codex" && Directory.Exists(root.Path)) ReadCodexTitles(root, sessions, firstSession, Report, cancellationToken);
            } catch (IOException) { Report(root.ProviderId, "unreadable-source");
            } catch (UnauthorizedAccessException) { Report(root.ProviderId, "unreadable-source"); }
        }
        // Merge each root before cross-root deduplication: an old recovered copy must not hide
        // a local log whose newer activity is only available in that local root's Desktop database.
        var observations = sessions.GroupBy(OriginKey, StringComparer.Ordinal).Select(group => group
            .OrderByDescending(static s => s.LastActivityAtUtc).ThenBy(static s => s.IsArchived).First()).ToDictionary(OriginKey, StringComparer.Ordinal);
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
                    ? metadata.LastActivityAtUtc : log.LastActivityAtUtc
            };
        }
        var now = nowUtc ?? DateTimeOffset.UtcNow;
        var resolved = observations.Values.Select(session => ResolveState(session, now, window,
            session.SourceRootId is not null && runningRoots.Contains(session.SourceRootId)))
            .GroupBy(Key, StringComparer.Ordinal).Select(group => group.OrderByDescending(static s => s.LastActivityAtUtc)
                .ThenBy(static s => s.IsArchived).First())
            .Where(session => since is null || session.LastActivityAtUtc >= since);
        return AgentWorkStateRules.Evaluate(resolved, Array.Empty<AgentPullRequestState>(), now, options.StaleAfter,
            diagnostics: diagnostics.Select(pair => new AgentSourceDiagnostic {
                Provider = pair.Key.Substring(0, pair.Key.IndexOf(':')), Code = pair.Key.Substring(pair.Key.IndexOf(':') + 1), Count = pair.Value
            }).ToArray()) with { Scan = new AgentWorkScanStatistics {
                FilesRead = scans.Sum(static scan => scan.FilesRead), FilesReused = scans.Sum(static scan => scan.FilesReused),
                FilesSkipped = scans.Sum(static scan => scan.FilesSkipped), FilesTailed = scans.Sum(static scan => scan.FilesTailed), BytesRead = scans.Sum(static scan => scan.BytesRead)
            } };
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

    private static bool IsSubagentPath(string path) => path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
        .Any(static part => part == "subagents") || Path.GetFileName(path).StartsWith("agent-", StringComparison.Ordinal);

    private static void ReadCodexTitles(SourceRootRecord root, List<AgentSessionState> sessions, int firstSession,
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
            Provider = "codex", Id = title.Key, Machine = machine, Title = title.Value, SourceRootId = root.Id
        });
    }
}
