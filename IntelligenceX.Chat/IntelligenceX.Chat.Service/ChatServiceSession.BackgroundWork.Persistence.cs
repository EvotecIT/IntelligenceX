using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using IntelligenceX.Chat.Service.Persistence;
using IntelligenceX.Tools;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private const int BackgroundWorkStoreVersion = 4;
    private static readonly object BackgroundWorkStoreLock = new();

    private sealed class BackgroundWorkStoreDto {
        public int Version { get; set; } = BackgroundWorkStoreVersion;
        public Dictionary<string, BackgroundWorkStoreThreadEntryDto> Threads { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed class BackgroundWorkStoreThreadEntryDto {
        public long SeenUtcTicks { get; set; }
        public int QueuedCount { get; set; }
        public int ReadyCount { get; set; }
        public int RunningCount { get; set; }
        public int CompletedCount { get; set; }
        public int PendingReadOnlyCount { get; set; }
        public int PendingUnknownCount { get; set; }
        public string[] RecentEvidenceTools { get; set; } = Array.Empty<string>();
        public BackgroundWorkStoreItemDto?[] Items { get; set; } = Array.Empty<BackgroundWorkStoreItemDto?>();
    }

    private sealed class BackgroundWorkStoreItemDto {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Request { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string[] DependencyItemIds { get; set; } = Array.Empty<string>();
        public string[] EvidenceToolNames { get; set; } = Array.Empty<string>();
        public string Kind { get; set; } = string.Empty;
        public string Mutability { get; set; } = string.Empty;
        public string SourceToolName { get; set; } = string.Empty;
        public string SourceCallId { get; set; } = string.Empty;
        public string TargetPackId { get; set; } = string.Empty;
        public string TargetToolName { get; set; } = string.Empty;
        public string FollowUpKind { get; set; } = string.Empty;
        public int FollowUpPriority { get; set; }
        public string PreparedArgumentsJson { get; set; } = "{}";
        public string ResultReference { get; set; } = string.Empty;
        public int ExecutionAttemptCount { get; set; }
        public string LastExecutionCallId { get; set; } = string.Empty;
        public long LastExecutionStartedUtcTicks { get; set; }
        public long LastExecutionFinishedUtcTicks { get; set; }
        public long LeaseExpiresUtcTicks { get; set; }
        public long CreatedUtcTicks { get; set; }
        public long UpdatedUtcTicks { get; set; }
    }

    private static string ResolveDefaultBackgroundWorkStorePath() =>
        ChatServiceJsonFileStore.ResolveDefaultPath("background-work-cache.json");

    private string ResolveBackgroundWorkStorePath() =>
        ChatServiceJsonFileStore.ResolveSiblingPath(ResolvePendingActionsStorePath(), "background-work-cache.json");

    private void PersistThreadBackgroundWorkSnapshot(string threadId, ThreadBackgroundWorkSnapshot snapshot, long seenUtcTicks) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return;
        }

        var path = ResolveBackgroundWorkStorePath();
        lock (BackgroundWorkStoreLock) {
            var store = ReadBackgroundWorkStoreNoThrow(path);
            if (seenUtcTicks <= 0 || IsEmptyBackgroundWorkSnapshot(snapshot)) {
                if (store.Threads.Remove(normalizedThreadId)) {
                    WriteBackgroundWorkStoreNoThrow(path, store);
                }

                return;
            }

            var normalizedRecentEvidenceTools = (snapshot.RecentEvidenceTools ?? Array.Empty<string>())
                .Where(static toolName => !string.IsNullOrWhiteSpace(toolName))
                .Select(static toolName => NormalizeToolNameForAnswerPlan(toolName))
                .Where(static toolName => toolName.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxBackgroundWorkEvidenceTools)
                .ToArray();
            var normalizedItems = (snapshot.Items ?? Array.Empty<ThreadBackgroundWorkItem>())
                .Where(static item => !string.IsNullOrWhiteSpace(item.Id))
                .Take(MaxBackgroundWorkItems)
                .Select(item => new BackgroundWorkStoreItemDto {
                    Id = (item.Id ?? string.Empty).Trim(),
                    Title = (item.Title ?? string.Empty).Trim(),
                    Request = NormalizeWorkingMemoryAnswerPlanFocus(item.Request),
                    State = NormalizeBackgroundWorkState(item.State),
                    DependencyItemIds = NormalizeBackgroundWorkDependencyItemIds(item.DependencyItemIds),
                    EvidenceToolNames = NormalizeBackgroundWorkToolNames(item.EvidenceToolNames),
                    Kind = NormalizeBackgroundWorkKind(item.Kind),
                    Mutability = NormalizeBackgroundWorkMutability(item.Mutability),
                    SourceToolName = NormalizeToolNameForAnswerPlan(item.SourceToolName),
                    SourceCallId = (item.SourceCallId ?? string.Empty).Trim(),
                    TargetPackId = (item.TargetPackId ?? string.Empty).Trim(),
                    TargetToolName = NormalizeToolNameForAnswerPlan(item.TargetToolName),
                    FollowUpKind = ToolHandoffFollowUpKinds.Normalize(item.FollowUpKind),
                    FollowUpPriority = ToolHandoffFollowUpPriorities.Normalize(item.FollowUpPriority),
                    PreparedArgumentsJson = NormalizeBackgroundWorkArgumentsJson(item.PreparedArgumentsJson),
                    ResultReference = (item.ResultReference ?? string.Empty).Trim(),
                    ExecutionAttemptCount = Math.Max(0, item.ExecutionAttemptCount),
                    LastExecutionCallId = (item.LastExecutionCallId ?? string.Empty).Trim(),
                    LastExecutionStartedUtcTicks = Math.Max(0, item.LastExecutionStartedUtcTicks),
                    LastExecutionFinishedUtcTicks = Math.Max(0, item.LastExecutionFinishedUtcTicks),
                    LeaseExpiresUtcTicks = Math.Max(0, item.LeaseExpiresUtcTicks),
                    CreatedUtcTicks = item.CreatedUtcTicks,
                    UpdatedUtcTicks = item.UpdatedUtcTicks > 0 ? item.UpdatedUtcTicks : item.CreatedUtcTicks
                })
                .Where(static item => item.Id.Length > 0)
                .ToArray();
            if (normalizedItems.Length == 0) {
                if (store.Threads.Remove(normalizedThreadId)) {
                    WriteBackgroundWorkStoreNoThrow(path, store);
                }

                return;
            }

            store.Threads[normalizedThreadId] = new BackgroundWorkStoreThreadEntryDto {
                SeenUtcTicks = seenUtcTicks,
                QueuedCount = Math.Max(0, snapshot.QueuedCount),
                ReadyCount = Math.Max(0, snapshot.ReadyCount),
                RunningCount = Math.Max(0, snapshot.RunningCount),
                CompletedCount = Math.Max(0, snapshot.CompletedCount),
                PendingReadOnlyCount = Math.Max(0, snapshot.PendingReadOnlyCount),
                PendingUnknownCount = Math.Max(0, snapshot.PendingUnknownCount),
                RecentEvidenceTools = normalizedRecentEvidenceTools,
                Items = normalizedItems
            };
            PruneBackgroundWorkStore(store);
            WriteBackgroundWorkStoreNoThrow(path, store);
        }
    }

    private bool TryLoadThreadBackgroundWorkSnapshot(string threadId, out ThreadBackgroundWorkSnapshot snapshot, out long seenUtcTicks) {
        snapshot = EmptyBackgroundWorkSnapshot();
        seenUtcTicks = 0;

        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return false;
        }

        var path = ResolveBackgroundWorkStorePath();
        lock (BackgroundWorkStoreLock) {
            var store = ReadBackgroundWorkStoreNoThrow(path);
            if (!store.Threads.TryGetValue(normalizedThreadId, out var entry) || entry is null) {
                return false;
            }

            if (!TryGetUtcDateTimeFromTicks(entry.SeenUtcTicks, out var seenUtc)) {
                store.Threads.Remove(normalizedThreadId);
                WriteBackgroundWorkStoreNoThrow(path, store);
                return false;
            }

            var nowUtc = DateTime.UtcNow;
            if (seenUtc > nowUtc || nowUtc - seenUtc > ThreadBackgroundWorkContextMaxAge) {
                store.Threads.Remove(normalizedThreadId);
                WriteBackgroundWorkStoreNoThrow(path, store);
                return false;
            }

            var normalizedRecentEvidenceTools = (entry.RecentEvidenceTools ?? Array.Empty<string>())
                .Where(static toolName => !string.IsNullOrWhiteSpace(toolName))
                .Select(static toolName => NormalizeToolNameForAnswerPlan(toolName))
                .Where(static toolName => toolName.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxBackgroundWorkEvidenceTools)
                .ToArray();
            var normalizedItems = new List<ThreadBackgroundWorkItem>(Math.Min(MaxBackgroundWorkItems, (entry.Items ?? Array.Empty<BackgroundWorkStoreItemDto?>()).Length));
            foreach (var persistedItem in entry.Items ?? Array.Empty<BackgroundWorkStoreItemDto?>()) {
                if (persistedItem is null || string.IsNullOrWhiteSpace(persistedItem.Id)) {
                    continue;
                }

                normalizedItems.Add(new ThreadBackgroundWorkItem(
                    Id: (persistedItem.Id ?? string.Empty).Trim(),
                    Title: (persistedItem.Title ?? string.Empty).Trim(),
                    Request: NormalizeWorkingMemoryAnswerPlanFocus(persistedItem.Request),
                    State: NormalizeBackgroundWorkState(persistedItem.State),
                    DependencyItemIds: NormalizeBackgroundWorkDependencyItemIds(persistedItem.DependencyItemIds),
                    EvidenceToolNames: NormalizeBackgroundWorkToolNames(persistedItem.EvidenceToolNames),
                    Kind: NormalizeBackgroundWorkKind(persistedItem.Kind),
                    Mutability: NormalizeBackgroundWorkMutability(persistedItem.Mutability),
                    SourceToolName: NormalizeToolNameForAnswerPlan(persistedItem.SourceToolName),
                    SourceCallId: (persistedItem.SourceCallId ?? string.Empty).Trim(),
                    TargetPackId: (persistedItem.TargetPackId ?? string.Empty).Trim(),
                    TargetToolName: NormalizeToolNameForAnswerPlan(persistedItem.TargetToolName),
                    FollowUpKind: ToolHandoffFollowUpKinds.Normalize(persistedItem.FollowUpKind),
                    FollowUpPriority: ToolHandoffFollowUpPriorities.Normalize(persistedItem.FollowUpPriority),
                    PreparedArgumentsJson: NormalizeBackgroundWorkArgumentsJson(persistedItem.PreparedArgumentsJson),
                    ResultReference: (persistedItem.ResultReference ?? string.Empty).Trim(),
                    ExecutionAttemptCount: Math.Max(0, persistedItem.ExecutionAttemptCount),
                    LastExecutionCallId: (persistedItem.LastExecutionCallId ?? string.Empty).Trim(),
                    LastExecutionStartedUtcTicks: Math.Max(0, persistedItem.LastExecutionStartedUtcTicks),
                    LastExecutionFinishedUtcTicks: Math.Max(0, persistedItem.LastExecutionFinishedUtcTicks),
                    LeaseExpiresUtcTicks: Math.Max(0, persistedItem.LeaseExpiresUtcTicks),
                    CreatedUtcTicks: persistedItem.CreatedUtcTicks,
                    UpdatedUtcTicks: persistedItem.UpdatedUtcTicks > 0 ? persistedItem.UpdatedUtcTicks : persistedItem.CreatedUtcTicks));
                if (normalizedItems.Count >= MaxBackgroundWorkItems) {
                    break;
                }
            }

            if (normalizedItems.Count == 0) {
                store.Threads.Remove(normalizedThreadId);
                WriteBackgroundWorkStoreNoThrow(path, store);
                return false;
            }

            snapshot = new ThreadBackgroundWorkSnapshot(
                QueuedCount: Math.Max(0, entry.QueuedCount),
                ReadyCount: Math.Max(0, entry.ReadyCount),
                RunningCount: Math.Max(0, entry.RunningCount),
                CompletedCount: Math.Max(0, entry.CompletedCount),
                PendingReadOnlyCount: Math.Max(0, entry.PendingReadOnlyCount),
                PendingUnknownCount: Math.Max(0, entry.PendingUnknownCount),
                RecentEvidenceTools: normalizedRecentEvidenceTools,
                Items: normalizedItems.ToArray());
            if (IsEmptyBackgroundWorkSnapshot(snapshot)) {
                store.Threads.Remove(normalizedThreadId);
                WriteBackgroundWorkStoreNoThrow(path, store);
                snapshot = EmptyBackgroundWorkSnapshot();
                return false;
            }

            seenUtcTicks = entry.SeenUtcTicks;
            return true;
        }
    }

    private PersistedThreadBackgroundWorkSnapshot[] LoadAllPersistedThreadBackgroundWorkSnapshots() {
        var path = ResolveBackgroundWorkStorePath();
        lock (BackgroundWorkStoreLock) {
            var store = ReadBackgroundWorkStoreNoThrow(path);
            if (store.Threads.Count == 0) {
                return Array.Empty<PersistedThreadBackgroundWorkSnapshot>();
            }

            var nowUtc = DateTime.UtcNow;
            var snapshots = new List<PersistedThreadBackgroundWorkSnapshot>(store.Threads.Count);
            foreach (var pair in store.Threads) {
                var threadId = (pair.Key ?? string.Empty).Trim();
                var entry = pair.Value;
                if (threadId.Length == 0
                    || entry is null
                    || !TryGetUtcDateTimeFromTicks(entry.SeenUtcTicks, out var seenUtc)
                    || seenUtc > nowUtc
                    || nowUtc - seenUtc > ThreadBackgroundWorkContextMaxAge) {
                    continue;
                }

                var normalizedRecentEvidenceTools = (entry.RecentEvidenceTools ?? Array.Empty<string>())
                    .Where(static toolName => !string.IsNullOrWhiteSpace(toolName))
                    .Select(static toolName => NormalizeToolNameForAnswerPlan(toolName))
                    .Where(static toolName => toolName.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(MaxBackgroundWorkEvidenceTools)
                    .ToArray();
                var normalizedItems = new List<ThreadBackgroundWorkItem>(Math.Min(MaxBackgroundWorkItems, (entry.Items ?? Array.Empty<BackgroundWorkStoreItemDto?>()).Length));
                foreach (var persistedItem in entry.Items ?? Array.Empty<BackgroundWorkStoreItemDto?>()) {
                    if (persistedItem is null || string.IsNullOrWhiteSpace(persistedItem.Id)) {
                        continue;
                    }

                    normalizedItems.Add(new ThreadBackgroundWorkItem(
                        Id: (persistedItem.Id ?? string.Empty).Trim(),
                        Title: (persistedItem.Title ?? string.Empty).Trim(),
                        Request: NormalizeWorkingMemoryAnswerPlanFocus(persistedItem.Request),
                        State: NormalizeBackgroundWorkState(persistedItem.State),
                        DependencyItemIds: NormalizeBackgroundWorkDependencyItemIds(persistedItem.DependencyItemIds),
                        EvidenceToolNames: NormalizeBackgroundWorkToolNames(persistedItem.EvidenceToolNames),
                        Kind: NormalizeBackgroundWorkKind(persistedItem.Kind),
                        Mutability: NormalizeBackgroundWorkMutability(persistedItem.Mutability),
                        SourceToolName: NormalizeToolNameForAnswerPlan(persistedItem.SourceToolName),
                        SourceCallId: (persistedItem.SourceCallId ?? string.Empty).Trim(),
                        TargetPackId: (persistedItem.TargetPackId ?? string.Empty).Trim(),
                        TargetToolName: NormalizeToolNameForAnswerPlan(persistedItem.TargetToolName),
                        FollowUpKind: ToolHandoffFollowUpKinds.Normalize(persistedItem.FollowUpKind),
                        FollowUpPriority: ToolHandoffFollowUpPriorities.Normalize(persistedItem.FollowUpPriority),
                        PreparedArgumentsJson: NormalizeBackgroundWorkArgumentsJson(persistedItem.PreparedArgumentsJson),
                        ResultReference: (persistedItem.ResultReference ?? string.Empty).Trim(),
                        ExecutionAttemptCount: Math.Max(0, persistedItem.ExecutionAttemptCount),
                        LastExecutionCallId: (persistedItem.LastExecutionCallId ?? string.Empty).Trim(),
                        LastExecutionStartedUtcTicks: Math.Max(0, persistedItem.LastExecutionStartedUtcTicks),
                        LastExecutionFinishedUtcTicks: Math.Max(0, persistedItem.LastExecutionFinishedUtcTicks),
                        LeaseExpiresUtcTicks: Math.Max(0, persistedItem.LeaseExpiresUtcTicks),
                        CreatedUtcTicks: persistedItem.CreatedUtcTicks,
                        UpdatedUtcTicks: persistedItem.UpdatedUtcTicks > 0 ? persistedItem.UpdatedUtcTicks : persistedItem.CreatedUtcTicks));
                    if (normalizedItems.Count >= MaxBackgroundWorkItems) {
                        break;
                    }
                }

                if (normalizedItems.Count == 0) {
                    continue;
                }

                var snapshot = new ThreadBackgroundWorkSnapshot(
                    QueuedCount: Math.Max(0, entry.QueuedCount),
                    ReadyCount: Math.Max(0, entry.ReadyCount),
                    RunningCount: Math.Max(0, entry.RunningCount),
                    CompletedCount: Math.Max(0, entry.CompletedCount),
                    PendingReadOnlyCount: Math.Max(0, entry.PendingReadOnlyCount),
                    PendingUnknownCount: Math.Max(0, entry.PendingUnknownCount),
                    RecentEvidenceTools: normalizedRecentEvidenceTools,
                    Items: normalizedItems.ToArray());
                if (IsEmptyBackgroundWorkSnapshot(snapshot)) {
                    continue;
                }

                snapshots.Add(new PersistedThreadBackgroundWorkSnapshot(threadId, snapshot, entry.SeenUtcTicks));
            }

            return snapshots.Count == 0 ? Array.Empty<PersistedThreadBackgroundWorkSnapshot>() : snapshots.ToArray();
        }
    }

    private void ClearBackgroundWorkSnapshots() {
        var path = ResolveBackgroundWorkStorePath();
        lock (BackgroundWorkStoreLock) {
            ChatServiceJsonFileStore.Delete(path, "Background work store");
        }
    }

    private static BackgroundWorkStoreDto ReadBackgroundWorkStoreNoThrow(string path) {
        return ChatServiceJsonFileStore.ReadOrCreate(
            path,
            maximumBytes: 1024 * 1024,
            static json => JsonSerializer.Deserialize<BackgroundWorkStoreDto>(json),
            static store => store.Version == BackgroundWorkStoreVersion && store.Threads is not null,
            static store => {
                if (store.Threads.Comparer != StringComparer.Ordinal) {
                    store.Threads = new Dictionary<string, BackgroundWorkStoreThreadEntryDto>(store.Threads, StringComparer.Ordinal);
                }
            },
            static () => new BackgroundWorkStoreDto(),
            "Background work store");
    }

    private static void WriteBackgroundWorkStoreNoThrow(string path, BackgroundWorkStoreDto store) {
        ChatServiceJsonFileStore.Write(
            path,
            store,
            static value => JsonSerializer.Serialize(value),
            "Background work store");
    }

    private static void PruneBackgroundWorkStore(BackgroundWorkStoreDto store) {
        if (store.Threads.Count <= MaxTrackedThreadBackgroundWorkContexts) {
            return;
        }

        var removeCount = store.Threads.Count - MaxTrackedThreadBackgroundWorkContexts;
        if (removeCount <= 0) {
            return;
        }

        var threadIdsToRemove = store.Threads
            .Select(pair => (ThreadId: pair.Key, Ticks: pair.Value?.SeenUtcTicks ?? 0))
            .OrderBy(static item => item.Ticks)
            .ThenBy(static item => item.ThreadId, StringComparer.Ordinal)
            .Take(removeCount)
            .Select(static item => item.ThreadId)
            .ToArray();
        for (var i = 0; i < threadIdsToRemove.Length; i++) {
            store.Threads.Remove(threadIdsToRemove[i]);
        }
    }
}
