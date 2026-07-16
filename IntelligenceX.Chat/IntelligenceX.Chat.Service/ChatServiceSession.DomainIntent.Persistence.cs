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
    private const int DomainIntentStoreVersion = 1;
    private static readonly object DomainIntentStoreLock = new();

    private sealed class DomainIntentStoreDto {
        public int Version { get; set; } = DomainIntentStoreVersion;
        public Dictionary<string, DomainIntentStoreEntryDto> Threads { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed class DomainIntentStoreEntryDto {
        public string Family { get; set; } = string.Empty;
        public long SeenUtcTicks { get; set; }
    }

    private static string ResolveDefaultDomainIntentStorePath() =>
        ChatServiceJsonFileStore.ResolveDefaultPath("domain-intent-families.json");

    private string ResolveDomainIntentStorePath() =>
        ChatServiceJsonFileStore.ResolveSiblingPath(ResolvePendingActionsStorePath(), "domain-intent-families.json");

    private void PersistDomainIntentFamilySnapshot(string threadId, string family, long seenUtcTicks) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        var normalizedFamily = (family ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0
            || seenUtcTicks <= 0
            || !IsSupportedDomainIntentFamily(normalizedFamily)) {
            return;
        }

        var path = ResolveDomainIntentStorePath();
        lock (DomainIntentStoreLock) {
            var store = ReadDomainIntentStoreNoThrow(path);
            store.Threads[normalizedThreadId] = new DomainIntentStoreEntryDto {
                Family = normalizedFamily,
                SeenUtcTicks = seenUtcTicks
            };
            PruneDomainIntentStore(store);
            WriteDomainIntentStoreNoThrow(path, store);
        }
    }

    private void RemoveDomainIntentFamilySnapshot(string threadId) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return;
        }

        var path = ResolveDomainIntentStorePath();
        lock (DomainIntentStoreLock) {
            var store = ReadDomainIntentStoreNoThrow(path);
            if (!store.Threads.Remove(normalizedThreadId)) {
                return;
            }

            WriteDomainIntentStoreNoThrow(path, store);
        }
    }

    private void ClearDomainIntentFamilySnapshots() {
        var path = ResolveDomainIntentStorePath();
        lock (DomainIntentStoreLock) {
            ChatServiceJsonFileStore.Delete(path, "Domain intent store");
        }
    }

    private bool TryLoadDomainIntentFamilySnapshot(string threadId, out string family, out long seenUtcTicks) {
        family = string.Empty;
        seenUtcTicks = 0;

        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return false;
        }

        var path = ResolveDomainIntentStorePath();
        lock (DomainIntentStoreLock) {
            var store = ReadDomainIntentStoreNoThrow(path);
            if (!store.Threads.TryGetValue(normalizedThreadId, out var entry) || entry is null) {
                return false;
            }

            var normalizedFamily = (entry.Family ?? string.Empty).Trim();
            if (!IsSupportedDomainIntentFamily(normalizedFamily)) {
                store.Threads.Remove(normalizedThreadId);
                WriteDomainIntentStoreNoThrow(path, store);
                return false;
            }

            if (!TryGetUtcDateTimeFromTicks(entry.SeenUtcTicks, out var seenUtc)) {
                store.Threads.Remove(normalizedThreadId);
                WriteDomainIntentStoreNoThrow(path, store);
                return false;
            }

            var now = DateTime.UtcNow;
            if (seenUtc > now || now - seenUtc > DomainIntentFamilyContextMaxAge) {
                store.Threads.Remove(normalizedThreadId);
                WriteDomainIntentStoreNoThrow(path, store);
                return false;
            }

            family = normalizedFamily;
            seenUtcTicks = entry.SeenUtcTicks;
            return true;
        }
    }

    private static bool IsSupportedDomainIntentFamily(string family) {
        return TryNormalizeDomainIntentFamily(family, out _);
    }

    private static DomainIntentStoreDto ReadDomainIntentStoreNoThrow(string path) {
        return ChatServiceJsonFileStore.ReadOrCreate(
            path,
            maximumBytes: 512 * 1024,
            static json => JsonSerializer.Deserialize<DomainIntentStoreDto>(json),
            static store => store.Version == DomainIntentStoreVersion && store.Threads is not null,
            static store => {
                if (store.Threads.Comparer != StringComparer.Ordinal) {
                    store.Threads = new Dictionary<string, DomainIntentStoreEntryDto>(store.Threads, StringComparer.Ordinal);
                }
            },
            static () => new DomainIntentStoreDto(),
            "Domain intent store");
    }

    private static void WriteDomainIntentStoreNoThrow(string path, DomainIntentStoreDto store) {
        ChatServiceJsonFileStore.Write(
            path,
            store,
            static value => JsonSerializer.Serialize(value),
            "Domain intent store");
    }

    private static void PruneDomainIntentStore(DomainIntentStoreDto store) {
        if (store.Threads.Count <= MaxTrackedDomainIntentFamilyContexts) {
            return;
        }

        var removeCount = store.Threads.Count - MaxTrackedDomainIntentFamilyContexts;
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
