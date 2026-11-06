using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace IntelligenceX.Telemetry.Usage;

// Membership cache for long-lived local collectors. Directory timestamps establish membership,
// never file content: callers still check known files for missed append notifications.
internal sealed class UsageDirectorySnapshotCache {
    private static readonly TimeSpan MaximumMembershipAge = TimeSpan.FromMinutes(5);
    private DateTimeOffset? _scanAtUtc;
    private readonly Dictionary<string, Entry> _entries = new(RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly HashSet<string> _visited = new(RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly Func<string, string[]> _readFiles;
    internal int Listings { get; private set; }

    internal UsageDirectorySnapshotCache(Func<string, string[]>? readFiles = null) {
        _readFiles = readFiles ?? (static directory => Directory.GetFiles(directory, "*.jsonl", SearchOption.TopDirectoryOnly));
    }

    internal IEnumerable<string> Files(string directory, string pattern, SearchOption option) {
        var entry = Get(directory);
        foreach (var file in entry.Files) yield return file;
        if (option == SearchOption.AllDirectories)
            foreach (var child in entry.Directories)
                foreach (var file in Files(child, pattern, option)) yield return file;
    }

    internal IEnumerable<string> Directories(string directory) => Get(directory).Directories;
    internal void Invalidate(string directory) => _entries.Remove(directory);
    internal void Clear() { _entries.Clear(); _visited.Clear(); }
    internal void BeginScan(DateTimeOffset? nowUtc = null) { _visited.Clear(); _scanAtUtc = nowUtc ?? DateTimeOffset.UtcNow; }
    internal void CompleteScan() {
        foreach (var directory in _entries.Keys.Where(directory => !_visited.Contains(directory)).ToArray()) _entries.Remove(directory);
    }

    private Entry Get(string directory) {
        _visited.Add(directory);
        var info = new DirectoryInfo(directory);
        if (!info.Exists) { _entries.Remove(directory); return Entry.Empty; }
        var now = _scanAtUtc ?? DateTimeOffset.UtcNow;
        if (_entries.TryGetValue(directory, out var previous) && previous.Stamp == info.LastWriteTimeUtc
            && now >= previous.ListedAtUtc && now - previous.ListedAtUtc < MaximumMembershipAge) return previous;
        var entry = new Entry(info.LastWriteTimeUtc, now,
            _readFiles(directory).Select(Path.GetFullPath).ToArray(),
            Directory.GetDirectories(directory).Select(Path.GetFullPath).ToArray());
        // Listing exceptions propagate to the root diagnostic. Never cache a failed listing
        // as empty: retain prior membership and retry on the next reconcile.
        _entries[directory] = entry;
        Listings++;
        return entry;
    }

    private sealed record Entry(DateTime Stamp, DateTimeOffset ListedAtUtc, string[] Files, string[] Directories) {
        internal static readonly Entry Empty = new(default, default, Array.Empty<string>(), Array.Empty<string>());
    }
}
