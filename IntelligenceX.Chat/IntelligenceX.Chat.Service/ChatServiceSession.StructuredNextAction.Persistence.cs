using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private const int StructuredNextActionStoreVersion = 1;
    private static readonly object StructuredNextActionStoreLock = new();
    private static readonly JsonSerializerOptions StructuredNextActionStoreJsonOptions = new() {
        WriteIndented = false,
        PropertyNameCaseInsensitive = false
    };

    private sealed class StructuredNextActionStoreDto {
        public int Version { get; set; } = StructuredNextActionStoreVersion;
        public Dictionary<string, StructuredNextActionStoreEntryDto> Threads { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed class StructuredNextActionStoreEntryDto {
        public string SourceToolName { get; set; } = string.Empty;
        public string ToolName { get; set; } = string.Empty;
        public string ArgumentsJson { get; set; } = "{}";
        public bool? Mutating { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string Confidence { get; set; } = string.Empty;
        public long SeenUtcTicks { get; set; }
    }

    private static string ResolveDefaultStructuredNextActionStorePath() {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root)) {
            root = ".";
        }

        return Path.Combine(root, "IntelligenceX.Chat", "structured-next-actions.json");
    }

    private string ResolveStructuredNextActionStorePath() {
        var pendingActionsPath = ResolvePendingActionsStorePath();
        var directory = Path.GetDirectoryName(pendingActionsPath);
        if (!string.IsNullOrWhiteSpace(directory)) {
            return Path.Combine(directory, "structured-next-actions.json");
        }

        return ResolveDefaultStructuredNextActionStorePath();
    }

    private void PersistStructuredNextActionSnapshot(string threadId, StructuredNextActionSnapshot snapshot) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0
            || snapshot.SeenUtcTicks <= 0
            || string.IsNullOrWhiteSpace(snapshot.ToolName)
            || string.IsNullOrWhiteSpace(snapshot.ArgumentsJson)) {
            return;
        }

        var path = ResolveStructuredNextActionStorePath();
        lock (StructuredNextActionStoreLock) {
            var store = ReadStructuredNextActionStoreNoThrow(path);
            store.Threads[normalizedThreadId] = new StructuredNextActionStoreEntryDto {
                SourceToolName = snapshot.SourceToolName.Trim(),
                ToolName = snapshot.ToolName.Trim(),
                ArgumentsJson = snapshot.ArgumentsJson.Trim(),
                Mutating = snapshot.Mutability == ActionMutability.Unknown
                    ? null
                    : snapshot.Mutability == ActionMutability.Mutating,
                Reason = snapshot.Reason.Trim(),
                Confidence = snapshot.Confidence.Trim(),
                SeenUtcTicks = snapshot.SeenUtcTicks
            };
            PruneStructuredNextActionStore(store);
            WriteStructuredNextActionStoreNoThrow(path, store);
        }
    }

    private void RemoveStructuredNextActionSnapshot(string threadId) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return;
        }

        var path = ResolveStructuredNextActionStorePath();
        lock (StructuredNextActionStoreLock) {
            var store = ReadStructuredNextActionStoreNoThrow(path);
            if (!store.Threads.Remove(normalizedThreadId)) {
                return;
            }

            WriteStructuredNextActionStoreNoThrow(path, store);
        }
    }

    private void ClearStructuredNextActionSnapshots() {
        var path = ResolveStructuredNextActionStorePath();
        lock (StructuredNextActionStoreLock) {
            try {
                if (File.Exists(path)) {
                    File.Delete(path);
                }
            } catch (Exception ex) {
                Trace.TraceWarning($"Structured next-action store clear failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    private bool TryLoadStructuredNextActionSnapshot(string threadId, out StructuredNextActionSnapshot snapshot) {
        snapshot = default;
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return false;
        }

        var path = ResolveStructuredNextActionStorePath();
        lock (StructuredNextActionStoreLock) {
            var store = ReadStructuredNextActionStoreNoThrow(path);
            if (!store.Threads.TryGetValue(normalizedThreadId, out var entry) || entry is null) {
                return false;
            }

            var toolName = (entry.ToolName ?? string.Empty).Trim();
            var argumentsJson = (entry.ArgumentsJson ?? string.Empty).Trim();
            if (toolName.Length == 0 || argumentsJson.Length == 0) {
                store.Threads.Remove(normalizedThreadId);
                WriteStructuredNextActionStoreNoThrow(path, store);
                return false;
            }

            if (!TryGetUtcDateTimeFromTicks(entry.SeenUtcTicks, out var seenUtc)) {
                store.Threads.Remove(normalizedThreadId);
                WriteStructuredNextActionStoreNoThrow(path, store);
                return false;
            }

            var now = DateTime.UtcNow;
            if (seenUtc > now || now - seenUtc > StructuredNextActionContextMaxAge) {
                store.Threads.Remove(normalizedThreadId);
                WriteStructuredNextActionStoreNoThrow(path, store);
                return false;
            }

            snapshot = new StructuredNextActionSnapshot(
                SourceToolName: (entry.SourceToolName ?? string.Empty).Trim(),
                ToolName: toolName,
                ArgumentsJson: argumentsJson,
                Mutability: ResolveActionMutabilityFromNullableBoolean(entry.Mutating),
                Reason: (entry.Reason ?? string.Empty).Trim(),
                Confidence: (entry.Confidence ?? string.Empty).Trim(),
                SeenUtcTicks: entry.SeenUtcTicks);
            return true;
        }
    }

    private static StructuredNextActionStoreDto ReadStructuredNextActionStoreNoThrow(string path) {
        try {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) {
                return new StructuredNextActionStoreDto();
            }

            var info = new FileInfo(path);
            if (info.Length <= 0 || info.Length > 1024 * 1024) {
                return new StructuredNextActionStoreDto();
            }

            var json = File.ReadAllText(path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json)) {
                return new StructuredNextActionStoreDto();
            }

            var store = JsonSerializer.Deserialize<StructuredNextActionStoreDto>(json, StructuredNextActionStoreJsonOptions);
            if (store is null || store.Version != StructuredNextActionStoreVersion || store.Threads is null) {
                return new StructuredNextActionStoreDto();
            }

            if (store.Threads.Comparer != StringComparer.Ordinal) {
                store.Threads = new Dictionary<string, StructuredNextActionStoreEntryDto>(store.Threads, StringComparer.Ordinal);
            }

            return store;
        } catch (Exception ex) {
            Trace.TraceWarning($"Structured next-action store read failed: {ex.GetType().Name}: {ex.Message}");
            return new StructuredNextActionStoreDto();
        }
    }

    private static void WriteStructuredNextActionStoreNoThrow(string path, StructuredNextActionStoreDto store) {
        string? tmp = null;
        try {
            if (string.IsNullOrWhiteSpace(path)) {
                return;
            }

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory)) {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(store, StructuredNextActionStoreJsonOptions);
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
            Trace.TraceWarning($"Structured next-action store write failed: {ex.GetType().Name}: {ex.Message}");
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

    private static void PruneStructuredNextActionStore(StructuredNextActionStoreDto store) {
        if (store.Threads.Count <= MaxTrackedStructuredNextActionContexts) {
            return;
        }

        var removeCount = store.Threads.Count - MaxTrackedStructuredNextActionContexts;
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
