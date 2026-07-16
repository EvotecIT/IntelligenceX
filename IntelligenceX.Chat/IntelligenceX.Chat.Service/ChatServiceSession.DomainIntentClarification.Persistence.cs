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
    private const int DomainIntentClarificationStoreVersion = 1;
    private static readonly object DomainIntentClarificationStoreLock = new();

    private sealed class DomainIntentClarificationStoreDto {
        public int Version { get; set; } = DomainIntentClarificationStoreVersion;
        public Dictionary<string, DomainIntentClarificationStoreEntryDto> Threads { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed class DomainIntentClarificationStoreEntryDto {
        public long SeenUtcTicks { get; set; }
        public string[] Families { get; set; } = Array.Empty<string>();
    }

    private static string ResolveDefaultDomainIntentClarificationStorePath() =>
        ChatServiceJsonFileStore.ResolveDefaultPath("domain-intent-clarifications.json");

    private string ResolveDomainIntentClarificationStorePath() =>
        ChatServiceJsonFileStore.ResolveSiblingPath(ResolvePendingActionsStorePath(), "domain-intent-clarifications.json");

    private void PersistPendingDomainIntentClarificationSnapshot(string threadId, long seenUtcTicks, IReadOnlyList<string>? families) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0 || seenUtcTicks <= 0) {
            return;
        }

        var path = ResolveDomainIntentClarificationStorePath();
        lock (DomainIntentClarificationStoreLock) {
            var store = ReadDomainIntentClarificationStoreNoThrow(path);
            store.Threads[normalizedThreadId] = new DomainIntentClarificationStoreEntryDto {
                SeenUtcTicks = seenUtcTicks,
                Families = NormalizePendingDomainIntentClarificationFamilies(families)
            };
            PruneDomainIntentClarificationStore(store);
            WriteDomainIntentClarificationStoreNoThrow(path, store);
        }
    }

    private void RemovePendingDomainIntentClarificationSnapshot(string threadId) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return;
        }

        var path = ResolveDomainIntentClarificationStorePath();
        lock (DomainIntentClarificationStoreLock) {
            var store = ReadDomainIntentClarificationStoreNoThrow(path);
            if (!store.Threads.Remove(normalizedThreadId)) {
                return;
            }

            WriteDomainIntentClarificationStoreNoThrow(path, store);
        }
    }

    private void ClearPendingDomainIntentClarificationSnapshots() {
        var path = ResolveDomainIntentClarificationStorePath();
        lock (DomainIntentClarificationStoreLock) {
            ChatServiceJsonFileStore.Delete(path, "Domain intent clarification store");
        }
    }

    private bool TryLoadPendingDomainIntentClarificationSnapshot(string threadId, out long seenUtcTicks, out string[] families) {
        seenUtcTicks = 0;
        families = Array.Empty<string>();
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return false;
        }

        var path = ResolveDomainIntentClarificationStorePath();
        lock (DomainIntentClarificationStoreLock) {
            var store = ReadDomainIntentClarificationStoreNoThrow(path);
            if (!store.Threads.TryGetValue(normalizedThreadId, out var entry) || entry is null) {
                return false;
            }

            if (!TryGetUtcDateTimeFromTicks(entry.SeenUtcTicks, out var seenUtc)) {
                store.Threads.Remove(normalizedThreadId);
                WriteDomainIntentClarificationStoreNoThrow(path, store);
                return false;
            }

            var now = DateTime.UtcNow;
            if (seenUtc > now || now - seenUtc > DomainIntentClarificationContextMaxAge) {
                store.Threads.Remove(normalizedThreadId);
                WriteDomainIntentClarificationStoreNoThrow(path, store);
                return false;
            }

            seenUtcTicks = entry.SeenUtcTicks;
            families = NormalizePendingDomainIntentClarificationFamilies(entry.Families);
            return true;
        }
    }

    private static string[] NormalizePendingDomainIntentClarificationFamilies(IReadOnlyList<string>? families) {
        if (families is not { Count: > 0 }) {
            return Array.Empty<string>();
        }

        var normalized = new List<string>(families.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < families.Count; i++) {
            if (!TryNormalizeDomainIntentFamily(families[i], out var family) || !seen.Add(family)) {
                continue;
            }

            normalized.Add(family);
        }

        if (normalized.Count == 0) {
            return Array.Empty<string>();
        }

        normalized.Sort(StringComparer.Ordinal);
        return normalized.ToArray();
    }

    private static DomainIntentClarificationStoreDto ReadDomainIntentClarificationStoreNoThrow(string path) {
        return ChatServiceJsonFileStore.ReadOrCreate(
            path,
            maximumBytes: 256 * 1024,
            static json => JsonSerializer.Deserialize<DomainIntentClarificationStoreDto>(json),
            static store => store.Version == DomainIntentClarificationStoreVersion && store.Threads is not null,
            static store => {
                if (store.Threads.Comparer != StringComparer.Ordinal) {
                    store.Threads = new Dictionary<string, DomainIntentClarificationStoreEntryDto>(store.Threads, StringComparer.Ordinal);
                }
            },
            static () => new DomainIntentClarificationStoreDto(),
            "Domain intent clarification store");
    }

    private static void WriteDomainIntentClarificationStoreNoThrow(string path, DomainIntentClarificationStoreDto store) {
        ChatServiceJsonFileStore.Write(
            path,
            store,
            static value => JsonSerializer.Serialize(value),
            "Domain intent clarification store");
    }

    private static void PruneDomainIntentClarificationStore(DomainIntentClarificationStoreDto store) {
        if (store.Threads.Count <= MaxTrackedDomainIntentClarificationContexts) {
            return;
        }

        var removeCount = store.Threads.Count - MaxTrackedDomainIntentClarificationContexts;
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
