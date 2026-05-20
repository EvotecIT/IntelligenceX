using System;
using System.Collections.Generic;
using System.Linq;

namespace IntelligenceX.Telemetry.Usage;

internal static class UsageTelemetryCachedSnapshotMerge {
    public static IReadOnlyList<UsageEventRecord> SelectStartupEvents(
        IReadOnlyList<UsageEventRecord> cachedEvents,
        IReadOnlyList<UsageEventRecord> serviceEvents,
        IReadOnlyList<UsageEventRecord> mergedRawEvents,
        bool hasCachedRawEvents,
        bool hasServiceRawEvents,
        DateTimeOffset cachedScannedAtUtc,
        DateTimeOffset serviceScannedAtUtc) {
        if (hasCachedRawEvents && hasServiceRawEvents) {
            return UsageTelemetryQuickReportScanner.BuildMergedEventsFromRawRecords(mergedRawEvents).ToList();
        }

        return serviceScannedAtUtc >= cachedScannedAtUtc
            ? serviceEvents.ToList()
            : cachedEvents.ToList();
    }
}
