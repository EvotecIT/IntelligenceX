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
    private const int PlannerThreadContextStoreVersion = 1;
    private static readonly object PlannerThreadContextStoreLock = new();

    private sealed class PlannerThreadContextStoreDto {
        public int Version { get; set; } = PlannerThreadContextStoreVersion;
        public Dictionary<string, PlannerThreadContextStoreEntryDto> Threads { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed class PlannerThreadContextStoreEntryDto {
        public string PlannerThreadId { get; set; } = string.Empty;
        public long SeenUtcTicks { get; set; }
    }

    private static string ResolveDefaultPlannerThreadContextStorePath() =>
        ChatServiceJsonFileStore.ResolveDefaultPath("planner-thread-contexts.json");

    private string ResolvePlannerThreadContextStorePath() =>
        ChatServiceJsonFileStore.ResolveSiblingPath(ResolvePendingActionsStorePath(), "planner-thread-contexts.json");

    private void PersistPlannerThreadContextSnapshot(string activeThreadId, string plannerThreadId, long seenUtcTicks) {
        var normalizedActiveThreadId = (activeThreadId ?? string.Empty).Trim();
        var normalizedPlannerThreadId = (plannerThreadId ?? string.Empty).Trim();
        if (normalizedActiveThreadId.Length == 0
            || normalizedPlannerThreadId.Length == 0
            || seenUtcTicks <= 0) {
            return;
        }

        var path = ResolvePlannerThreadContextStorePath();
        lock (PlannerThreadContextStoreLock) {
            var store = ReadPlannerThreadContextStoreNoThrow(path);
            store.Threads[normalizedActiveThreadId] = new PlannerThreadContextStoreEntryDto {
                PlannerThreadId = normalizedPlannerThreadId,
                SeenUtcTicks = seenUtcTicks
            };
            PrunePlannerThreadContextStore(store);
            WritePlannerThreadContextStoreNoThrow(path, store);
        }
    }

    private void RemovePlannerThreadContextSnapshot(string activeThreadId) {
        var normalizedActiveThreadId = (activeThreadId ?? string.Empty).Trim();
        if (normalizedActiveThreadId.Length == 0) {
            return;
        }

        var path = ResolvePlannerThreadContextStorePath();
        lock (PlannerThreadContextStoreLock) {
            var store = ReadPlannerThreadContextStoreNoThrow(path);
            if (!store.Threads.Remove(normalizedActiveThreadId)) {
                return;
            }

            WritePlannerThreadContextStoreNoThrow(path, store);
        }
    }

    private void ClearPlannerThreadContextSnapshots() {
        var path = ResolvePlannerThreadContextStorePath();
        lock (PlannerThreadContextStoreLock) {
            ChatServiceJsonFileStore.Delete(path, "Planner thread-context store");
        }
    }

    private bool TryLoadPlannerThreadContextSnapshot(string activeThreadId, out string plannerThreadId, out long seenUtcTicks) {
        plannerThreadId = string.Empty;
        seenUtcTicks = 0;

        var normalizedActiveThreadId = (activeThreadId ?? string.Empty).Trim();
        if (normalizedActiveThreadId.Length == 0) {
            return false;
        }

        var path = ResolvePlannerThreadContextStorePath();
        lock (PlannerThreadContextStoreLock) {
            var store = ReadPlannerThreadContextStoreNoThrow(path);
            if (!store.Threads.TryGetValue(normalizedActiveThreadId, out var entry) || entry is null) {
                return false;
            }

            var normalizedPlannerThreadId = (entry.PlannerThreadId ?? string.Empty).Trim();
            if (normalizedPlannerThreadId.Length == 0) {
                store.Threads.Remove(normalizedActiveThreadId);
                WritePlannerThreadContextStoreNoThrow(path, store);
                return false;
            }

            if (!TryGetUtcDateTimeFromTicks(entry.SeenUtcTicks, out var seenUtc)) {
                store.Threads.Remove(normalizedActiveThreadId);
                WritePlannerThreadContextStoreNoThrow(path, store);
                return false;
            }

            var now = DateTime.UtcNow;
            if (seenUtc > now || now - seenUtc > PlannerThreadContextMaxAge) {
                store.Threads.Remove(normalizedActiveThreadId);
                WritePlannerThreadContextStoreNoThrow(path, store);
                return false;
            }

            plannerThreadId = normalizedPlannerThreadId;
            seenUtcTicks = entry.SeenUtcTicks;
            return true;
        }
    }

    private static PlannerThreadContextStoreDto ReadPlannerThreadContextStoreNoThrow(string path) {
        return ChatServiceJsonFileStore.ReadOrCreate(
            path,
            maximumBytes: 1024 * 1024,
            static json => JsonSerializer.Deserialize<PlannerThreadContextStoreDto>(json),
            static store => store.Version == PlannerThreadContextStoreVersion && store.Threads is not null,
            static store => {
                if (store.Threads.Comparer != StringComparer.Ordinal) {
                    store.Threads = new Dictionary<string, PlannerThreadContextStoreEntryDto>(store.Threads, StringComparer.Ordinal);
                }
            },
            static () => new PlannerThreadContextStoreDto(),
            "Planner thread-context store");
    }

    private static void WritePlannerThreadContextStoreNoThrow(string path, PlannerThreadContextStoreDto store) {
        ChatServiceJsonFileStore.Write(
            path,
            store,
            static value => JsonSerializer.Serialize(value),
            "Planner thread-context store");
    }

    private static void PrunePlannerThreadContextStore(PlannerThreadContextStoreDto store) {
        if (store.Threads.Count <= MaxTrackedPlannerThreadContexts) {
            return;
        }

        var removeCount = store.Threads.Count - MaxTrackedPlannerThreadContexts;
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
