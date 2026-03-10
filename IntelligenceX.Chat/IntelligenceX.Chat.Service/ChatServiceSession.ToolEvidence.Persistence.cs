using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private const int ToolEvidenceStoreVersion = 1;
    private static readonly object ToolEvidenceStoreLock = new();
    private static readonly JsonSerializerOptions ToolEvidenceStoreJsonOptions = new() {
        WriteIndented = false,
        PropertyNameCaseInsensitive = false
    };

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

    private static string ResolveDefaultToolEvidenceStorePath() {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root)) {
            root = ".";
        }

        return Path.Combine(root, "IntelligenceX.Chat", "tool-evidence-cache.json");
    }

    private string ResolveToolEvidenceStorePath() {
        var pendingActionsPath = ResolvePendingActionsStorePath();
        var directory = Path.GetDirectoryName(pendingActionsPath);
        if (!string.IsNullOrWhiteSpace(directory)) {
            return Path.Combine(directory, "tool-evidence-cache.json");
        }

        return ResolveDefaultToolEvidenceStorePath();
    }

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
        try {
            var path = ResolveToolEvidenceStorePath();
            lock (ToolEvidenceStoreLock) {
                if (!File.Exists(path)) {
                    return;
                }

                File.Delete(path);
            }
        } catch {
            // Best effort only.
        }
    }

    private static ToolEvidenceStoreDto ReadToolEvidenceStoreNoThrow(string path) {
        try {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) {
                return new ToolEvidenceStoreDto();
            }

            var info = new FileInfo(path);
            if (info.Length <= 0 || info.Length > 1024 * 1024) {
                return new ToolEvidenceStoreDto();
            }

            var json = File.ReadAllText(path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json)) {
                return new ToolEvidenceStoreDto();
            }

            var store = JsonSerializer.Deserialize<ToolEvidenceStoreDto>(json, ToolEvidenceStoreJsonOptions);
            if (store is null || store.Version != ToolEvidenceStoreVersion || store.Threads is null) {
                return new ToolEvidenceStoreDto();
            }

            if (store.Threads.Comparer != StringComparer.Ordinal) {
                store.Threads = new Dictionary<string, ToolEvidenceStoreThreadEntryDto>(store.Threads, StringComparer.Ordinal);
            }

            return store;
        } catch (Exception ex) {
            Trace.TraceWarning($"Tool evidence store read failed: {ex.GetType().Name}: {ex.Message}");
            return new ToolEvidenceStoreDto();
        }
    }

    private static void WriteToolEvidenceStoreNoThrow(string path, ToolEvidenceStoreDto store) {
        string? tmp = null;
        try {
            if (string.IsNullOrWhiteSpace(path)) {
                return;
            }

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory)) {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(store, ToolEvidenceStoreJsonOptions);
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
            Trace.TraceWarning($"Tool evidence store write failed: {ex.GetType().Name}: {ex.Message}");
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
