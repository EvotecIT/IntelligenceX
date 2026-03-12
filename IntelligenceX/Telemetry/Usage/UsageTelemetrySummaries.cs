using System;
using System.Collections.Generic;
using System.Linq;

namespace IntelligenceX.Telemetry.Usage;

/// <summary>
/// Selects the metric used by usage summary calculations.
/// </summary>
public enum UsageSummaryMetric {
    /// <summary>
    /// Summarize by total tokens.
    /// </summary>
    TotalTokens,

    /// <summary>
    /// Summarize by cost in USD.
    /// </summary>
    CostUsd,

    /// <summary>
    /// Summarize by total duration in milliseconds.
    /// </summary>
    DurationMs,

    /// <summary>
    /// Summarize by event count.
    /// </summary>
    EventCount,
}

/// <summary>
/// Configures provider-neutral usage summary generation.
/// </summary>
public sealed class UsageSummaryOptions {
    /// <summary>
    /// Gets or sets the metric used for calculations.
    /// </summary>
    public UsageSummaryMetric Metric { get; set; } = UsageSummaryMetric.TotalTokens;

    /// <summary>
    /// Gets or sets the rolling window sizes, in days, used for burn-rate style summaries.
    /// </summary>
    public IReadOnlyList<int>? RollingWindowDays { get; set; } = new[] { 7, 30 };

    /// <summary>
    /// Gets or sets the maximum number of entries retained per breakdown.
    /// </summary>
    public int BreakdownLimit { get; set; } = 5;
}

/// <summary>
/// Represents a provider-neutral summary snapshot over canonical daily aggregates.
/// </summary>
public sealed class UsageSummarySnapshot {
    /// <summary>
    /// Gets or sets the selected metric.
    /// </summary>
    public UsageSummaryMetric Metric { get; set; }

    /// <summary>
    /// Gets or sets the earliest UTC day included in the snapshot.
    /// </summary>
    public DateTime? StartDayUtc { get; set; }

    /// <summary>
    /// Gets or sets the latest UTC day included in the snapshot.
    /// </summary>
    public DateTime? EndDayUtc { get; set; }

    /// <summary>
    /// Gets or sets the total value summed across all included days.
    /// </summary>
    public decimal TotalValue { get; set; }

    /// <summary>
    /// Gets or sets the number of calendar days covered by the snapshot.
    /// </summary>
    public int TotalDays { get; set; }

    /// <summary>
    /// Gets or sets the number of days with a non-zero metric value.
    /// </summary>
    public int ActiveDays { get; set; }

    /// <summary>
    /// Gets or sets the average value per covered calendar day.
    /// </summary>
    public decimal AveragePerCalendarDay { get; set; }

    /// <summary>
    /// Gets or sets the average value per active day.
    /// </summary>
    public decimal AveragePerActiveDay { get; set; }

    /// <summary>
    /// Gets or sets the UTC day with the highest metric value.
    /// </summary>
    public DateTime? PeakDayUtc { get; set; }

    /// <summary>
    /// Gets or sets the highest single-day metric value.
    /// </summary>
    public decimal PeakValue { get; set; }

    /// <summary>
    /// Gets the rolling-window summaries.
    /// </summary>
    public List<UsageRollingWindowSummary> RollingWindows { get; } = new();

    /// <summary>
    /// Gets the top provider totals when provider information is present.
    /// </summary>
    public List<UsageSummaryBreakdownEntry> ProviderBreakdown { get; } = new();

    /// <summary>
    /// Gets the top account totals when account information is present.
    /// </summary>
    public List<UsageSummaryBreakdownEntry> AccountBreakdown { get; } = new();

    /// <summary>
    /// Gets the top model totals when model information is present.
    /// </summary>
    public List<UsageSummaryBreakdownEntry> ModelBreakdown { get; } = new();

    /// <summary>
    /// Gets the top source-root totals when source-root information is present.
    /// </summary>
    public List<UsageSummaryBreakdownEntry> SourceRootBreakdown { get; } = new();

