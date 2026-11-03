using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using IntelligenceX.Telemetry.Usage;
using IntelligenceX.Telemetry.Usage.Claude;
using IntelligenceX.Telemetry.Usage.Codex;

namespace IntelligenceX.Telemetry.Agents;

// Single-writer collection shared by one-shot reads and the live watcher. Files retain raw
// observations: clock aging, Desktop merges and repository enrichment never poison parser state.
internal sealed class AgentLocalCollection {
    private readonly AgentWorkOptions _options;
    private readonly Dictionary<string, RootState> _roots = new(StringComparer.Ordinal);
    private readonly AgentRepositoryCache _repositories = new();
    private readonly AgentSnapshotIndex? _index;

    internal AgentLocalCollection(AgentWorkOptions options, bool incremental = false) {
        _options = options;
        if (incremental) _index = new AgentSnapshotIndex(options);
    }

    internal void Reconcile(SourceRootRecord[] roots, DateTimeOffset? now, CancellationToken token, bool forceMembership = false) {
        using var measurement = AgentWorkMeasurement.Begin("reconcile");
        foreach (var id in _roots.Keys.Where(id => !roots.Any(root => root.Enabled && root.Id == id)).ToArray()) _roots.Remove(id);
        foreach (var root in roots) {
            token.ThrowIfCancellationRequested();
            if (!root.Enabled || (root.ProviderId != "codex" && root.ProviderId != "claude")) continue;
            if (!_roots.TryGetValue(root.Id, out var state) || state.Root.Path != root.Path || state.Root.ProviderId != root.ProviderId
                || state.Root.SourceKind != root.SourceKind || state.Root.MachineLabel != root.MachineLabel || state.Root.PlatformHint != root.PlatformHint) {
                state = new RootState(root, _options.Cache);
                _roots[root.Id] = state;
            }
            state.Scan.ResetStatistics();
            if (forceMembership) state.Directories.Clear();
            state.Directories.BeginScan();
            state.RootDiagnostics.Clear();
            if (!Directory.Exists(root.Path) && !File.Exists(root.Path)) {
                state.Clear();
                state.Report("missing-source");
                continue;
            }
            try {
                var previousListings = state.Directories.Listings;
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var paths = root.ProviderId == "codex"
                    ? CodexSessionImportSupport.EnumerateCandidateFiles(root.Path, preferRecentArtifacts: false, orderByActivity: false, directoryCache: state.Directories)
                    : ClaudeSessionImportSupport.EnumerateCandidateFiles(root.Path, preferRecentArtifacts: false, directoryCache: state.Directories);
                foreach (var path in paths) {
                    token.ThrowIfCancellationRequested();
                    if (root.ProviderId == "claude" && LocalAgentWorkStateService.IsSubagentPath(path)) continue;
                    seen.Add(path);
                    ReadFile(state, path, Since(now), token);
                }
                foreach (var path in state.Files.Keys.Where(path => !seen.Contains(path)).ToArray()) state.Remove(path);
                state.Directories.CompleteScan();
                AgentWorkMeasurement.Add("directories-listed", state.Directories.Listings - previousListings);
                state.Scan.Flush();
                ReadTitles(state, token);
            } catch (IOException) { state.Report("unreadable-source");
            } catch (UnauthorizedAccessException) { state.Report("unreadable-source"); }
        }
        if (_repositories.Refresh(_roots.Values.Where(static state => AgentRepositoryIdentity.CanResolveWorkspace(state.Root))
            .SelectMany(static state => state.Files.Values)
            .Where(static file => file.Session is not null).Select(static file => file.Session!)))
            foreach (var state in _roots.Values)
                foreach (var file in state.Files.Values) file.Enriched = null;
    }

