using System.IO;
using IntelligenceX.Telemetry.Usage;

namespace IntelligenceX.Tray.Services;

public sealed class UsageChangeWatcher : IDisposable {
    private readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();

    public event EventHandler<UsageChangeDetectedEventArgs>? Changed;

    public bool HasActiveWatchers {
        get {
            lock (_sync) {
                return _watchers.Count > 0;
            }
        }
    }

    public void SetRoots(IEnumerable<SourceRootRecord>? sourceRoots) {
        var activePaths = (sourceRoots ?? Array.Empty<SourceRootRecord>())
            .Where(static root => root is not null && root.Enabled && !string.IsNullOrWhiteSpace(root.Path))
            .Select(static root => UsageTelemetryIdentity.NormalizePath(root.Path))
            .Where(Directory.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        lock (_sync) {
            foreach (var stalePath in _watchers.Keys
                         .Where(path => !activePaths.Contains(path, StringComparer.OrdinalIgnoreCase))
                         .ToList()) {
                DisposeWatcher(stalePath);
            }

            foreach (var path in activePaths) {
                if (_watchers.ContainsKey(path)) {
                    continue;
                }

                var watcher = TryCreateWatcher(path);
                if (watcher is not null) {
                    _watchers[path] = watcher;
                }
            }
        }
    }

    public void Dispose() {
        lock (_sync) {
            foreach (var path in _watchers.Keys.ToList()) {
                DisposeWatcher(path);
            }
        }
    }

    private FileSystemWatcher? TryCreateWatcher(string path) {
        try {
            var watcher = new FileSystemWatcher(path) {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName
                               | NotifyFilters.DirectoryName
                               | NotifyFilters.LastWrite
                               | NotifyFilters.CreationTime
                               | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            watcher.Changed += OnChanged;
            watcher.Created += OnChanged;
            watcher.Deleted += OnChanged;
            watcher.Renamed += OnRenamed;
            watcher.Error += OnError;
            return watcher;
        } catch {
            return null;
        }
    }

    private void DisposeWatcher(string path) {
        if (!_watchers.Remove(path, out var watcher)) {
            return;
        }

        watcher.EnableRaisingEvents = false;
        watcher.Changed -= OnChanged;
        watcher.Created -= OnChanged;
        watcher.Deleted -= OnChanged;
        watcher.Renamed -= OnRenamed;
        watcher.Error -= OnError;
        watcher.Dispose();
    }

    private void OnChanged(object sender, FileSystemEventArgs e) {
        if (sender is not FileSystemWatcher watcher) {
            return;
        }

        RaiseChanged(watcher.Path, e.FullPath, e.ChangeType.ToString());
    }

    private void OnRenamed(object sender, RenamedEventArgs e) {
        if (sender is not FileSystemWatcher watcher) {
            return;
        }

        RaiseChanged(watcher.Path, e.FullPath, "Renamed");
    }

    private void OnError(object sender, ErrorEventArgs e) {
        if (sender is not FileSystemWatcher watcher) {
            return;
        }

        RaiseChanged(watcher.Path, watcher.Path, "WatcherError");
    }

    private void RaiseChanged(string rootPath, string? changedPath, string changeKind) {
        Changed?.Invoke(this, new UsageChangeDetectedEventArgs(
            rootPath,
            string.IsNullOrWhiteSpace(changedPath) ? null : changedPath,
            changeKind,
            DateTimeOffset.UtcNow));
    }
}

public sealed class UsageChangeDetectedEventArgs : EventArgs {
    public UsageChangeDetectedEventArgs(
        string rootPath,
        string? changedPath,
        string changeKind,
        DateTimeOffset changedAtUtc) {
        RootPath = rootPath;
        ChangedPath = changedPath;
        ChangeKind = string.IsNullOrWhiteSpace(changeKind) ? "Changed" : changeKind.Trim();
        ChangedAtUtc = changedAtUtc;
    }

    public string RootPath { get; }
    public string? ChangedPath { get; }
    public string ChangeKind { get; }
    public DateTimeOffset ChangedAtUtc { get; }
}
