using System;
using System.Collections.Generic;
using System.Linq;

namespace IntelligenceX.Telemetry.Usage;

/// <summary>
/// Selects additional grouping dimensions for daily usage aggregates.
/// </summary>
[Flags]
public enum UsageAggregateDimensions {
    /// <summary>
    /// Group only by day.
    /// </summary>
    None = 0,

    /// <summary>
    /// Split aggregates by provider.
    /// </summary>
    Provider = 1 << 0,

    /// <summary>
    /// Split aggregates by resolved account.
    /// </summary>
    Account = 1 << 1,

    /// <summary>
    /// Split aggregates by person-level identity.
    /// </summary>
    Person = 1 << 2,

    /// <summary>
    /// Split aggregates by model.
    /// </summary>
    Model = 1 << 3,

    /// <summary>
    /// Split aggregates by source root.
    /// </summary>
    SourceRoot = 1 << 4,

    /// <summary>
    /// Split aggregates by surface.
    /// </summary>
    Surface = 1 << 5,
}

/// <summary>
/// Configures daily usage aggregation.
/// </summary>
public sealed class UsageDailyAggregateOptions {
    /// <summary>
    /// Gets or sets the grouping dimensions beyond the UTC day.
    /// </summary>
    public UsageAggregateDimensions Dimensions { get; set; } = UsageAggregateDimensions.Provider | UsageAggregateDimensions.Account;
}

/// <summary>
/// Represents one provider-neutral daily usage rollup.
/// </summary>
public sealed class UsageDailyAggregateRecord {
    /// <summary>
    /// Initializes a new daily aggregate record.
    /// </summary>
    public UsageDailyAggregateRecord(DateTime dayUtc) {
        DayUtc = dayUtc.Date;
    }

    /// <summary>
    /// Gets the UTC day represented by the aggregate.
    /// </summary>
    public DateTime DayUtc { get; }

    /// <summary>
    /// Gets or sets the provider identifier when provider grouping is enabled.
    /// </summary>
    public string? ProviderId { get; set; }

    /// <summary>
    /// Gets or sets the stable account key when account grouping is enabled.
    /// </summary>
    public string? AccountKey { get; set; }

    /// <summary>
    /// Gets or sets the provider account id that contributed to the account key when available.
    /// </summary>
    public string? ProviderAccountId { get; set; }

    /// <summary>
    /// Gets or sets the display label for the account when known.
    /// </summary>
    public string? AccountLabel { get; set; }

    /// <summary>
    /// Gets or sets the person-level label when known.
    /// </summary>
    public string? PersonLabel { get; set; }

    /// <summary>
    /// Gets or sets the model when model grouping is enabled.
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Gets or sets the source-root id when source-root grouping is enabled.
    /// </summary>
    public string? SourceRootId { get; set; }

    /// <summary>
    /// Gets or sets the surface when surface grouping is enabled.
    /// </summary>
    public string? Surface { get; set; }

    /// <summary>
    /// Gets or sets the number of canonical events included in this rollup.
    /// </summary>
    public int EventCount { get; set; }

    /// <summary>
    /// Gets or sets the summed input token count when available.
    /// </summary>
    public long? InputTokens { get; set; }

    /// <summary>
    /// Gets or sets the summed cached-input token count when available.
    /// </summary>
    public long? CachedInputTokens { get; set; }

    /// <summary>
    /// Gets or sets the summed output token count when available.
    /// </summary>
    public long? OutputTokens { get; set; }

    /// <summary>
    /// Gets or sets the summed reasoning token count when available.
    /// </summary>
    public long? ReasoningTokens { get; set; }

    /// <summary>
    /// Gets or sets the summed total token count when available.
    /// </summary>
    public long? TotalTokens { get; set; }

    /// <summary>
    /// Gets or sets the summed duration in milliseconds when available.
    /// </summary>
    public long? TotalDurationMs { get; set; }

    /// <summary>
    /// Gets or sets the summed cost in USD when available.
    /// </summary>
    public decimal? TotalCostUsd { get; set; }

    /// <summary>
    /// Gets or sets the strongest truth level observed for events in this bucket.
    /// </summary>
    public UsageTruthLevel TruthLevel { get; set; } = UsageTruthLevel.Unknown;
}

