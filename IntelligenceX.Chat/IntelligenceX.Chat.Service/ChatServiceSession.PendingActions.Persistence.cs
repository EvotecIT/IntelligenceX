using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private const int PendingActionStoreVersion = 1;
    private static readonly object PendingActionStoreLock = new();
    private static readonly JsonSerializerOptions PendingActionStoreJsonOptions = new() {
        WriteIndented = false,
        PropertyNameCaseInsensitive = false
    };

    private static bool TryGetUtcDateTimeFromTicks(long utcTicks, out DateTime value) {
        value = default;
        if (utcTicks <= 0) {
            return false;
        }
        if (utcTicks < DateTime.MinValue.Ticks || utcTicks > DateTime.MaxValue.Ticks) {
            return false;
        }
        try {
            value = new DateTime(utcTicks, DateTimeKind.Utc);
            return true;
        } catch (ArgumentOutOfRangeException) {
            return false;
        }
    }

    private static void TryHardenPendingActionsStoreAclNoThrow(string path) {
        if (string.IsNullOrWhiteSpace(path)) {
            return;
        }
        if (!OperatingSystem.IsWindows()) {
            return;
        }

        try {
            var currentSid = WindowsIdentity.GetCurrent().User;
            if (currentSid is null) {
                return;
            }

            var security = new FileSecurity();
            security.SetOwner(currentSid);
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new FileSystemAccessRule(currentSid, FileSystemRights.FullControl, AccessControlType.Allow));

            new FileInfo(path).SetAccessControl(security);
        } catch (Exception ex) {
            Trace.TraceWarning($"Pending action store ACL hardening failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private sealed class PendingActionStoreDto {
        public int Version { get; set; } = PendingActionStoreVersion;
        public Dictionary<string, PendingActionStoreEntryDto> Threads { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed class PendingActionStoreEntryDto {
        public long SeenUtcTicks { get; set; }
        public PendingActionDto[] Actions { get; set; } = Array.Empty<PendingActionDto>();
    }

    private sealed class PendingActionDto {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Request { get; set; } = string.Empty;
    }

    private static string ResolveDefaultPendingActionsStorePath() {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root)) {
            root = ".";
        }
        return Path.Combine(root, "IntelligenceX.Chat", "pending-actions.json");
    }

    private string ResolvePendingActionsStorePath() {
        var candidate = (_options.PendingActionsStorePath ?? string.Empty).Trim();
        if (candidate.Length == 0) {
            return ResolveDefaultPendingActionsStorePath();
        }

        // Treat overrides as trusted *file names* under LocalAppData by default to avoid arbitrary-path writes.
        // If a fully-qualified path is provided, only honor it when it still resolves under LocalAppData\IntelligenceX.Chat.
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root)) {
            root = ".";
        }

        var baseDir = Path.Combine(root, "IntelligenceX.Chat");
        var defaultPath = ResolveDefaultPendingActionsStorePath();

        try {
            if (candidate.StartsWith(@"\\", StringComparison.Ordinal)) {
                return defaultPath;
            }

            if (!Path.IsPathFullyQualified(candidate)) {
                // Only allow simple file names (no traversal / no separators) for overrides.
                if (candidate.Contains("..", StringComparison.Ordinal) ||
                    candidate.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }) >= 0) {
                    return defaultPath;
                }
                return Path.Combine(baseDir, candidate);
            }

            var fullCandidate = Path.GetFullPath(candidate);
            var fullBaseDir = Path.GetFullPath(baseDir)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            return fullCandidate.StartsWith(fullBaseDir, StringComparison.OrdinalIgnoreCase)
                ? fullCandidate
                : defaultPath;
        } catch {
            return defaultPath;
        }
    }

    private void PersistPendingActionsSnapshot(string threadId, long seenUtcTicks, PendingAction[] actions) {
        if (string.IsNullOrWhiteSpace(threadId) || actions is null || actions.Length == 0 || seenUtcTicks <= 0) {
            return;
        }

        var path = ResolvePendingActionsStorePath();
        lock (PendingActionStoreLock) {
            var store = ReadPendingActionsStoreNoThrow(path);

            // Normalize and enforce bounds.
            var normalizedId = threadId.Trim();
            var dto = new PendingActionStoreEntryDto {
                SeenUtcTicks = seenUtcTicks,
                Actions = actions
                    .Where(a => !string.IsNullOrWhiteSpace(a.Id))
                    .Take(6)
                    .Select(a => new PendingActionDto {
                        Id = (a.Id ?? string.Empty).Trim(),
                        Title = (a.Title ?? string.Empty).Trim(),
                        Request = (a.Request ?? string.Empty).Trim()
                    })
                    .ToArray()
            };

            store.Threads[normalizedId] = dto;
            PrunePendingActionsStore(store);
            WritePendingActionsStoreNoThrow(path, store);
        }
    }

    private void RemovePendingActionsSnapshot(string threadId) {
        var normalized = (threadId ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return;
        }

        var path = ResolvePendingActionsStorePath();
        lock (PendingActionStoreLock) {
            var store = ReadPendingActionsStoreNoThrow(path);
            if (store.Threads.Remove(normalized)) {
                WritePendingActionsStoreNoThrow(path, store);
            }
        }
    }

    private bool TryLoadPendingActionsSnapshot(string threadId, out long seenUtcTicks, out PendingAction[] actions) {
        seenUtcTicks = 0;
        actions = Array.Empty<PendingAction>();

        var normalized = (threadId ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        var path = ResolvePendingActionsStorePath();
        lock (PendingActionStoreLock) {
            var store = ReadPendingActionsStoreNoThrow(path);
            if (!store.Threads.TryGetValue(normalized, out var entry) || entry is null) {
                return false;
            }

            seenUtcTicks = entry.SeenUtcTicks;
            if (!TryGetUtcDateTimeFromTicks(seenUtcTicks, out var seenUtc)) {
                store.Threads.Remove(normalized);
                WritePendingActionsStoreNoThrow(path, store);
                return false;
            }

            var now = DateTime.UtcNow;
            if (seenUtc > now) {
                store.Threads.Remove(normalized);
                WritePendingActionsStoreNoThrow(path, store);
                return false;
            }

            var age = now - seenUtc;
            if (age > PendingActionContextMaxAge) {
                store.Threads.Remove(normalized);
                WritePendingActionsStoreNoThrow(path, store);
                return false;
            }

            actions = (entry.Actions ?? Array.Empty<PendingActionDto>())
                .Where(a => a is not null && !string.IsNullOrWhiteSpace(a.Id))
                .Take(6)
                .Select(a => new PendingAction(
                    Id: (a.Id ?? string.Empty).Trim(),
                    Title: (a.Title ?? string.Empty).Trim(),
                    Request: (a.Request ?? string.Empty).Trim()))
                .ToArray();

            if (actions.Length == 0) {
                store.Threads.Remove(normalized);
                WritePendingActionsStoreNoThrow(path, store);
                return false;
            }

            return true;
        }
    }

    private static PendingActionStoreDto ReadPendingActionsStoreNoThrow(string path) {
        try {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) {
                return new PendingActionStoreDto();
            }

            var info = new FileInfo(path);
            if (info.Length <= 0 || info.Length > 1024 * 1024) {
                // Cap read size to avoid local DoS via gigantic store files.
                return new PendingActionStoreDto();
            }

            var json = File.ReadAllText(path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json)) {
                return new PendingActionStoreDto();
            }

            var store = JsonSerializer.Deserialize<PendingActionStoreDto>(json, PendingActionStoreJsonOptions);
            if (store is null || store.Version != PendingActionStoreVersion || store.Threads is null) {
                return new PendingActionStoreDto();
            }

            // Ensure dictionary comparer matches expectations.
            if (store.Threads.Comparer != StringComparer.Ordinal) {
                store.Threads = new Dictionary<string, PendingActionStoreEntryDto>(store.Threads, StringComparer.Ordinal);
            }

            return store;
        } catch (Exception ex) {
            Trace.TraceWarning($"Pending action store read failed: {ex.GetType().Name}: {ex.Message}");
            return new PendingActionStoreDto();
        }
    }

    private static void WritePendingActionsStoreNoThrow(string path, PendingActionStoreDto store) {
        string? tmp = null;
        try {
            if (string.IsNullOrWhiteSpace(path)) {
                return;
            }

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir)) {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(store, PendingActionStoreJsonOptions);
            var fileName = Path.GetFileName(path);
            var tmpName = $"{fileName}.{Guid.NewGuid():N}.tmp";
            tmp = string.IsNullOrWhiteSpace(dir) ? tmpName : Path.Combine(dir!, tmpName);

            // CreateNew avoids clobbering attacker-planted tmp files.
            using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                // Harden as early as possible; with FileShare.None other users can't open this file while we write.
                TryHardenPendingActionsStoreAclNoThrow(tmp);
                using var writer = new StreamWriter(fs, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                writer.Write(json);
                writer.Flush();
                fs.Flush(true);
            }

            if (File.Exists(path)) {
                // Atomic swap (best-effort) to avoid losing the store if we crash mid-write.
                File.Replace(tmp, path, null, ignoreMetadataErrors: true);
            } else {
                File.Move(tmp, path);
            }

            TryHardenPendingActionsStoreAclNoThrow(path);
        } catch (Exception ex) {
            Trace.TraceWarning($"Pending action store write failed: {ex.GetType().Name}: {ex.Message}");
        } finally {
            if (!string.IsNullOrWhiteSpace(tmp) && File.Exists(tmp)) {
                try {
                    File.Delete(tmp);
                } catch {
                    // Ignore cleanup failures; store writes are best-effort.
                }
            }
        }
    }

    private static void PrunePendingActionsStore(PendingActionStoreDto store) {
        if (store.Threads.Count <= MaxTrackedPendingActionContexts) {
            return;
        }

        // Keep most-recent entries only.
        var toRemove = store.Threads
            .OrderByDescending(kvp => kvp.Value?.SeenUtcTicks ?? 0L)
            .Skip(MaxTrackedPendingActionContexts)
            .Select(kvp => kvp.Key)
            .ToArray();

        foreach (var key in toRemove) {
            store.Threads.Remove(key);
        }
    }
}
