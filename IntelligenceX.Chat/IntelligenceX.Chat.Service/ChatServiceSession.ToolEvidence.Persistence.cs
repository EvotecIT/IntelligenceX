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
    private const int ToolEvidenceStoreVersion = 1;
    private static readonly object ToolEvidenceStoreLock = new();

    private sealed class ToolEvidenceStoreDto {
        public int Version { get; set; } = ToolEvidenceStoreVersion;
        public Dictionary<string, ToolEvidenceStoreThreadEntryDto> Threads { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed class ToolEvidenceStoreThreadEntryDto {
        public ToolEvidenceStoreEntryDto[] Entries { get; set; } = Array.Empty<ToolEvidenceStoreEntryDto>();
        public long SeenUtcTicks { get; set; }
    }

    private sealed class ToolEvidenceStoreEntryDto {
        public string ToolName { get; set; } = string.Empty;
        public string ArgumentsJson { get; set; } = "{}";
        public string Output { get; set; } = string.Empty;
        public string SummaryMarkdown { get; set; } = string.Empty;
        public string ExecutionBackend { get; set; } = string.Empty;
        public long SeenUtcTicks { get; set; }
    }

    private static string ResolveDefaultToolEvidenceStorePath() =>
        ChatServiceJsonFileStore.ResolveDefaultPath("tool-evidence-cache.json");

    private string ResolveToolEvidenceStorePath() =>
        ChatServiceJsonFileStore.ResolveSiblingPath(ResolvePendingActionsStorePath(), "tool-evidence-cache.json");

    private void PersistThreadToolEvidenceSnapshot(string threadId, IReadOnlyCollection<ThreadToolEvidenceEntry> entries) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return;
        }

        var path = ResolveToolEvidenceStorePath();
        lock (ToolEvidenceStoreLock) {
            var store = ReadToolEvidenceStoreNoThrow(path);
            if (entries is null || entries.Count == 0) {
                if (store.Threads.Remove(normalizedThreadId)) {
                    WriteToolEvidenceStoreNoThrow(path, store);
                }
                return;
            }

            var normalizedEntries = entries
                .Where(static entry => !string.IsNullOrWhiteSpace(entry.ToolName))
                .OrderByDescending(static entry => entry.SeenUtcTicks)
                .Take(MaxToolEvidenceEntriesPerThread)
                .Select(static entry => new ToolEvidenceStoreEntryDto {
                    ToolName = (entry.ToolName ?? string.Empty).Trim(),
                    ArgumentsJson = NormalizeArgumentsJsonForReplayContract(entry.ArgumentsJson),
                    Output = CompactToolEvidencePayload((entry.Output ?? string.Empty).Trim()),
                    SummaryMarkdown = CompactToolEvidenceSummary((entry.SummaryMarkdown ?? string.Empty).Trim()),
                    ExecutionBackend = NormalizeToolExecutionBackend(entry.ExecutionBackend),
                    SeenUtcTicks = entry.SeenUtcTicks
                })
                .Where(static entry => entry.ToolName.Length > 0 && (entry.Output.Length > 0 || entry.SummaryMarkdown.Length > 0))
                .ToArray();
            if (normalizedEntries.Length == 0) {
                if (store.Threads.Remove(normalizedThreadId)) {
                    WriteToolEvidenceStoreNoThrow(path, store);
                }
                return;
            }

            var latestSeenTicks = normalizedEntries.Max(static entry => entry.SeenUtcTicks);
            store.Threads[normalizedThreadId] = new ToolEvidenceStoreThreadEntryDto {
                Entries = normalizedEntries,
                SeenUtcTicks = latestSeenTicks
            };

            PruneToolEvidenceStore(store);
            WriteToolEvidenceStoreNoThrow(path, store);
        }
    }

    private bool TryLoadThreadToolEvidenceSnapshot(string threadId, out ThreadToolEvidenceEntry[] entries) {
        entries = Array.Empty<ThreadToolEvidenceEntry>();
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return false;
        }

        var path = ResolveToolEvidenceStorePath();
        lock (ToolEvidenceStoreLock) {
            var store = ReadToolEvidenceStoreNoThrow(path);
            if (!store.Threads.TryGetValue(normalizedThreadId, out var threadEntry) || threadEntry is null) {
                return false;
            }

            if (!TryGetUtcDateTimeFromTicks(threadEntry.SeenUtcTicks, out var seenUtc)) {
                store.Threads.Remove(normalizedThreadId);
                WriteToolEvidenceStoreNoThrow(path, store);
                return false;
            }

            var nowUtc = DateTime.UtcNow;
            if (seenUtc > nowUtc || nowUtc - seenUtc > ThreadToolEvidenceContextMaxAge) {
                store.Threads.Remove(normalizedThreadId);
                WriteToolEvidenceStoreNoThrow(path, store);
                return false;
            }

            var normalizedEntries = (threadEntry.Entries ?? Array.Empty<ToolEvidenceStoreEntryDto>())
                .Where(static entry => entry is not null && !string.IsNullOrWhiteSpace(entry.ToolName))
                .Select(static entry => new ThreadToolEvidenceEntry(
                    ToolName: (entry.ToolName ?? string.Empty).Trim(),
                    ArgumentsJson: NormalizeArgumentsJsonForReplayContract(entry.ArgumentsJson),
                    Output: CompactToolEvidencePayload((entry.Output ?? string.Empty).Trim()),
                    SummaryMarkdown: CompactToolEvidenceSummary((entry.SummaryMarkdown ?? string.Empty).Trim()),
                    ExecutionBackend: NormalizeToolExecutionBackend(entry.ExecutionBackend),
                    SeenUtcTicks: entry.SeenUtcTicks))
                .Where(static entry => entry.ToolName.Length > 0 && (entry.Output.Length > 0 || entry.SummaryMarkdown.Length > 0))
                .OrderByDescending(static entry => entry.SeenUtcTicks)
                .Take(MaxToolEvidenceEntriesPerThread)
                .ToArray();

            if (normalizedEntries.Length == 0) {
                store.Threads.Remove(normalizedThreadId);
                WriteToolEvidenceStoreNoThrow(path, store);
                return false;
            }

            entries = normalizedEntries;
            return true;
        }
    }

    private void ClearThreadToolEvidenceSnapshotsNoThrow() {
        var path = ResolveToolEvidenceStorePath();
        lock (ToolEvidenceStoreLock) {
            ChatServiceJsonFileStore.Delete(path, "Tool evidence store");
        }
    }

    private static ToolEvidenceStoreDto ReadToolEvidenceStoreNoThrow(string path) {
        return ChatServiceJsonFileStore.ReadOrCreate(
            path,
            maximumBytes: 1024 * 1024,
            static json => JsonSerializer.Deserialize<ToolEvidenceStoreDto>(json),
            static store => store.Version == ToolEvidenceStoreVersion && store.Threads is not null,
            static store => {
                if (store.Threads.Comparer != StringComparer.Ordinal) {
                    store.Threads = new Dictionary<string, ToolEvidenceStoreThreadEntryDto>(store.Threads, StringComparer.Ordinal);
                }
            },
            static () => new ToolEvidenceStoreDto(),
            "Tool evidence store");
    }

    private static void WriteToolEvidenceStoreNoThrow(string path, ToolEvidenceStoreDto store) {
        ChatServiceJsonFileStore.Write(
            path,
            store,
            static value => JsonSerializer.Serialize(value),
            "Tool evidence store");
    }

    private static void PruneToolEvidenceStore(ToolEvidenceStoreDto store) {
        if (store.Threads.Count <= MaxTrackedThreadToolEvidenceContexts) {
            return;
        }

        var removeCount = store.Threads.Count - MaxTrackedThreadToolEvidenceContexts;
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
