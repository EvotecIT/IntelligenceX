using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.Tools;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private const int HostBootstrapFailureStoreVersion = 1;
    private const int MaxRememberedHostBootstrapFailuresPerThread = 16;
    private static readonly TimeSpan HostBootstrapFailureContextMaxAge = TimeSpan.FromMinutes(30);
    private const string HostBootstrapFailureKindPackPreflight = "pack_preflight";
    private const string HostBootstrapFailureKindRecoveryHelper = "recovery_helper";
    private static readonly object HostBootstrapFailureStoreLock = new();
    private static readonly JsonSerializerOptions HostBootstrapFailureStoreJsonOptions = new() {
        WriteIndented = false,
        PropertyNameCaseInsensitive = false
    };

    private sealed class HostBootstrapFailureStoreDto {
        public int Version { get; set; } = HostBootstrapFailureStoreVersion;
        public Dictionary<string, HostBootstrapFailureStoreEntryDto[]> Threads { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed class HostBootstrapFailureStoreEntryDto {
        public string ToolName { get; set; } = string.Empty;
        public string FailureKind { get; set; } = string.Empty;
        public string ErrorCode { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
        public long SeenUtcTicks { get; set; }
    }

    private readonly record struct HostBootstrapFailureSnapshot(
        string ToolName,
        string FailureKind,
        string ErrorCode,
        string Error,
        long SeenUtcTicks);

    private static string ResolveDefaultHostBootstrapFailureStorePath() {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root)) {
            root = ".";
        }

        return Path.Combine(root, "IntelligenceX.Chat", "host-bootstrap-failures.json");
    }

    private string ResolveHostBootstrapFailureStorePath() {
        var pendingActionsPath = ResolvePendingActionsStorePath();
        var directory = Path.GetDirectoryName(pendingActionsPath);
        if (!string.IsNullOrWhiteSpace(directory)) {
            return Path.Combine(directory, "host-bootstrap-failures.json");
        }

        return ResolveDefaultHostBootstrapFailureStorePath();
    }

    private void RememberHostBootstrapFailure(string threadId, string toolName, string failureKind, ToolOutputDto output) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        var normalizedToolName = (toolName ?? string.Empty).Trim();
        var normalizedFailureKind = NormalizeHostBootstrapFailureKind(failureKind);
        if (normalizedThreadId.Length == 0
            || normalizedToolName.Length == 0
            || normalizedFailureKind.Length == 0
            || IsSuccessfulToolOutput(output)) {
            return;
        }

        var snapshot = new HostBootstrapFailureSnapshot(
            ToolName: normalizedToolName,
            FailureKind: normalizedFailureKind,
            ErrorCode: NormalizeHostBootstrapFailureText(output.ErrorCode, maxLength: 128),
            Error: NormalizeHostBootstrapFailureText(output.Error, maxLength: 280),
            SeenUtcTicks: DateTime.UtcNow.Ticks);
        PersistHostBootstrapFailureSnapshot(normalizedThreadId, snapshot);
    }

    private void ClearHostBootstrapFailure(string threadId, string toolName) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        var normalizedToolName = (toolName ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0 || normalizedToolName.Length == 0) {
            return;
        }

        var path = ResolveHostBootstrapFailureStorePath();
        lock (HostBootstrapFailureStoreLock) {
            var store = ReadHostBootstrapFailureStoreNoThrow(path);
            if (!store.Threads.TryGetValue(normalizedThreadId, out var entries) || entries is not { Length: > 0 }) {
                return;
            }

            var updatedEntries = entries
                .Where(entry => entry is not null && !string.Equals(entry.ToolName, normalizedToolName, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (updatedEntries.Length == entries.Length) {
                return;
            }

            if (updatedEntries.Length == 0) {
                store.Threads.Remove(normalizedThreadId);
            } else {
                store.Threads[normalizedThreadId] = updatedEntries;
            }

            WriteHostBootstrapFailureStoreNoThrow(path, store);
        }
    }

    private HashSet<string> SnapshotRecentHostBootstrapFailureToolNames(string threadId) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        if (!TryLoadHostBootstrapFailureSnapshots(normalizedThreadId, out var entries) || entries.Length == 0) {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return new HashSet<string>(
            entries.Select(static entry => entry.ToolName),
            StringComparer.OrdinalIgnoreCase);
    }

    private void RememberFailedPackPreflightCalls(
        string threadId,
        IReadOnlyList<ToolCall> executedCalls,
        IReadOnlyList<ToolOutputDto> outputs) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0 || executedCalls.Count == 0 || outputs.Count == 0) {
            return;
        }

        var outputByCallId = new Dictionary<string, ToolOutputDto>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < outputs.Count; i++) {
            var output = outputs[i];
            var callId = (output.CallId ?? string.Empty).Trim();
            if (callId.Length == 0 || outputByCallId.ContainsKey(callId)) {
                continue;
            }

            outputByCallId[callId] = output;
        }

        for (var i = 0; i < executedCalls.Count; i++) {
            var call = executedCalls[i];
            var callId = (call.CallId ?? string.Empty).Trim();
            var toolName = (call.Name ?? string.Empty).Trim();
            var failureKind = ResolveHostBootstrapFailureKind(callId, toolName);
            if (callId.Length == 0
                || toolName.Length == 0
                || failureKind.Length == 0
                || !outputByCallId.TryGetValue(callId, out var output)
                || IsSuccessfulToolOutput(output)) {
                continue;
            }

            RememberHostBootstrapFailure(normalizedThreadId, toolName, failureKind, output);
        }
    }

    private string ResolveHostBootstrapFailureKind(string callId, string toolName) {
        var normalizedCallId = (callId ?? string.Empty).Trim();
        var normalizedToolName = (toolName ?? string.Empty).Trim();
        if (!IsHostGeneratedPackPreflightCallId(normalizedCallId) || normalizedToolName.Length == 0) {
            return string.Empty;
        }

        return IsHostPackPreflightToolName(normalizedToolName)
            ? HostBootstrapFailureKindPackPreflight
            : HostBootstrapFailureKindRecoveryHelper;
    }

    private static bool IsHostGeneratedPackPreflightCallId(string callId) {
        var normalizedCallId = (callId ?? string.Empty).Trim();
        return normalizedCallId.StartsWith(HostPackPreflightCallIdPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeHostBootstrapFailureKind(string? failureKind) {
        var normalized = (failureKind ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch {
            HostBootstrapFailureKindPackPreflight => HostBootstrapFailureKindPackPreflight,
            HostBootstrapFailureKindRecoveryHelper => HostBootstrapFailureKindRecoveryHelper,
            _ => string.Empty
        };
    }

    private static string NormalizeHostBootstrapFailureText(string? value, int maxLength) {
        var normalized = ToolHealthDiagnostics.CompactOneLine(value);
        if (normalized.Length == 0) {
            return string.Empty;
        }

        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private void PersistHostBootstrapFailureSnapshot(string threadId, HostBootstrapFailureSnapshot snapshot) {
        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0
            || snapshot.ToolName.Length == 0
            || snapshot.SeenUtcTicks <= 0
            || snapshot.FailureKind.Length == 0) {
            return;
        }

        var path = ResolveHostBootstrapFailureStorePath();
        lock (HostBootstrapFailureStoreLock) {
            var store = ReadHostBootstrapFailureStoreNoThrow(path);
            var latestByToolName = new Dictionary<string, HostBootstrapFailureStoreEntryDto>(StringComparer.OrdinalIgnoreCase);
            if (store.Threads.TryGetValue(normalizedThreadId, out var entries) && entries is { Length: > 0 }) {
                for (var i = 0; i < entries.Length; i++) {
                    var entry = entries[i];
                    if (entry is null) {
                        continue;
                    }

                    var normalizedToolName = (entry.ToolName ?? string.Empty).Trim();
                    if (normalizedToolName.Length == 0 || !TryGetUtcDateTimeFromTicks(entry.SeenUtcTicks, out _)) {
                        continue;
                    }

                    latestByToolName[normalizedToolName] = entry;
                }
            }

            latestByToolName[snapshot.ToolName] = new HostBootstrapFailureStoreEntryDto {
                ToolName = snapshot.ToolName,
                FailureKind = snapshot.FailureKind,
                ErrorCode = snapshot.ErrorCode,
                Error = snapshot.Error,
                SeenUtcTicks = snapshot.SeenUtcTicks
            };

            store.Threads[normalizedThreadId] = latestByToolName.Values
                .OrderByDescending(static entry => entry.SeenUtcTicks)
                .ThenBy(static entry => entry.ToolName, StringComparer.OrdinalIgnoreCase)
                .Take(MaxRememberedHostBootstrapFailuresPerThread)
                .ToArray();
            PruneHostBootstrapFailureStore(store);
            WriteHostBootstrapFailureStoreNoThrow(path, store);
        }
    }

    private void ClearHostBootstrapFailureSnapshots() {
        var path = ResolveHostBootstrapFailureStorePath();
        lock (HostBootstrapFailureStoreLock) {
            try {
                if (File.Exists(path)) {
                    File.Delete(path);
                }
            } catch (Exception ex) {
                Trace.TraceWarning($"Host bootstrap failure store clear failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }

    private bool TryLoadHostBootstrapFailureSnapshots(string threadId, out HostBootstrapFailureSnapshot[] entries) {
        entries = Array.Empty<HostBootstrapFailureSnapshot>();

        var normalizedThreadId = (threadId ?? string.Empty).Trim();
        if (normalizedThreadId.Length == 0) {
            return false;
        }

        var path = ResolveHostBootstrapFailureStorePath();
        lock (HostBootstrapFailureStoreLock) {
            var store = ReadHostBootstrapFailureStoreNoThrow(path);
            if (!store.Threads.TryGetValue(normalizedThreadId, out var persistedEntries) || persistedEntries is not { Length: > 0 }) {
                return false;
            }

            var nowUtc = DateTime.UtcNow;
            var sanitizedEntries = persistedEntries
                .Where(static entry => entry is not null)
                .Select(static entry => new HostBootstrapFailureSnapshot(
                    ToolName: (entry!.ToolName ?? string.Empty).Trim(),
                    FailureKind: NormalizeHostBootstrapFailureKind(entry!.FailureKind),
                    ErrorCode: NormalizeHostBootstrapFailureText(entry!.ErrorCode, maxLength: 128),
                    Error: NormalizeHostBootstrapFailureText(entry!.Error, maxLength: 280),
                    SeenUtcTicks: entry!.SeenUtcTicks))
                .Where(entry =>
                    entry.ToolName.Length > 0
                    && entry.FailureKind.Length > 0
                    && TryGetUtcDateTimeFromTicks(entry.SeenUtcTicks, out var seenUtc)
                    && seenUtc <= nowUtc
                    && nowUtc - seenUtc <= HostBootstrapFailureContextMaxAge)
                .GroupBy(static entry => entry.ToolName, StringComparer.OrdinalIgnoreCase)
                .Select(static group => group
                    .OrderByDescending(static entry => entry.SeenUtcTicks)
                    .ThenBy(static entry => entry.ToolName, StringComparer.OrdinalIgnoreCase)
                    .First())
                .OrderByDescending(static entry => entry.SeenUtcTicks)
                .ThenBy(static entry => entry.ToolName, StringComparer.OrdinalIgnoreCase)
                .Take(MaxRememberedHostBootstrapFailuresPerThread)
                .ToArray();

            if (sanitizedEntries.Length == 0) {
                store.Threads.Remove(normalizedThreadId);
                WriteHostBootstrapFailureStoreNoThrow(path, store);
                return false;
            }

            var changed = sanitizedEntries.Length != persistedEntries.Length;
            if (!changed) {
                for (var i = 0; i < sanitizedEntries.Length; i++) {
                    var current = sanitizedEntries[i];
                    var persisted = persistedEntries[i];
                    if (persisted is null
                        || !string.Equals(current.ToolName, (persisted.ToolName ?? string.Empty).Trim(), StringComparison.Ordinal)
                        || !string.Equals(current.FailureKind, NormalizeHostBootstrapFailureKind(persisted.FailureKind), StringComparison.Ordinal)
                        || current.SeenUtcTicks != persisted.SeenUtcTicks
                        || !string.Equals(current.ErrorCode, NormalizeHostBootstrapFailureText(persisted.ErrorCode, 128), StringComparison.Ordinal)
                        || !string.Equals(current.Error, NormalizeHostBootstrapFailureText(persisted.Error, 280), StringComparison.Ordinal)) {
                        changed = true;
                        break;
                    }
                }
            }

            if (changed) {
                store.Threads[normalizedThreadId] = sanitizedEntries
                    .Select(static entry => new HostBootstrapFailureStoreEntryDto {
                        ToolName = entry.ToolName,
                        FailureKind = entry.FailureKind,
                        ErrorCode = entry.ErrorCode,
                        Error = entry.Error,
                        SeenUtcTicks = entry.SeenUtcTicks
                    })
                    .ToArray();
                WriteHostBootstrapFailureStoreNoThrow(path, store);
            }

            entries = sanitizedEntries;
            return true;
        }
    }

    private static HostBootstrapFailureStoreDto ReadHostBootstrapFailureStoreNoThrow(string path) {
        try {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) {
                return new HostBootstrapFailureStoreDto();
            }

            var info = new FileInfo(path);
            if (info.Length <= 0 || info.Length > 512 * 1024) {
                return new HostBootstrapFailureStoreDto();
            }

            var json = File.ReadAllText(path, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json)) {
                return new HostBootstrapFailureStoreDto();
            }

            var store = JsonSerializer.Deserialize<HostBootstrapFailureStoreDto>(json, HostBootstrapFailureStoreJsonOptions);
            if (store is null || store.Version != HostBootstrapFailureStoreVersion || store.Threads is null) {
                return new HostBootstrapFailureStoreDto();
            }

            if (store.Threads.Comparer != StringComparer.Ordinal) {
                store.Threads = new Dictionary<string, HostBootstrapFailureStoreEntryDto[]>(store.Threads, StringComparer.Ordinal);
            }

            return store;
        } catch (Exception ex) {
            Trace.TraceWarning($"Host bootstrap failure store read failed: {ex.GetType().Name}: {ex.Message}");
            return new HostBootstrapFailureStoreDto();
        }
    }

    private static void WriteHostBootstrapFailureStoreNoThrow(string path, HostBootstrapFailureStoreDto store) {
        string? tmp = null;
        try {
            if (string.IsNullOrWhiteSpace(path)) {
                return;
            }

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory)) {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(store, HostBootstrapFailureStoreJsonOptions);
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
            Trace.TraceWarning($"Host bootstrap failure store write failed: {ex.GetType().Name}: {ex.Message}");
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

    private static void PruneHostBootstrapFailureStore(HostBootstrapFailureStoreDto store) {
        if (store.Threads.Count <= MaxTrackedPackPreflightContexts) {
            return;
        }

        var removeCount = store.Threads.Count - MaxTrackedPackPreflightContexts;
        if (removeCount <= 0) {
            return;
        }

        var toRemove = store.Threads
            .Select(pair => (
                ThreadId: pair.Key,
                Ticks: pair.Value is { Length: > 0 }
                    ? pair.Value.Max(static entry => entry?.SeenUtcTicks ?? 0)
                    : 0L))
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
