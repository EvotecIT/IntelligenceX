using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private const int PlannerThreadContextStoreVersion = 1;
    private static readonly object PlannerThreadContextStoreLock = new();
    private static readonly JsonSerializerOptions PlannerThreadContextStoreJsonOptions = new() {
        WriteIndented = false,
        PropertyNameCaseInsensitive = false
    };

    private sealed class PlannerThreadContextStoreDto {
        public int Version { get; set; } = PlannerThreadContextStoreVersion;
        public Dictionary<string, PlannerThreadContextStoreEntryDto> Threads { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed class PlannerThreadContextStoreEntryDto {
        public string PlannerThreadId { get; set; } = string.Empty;
        public long SeenUtcTicks { get; set; }
    }

    private static string ResolveDefaultPlannerThreadContextStorePath() {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root)) {
            root = ".";
        }

        return Path.Combine(root, "IntelligenceX.Chat", "planner-thread-contexts.json");
    }

    private string ResolvePlannerThreadContextStorePath() {
        var pendingActionsPath = ResolvePendingActionsStorePath();
        var directory = Path.GetDirectoryName(pendingActionsPath);
        if (!string.IsNullOrWhiteSpace(directory)) {
            return Path.Combine(directory, "planner-thread-contexts.json");
        }

        return ResolveDefaultPlannerThreadContextStorePath();
    }

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
            try {
                if (File.Exists(path)) {
                    File.Delete(path);
                }
            } catch (Exception ex) {
                Trace.TraceWarning($"Planner thread-context store clear failed: {ex.GetType().Name}: {ex.Message}");
            }
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
        try {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) {
                return new PlannerThreadContextStoreDto();
            }

            var info = new FileInfo(path);
            if (info.Length <= 0 || info.Length > 1024 * 1024) {
                return new PlannerThreadContextStoreDto();
            }

            var json = File.ReadAllText(path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json)) {
                return new PlannerThreadContextStoreDto();
            }

            var store = JsonSerializer.Deserialize<PlannerThreadContextStoreDto>(json, PlannerThreadContextStoreJsonOptions);
            if (store is null || store.Version != PlannerThreadContextStoreVersion || store.Threads is null) {
                return new PlannerThreadContextStoreDto();
            }

            if (store.Threads.Comparer != StringComparer.Ordinal) {
                store.Threads = new Dictionary<string, PlannerThreadContextStoreEntryDto>(store.Threads, StringComparer.Ordinal);
            }

            return store;
        } catch (Exception ex) {
            Trace.TraceWarning($"Planner thread-context store read failed: {ex.GetType().Name}: {ex.Message}");
            return new PlannerThreadContextStoreDto();
        }
    }

    private static void WritePlannerThreadContextStoreNoThrow(string path, PlannerThreadContextStoreDto store) {
        string? tmp = null;
        try {
            if (string.IsNullOrWhiteSpace(path)) {
                return;
            }

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory)) {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(store, PlannerThreadContextStoreJsonOptions);
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
            Trace.TraceWarning($"Planner thread-context store write failed: {ex.GetType().Name}: {ex.Message}");
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
