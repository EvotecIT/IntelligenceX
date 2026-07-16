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
    private const int StructuredNextActionStoreVersion = 2;
    private static readonly object StructuredNextActionStoreLock = new();

    private sealed class StructuredNextActionStoreDto {
        public int Version { get; set; } = StructuredNextActionStoreVersion;
        public Dictionary<string, StructuredNextActionStoreEntryDto> Threads { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed class StructuredNextActionStoreEntryDto {
        public string SourceToolName { get; set; } = string.Empty;
        public string ToolName { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public double? Confidence { get; set; }
        public string ArgumentsJson { get; set; } = "{}";
        public bool? Mutating { get; set; }
        public long SeenUtcTicks { get; set; }
    }

    private static string ResolveDefaultStructuredNextActionStorePath() =>
        ChatServiceJsonFileStore.ResolveDefaultPath("structured-next-actions.json");

    private string ResolveStructuredNextActionStorePath() =>
        ChatServiceJsonFileStore.ResolveSiblingPath(ResolvePendingActionsStorePath(), "structured-next-actions.json");

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
                Reason = snapshot.Reason.Trim(),
                Confidence = snapshot.Confidence,
                ArgumentsJson = snapshot.ArgumentsJson.Trim(),
                Mutating = snapshot.Mutability == ActionMutability.Unknown
                    ? null
                    : snapshot.Mutability == ActionMutability.Mutating,
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
            ChatServiceJsonFileStore.Delete(path, "Structured next-action store");
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
                Reason: NormalizeStructuredNextActionReason(entry.Reason),
                Confidence: NormalizeStructuredNextActionConfidence(entry.Confidence),
                ArgumentsJson: argumentsJson,
                Mutability: ResolveActionMutabilityFromNullableBoolean(entry.Mutating),
                SeenUtcTicks: entry.SeenUtcTicks);
            return true;
        }
    }

    private static StructuredNextActionStoreDto ReadStructuredNextActionStoreNoThrow(string path) {
        return ChatServiceJsonFileStore.ReadOrCreate(
            path,
            maximumBytes: 1024 * 1024,
            static json => JsonSerializer.Deserialize<StructuredNextActionStoreDto>(json),
            static store => store.Version == StructuredNextActionStoreVersion && store.Threads is not null,
            static store => {
                if (store.Threads.Comparer != StringComparer.Ordinal) {
                    store.Threads = new Dictionary<string, StructuredNextActionStoreEntryDto>(store.Threads, StringComparer.Ordinal);
                }
            },
            static () => new StructuredNextActionStoreDto(),
            "Structured next-action store");
    }

    private static void WriteStructuredNextActionStoreNoThrow(string path, StructuredNextActionStoreDto store) {
        ChatServiceJsonFileStore.Write(
            path,
            store,
            static value => JsonSerializer.Serialize(value),
            "Structured next-action store");
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
