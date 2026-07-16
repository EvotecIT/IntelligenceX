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
    private const int PendingActionStoreVersion = 1;
    private static readonly object PendingActionStoreLock = new();

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

    private sealed class PendingActionStoreDto {
        public int Version { get; set; } = PendingActionStoreVersion;
        public Dictionary<string, PendingActionStoreEntryDto> Threads { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed class PendingActionStoreEntryDto {
        public long SeenUtcTicks { get; set; }
        // Legacy v1 field: kept for backward compatibility with store files written by older builds.
        // Newer builds persist CallToActionTokens instead of raw assistant text snippets.
        public string AssistantContext { get; set; } = string.Empty;
        public string[] CallToActionTokens { get; set; } = Array.Empty<string>();
        public PendingActionDto[] Actions { get; set; } = Array.Empty<PendingActionDto>();
    }

    private sealed class PendingActionDto {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Request { get; set; } = string.Empty;
        public bool? Mutating { get; set; }
    }

    private static string ResolveDefaultPendingActionsStorePath() =>
        ChatServiceJsonFileStore.ResolveDefaultPath("pending-actions.json");

    private string ResolvePendingActionsStorePath() {
        var candidate = (_options.PendingActionsStorePath ?? string.Empty).Trim();
        if (candidate.Length == 0) {
            return ResolveDefaultPendingActionsStorePath();
        }

        // Treat overrides as trusted file names beneath the shared per-user state directory.
        // Fully-qualified paths are honored only when they remain inside that directory.
        var baseDir = ChatServiceJsonFileStore.ResolveDefaultDirectory();
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

    private void PersistPendingActionsSnapshot(string threadId, long seenUtcTicks, PendingAction[] actions, string[] callToActionTokens) {
        if (string.IsNullOrWhiteSpace(threadId) || actions is null || actions.Length == 0 || seenUtcTicks <= 0) {
            return;
        }

        var path = ResolvePendingActionsStorePath();
        lock (PendingActionStoreLock) {
            var store = ReadPendingActionsStoreNoThrow(path);

            // Normalize and enforce bounds.
            var normalizedId = threadId.Trim();
            var normalizedCtas = (callToActionTokens ?? Array.Empty<string>())
                .Where(static token => !string.IsNullOrWhiteSpace(token))
                .Select(static token => NormalizeCompactCallToActionToken(token))
                .Where(static token => token.Length > 0)
                .Where(static token => LooksLikeCompactCallToActionToken(token))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(6)
                .ToArray();
            var dto = new PendingActionStoreEntryDto {
                SeenUtcTicks = seenUtcTicks,
                // Avoid persisting raw assistant snippets; store only the extracted CTA tokens.
                AssistantContext = string.Empty,
                CallToActionTokens = normalizedCtas,
                Actions = actions
                    .Where(a => !string.IsNullOrWhiteSpace(a.Id))
                    .Take(6)
                    .Select(a => new PendingActionDto {
                        Id = (a.Id ?? string.Empty).Trim(),
                        Title = (a.Title ?? string.Empty).Trim(),
                        Request = (a.Request ?? string.Empty).Trim(),
                        Mutating = a.Mutability == ActionMutability.Unknown
                            ? null
                            : a.Mutability == ActionMutability.Mutating
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

    private void ClearPendingActionsSnapshots() {
        var path = ResolvePendingActionsStorePath();
        lock (PendingActionStoreLock) {
            ChatServiceJsonFileStore.Delete(path, "Pending action store");
        }
    }

    private bool TryLoadPendingActionsSnapshot(string threadId, out long seenUtcTicks, out PendingAction[] actions, out string[] callToActionTokens) {
        seenUtcTicks = 0;
        actions = Array.Empty<PendingAction>();
        callToActionTokens = Array.Empty<string>();

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
                    Request: (a.Request ?? string.Empty).Trim(),
                    Mutability: ResolveActionMutabilityFromNullableBoolean(a.Mutating)))
                .ToArray();

            if (actions.Length == 0) {
                store.Threads.Remove(normalized);
                WritePendingActionsStoreNoThrow(path, store);
                return false;
            }

            callToActionTokens = (entry.CallToActionTokens ?? Array.Empty<string>())
                .Where(static token => !string.IsNullOrWhiteSpace(token))
                .Select(static token => NormalizeCompactCallToActionToken(token))
                .Where(static token => token.Length > 0)
                .Where(static token => LooksLikeCompactCallToActionToken(token))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(6)
                .ToArray();
            if (callToActionTokens.Length == 0 && !string.IsNullOrWhiteSpace(entry.AssistantContext)) {
                // Backward compat: older builds persisted a raw assistant context snippet; extract CTA tokens on load.
                var context = entry.AssistantContext.Trim();
                if (context.Length > MaxPendingActionAssistantContextChars) {
                    context = context.Substring(0, MaxPendingActionAssistantContextChars);
                }

                callToActionTokens = ExtractPendingActionCallToActionTokens(context);
            }

            return true;
        }
    }

    private static PendingActionStoreDto ReadPendingActionsStoreNoThrow(string path) {
        return ChatServiceJsonFileStore.ReadOrCreate(
            path,
            maximumBytes: 1024 * 1024,
            static json => JsonSerializer.Deserialize<PendingActionStoreDto>(json),
            static store => store.Version == PendingActionStoreVersion && store.Threads is not null,
            static store => {
                if (store.Threads.Comparer != StringComparer.Ordinal) {
                    store.Threads = new Dictionary<string, PendingActionStoreEntryDto>(store.Threads, StringComparer.Ordinal);
                }
            },
            static () => new PendingActionStoreDto(),
            "Pending action store");
    }

    private static void WritePendingActionsStoreNoThrow(string path, PendingActionStoreDto store) {
        ChatServiceJsonFileStore.Write(
            path,
            store,
            static value => JsonSerializer.Serialize(value),
            "Pending action store");
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
