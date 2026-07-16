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
    private const int UserIntentStoreVersion = 1;
    private static readonly object UserIntentStoreLock = new();

    private sealed class UserIntentStoreDto {
        public int Version { get; set; } = UserIntentStoreVersion;
        public Dictionary<string, UserIntentStoreEntryDto> Threads { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed class UserIntentStoreEntryDto {
        public string Intent { get; set; } = string.Empty;
        public long SeenUtcTicks { get; set; }
    }

    private static string ResolveDefaultUserIntentStorePath() =>
        ChatServiceJsonFileStore.ResolveDefaultPath("user-intents.json");

    private string ResolveUserIntentStorePath() =>
        ChatServiceJsonFileStore.ResolveSiblingPath(ResolvePendingActionsStorePath(), "user-intents.json");

    private void PersistUserIntentSnapshot(string threadId, string intent, long seenUtcTicks) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        var normalizedIntent = (intent ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0 || normalizedIntent.Length == 0 || seenUtcTicks <= 0) {
            return;
        }

        if (normalizedIntent.Length > 600) {
            normalizedIntent = normalizedIntent[..600];
        }

        var path = ResolveUserIntentStorePath();
        lock (UserIntentStoreLock) {
            var store = ReadUserIntentStoreNoThrow(path);
            store.Threads[normalizedThreadId] = new UserIntentStoreEntryDto {
                Intent = normalizedIntent,
                SeenUtcTicks = seenUtcTicks
            };
            PruneUserIntentStore(store);
            WriteUserIntentStoreNoThrow(path, store);
        }
    }

    private void RemoveUserIntentSnapshot(string threadId) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return;
        }

        var path = ResolveUserIntentStorePath();
        lock (UserIntentStoreLock) {
            var store = ReadUserIntentStoreNoThrow(path);
            if (!store.Threads.Remove(normalizedThreadId)) {
                return;
            }

            WriteUserIntentStoreNoThrow(path, store);
        }
    }

    private void ClearUserIntentSnapshots() {
        var path = ResolveUserIntentStorePath();
        lock (UserIntentStoreLock) {
            ChatServiceJsonFileStore.Delete(path, "User intent store");
        }
    }

    private bool TryLoadUserIntentSnapshot(string threadId, out string intent, out long seenUtcTicks) {
        intent = string.Empty;
        seenUtcTicks = 0;

        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return false;
        }

        var path = ResolveUserIntentStorePath();
        lock (UserIntentStoreLock) {
            var store = ReadUserIntentStoreNoThrow(path);
            if (!store.Threads.TryGetValue(normalizedThreadId, out var entry) || entry is null) {
                return false;
            }

            var normalizedIntent = (entry.Intent ?? string.Empty).Trim();
            if (normalizedIntent.Length == 0) {
                store.Threads.Remove(normalizedThreadId);
                WriteUserIntentStoreNoThrow(path, store);
                return false;
            }

            if (normalizedIntent.Length > 600) {
                normalizedIntent = normalizedIntent[..600];
            }

            if (!TryGetUtcDateTimeFromTicks(entry.SeenUtcTicks, out var seenUtc)) {
                store.Threads.Remove(normalizedThreadId);
                WriteUserIntentStoreNoThrow(path, store);
                return false;
            }

            var now = DateTime.UtcNow;
            if (seenUtc > now || now - seenUtc > UserIntentContextMaxAge) {
                store.Threads.Remove(normalizedThreadId);
                WriteUserIntentStoreNoThrow(path, store);
                return false;
            }

            intent = normalizedIntent;
            seenUtcTicks = entry.SeenUtcTicks;
            return true;
        }
    }

    private static UserIntentStoreDto ReadUserIntentStoreNoThrow(string path) {
        return ChatServiceJsonFileStore.ReadOrCreate(
            path,
            maximumBytes: 512 * 1024,
            static json => JsonSerializer.Deserialize<UserIntentStoreDto>(json),
            static store => store.Version == UserIntentStoreVersion && store.Threads is not null,
            static store => {
                if (store.Threads.Comparer != StringComparer.Ordinal) {
                    store.Threads = new Dictionary<string, UserIntentStoreEntryDto>(store.Threads, StringComparer.Ordinal);
                }
            },
            static () => new UserIntentStoreDto(),
            "User intent store");
    }

    private static void WriteUserIntentStoreNoThrow(string path, UserIntentStoreDto store) {
        ChatServiceJsonFileStore.Write(
            path,
            store,
            static value => JsonSerializer.Serialize(value),
            "User intent store");
    }

    private static void PruneUserIntentStore(UserIntentStoreDto store) {
        if (store.Threads.Count <= MaxTrackedUserIntentContexts) {
            return;
        }

        var removeCount = store.Threads.Count - MaxTrackedUserIntentContexts;
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
