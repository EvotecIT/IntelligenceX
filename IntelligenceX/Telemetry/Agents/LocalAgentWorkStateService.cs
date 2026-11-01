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
    /// <param name="nowUtc">Evaluation time; defaults to UTC now.</param>
    /// <param name="activeWindow">Maximum quiet interval for an inferred running turn; defaults to five minutes.</param>
    /// <param name="codexMetadata">Optional read-only Codex database metadata from the SQLite companion library.</param>
    /// <param name="cancellationToken">Cancels enumeration and parsing.</param>
    public static AgentWorkSnapshot ReadLocal(IEnumerable<SourceRootRecord>? roots = null, DateTimeOffset? nowUtc = null,
        TimeSpan? activeWindow = null, IEnumerable<AgentSessionState>? codexMetadata = null,
        CancellationToken cancellationToken = default) {
        var now = nowUtc ?? DateTimeOffset.UtcNow;
        var window = activeWindow ?? TimeSpan.FromMinutes(5);
        if (window <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(activeWindow));
        var sessions = new List<AgentSessionState>();
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
                var firstSession = sessions.Count;
                var files = root.ProviderId == "codex"
                    ? CodexSessionImportSupport.EnumerateCandidateFiles(root.Path, preferRecentArtifacts: false)
                    : ClaudeSessionImportSupport.EnumerateCandidateFiles(root.Path, preferRecentArtifacts: false);
                foreach (var path in files) {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (root.ProviderId == "claude" && IsSubagentPath(path)) continue;
                    try {
                        Action<string> report = code => Report(root.ProviderId, code);
                        var session = root.ProviderId == "codex"
                            ? CodexAgentSessionReader.Read(path, root, now, window, report, cancellationToken)
                            : ClaudeAgentSessionReader.Read(path, root, now, window, report, cancellationToken);
                        if (session is not null) sessions.Add(AgentRepositoryIdentity.CanResolveWorkspace(root)
                            ? AgentRepositoryIdentity.Enrich(session) : session);
                    } catch (IOException) { Report(root.ProviderId, "unreadable-source");
                    } catch (UnauthorizedAccessException) { Report(root.ProviderId, "unreadable-source"); }
                }
                if (root.ProviderId == "codex" && Directory.Exists(root.Path)) ReadCodexTitles(root, sessions, firstSession, Report, cancellationToken);
            } catch (IOException) { Report(root.ProviderId, "unreadable-source");
            } catch (UnauthorizedAccessException) { Report(root.ProviderId, "unreadable-source"); }
        }
        var deduped = sessions.GroupBy(Key, StringComparer.Ordinal).Select(group => group
            .OrderByDescending(static s => s.LastActivityAtUtc).ThenBy(static s => s.IsArchived).First()).ToDictionary(Key, StringComparer.Ordinal);
        foreach (var metadata in (codexMetadata ?? Array.Empty<AgentSessionState>()).GroupBy(Key, StringComparer.Ordinal)
                     .Select(static group => group.OrderByDescending(static s => s.LastActivityAtUtc).First())) {
            if (metadata.Provider != "codex") continue;
            var key = Key(metadata);
            if (!deduped.TryGetValue(key, out var log)) {
                deduped[key] = metadata;
                continue;
            }
            deduped[key] = log with {
                Title = log.Title ?? metadata.Title, WorkingDirectory = metadata.WorkingDirectory ?? log.WorkingDirectory,
                Repository = metadata.Repository ?? log.Repository, Branch = metadata.Branch ?? log.Branch,
                IsArchived = metadata.IsArchived,
                State = metadata.IsArchived ? AgentWorkState.Finished
                    : log.IsArchived && log.State == AgentWorkState.Finished ? AgentWorkState.Idle : log.State,
                StateIsInferred = !metadata.IsArchived && log.StateIsInferred,
                StartedAtUtc = log.StartedAtUtc ?? metadata.StartedAtUtc,
                LastActivityAtUtc = log.LastActivityAtUtc ?? metadata.LastActivityAtUtc
            };
        }
        return AgentWorkStateRules.Evaluate(deduped.Values, Array.Empty<AgentPullRequestState>(), now,
            diagnostics: diagnostics.Select(pair => new AgentSourceDiagnostic {
                Provider = pair.Key.Substring(0, pair.Key.IndexOf(':')), Code = pair.Key.Substring(pair.Key.IndexOf(':') + 1), Count = pair.Value
            }).ToArray());
    }

    private static string Key(AgentSessionState session) => session.Provider + "\0" + session.Machine + "\0" + session.Id;

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
            Provider = "codex", Id = title.Key, Machine = machine, Title = title.Value
        });
    }
}
