using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IntelligenceX.Telemetry.Agents;

public sealed partial class AgentWorkWatcher {
    private void RefreshDiscoveredRoots() {
        if (_discoverRoots is null) return;
        try {
            var roots = _discoverRoots().ToArray();
            lock (_gate) {
                foreach (var id in _watchers.Keys.Where(id => !roots.Any(root => root.Id == id)).ToArray()) {
                    _watchers[id].Dispose();
                    _watchers.Remove(id);
                    SetDiagnostic("watcher", null, id);
                }
                _roots = roots;
            }
            SetDiagnostic("discovery", null);
        } catch (Exception) { SetDiagnostic("discovery", "discovery-unavailable"); }
    }

    private void EnsureWatchers() {
        lock (_gate) {
            if (_disposed) return;
            foreach (var root in _roots) {
                if (!root.Enabled || (root.ProviderId != "codex" && root.ProviderId != "claude") || _watchers.ContainsKey(root.Id)) continue;
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
                    watcher.Error += (_, _) => ResetWatcher(root.Id);
                    _watchers[root.Id] = watcher;
                    watcher.EnableRaisingEvents = true;
                    pending = null;
                    SetDiagnostic("watcher", null, root.Id);
                } catch (IOException) { SetDiagnostic("watcher", "watch-unavailable", root.Id);
                } catch (UnauthorizedAccessException) { SetDiagnostic("watcher", "watch-unavailable", root.Id);
                } catch (ArgumentException) { SetDiagnostic("watcher", "watch-unavailable", root.Id);
                } catch (System.ComponentModel.Win32Exception) { SetDiagnostic("watcher", "watch-unavailable", root.Id);
                } catch (NotSupportedException) { SetDiagnostic("watcher", "watch-unavailable", root.Id);
                } finally {
                    if (pending is not null) { _watchers.Remove(root.Id); pending.Dispose(); }
                }
            }
        }
    }

    private void ResetWatcher(string rootId) {
        lock (_gate) {
            if (_watchers.TryGetValue(rootId, out var watcher)) { _watchers.Remove(rootId); watcher.Dispose(); }
        }
        Interlocked.Exchange(ref _reconcileRequested, 1);
        SetDiagnostic("watcher", "watch-overflow", rootId);
    }

    private void QueueFile(string rootId, string path, bool directoryChange) {
        var jsonl = string.Equals(Path.GetExtension(path), ".jsonl", StringComparison.OrdinalIgnoreCase);
        // Incoming directories can contain existing logs. Deletion/move-out of directories
        // is repaired by reconciliation; a deleted WAL/lock must not look like a directory.
        if (!jsonl && (!directoryChange || !Directory.Exists(path))) return;
        lock (_gate) {
            if (_disposed) return;
            if (!jsonl || _changedFiles.Count >= 4096) Interlocked.Exchange(ref _reconcileRequested, 1);
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
                    lock (_gate) { _metadata = metadata; SetDiagnostic("codex", null); }
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
