using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using IntelligenceX.Telemetry.Usage;

namespace IntelligenceX.Telemetry.Git;

#pragma warning disable CS1591

/// <summary>
/// Builds a compact summary that compares local git churn with recent telemetry usage patterns.
/// </summary>
internal static class GitCodeUsageCorrelationSummaryBuilder {
    private const int RecentWindowDays = 7;
    private const double MinimumCorrelationMagnitude = 0.25d;

    public static GitCodeUsageCorrelationSummaryData Build(
        GitCodeChurnSummaryData? churnSummary,
        IReadOnlyList<UsageEventRecord>? usageEvents,
        Func<string, string?>? providerDisplayNameSelector = null,
        string? activityUnitsLabel = null) {
        if (churnSummary is null || !churnSummary.HasData || usageEvents is null || usageEvents.Count == 0) {
            return GitCodeUsageCorrelationSummaryData.Empty;
        }

        var providers = usageEvents
            .GroupBy(static usageEvent => NormalizeProviderId(usageEvent.ProviderId))
            .Select(group => new GitCodeUsageProviderSeriesData(
                group.Key,
                providerDisplayNameSelector?.Invoke(group.Key) ?? group.Key,
                group.GroupBy(static usageEvent => usageEvent.TimestampUtc.ToLocalTime().Date)
                    .Select(static dayGroup => new GitCodeUsageDailyValueData(
                        dayGroup.Key,
                        dayGroup.Sum(static usageEvent => usageEvent.TotalTokens ?? 0L),
                        dayGroup.Count()))
                    .ToArray()))
            .ToArray();

        return BuildFromDailySeries(churnSummary, providers, activityUnitsLabel ?? "usage");
    }

