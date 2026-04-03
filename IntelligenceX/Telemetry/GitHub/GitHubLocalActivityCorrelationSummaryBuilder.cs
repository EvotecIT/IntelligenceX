using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.Telemetry.Git;
using IntelligenceX.Telemetry.Usage;

namespace IntelligenceX.Telemetry.GitHub;

#pragma warning disable CS1591

/// <summary>
/// Correlates watched GitHub repository momentum with local code churn and telemetry activity.
/// </summary>
internal static class GitHubLocalActivityCorrelationSummaryBuilder {
    private const int RecentWindowDays = 7;
    private const double MinimumCorrelationMagnitude = 0.25d;

    public static GitHubLocalActivityCorrelationSummaryData Build(
        GitCodeChurnSummaryData? churnSummary,
        IReadOnlyList<UsageEventRecord>? usageEvents,
        GitHubObservabilitySummaryData? gitHubSummary) {
        if (usageEvents is null || usageEvents.Count == 0) {
            return BuildFromDailySeries(churnSummary, Array.Empty<GitCodeUsageProviderSeriesData>(), gitHubSummary);
        }

        var providerSeries = usageEvents
            .GroupBy(static usageEvent => NormalizeProviderId(usageEvent.ProviderId))
            .Select(group => new GitCodeUsageProviderSeriesData(
                group.Key,
                group.Key,
                group.GroupBy(static usageEvent => usageEvent.TimestampUtc.UtcDateTime.Date)
                    .Select(static dayGroup => new GitCodeUsageDailyValueData(
                        dayGroup.Key,
                        dayGroup.Sum(static usageEvent => usageEvent.TotalTokens ?? 0L),
                        dayGroup.Count()))
                    .ToArray()))
            .ToArray();

        return BuildFromDailySeries(churnSummary, providerSeries, gitHubSummary);
    }

