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
    private const int WorkingMemoryCheckpointStoreVersion = 2;
    private static readonly object WorkingMemoryCheckpointStoreLock = new();

    private sealed class WorkingMemoryCheckpointStoreDto {
        public int Version { get; set; } = WorkingMemoryCheckpointStoreVersion;
        public Dictionary<string, WorkingMemoryCheckpointStoreEntryDto> Threads { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed class WorkingMemoryCheckpointStoreEntryDto {
        public string IntentAnchor { get; set; } = string.Empty;
        public string DomainIntentFamily { get; set; } = string.Empty;
        public string[] RecentToolNames { get; set; } = Array.Empty<string>();
        public string[] RecentEvidenceSnippets { get; set; } = Array.Empty<string>();
        public string PriorAnswerPlanUserGoal { get; set; } = string.Empty;
        public string PriorAnswerPlanUnresolvedNow { get; set; } = string.Empty;
        public bool PriorAnswerPlanRequiresLiveExecution { get; set; }
        public string PriorAnswerPlanMissingLiveEvidence { get; set; } = string.Empty;
        public string[] PriorAnswerPlanPreferredPackIds { get; set; } = Array.Empty<string>();
        public string[] PriorAnswerPlanPreferredToolNames { get; set; } = Array.Empty<string>();
        public string[] PriorAnswerPlanPreferredDeferredWorkCapabilityIds { get; set; } = Array.Empty<string>();
        public bool PriorAnswerPlanAllowCachedEvidenceReuse { get; set; }
        public bool PriorAnswerPlanPreferCachedEvidenceReuse { get; set; }
        public string PriorAnswerPlanCachedEvidenceReuseReason { get; set; } = string.Empty;
        public string PriorAnswerPlanPrimaryArtifact { get; set; } = string.Empty;
        public string[] CapabilityEnabledPackIds { get; set; } = Array.Empty<string>();
        public string[] CapabilityRoutingFamilies { get; set; } = Array.Empty<string>();
        public string[] CapabilitySkills { get; set; } = Array.Empty<string>();
        public string[] CapabilityHealthyToolNames { get; set; } = Array.Empty<string>();
        public long SeenUtcTicks { get; set; }
    }

    private static string ResolveDefaultWorkingMemoryCheckpointStorePath() =>
        ChatServiceJsonFileStore.ResolveDefaultPath("working-memory-checkpoints.json");

    private string ResolveWorkingMemoryCheckpointStorePath() =>
        ChatServiceJsonFileStore.ResolveSiblingPath(ResolvePendingActionsStorePath(), "working-memory-checkpoints.json");

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
                PriorAnswerPlanUserGoal = NormalizeWorkingMemoryAnswerPlanFocus(checkpoint.PriorAnswerPlanUserGoal),
                PriorAnswerPlanUnresolvedNow = NormalizeWorkingMemoryAnswerPlanFocus(checkpoint.PriorAnswerPlanUnresolvedNow),
                PriorAnswerPlanRequiresLiveExecution = checkpoint.PriorAnswerPlanRequiresLiveExecution,
                PriorAnswerPlanMissingLiveEvidence = checkpoint.PriorAnswerPlanRequiresLiveExecution
                    ? NormalizeWorkingMemoryAnswerPlanFocus(checkpoint.PriorAnswerPlanMissingLiveEvidence)
                    : string.Empty,
                PriorAnswerPlanPreferredPackIds = NormalizeDistinctStrings(
                    (checkpoint.PriorAnswerPlanPreferredPackIds ?? Array.Empty<string>())
                    .Select(static packId => NormalizePackId(packId))
                    .Where(static packId => packId.Length > 0),
                    maxItems: 8),
                PriorAnswerPlanPreferredToolNames = NormalizeDistinctStrings(
                    checkpoint.PriorAnswerPlanPreferredToolNames ?? Array.Empty<string>(),
                    maxItems: 8),
                PriorAnswerPlanPreferredDeferredWorkCapabilityIds = NormalizeDistinctStrings(
                    (checkpoint.PriorAnswerPlanPreferredDeferredWorkCapabilityIds ?? Array.Empty<string>())
                    .Select(static capabilityId => NormalizeDeferredWorkCapabilityId(capabilityId))
                    .Where(static capabilityId => capabilityId.Length > 0),
                    maxItems: 6),
                PriorAnswerPlanAllowCachedEvidenceReuse = checkpoint.PriorAnswerPlanAllowCachedEvidenceReuse,
                PriorAnswerPlanPreferCachedEvidenceReuse = checkpoint.PriorAnswerPlanPreferCachedEvidenceReuse,
                PriorAnswerPlanCachedEvidenceReuseReason = checkpoint.PriorAnswerPlanPreferCachedEvidenceReuse
                    ? NormalizeWorkingMemoryAnswerPlanFocus(checkpoint.PriorAnswerPlanCachedEvidenceReuseReason)
                    : string.Empty,
                PriorAnswerPlanPrimaryArtifact = NormalizeWorkingMemoryAnswerPlanArtifact(checkpoint.PriorAnswerPlanPrimaryArtifact),
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
            var priorAnswerPlanUserGoal = NormalizeWorkingMemoryAnswerPlanFocus(entry.PriorAnswerPlanUserGoal);
            var priorAnswerPlanUnresolvedNow = NormalizeWorkingMemoryAnswerPlanFocus(entry.PriorAnswerPlanUnresolvedNow);
            var priorAnswerPlanRequiresLiveExecution = entry.PriorAnswerPlanRequiresLiveExecution;
            var priorAnswerPlanMissingLiveEvidence = priorAnswerPlanRequiresLiveExecution
                ? NormalizeWorkingMemoryAnswerPlanFocus(entry.PriorAnswerPlanMissingLiveEvidence)
                : string.Empty;
            var priorAnswerPlanPreferredPackIds = NormalizeDistinctStrings(
                (entry.PriorAnswerPlanPreferredPackIds ?? Array.Empty<string>())
                .Select(static packId => NormalizePackId(packId))
                .Where(static packId => packId.Length > 0),
                maxItems: 8);
            var priorAnswerPlanPreferredToolNames = NormalizeDistinctStrings(
                entry.PriorAnswerPlanPreferredToolNames ?? Array.Empty<string>(),
                maxItems: 8);
            var priorAnswerPlanPreferredDeferredWorkCapabilityIds = NormalizeDistinctStrings(
                (entry.PriorAnswerPlanPreferredDeferredWorkCapabilityIds ?? Array.Empty<string>())
                .Select(static capabilityId => NormalizeDeferredWorkCapabilityId(capabilityId))
                .Where(static capabilityId => capabilityId.Length > 0),
                maxItems: 6);
            var priorAnswerPlanAllowCachedEvidenceReuse = entry.PriorAnswerPlanAllowCachedEvidenceReuse;
            var priorAnswerPlanPreferCachedEvidenceReuse = entry.PriorAnswerPlanPreferCachedEvidenceReuse;
            var priorAnswerPlanCachedEvidenceReuseReason = priorAnswerPlanPreferCachedEvidenceReuse
                ? NormalizeWorkingMemoryAnswerPlanFocus(entry.PriorAnswerPlanCachedEvidenceReuseReason)
                : string.Empty;
            var priorAnswerPlanPrimaryArtifact = NormalizeWorkingMemoryAnswerPlanArtifact(entry.PriorAnswerPlanPrimaryArtifact);
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
                && priorAnswerPlanUserGoal.Length == 0
                && priorAnswerPlanUnresolvedNow.Length == 0
                && !priorAnswerPlanRequiresLiveExecution
                && priorAnswerPlanMissingLiveEvidence.Length == 0
                && priorAnswerPlanPreferredPackIds.Length == 0
                && priorAnswerPlanPreferredToolNames.Length == 0
                && priorAnswerPlanPreferredDeferredWorkCapabilityIds.Length == 0
                && !priorAnswerPlanAllowCachedEvidenceReuse
                && !priorAnswerPlanPreferCachedEvidenceReuse
                && priorAnswerPlanCachedEvidenceReuseReason.Length == 0
                && priorAnswerPlanPrimaryArtifact.Length == 0
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
                PriorAnswerPlanUserGoal: priorAnswerPlanUserGoal,
                PriorAnswerPlanUnresolvedNow: priorAnswerPlanUnresolvedNow,
                PriorAnswerPlanRequiresLiveExecution: priorAnswerPlanRequiresLiveExecution,
                PriorAnswerPlanMissingLiveEvidence: priorAnswerPlanMissingLiveEvidence,
                PriorAnswerPlanPreferredPackIds: priorAnswerPlanPreferredPackIds,
                PriorAnswerPlanPreferredToolNames: priorAnswerPlanPreferredToolNames,
                PriorAnswerPlanPreferredDeferredWorkCapabilityIds: priorAnswerPlanPreferredDeferredWorkCapabilityIds,
                PriorAnswerPlanAllowCachedEvidenceReuse: priorAnswerPlanAllowCachedEvidenceReuse,
                PriorAnswerPlanPreferCachedEvidenceReuse: priorAnswerPlanPreferCachedEvidenceReuse,
                PriorAnswerPlanCachedEvidenceReuseReason: priorAnswerPlanCachedEvidenceReuseReason,
                PriorAnswerPlanPrimaryArtifact: priorAnswerPlanPrimaryArtifact,
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
            ChatServiceJsonFileStore.Delete(path, "Working-memory checkpoint store");
        }
    }

    private static WorkingMemoryCheckpointStoreDto ReadWorkingMemoryCheckpointStoreNoThrow(string path) {
        return ChatServiceJsonFileStore.ReadOrCreate(
            path,
            maximumBytes: 1024 * 1024,
            static json => JsonSerializer.Deserialize<WorkingMemoryCheckpointStoreDto>(json),
            static store => store.Version == WorkingMemoryCheckpointStoreVersion && store.Threads is not null,
            static store => {
                if (store.Threads.Comparer != StringComparer.Ordinal) {
                    store.Threads = new Dictionary<string, WorkingMemoryCheckpointStoreEntryDto>(store.Threads, StringComparer.Ordinal);
                }
            },
            static () => new WorkingMemoryCheckpointStoreDto(),
            "Working-memory checkpoint store");
    }

    private static void WriteWorkingMemoryCheckpointStoreNoThrow(string path, WorkingMemoryCheckpointStoreDto store) {
        ChatServiceJsonFileStore.Write(
            path,
            store,
            static value => JsonSerializer.Serialize(value),
            "Working-memory checkpoint store");
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
