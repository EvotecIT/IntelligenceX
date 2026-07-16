using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    // The event window is the failure-streak authority and intentionally saturates.
    // It is far above the maximum auto-pause threshold while keeping the runtime
    // store below its 512 KiB read limit. Lifetime outcome counters remain separate.
    internal const int MaxBackgroundSchedulerFailureStreakEvents = 4096;
    private const int MaxBackgroundSchedulerFailureEventIdLength = 40;

    private void RememberBackgroundSchedulerFailureEventNoLock(long recordedUtcTicks) {
        _backgroundSchedulerFailureStreakEvents.Add(new BackgroundSchedulerFailureEventDto {
            EventId = Guid.NewGuid().ToString("N"),
            RecordedUtcTicks = Math.Max(1, recordedUtcTicks)
        });
        if (_backgroundSchedulerFailureStreakEvents.Count > MaxBackgroundSchedulerFailureStreakEvents) {
            _backgroundSchedulerFailureStreakEvents.RemoveRange(
                0,
                _backgroundSchedulerFailureStreakEvents.Count - MaxBackgroundSchedulerFailureStreakEvents);
        }
        _backgroundSchedulerConsecutiveFailureCount = _backgroundSchedulerFailureStreakEvents.Count;
    }

    private static void MergeBackgroundSchedulerConsecutiveFailureState(
        BackgroundSchedulerRuntimeStoreDto merged,
        BackgroundSchedulerRuntimeStoreDto persisted,
        BackgroundSchedulerRuntimeStoreDto baseline,
        BackgroundSchedulerRuntimeStoreDto desired) {
        var baselineIds = baseline.FailureStreakEvents
            .Select(static failureEvent => failureEvent.EventId)
            .ToHashSet(StringComparer.Ordinal);
        var desiredDelta = desired.FailureStreakEvents
            .Where(failureEvent => !baselineIds.Contains(failureEvent.EventId));
        var mergedEvents = NormalizeBackgroundSchedulerFailureEvents(
            persisted.FailureStreakEvents.Concat(desiredDelta),
            consecutiveFailureCount: 0,
            Math.Max(persisted.LastFailureUtcTicks, desired.LastFailureUtcTicks),
            merged.LastSuccessUtcTicks);

        merged.FailureStreakEvents = mergedEvents;
        merged.ConsecutiveFailureCount = mergedEvents.Length;
    }

    private static BackgroundSchedulerFailureEventDto[] NormalizeBackgroundSchedulerFailureEvents(
        IEnumerable<BackgroundSchedulerFailureEventDto>? failureEvents,
        int consecutiveFailureCount,
        long lastFailureUtcTicks,
        long lastSuccessUtcTicks) {
        var normalized = (failureEvents ?? Array.Empty<BackgroundSchedulerFailureEventDto>())
            .Where(static failureEvent => failureEvent is not null)
            .Select(static failureEvent => new BackgroundSchedulerFailureEventDto {
                EventId = NormalizeBackgroundSchedulerFailureEventId(failureEvent.EventId),
                RecordedUtcTicks = Math.Max(0, failureEvent.RecordedUtcTicks)
            })
            .Where(failureEvent => failureEvent.EventId.Length > 0
                && failureEvent.RecordedUtcTicks > Math.Max(0, lastSuccessUtcTicks))
            .GroupBy(static failureEvent => failureEvent.EventId, StringComparer.Ordinal)
            .Select(static group => group.OrderByDescending(static failureEvent => failureEvent.RecordedUtcTicks).First())
            .OrderBy(static failureEvent => failureEvent.RecordedUtcTicks)
            .ThenBy(static failureEvent => failureEvent.EventId, StringComparer.Ordinal)
            .TakeLast(MaxBackgroundSchedulerFailureStreakEvents)
            .ToList();

        var targetCount = lastFailureUtcTicks > lastSuccessUtcTicks
            ? Math.Min(MaxBackgroundSchedulerFailureStreakEvents, Math.Max(0, consecutiveFailureCount))
            : 0;
        if (normalized.Count >= targetCount) {
            return normalized.ToArray();
        }

        var existingIds = normalized
            .Select(static failureEvent => failureEvent.EventId)
            .ToHashSet(StringComparer.Ordinal);
        var legacyRecordedUtcTicks = Math.Max(1, lastFailureUtcTicks);
        for (var index = 0; normalized.Count < targetCount; index++) {
            var eventId = "legacy-" + index.ToString("D4", CultureInfo.InvariantCulture);
            if (!existingIds.Add(eventId)) {
                continue;
            }

            normalized.Add(new BackgroundSchedulerFailureEventDto {
                EventId = eventId,
                RecordedUtcTicks = legacyRecordedUtcTicks
            });
        }

        return normalized
            .OrderBy(static failureEvent => failureEvent.RecordedUtcTicks)
            .ThenBy(static failureEvent => failureEvent.EventId, StringComparer.Ordinal)
            .ToArray();
    }

    private static BackgroundSchedulerFailureEventDto[] CloneBackgroundSchedulerFailureEvents(
        IEnumerable<BackgroundSchedulerFailureEventDto> failureEvents) =>
        failureEvents
            .Select(static failureEvent => new BackgroundSchedulerFailureEventDto {
                EventId = failureEvent.EventId,
                RecordedUtcTicks = failureEvent.RecordedUtcTicks
            })
            .ToArray();

    private static int ResolveBackgroundSchedulerConsecutiveFailureCount(int failureEventCount) =>
        Math.Clamp(failureEventCount, 0, MaxBackgroundSchedulerFailureStreakEvents);

    private static string NormalizeBackgroundSchedulerFailureEventId(string? eventId) {
        var normalized = eventId?.Trim() ?? string.Empty;
        if (normalized.Length == 0 || normalized.Length > MaxBackgroundSchedulerFailureEventIdLength) {
            return string.Empty;
        }

        return normalized.All(static character =>
            character is >= 'a' and <= 'z'
            or >= 'A' and <= 'Z'
            or >= '0' and <= '9'
            or '-' or '_' or '.' or ':')
            ? normalized
            : string.Empty;
    }
}
