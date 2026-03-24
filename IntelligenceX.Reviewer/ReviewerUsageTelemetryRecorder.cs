using System;
using IntelligenceX.Telemetry.Usage;

namespace IntelligenceX.Reviewer;

internal static class ReviewerUsageTelemetryRecorder {
    internal const string ClaudeApiAdapterId = "claude.reviewer-api";

    internal static void TryRecordClaudeTurn(
        string? providerAccountId,
        string? accountLabel,
        string? model,
        string? responseId,
        long? inputTokens,
        long? outputTokens,
        DateTimeOffset completedAtUtc,
        TimeSpan duration) {
        var dbPath = UsageTelemetryPathResolver.ResolveDatabasePath(enabledByDefault: true);
        if (string.IsNullOrWhiteSpace(dbPath)) {
            return;
        }

        var machineId = NormalizeOptional(Environment.MachineName) ?? "local";
        var sourcePath = "claude://internal/reviewer/" + machineId;
        var sourceRoot = new SourceRootRecord(
            SourceRootRecord.CreateStableId("claude", UsageSourceKind.InternalIx, sourcePath),
            "claude",
            UsageSourceKind.InternalIx,
            sourcePath) {
            MachineLabel = machineId,
            AccountHint = accountLabel
        };

        var turnId = NormalizeOptional(responseId) ?? "review-turn";
        var eventIdentity = "claude|reviewer|" + turnId;
        var rawIdentity = eventIdentity + "|" + completedAtUtc.ToUniversalTime().ToString("O");
        var totalTokens = AddNonNegative(inputTokens, outputTokens);
        var durationMs = SafeDurationMs(duration);

        var record = new UsageEventRecord(
            "ev_" + UsageTelemetryIdentity.ComputeStableHash(eventIdentity, 16),
            "claude",
            ClaudeApiAdapterId,
            sourceRoot.Id,
            completedAtUtc) {
            ProviderAccountId = NormalizeOptional(providerAccountId),
            AccountLabel = NormalizeOptional(accountLabel),
            MachineId = machineId,
            SessionId = "reviewer",
            ThreadId = "reviewer",
            TurnId = turnId,
            ResponseId = NormalizeOptional(responseId),
            Model = NormalizeOptional(model),
            Surface = "reviewer",
            InputTokens = NormalizeNonNegative(inputTokens),
            OutputTokens = NormalizeNonNegative(outputTokens),
            TotalTokens = totalTokens,
            DurationMs = durationMs,
            TruthLevel = totalTokens.HasValue ? UsageTruthLevel.Exact : UsageTruthLevel.Unknown,
            RawHash = UsageTelemetryIdentity.ComputeStableHash(rawIdentity, 16)
        };

        using var sourceRootStore = new SqliteSourceRootStore(dbPath!);
        using var usageEventStore = new SqliteUsageEventStore(dbPath!);
        sourceRootStore.Upsert(sourceRoot);
        usageEventStore.Upsert(record);
    }

    private static long? NormalizeNonNegative(long? value) {
        if (!value.HasValue) {
            return null;
        }

        return Math.Max(0L, value.Value);
    }

    private static long? AddNonNegative(long? first, long? second) {
        if (!first.HasValue && !second.HasValue) {
            return null;
        }

        return Math.Max(0L, first ?? 0L) + Math.Max(0L, second ?? 0L);
    }

    private static long SafeDurationMs(TimeSpan duration) {
        var milliseconds = duration.TotalMilliseconds;
        if (milliseconds <= 0) {
            return 0;
        }

        if (milliseconds >= long.MaxValue) {
            return long.MaxValue;
        }

        return (long)Math.Round(milliseconds, MidpointRounding.AwayFromZero);
    }

    private static string? NormalizeOptional(string? value) {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
