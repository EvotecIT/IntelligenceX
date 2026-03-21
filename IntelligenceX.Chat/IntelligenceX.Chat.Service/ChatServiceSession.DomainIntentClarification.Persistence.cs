using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private const int DomainIntentClarificationStoreVersion = 1;
    private static readonly object DomainIntentClarificationStoreLock = new();
    private static readonly JsonSerializerOptions DomainIntentClarificationStoreJsonOptions = new() {
        WriteIndented = false,
        PropertyNameCaseInsensitive = false
    };

    private sealed class DomainIntentClarificationStoreDto {
        public int Version { get; set; } = DomainIntentClarificationStoreVersion;
        public Dictionary<string, DomainIntentClarificationStoreEntryDto> Threads { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed class DomainIntentClarificationStoreEntryDto {
        public long SeenUtcTicks { get; set; }
        public string[] Families { get; set; } = Array.Empty<string>();
    }

    private static string ResolveDefaultDomainIntentClarificationStorePath() {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root)) {
            root = ".";
        }

        return Path.Combine(root, "IntelligenceX.Chat", "domain-intent-clarifications.json");
    }

    private string ResolveDomainIntentClarificationStorePath() {
        var pendingActionsPath = ResolvePendingActionsStorePath();
        var directory = Path.GetDirectoryName(pendingActionsPath);
        if (!string.IsNullOrWhiteSpace(directory)) {
            return Path.Combine(directory, "domain-intent-clarifications.json");
        }

        return ResolveDefaultDomainIntentClarificationStorePath();
    }

    private void PersistPendingDomainIntentClarificationSnapshot(string threadId, long seenUtcTicks, IReadOnlyList<string>? families) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0 || seenUtcTicks <= 0) {
            return;
        }

        var path = ResolveDomainIntentClarificationStorePath();
        lock (DomainIntentClarificationStoreLock) {
            var store = ReadDomainIntentClarificationStoreNoThrow(path);
            store.Threads[normalizedThreadId] = new DomainIntentClarificationStoreEntryDto {
                SeenUtcTicks = seenUtcTicks,
                Families = NormalizePendingDomainIntentClarificationFamilies(families)
            };
            PruneDomainIntentClarificationStore(store);
            WriteDomainIntentClarificationStoreNoThrow(path, store);
        }
    }

    private void RemovePendingDomainIntentClarificationSnapshot(string threadId) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return;
        }

        var path = ResolveDomainIntentClarificationStorePath();
        lock (DomainIntentClarificationStoreLock) {
            var store = ReadDomainIntentClarificationStoreNoThrow(path);
            if (!store.Threads.Remove(normalizedThreadId)) {
                return;
            }

            WriteDomainIntentClarificationStoreNoThrow(path, store);
        }
    }

    private void ClearPendingDomainIntentClarificationSnapshots() {
        var path = ResolveDomainIntentClarificationStorePath();
        lock (DomainIntentClarificationStoreLock) {
            try {
                if (File.Exists(path)) {
                    File.Delete(path);
                }
            } catch (Exception ex) {
                Trace.TraceWarning($"Domain intent clarification store clear failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    private bool TryLoadPendingDomainIntentClarificationSnapshot(string threadId, out long seenUtcTicks, out string[] families) {
        seenUtcTicks = 0;
        families = Array.Empty<string>();
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return false;
        }

        var path = ResolveDomainIntentClarificationStorePath();
        lock (DomainIntentClarificationStoreLock) {
            var store = ReadDomainIntentClarificationStoreNoThrow(path);
            if (!store.Threads.TryGetValue(normalizedThreadId, out var entry) || entry is null) {
                return false;
            }

            if (!TryGetUtcDateTimeFromTicks(entry.SeenUtcTicks, out var seenUtc)) {
                store.Threads.Remove(normalizedThreadId);
                WriteDomainIntentClarificationStoreNoThrow(path, store);
                return false;
            }

            var now = DateTime.UtcNow;
            if (seenUtc > now || now - seenUtc > DomainIntentClarificationContextMaxAge) {
                store.Threads.Remove(normalizedThreadId);
                WriteDomainIntentClarificationStoreNoThrow(path, store);
                return false;
            }

            seenUtcTicks = entry.SeenUtcTicks;
            families = NormalizePendingDomainIntentClarificationFamilies(entry.Families);
            return true;
        }
    }

    private static string[] NormalizePendingDomainIntentClarificationFamilies(IReadOnlyList<string>? families) {
        if (families is not { Count: > 0 }) {
            return Array.Empty<string>();
        }

        var normalized = new List<string>(families.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < families.Count; i++) {
            if (!TryNormalizeDomainIntentFamily(families[i], out var family) || !seen.Add(family)) {
                continue;
            }

            normalized.Add(family);
        }

        if (normalized.Count == 0) {
            return Array.Empty<string>();
        }

        normalized.Sort(StringComparer.Ordinal);
        return normalized.ToArray();
    }

    private static DomainIntentClarificationStoreDto ReadDomainIntentClarificationStoreNoThrow(string path) {
        try {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) {
                return new DomainIntentClarificationStoreDto();
            }

            var info = new FileInfo(path);
            if (info.Length <= 0 || info.Length > 256 * 1024) {
                return new DomainIntentClarificationStoreDto();
            }

            var json = File.ReadAllText(path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json)) {
                return new DomainIntentClarificationStoreDto();
            }

            var store = JsonSerializer.Deserialize<DomainIntentClarificationStoreDto>(json, DomainIntentClarificationStoreJsonOptions);
            if (store is null || store.Version != DomainIntentClarificationStoreVersion || store.Threads is null) {
                return new DomainIntentClarificationStoreDto();
            }

            if (store.Threads.Comparer != StringComparer.Ordinal) {
                store.Threads = new Dictionary<string, DomainIntentClarificationStoreEntryDto>(store.Threads, StringComparer.Ordinal);
            }

            return store;
        } catch (Exception ex) {
            Trace.TraceWarning($"Domain intent clarification store read failed: {ex.GetType().Name}: {ex.Message}");
            return new DomainIntentClarificationStoreDto();
        }
    }

    private static void WriteDomainIntentClarificationStoreNoThrow(string path, DomainIntentClarificationStoreDto store) {
        string? tmp = null;
        try {
            if (string.IsNullOrWhiteSpace(path)) {
                return;
            }

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory)) {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(store, DomainIntentClarificationStoreJsonOptions);
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
            Trace.TraceWarning($"Domain intent clarification store write failed: {ex.GetType().Name}: {ex.Message}");
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

    private static void PruneDomainIntentClarificationStore(DomainIntentClarificationStoreDto store) {
        if (store.Threads.Count <= MaxTrackedDomainIntentClarificationContexts) {
            return;
        }

        var removeCount = store.Threads.Count - MaxTrackedDomainIntentClarificationContexts;
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
