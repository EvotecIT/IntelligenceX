using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    // The runtime store is capped at 512 KiB. This retains far more events than the
    // maximum configured auto-pause threshold while keeping malformed or abandoned
    // stores bounded.
    private const int MaxBackgroundSchedulerFailureStreakEvents = 4096;
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
        if (BackgroundSchedulerFailureEventsAreComplete(persisted)
            && BackgroundSchedulerFailureEventsAreComplete(baseline)
            && BackgroundSchedulerFailureEventsAreComplete(desired)) {
            merged.ConsecutiveFailureCount = mergedEvents.Length;
            return;
        }

        MergeOverflowedBackgroundSchedulerConsecutiveFailureState(
            merged,
            persisted,
            baseline,
            desired);
    }

    private static bool BackgroundSchedulerFailureEventsAreComplete(BackgroundSchedulerRuntimeStoreDto store) =>
        Math.Max(0, store.ConsecutiveFailureCount) <= MaxBackgroundSchedulerFailureStreakEvents
        && Math.Max(0, store.ConsecutiveFailureCount) == store.FailureStreakEvents.Length;

    private static void MergeOverflowedBackgroundSchedulerConsecutiveFailureState(
        BackgroundSchedulerRuntimeStoreDto merged,
        BackgroundSchedulerRuntimeStoreDto persisted,
        BackgroundSchedulerRuntimeStoreDto baseline,
        BackgroundSchedulerRuntimeStoreDto desired) {
        var localSuccessChanged = desired.LastSuccessUtcTicks > baseline.LastSuccessUtcTicks;
        var persistedSuccessChanged = persisted.LastSuccessUtcTicks > baseline.LastSuccessUtcTicks;
        if (!localSuccessChanged && !persistedSuccessChanged) {
            merged.ConsecutiveFailureCount = AddBackgroundSchedulerCounterDelta(
                persisted.ConsecutiveFailureCount,
                baseline.ConsecutiveFailureCount,
                desired.ConsecutiveFailureCount);
            return;
        }

        var latestSuccessIsLocal = localSuccessChanged
            && (!persistedSuccessChanged || desired.LastSuccessUtcTicks >= persisted.LastSuccessUtcTicks);
        var resetWriter = latestSuccessIsLocal ? desired : persisted;
        var otherWriter = latestSuccessIsLocal ? persisted : desired;
        var postResetCount = Math.Max(0, resetWriter.ConsecutiveFailureCount);
        if (otherWriter.LastFailureUtcTicks > resetWriter.LastSuccessUtcTicks) {
            postResetCount = AddBackgroundSchedulerCounterDelta(
                postResetCount,
                0,
                Math.Max(0, otherWriter.ConsecutiveFailureCount - baseline.ConsecutiveFailureCount));
        }

        merged.ConsecutiveFailureCount = Math.Max(postResetCount, merged.FailureStreakEvents.Length);
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

    private static int ResolveBackgroundSchedulerConsecutiveFailureCount(
        int consecutiveFailureCount,
        int failureEventCount) =>
        Math.Max(0, consecutiveFailureCount) <= MaxBackgroundSchedulerFailureStreakEvents
            ? Math.Max(0, failureEventCount)
            : Math.Max(Math.Max(0, consecutiveFailureCount), Math.Max(0, failureEventCount));

    private static string NormalizeBackgroundSchedulerFailureEventId(string? eventId) {
        var normalized = eventId?.Trim() ?? string.Empty;
        return normalized.Length <= MaxBackgroundSchedulerFailureEventIdLength
            ? normalized
            : normalized[..MaxBackgroundSchedulerFailureEventIdLength];
    }
}