/// <summary>
/// Builds provider-neutral daily aggregates from canonical usage events.
/// </summary>
public sealed class UsageDailyAggregateBuilder {
    /// <summary>
    /// Builds daily aggregates from normalized usage events.
    /// </summary>
    public IReadOnlyList<UsageDailyAggregateRecord> Build(
        IEnumerable<UsageEventRecord> events,
        UsageDailyAggregateOptions? options = null) {
        if (events is null) {
            throw new ArgumentNullException(nameof(events));
        }

        var effectiveOptions = options ?? new UsageDailyAggregateOptions();
        var buckets = new Dictionary<string, UsageDailyAggregateAccumulator>(StringComparer.Ordinal);

        foreach (var usageEvent in events) {
            if (usageEvent is null) {
                continue;
            }

            var key = BuildAggregateKey(usageEvent, effectiveOptions.Dimensions);
            if (!buckets.TryGetValue(key, out var accumulator)) {
                accumulator = new UsageDailyAggregateAccumulator(
                    usageEvent.TimestampUtc.UtcDateTime.Date,
                    effectiveOptions.Dimensions.HasFlag(UsageAggregateDimensions.Provider) ? NormalizeOptional(usageEvent.ProviderId) : null,
                    effectiveOptions.Dimensions.HasFlag(UsageAggregateDimensions.Account) ? NormalizeAccountKey(usageEvent) : null,
                    effectiveOptions.Dimensions.HasFlag(UsageAggregateDimensions.Account) ? NormalizeOptional(usageEvent.ProviderAccountId) : null,
                    effectiveOptions.Dimensions.HasFlag(UsageAggregateDimensions.Account) ? NormalizeOptional(usageEvent.AccountLabel) : null,
                    effectiveOptions.Dimensions.HasFlag(UsageAggregateDimensions.Person) ? NormalizeOptional(usageEvent.PersonLabel) : null,
                    effectiveOptions.Dimensions.HasFlag(UsageAggregateDimensions.Model) ? NormalizeOptional(usageEvent.Model) : null,
                    effectiveOptions.Dimensions.HasFlag(UsageAggregateDimensions.SourceRoot) ? NormalizeOptional(usageEvent.SourceRootId) : null,
                    effectiveOptions.Dimensions.HasFlag(UsageAggregateDimensions.Surface) ? NormalizeOptional(usageEvent.Surface) : null);
                buckets.Add(key, accumulator);
            }

            accumulator.EventCount++;
            accumulator.InputTokens = SumNullable(accumulator.InputTokens, usageEvent.InputTokens);
            accumulator.CachedInputTokens = SumNullable(accumulator.CachedInputTokens, usageEvent.CachedInputTokens);
            accumulator.OutputTokens = SumNullable(accumulator.OutputTokens, usageEvent.OutputTokens);
            accumulator.ReasoningTokens = SumNullable(accumulator.ReasoningTokens, usageEvent.ReasoningTokens);
            accumulator.TotalTokens = SumNullable(accumulator.TotalTokens, usageEvent.TotalTokens);
            accumulator.TotalDurationMs = SumNullable(accumulator.TotalDurationMs, usageEvent.DurationMs);
            accumulator.TotalCostUsd = SumNullable(accumulator.TotalCostUsd, usageEvent.CostUsd);
            accumulator.TruthLevel = GetStrongerTruthLevel(accumulator.TruthLevel, usageEvent.TruthLevel);
            accumulator.ProviderAccountId ??= NormalizeOptional(usageEvent.ProviderAccountId);
            accumulator.AccountLabel ??= NormalizeOptional(usageEvent.AccountLabel);
            accumulator.PersonLabel ??= NormalizeOptional(usageEvent.PersonLabel);
            accumulator.SourceRootId ??= NormalizeOptional(usageEvent.SourceRootId);
        }

        return buckets.Values
            .Select(static value => value.ToRecord())
            .OrderBy(static value => value.DayUtc)
            .ThenBy(static value => value.ProviderId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static value => value.AccountKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static value => value.Model, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static value => value.SourceRootId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static value => value.Surface, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string BuildAggregateKey(UsageEventRecord usageEvent, UsageAggregateDimensions dimensions) {
        var parts = new List<string> {
            usageEvent.TimestampUtc.UtcDateTime.ToString("yyyy-MM-dd")
        };

        if (dimensions.HasFlag(UsageAggregateDimensions.Provider)) {
            parts.Add(NormalizeOptional(usageEvent.ProviderId) ?? "unknown-provider");
        }
        if (dimensions.HasFlag(UsageAggregateDimensions.Account)) {
            parts.Add(NormalizeAccountKey(usageEvent));
        }
        if (dimensions.HasFlag(UsageAggregateDimensions.Person)) {
            parts.Add(NormalizeOptional(usageEvent.PersonLabel) ?? "unknown-person");
        }
        if (dimensions.HasFlag(UsageAggregateDimensions.Model)) {
            parts.Add(NormalizeOptional(usageEvent.Model) ?? "unknown-model");
        }
        if (dimensions.HasFlag(UsageAggregateDimensions.SourceRoot)) {
            parts.Add(NormalizeOptional(usageEvent.SourceRootId) ?? "unknown-source-root");
        }
        if (dimensions.HasFlag(UsageAggregateDimensions.Surface)) {
            parts.Add(NormalizeOptional(usageEvent.Surface) ?? "unknown-surface");
        }

        return string.Join("|", parts);
    }

    private static string NormalizeAccountKey(UsageEventRecord usageEvent) {
        var providerAccountId = NormalizeOptional(usageEvent.ProviderAccountId);
        if (!string.IsNullOrWhiteSpace(providerAccountId)) {
            return "acct:" + providerAccountId;
        }

        var accountLabel = NormalizeOptional(usageEvent.AccountLabel);
        if (!string.IsNullOrWhiteSpace(accountLabel)) {
            return "label:" + accountLabel;
        }

        return "unknown";
    }

    private static long? SumNullable(long? current, long? next) {
        if (!current.HasValue) {
            return next;
        }
        if (!next.HasValue) {
            return current;
        }

        return current.Value + next.Value;
    }

    private static decimal? SumNullable(decimal? current, decimal? next) {
        if (!current.HasValue) {
            return next;
        }
        if (!next.HasValue) {
            return current;
        }

        return current.Value + next.Value;
    }

    private static UsageTruthLevel GetStrongerTruthLevel(UsageTruthLevel left, UsageTruthLevel right) {
        return RankTruthLevel(right) > RankTruthLevel(left) ? right : left;
    }

    private static int RankTruthLevel(UsageTruthLevel value) {
        return value switch {
            UsageTruthLevel.Exact => 4,
            UsageTruthLevel.Inferred => 3,
            UsageTruthLevel.Estimated => 2,
            _ => 1
        };
    }

    private static string? NormalizeOptional(string? value) {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private sealed class UsageDailyAggregateAccumulator {
        public UsageDailyAggregateAccumulator(
            DateTime dayUtc,
            string? providerId,
            string? accountKey,
            string? providerAccountId,
            string? accountLabel,
            string? personLabel,
            string? model,
            string? sourceRootId,
            string? surface) {
            DayUtc = dayUtc;
            ProviderId = providerId;
            AccountKey = accountKey;
            ProviderAccountId = providerAccountId;
            AccountLabel = accountLabel;
            PersonLabel = personLabel;
            Model = model;
            SourceRootId = sourceRootId;
            Surface = surface;
        }

        public DateTime DayUtc { get; }
        public string? ProviderId { get; }
        public string? AccountKey { get; }
        public string? ProviderAccountId { get; set; }
        public string? AccountLabel { get; set; }
        public string? PersonLabel { get; set; }
        public string? Model { get; }
        public string? SourceRootId { get; set; }
        public string? Surface { get; }
        public int EventCount { get; set; }
        public long? InputTokens { get; set; }
        public long? CachedInputTokens { get; set; }
        public long? OutputTokens { get; set; }
        public long? ReasoningTokens { get; set; }
        public long? TotalTokens { get; set; }
        public long? TotalDurationMs { get; set; }
        public decimal? TotalCostUsd { get; set; }
        public UsageTruthLevel TruthLevel { get; set; } = UsageTruthLevel.Unknown;

        public UsageDailyAggregateRecord ToRecord() {
            return new UsageDailyAggregateRecord(DayUtc) {
                ProviderId = ProviderId,
                AccountKey = AccountKey,
                ProviderAccountId = ProviderAccountId,
                AccountLabel = AccountLabel,
                PersonLabel = PersonLabel,
                Model = Model,
                SourceRootId = SourceRootId,
                Surface = Surface,
                EventCount = EventCount,
                InputTokens = InputTokens,
                CachedInputTokens = CachedInputTokens,
                OutputTokens = OutputTokens,
                ReasoningTokens = ReasoningTokens,
                TotalTokens = TotalTokens,
                TotalDurationMs = TotalDurationMs,
                TotalCostUsd = TotalCostUsd,
                TruthLevel = TruthLevel
            };
        }
    }
}
