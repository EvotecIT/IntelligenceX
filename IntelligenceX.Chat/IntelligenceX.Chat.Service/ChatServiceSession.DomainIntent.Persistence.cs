using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private const int DomainIntentStoreVersion = 1;
    private static readonly object DomainIntentStoreLock = new();
    private static readonly JsonSerializerOptions DomainIntentStoreJsonOptions = new() {
        WriteIndented = false,
        PropertyNameCaseInsensitive = false
    };

    private sealed class DomainIntentStoreDto {
        public int Version { get; set; } = DomainIntentStoreVersion;
        public Dictionary<string, DomainIntentStoreEntryDto> Threads { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed class DomainIntentStoreEntryDto {
        public string Family { get; set; } = string.Empty;
        public long SeenUtcTicks { get; set; }
    }

    private static string ResolveDefaultDomainIntentStorePath() {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root)) {
            root = ".";
        }
        return Path.Combine(root, "IntelligenceX.Chat", "domain-intent-families.json");
    }

    private string ResolveDomainIntentStorePath() {
        var pendingActionsPath = ResolvePendingActionsStorePath();
        var directory = Path.GetDirectoryName(pendingActionsPath);
        if (!string.IsNullOrWhiteSpace(directory)) {
            return Path.Combine(directory, "domain-intent-families.json");
        }

        return ResolveDefaultDomainIntentStorePath();
    }

    private void PersistDomainIntentFamilySnapshot(string threadId, string family, long seenUtcTicks) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        var normalizedFamily = (family ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0
            || seenUtcTicks <= 0
            || !IsSupportedDomainIntentFamily(normalizedFamily)) {
            return;
        }

        var path = ResolveDomainIntentStorePath();
        lock (DomainIntentStoreLock) {
            var store = ReadDomainIntentStoreNoThrow(path);
            store.Threads[normalizedThreadId] = new DomainIntentStoreEntryDto {
                Family = normalizedFamily,
                SeenUtcTicks = seenUtcTicks
            };
            PruneDomainIntentStore(store);
            WriteDomainIntentStoreNoThrow(path, store);
        }
    }

    private void RemoveDomainIntentFamilySnapshot(string threadId) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return;
        }

        var path = ResolveDomainIntentStorePath();
        lock (DomainIntentStoreLock) {
            var store = ReadDomainIntentStoreNoThrow(path);
            if (!store.Threads.Remove(normalizedThreadId)) {
                return;
            }

            WriteDomainIntentStoreNoThrow(path, store);
        }
    }

    private void ClearDomainIntentFamilySnapshots() {
        var path = ResolveDomainIntentStorePath();
        lock (DomainIntentStoreLock) {
            try {
                if (File.Exists(path)) {
                    File.Delete(path);
                }
            } catch (Exception ex) {
                Trace.TraceWarning($"Domain intent store clear failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    private bool TryLoadDomainIntentFamilySnapshot(string threadId, out string family, out long seenUtcTicks) {
        family = string.Empty;
        seenUtcTicks = 0;

        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return false;
        }

        var path = ResolveDomainIntentStorePath();
        lock (DomainIntentStoreLock) {
            var store = ReadDomainIntentStoreNoThrow(path);
            if (!store.Threads.TryGetValue(normalizedThreadId, out var entry) || entry is null) {
                return false;
            }

            var normalizedFamily = (entry.Family ?? string.Empty).Trim();
            if (!IsSupportedDomainIntentFamily(normalizedFamily)) {
                store.Threads.Remove(normalizedThreadId);
                WriteDomainIntentStoreNoThrow(path, store);
                return false;
            }

            if (!TryGetUtcDateTimeFromTicks(entry.SeenUtcTicks, out var seenUtc)) {
                store.Threads.Remove(normalizedThreadId);
                WriteDomainIntentStoreNoThrow(path, store);
                return false;
            }

            var now = DateTime.UtcNow;
            if (seenUtc > now || now - seenUtc > DomainIntentFamilyContextMaxAge) {
                store.Threads.Remove(normalizedThreadId);
                WriteDomainIntentStoreNoThrow(path, store);
                return false;
            }

            family = normalizedFamily;
            seenUtcTicks = entry.SeenUtcTicks;
            return true;
        }
    }

    private static bool IsSupportedDomainIntentFamily(string family) {
        var normalizedFamily = (family ?? string.Empty).Trim();
        return string.Equals(normalizedFamily, DomainIntentFamilyAd, StringComparison.Ordinal)
               || string.Equals(normalizedFamily, DomainIntentFamilyPublic, StringComparison.Ordinal);
    }

    private static DomainIntentStoreDto ReadDomainIntentStoreNoThrow(string path) {
        try {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) {
                return new DomainIntentStoreDto();
            }

            var info = new FileInfo(path);
            if (info.Length <= 0 || info.Length > 512 * 1024) {
                return new DomainIntentStoreDto();
            }

            var json = File.ReadAllText(path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json)) {
                return new DomainIntentStoreDto();
            }

            var store = JsonSerializer.Deserialize<DomainIntentStoreDto>(json, DomainIntentStoreJsonOptions);
            if (store is null || store.Version != DomainIntentStoreVersion || store.Threads is null) {
                return new DomainIntentStoreDto();
            }

            if (store.Threads.Comparer != StringComparer.Ordinal) {
                store.Threads = new Dictionary<string, DomainIntentStoreEntryDto>(store.Threads, StringComparer.Ordinal);
            }

            return store;
        } catch (Exception ex) {
            Trace.TraceWarning($"Domain intent store read failed: {ex.GetType().Name}: {ex.Message}");
            return new DomainIntentStoreDto();
        }
    }

    private static void WriteDomainIntentStoreNoThrow(string path, DomainIntentStoreDto store) {
        string? tmp = null;
        try {
            if (string.IsNullOrWhiteSpace(path)) {
                return;
            }

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory)) {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(store, DomainIntentStoreJsonOptions);
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
            Trace.TraceWarning($"Domain intent store write failed: {ex.GetType().Name}: {ex.Message}");
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

    private static void PruneDomainIntentStore(DomainIntentStoreDto store) {
        if (store.Threads.Count <= MaxTrackedDomainIntentFamilyContexts) {
            return;
        }

        var removeCount = store.Threads.Count - MaxTrackedDomainIntentFamilyContexts;
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
