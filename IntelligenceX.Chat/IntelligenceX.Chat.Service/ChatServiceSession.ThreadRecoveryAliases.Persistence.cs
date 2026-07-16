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
    private const int ThreadRecoveryAliasStoreVersion = 1;
    private static readonly object ThreadRecoveryAliasStoreLock = new();

    private sealed class ThreadRecoveryAliasStoreDto {
        public int Version { get; set; } = ThreadRecoveryAliasStoreVersion;
        public Dictionary<string, ThreadRecoveryAliasStoreEntryDto> Threads { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed class ThreadRecoveryAliasStoreEntryDto {
        public string RecoveredThreadId { get; set; } = string.Empty;
        public long SeenUtcTicks { get; set; }
    }

    private static string ResolveDefaultThreadRecoveryAliasStorePath() =>
        ChatServiceJsonFileStore.ResolveDefaultPath("thread-recovery-aliases.json");

    private string ResolveThreadRecoveryAliasStorePath() =>
        ChatServiceJsonFileStore.ResolveSiblingPath(ResolvePendingActionsStorePath(), "thread-recovery-aliases.json");

    private void PersistRecoveredThreadAliasSnapshot(string originalThreadId, string recoveredThreadId, long seenUtcTicks) {
        var normalizedOriginal = (originalThreadId ?? string.Empty).Trim();
        var normalizedRecovered = (recoveredThreadId ?? string.Empty).Trim();
        if (normalizedOriginal.Length == 0
            || normalizedRecovered.Length == 0
            || string.Equals(normalizedOriginal, normalizedRecovered, StringComparison.Ordinal)
            || seenUtcTicks <= 0) {
            return;
        }

        var path = ResolveThreadRecoveryAliasStorePath();
        lock (ThreadRecoveryAliasStoreLock) {
            var store = ReadThreadRecoveryAliasStoreNoThrow(path);
            store.Threads[normalizedOriginal] = new ThreadRecoveryAliasStoreEntryDto {
                RecoveredThreadId = normalizedRecovered,
                SeenUtcTicks = seenUtcTicks
            };
            PruneThreadRecoveryAliasStore(store);
            WriteThreadRecoveryAliasStoreNoThrow(path, store);
        }
    }

    private void RemoveRecoveredThreadAliasSnapshot(string originalThreadId) {
        var normalizedOriginal = (originalThreadId ?? string.Empty).Trim();
        if (normalizedOriginal.Length == 0) {
            return;
        }

        var path = ResolveThreadRecoveryAliasStorePath();
        lock (ThreadRecoveryAliasStoreLock) {
            var store = ReadThreadRecoveryAliasStoreNoThrow(path);
            if (!store.Threads.Remove(normalizedOriginal)) {
                return;
            }

            WriteThreadRecoveryAliasStoreNoThrow(path, store);
        }
    }

    private void ClearRecoveredThreadAliasSnapshots() {
        var path = ResolveThreadRecoveryAliasStorePath();
        lock (ThreadRecoveryAliasStoreLock) {
            ChatServiceJsonFileStore.Delete(path, "Thread-recovery alias store");
        }
    }

    private bool TryLoadRecoveredThreadAliasSnapshot(string originalThreadId, out string recoveredThreadId, out long seenUtcTicks) {
        recoveredThreadId = string.Empty;
        seenUtcTicks = 0;

        var normalizedOriginal = (originalThreadId ?? string.Empty).Trim();
        if (normalizedOriginal.Length == 0) {
            return false;
        }

        var path = ResolveThreadRecoveryAliasStorePath();
        lock (ThreadRecoveryAliasStoreLock) {
            var store = ReadThreadRecoveryAliasStoreNoThrow(path);
            if (!store.Threads.TryGetValue(normalizedOriginal, out var entry) || entry is null) {
                return false;
            }

            var normalizedRecovered = (entry.RecoveredThreadId ?? string.Empty).Trim();
            if (normalizedRecovered.Length == 0 || string.Equals(normalizedRecovered, normalizedOriginal, StringComparison.Ordinal)) {
                store.Threads.Remove(normalizedOriginal);
                WriteThreadRecoveryAliasStoreNoThrow(path, store);
                return false;
            }

            if (!TryGetUtcDateTimeFromTicks(entry.SeenUtcTicks, out var seenUtc)) {
                store.Threads.Remove(normalizedOriginal);
                WriteThreadRecoveryAliasStoreNoThrow(path, store);
                return false;
            }

            var now = DateTime.UtcNow;
            if (seenUtc > now || now - seenUtc > ThreadRecoveryAliasContextMaxAge) {
                store.Threads.Remove(normalizedOriginal);
                WriteThreadRecoveryAliasStoreNoThrow(path, store);
                return false;
            }

            recoveredThreadId = normalizedRecovered;
            seenUtcTicks = entry.SeenUtcTicks;
            return true;
        }
    }

    private static ThreadRecoveryAliasStoreDto ReadThreadRecoveryAliasStoreNoThrow(string path) {
        return ChatServiceJsonFileStore.ReadOrCreate(
            path,
            maximumBytes: 1024 * 1024,
            static json => JsonSerializer.Deserialize<ThreadRecoveryAliasStoreDto>(json),
            static store => store.Version == ThreadRecoveryAliasStoreVersion && store.Threads is not null,
            static store => {
                if (store.Threads.Comparer != StringComparer.Ordinal) {
                    store.Threads = new Dictionary<string, ThreadRecoveryAliasStoreEntryDto>(store.Threads, StringComparer.Ordinal);
                }
            },
            static () => new ThreadRecoveryAliasStoreDto(),
            "Thread-recovery alias store");
    }

    private static void WriteThreadRecoveryAliasStoreNoThrow(string path, ThreadRecoveryAliasStoreDto store) {
        ChatServiceJsonFileStore.Write(
            path,
            store,
            static value => JsonSerializer.Serialize(value),
            "Thread-recovery alias store");
    }

    private static void PruneThreadRecoveryAliasStore(ThreadRecoveryAliasStoreDto store) {
        if (store.Threads.Count <= MaxTrackedThreadRecoveryAliases) {
            return;
        }

        var removeCount = store.Threads.Count - MaxTrackedThreadRecoveryAliases;
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
