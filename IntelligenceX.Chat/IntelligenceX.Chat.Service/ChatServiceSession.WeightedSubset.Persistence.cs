using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using IntelligenceX.Chat.Service.Persistence;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private const int WeightedSubsetStoreVersion = 1;
    private static readonly object WeightedSubsetStoreLock = new();

    private sealed class WeightedSubsetStoreDto {
        public int Version { get; set; } = WeightedSubsetStoreVersion;
        public Dictionary<string, WeightedSubsetStoreEntryDto> Threads { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed class WeightedSubsetStoreEntryDto {
        public long SeenUtcTicks { get; set; }
        public string[] ToolNames { get; set; } = Array.Empty<string>();
    }

    private static string ResolveDefaultWeightedSubsetStorePath() =>
        ChatServiceJsonFileStore.ResolveDefaultPath("weighted-tool-subsets.json");

    private string ResolveWeightedSubsetStorePath() =>
        ChatServiceJsonFileStore.ResolveSiblingPath(ResolvePendingActionsStorePath(), "weighted-tool-subsets.json");

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
            ChatServiceJsonFileStore.Delete(path, "Weighted subset store");
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
        return ChatServiceJsonFileStore.ReadOrCreate(
            path,
            maximumBytes: 1024 * 1024,
            static json => JsonSerializer.Deserialize<WeightedSubsetStoreDto>(json),
            static store => store.Version == WeightedSubsetStoreVersion && store.Threads is not null,
            static store => {
                if (store.Threads.Comparer != StringComparer.Ordinal) {
                    store.Threads = new Dictionary<string, WeightedSubsetStoreEntryDto>(store.Threads, StringComparer.Ordinal);
                }
            },
            static () => new WeightedSubsetStoreDto(),
            "Weighted subset store");
    }

    private static void WriteWeightedSubsetStoreNoThrow(string path, WeightedSubsetStoreDto store) {
        ChatServiceJsonFileStore.Write(
            path,
            store,
            static value => JsonSerializer.Serialize(value),
            "Weighted subset store");
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
