using System;

namespace IntelligenceX.Telemetry.Usage;

internal static class UsageTelemetryEventMerge {
    public static UsageEventRecord Clone(UsageEventRecord record) {
        if (record is null) {
            throw new ArgumentNullException(nameof(record));
        }

        return new UsageEventRecord(
            record.EventId,
            record.ProviderId,
            record.AdapterId,
            record.SourceRootId,
            record.TimestampUtc) {
            ProviderAccountId = record.ProviderAccountId,
            AccountLabel = record.AccountLabel,
            PersonLabel = record.PersonLabel,
            MachineId = record.MachineId,
            SessionId = record.SessionId,
            ThreadId = record.ThreadId,
            ConversationTitle = record.ConversationTitle,
            WorkspacePath = record.WorkspacePath,
            RepositoryName = record.RepositoryName,
            TurnId = record.TurnId,
            ResponseId = record.ResponseId,
            Model = record.Model,
            Surface = record.Surface,
            InputTokens = record.InputTokens,
            CachedInputTokens = record.CachedInputTokens,
            OutputTokens = record.OutputTokens,
            ReasoningTokens = record.ReasoningTokens,
            TotalTokens = record.TotalTokens,
            CompactCount = record.CompactCount,
            DurationMs = record.DurationMs,
            CostUsd = record.CostUsd,
            TruthLevel = record.TruthLevel,
            RawHash = record.RawHash,
        };
    }

    public static bool MergeInto(UsageEventRecord existing, UsageEventRecord incoming) {
        if (existing is null) {
            throw new ArgumentNullException(nameof(existing));
        }
        if (incoming is null) {
            throw new ArgumentNullException(nameof(incoming));
        }

        var updated = false;
        updated |= MergePreferredString(existing.ProviderAccountId, incoming.ProviderAccountId, value => existing.ProviderAccountId = value);
        updated |= MergePreferredString(existing.AccountLabel, incoming.AccountLabel, value => existing.AccountLabel = value);
        updated |= MergePreferredString(existing.PersonLabel, incoming.PersonLabel, value => existing.PersonLabel = value);
        updated |= MergeString(existing.MachineId, incoming.MachineId, value => existing.MachineId = value);
        updated |= MergeString(existing.SessionId, incoming.SessionId, value => existing.SessionId = value);
        updated |= MergeString(existing.ThreadId, incoming.ThreadId, value => existing.ThreadId = value);
        updated |= MergeString(existing.ConversationTitle, incoming.ConversationTitle, value => existing.ConversationTitle = value);
        updated |= MergeString(existing.WorkspacePath, incoming.WorkspacePath, value => existing.WorkspacePath = value);
        updated |= MergeString(existing.RepositoryName, incoming.RepositoryName, value => existing.RepositoryName = value);
        updated |= MergeString(existing.TurnId, incoming.TurnId, value => existing.TurnId = value);
        updated |= MergeString(existing.ResponseId, incoming.ResponseId, value => existing.ResponseId = value);
        updated |= MergeString(existing.Model, incoming.Model, value => existing.Model = value);
        updated |= MergeString(existing.Surface, incoming.Surface, value => existing.Surface = value);
        updated |= MergeNullableInt64(existing.InputTokens, incoming.InputTokens, value => existing.InputTokens = value);
        updated |= MergeNullableInt64(existing.CachedInputTokens, incoming.CachedInputTokens, value => existing.CachedInputTokens = value);
        updated |= MergeNullableInt64(existing.OutputTokens, incoming.OutputTokens, value => existing.OutputTokens = value);
        updated |= MergeNullableInt64(existing.ReasoningTokens, incoming.ReasoningTokens, value => existing.ReasoningTokens = value);
        updated |= MergeNullableInt64(existing.TotalTokens, incoming.TotalTokens, value => existing.TotalTokens = value);
        updated |= MergeNullableInt32(existing.CompactCount, incoming.CompactCount, value => existing.CompactCount = value);
        updated |= MergeNullableInt64(existing.DurationMs, incoming.DurationMs, value => existing.DurationMs = value);
        updated |= MergeNullableDecimal(existing.CostUsd, incoming.CostUsd, value => existing.CostUsd = value);
        updated |= MergeTruthLevel(existing.TruthLevel, incoming.TruthLevel, value => existing.TruthLevel = value);
        updated |= MergeString(existing.RawHash, incoming.RawHash, value => existing.RawHash = value);
        return updated;
    }

    private static bool MergeString(string? target, string? incoming, Action<string?> apply) {
        if (!string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(incoming)) {
            return false;
        }

        apply(incoming);
        return true;
    }

    private static bool MergePreferredString(string? target, string? incoming, Action<string?> apply) {
        if (string.IsNullOrWhiteSpace(incoming)) {
            return false;
        }
        if (string.Equals(target, incoming, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        apply(incoming);
        return true;
    }

    private static bool MergeNullableInt64(long? target, long? incoming, Action<long?> apply) {
        if (target.HasValue || !incoming.HasValue) {
            return false;
        }

        apply(incoming);
        return true;
    }

    private static bool MergeNullableInt32(int? target, int? incoming, Action<int?> apply) {
        if (target.HasValue || !incoming.HasValue) {
            return false;
        }

        apply(incoming);
        return true;
    }

    private static bool MergeNullableDecimal(decimal? target, decimal? incoming, Action<decimal?> apply) {
        if (target.HasValue || !incoming.HasValue) {
            return false;
        }

        apply(incoming);
        return true;
    }

    private static bool MergeTruthLevel(UsageTruthLevel target, UsageTruthLevel incoming, Action<UsageTruthLevel> apply) {
        if ((int)incoming <= (int)target) {
            return false;
        }

        apply(incoming);
        return true;
    }
}
