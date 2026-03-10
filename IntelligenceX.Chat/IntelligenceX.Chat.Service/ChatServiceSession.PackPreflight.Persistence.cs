using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private const int PackPreflightStoreVersion = 1;
    private static readonly object PackPreflightStoreLock = new();
    private static readonly JsonSerializerOptions PackPreflightStoreJsonOptions = new() {
        WriteIndented = false,
        PropertyNameCaseInsensitive = false
    };

    private sealed class PackPreflightStoreDto {
        public int Version { get; set; } = PackPreflightStoreVersion;
        public Dictionary<string, PackPreflightStoreEntryDto> Threads { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed class PackPreflightStoreEntryDto {
        public string[] ToolNames { get; set; } = Array.Empty<string>();
        public long SeenUtcTicks { get; set; }
    }

    private static string ResolveDefaultPackPreflightStorePath() {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root)) {
            root = ".";
        }

        return Path.Combine(root, "IntelligenceX.Chat", "pack-preflight.json");
    }

    private string ResolvePackPreflightStorePath() {
        var pendingActionsPath = ResolvePendingActionsStorePath();
        var directory = Path.GetDirectoryName(pendingActionsPath);
        if (!string.IsNullOrWhiteSpace(directory)) {
            return Path.Combine(directory, "pack-preflight.json");
        }

        return ResolveDefaultPackPreflightStorePath();
    }

    private void PersistPackPreflightSnapshot(string threadId, IReadOnlyCollection<string> toolNames, long seenUtcTicks) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0 || seenUtcTicks <= 0) {
            return;
        }

        var normalizedToolNames = NormalizePackPreflightToolNames(toolNames);
        if (normalizedToolNames.Length == 0) {
            RemovePackPreflightSnapshot(normalizedThreadId);
            return;
        }

        var path = ResolvePackPreflightStorePath();
        lock (PackPreflightStoreLock) {
            var store = ReadPackPreflightStoreNoThrow(path);
            store.Threads[normalizedThreadId] = new PackPreflightStoreEntryDto {
                ToolNames = normalizedToolNames,
                SeenUtcTicks = seenUtcTicks
            };
            PrunePackPreflightStore(store);
            WritePackPreflightStoreNoThrow(path, store);
        }
    }

    private void RemovePackPreflightSnapshot(string threadId) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return;
        }

        var path = ResolvePackPreflightStorePath();
        lock (PackPreflightStoreLock) {
            var store = ReadPackPreflightStoreNoThrow(path);
            if (!store.Threads.Remove(normalizedThreadId)) {
                return;
            }

            WritePackPreflightStoreNoThrow(path, store);
        }
    }

    private void ClearPackPreflightSnapshots() {
        var path = ResolvePackPreflightStorePath();
        lock (PackPreflightStoreLock) {
            try {
                if (File.Exists(path)) {
                    File.Delete(path);
                }
            } catch (Exception ex) {
                Trace.TraceWarning($"Pack preflight store clear failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    private bool TryLoadPackPreflightSnapshot(string threadId, out string[] toolNames, out long seenUtcTicks) {
        toolNames = Array.Empty<string>();
        seenUtcTicks = 0;

        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return false;
        }

        var path = ResolvePackPreflightStorePath();
        lock (PackPreflightStoreLock) {
            var store = ReadPackPreflightStoreNoThrow(path);
            if (!store.Threads.TryGetValue(normalizedThreadId, out var entry) || entry is null) {
                return false;
            }

            var normalizedToolNames = NormalizePackPreflightToolNames(entry.ToolNames);
            if (normalizedToolNames.Length == 0) {
                store.Threads.Remove(normalizedThreadId);
                WritePackPreflightStoreNoThrow(path, store);
                return false;
            }

            if (!TryGetUtcDateTimeFromTicks(entry.SeenUtcTicks, out var seenUtc)) {
                store.Threads.Remove(normalizedThreadId);
                WritePackPreflightStoreNoThrow(path, store);
                return false;
            }

            var nowUtc = DateTime.UtcNow;
            if (seenUtc > nowUtc || nowUtc - seenUtc > PackPreflightContextMaxAge) {
                store.Threads.Remove(normalizedThreadId);
                WritePackPreflightStoreNoThrow(path, store);
                return false;
            }

            toolNames = normalizedToolNames;
            seenUtcTicks = entry.SeenUtcTicks;
            return true;
        }
    }

    private static string[] NormalizePackPreflightToolNames(IEnumerable<string>? toolNames) {
        return NormalizeDistinctStrings(
            (toolNames ?? Array.Empty<string>())
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Select(static name => name.Trim()),
            MaxRememberedPackPreflightToolNames);
    }

    private static PackPreflightStoreDto ReadPackPreflightStoreNoThrow(string path) {
        try {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) {
                return new PackPreflightStoreDto();
            }

            var info = new FileInfo(path);
            if (info.Length <= 0 || info.Length > 512 * 1024) {
                return new PackPreflightStoreDto();
            }

            var json = File.ReadAllText(path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json)) {
                return new PackPreflightStoreDto();
            }

            var store = JsonSerializer.Deserialize<PackPreflightStoreDto>(json, PackPreflightStoreJsonOptions);
            if (store is null || store.Version != PackPreflightStoreVersion || store.Threads is null) {
                return new PackPreflightStoreDto();
            }

            if (store.Threads.Comparer != StringComparer.Ordinal) {
                store.Threads = new Dictionary<string, PackPreflightStoreEntryDto>(store.Threads, StringComparer.Ordinal);
            }

            return store;
        } catch (Exception ex) {
            Trace.TraceWarning($"Pack preflight store read failed: {ex.GetType().Name}: {ex.Message}");
            return new PackPreflightStoreDto();
        }
    }

    private static void WritePackPreflightStoreNoThrow(string path, PackPreflightStoreDto store) {
        string? tmp = null;
        try {
            if (string.IsNullOrWhiteSpace(path)) {
                return;
            }

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory)) {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(store, PackPreflightStoreJsonOptions);
            var fileName = Path.GetFileName(path);
            var tmpName = $"{fileName}.{Guid.NewGuid():N}.tmp";
            tmp = string.IsNullOrWhiteSpace(directory) ? tmpName : Path.Combine(directory!, tmpName);

            using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                TryHardenPendingActionsStoreAclNoThrow(tmp);
                using var writer = new StreamWriter(fs, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                writer.Write(json);
                writer.Flush();
                fs.Flush(true);
            }

            if (File.Exists(path)) {
                File.Replace(tmp, path, null, ignoreMetadataErrors: true);
            } else {
                File.Move(tmp, path);
            }

            TryHardenPendingActionsStoreAclNoThrow(path);
        } catch (Exception ex) {
            Trace.TraceWarning($"Pack preflight store write failed: {ex.GetType().Name}: {ex.Message}");
        } finally {
            if (!string.IsNullOrWhiteSpace(tmp) && File.Exists(tmp)) {
                try {
                    File.Delete(tmp);
                } catch {
                    // Best effort only.
                }
            }
        }
    }

    private static void PrunePackPreflightStore(PackPreflightStoreDto store) {
        if (store.Threads.Count <= MaxTrackedPackPreflightContexts) {
            return;
        }

        var removeCount = store.Threads.Count - MaxTrackedPackPreflightContexts;
        if (removeCount <= 0) {
            return;
        }

        var toRemove = store.Threads
            .Select(pair => (ThreadId: pair.Key, Ticks: pair.Value?.SeenUtcTicks ?? 0))
            .OrderBy(static item => item.Ticks)
            .ThenBy(static item => item.ThreadId, StringComparer.Ordinal)
            .Take(removeCount)
            .Select(static item => item.ThreadId)
            .ToArray();
        foreach (var threadId in toRemove) {
            store.Threads.Remove(threadId);
        }
    }
}