    // Returns false for unknown paths: discovery belongs to reconciliation, not an alternative
    // copy of provider candidate rules. Existing files (including deletion) need no enumeration.
    internal bool Update(string rootId, string path, CancellationToken token) {
        using var measurement = AgentWorkMeasurement.Begin("event-update");
        if (!_roots.TryGetValue(rootId, out var state)) return false;
        if (state.Root.ProviderId == "codex" && string.Equals(path, Path.Combine(state.Root.Path, "session_index.jsonl"), StringComparison.OrdinalIgnoreCase)) {
            ReadTitles(state, token);
            return true;
        }
        if (!state.Files.ContainsKey(path)) {
            if (state.Root.ProviderId == "claude" && LocalAgentWorkStateService.IsSubagentPath(path)) return true;
            if (state.Root.ProviderId == "codex" && !string.Equals(path, state.Root.Path, StringComparison.OrdinalIgnoreCase)
                && !Path.GetFileName(path).StartsWith("rollout-", StringComparison.OrdinalIgnoreCase)) return true;
            if (!File.Exists(path)) return true; // A deleted unknown file needs no discovery.
            state.Directories.Invalidate(Path.GetDirectoryName(path)!);
            return false;
        }
        if (!File.Exists(path)) { state.Directories.Invalidate(Path.GetDirectoryName(path)!); state.Remove(path); return true; }
        ReadFile(state, path, Since(null), token, persist: false);
        return true;
    }

    internal void ResetStatistics() {
        foreach (var state in _roots.Values) state.Scan.ResetStatistics();
    }

    internal AgentWorkSnapshot Snapshot(IEnumerable<AgentSessionState> metadata, DateTimeOffset? nowUtc = null, TimeSpan? window = null,
        IReadOnlyList<AgentPullRequestState>? pullRequests = null, IReadOnlyList<AgentSourceDiagnostic>? additionalDiagnostics = null) {
        using var measurement = AgentWorkMeasurement.Begin("snapshot");
        var sessions = new List<AgentSessionState>();
        var diagnostics = new List<AgentSourceDiagnostic>();
        var running = new HashSet<string>(StringComparer.Ordinal);
        foreach (var state in _roots.Values) {
            if (state.Root.SourceKind == UsageSourceKind.LocalLogs) running.Add(state.Root.Id);
            var local = AgentRepositoryIdentity.CanResolveWorkspace(state.Root);
            var machine = state.Root.MachineLabel ?? (state.Root.SourceKind == UsageSourceKind.LocalLogs ? Environment.MachineName : null);
            foreach (var file in state.Files.Values) {
                if (file.Session is not { } raw) continue;
                // Machine identity belongs to the current root, not its persisted parser checkpoint.
                if (file.Enriched is null || file.Enriched.Machine != machine) {
                    var observation = raw with { Machine = machine };
                    file.Enriched = local ? _repositories.Enrich(observation) : observation;
                }
                var session = file.Enriched;
                if (state.Titles.TryGetValue(session.Id, out var title) && session.Title != title.Title) session = session with { Title = title.Title };
                sessions.Add(session);
            }
            sessions.AddRange(state.Titles.Values);
            var counts = new Dictionary<string, int>(state.RootDiagnostics, StringComparer.Ordinal);
            foreach (var pair in state.IndexDiagnostics.Diagnostics) { counts.TryGetValue(pair.Key, out var count); counts[pair.Key] = count + pair.Value; }
            if (state.Scan.CacheUnavailable) counts["cache-unavailable"] = 1;
            foreach (var file in state.Files.Values)
                foreach (var pair in file.Diagnostics) { counts.TryGetValue(pair.Key, out var count); counts[pair.Key] = count + pair.Value; }
            diagnostics.AddRange(counts.Select(pair => new AgentSourceDiagnostic { Provider = state.Root.ProviderId, SourceRootId = state.Root.Id, Code = pair.Key, Count = pair.Value }));
        }
        var now = nowUtc ?? DateTimeOffset.UtcNow; // Sample after reading, including metadata supplied by the caller.
        if (additionalDiagnostics is not null) diagnostics.AddRange(additionalDiagnostics);
        var statistics = new AgentWorkScanStatistics { FilesRead = _roots.Values.Sum(static s => s.Scan.FilesRead),
            FilesReused = _roots.Values.Sum(static s => s.Scan.FilesReused), FilesSkipped = _roots.Values.Sum(static s => s.Scan.FilesSkipped),
            FilesTailed = _roots.Values.Sum(static s => s.Scan.FilesTailed), BytesRead = _roots.Values.Sum(static s => s.Scan.BytesRead) };
        if (_index is not null) return _index.Build(sessions, metadata, running, now, pullRequests ?? Array.Empty<AgentPullRequestState>(), diagnostics, statistics);
        return LocalAgentWorkStateService.BuildSnapshot(sessions, metadata.Where(static row => row.Provider == "codex"), running,
            now, Since(now), window ?? _options.ActiveTurnWindow, _options.StaleAfter, diagnostics, pullRequests ?? Array.Empty<AgentPullRequestState>(),
            statistics);
    }

