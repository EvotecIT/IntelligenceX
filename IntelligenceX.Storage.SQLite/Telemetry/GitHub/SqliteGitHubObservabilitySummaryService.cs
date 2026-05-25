using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IntelligenceX.Telemetry.Usage;

namespace IntelligenceX.Telemetry.GitHub;

/// <summary>
/// Loads compact GitHub repository watch momentum data from the SQLite observability store.
/// </summary>
internal sealed class SqliteGitHubObservabilitySummaryService {
    /// <summary>
    /// Reads the local GitHub observability snapshot store and summarizes the latest tracked state.
    /// </summary>
    /// <returns>Normalized watch summary data.</returns>
    public GitHubObservabilitySummaryData Load() {
        var dbPath = UsageTelemetryPathResolver.ResolveDatabasePath(enabledByDefault: true);
        if (string.IsNullOrWhiteSpace(dbPath)) {
            return GitHubObservabilitySummaryData.Empty;
        }

        dbPath = Path.GetFullPath(dbPath);
        if (!File.Exists(dbPath)) {
            return GitHubObservabilitySummaryService.CreateMissingDatabaseSummary(dbPath);
        }

        using var watchStore = new SqliteGitHubRepositoryWatchStore(dbPath);
        using var snapshotStore = new SqliteGitHubRepositorySnapshotStore(dbPath);
        using var forkSnapshotStore = new SqliteGitHubRepositoryForkSnapshotStore(dbPath);
        using var stargazerSnapshotStore = new SqliteGitHubRepositoryStargazerSnapshotStore(dbPath);
        var service = new GitHubRepositoryObservabilityService(watchStore, snapshotStore);
        var watches = service.GetWatches(enabledOnly: true);
        var latestDeltas = new List<GitHubRepositorySnapshotDelta>(watches.Count);
        var repositories = new List<GitHubObservedRepositoryTrendData>(watches.Count);
        foreach (var watch in watches) {
            var snapshots = snapshotStore.GetByWatch(watch.Id);
            if (snapshots.Count == 0) {
                continue;
            }

            var latestSnapshot = snapshots[snapshots.Count - 1];
            var previousSnapshot = snapshots.Count > 1 ? snapshots[snapshots.Count - 2] : null;
            var latestDelta = GitHubRepositorySnapshotAnalytics.CreateDelta(previousSnapshot, latestSnapshot);
            latestDeltas.Add(latestDelta);

            repositories.Add(new GitHubObservedRepositoryTrendData(
                watch.RepositoryNameWithOwner,
                latestDelta.Stars,
                latestDelta.Forks,
                latestDelta.Watchers,
                latestDelta.OpenIssues,
                latestDelta.StarDelta,
                latestDelta.ForkDelta,
                latestDelta.WatcherDelta,
                latestDelta.OpenIssueDelta,
                latestDelta.CurrentCapturedAtUtc,
                latestDelta.PreviousCapturedAtUtc,
                GitHubObservabilitySummaryService.BuildTrendPoints(
                    GitHubRepositorySnapshotAnalytics.BuildDailyDeltas(snapshots))));
        }

        var comparableDeltas = latestDeltas
            .Where(static delta => delta.PreviousCapturedAtUtc.HasValue)
            .ToArray();

        var correlations = GitHubObservabilitySummaryService.BuildCorrelations(repositories);
        var starCorrelations = GitHubObservabilitySummaryService.BuildStarCorrelations(repositories);
        var allForkSnapshots = forkSnapshotStore.GetAll();
        var allStargazerSnapshots = stargazerSnapshotStore.GetAll();
        var watchedRepositoryNames = new HashSet<string>(
            watches.Select(static watch => watch.RepositoryNameWithOwner),
            StringComparer.OrdinalIgnoreCase);
        var forkNetworkOverlaps = GitHubObservabilitySummaryService.BuildForkNetworkOverlaps(
            allForkSnapshots,
            watchedRepositoryNames);
        var latestForkCapturesByRepository = watches
            .Select(static watch => watch.RepositoryNameWithOwner)
            .Select(repositoryNameWithOwner => new {
                RepositoryNameWithOwner = repositoryNameWithOwner,
                CapturedAtUtc = forkSnapshotStore.GetLatestCaptureAtUtcByParentRepository(repositoryNameWithOwner)
            })
            .Where(static entry => entry.CapturedAtUtc.HasValue)
            .ToArray();
        var forkChanges = GitHubRepositoryForkHistoryAnalytics.BuildLatestChanges(
                allForkSnapshots.Where(snapshot => watchedRepositoryNames.Contains(snapshot.ParentRepositoryNameWithOwner)))
            .OrderByDescending(static change => GitHubObservabilitySummaryService.GetForkChangePriority(change))
            .ThenByDescending(static change => Math.Abs(change.ScoreDelta))
            .ThenByDescending(static change => change.Stars)
            .ThenByDescending(static change => change.Watchers)
            .ThenBy(static change => change.ParentRepositoryNameWithOwner, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static change => change.ForkRepositoryNameWithOwner, StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToArray();
        var stargazerAudienceOverlaps = GitHubObservabilitySummaryService.BuildStargazerAudienceOverlaps(
            allStargazerSnapshots,
            watchedRepositoryNames);
        var latestCaptureAtUtc = latestDeltas.Count == 0
            ? (DateTimeOffset?)null
            : latestDeltas.Max(static delta => delta.CurrentCapturedAtUtc);
        var latestForkCaptureAtUtc = latestForkCapturesByRepository.Length == 0
            ? (DateTimeOffset?)null
            : latestForkCapturesByRepository.Max(static entry => entry.CapturedAtUtc!.Value);
        var laggingForkRepositoryCount = latestCaptureAtUtc.HasValue
            ? latestForkCapturesByRepository.Count(entry => entry.CapturedAtUtc!.Value < latestCaptureAtUtc.Value)
            : 0;
        var latestStargazerCapturesByRepository = watches
            .Select(static watch => watch.RepositoryNameWithOwner)
            .Select(repositoryNameWithOwner => new {
                RepositoryNameWithOwner = repositoryNameWithOwner,
                CapturedAtUtc = stargazerSnapshotStore.GetLatestCaptureAtUtcByRepository(repositoryNameWithOwner)
            })
            .Where(static entry => entry.CapturedAtUtc.HasValue)
            .ToArray();
        var latestStargazerCaptureAtUtc = latestStargazerCapturesByRepository.Length == 0
            ? (DateTimeOffset?)null
            : latestStargazerCapturesByRepository.Max(static entry => entry.CapturedAtUtc!.Value);
        var laggingStargazerRepositoryCount = latestCaptureAtUtc.HasValue
            ? latestStargazerCapturesByRepository.Count(entry => entry.CapturedAtUtc!.Value < latestCaptureAtUtc.Value)
            : 0;

        return new GitHubObservabilitySummaryData(
            dbPath: dbPath,
            enabledWatchCount: watches.Count,
            snapshotRepositoryCount: latestDeltas.Count,
            comparableRepositoryCount: comparableDeltas.Length,
            totalStars: latestDeltas.Sum(static delta => delta.Stars),
            totalForks: latestDeltas.Sum(static delta => delta.Forks),
            totalWatchers: latestDeltas.Sum(static delta => delta.Watchers),
            positiveStarDelta: comparableDeltas.Where(static delta => delta.StarDelta > 0).Sum(static delta => delta.StarDelta),
            positiveForkDelta: comparableDeltas.Where(static delta => delta.ForkDelta > 0).Sum(static delta => delta.ForkDelta),
            positiveWatcherDelta: comparableDeltas.Where(static delta => delta.WatcherDelta > 0).Sum(static delta => delta.WatcherDelta),
            changedRepositoryCount: comparableDeltas.Count(static delta =>
                delta.StarDelta != 0 ||
                delta.ForkDelta != 0 ||
                delta.WatcherDelta != 0 ||
                delta.OpenIssueDelta != 0),
            latestCaptureAtUtc: latestCaptureAtUtc,
            repositories: repositories.ToArray(),
            correlations: correlations,
            starCorrelations: starCorrelations,
            forkNetworkOverlaps: forkNetworkOverlaps,
            observedForkOwnerCount: GitHubObservabilitySummaryService.CountDistinctForkOwners(
                allForkSnapshots,
                watchedRepositoryNames),
            forkChanges: forkChanges,
            latestForkCaptureAtUtc: latestForkCaptureAtUtc,
            forkSnapshotRepositoryCount: latestForkCapturesByRepository.Length,
            laggingForkRepositoryCount: laggingForkRepositoryCount,
            stargazerAudienceOverlaps: stargazerAudienceOverlaps,
            observedStargazerCount: GitHubObservabilitySummaryService.CountDistinctStargazers(
                allStargazerSnapshots,
                watchedRepositoryNames),
            latestStargazerCaptureAtUtc: latestStargazerCaptureAtUtc,
            stargazerSnapshotRepositoryCount: latestStargazerCapturesByRepository.Length,
            laggingStargazerRepositoryCount: laggingStargazerRepositoryCount);
    }
}
