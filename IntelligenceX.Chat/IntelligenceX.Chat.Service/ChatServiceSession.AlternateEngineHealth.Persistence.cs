using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Chat.Abstractions.Protocol;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private const int AlternateEngineHealthStoreVersion = 1;
    private const int MaxRememberedAlternateEngineHealthEntriesPerThread = 24;
    private static readonly TimeSpan AlternateEngineHealthContextMaxAge = TimeSpan.FromMinutes(30);
    private static readonly object AlternateEngineHealthStoreLock = new();
    private static readonly JsonSerializerOptions AlternateEngineHealthStoreJsonOptions = new() {
        WriteIndented = false,
        PropertyNameCaseInsensitive = false
    };

    private sealed class AlternateEngineHealthStoreDto {
        public int Version { get; set; } = AlternateEngineHealthStoreVersion;
        public Dictionary<string, AlternateEngineHealthStoreEntryDto[]> Threads { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed class AlternateEngineHealthStoreEntryDto {
        public string ToolName { get; set; } = string.Empty;
        public string EngineId { get; set; } = string.Empty;
        public string ErrorCode { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
        public long LastSuccessUtcTicks { get; set; }
        public long LastFailureUtcTicks { get; set; }
    }

    private readonly record struct AlternateEngineHealthSnapshot(
        string ToolName,
        string EngineId,
        string ErrorCode,
        string Error,
        long LastSuccessUtcTicks,
        long LastFailureUtcTicks);

    private static string ResolveDefaultAlternateEngineHealthStorePath() {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root)) {
            root = ".";
        }

        return Path.Combine(root, "IntelligenceX.Chat", "alternate-engine-health.json");
    }

    private string ResolveAlternateEngineHealthStorePath() {
        var pendingActionsPath = ResolvePendingActionsStorePath();
        var directory = Path.GetDirectoryName(pendingActionsPath);
        if (!string.IsNullOrWhiteSpace(directory)) {
            return Path.Combine(directory, "alternate-engine-health.json");
        }

        return ResolveDefaultAlternateEngineHealthStorePath();
    }

    private void RememberAlternateEngineOutcome(string threadId, string toolName, string engineId, ToolOutputDto output, long? seenUtcTicks = null) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        var normalizedToolName = (toolName ?? string.Empty).Trim();
        var normalizedEngineId = NormalizeAlternateEngineHealthToken(engineId);
        if (normalizedThreadId.Length == 0 || normalizedToolName.Length == 0 || normalizedEngineId.Length == 0) {
            return;
        }

        var isSuccess = IsSuccessfulToolOutput(output);
        var nowTicks = seenUtcTicks.GetValueOrDefault(DateTime.UtcNow.Ticks);
        PersistAlternateEngineHealthSnapshot(
            normalizedThreadId,
            new AlternateEngineHealthSnapshot(
                ToolName: normalizedToolName,
                EngineId: normalizedEngineId,
                ErrorCode: isSuccess
                    ? string.Empty
                    : NormalizeAlternateEngineHealthText(output.ErrorCode, maxLength: 128),
                Error: isSuccess
                    ? string.Empty
                    : NormalizeAlternateEngineHealthText(output.Error, maxLength: 280),
                LastSuccessUtcTicks: isSuccess ? nowTicks : 0,
                LastFailureUtcTicks: isSuccess ? 0 : nowTicks));
    }

    private string[] OrderAlternateEngineIdsByHealth(
        string threadId,
        string toolName,
        IReadOnlyList<string> candidateEngineIds,
        IReadOnlySet<string>? allowedSelectorValues = null,
        IReadOnlySet<string>? excludedEngineIds = null) {
        if (candidateEngineIds is not { Count: > 0 }) {
            return Array.Empty<string>();
        }

        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        var normalizedToolName = (toolName ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0 || normalizedToolName.Length == 0) {
            return candidateEngineIds
                .Where(static candidate => !string.IsNullOrWhiteSpace(candidate))
                .Select(static candidate => candidate.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        var historyByEngineId = SnapshotAlternateEngineHealthByEngineId(normalizedThreadId, normalizedToolName);
        var ranked = new List<(string EngineId, double Score, int Index)>(candidateEngineIds.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < candidateEngineIds.Count; i++) {
            var normalizedEngineId = NormalizeAlternateEngineHealthToken(candidateEngineIds[i]);
            if (normalizedEngineId.Length == 0
                || !seen.Add(normalizedEngineId)
                || excludedEngineIds?.Contains(normalizedEngineId) == true
                || (allowedSelectorValues is not null
                    && allowedSelectorValues.Count > 0
                    && !allowedSelectorValues.Contains(normalizedEngineId))) {
                continue;
            }

            var score = 0d;
            if (historyByEngineId.TryGetValue(normalizedEngineId, out var snapshot)) {
                score += ComputeAlternateEngineHealthScore(snapshot);
            }

            ranked.Add((normalizedEngineId, score, i));
        }

        if (ranked.Count == 0) {
            return Array.Empty<string>();
        }

        return ranked
            .OrderByDescending(static candidate => candidate.Score)
            .ThenBy(static candidate => candidate.Index)
            .Select(static candidate => candidate.EngineId)
            .ToArray();
    }

    private static double ComputeAlternateEngineHealthScore(AlternateEngineHealthSnapshot snapshot) {
        var score = 0d;
        if (TryGetUtcDateTimeFromTicks(snapshot.LastSuccessUtcTicks, out var successUtc)) {
            var sinceSuccess = DateTime.UtcNow - successUtc;
            if (sinceSuccess <= AlternateEngineHealthContextMaxAge) {
                score += 2.0d;
                if (sinceSuccess <= TimeSpan.FromMinutes(10)) {
                    score += 0.4d;
                }
            }
        }

        if (TryGetUtcDateTimeFromTicks(snapshot.LastFailureUtcTicks, out var failureUtc)) {
            var sinceFailure = DateTime.UtcNow - failureUtc;
            if (sinceFailure <= AlternateEngineHealthContextMaxAge) {
                score -= 2.6d;
                if (sinceFailure <= TimeSpan.FromMinutes(10)) {
                    score -= 0.4d;
                }
            }
        }

        return score;
    }

    private Dictionary<string, AlternateEngineHealthSnapshot> SnapshotAlternateEngineHealthByEngineId(string threadId, string toolName) {
        if (!TryLoadAlternateEngineHealthSnapshots(threadId, out var entries) || entries.Length == 0) {
            return new Dictionary<string, AlternateEngineHealthSnapshot>(StringComparer.OrdinalIgnoreCase);
        }

        var normalizedToolName = (toolName ?? string.Empty).Trim();
        if (normalizedToolName.Length == 0) {
            return new Dictionary<string, AlternateEngineHealthSnapshot>(StringComparer.OrdinalIgnoreCase);
        }

        var snapshotsByEngineId = new Dictionary<string, AlternateEngineHealthSnapshot>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < entries.Length; i++) {
            var entry = entries[i];
            if (!string.Equals(entry.ToolName, normalizedToolName, StringComparison.OrdinalIgnoreCase)
                || entry.EngineId.Length == 0) {
                continue;
            }

            if (!snapshotsByEngineId.TryGetValue(entry.EngineId, out var existing)
                || Math.Max(entry.LastSuccessUtcTicks, entry.LastFailureUtcTicks) > Math.Max(existing.LastSuccessUtcTicks, existing.LastFailureUtcTicks)) {
                snapshotsByEngineId[entry.EngineId] = entry;
            }
        }

        return snapshotsByEngineId;
    }

    private static string NormalizeAlternateEngineHealthToken(string? value) {
        return (value ?? string.Empty).Trim().ToLowerInvariant();
    }

    private static string NormalizeAlternateEngineHealthText(string? value, int maxLength) {
        var normalized = ToolHealthDiagnostics.CompactOneLine(value);
        if (normalized.Length == 0) {
            return string.Empty;
        }

        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private void PersistAlternateEngineHealthSnapshot(string threadId, AlternateEngineHealthSnapshot snapshot) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0
            || snapshot.ToolName.Length == 0
            || snapshot.EngineId.Length == 0
            || (snapshot.LastSuccessUtcTicks <= 0 && snapshot.LastFailureUtcTicks <= 0)) {
            return;
        }

        var path = ResolveAlternateEngineHealthStorePath();
        lock (AlternateEngineHealthStoreLock) {
            var store = ReadAlternateEngineHealthStoreNoThrow(path);
            var latestByKey = new Dictionary<string, AlternateEngineHealthStoreEntryDto>(StringComparer.OrdinalIgnoreCase);
            if (store.Threads.TryGetValue(normalizedThreadId, out var entries) && entries is { Length: > 0 }) {
                for (var i = 0; i < entries.Length; i++) {
                    var entry = entries[i];
                    if (entry is null) {
                        continue;
                    }

                    var normalizedToolName = (entry.ToolName ?? string.Empty).Trim();
                    var normalizedEngineId = NormalizeAlternateEngineHealthToken(entry.EngineId);
                    if (normalizedToolName.Length == 0 || normalizedEngineId.Length == 0) {
                        continue;
                    }

                    latestByKey[BuildAlternateEngineHealthKey(normalizedToolName, normalizedEngineId)] = entry;
                }
            }

            var key = BuildAlternateEngineHealthKey(snapshot.ToolName, snapshot.EngineId);
            latestByKey[key] = new AlternateEngineHealthStoreEntryDto {
                ToolName = snapshot.ToolName,
                EngineId = snapshot.EngineId,
                ErrorCode = snapshot.ErrorCode,
                Error = snapshot.Error,
                LastSuccessUtcTicks = snapshot.LastSuccessUtcTicks > 0
                    ? snapshot.LastSuccessUtcTicks
                    : latestByKey.TryGetValue(key, out var existing) ? existing.LastSuccessUtcTicks : 0,
                LastFailureUtcTicks = snapshot.LastFailureUtcTicks > 0
                    ? snapshot.LastFailureUtcTicks
                    : latestByKey.TryGetValue(key, out var existingFailure) ? existingFailure.LastFailureUtcTicks : 0
            };

            store.Threads[normalizedThreadId] = latestByKey.Values
                .OrderByDescending(static entry => Math.Max(entry.LastSuccessUtcTicks, entry.LastFailureUtcTicks))
                .ThenBy(static entry => entry.ToolName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static entry => entry.EngineId, StringComparer.OrdinalIgnoreCase)
                .Take(MaxRememberedAlternateEngineHealthEntriesPerThread)
                .ToArray();
            PruneAlternateEngineHealthStore(store);
            WriteAlternateEngineHealthStoreNoThrow(path, store);
        }
    }

    private void ClearAlternateEngineHealthSnapshots() {
        var path = ResolveAlternateEngineHealthStorePath();
        lock (AlternateEngineHealthStoreLock) {
            try {
                if (File.Exists(path)) {
                    File.Delete(path);
                }
            } catch (Exception ex) {
                Trace.TraceWarning($"Alternate engine health store clear failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    private bool TryLoadAlternateEngineHealthSnapshots(string threadId, out AlternateEngineHealthSnapshot[] entries) {
        entries = Array.Empty<AlternateEngineHealthSnapshot>();

        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return false;
        }

        var path = ResolveAlternateEngineHealthStorePath();
        lock (AlternateEngineHealthStoreLock) {
            var store = ReadAlternateEngineHealthStoreNoThrow(path);
            if (!store.Threads.TryGetValue(normalizedThreadId, out var persistedEntries) || persistedEntries is not { Length: > 0 }) {
                return false;
            }

            var nowUtc = DateTime.UtcNow;
            var sanitizedEntries = persistedEntries
                .Where(static entry => entry is not null)
                .Select(static entry => new AlternateEngineHealthSnapshot(
                    ToolName: (entry!.ToolName ?? string.Empty).Trim(),
                    EngineId: NormalizeAlternateEngineHealthToken(entry!.EngineId),
                    ErrorCode: NormalizeAlternateEngineHealthText(entry!.ErrorCode, maxLength: 128),
                    Error: NormalizeAlternateEngineHealthText(entry!.Error, maxLength: 280),
                    LastSuccessUtcTicks: entry!.LastSuccessUtcTicks,
                    LastFailureUtcTicks: entry!.LastFailureUtcTicks))
                .Where(entry =>
                    entry.ToolName.Length > 0
                    && entry.EngineId.Length > 0
                    && HasFreshAlternateEngineHealth(entry.LastSuccessUtcTicks, entry.LastFailureUtcTicks, nowUtc))
                .GroupBy(static entry => BuildAlternateEngineHealthKey(entry.ToolName, entry.EngineId), StringComparer.OrdinalIgnoreCase)
                .Select(static group => group
                    .OrderByDescending(static entry => Math.Max(entry.LastSuccessUtcTicks, entry.LastFailureUtcTicks))
                    .First())
                .OrderByDescending(static entry => Math.Max(entry.LastSuccessUtcTicks, entry.LastFailureUtcTicks))
                .ThenBy(static entry => entry.ToolName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static entry => entry.EngineId, StringComparer.OrdinalIgnoreCase)
                .Take(MaxRememberedAlternateEngineHealthEntriesPerThread)
                .ToArray();

            if (sanitizedEntries.Length == 0) {
                store.Threads.Remove(normalizedThreadId);
                WriteAlternateEngineHealthStoreNoThrow(path, store);
                return false;
            }

            entries = sanitizedEntries;
            return true;
        }
    }

    private static bool HasFreshAlternateEngineHealth(long lastSuccessUtcTicks, long lastFailureUtcTicks, DateTime nowUtc) {
        return IsFreshAlternateEngineHealthTick(lastSuccessUtcTicks, nowUtc)
               || IsFreshAlternateEngineHealthTick(lastFailureUtcTicks, nowUtc);
    }

    private static bool IsFreshAlternateEngineHealthTick(long ticks, DateTime nowUtc) {
        return TryGetUtcDateTimeFromTicks(ticks, out var seenUtc)
               && seenUtc <= nowUtc
               && nowUtc - seenUtc <= AlternateEngineHealthContextMaxAge;
    }

    private static string BuildAlternateEngineHealthKey(string toolName, string engineId) {
        return $"{toolName}\u001f{engineId}";
    }

    private static AlternateEngineHealthStoreDto ReadAlternateEngineHealthStoreNoThrow(string path) {
        try {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) {
                return new AlternateEngineHealthStoreDto();
            }

            var info = new FileInfo(path);
            if (info.Length <= 0 || info.Length > 512 * 1024) {
                return new AlternateEngineHealthStoreDto();
            }

            var json = File.ReadAllText(path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json)) {
                return new AlternateEngineHealthStoreDto();
            }

            var store = JsonSerializer.Deserialize<AlternateEngineHealthStoreDto>(json, AlternateEngineHealthStoreJsonOptions);
            if (store is null || store.Version != AlternateEngineHealthStoreVersion || store.Threads is null) {
                return new AlternateEngineHealthStoreDto();
            }

            if (store.Threads.Comparer != StringComparer.Ordinal) {
                store.Threads = new Dictionary<string, AlternateEngineHealthStoreEntryDto[]>(store.Threads, StringComparer.Ordinal);
            }

            return store;
        } catch (Exception ex) {
            Trace.TraceWarning($"Alternate engine health store read failed: {ex.GetType().Name}: {ex.Message}");
            return new AlternateEngineHealthStoreDto();
        }
    }

    private static void WriteAlternateEngineHealthStoreNoThrow(string path, AlternateEngineHealthStoreDto store) {
        string? tmp = null;
        try {
            if (string.IsNullOrWhiteSpace(path)) {
                return;
            }

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory)) {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(store, AlternateEngineHealthStoreJsonOptions);
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
            Trace.TraceWarning($"Alternate engine health store write failed: {ex.GetType().Name}: {ex.Message}");
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

    private static void PruneAlternateEngineHealthStore(AlternateEngineHealthStoreDto store) {
        var nowUtc = DateTime.UtcNow;
        var emptyThreadIds = new List<string>();
        foreach (var pair in store.Threads) {
            var entries = pair.Value ?? Array.Empty<AlternateEngineHealthStoreEntryDto>();
            var sanitizedEntries = entries
                .Where(static entry => entry is not null)
                .Where(entry => {
                    var normalizedToolName = (entry!.ToolName ?? string.Empty).Trim();
                    var normalizedEngineId = NormalizeAlternateEngineHealthToken(entry.EngineId);
                    return normalizedToolName.Length > 0
                           && normalizedEngineId.Length > 0
                           && HasFreshAlternateEngineHealth(entry.LastSuccessUtcTicks, entry.LastFailureUtcTicks, nowUtc);
                })
                .OrderByDescending(static entry => Math.Max(entry.LastSuccessUtcTicks, entry.LastFailureUtcTicks))
                .ThenBy(static entry => entry!.ToolName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static entry => entry!.EngineId, StringComparer.OrdinalIgnoreCase)
                .Take(MaxRememberedAlternateEngineHealthEntriesPerThread)
                .ToArray();

            if (sanitizedEntries.Length == 0) {
                emptyThreadIds.Add(pair.Key);
                continue;
            }

            store.Threads[pair.Key] = sanitizedEntries!;
        }

        for (var i = 0; i < emptyThreadIds.Count; i++) {
            store.Threads.Remove(emptyThreadIds[i]);
        }
    }
}
