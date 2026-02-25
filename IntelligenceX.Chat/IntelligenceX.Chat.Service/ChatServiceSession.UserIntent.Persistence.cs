using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private const int UserIntentStoreVersion = 1;
    private static readonly object UserIntentStoreLock = new();
    private static readonly JsonSerializerOptions UserIntentStoreJsonOptions = new() {
        WriteIndented = false,
        PropertyNameCaseInsensitive = false
    };

    private sealed class UserIntentStoreDto {
        public int Version { get; set; } = UserIntentStoreVersion;
        public Dictionary<string, UserIntentStoreEntryDto> Threads { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed class UserIntentStoreEntryDto {
        public string Intent { get; set; } = string.Empty;
        public long SeenUtcTicks { get; set; }
    }

    private static string ResolveDefaultUserIntentStorePath() {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root)) {
            root = ".";
        }

        return Path.Combine(root, "IntelligenceX.Chat", "user-intents.json");
    }

    private string ResolveUserIntentStorePath() {
        var pendingActionsPath = ResolvePendingActionsStorePath();
        var directory = Path.GetDirectoryName(pendingActionsPath);
        if (!string.IsNullOrWhiteSpace(directory)) {
            return Path.Combine(directory, "user-intents.json");
        }

        return ResolveDefaultUserIntentStorePath();
    }

    private void PersistUserIntentSnapshot(string threadId, string intent, long seenUtcTicks) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        var normalizedIntent = (intent ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0 || normalizedIntent.Length == 0 || seenUtcTicks <= 0) {
            return;
        }

        if (normalizedIntent.Length > 600) {
            normalizedIntent = normalizedIntent[..600];
        }

        var path = ResolveUserIntentStorePath();
        lock (UserIntentStoreLock) {
            var store = ReadUserIntentStoreNoThrow(path);
            store.Threads[normalizedThreadId] = new UserIntentStoreEntryDto {
                Intent = normalizedIntent,
                SeenUtcTicks = seenUtcTicks
            };
            PruneUserIntentStore(store);
            WriteUserIntentStoreNoThrow(path, store);
        }
    }

    private void RemoveUserIntentSnapshot(string threadId) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return;
        }

        var path = ResolveUserIntentStorePath();
        lock (UserIntentStoreLock) {
            var store = ReadUserIntentStoreNoThrow(path);
            if (!store.Threads.Remove(normalizedThreadId)) {
                return;
            }

            WriteUserIntentStoreNoThrow(path, store);
        }
    }

    private bool TryLoadUserIntentSnapshot(string threadId, out string intent, out long seenUtcTicks) {
        intent = string.Empty;
        seenUtcTicks = 0;

        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return false;
        }

        var path = ResolveUserIntentStorePath();
        lock (UserIntentStoreLock) {
            var store = ReadUserIntentStoreNoThrow(path);
            if (!store.Threads.TryGetValue(normalizedThreadId, out var entry) || entry is null) {
                return false;
            }

            var normalizedIntent = (entry.Intent ?? string.Empty).Trim();
            if (normalizedIntent.Length == 0) {
                store.Threads.Remove(normalizedThreadId);
                WriteUserIntentStoreNoThrow(path, store);
                return false;
            }

            if (normalizedIntent.Length > 600) {
                normalizedIntent = normalizedIntent[..600];
            }

            if (!TryGetUtcDateTimeFromTicks(entry.SeenUtcTicks, out var seenUtc)) {
                store.Threads.Remove(normalizedThreadId);
                WriteUserIntentStoreNoThrow(path, store);
                return false;
            }

            var now = DateTime.UtcNow;
            if (seenUtc > now || now - seenUtc > UserIntentContextMaxAge) {
                store.Threads.Remove(normalizedThreadId);
                WriteUserIntentStoreNoThrow(path, store);
                return false;
            }

            intent = normalizedIntent;
            seenUtcTicks = entry.SeenUtcTicks;
            return true;
        }
    }

    private static UserIntentStoreDto ReadUserIntentStoreNoThrow(string path) {
        try {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) {
                return new UserIntentStoreDto();
            }

            var info = new FileInfo(path);
            if (info.Length <= 0 || info.Length > 512 * 1024) {
                return new UserIntentStoreDto();
            }

            var json = File.ReadAllText(path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json)) {
                return new UserIntentStoreDto();
            }

            var store = JsonSerializer.Deserialize<UserIntentStoreDto>(json, UserIntentStoreJsonOptions);
            if (store is null || store.Version != UserIntentStoreVersion || store.Threads is null) {
                return new UserIntentStoreDto();
            }

            if (store.Threads.Comparer != StringComparer.Ordinal) {
                store.Threads = new Dictionary<string, UserIntentStoreEntryDto>(store.Threads, StringComparer.Ordinal);
            }

            return store;
        } catch (Exception ex) {
            Trace.TraceWarning($"User intent store read failed: {ex.GetType().Name}: {ex.Message}");
            return new UserIntentStoreDto();
        }
    }

    private static void WriteUserIntentStoreNoThrow(string path, UserIntentStoreDto store) {
        string? tmp = null;
        try {
            if (string.IsNullOrWhiteSpace(path)) {
                return;
            }

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory)) {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(store, UserIntentStoreJsonOptions);
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
            Trace.TraceWarning($"User intent store write failed: {ex.GetType().Name}: {ex.Message}");
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

    private static void PruneUserIntentStore(UserIntentStoreDto store) {
        if (store.Threads.Count <= MaxTrackedUserIntentContexts) {
            return;
        }

        var removeCount = store.Threads.Count - MaxTrackedUserIntentContexts;
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
