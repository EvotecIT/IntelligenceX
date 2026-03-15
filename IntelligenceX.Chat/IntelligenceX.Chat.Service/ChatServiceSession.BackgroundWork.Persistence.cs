using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using IntelligenceX.Tools;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private const int BackgroundWorkStoreVersion = 4;
    private static readonly object BackgroundWorkStoreLock = new();
    private static readonly JsonSerializerOptions BackgroundWorkStoreJsonOptions = new() {
        WriteIndented = false,
        PropertyNameCaseInsensitive = false
    };

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
        public BackgroundWorkStoreItemDto[] Items { get; set; } = Array.Empty<BackgroundWorkStoreItemDto>();
    }

    private sealed class BackgroundWorkStoreItemDto {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Request { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
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

    private static string ResolveDefaultBackgroundWorkStorePath() {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root)) {
            root = ".";
        }

        return Path.Combine(root, "IntelligenceX.Chat", "background-work-cache.json");
    }

    private string ResolveBackgroundWorkStorePath() {
        var pendingActionsPath = ResolvePendingActionsStorePath();
        var directory = Path.GetDirectoryName(pendingActionsPath);
        if (!string.IsNullOrWhiteSpace(directory)) {
            return Path.Combine(directory, "background-work-cache.json");
        }

        return ResolveDefaultBackgroundWorkStorePath();
    }

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
            var normalizedItems = (entry.Items ?? Array.Empty<BackgroundWorkStoreItemDto>())
                .Where(static item => item is not null && !string.IsNullOrWhiteSpace(item.Id))
                .Take(MaxBackgroundWorkItems)
                .Select(item => new ThreadBackgroundWorkItem(
                    Id: (item.Id ?? string.Empty).Trim(),
                    Title: (item.Title ?? string.Empty).Trim(),
                    Request: NormalizeWorkingMemoryAnswerPlanFocus(item.Request),
                    State: NormalizeBackgroundWorkState(item.State),
                    EvidenceToolNames: NormalizeBackgroundWorkToolNames(item.EvidenceToolNames),
                    Kind: NormalizeBackgroundWorkKind(item.Kind),
                    Mutability: NormalizeBackgroundWorkMutability(item.Mutability),
                    SourceToolName: NormalizeToolNameForAnswerPlan(item.SourceToolName),
                    SourceCallId: (item.SourceCallId ?? string.Empty).Trim(),
                    TargetPackId: (item.TargetPackId ?? string.Empty).Trim(),
                    TargetToolName: NormalizeToolNameForAnswerPlan(item.TargetToolName),
                    FollowUpKind: ToolHandoffFollowUpKinds.Normalize(item.FollowUpKind),
                    FollowUpPriority: ToolHandoffFollowUpPriorities.Normalize(item.FollowUpPriority),
                    PreparedArgumentsJson: NormalizeBackgroundWorkArgumentsJson(item.PreparedArgumentsJson),
                    ResultReference: (item.ResultReference ?? string.Empty).Trim(),
                    ExecutionAttemptCount: Math.Max(0, item.ExecutionAttemptCount),
                    LastExecutionCallId: (item.LastExecutionCallId ?? string.Empty).Trim(),
                    LastExecutionStartedUtcTicks: Math.Max(0, item.LastExecutionStartedUtcTicks),
                    LastExecutionFinishedUtcTicks: Math.Max(0, item.LastExecutionFinishedUtcTicks),
                    LeaseExpiresUtcTicks: Math.Max(0, item.LeaseExpiresUtcTicks),
                    CreatedUtcTicks: item.CreatedUtcTicks,
                    UpdatedUtcTicks: item.UpdatedUtcTicks > 0 ? item.UpdatedUtcTicks : item.CreatedUtcTicks))
                .Where(static item => item.Id.Length > 0)
                .ToArray();

            if (normalizedItems.Length == 0) {
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
                Items: normalizedItems);
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

    private void ClearBackgroundWorkSnapshots() {
        var path = ResolveBackgroundWorkStorePath();
        lock (BackgroundWorkStoreLock) {
            try {
                if (File.Exists(path)) {
                    File.Delete(path);
                }
            } catch (Exception ex) {
                Trace.TraceWarning($"Background work store clear failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    private static BackgroundWorkStoreDto ReadBackgroundWorkStoreNoThrow(string path) {
        try {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) {
                return new BackgroundWorkStoreDto();
            }

            var info = new FileInfo(path);
            if (info.Length <= 0 || info.Length > 1024 * 1024) {
                return new BackgroundWorkStoreDto();
            }

            var json = File.ReadAllText(path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json)) {
                return new BackgroundWorkStoreDto();
            }

            var store = JsonSerializer.Deserialize<BackgroundWorkStoreDto>(json, BackgroundWorkStoreJsonOptions);
            if (store is null || store.Version != BackgroundWorkStoreVersion || store.Threads is null) {
                return new BackgroundWorkStoreDto();
            }

            if (store.Threads.Comparer != StringComparer.Ordinal) {
                store.Threads = new Dictionary<string, BackgroundWorkStoreThreadEntryDto>(store.Threads, StringComparer.Ordinal);
            }

            return store;
        } catch (Exception ex) {
            Trace.TraceWarning($"Background work store read failed: {ex.GetType().Name}: {ex.Message}");
            return new BackgroundWorkStoreDto();
        }
    }

    private static void WriteBackgroundWorkStoreNoThrow(string path, BackgroundWorkStoreDto store) {
        string? tmp = null;
        try {
            if (string.IsNullOrWhiteSpace(path)) {
                return;
            }

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory)) {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(store, BackgroundWorkStoreJsonOptions);
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
            Trace.TraceWarning($"Background work store write failed: {ex.GetType().Name}: {ex.Message}");
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
