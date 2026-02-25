using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private const int WeightedSubsetStoreVersion = 1;
    private static readonly object WeightedSubsetStoreLock = new();
    private static readonly JsonSerializerOptions WeightedSubsetStoreJsonOptions = new() {
        WriteIndented = false,
        PropertyNameCaseInsensitive = false
    };

    private sealed class WeightedSubsetStoreDto {
        public int Version { get; set; } = WeightedSubsetStoreVersion;
        public Dictionary<string, WeightedSubsetStoreEntryDto> Threads { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed class WeightedSubsetStoreEntryDto {
        public long SeenUtcTicks { get; set; }
        public string[] ToolNames { get; set; } = Array.Empty<string>();
    }

    private static string ResolveDefaultWeightedSubsetStorePath() {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root)) {
            root = ".";
        }

        return Path.Combine(root, "IntelligenceX.Chat", "weighted-tool-subsets.json");
    }

    private string ResolveWeightedSubsetStorePath() {
        var pendingActionsPath = ResolvePendingActionsStorePath();
        var directory = Path.GetDirectoryName(pendingActionsPath);
        if (!string.IsNullOrWhiteSpace(directory)) {
            return Path.Combine(directory, "weighted-tool-subsets.json");
        }

        return ResolveDefaultWeightedSubsetStorePath();
    }

    private void PersistWeightedToolSubsetSnapshot(string threadId, long seenUtcTicks, IReadOnlyList<string> toolNames) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0 || seenUtcTicks <= 0 || toolNames is null || toolNames.Count == 0) {
            return;
        }

        var normalizedToolNames = toolNames
            .Select(static name => (name ?? string.Empty).Trim())
            .Where(static name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(64)
            .ToArray();
        if (normalizedToolNames.Length == 0) {
            return;
        }

        var path = ResolveWeightedSubsetStorePath();
        lock (WeightedSubsetStoreLock) {
            var store = ReadWeightedSubsetStoreNoThrow(path);
            store.Threads[normalizedThreadId] = new WeightedSubsetStoreEntryDto {
                SeenUtcTicks = seenUtcTicks,
                ToolNames = normalizedToolNames
            };
            PruneWeightedSubsetStore(store);
            WriteWeightedSubsetStoreNoThrow(path, store);
        }
    }

    private void RemoveWeightedToolSubsetSnapshot(string threadId) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return;
        }

        var path = ResolveWeightedSubsetStorePath();
        lock (WeightedSubsetStoreLock) {
            var store = ReadWeightedSubsetStoreNoThrow(path);
            if (!store.Threads.Remove(normalizedThreadId)) {
                return;
            }

            WriteWeightedSubsetStoreNoThrow(path, store);
        }
    }

    private void ClearWeightedToolSubsetSnapshots() {
        var path = ResolveWeightedSubsetStorePath();
        lock (WeightedSubsetStoreLock) {
            try {
                if (File.Exists(path)) {
                    File.Delete(path);
                }
            } catch (Exception ex) {
                Trace.TraceWarning($"Weighted subset store clear failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    private bool TryLoadWeightedToolSubsetSnapshot(string threadId, out long seenUtcTicks, out string[] toolNames) {
        seenUtcTicks = 0;
        toolNames = Array.Empty<string>();

        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return false;
        }

        var path = ResolveWeightedSubsetStorePath();
        lock (WeightedSubsetStoreLock) {
            var store = ReadWeightedSubsetStoreNoThrow(path);
            if (!store.Threads.TryGetValue(normalizedThreadId, out var entry) || entry is null) {
                return false;
            }

            if (!TryGetUtcDateTimeFromTicks(entry.SeenUtcTicks, out var seenUtc)) {
                store.Threads.Remove(normalizedThreadId);
                WriteWeightedSubsetStoreNoThrow(path, store);
                return false;
            }

            var now = DateTime.UtcNow;
            if (seenUtc > now || now - seenUtc > UserIntentContextMaxAge) {
                store.Threads.Remove(normalizedThreadId);
                WriteWeightedSubsetStoreNoThrow(path, store);
                return false;
            }

            var normalizedToolNames = (entry.ToolNames ?? Array.Empty<string>())
                .Select(static name => (name ?? string.Empty).Trim())
                .Where(static name => name.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(64)
                .ToArray();
            if (normalizedToolNames.Length == 0) {
                store.Threads.Remove(normalizedThreadId);
                WriteWeightedSubsetStoreNoThrow(path, store);
                return false;
            }

            seenUtcTicks = entry.SeenUtcTicks;
            toolNames = normalizedToolNames;
            return true;
        }
    }

    private static WeightedSubsetStoreDto ReadWeightedSubsetStoreNoThrow(string path) {
        try {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) {
                return new WeightedSubsetStoreDto();
            }

            var info = new FileInfo(path);
            if (info.Length <= 0 || info.Length > 1024 * 1024) {
                return new WeightedSubsetStoreDto();
            }

            var json = File.ReadAllText(path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json)) {
                return new WeightedSubsetStoreDto();
            }

            var store = JsonSerializer.Deserialize<WeightedSubsetStoreDto>(json, WeightedSubsetStoreJsonOptions);
            if (store is null || store.Version != WeightedSubsetStoreVersion || store.Threads is null) {
                return new WeightedSubsetStoreDto();
            }

            if (store.Threads.Comparer != StringComparer.Ordinal) {
                store.Threads = new Dictionary<string, WeightedSubsetStoreEntryDto>(store.Threads, StringComparer.Ordinal);
            }

            return store;
        } catch (Exception ex) {
            Trace.TraceWarning($"Weighted subset store read failed: {ex.GetType().Name}: {ex.Message}");
            return new WeightedSubsetStoreDto();
        }
    }

    private static void WriteWeightedSubsetStoreNoThrow(string path, WeightedSubsetStoreDto store) {
        string? tmp = null;
        try {
            if (string.IsNullOrWhiteSpace(path)) {
                return;
            }

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory)) {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(store, WeightedSubsetStoreJsonOptions);
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
            Trace.TraceWarning($"Weighted subset store write failed: {ex.GetType().Name}: {ex.Message}");
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

    private static void PruneWeightedSubsetStore(WeightedSubsetStoreDto store) {
        if (store.Threads.Count <= MaxTrackedWeightedRoutingContexts) {
            return;
        }

        var removeCount = store.Threads.Count - MaxTrackedWeightedRoutingContexts;
        if (removeCount <= 0) {
            return;
        }

        var toRemove = store.Threads
            .OrderBy(static pair => pair.Value?.SeenUtcTicks ?? 0)
            .ThenBy(static pair => pair.Key, StringComparer.Ordinal)
            .Take(removeCount)
            .Select(static pair => pair.Key)
            .ToArray();
        foreach (var threadId in toRemove) {
            store.Threads.Remove(threadId);
        }
    }
}
