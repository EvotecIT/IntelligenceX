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
        if (mergedRawEvents.Count > 0) {
            var rawRollups = UsageTelemetryQuickReportScanner.BuildMergedEventsFromRawRecords(mergedRawEvents);
            var fallbackRollups = cachedEvents.Concat(serviceEvents);
            return MergeRollups(rawRollups, fallbackRollups);
        }

        return serviceScannedAtUtc >= cachedScannedAtUtc
            ? serviceEvents.ToList()
            : cachedEvents.ToList();
    }

    public static IReadOnlyList<SourceRootRecord> SelectStartupSourceRoots(
        IReadOnlyList<SourceRootRecord> cachedSourceRoots,
        IReadOnlyList<SourceRootRecord> serviceSourceRoots,
        DateTimeOffset cachedScannedAtUtc,
        DateTimeOffset serviceScannedAtUtc) {
        var primaryRoots = serviceScannedAtUtc >= cachedScannedAtUtc
            ? serviceSourceRoots
            : cachedSourceRoots;
        var fallbackRoots = serviceScannedAtUtc >= cachedScannedAtUtc
            ? cachedSourceRoots
            : serviceSourceRoots;

        return primaryRoots
            .Concat(fallbackRoots)
            .Where(static root => root is not null && !string.IsNullOrWhiteSpace(root.Id))
            .GroupBy(static root => root.Id, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToList();
    }

    public static UsageTelemetrySnapshotHealth? SelectStartupHealth(
        UsageTelemetrySnapshotHealth? cachedHealth,
        UsageTelemetrySnapshotHealth? serviceHealth,
        DateTimeOffset cachedScannedAtUtc,
        DateTimeOffset serviceScannedAtUtc) {
        if (cachedHealth is null) {
            return serviceHealth;
        }

        if (serviceHealth is null) {
            return cachedHealth;
        }

        if (cachedHealth.IsPartialScan != serviceHealth.IsPartialScan) {
            return cachedHealth.IsPartialScan ? serviceHealth : cachedHealth;
        }

        return serviceScannedAtUtc >= cachedScannedAtUtc
            ? serviceHealth
            : cachedHealth;
    }

    private static IReadOnlyList<UsageEventRecord> MergeRollups(
        IEnumerable<UsageEventRecord> primaryEvents,
        IEnumerable<UsageEventRecord> fallbackEvents) {
        var mergedById = new Dictionary<string, UsageEventRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (var usageEvent in fallbackEvents.Concat(primaryEvents)) {
            if (usageEvent is null || string.IsNullOrWhiteSpace(usageEvent.EventId)) {
                continue;
            }

            if (!mergedById.TryGetValue(usageEvent.EventId, out var existing)) {
                mergedById[usageEvent.EventId] = CloneUsageEvent(usageEvent);
                continue;
            }

            if (HasSameRollupContribution(existing, usageEvent)) {
                continue;
            }

            MergeUsageEventInto(existing, usageEvent);
        }

        return mergedById.Values
            .OrderBy(static usageEvent => usageEvent.TimestampUtc)
            .ThenBy(static usageEvent => usageEvent.ProviderId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static usageEvent => usageEvent.Model, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static usageEvent => usageEvent.EventId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static UsageEventRecord CloneUsageEvent(UsageEventRecord usageEvent) {
        return new UsageEventRecord(
            usageEvent.EventId,
            usageEvent.ProviderId,
            usageEvent.AdapterId,
            usageEvent.SourceRootId,
            usageEvent.TimestampUtc) {
            ProviderAccountId = usageEvent.ProviderAccountId,
            AccountLabel = usageEvent.AccountLabel,
            PersonLabel = usageEvent.PersonLabel,
            MachineId = usageEvent.MachineId,
            SessionId = usageEvent.SessionId,
            ThreadId = usageEvent.ThreadId,
            ConversationTitle = usageEvent.ConversationTitle,
            WorkspacePath = usageEvent.WorkspacePath,
            RepositoryName = usageEvent.RepositoryName,
            TurnId = usageEvent.TurnId,
            ResponseId = usageEvent.ResponseId,
            Model = usageEvent.Model,
            Surface = usageEvent.Surface,
            InputTokens = usageEvent.InputTokens,
            CachedInputTokens = usageEvent.CachedInputTokens,
            OutputTokens = usageEvent.OutputTokens,
            ReasoningTokens = usageEvent.ReasoningTokens,
            TotalTokens = usageEvent.TotalTokens,
            CompactCount = usageEvent.CompactCount,
            DurationMs = usageEvent.DurationMs,
            CostUsd = usageEvent.CostUsd,
            TruthLevel = usageEvent.TruthLevel,
            RawHash = usageEvent.RawHash
        };
    }

    private static void MergeUsageEventInto(UsageEventRecord existing, UsageEventRecord incoming) {
        existing.InputTokens = SumNullable(existing.InputTokens, incoming.InputTokens);
        existing.CachedInputTokens = SumNullable(existing.CachedInputTokens, incoming.CachedInputTokens);
        existing.OutputTokens = SumNullable(existing.OutputTokens, incoming.OutputTokens);
        existing.ReasoningTokens = SumNullable(existing.ReasoningTokens, incoming.ReasoningTokens);
        existing.TotalTokens = SumNullable(existing.TotalTokens, incoming.TotalTokens);
        existing.CompactCount = SumNullable(existing.CompactCount, incoming.CompactCount);
        existing.DurationMs = MaxNullable(existing.DurationMs, incoming.DurationMs);
        existing.CostUsd = SumNullable(existing.CostUsd, incoming.CostUsd);
        if (incoming.TruthLevel > existing.TruthLevel) {
            existing.TruthLevel = incoming.TruthLevel;
        }
    }

    private static bool HasSameRollupContribution(UsageEventRecord existing, UsageEventRecord incoming) =>
        existing.InputTokens == incoming.InputTokens &&
        existing.CachedInputTokens == incoming.CachedInputTokens &&
        existing.OutputTokens == incoming.OutputTokens &&
        existing.ReasoningTokens == incoming.ReasoningTokens &&
        existing.TotalTokens == incoming.TotalTokens &&
        existing.CompactCount == incoming.CompactCount &&
        existing.DurationMs == incoming.DurationMs &&
        existing.CostUsd == incoming.CostUsd;

    private static long? SumNullable(long? existing, long? incoming) {
        if (!existing.HasValue) {
            return incoming;
        }

        if (!incoming.HasValue) {
            return existing;
        }

        return existing.Value + incoming.Value;
    }

    private static int? SumNullable(int? existing, int? incoming) {
        if (!existing.HasValue) {
            return incoming;
        }

        if (!incoming.HasValue) {
            return existing;
        }

        return existing.Value + incoming.Value;
    }

    private static decimal? SumNullable(decimal? existing, decimal? incoming) {
        if (!existing.HasValue) {
            return incoming;
        }

        if (!incoming.HasValue) {
            return existing;
        }

        return existing.Value + incoming.Value;
    }

    private static long? MaxNullable(long? existing, long? incoming) {
        if (!existing.HasValue) {
            return incoming;
        }

        if (!incoming.HasValue) {
            return existing;
        }

        return Math.Max(existing.Value, incoming.Value);
    }
}