    /// <summary>
    /// Gets the top person totals when person information is present.
    /// </summary>
    public List<UsageSummaryBreakdownEntry> PersonBreakdown { get; } = new();

    /// <summary>
    /// Gets the top surface totals when surface information is present.
    /// </summary>
    public List<UsageSummaryBreakdownEntry> SurfaceBreakdown { get; } = new();
}

/// <summary>
/// Represents one rolling-window usage summary.
/// </summary>
public sealed class UsageRollingWindowSummary {
    /// <summary>
    /// Gets or sets the requested window size.
    /// </summary>
    public int WindowDays { get; set; }

    /// <summary>
    /// Gets or sets the earliest UTC day included in the window.
    /// </summary>
    public DateTime? StartDayUtc { get; set; }

    /// <summary>
    /// Gets or sets the latest UTC day included in the window.
    /// </summary>
    public DateTime? EndDayUtc { get; set; }

    /// <summary>
    /// Gets or sets the number of days actually covered by this window.
    /// </summary>
    public int DaysCovered { get; set; }

    /// <summary>
    /// Gets or sets the total metric value summed across the window.
    /// </summary>
    public decimal TotalValue { get; set; }

    /// <summary>
    /// Gets or sets the average metric value per covered day.
    /// </summary>
    public decimal AveragePerCalendarDay { get; set; }
}

/// <summary>
/// Represents one ranked breakdown entry in a usage summary.
/// </summary>
public sealed class UsageSummaryBreakdownEntry {
    /// <summary>
    /// Gets or sets the breakdown key.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the aggregated metric value for the key.
    /// </summary>
    public decimal Value { get; set; }
}