    public static GitCodeUsageCorrelationSummaryData BuildFromDailySeries(
        GitCodeChurnSummaryData? churnSummary,
        IReadOnlyList<GitCodeUsageProviderSeriesData>? providers,
        string? activityUnitsLabel = null) {
        if (churnSummary is null || !churnSummary.HasData || providers is null || providers.Count == 0) {
            return GitCodeUsageCorrelationSummaryData.Empty;
        }

        var trendDays = churnSummary.TrendDays
            .Where(static day => day.DayUtc != default)
            .OrderBy(static day => day.DayUtc)
            .ToArray();
        if (trendDays.Length == 0) {
            return GitCodeUsageCorrelationSummaryData.Empty;
        }

        var recentEndDay = trendDays[trendDays.Length - 1].DayUtc.Date;
        var recentStartDay = recentEndDay.AddDays(-(RecentWindowDays - 1));
        var previousEndDay = recentStartDay.AddDays(-1);
        var previousStartDay = previousEndDay.AddDays(-(RecentWindowDays - 1));

        var recentDays = Enumerable.Range(0, RecentWindowDays)
            .Select(offset => recentStartDay.AddDays(offset))
            .ToArray();
        var previousDays = Enumerable.Range(0, RecentWindowDays)
            .Select(offset => previousStartDay.AddDays(offset))
            .ToArray();

        var churnByDay = trendDays.ToDictionary(static day => day.DayUtc.Date, static day => day);
        var providerCorrelations = new List<GitCodeUsageProviderCorrelationData>();
        double recentActivityTotal = 0d;
        double previousActivityTotal = 0d;
        var recentActivityDays = new HashSet<DateTime>();
        var previousActivityDays = new HashSet<DateTime>();
        var recentEventCount = 0;
        var previousEventCount = 0;

        foreach (var provider in providers) {
            if (string.IsNullOrWhiteSpace(provider.ProviderId)) {
                continue;
            }

            var dailyValues = provider.Days
                .Where(static day => day.Day != default)
                .GroupBy(static day => day.Day.Date)
                .ToDictionary(
                    static group => group.Key,
                    static group => new GitCodeUsageDailyValueData(
                        group.Key,
                        group.Sum(static day => day.ActivityValue),
                        group.Sum(static day => day.EventCount)));

            if (dailyValues.Count == 0) {
                continue;
            }

            recentActivityTotal += recentDays.Sum(day => dailyValues.TryGetValue(day, out var value) ? value.ActivityValue : 0d);
            previousActivityTotal += previousDays.Sum(day => dailyValues.TryGetValue(day, out var value) ? value.ActivityValue : 0d);
            foreach (var day in recentDays.Where(day => dailyValues.TryGetValue(day, out var value) && value.ActivityValue > 0d)) {
                recentActivityDays.Add(day);
            }
            foreach (var day in previousDays.Where(day => dailyValues.TryGetValue(day, out var value) && value.ActivityValue > 0d)) {
                previousActivityDays.Add(day);
            }
            recentEventCount += recentDays.Sum(day => dailyValues.TryGetValue(day, out var value) ? value.EventCount : 0);
            previousEventCount += previousDays.Sum(day => dailyValues.TryGetValue(day, out var value) ? value.EventCount : 0);

            var correlation = TryBuildCorrelation(provider, dailyValues, churnByDay, recentDays);
            if (correlation is not null) {
                providerCorrelations.Add(correlation);
            }
        }

        return new GitCodeUsageCorrelationSummaryData(
            churnSummary.RepositoryName,
            activityUnitsLabel ?? "usage",
            churnSummary.RecentAddedLines + churnSummary.RecentDeletedLines,
            churnSummary.PreviousAddedLines + churnSummary.PreviousDeletedLines,
            recentActivityTotal,
            previousActivityTotal,
            recentEventCount,
            previousEventCount,
            recentActivityDays.Count,
            previousActivityDays.Count,
            providerCorrelations
                .OrderByDescending(static item => Math.Abs(item.Correlation))
                .ThenByDescending(static item => item.RecentActivityValue)
                .ThenBy(static item => item.ProviderDisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    private static GitCodeUsageProviderCorrelationData? TryBuildCorrelation(
        GitCodeUsageProviderSeriesData provider,
        IReadOnlyDictionary<DateTime, GitCodeUsageDailyValueData> dailyValues,
        IReadOnlyDictionary<DateTime, GitCodeChurnDayData> churnByDay,
        IReadOnlyList<DateTime> recentDays) {
        var overlapDays = recentDays
            .Where(day => churnByDay.ContainsKey(day))
            .ToArray();
        if (overlapDays.Length < 4) {
            return null;
        }

        var churnValues = overlapDays
            .Select(day => Math.Log10(1d + Math.Max(0d, churnByDay[day].TotalChangedLines)))
            .ToArray();
        var usageValues = overlapDays
            .Select(day => Math.Log10(1d + Math.Max(0d, dailyValues.TryGetValue(day, out var value) ? value.ActivityValue : 0d)))
            .ToArray();
        var correlation = ComputePearson(churnValues, usageValues);
        if (double.IsNaN(correlation) || double.IsInfinity(correlation) || Math.Abs(correlation) < MinimumCorrelationMagnitude) {
            return null;
        }

        var recentActivityValue = overlapDays.Sum(day => dailyValues.TryGetValue(day, out var value) ? value.ActivityValue : 0d);
        var recentEventCount = overlapDays.Sum(day => dailyValues.TryGetValue(day, out var value) ? value.EventCount : 0);
        var providerActiveDays = overlapDays.Count(day => dailyValues.TryGetValue(day, out var value) && value.ActivityValue > 0d);
        var sharedActiveDays = overlapDays.Count(day =>
            churnByDay[day].TotalChangedLines > 0 &&
            dailyValues.TryGetValue(day, out var value) &&
            value.ActivityValue > 0d);

        return new GitCodeUsageProviderCorrelationData(
            provider.ProviderId,
            provider.ProviderDisplayName,
            correlation,
            overlapDays.Length,
            sharedActiveDays,
            providerActiveDays,
            recentActivityValue,
            recentEventCount);
    }

    private static double ComputePearson(IReadOnlyList<double> left, IReadOnlyList<double> right) {
        if (left is null || right is null || left.Count != right.Count || left.Count < 2) {
            return double.NaN;
        }

        var meanLeft = left.Average();
        var meanRight = right.Average();
        double covariance = 0d;
        double varianceLeft = 0d;
        double varianceRight = 0d;
        for (var i = 0; i < left.Count; i++) {
            var leftCentered = left[i] - meanLeft;
            var rightCentered = right[i] - meanRight;
            covariance += leftCentered * rightCentered;
            varianceLeft += leftCentered * leftCentered;
            varianceRight += rightCentered * rightCentered;
        }

        if (varianceLeft <= 0d || varianceRight <= 0d) {
            return double.NaN;
        }

        return covariance / Math.Sqrt(varianceLeft * varianceRight);
    }

    private static string NormalizeProviderId(string? providerId) {
        var normalized = providerId?.Trim();
        return string.IsNullOrWhiteSpace(normalized)
            ? "unknown"
            : normalized!.ToLowerInvariant();
    }
}

/// <summary>
/// Compact repository churn vs usage summary.
/// </summary>
internal sealed class GitCodeUsageCorrelationSummaryData {
    public static GitCodeUsageCorrelationSummaryData Empty { get; } = new(
        repositoryName: null,
        activityUnitsLabel: "usage",
        recentChurnVolume: 0,
        previousChurnVolume: 0,
        recentActivityTotal: 0d,
        previousActivityTotal: 0d,
        recentEventCount: 0,
        previousEventCount: 0,
        recentActivityDays: 0,
        previousActivityDays: 0,
        providerCorrelations: Array.Empty<GitCodeUsageProviderCorrelationData>());

    public GitCodeUsageCorrelationSummaryData(
        string? repositoryName,
        string activityUnitsLabel,
        int recentChurnVolume,
        int previousChurnVolume,
        double recentActivityTotal,
        double previousActivityTotal,
        int recentEventCount,
        int previousEventCount,
        int recentActivityDays,
        int previousActivityDays,
        IReadOnlyList<GitCodeUsageProviderCorrelationData> providerCorrelations) {
        var normalizedRepositoryName = repositoryName;
        if (string.IsNullOrWhiteSpace(normalizedRepositoryName)) {
            normalizedRepositoryName = null;
        } else {
            normalizedRepositoryName = normalizedRepositoryName!.Trim();
        }

        var normalizedUnitsLabel = activityUnitsLabel;
        if (string.IsNullOrWhiteSpace(normalizedUnitsLabel)) {
            normalizedUnitsLabel = "usage";
        } else {
            normalizedUnitsLabel = normalizedUnitsLabel.Trim();
        }

        RepositoryName = normalizedRepositoryName;
        ActivityUnitsLabel = normalizedUnitsLabel;
        RecentChurnVolume = Math.Max(0, recentChurnVolume);
        PreviousChurnVolume = Math.Max(0, previousChurnVolume);
        RecentActivityTotal = Math.Max(0d, recentActivityTotal);
        PreviousActivityTotal = Math.Max(0d, previousActivityTotal);
        RecentEventCount = Math.Max(0, recentEventCount);
        PreviousEventCount = Math.Max(0, previousEventCount);
        RecentActivityDays = Math.Max(0, recentActivityDays);
        PreviousActivityDays = Math.Max(0, previousActivityDays);
        ProviderCorrelations = providerCorrelations ?? Array.Empty<GitCodeUsageProviderCorrelationData>();
    }

    public string? RepositoryName { get; }
    public string ActivityUnitsLabel { get; }
    public int RecentChurnVolume { get; }
    public int PreviousChurnVolume { get; }
    public double RecentActivityTotal { get; }
    public double PreviousActivityTotal { get; }
    public int RecentEventCount { get; }
    public int PreviousEventCount { get; }
    public int RecentActivityDays { get; }
    public int PreviousActivityDays { get; }
    public IReadOnlyList<GitCodeUsageProviderCorrelationData> ProviderCorrelations { get; }

    public bool HasData => RecentChurnVolume > 0 || PreviousChurnVolume > 0 || RecentActivityTotal > 0d || PreviousActivityTotal > 0d;
    public bool HasCorrelationSignals => ProviderCorrelations.Count > 0;
    public double ActivityDeltaRatio => ComputeDeltaRatio(RecentActivityTotal, PreviousActivityTotal);
    public double ChurnDeltaRatio => ComputeDeltaRatio(RecentChurnVolume, PreviousChurnVolume);
    public GitCodeUsageProviderCorrelationData? StrongestPositiveCorrelation => ProviderCorrelations
        .Where(static item => item.Correlation > 0d)
        .OrderByDescending(static item => item.Correlation)
        .ThenByDescending(static item => item.RecentActivityValue)
        .FirstOrDefault();
    public GitCodeUsageProviderCorrelationData? StrongestNegativeCorrelation => ProviderCorrelations
        .Where(static item => item.Correlation < 0d)
        .OrderBy(static item => item.Correlation)
        .ThenByDescending(static item => item.RecentActivityValue)
        .FirstOrDefault();

    private static double ComputeDeltaRatio(double recentValue, double previousValue) {
        if (previousValue > 0d) {
            return (recentValue - previousValue) / previousValue;
        }

        return recentValue > 0d ? 1d : 0d;
    }
}

/// <summary>
/// One provider-level churn vs usage correlation result.
/// </summary>
internal sealed class GitCodeUsageProviderCorrelationData {
    public GitCodeUsageProviderCorrelationData(
        string providerId,
        string providerDisplayName,
        double correlation,
        int overlapDays,
        int sharedActiveDays,
        int providerActiveDays,
        double recentActivityValue,
        int recentEventCount) {
        ProviderId = string.IsNullOrWhiteSpace(providerId) ? "unknown" : providerId.Trim();
        ProviderDisplayName = string.IsNullOrWhiteSpace(providerDisplayName) ? ProviderId : providerDisplayName.Trim();
        Correlation = correlation;
        OverlapDays = Math.Max(0, overlapDays);
        SharedActiveDays = Math.Max(0, sharedActiveDays);
        ProviderActiveDays = Math.Max(0, providerActiveDays);
        RecentActivityValue = Math.Max(0d, recentActivityValue);
        RecentEventCount = Math.Max(0, recentEventCount);
    }

    public string ProviderId { get; }
    public string ProviderDisplayName { get; }
    public double Correlation { get; }
    public int OverlapDays { get; }
    public int SharedActiveDays { get; }
    public int ProviderActiveDays { get; }
    public double RecentActivityValue { get; }
    public int RecentEventCount { get; }
}

/// <summary>
/// Daily usage/activity series for one provider.
/// </summary>
internal sealed class GitCodeUsageProviderSeriesData {
    public GitCodeUsageProviderSeriesData(
        string providerId,
        string providerDisplayName,
        IReadOnlyList<GitCodeUsageDailyValueData> days) {
        ProviderId = string.IsNullOrWhiteSpace(providerId) ? "unknown" : providerId.Trim();
        ProviderDisplayName = string.IsNullOrWhiteSpace(providerDisplayName) ? ProviderId : providerDisplayName.Trim();
        Days = days ?? Array.Empty<GitCodeUsageDailyValueData>();
    }

    public string ProviderId { get; }
    public string ProviderDisplayName { get; }
    public IReadOnlyList<GitCodeUsageDailyValueData> Days { get; }
}

/// <summary>
/// One daily activity point for churn-vs-usage comparison.
/// </summary>
internal sealed class GitCodeUsageDailyValueData {
    public GitCodeUsageDailyValueData(
        DateTime day,
        double activityValue,
        int eventCount) {
        Day = day.Date;
        ActivityValue = Math.Max(0d, activityValue);
        EventCount = Math.Max(0, eventCount);
    }

    public DateTime Day { get; }
    public double ActivityValue { get; }
    public int EventCount { get; }
}
