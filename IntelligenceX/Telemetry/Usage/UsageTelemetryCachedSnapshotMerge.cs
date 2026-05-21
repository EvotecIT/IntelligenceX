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
        var contributionsById = new Dictionary<string, List<UsageEventRecord>>(StringComparer.OrdinalIgnoreCase);
        foreach (var usageEvent in fallbackEvents.Concat(primaryEvents)) {
            if (usageEvent is null || string.IsNullOrWhiteSpace(usageEvent.EventId)) {
                continue;
            }

            if (!contributionsById.TryGetValue(usageEvent.EventId, out var contributions)) {
                contributions = new List<UsageEventRecord>();
                contributionsById[usageEvent.EventId] = contributions;
            }

            if (TryMergeExistingContribution(contributions, usageEvent)) {
                mergedById[usageEvent.EventId] = RebuildMergedUsageEvent(contributions);
                continue;
            }

            contributions.Add(CloneUsageEvent(usageEvent));
            mergedById[usageEvent.EventId] = RebuildMergedUsageEvent(contributions);
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

    private static bool TryMergeExistingContribution(List<UsageEventRecord> contributions, UsageEventRecord incoming) {
        for (var i = 0; i < contributions.Count; i++) {
            var existing = contributions[i];
            if (HasSameRollupCoverage(existing, incoming) ||
                HasSameRollupIdentity(existing, incoming) && HasDominatingRollupCoverage(existing, incoming) ||
                HasSameRollupIdentityIgnoringTimestamp(existing, incoming) && CoverageDominates(incoming, existing)) {
                MergeDuplicateContributionInto(existing, incoming);
                return true;
            }
        }

        return false;
    }

    private static UsageEventRecord RebuildMergedUsageEvent(IReadOnlyList<UsageEventRecord> contributions) {
        var merged = CloneUsageEvent(contributions[0]);
        merged.InputTokens = null;
        merged.CachedInputTokens = null;
        merged.OutputTokens = null;
        merged.ReasoningTokens = null;
        merged.TotalTokens = null;
        merged.CompactCount = null;
        merged.DurationMs = null;
        merged.CostUsd = null;
        for (var i = 0; i < contributions.Count; i++) {
            MergeUsageEventInto(merged, contributions[i]);
        }

        return merged;
    }

    private static void MergeUsageEventInto(UsageEventRecord existing, UsageEventRecord incoming) {
        existing.InputTokens = SumNullable(existing.InputTokens, incoming.InputTokens);
        existing.CachedInputTokens = SumNullable(existing.CachedInputTokens, incoming.CachedInputTokens);
        existing.OutputTokens = SumNullable(existing.OutputTokens, incoming.OutputTokens);
        existing.ReasoningTokens = SumNullable(existing.ReasoningTokens, incoming.ReasoningTokens);
        existing.TotalTokens = SumNullable(existing.TotalTokens, incoming.TotalTokens);
        existing.CompactCount = SumNullable(existing.CompactCount, incoming.CompactCount);
        existing.DurationMs = MaxNullable(existing.DurationMs, incoming.DurationMs);
        existing.CostUsd = MaxNullable(existing.CostUsd, incoming.CostUsd);
        if (incoming.TruthLevel > existing.TruthLevel) {
            existing.TruthLevel = incoming.TruthLevel;
        }
    }

    private static void MergeDuplicateContributionInto(UsageEventRecord existing, UsageEventRecord incoming) {
        existing.InputTokens = MaxNullable(existing.InputTokens, incoming.InputTokens);
        existing.CachedInputTokens = MaxNullable(existing.CachedInputTokens, incoming.CachedInputTokens);
        existing.OutputTokens = MaxNullable(existing.OutputTokens, incoming.OutputTokens);
        existing.ReasoningTokens = MaxNullable(existing.ReasoningTokens, incoming.ReasoningTokens);
        existing.TotalTokens = MaxNullable(existing.TotalTokens, incoming.TotalTokens);
        existing.CompactCount = MaxNullable(existing.CompactCount, incoming.CompactCount);
        existing.DurationMs = MaxNullable(existing.DurationMs, incoming.DurationMs);
        existing.CostUsd = MaxNullable(existing.CostUsd, incoming.CostUsd);
        if (incoming.TruthLevel > existing.TruthLevel) {
            existing.TruthLevel = incoming.TruthLevel;
        }
    }

    private static bool HasSameRollupCoverage(UsageEventRecord existing, UsageEventRecord incoming) =>
        existing.InputTokens == incoming.InputTokens &&
        existing.CachedInputTokens == incoming.CachedInputTokens &&
        existing.OutputTokens == incoming.OutputTokens &&
        existing.ReasoningTokens == incoming.ReasoningTokens &&
        existing.TotalTokens == incoming.TotalTokens &&
        existing.CompactCount == incoming.CompactCount;

    private static bool HasSameRollupIdentity(UsageEventRecord existing, UsageEventRecord incoming) =>
        string.Equals(existing.EventId, incoming.EventId, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(existing.ProviderId, incoming.ProviderId, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(existing.AdapterId, incoming.AdapterId, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(existing.SourceRootId, incoming.SourceRootId, StringComparison.OrdinalIgnoreCase) &&
        existing.TimestampUtc == incoming.TimestampUtc &&
        HasSameRollupIdentityIgnoringTimestamp(existing, incoming);

    private static bool HasSameRollupIdentityIgnoringTimestamp(UsageEventRecord existing, UsageEventRecord incoming) =>
        string.Equals(existing.EventId, incoming.EventId, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(existing.ProviderId, incoming.ProviderId, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(existing.AdapterId, incoming.AdapterId, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(existing.SourceRootId, incoming.SourceRootId, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(existing.ProviderAccountId ?? string.Empty, incoming.ProviderAccountId ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(existing.AccountLabel ?? string.Empty, incoming.AccountLabel ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(existing.SessionId ?? string.Empty, incoming.SessionId ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(existing.ThreadId ?? string.Empty, incoming.ThreadId ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(existing.TurnId ?? string.Empty, incoming.TurnId ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(existing.ResponseId ?? string.Empty, incoming.ResponseId ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(existing.Model ?? string.Empty, incoming.Model ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(existing.Surface ?? string.Empty, incoming.Surface ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(existing.RawHash ?? string.Empty, incoming.RawHash ?? string.Empty, StringComparison.OrdinalIgnoreCase);

    private static bool HasDominatingRollupCoverage(UsageEventRecord existing, UsageEventRecord incoming) =>
        CoverageDominates(existing, incoming) || CoverageDominates(incoming, existing);

    private static bool CoverageDominates(UsageEventRecord candidate, UsageEventRecord other) =>
        NullableGreaterOrEqual(candidate.InputTokens, other.InputTokens) &&
        NullableGreaterOrEqual(candidate.CachedInputTokens, other.CachedInputTokens) &&
        NullableGreaterOrEqual(candidate.OutputTokens, other.OutputTokens) &&
        NullableGreaterOrEqual(candidate.ReasoningTokens, other.ReasoningTokens) &&
        NullableGreaterOrEqual(candidate.TotalTokens, other.TotalTokens) &&
        NullableGreaterOrEqual(candidate.CompactCount, other.CompactCount);

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

    private static int? MaxNullable(int? existing, int? incoming) {
        if (!existing.HasValue) {
            return incoming;
        }

        if (!incoming.HasValue) {
            return existing;
        }

        return Math.Max(existing.Value, incoming.Value);
    }

    private static decimal? MaxNullable(decimal? existing, decimal? incoming) {
        if (!existing.HasValue) {
            return incoming;
        }

        if (!incoming.HasValue) {
            return existing;
        }

        return Math.Max(existing.Value, incoming.Value);
    }

    private static bool NullableGreaterOrEqual(long? candidate, long? other) =>
        !other.HasValue || candidate.HasValue && candidate.Value >= other.Value;

    private static bool NullableGreaterOrEqual(int? candidate, int? other) =>
        !other.HasValue || candidate.HasValue && candidate.Value >= other.Value;
}
