using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Telemetry.Usage.Claude;
using IntelligenceX.Telemetry.Usage.Codex;

namespace IntelligenceX.Telemetry.Agents;

public sealed partial class AgentWorkWatcher {
    private void RefreshDiscoveredRoots() {
        if (_discoverRoots is null) return;
        try {
            var roots = _discoverRoots().ToArray();
            lock (_gate) {
                foreach (var id in _watchers.Keys.Concat(_failedWatchers).Distinct(StringComparer.OrdinalIgnoreCase)
                    .Where(id => !roots.Any(root => root.Enabled && root.Id == id)).ToArray()) {
                    if (_watchers.TryGetValue(id, out var watcher)) watcher.Dispose();
                    _watchers.Remove(id);
                    _failedWatchers.Remove(id);
                    SetDiagnostic("watcher", null, id);
                }
                _roots = roots;
            }
            SetDiagnostic("discovery", null);
        } catch (Exception) { SetDiagnostic("discovery", "discovery-unavailable"); }
    }

    private void EnsureWatchers(bool rearmFailed) {
        lock (_gate) {
            if (_disposed) return;
            foreach (var root in _roots) {
                if (!root.Enabled || (root.ProviderId != "codex" && root.ProviderId != "claude") || _watchers.ContainsKey(root.Id)) continue;
                if (_failedWatchers.Contains(root.Id) && !rearmFailed) continue;
                var directory = File.Exists(root.Path) ? Path.GetDirectoryName(root.Path)! : root.Path;
                if (!Directory.Exists(directory)) continue; // A later reconcile discovers roots created after startup.
                FileSystemWatcher? pending = null;
                try {
                    var watcher = pending = _createWatcher(directory, File.Exists(root.Path) ? Path.GetFileName(root.Path) : "*");
                    watcher.IncludeSubdirectories = !File.Exists(root.Path);
                    watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size;
                    watcher.Changed += (_, e) => QueueFile(root.Id, e.FullPath, directoryChange: false);
                    watcher.Created += (_, e) => QueueFile(root.Id, e.FullPath, directoryChange: true);
                    watcher.Deleted += (_, e) => QueueFile(root.Id, e.FullPath, directoryChange: true);
                    watcher.Renamed += (_, e) => {
                        QueueFile(root.Id, e.OldFullPath, directoryChange: true);
                        QueueFile(root.Id, e.FullPath, directoryChange: true);
                    };
                    watcher.Error += (_, e) => {
                        AgentWorkMeasurement.Add("watcher-errors", 1);
                        if (e.GetException() is System.ComponentModel.Win32Exception error)
                            AgentWorkMeasurement.Add("watcher-error-code-" + error.NativeErrorCode, 1);
                        ResetWatcher(root.Id, watcher, e.GetException() is InternalBufferOverflowException);
                    };
                    _watchers[root.Id] = watcher;
                    watcher.EnableRaisingEvents = true;
                    // Unsupported filesystems can raise Error synchronously from this setter.
                    // That callback has already disposed/revoked the registration; do not clear
                    // its failure or turn a successful setter return into an immediate retry.
                    if (!_watchers.TryGetValue(root.Id, out var registered) || !ReferenceEquals(registered, watcher)) {
                        AgentWorkMeasurement.Add("synchronous-watch-failures", 1);
                        pending = null;
                        continue;
                    }
                    pending = null;
                    _failedWatchers.Remove(root.Id);
                    SetDiagnostic("watcher", null, root.Id);
                } catch (IOException) { Failed();
                } catch (UnauthorizedAccessException) { Failed();
                } catch (ArgumentException) { Failed();
                } catch (System.ComponentModel.Win32Exception) { Failed();
                } catch (NotSupportedException) { Failed();
                } finally {
                    if (pending is not null) { _watchers.Remove(root.Id); pending.Dispose(); }
                }
                void Failed() { _failedWatchers.Add(root.Id); SetDiagnostic("watcher", "watch-unavailable", root.Id); }
            }
        }
    }

    private void ResetWatcher(string rootId, FileSystemWatcher source, bool overflow) {
        lock (_gate) {
            if (_disposed || !_watchers.TryGetValue(rootId, out var watcher) || !ReferenceEquals(watcher, source)) return;
            _watchers.Remove(rootId);
            watcher.Dispose();
            _failedWatchers.Add(rootId);
        }
        Interlocked.Exchange(ref _reconcileRequested, 2);
        SetDiagnostic("watcher", overflow ? "watch-overflow" : "watch-unavailable", rootId);
    }

    private void QueueFile(string rootId, string path, bool directoryChange) {
        var jsonl = string.Equals(Path.GetExtension(path), ".jsonl", StringComparison.OrdinalIgnoreCase);
        // Incoming directories can contain existing logs. Deletion/move-out of directories
        // is repaired by reconciliation; a deleted WAL/lock must not look like a directory.
        if (!jsonl && (!directoryChange || !Directory.Exists(path))) return;
        lock (_gate) {
            if (_disposed) return;
            var root = _roots.FirstOrDefault(root => root.Id == rootId);
            if (root is null || !root.Enabled) return;
            if (!jsonl && !(root.ProviderId == "codex"
                ? CodexSessionImportSupport.IsCandidateDirectory(root.Path, path)
                : !LocalAgentWorkStateService.IsSubagentPath(path) && ClaudeSessionImportSupport.IsCandidateDirectory(root.Path, path))) {
                AgentWorkMeasurement.Add("unrelated-directories-ignored", 1);
                return;
            }
            if (!jsonl || _changedFiles.Count >= 4096) {
                AgentWorkMeasurement.Add("directory-reconcile-requests", 1);
                Interlocked.Exchange(ref _reconcileRequested, 2);
            }
            else _changedFiles[rootId + "\0" + path] = (rootId, path);
        }
        Signal();
    }

    private async Task RunMetadataAsync() {
        if (_readMetadata is null) return;
        try {
            while (true) {
                try {
                    var metadata = await _readMetadata(_stop.Token).ConfigureAwait(false);
                    lock (_gate) { _metadata = metadata.ToArray(); _metadataCheckedAtUtc = DateTimeOffset.UtcNow; SetDiagnostic("codex", null); }
                    Signal();
                } catch (OperationCanceledException) when (_stop.IsCancellationRequested) { throw;
                } catch (Exception) { SetDiagnostic("codex", "unreadable-metadata"); }
                await DelayAsync(_options.MetadataRefreshInterval, _stop.Token).ConfigureAwait(false);
            }
        } catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    private async Task RunGitHubAsync() {
        if (_github is null) return;
        try {
            while (true) {
                try {
                    var prs = await _github.FetchAgentPullRequestsAsync(_login, _stop.Token).ConfigureAwait(false);
                    lock (_gate) { _pullRequests = prs; _gitHubCheckedAtUtc = DateTimeOffset.UtcNow; SetDiagnostic("github", null); }
                    Signal();
                } catch (OperationCanceledException) when (_stop.IsCancellationRequested) { throw;
                } catch (Exception) { SetDiagnostic("github", "request-failed"); }
                await DelayAsync(_options.GitHubRefreshInterval, _stop.Token).ConfigureAwait(false);
            }
        } catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }
}