    private DateTimeOffset? Since(DateTimeOffset? now) => _options.ActiveWithin.HasValue
        ? (now ?? DateTimeOffset.UtcNow) - _options.ActiveWithin.Value : null;

    private static void ReadFile(RootState state, string path, DateTimeOffset? since, CancellationToken token, bool persist = true) {
        var file = new FileState();
        state.CurrentFile = file;
        try {
            file.Session = state.Scan.Read(path, since, token, persist);
            if (file.Session is null && state.Files.TryGetValue(path, out var known) && known.Session is { } retained
                && AgentInputRequests.IsUnresolved(retained)) file.Session = retained; // A recent-window cutoff is not an answer.
            if (state.Files.TryGetValue(path, out var previous) && previous.Session == file.Session
                && previous.Diagnostics.Count == file.Diagnostics.Count && previous.Diagnostics.All(pair => file.Diagnostics.TryGetValue(pair.Key, out var count) && count == pair.Value)) return;
            state.Files[path] = file;
        } catch (IOException) { Unavailable();
        } catch (UnauthorizedAccessException) { Unavailable();
        } finally { state.CurrentFile = null; }
        void Unavailable() {
            state.Report("unreadable-source");
            if (state.Files.TryGetValue(path, out var previous)) file.Session = previous.Session;
            state.Files[path] = file;
        }
    }

    private static void ReadTitles(RootState state, CancellationToken token) {
        if (state.Root.ProviderId != "codex" || !Directory.Exists(state.Root.Path)) return;
        state.Titles.Clear();
        state.IndexDiagnostics.Diagnostics.Clear();
        state.CurrentFile = state.IndexDiagnostics;
        var titles = new List<AgentSessionState>();
        try {
            LocalAgentWorkStateService.ReadCodexTitles(state.Root, titles, 0, (_, code) => state.Report(code), token);
            foreach (var title in titles) state.Titles[title.Id] = title;
        } catch (IOException) { state.Report("unreadable-source");
        } catch (UnauthorizedAccessException) { state.Report("unreadable-source");
        } finally { state.CurrentFile = null; }
    }

    private sealed class FileState {
        internal AgentSessionState? Session;
        internal AgentSessionState? Enriched;
        internal readonly Dictionary<string, int> Diagnostics = new(StringComparer.Ordinal);
    }

    private sealed class RootState {
        internal readonly SourceRootRecord Root;
        internal readonly UsageDirectorySnapshotCache Directories = new();
        internal readonly AgentSessionScan Scan;
        internal readonly Dictionary<string, FileState> Files = new(StringComparer.OrdinalIgnoreCase);
        internal readonly Dictionary<string, AgentSessionState> Titles = new(StringComparer.Ordinal);
        internal readonly Dictionary<string, int> RootDiagnostics = new(StringComparer.Ordinal);
        internal readonly FileState IndexDiagnostics = new();
        internal FileState? CurrentFile;
        internal RootState(SourceRootRecord root, IRawArtifactStore? cache) {
            Root = root;
            Scan = new AgentSessionScan(root, cache, Report);
        }
        internal void Report(string code) {
            var diagnostics = CurrentFile?.Diagnostics ?? RootDiagnostics;
            diagnostics.TryGetValue(code, out var count);
            diagnostics[code] = count + 1;
        }
        internal void Remove(string path) { Files.Remove(path); Scan.Forget(path); }
        internal void Clear() { foreach (var path in Files.Keys.ToArray()) Remove(path); Titles.Clear(); Directories.Clear(); IndexDiagnostics.Diagnostics.Clear(); }
    }
}