/// <summary>
/// Builds provider-neutral usage summaries from canonical daily aggregates.
/// </summary>
public sealed class UsageSummaryBuilder {
    /// <summary>
    /// Builds a summary snapshot from canonical daily aggregates.
    /// </summary>
    public UsageSummarySnapshot Build(
        IEnumerable<UsageDailyAggregateRecord> aggregates,
        UsageSummaryOptions? options = null) {
        if (aggregates is null) {
            throw new ArgumentNullException(nameof(aggregates));
        }

        var effectiveOptions = options ?? new UsageSummaryOptions();
        var ordered = aggregates
            .Where(static aggregate => aggregate is not null)
            .OrderBy(static aggregate => aggregate.DayUtc)
            .ThenBy(static aggregate => aggregate.ProviderId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static aggregate => aggregate.AccountKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static aggregate => aggregate.Model, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static aggregate => aggregate.Surface, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var snapshot = new UsageSummarySnapshot {
            Metric = effectiveOptions.Metric
        };

        if (ordered.Length == 0) {
            return snapshot;
        }

        snapshot.StartDayUtc = ordered[0].DayUtc;
        snapshot.EndDayUtc = ordered[ordered.Length - 1].DayUtc;
        snapshot.TotalDays = (snapshot.EndDayUtc.Value - snapshot.StartDayUtc.Value).Days + 1;

        var valueByDay = ordered
            .GroupBy(static aggregate => aggregate.DayUtc)
            .Select(group => new {
                DayUtc = group.Key,
                Value = group.Sum(aggregate => GetMetricValue(aggregate, effectiveOptions.Metric))
            })
            .OrderBy(static value => value.DayUtc)
            .ToArray();

        snapshot.TotalValue = valueByDay.Sum(static value => value.Value);
        snapshot.ActiveDays = valueByDay.Count(static value => value.Value > 0m);
        snapshot.AveragePerCalendarDay = Divide(snapshot.TotalValue, snapshot.TotalDays);
        snapshot.AveragePerActiveDay = Divide(snapshot.TotalValue, snapshot.ActiveDays);

        var peak = valueByDay
            .OrderByDescending(static value => value.Value)
            .ThenBy(static value => value.DayUtc)
            .FirstOrDefault();
        if (peak is not null) {
            snapshot.PeakDayUtc = peak.DayUtc;
            snapshot.PeakValue = peak.Value;
        }

        foreach (var windowDays in NormalizeRollingWindows(effectiveOptions.RollingWindowDays)) {
            var windowEnd = snapshot.EndDayUtc.Value;
            var requestedStart = windowEnd.AddDays(-(windowDays - 1));
            var windowStart = requestedStart < snapshot.StartDayUtc.Value ? snapshot.StartDayUtc.Value : requestedStart;
            var daysCovered = (windowEnd - windowStart).Days + 1;
            var totalValue = valueByDay
                .Where(value => value.DayUtc >= windowStart && value.DayUtc <= windowEnd)
                .Sum(static value => value.Value);

            snapshot.RollingWindows.Add(new UsageRollingWindowSummary {
                WindowDays = windowDays,
                StartDayUtc = windowStart,
                EndDayUtc = windowEnd,
                DaysCovered = daysCovered,
                TotalValue = totalValue,
                AveragePerCalendarDay = Divide(totalValue, daysCovered)
            });
        }

        PopulateBreakdown(snapshot.ProviderBreakdown, ordered, effectiveOptions, aggregate => NormalizeOptional(aggregate.ProviderId) ?? "unknown-provider");
        PopulateBreakdown(snapshot.AccountBreakdown, ordered, effectiveOptions, aggregate => NormalizeOptional(aggregate.AccountKey) ?? "unknown-account");
        PopulateBreakdown(snapshot.ModelBreakdown, ordered, effectiveOptions, aggregate => NormalizeOptional(aggregate.Model) ?? "unknown-model");
        PopulateBreakdown(snapshot.SourceRootBreakdown, ordered, effectiveOptions, aggregate => NormalizeOptional(aggregate.SourceRootId) ?? "unknown-source-root");
        PopulateBreakdown(snapshot.PersonBreakdown, ordered, effectiveOptions, aggregate => NormalizeOptional(aggregate.PersonLabel) ?? "unknown-person");
        PopulateBreakdown(snapshot.SurfaceBreakdown, ordered, effectiveOptions, aggregate => NormalizeOptional(aggregate.Surface) ?? "unknown-surface");

        return snapshot;
    }

    private static void PopulateBreakdown(
        ICollection<UsageSummaryBreakdownEntry> target,
        IEnumerable<UsageDailyAggregateRecord> aggregates,
        UsageSummaryOptions options,
        Func<UsageDailyAggregateRecord, string> keySelector) {
        var groups = aggregates
            .GroupBy(keySelector, StringComparer.OrdinalIgnoreCase)
            .Select(group => new UsageSummaryBreakdownEntry {
                Key = group.Key,
                Value = group.Sum(aggregate => GetMetricValue(aggregate, options.Metric))
            })
            .Where(static entry => entry.Value > 0m)
            .OrderByDescending(static entry => entry.Value)
            .ThenBy(static entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .Take(options.BreakdownLimit > 0 ? options.BreakdownLimit : 0);

        foreach (var entry in groups) {
            target.Add(entry);
        }
    }

    private static IReadOnlyList<int> NormalizeRollingWindows(IReadOnlyList<int>? values) {
        if (values is null || values.Count == 0) {
            return Array.Empty<int>();
        }

        return values
            .Where(static value => value > 0)
            .Distinct()
            .OrderBy(static value => value)
            .ToArray();
    }

    private static decimal GetMetricValue(UsageDailyAggregateRecord aggregate, UsageSummaryMetric metric) {
        return metric switch {
            UsageSummaryMetric.TotalTokens => aggregate.TotalTokens ?? 0L,
            UsageSummaryMetric.CostUsd => aggregate.TotalCostUsd ?? 0m,
            UsageSummaryMetric.DurationMs => aggregate.TotalDurationMs ?? 0L,
            UsageSummaryMetric.EventCount => aggregate.EventCount,
            _ => 0m
        };
    }

    private static decimal Divide(decimal value, int divisor) {
        if (divisor <= 0) {
            return 0m;
        }

        return value / divisor;
    }

    private static string? NormalizeOptional(string? value) {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
