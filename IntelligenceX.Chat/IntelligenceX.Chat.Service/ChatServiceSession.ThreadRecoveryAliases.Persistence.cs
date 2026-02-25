using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private const int ThreadRecoveryAliasStoreVersion = 1;
    private static readonly object ThreadRecoveryAliasStoreLock = new();
    private static readonly JsonSerializerOptions ThreadRecoveryAliasStoreJsonOptions = new() {
        WriteIndented = false,
        PropertyNameCaseInsensitive = false
    };

    private sealed class ThreadRecoveryAliasStoreDto {
        public int Version { get; set; } = ThreadRecoveryAliasStoreVersion;
        public Dictionary<string, ThreadRecoveryAliasStoreEntryDto> Threads { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed class ThreadRecoveryAliasStoreEntryDto {
        public string RecoveredThreadId { get; set; } = string.Empty;
        public long SeenUtcTicks { get; set; }
    }

    private static string ResolveDefaultThreadRecoveryAliasStorePath() {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root)) {
            root = ".";
        }

        return Path.Combine(root, "IntelligenceX.Chat", "thread-recovery-aliases.json");
    }

    private string ResolveThreadRecoveryAliasStorePath() {
        var pendingActionsPath = ResolvePendingActionsStorePath();
        var directory = Path.GetDirectoryName(pendingActionsPath);
        if (!string.IsNullOrWhiteSpace(directory)) {
            return Path.Combine(directory, "thread-recovery-aliases.json");
        }

        return ResolveDefaultThreadRecoveryAliasStorePath();
    }

    private void PersistRecoveredThreadAliasSnapshot(string originalThreadId, string recoveredThreadId, long seenUtcTicks) {
        var normalizedOriginal = (originalThreadId ?? string.Empty).Trim();
        var normalizedRecovered = (recoveredThreadId ?? string.Empty).Trim();
        if (normalizedOriginal.Length == 0
            || normalizedRecovered.Length == 0
            || string.Equals(normalizedOriginal, normalizedRecovered, StringComparison.Ordinal)
            || seenUtcTicks <= 0) {
            return;
        }

        var path = ResolveThreadRecoveryAliasStorePath();
        lock (ThreadRecoveryAliasStoreLock) {
            var store = ReadThreadRecoveryAliasStoreNoThrow(path);
            store.Threads[normalizedOriginal] = new ThreadRecoveryAliasStoreEntryDto {
                RecoveredThreadId = normalizedRecovered,
                SeenUtcTicks = seenUtcTicks
            };
            PruneThreadRecoveryAliasStore(store);
            WriteThreadRecoveryAliasStoreNoThrow(path, store);
        }
    }

    private void RemoveRecoveredThreadAliasSnapshot(string originalThreadId) {
        var normalizedOriginal = (originalThreadId ?? string.Empty).Trim();
        if (normalizedOriginal.Length == 0) {
            return;
        }

        var path = ResolveThreadRecoveryAliasStorePath();
        lock (ThreadRecoveryAliasStoreLock) {
            var store = ReadThreadRecoveryAliasStoreNoThrow(path);
            if (!store.Threads.Remove(normalizedOriginal)) {
                return;
            }

            WriteThreadRecoveryAliasStoreNoThrow(path, store);
        }
    }

    private void ClearRecoveredThreadAliasSnapshots() {
        var path = ResolveThreadRecoveryAliasStorePath();
        lock (ThreadRecoveryAliasStoreLock) {
            try {
                if (File.Exists(path)) {
                    File.Delete(path);
                }
            } catch (Exception ex) {
                Trace.TraceWarning($"Thread-recovery alias store clear failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    private bool TryLoadRecoveredThreadAliasSnapshot(string originalThreadId, out string recoveredThreadId, out long seenUtcTicks) {
        recoveredThreadId = string.Empty;
        seenUtcTicks = 0;

        var normalizedOriginal = (originalThreadId ?? string.Empty).Trim();
        if (normalizedOriginal.Length == 0) {
            return false;
        }

        var path = ResolveThreadRecoveryAliasStorePath();
        lock (ThreadRecoveryAliasStoreLock) {
            var store = ReadThreadRecoveryAliasStoreNoThrow(path);
            if (!store.Threads.TryGetValue(normalizedOriginal, out var entry) || entry is null) {
                return false;
            }

            var normalizedRecovered = (entry.RecoveredThreadId ?? string.Empty).Trim();
            if (normalizedRecovered.Length == 0 || string.Equals(normalizedRecovered, normalizedOriginal, StringComparison.Ordinal)) {
                store.Threads.Remove(normalizedOriginal);
                WriteThreadRecoveryAliasStoreNoThrow(path, store);
                return false;
            }

            if (!TryGetUtcDateTimeFromTicks(entry.SeenUtcTicks, out var seenUtc)) {
                store.Threads.Remove(normalizedOriginal);
                WriteThreadRecoveryAliasStoreNoThrow(path, store);
                return false;
            }

            var now = DateTime.UtcNow;
            if (seenUtc > now || now - seenUtc > ThreadRecoveryAliasContextMaxAge) {
                store.Threads.Remove(normalizedOriginal);
                WriteThreadRecoveryAliasStoreNoThrow(path, store);
                return false;
            }

            recoveredThreadId = normalizedRecovered;
            seenUtcTicks = entry.SeenUtcTicks;
            return true;
        }
    }

    private static ThreadRecoveryAliasStoreDto ReadThreadRecoveryAliasStoreNoThrow(string path) {
        try {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) {
                return new ThreadRecoveryAliasStoreDto();
            }

            var info = new FileInfo(path);
            if (info.Length <= 0 || info.Length > 1024 * 1024) {
                return new ThreadRecoveryAliasStoreDto();
            }

            var json = File.ReadAllText(path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json)) {
                return new ThreadRecoveryAliasStoreDto();
            }

            var store = JsonSerializer.Deserialize<ThreadRecoveryAliasStoreDto>(json, ThreadRecoveryAliasStoreJsonOptions);
            if (store is null || store.Version != ThreadRecoveryAliasStoreVersion || store.Threads is null) {
                return new ThreadRecoveryAliasStoreDto();
            }

            if (store.Threads.Comparer != StringComparer.Ordinal) {
                store.Threads = new Dictionary<string, ThreadRecoveryAliasStoreEntryDto>(store.Threads, StringComparer.Ordinal);
            }

            return store;
        } catch (Exception ex) {
            Trace.TraceWarning($"Thread-recovery alias store read failed: {ex.GetType().Name}: {ex.Message}");
            return new ThreadRecoveryAliasStoreDto();
        }
    }

    private static void WriteThreadRecoveryAliasStoreNoThrow(string path, ThreadRecoveryAliasStoreDto store) {
        string? tmp = null;
        try {
            if (string.IsNullOrWhiteSpace(path)) {
                return;
            }

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory)) {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(store, ThreadRecoveryAliasStoreJsonOptions);
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
            Trace.TraceWarning($"Thread-recovery alias store write failed: {ex.GetType().Name}: {ex.Message}");
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

    private static void PruneThreadRecoveryAliasStore(ThreadRecoveryAliasStoreDto store) {
        if (store.Threads.Count <= MaxTrackedThreadRecoveryAliases) {
            return;
        }

        var removeCount = store.Threads.Count - MaxTrackedThreadRecoveryAliases;
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