    public static GitHubLocalActivityCorrelationSummaryData BuildFromDailySeries(
        GitCodeChurnSummaryData? churnSummary,
        IReadOnlyList<GitCodeUsageProviderSeriesData>? providerSeries,
        GitHubObservabilitySummaryData? gitHubSummary) {
        if (churnSummary is null || !churnSummary.HasData || gitHubSummary is null || gitHubSummary.Repositories.Count == 0) {
            return GitHubLocalActivityCorrelationSummaryData.Empty;
        }

        var trendDays = churnSummary.TrendDays
            .Where(static day => day.DayUtc != default)
            .OrderBy(static day => day.DayUtc)
            .ToArray();
        if (trendDays.Length == 0) {
            return GitHubLocalActivityCorrelationSummaryData.Empty;
        }

        var recentEndDay = trendDays[trendDays.Length - 1].DayUtc.Date;
        var recentStartDay = recentEndDay.AddDays(-(RecentWindowDays - 1));
        var recentDays = Enumerable.Range(0, RecentWindowDays)
            .Select(offset => recentStartDay.AddDays(offset))
            .ToArray();

        var churnByDay = trendDays.ToDictionary(static day => day.DayUtc.Date, static day => day);
        var usageByDay = BuildUsageByDay(providerSeries);
        var localSignalByDay = new Dictionary<DateTime, double>(RecentWindowDays);
        var activeLocalDays = 0;
        var recentActivityTotal = 0d;
        foreach (var day in recentDays) {
            var changedLines = churnByDay.TryGetValue(day, out var churnDay) ? churnDay.TotalChangedLines : 0;
            var activityValue = usageByDay.TryGetValue(day, out var usageValue) ? usageValue : 0d;
            if (changedLines > 0 || activityValue > 0d) {
                activeLocalDays++;
            }

            recentActivityTotal += activityValue;
            localSignalByDay[day] = ComputeLocalSignal(changedLines, activityValue);
        }

        var correlations = new List<GitHubLocalActivityRepositoryCorrelationData>();
        foreach (var repository in gitHubSummary.Repositories) {
            var correlation = TryBuildCorrelation(repository, localSignalByDay, recentDays);
            if (correlation is not null) {
                correlations.Add(correlation);
            }
        }

        return new GitHubLocalActivityCorrelationSummaryData(
            repositoryName: churnSummary.RepositoryName,
            watchedRepositoryCount: gitHubSummary.Repositories.Count,
            recentChurnVolume: churnSummary.RecentAddedLines + churnSummary.RecentDeletedLines,
            recentUsageTotal: recentActivityTotal,
            activeLocalDays: activeLocalDays,
            repositoryCorrelations: correlations
                .OrderByDescending(static correlation => Math.Abs(correlation.Correlation))
                .ThenByDescending(static correlation => correlation.RecentStars)
                .ThenBy(static correlation => correlation.RepositoryNameWithOwner, StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    private static GitHubLocalActivityRepositoryCorrelationData? TryBuildCorrelation(
        GitHubObservedRepositoryTrendData repository,
        IReadOnlyDictionary<DateTime, double> localSignalByDay,
        IReadOnlyList<DateTime> recentDays) {
        var repoSignals = repository.TrendPoints
            .Where(static point => point.DayUtc != default)
            .ToDictionary(static point => point.DayUtc.Date, static point => point.Score);
        var overlapDays = recentDays
            .Where(day => localSignalByDay.ContainsKey(day) && repoSignals.ContainsKey(day))
            .ToArray();
        if (overlapDays.Length < 4) {
            return null;
        }

        var localValues = overlapDays
            .Select(day => localSignalByDay[day])
            .ToArray();
        var repoValues = overlapDays
            .Select(day => repoSignals[day])
            .ToArray();
        var correlation = ComputePearson(localValues, repoValues);
        if (double.IsNaN(correlation) || double.IsInfinity(correlation) || Math.Abs(correlation) < MinimumCorrelationMagnitude) {
            return null;
        }

        var alignedDays = overlapDays.Count(day => localSignalByDay[day] > 0d && repoSignals[day] > 0d);
        var opposingDays = overlapDays.Count(day => localSignalByDay[day] > 0d && repoSignals[day] < 0d);
        var localQuietDays = overlapDays.Count(day => localSignalByDay[day] <= 0d);
        var recentMomentumScore = overlapDays.Sum(day => repoSignals[day]);

        return new GitHubLocalActivityRepositoryCorrelationData(
            repositoryNameWithOwner: repository.RepositoryNameWithOwner,
            correlation: correlation,
            overlapDays: overlapDays.Length,
            alignedDays: alignedDays,
            opposingDays: opposingDays,
            localQuietDays: localQuietDays,
            recentMomentumScore: recentMomentumScore,
            recentStars: repository.Stars,
            recentForks: repository.Forks,
            recentWatchers: repository.Watchers,
            starDelta: repository.StarDelta,
            forkDelta: repository.ForkDelta,
            watcherDelta: repository.WatcherDelta);
    }

    private static Dictionary<DateTime, double> BuildUsageByDay(IReadOnlyList<GitCodeUsageProviderSeriesData>? providerSeries) {
        if (providerSeries is null || providerSeries.Count == 0) {
            return new Dictionary<DateTime, double>();
        }

        var usageByDay = new Dictionary<DateTime, double>();
        foreach (var provider in providerSeries.Where(static provider => !string.Equals(NormalizeProviderId(provider.ProviderId), "github", StringComparison.OrdinalIgnoreCase))) {
            foreach (var day in provider.Days.Where(static day => day.Day != default)) {
                usageByDay[day.Day.Date] = usageByDay.TryGetValue(day.Day.Date, out var existing)
                    ? existing + day.ActivityValue
                    : day.ActivityValue;
            }
        }

        return usageByDay;
    }

    private static double ComputeLocalSignal(int changedLines, double activityValue) {
        if (changedLines <= 0 && activityValue <= 0d) {
            return 0d;
        }

        return Math.Log10(1d + Math.Max(0d, changedLines))
               + Math.Log10(1d + Math.Max(0d, activityValue));
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

internal sealed class GitHubLocalActivityCorrelationSummaryData {
    public static GitHubLocalActivityCorrelationSummaryData Empty { get; } = new(
        repositoryName: null,
        watchedRepositoryCount: 0,
        recentChurnVolume: 0,
        recentUsageTotal: 0d,
        activeLocalDays: 0,
        repositoryCorrelations: Array.Empty<GitHubLocalActivityRepositoryCorrelationData>());

    public GitHubLocalActivityCorrelationSummaryData(
        string? repositoryName,
        int watchedRepositoryCount,
        int recentChurnVolume,
        double recentUsageTotal,
        int activeLocalDays,
        IReadOnlyList<GitHubLocalActivityRepositoryCorrelationData> repositoryCorrelations) {
        RepositoryName = string.IsNullOrWhiteSpace(repositoryName) ? null : repositoryName!.Trim();
        WatchedRepositoryCount = Math.Max(0, watchedRepositoryCount);
        RecentChurnVolume = Math.Max(0, recentChurnVolume);
        RecentUsageTotal = Math.Max(0d, recentUsageTotal);
        ActiveLocalDays = Math.Max(0, activeLocalDays);
        RepositoryCorrelations = repositoryCorrelations ?? Array.Empty<GitHubLocalActivityRepositoryCorrelationData>();
    }

    public string? RepositoryName { get; }
    public int WatchedRepositoryCount { get; }
    public int RecentChurnVolume { get; }
    public double RecentUsageTotal { get; }
    public int ActiveLocalDays { get; }
    public IReadOnlyList<GitHubLocalActivityRepositoryCorrelationData> RepositoryCorrelations { get; }
    public bool HasData => WatchedRepositoryCount > 0 && (RecentChurnVolume > 0 || RecentUsageTotal > 0d);
    public bool HasSignals => RepositoryCorrelations.Count > 0;
    public GitHubLocalActivityRepositoryCorrelationData? StrongestPositiveCorrelation => RepositoryCorrelations
        .Where(static repository => repository.Correlation > 0d)
        .OrderByDescending(static repository => repository.Correlation)
        .ThenByDescending(static repository => repository.RecentStars)
        .FirstOrDefault();
    public GitHubLocalActivityRepositoryCorrelationData? StrongestNegativeCorrelation => RepositoryCorrelations
        .Where(static repository => repository.Correlation < 0d)
        .OrderBy(static repository => repository.Correlation)
        .ThenByDescending(static repository => repository.RecentStars)
        .FirstOrDefault();
}

internal sealed class GitHubLocalActivityRepositoryCorrelationData {
    public GitHubLocalActivityRepositoryCorrelationData(
        string repositoryNameWithOwner,
        double correlation,
        int overlapDays,
        int alignedDays,
        int opposingDays,
        int localQuietDays,
        double recentMomentumScore,
        int recentStars,
        int recentForks,
        int recentWatchers,
        int starDelta,
        int forkDelta,
        int watcherDelta) {
        RepositoryNameWithOwner = string.IsNullOrWhiteSpace(repositoryNameWithOwner)
            ? throw new ArgumentNullException(nameof(repositoryNameWithOwner))
            : repositoryNameWithOwner.Trim();
        Correlation = correlation;
        OverlapDays = Math.Max(0, overlapDays);
        AlignedDays = Math.Max(0, alignedDays);
        OpposingDays = Math.Max(0, opposingDays);
        LocalQuietDays = Math.Max(0, localQuietDays);
        RecentMomentumScore = recentMomentumScore;
        RecentStars = Math.Max(0, recentStars);
        RecentForks = Math.Max(0, recentForks);
        RecentWatchers = Math.Max(0, recentWatchers);
        StarDelta = starDelta;
        ForkDelta = forkDelta;
        WatcherDelta = watcherDelta;
    }

    public string RepositoryNameWithOwner { get; }
    public double Correlation { get; }
    public int OverlapDays { get; }
    public int AlignedDays { get; }
    public int OpposingDays { get; }
    public int LocalQuietDays { get; }
    public double RecentMomentumScore { get; }
    public int RecentStars { get; }
    public int RecentForks { get; }
    public int RecentWatchers { get; }
    public int StarDelta { get; }
    public int ForkDelta { get; }
    public int WatcherDelta { get; }
}
