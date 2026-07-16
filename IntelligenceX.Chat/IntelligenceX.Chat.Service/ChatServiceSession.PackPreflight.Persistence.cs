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
    private const int PackPreflightStoreVersion = 1;
    private static readonly object PackPreflightStoreLock = new();

    private sealed class PackPreflightStoreDto {
        public int Version { get; set; } = PackPreflightStoreVersion;
        public Dictionary<string, PackPreflightStoreEntryDto> Threads { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed class PackPreflightStoreEntryDto {
        public string[] ToolNames { get; set; } = Array.Empty<string>();
        public long SeenUtcTicks { get; set; }
    }

    private static string ResolveDefaultPackPreflightStorePath() =>
        ChatServiceJsonFileStore.ResolveDefaultPath("pack-preflight.json");

    private string ResolvePackPreflightStorePath() =>
        ChatServiceJsonFileStore.ResolveSiblingPath(ResolvePendingActionsStorePath(), "pack-preflight.json");

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
            ChatServiceJsonFileStore.Delete(path, "Pack preflight store");
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
        return ChatServiceJsonFileStore.ReadOrCreate(
            path,
            maximumBytes: 512 * 1024,
            static json => JsonSerializer.Deserialize<PackPreflightStoreDto>(json),
            static store => store.Version == PackPreflightStoreVersion && store.Threads is not null,
            static store => {
                if (store.Threads.Comparer != StringComparer.Ordinal) {
                    store.Threads = new Dictionary<string, PackPreflightStoreEntryDto>(store.Threads, StringComparer.Ordinal);
                }
            },
            static () => new PackPreflightStoreDto(),
            "Pack preflight store");
    }

    private static void WritePackPreflightStoreNoThrow(string path, PackPreflightStoreDto store) {
        ChatServiceJsonFileStore.Write(
            path,
            store,
            static value => JsonSerializer.Serialize(value),
            "Pack preflight store");
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
