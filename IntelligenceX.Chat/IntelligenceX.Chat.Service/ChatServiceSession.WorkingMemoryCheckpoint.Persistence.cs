using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private const int WorkingMemoryCheckpointStoreVersion = 1;
    private static readonly object WorkingMemoryCheckpointStoreLock = new();
    private static readonly JsonSerializerOptions WorkingMemoryCheckpointStoreJsonOptions = new() {
        WriteIndented = false,
        PropertyNameCaseInsensitive = false
    };

    private sealed class WorkingMemoryCheckpointStoreDto {
        public int Version { get; set; } = WorkingMemoryCheckpointStoreVersion;
        public Dictionary<string, WorkingMemoryCheckpointStoreEntryDto> Threads { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed class WorkingMemoryCheckpointStoreEntryDto {
        public string IntentAnchor { get; set; } = string.Empty;
        public string DomainIntentFamily { get; set; } = string.Empty;
        public string[] RecentToolNames { get; set; } = Array.Empty<string>();
        public string[] RecentEvidenceSnippets { get; set; } = Array.Empty<string>();
        public string[] CapabilityEnabledPackIds { get; set; } = Array.Empty<string>();
        public string[] CapabilityRoutingFamilies { get; set; } = Array.Empty<string>();
        public string[] CapabilitySkills { get; set; } = Array.Empty<string>();
        public string[] CapabilityHealthyToolNames { get; set; } = Array.Empty<string>();
        public long SeenUtcTicks { get; set; }
    }

    private static string ResolveDefaultWorkingMemoryCheckpointStorePath() {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root)) {
            root = ".";
        }

        return Path.Combine(root, "IntelligenceX.Chat", "working-memory-checkpoints.json");
    }

    private string ResolveWorkingMemoryCheckpointStorePath() {
        var pendingActionsPath = ResolvePendingActionsStorePath();
        var directory = Path.GetDirectoryName(pendingActionsPath);
        if (!string.IsNullOrWhiteSpace(directory)) {
            return Path.Combine(directory, "working-memory-checkpoints.json");
        }

        return ResolveDefaultWorkingMemoryCheckpointStorePath();
    }

    private void PersistWorkingMemoryCheckpointSnapshot(string threadId, WorkingMemoryCheckpoint checkpoint) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0 || checkpoint.SeenUtcTicks <= 0) {
            return;
        }

        var path = ResolveWorkingMemoryCheckpointStorePath();
        lock (WorkingMemoryCheckpointStoreLock) {
            var store = ReadWorkingMemoryCheckpointStoreNoThrow(path);
            store.Threads[normalizedThreadId] = new WorkingMemoryCheckpointStoreEntryDto {
                IntentAnchor = checkpoint.IntentAnchor,
                DomainIntentFamily = checkpoint.DomainIntentFamily,
                RecentToolNames = NormalizeWorkingMemoryList(checkpoint.RecentToolNames, MaxWorkingMemoryToolNames),
                RecentEvidenceSnippets = NormalizeWorkingMemoryList(checkpoint.RecentEvidenceSnippets, MaxWorkingMemoryEvidenceLines),
                CapabilityEnabledPackIds = NormalizeDistinctStrings(
                    (checkpoint.CapabilityEnabledPackIds ?? Array.Empty<string>())
                    .Select(static packId => NormalizePackId(packId))
                    .Where(static packId => packId.Length > 0),
                    MaxCapabilitySnapshotPackIds),
                CapabilityRoutingFamilies = NormalizeCapabilitySnapshotRoutingFamilies(checkpoint.CapabilityRoutingFamilies ?? Array.Empty<string>()),
                CapabilitySkills = ResolveWorkingMemoryCapabilitySkills(checkpoint.CapabilitySkills ?? Array.Empty<string>()),
                CapabilityHealthyToolNames = NormalizeCapabilitySnapshotHealthyToolNames(
                    checkpoint.CapabilityHealthyToolNames ?? Array.Empty<string>()),
                SeenUtcTicks = checkpoint.SeenUtcTicks
            };
            PruneWorkingMemoryCheckpointStore(store);
            WriteWorkingMemoryCheckpointStoreNoThrow(path, store);
        }
    }

    private bool TryLoadWorkingMemoryCheckpointSnapshot(string threadId, out WorkingMemoryCheckpoint checkpoint) {
        checkpoint = default;
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return false;
        }

        var path = ResolveWorkingMemoryCheckpointStorePath();
        lock (WorkingMemoryCheckpointStoreLock) {
            var store = ReadWorkingMemoryCheckpointStoreNoThrow(path);
            if (!store.Threads.TryGetValue(normalizedThreadId, out var entry) || entry is null) {
                return false;
            }

            var intentAnchor = NormalizeWorkingMemoryIntentAnchor(entry.IntentAnchor);
            var domainIntentFamily = (entry.DomainIntentFamily ?? string.Empty).Trim();
            var recentToolNames = NormalizeWorkingMemoryList(entry.RecentToolNames ?? Array.Empty<string>(), MaxWorkingMemoryToolNames);
            var recentEvidenceSnippets =
                NormalizeWorkingMemoryList(entry.RecentEvidenceSnippets ?? Array.Empty<string>(), MaxWorkingMemoryEvidenceLines);
            var capabilityEnabledPackIds = NormalizeDistinctStrings(
                (entry.CapabilityEnabledPackIds ?? Array.Empty<string>())
                .Select(static packId => NormalizePackId(packId))
                .Where(static packId => packId.Length > 0),
                MaxCapabilitySnapshotPackIds);
            var capabilityRoutingFamilies = NormalizeCapabilitySnapshotRoutingFamilies(entry.CapabilityRoutingFamilies ?? Array.Empty<string>());
            var capabilitySkills = ResolveWorkingMemoryCapabilitySkills(entry.CapabilitySkills ?? Array.Empty<string>());
            var capabilityHealthyToolNames = NormalizeCapabilitySnapshotHealthyToolNames(
                entry.CapabilityHealthyToolNames ?? Array.Empty<string>());

            if (intentAnchor.Length == 0
                && domainIntentFamily.Length == 0
                && recentToolNames.Length == 0
                && recentEvidenceSnippets.Length == 0
                && capabilityEnabledPackIds.Length == 0
                && capabilityRoutingFamilies.Length == 0
                && capabilitySkills.Length == 0
                && capabilityHealthyToolNames.Length == 0) {
                store.Threads.Remove(normalizedThreadId);
                WriteWorkingMemoryCheckpointStoreNoThrow(path, store);
                return false;
            }

            if (!TryGetUtcDateTimeFromTicks(entry.SeenUtcTicks, out var seenUtc)) {
                store.Threads.Remove(normalizedThreadId);
                WriteWorkingMemoryCheckpointStoreNoThrow(path, store);
                return false;
            }

            var nowUtc = DateTime.UtcNow;
            if (seenUtc > nowUtc || nowUtc - seenUtc > WorkingMemoryContextMaxAge) {
                store.Threads.Remove(normalizedThreadId);
                WriteWorkingMemoryCheckpointStoreNoThrow(path, store);
                return false;
            }

            checkpoint = new WorkingMemoryCheckpoint(
                IntentAnchor: intentAnchor,
                DomainIntentFamily: domainIntentFamily,
                RecentToolNames: recentToolNames,
                RecentEvidenceSnippets: recentEvidenceSnippets,
                CapabilityEnabledPackIds: capabilityEnabledPackIds,
                CapabilityRoutingFamilies: capabilityRoutingFamilies,
                CapabilitySkills: capabilitySkills,
                CapabilityHealthyToolNames: capabilityHealthyToolNames,
                SeenUtcTicks: entry.SeenUtcTicks);
            return true;
        }
    }

    private void RemoveWorkingMemoryCheckpointSnapshot(string threadId) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return;
        }

        var path = ResolveWorkingMemoryCheckpointStorePath();
        lock (WorkingMemoryCheckpointStoreLock) {
            var store = ReadWorkingMemoryCheckpointStoreNoThrow(path);
            if (store.Threads.Remove(normalizedThreadId)) {
                WriteWorkingMemoryCheckpointStoreNoThrow(path, store);
            }
        }
    }

    private void ClearPersistedWorkingMemoryCheckpointStore() {
        var path = ResolveWorkingMemoryCheckpointStorePath();
        lock (WorkingMemoryCheckpointStoreLock) {
            try {
                if (File.Exists(path)) {
                    File.Delete(path);
                }
            } catch (Exception ex) {
                Trace.TraceWarning($"Working-memory checkpoint store clear failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    private static WorkingMemoryCheckpointStoreDto ReadWorkingMemoryCheckpointStoreNoThrow(string path) {
        try {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) {
                return new WorkingMemoryCheckpointStoreDto();
            }

            var info = new FileInfo(path);
            if (info.Length <= 0 || info.Length > 1024 * 1024) {
                return new WorkingMemoryCheckpointStoreDto();
            }

            var json = File.ReadAllText(path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json)) {
                return new WorkingMemoryCheckpointStoreDto();
            }

            var store = JsonSerializer.Deserialize<WorkingMemoryCheckpointStoreDto>(json, WorkingMemoryCheckpointStoreJsonOptions);
            if (store is null || store.Version != WorkingMemoryCheckpointStoreVersion || store.Threads is null) {
                return new WorkingMemoryCheckpointStoreDto();
            }

            if (store.Threads.Comparer != StringComparer.Ordinal) {
                store.Threads = new Dictionary<string, WorkingMemoryCheckpointStoreEntryDto>(store.Threads, StringComparer.Ordinal);
            }

            return store;
        } catch (Exception ex) {
            Trace.TraceWarning($"Working-memory checkpoint store read failed: {ex.GetType().Name}: {ex.Message}");
            return new WorkingMemoryCheckpointStoreDto();
        }
    }

    private static void WriteWorkingMemoryCheckpointStoreNoThrow(string path, WorkingMemoryCheckpointStoreDto store) {
        string? tmp = null;
        try {
            if (string.IsNullOrWhiteSpace(path)) {
                return;
            }

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory)) {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(store, WorkingMemoryCheckpointStoreJsonOptions);
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
            Trace.TraceWarning($"Working-memory checkpoint store write failed: {ex.GetType().Name}: {ex.Message}");
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

    private static void PruneWorkingMemoryCheckpointStore(WorkingMemoryCheckpointStoreDto store) {
        if (store.Threads.Count <= MaxTrackedWorkingMemoryContexts) {
            return;
        }

        var removeCount = store.Threads.Count - MaxTrackedWorkingMemoryContexts;
        if (removeCount <= 0) {
            return;
        }

        var threadIdsToRemove = store.Threads
            .Select(static pair => (ThreadId: pair.Key, SeenUtcTicks: pair.Value?.SeenUtcTicks ?? 0L))
            .OrderBy(static pair => pair.SeenUtcTicks)
            .ThenBy(static pair => pair.ThreadId, StringComparer.Ordinal)
            .Take(removeCount)
            .Select(static pair => pair.ThreadId)
            .ToArray();
        foreach (var threadId in threadIdsToRemove) {
            store.Threads.Remove(threadId);
        }
    }
}
