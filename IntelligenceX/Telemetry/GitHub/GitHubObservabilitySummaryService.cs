using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IntelligenceX.Telemetry.Usage;

namespace IntelligenceX.Telemetry.GitHub;

#pragma warning disable CS1591

/// <summary>
/// Loads compact GitHub repository watch momentum data for UI and report surfaces.
/// </summary>
internal sealed class GitHubObservabilitySummaryService {
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
            return CreateMissingDatabaseSummary(dbPath);
        }

#if NETSTANDARD2_0
        return CreateMissingDatabaseSummary(dbPath);
#else
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
                BuildTrendPoints(GitHubRepositorySnapshotAnalytics.BuildDailyDeltas(snapshots))));
        }

        var comparableDeltas = latestDeltas
            .Where(static delta => delta.PreviousCapturedAtUtc.HasValue)
            .ToArray();

        var correlations = BuildCorrelations(repositories);
        var starCorrelations = BuildStarCorrelations(repositories);
        var allForkSnapshots = forkSnapshotStore.GetAll();
        var allStargazerSnapshots = stargazerSnapshotStore.GetAll();
        var watchedRepositoryNames = new HashSet<string>(watches.Select(static watch => watch.RepositoryNameWithOwner), StringComparer.OrdinalIgnoreCase);
        var forkNetworkOverlaps = BuildForkNetworkOverlaps(allForkSnapshots, watchedRepositoryNames);
        var latestForkSnapshots = BuildLatestForkSnapshots(allForkSnapshots, watchedRepositoryNames);
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
            .OrderByDescending(static change => GetForkChangePriority(change))
            .ThenByDescending(static change => Math.Abs(change.ScoreDelta))
            .ThenByDescending(static change => change.Stars)
            .ThenByDescending(static change => change.Watchers)
            .ThenBy(static change => change.ParentRepositoryNameWithOwner, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static change => change.ForkRepositoryNameWithOwner, StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToArray();
        var stargazerAudienceOverlaps = BuildStargazerAudienceOverlaps(allStargazerSnapshots, watchedRepositoryNames);
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
            repositories: repositories,
            correlations: correlations,
            starCorrelations: starCorrelations,
            forkNetworkOverlaps: forkNetworkOverlaps,
            observedForkOwnerCount: CountDistinctForkOwners(allForkSnapshots, watchedRepositoryNames),
            forkChanges: forkChanges,
            latestForkCaptureAtUtc: latestForkCaptureAtUtc,
            forkSnapshotRepositoryCount: latestForkCapturesByRepository.Length,
            laggingForkRepositoryCount: laggingForkRepositoryCount,
            stargazerAudienceOverlaps: stargazerAudienceOverlaps,
            observedStargazerCount: CountDistinctStargazers(allStargazerSnapshots, watchedRepositoryNames),
            latestStargazerCaptureAtUtc: latestStargazerCaptureAtUtc,
            stargazerSnapshotRepositoryCount: latestStargazerCapturesByRepository.Length,
            laggingStargazerRepositoryCount: laggingStargazerRepositoryCount);
#endif
    }

    public static IReadOnlyList<GitHubObservedCorrelationData> BuildCorrelations(
        IReadOnlyList<GitHubObservedRepositoryTrendData>? repositories) {
        if (repositories is null || repositories.Count < 2) {
            return Array.Empty<GitHubObservedCorrelationData>();
        }

        var comparableRepositories = repositories
            .Where(static repository => repository.PreviousCapturedAtUtc.HasValue)
            .ToArray();
        if (comparableRepositories.Length < 2) {
            return Array.Empty<GitHubObservedCorrelationData>();
        }

        var correlations = new List<GitHubObservedCorrelationData>();
        for (var i = 0; i < comparableRepositories.Length - 1; i++) {
            for (var j = i + 1; j < comparableRepositories.Length; j++) {
                var correlation = TryBuildCorrelation(comparableRepositories[i], comparableRepositories[j]);
                if (correlation is not null) {
                    correlations.Add(correlation);
                }
            }
        }

        return correlations
            .OrderByDescending(static correlation => Math.Abs(correlation.Correlation))
            .ThenByDescending(static correlation => correlation.OverlapDays)
            .ThenBy(static correlation => correlation.RepositoryANameWithOwner, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static correlation => correlation.RepositoryBNameWithOwner, StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToArray();
    }

    public static IReadOnlyList<GitHubObservedStarCorrelationData> BuildStarCorrelations(
        IReadOnlyList<GitHubObservedRepositoryTrendData>? repositories) {
        if (repositories is null || repositories.Count < 2) {
            return Array.Empty<GitHubObservedStarCorrelationData>();
        }

        var comparableRepositories = repositories
            .Where(static repository => repository.PreviousCapturedAtUtc.HasValue)
            .ToArray();
        if (comparableRepositories.Length < 2) {
            return Array.Empty<GitHubObservedStarCorrelationData>();
        }

        var correlations = new List<GitHubObservedStarCorrelationData>();
        for (var i = 0; i < comparableRepositories.Length - 1; i++) {
            for (var j = i + 1; j < comparableRepositories.Length; j++) {
                var correlation = TryBuildStarCorrelation(comparableRepositories[i], comparableRepositories[j]);
                if (correlation is not null) {
                    correlations.Add(correlation);
                }
            }
        }

        return correlations
            .OrderByDescending(static correlation => Math.Abs(correlation.Correlation))
            .ThenByDescending(static correlation => correlation.OverlapDays)
            .ThenBy(static correlation => correlation.RepositoryANameWithOwner, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static correlation => correlation.RepositoryBNameWithOwner, StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToArray();
    }

    public static IReadOnlyList<GitHubObservedForkNetworkOverlapData> BuildForkNetworkOverlaps(
        IReadOnlyList<GitHubRepositoryForkSnapshotRecord>? snapshots,
        IReadOnlyCollection<string>? parentRepositories = null) {
        if (snapshots is null || snapshots.Count == 0) {
            return Array.Empty<GitHubObservedForkNetworkOverlapData>();
        }

        var parentFilter = parentRepositories is null || parentRepositories.Count == 0
            ? null
            : new HashSet<string>(parentRepositories, StringComparer.OrdinalIgnoreCase);

        var latestSnapshots = snapshots
            .Where(snapshot => parentFilter is null || parentFilter.Contains(snapshot.ParentRepositoryNameWithOwner))
            .GroupBy(static snapshot => snapshot.ParentRepositoryNameWithOwner + "|" + snapshot.ForkRepositoryNameWithOwner, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group
                .OrderByDescending(static snapshot => snapshot.CapturedAtUtc)
                .ThenByDescending(static snapshot => snapshot.Id, StringComparer.OrdinalIgnoreCase)
                .First())
            .Where(static snapshot => !snapshot.IsArchived)
            .ToArray();
        if (latestSnapshots.Length < 2) {
            return Array.Empty<GitHubObservedForkNetworkOverlapData>();
        }

        var ownersByRepository = latestSnapshots
            .GroupBy(static snapshot => snapshot.ParentRepositoryNameWithOwner, StringComparer.OrdinalIgnoreCase)
            .Select(group => new {
                Repository = group.Key,
                Owners = group
                    .GroupBy(static snapshot => GitHubRepositoryIdentity.SplitNameWithOwner(snapshot.ForkRepositoryNameWithOwner).Owner, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        static ownerGroup => ownerGroup.Key,
                        static ownerGroup => ownerGroup
                            .OrderByDescending(static snapshot => snapshot.Score)
                            .ThenByDescending(static snapshot => snapshot.Stars)
                            .ThenByDescending(static snapshot => snapshot.Watchers)
                            .First(),
                        StringComparer.OrdinalIgnoreCase)
            })
            .Where(static entry => entry.Owners.Count > 0)
            .OrderBy(static entry => entry.Repository, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (ownersByRepository.Length < 2) {
            return Array.Empty<GitHubObservedForkNetworkOverlapData>();
        }

        var overlaps = new List<GitHubObservedForkNetworkOverlapData>();
        for (var i = 0; i < ownersByRepository.Length - 1; i++) {
            for (var j = i + 1; j < ownersByRepository.Length; j++) {
                var sharedOwners = ownersByRepository[i].Owners.Keys
                    .Intersect(ownersByRepository[j].Owners.Keys, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(static owner => owner, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (sharedOwners.Length == 0) {
                    continue;
                }

                var sampledOwners = sharedOwners
                    .Select(owner => new {
                        Owner = owner,
                        CombinedScore = ownersByRepository[i].Owners[owner].Score + ownersByRepository[j].Owners[owner].Score
                    })
                    .OrderByDescending(static owner => owner.CombinedScore)
                    .ThenBy(static owner => owner.Owner, StringComparer.OrdinalIgnoreCase)
                    .Take(3)
                    .Select(static owner => owner.Owner)
                    .ToArray();
                var overlapRatio = Math.Min(1d, sharedOwners.Length / (double)Math.Max(1, Math.Min(ownersByRepository[i].Owners.Count, ownersByRepository[j].Owners.Count)));

                overlaps.Add(new GitHubObservedForkNetworkOverlapData(
                    ownersByRepository[i].Repository,
                    ownersByRepository[j].Repository,
                    sharedOwners.Length,
                    ownersByRepository[i].Owners.Count,
                    ownersByRepository[j].Owners.Count,
                    overlapRatio,
                    sampledOwners));
            }
        }

        return overlaps
            .OrderByDescending(static overlap => overlap.SharedForkOwnerCount)
            .ThenByDescending(static overlap => overlap.OverlapRatio)
            .ThenBy(static overlap => overlap.RepositoryANameWithOwner, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static overlap => overlap.RepositoryBNameWithOwner, StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToArray();
    }

    public static IReadOnlyList<GitHubObservedStargazerAudienceOverlapData> BuildStargazerAudienceOverlaps(
        IReadOnlyList<GitHubRepositoryStargazerSnapshotRecord>? snapshots,
        IReadOnlyCollection<string>? repositories = null) {
        var latestSnapshots = BuildLatestStargazerSnapshots(snapshots, repositories);
        if (latestSnapshots.Count < 2) {
            return Array.Empty<GitHubObservedStargazerAudienceOverlapData>();
        }

        var loginsByRepository = latestSnapshots
            .GroupBy(static snapshot => snapshot.RepositoryNameWithOwner, StringComparer.OrdinalIgnoreCase)
            .Select(group => new {
                Repository = group.Key,
                Stargazers = group
                    .GroupBy(static snapshot => snapshot.StargazerLogin, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        static loginGroup => loginGroup.Key,
                        static loginGroup => loginGroup
                            .OrderByDescending(static snapshot => snapshot.StarredAtUtc ?? DateTimeOffset.MinValue)
                            .ThenByDescending(static snapshot => snapshot.CapturedAtUtc)
                            .First(),
                        StringComparer.OrdinalIgnoreCase)
            })
            .Where(static entry => entry.Stargazers.Count > 0)
            .OrderBy(static entry => entry.Repository, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (loginsByRepository.Length < 2) {
            return Array.Empty<GitHubObservedStargazerAudienceOverlapData>();
        }

        var overlaps = new List<GitHubObservedStargazerAudienceOverlapData>();
        for (var i = 0; i < loginsByRepository.Length - 1; i++) {
            for (var j = i + 1; j < loginsByRepository.Length; j++) {
                var sharedLogins = loginsByRepository[i].Stargazers.Keys
                    .Intersect(loginsByRepository[j].Stargazers.Keys, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(static login => login, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (sharedLogins.Length == 0) {
                    continue;
                }

                var sampledLogins = sharedLogins
                    .Select(login => new {
                        Login = login,
                        LatestSharedStarredAtUtc = MaxNullable(
                            loginsByRepository[i].Stargazers[login].StarredAtUtc,
                            loginsByRepository[j].Stargazers[login].StarredAtUtc)
                    })
                    .OrderByDescending(static login => login.LatestSharedStarredAtUtc ?? DateTimeOffset.MinValue)
                    .ThenBy(static login => login.Login, StringComparer.OrdinalIgnoreCase)
                    .Take(3)
                    .Select(static login => login.Login)
                    .ToArray();
                var overlapRatio = Math.Min(1d, sharedLogins.Length / (double)Math.Max(1, Math.Min(loginsByRepository[i].Stargazers.Count, loginsByRepository[j].Stargazers.Count)));

                overlaps.Add(new GitHubObservedStargazerAudienceOverlapData(
                    loginsByRepository[i].Repository,
                    loginsByRepository[j].Repository,
                    sharedLogins.Length,
                    loginsByRepository[i].Stargazers.Count,
                    loginsByRepository[j].Stargazers.Count,
                    overlapRatio,
                    sampledLogins));
            }
        }

        return overlaps
            .OrderByDescending(static overlap => overlap.SharedStargazerCount)
            .ThenByDescending(static overlap => overlap.OverlapRatio)
            .ThenBy(static overlap => overlap.RepositoryANameWithOwner, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static overlap => overlap.RepositoryBNameWithOwner, StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToArray();
    }

    private static GitHubObservabilitySummaryData CreateMissingDatabaseSummary(string dbPath) {
        return new GitHubObservabilitySummaryData(
            dbPath,
            enabledWatchCount: 0,
            snapshotRepositoryCount: 0,
            comparableRepositoryCount: 0,
            totalStars: 0,
            totalForks: 0,
            totalWatchers: 0,
            positiveStarDelta: 0,
            positiveForkDelta: 0,
            positiveWatcherDelta: 0,
            changedRepositoryCount: 0,
            latestCaptureAtUtc: null,
            repositories: Array.Empty<GitHubObservedRepositoryTrendData>(),
            correlations: Array.Empty<GitHubObservedCorrelationData>(),
            starCorrelations: Array.Empty<GitHubObservedStarCorrelationData>(),
            forkNetworkOverlaps: Array.Empty<GitHubObservedForkNetworkOverlapData>(),
            observedForkOwnerCount: 0,
            stargazerAudienceOverlaps: Array.Empty<GitHubObservedStargazerAudienceOverlapData>(),
            observedStargazerCount: 0);
    }

    private static int CountDistinctForkOwners(
        IReadOnlyList<GitHubRepositoryForkSnapshotRecord>? snapshots,
        IReadOnlyCollection<string>? parentRepositories = null) {
        if (snapshots is null || snapshots.Count == 0) {
            return 0;
        }

        var parentFilter = parentRepositories is null || parentRepositories.Count == 0
            ? null
            : new HashSet<string>(parentRepositories, StringComparer.OrdinalIgnoreCase);
        return snapshots
            .Where(snapshot => parentFilter is null || parentFilter.Contains(snapshot.ParentRepositoryNameWithOwner))
            .Where(static snapshot => !snapshot.IsArchived)
            .Select(static snapshot => GitHubRepositoryIdentity.SplitNameWithOwner(snapshot.ForkRepositoryNameWithOwner).Owner)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
    }

    private static int CountDistinctStargazers(
        IReadOnlyList<GitHubRepositoryStargazerSnapshotRecord>? snapshots,
        IReadOnlyCollection<string>? repositories = null) {
        return BuildLatestStargazerSnapshots(snapshots, repositories)
            .Select(static snapshot => snapshot.StargazerLogin)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
    }

    private static IReadOnlyList<GitHubRepositoryForkSnapshotRecord> BuildLatestForkSnapshots(
        IReadOnlyList<GitHubRepositoryForkSnapshotRecord>? snapshots,
        IReadOnlyCollection<string>? repositories = null) {
        if (snapshots is null || snapshots.Count == 0) {
            return Array.Empty<GitHubRepositoryForkSnapshotRecord>();
        }

        var repositoryFilter = repositories is null || repositories.Count == 0
            ? null
            : new HashSet<string>(repositories, StringComparer.OrdinalIgnoreCase);
        return snapshots
            .Where(snapshot => repositoryFilter is null || repositoryFilter.Contains(snapshot.ParentRepositoryNameWithOwner))
            .GroupBy(static snapshot => snapshot.ParentRepositoryNameWithOwner + "|" + snapshot.ForkRepositoryNameWithOwner, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group
                .OrderByDescending(static snapshot => snapshot.CapturedAtUtc)
                .ThenByDescending(static snapshot => snapshot.Id, StringComparer.OrdinalIgnoreCase)
                .First())
            .ToArray();
    }

    private static IReadOnlyList<GitHubRepositoryStargazerSnapshotRecord> BuildLatestStargazerSnapshots(
        IReadOnlyList<GitHubRepositoryStargazerSnapshotRecord>? snapshots,
        IReadOnlyCollection<string>? repositories = null) {
        if (snapshots is null || snapshots.Count == 0) {
            return Array.Empty<GitHubRepositoryStargazerSnapshotRecord>();
        }

        var repositoryFilter = repositories is null || repositories.Count == 0
            ? null
            : new HashSet<string>(repositories, StringComparer.OrdinalIgnoreCase);
        return snapshots
            .Where(snapshot => repositoryFilter is null || repositoryFilter.Contains(snapshot.RepositoryNameWithOwner))
            .GroupBy(static snapshot => snapshot.RepositoryNameWithOwner + "|" + snapshot.StargazerLogin, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group
                .OrderByDescending(static snapshot => snapshot.CapturedAtUtc)
                .ThenByDescending(static snapshot => snapshot.Id, StringComparer.OrdinalIgnoreCase)
                .First())
            .ToArray();
    }

    private static IReadOnlyList<GitHubObservedTrendPointData> BuildTrendPoints(IReadOnlyList<GitHubRepositorySnapshotDelta> dailyDeltas) {
        const int pointCount = 7;
        var selected = dailyDeltas
            .Where(static delta => delta.PreviousCapturedAtUtc.HasValue)
            .OrderByDescending(static delta => delta.CurrentCapturedAtUtc)
            .Take(pointCount)
            .OrderBy(static delta => delta.CurrentCapturedAtUtc)
            .ToList();

        var points = new List<GitHubObservedTrendPointData>(pointCount);
        foreach (var delta in selected) {
            var score = ComputeTrendScore(delta.StarDelta, delta.ForkDelta, delta.WatcherDelta, delta.OpenIssueDelta);
            points.Add(new GitHubObservedTrendPointData(
                delta.CurrentCapturedAtUtc.UtcDateTime.Date,
                score,
                delta.StarDelta,
                delta.ForkDelta,
                delta.WatcherDelta));
        }

        while (points.Count < pointCount) {
            points.Insert(0, GitHubObservedTrendPointData.Empty);
        }

        return points;
    }

    private static double GetMomentumScore(GitHubObservedRepositoryTrendData repository) {
        if (!repository.PreviousCapturedAtUtc.HasValue) {
            return 0d;
        }

        return ComputeTrendScore(repository.StarDelta, repository.ForkDelta, repository.WatcherDelta, repository.OpenIssueDelta);
    }

    private static int GetForkChangePriority(GitHubRepositoryForkChange change) {
        if (change is null) {
            return 0;
        }

        return change.Status switch {
            "new" => 5,
            "rising" => 4,
            "steady" => 3,
            "cooling" => 2,
            "archived" => 1,
            _ => 0
        };
    }

    private static double ComputeTrendScore(int starDelta, int forkDelta, int watcherDelta, int openIssueDelta) {
        return (starDelta * 6d)
               + (forkDelta * 8d)
               + (watcherDelta * 3d)
               - Math.Max(0, openIssueDelta);
    }

    private static GitHubObservedCorrelationData? TryBuildCorrelation(
        GitHubObservedRepositoryTrendData repositoryA,
        GitHubObservedRepositoryTrendData repositoryB) {
        var left = repositoryA.TrendPoints
            .Where(static point => point.DayUtc != default)
            .ToDictionary(static point => point.DayUtc.Date, static point => point);
        var right = repositoryB.TrendPoints
            .Where(static point => point.DayUtc != default)
            .ToDictionary(static point => point.DayUtc.Date, static point => point);
        if (left.Count < 4 || right.Count < 4) {
            return null;
        }

        var overlapDays = left.Keys
            .Intersect(right.Keys)
            .OrderBy(static day => day)
            .ToArray();
        if (overlapDays.Length < 4) {
            return null;
        }

        var valuesA = overlapDays
            .Select(day => left[day].Score)
            .ToArray();
        var valuesB = overlapDays
            .Select(day => right[day].Score)
            .ToArray();
        var correlation = ComputePearson(valuesA, valuesB);
        if (double.IsNaN(correlation) || double.IsInfinity(correlation) || Math.Abs(correlation) < 0.45d) {
            return null;
        }

        var sharedUpDays = overlapDays.Count(day => left[day].Score > 0d && right[day].Score > 0d);
        var sharedDownDays = overlapDays.Count(day => left[day].Score < 0d && right[day].Score < 0d);
        var opposingDays = overlapDays.Count(day =>
            (left[day].Score > 0d && right[day].Score < 0d) ||
            (left[day].Score < 0d && right[day].Score > 0d));

        return new GitHubObservedCorrelationData(
            repositoryA.RepositoryNameWithOwner,
            repositoryB.RepositoryNameWithOwner,
            correlation,
            overlapDays.Length,
            sharedUpDays,
            sharedDownDays,
            opposingDays);
    }

    private static GitHubObservedStarCorrelationData? TryBuildStarCorrelation(
        GitHubObservedRepositoryTrendData repositoryA,
        GitHubObservedRepositoryTrendData repositoryB) {
        var left = repositoryA.TrendPoints
            .Where(static point => point.DayUtc != default)
            .ToDictionary(static point => point.DayUtc.Date, static point => point.StarDelta);
        var right = repositoryB.TrendPoints
            .Where(static point => point.DayUtc != default)
            .ToDictionary(static point => point.DayUtc.Date, static point => point.StarDelta);
        if (left.Count < 4 || right.Count < 4) {
            return null;
        }

        var overlapDays = left.Keys
            .Intersect(right.Keys)
            .OrderBy(static day => day)
            .ToArray();
        if (overlapDays.Length < 4) {
            return null;
        }

        var valuesA = overlapDays
            .Select(day => (double)left[day])
            .ToArray();
        var valuesB = overlapDays
            .Select(day => (double)right[day])
            .ToArray();
        var correlation = ComputePearson(valuesA, valuesB);
        if (double.IsNaN(correlation) || double.IsInfinity(correlation) || Math.Abs(correlation) < 0.45d) {
            return null;
        }

        var sharedGainDays = overlapDays.Count(day => left[day] > 0 && right[day] > 0);
        var sharedDropDays = overlapDays.Count(day => left[day] < 0 && right[day] < 0);
        var opposingDays = overlapDays.Count(day =>
            (left[day] > 0 && right[day] < 0) ||
            (left[day] < 0 && right[day] > 0));

        return new GitHubObservedStarCorrelationData(
            repositoryA.RepositoryNameWithOwner,
            repositoryB.RepositoryNameWithOwner,
            correlation,
            overlapDays.Length,
            sharedGainDays,
            sharedDropDays,
            opposingDays,
            overlapDays.Sum(day => left[day]),
            overlapDays.Sum(day => right[day]));
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
            var centeredLeft = left[i] - meanLeft;
            var centeredRight = right[i] - meanRight;
            covariance += centeredLeft * centeredRight;
            varianceLeft += centeredLeft * centeredLeft;
            varianceRight += centeredRight * centeredRight;
        }

        if (varianceLeft <= 0d || varianceRight <= 0d) {
            return double.NaN;
        }

        return covariance / Math.Sqrt(varianceLeft * varianceRight);
    }

    private static DateTimeOffset? MaxNullable(DateTimeOffset? left, DateTimeOffset? right) {
        if (!left.HasValue) {
            return right;
        }
        if (!right.HasValue) {
            return left;
        }

        return left.Value >= right.Value ? left : right;
    }
}

/// <summary>
/// Compact GitHub watch summary consumed by tray and report view models.
/// </summary>
internal sealed class GitHubObservabilitySummaryData {
    public static GitHubObservabilitySummaryData Empty { get; } = new(
        dbPath: null,
        enabledWatchCount: 0,
        snapshotRepositoryCount: 0,
        comparableRepositoryCount: 0,
        totalStars: 0,
        totalForks: 0,
        totalWatchers: 0,
        positiveStarDelta: 0,
        positiveForkDelta: 0,
        positiveWatcherDelta: 0,
        changedRepositoryCount: 0,
        latestCaptureAtUtc: null,
        repositories: Array.Empty<GitHubObservedRepositoryTrendData>(),
        correlations: Array.Empty<GitHubObservedCorrelationData>(),
        starCorrelations: Array.Empty<GitHubObservedStarCorrelationData>(),
        forkChanges: Array.Empty<GitHubRepositoryForkChange>());

    public GitHubObservabilitySummaryData(
        string? dbPath,
        int enabledWatchCount,
        int snapshotRepositoryCount,
        int comparableRepositoryCount,
        int totalStars,
        int totalForks,
        int totalWatchers,
        int positiveStarDelta,
        int positiveForkDelta,
        int positiveWatcherDelta,
        int changedRepositoryCount,
        DateTimeOffset? latestCaptureAtUtc,
        IReadOnlyList<GitHubObservedRepositoryTrendData> repositories,
        IReadOnlyList<GitHubObservedCorrelationData> correlations,
        IReadOnlyList<GitHubObservedStarCorrelationData>? starCorrelations = null,
        IReadOnlyList<GitHubObservedForkNetworkOverlapData>? forkNetworkOverlaps = null,
        int observedForkOwnerCount = 0,
        IReadOnlyList<GitHubRepositoryForkChange>? forkChanges = null,
        DateTimeOffset? latestForkCaptureAtUtc = null,
        int forkSnapshotRepositoryCount = 0,
        int laggingForkRepositoryCount = 0,
        IReadOnlyList<GitHubObservedStargazerAudienceOverlapData>? stargazerAudienceOverlaps = null,
        int observedStargazerCount = 0,
        DateTimeOffset? latestStargazerCaptureAtUtc = null,
        int stargazerSnapshotRepositoryCount = 0,
        int laggingStargazerRepositoryCount = 0) {
        DatabasePath = dbPath;
        EnabledWatchCount = Math.Max(0, enabledWatchCount);
        SnapshotRepositoryCount = Math.Max(0, snapshotRepositoryCount);
        ComparableRepositoryCount = Math.Max(0, comparableRepositoryCount);
        TotalStars = Math.Max(0, totalStars);
        TotalForks = Math.Max(0, totalForks);
        TotalWatchers = Math.Max(0, totalWatchers);
        PositiveStarDelta = Math.Max(0, positiveStarDelta);
        PositiveForkDelta = Math.Max(0, positiveForkDelta);
        PositiveWatcherDelta = Math.Max(0, positiveWatcherDelta);
        ChangedRepositoryCount = Math.Max(0, changedRepositoryCount);
        LatestCaptureAtUtc = latestCaptureAtUtc?.ToUniversalTime();
        Repositories = repositories ?? Array.Empty<GitHubObservedRepositoryTrendData>();
        Correlations = correlations ?? Array.Empty<GitHubObservedCorrelationData>();
        StarCorrelations = starCorrelations ?? Array.Empty<GitHubObservedStarCorrelationData>();
        ForkNetworkOverlaps = forkNetworkOverlaps ?? Array.Empty<GitHubObservedForkNetworkOverlapData>();
        ObservedForkOwnerCount = Math.Max(0, observedForkOwnerCount);
        ForkChanges = forkChanges ?? Array.Empty<GitHubRepositoryForkChange>();
        LatestForkCaptureAtUtc = latestForkCaptureAtUtc?.ToUniversalTime();
        ForkSnapshotRepositoryCount = Math.Max(0, forkSnapshotRepositoryCount);
        LaggingForkRepositoryCount = Math.Max(0, laggingForkRepositoryCount);
        StargazerAudienceOverlaps = stargazerAudienceOverlaps ?? Array.Empty<GitHubObservedStargazerAudienceOverlapData>();
        ObservedStargazerCount = Math.Max(0, observedStargazerCount);
        LatestStargazerCaptureAtUtc = latestStargazerCaptureAtUtc?.ToUniversalTime();
        StargazerSnapshotRepositoryCount = Math.Max(0, stargazerSnapshotRepositoryCount);
        LaggingStargazerRepositoryCount = Math.Max(0, laggingStargazerRepositoryCount);
    }

    public string? DatabasePath { get; }
    public int EnabledWatchCount { get; }
    public int SnapshotRepositoryCount { get; }
    public int ComparableRepositoryCount { get; }
    public int TotalStars { get; }
    public int TotalForks { get; }
    public int TotalWatchers { get; }
    public int PositiveStarDelta { get; }
    public int PositiveForkDelta { get; }
    public int PositiveWatcherDelta { get; }
    public int ChangedRepositoryCount { get; }
    public DateTimeOffset? LatestCaptureAtUtc { get; }
    public IReadOnlyList<GitHubObservedRepositoryTrendData> Repositories { get; }
    public IReadOnlyList<GitHubObservedRepositoryTrendData> FeaturedRepositories => Repositories
        .OrderByDescending(static repository => repository.PreviousCapturedAtUtc.HasValue)
        .ThenByDescending(static repository => repository.PreviousCapturedAtUtc.HasValue ? ComputeRepositoryMomentumScore(repository) : 0d)
        .ThenByDescending(static repository => repository.Stars)
        .ThenByDescending(static repository => repository.Forks)
        .ThenBy(static repository => repository.RepositoryNameWithOwner, StringComparer.OrdinalIgnoreCase)
        .Take(6)
        .ToArray();
    public IReadOnlyList<GitHubObservedCorrelationData> Correlations { get; }
    public IReadOnlyList<GitHubObservedStarCorrelationData> StarCorrelations { get; }
    public IReadOnlyList<GitHubObservedForkNetworkOverlapData> ForkNetworkOverlaps { get; }
    public int ObservedForkOwnerCount { get; }
    public IReadOnlyList<GitHubRepositoryForkChange> ForkChanges { get; }
    public DateTimeOffset? LatestForkCaptureAtUtc { get; }
    public int ForkSnapshotRepositoryCount { get; }
    public int LaggingForkRepositoryCount { get; }
    public int MissingForkSnapshotRepositoryCount => Math.Max(0, EnabledWatchCount - ForkSnapshotRepositoryCount);
    public bool HasAnyForkSnapshots => ForkSnapshotRepositoryCount > 0;
    public bool HasCompleteForkCoverage => EnabledWatchCount > 0 && MissingForkSnapshotRepositoryCount == 0;
    public bool HasFreshForkCoverage => HasCompleteForkCoverage && LaggingForkRepositoryCount == 0;
    public bool HasStaleForkCoverage => EnabledWatchCount > 0 && (MissingForkSnapshotRepositoryCount > 0 || LaggingForkRepositoryCount > 0);
    public IReadOnlyList<GitHubObservedStargazerAudienceOverlapData> StargazerAudienceOverlaps { get; }
    public int ObservedStargazerCount { get; }
    public DateTimeOffset? LatestStargazerCaptureAtUtc { get; }
    public int StargazerSnapshotRepositoryCount { get; }
    public int LaggingStargazerRepositoryCount { get; }
    public int MissingStargazerSnapshotRepositoryCount => Math.Max(0, EnabledWatchCount - StargazerSnapshotRepositoryCount);
    public bool HasAnyStargazerSnapshots => StargazerSnapshotRepositoryCount > 0;
    public bool HasCompleteStargazerCoverage => EnabledWatchCount > 0 && MissingStargazerSnapshotRepositoryCount == 0;
    public bool HasFreshStargazerCoverage => HasCompleteStargazerCoverage && LaggingStargazerRepositoryCount == 0;
    public bool HasStaleStargazerCoverage => EnabledWatchCount > 0 && (MissingStargazerSnapshotRepositoryCount > 0 || LaggingStargazerRepositoryCount > 0);
    public GitHubObservedCorrelationData? StrongestPositiveCorrelation => Correlations
        .Where(static correlation => correlation.Correlation > 0d)
        .OrderByDescending(static correlation => correlation.Correlation)
        .ThenByDescending(static correlation => correlation.OverlapDays)
        .FirstOrDefault();
    public GitHubObservedCorrelationData? StrongestNegativeCorrelation => Correlations
        .Where(static correlation => correlation.Correlation < 0d)
        .OrderBy(static correlation => correlation.Correlation)
        .ThenByDescending(static correlation => correlation.OverlapDays)
        .FirstOrDefault();
    public GitHubObservedStarCorrelationData? StrongestPositiveStarCorrelation => StarCorrelations
        .Where(static correlation => correlation.Correlation > 0d)
        .OrderByDescending(static correlation => correlation.Correlation)
        .ThenByDescending(static correlation => correlation.OverlapDays)
        .FirstOrDefault();
    public GitHubObservedStarCorrelationData? StrongestNegativeStarCorrelation => StarCorrelations
        .Where(static correlation => correlation.Correlation < 0d)
        .OrderBy(static correlation => correlation.Correlation)
        .ThenByDescending(static correlation => correlation.OverlapDays)
        .FirstOrDefault();
    public GitHubObservedForkNetworkOverlapData? StrongestForkNetworkOverlap => ForkNetworkOverlaps
        .OrderByDescending(static overlap => overlap.SharedForkOwnerCount)
        .ThenByDescending(static overlap => overlap.OverlapRatio)
        .ThenBy(static overlap => overlap.RepositoryANameWithOwner, StringComparer.OrdinalIgnoreCase)
        .FirstOrDefault();
    public GitHubRepositoryForkChange? StrongestForkChange => ForkChanges
        .OrderByDescending(static change => change.Status switch {
            "new" => 5,
            "rising" => 4,
            "steady" => 3,
            "cooling" => 2,
            "archived" => 1,
            _ => 0
        })
        .ThenByDescending(static change => Math.Abs(change.ScoreDelta))
        .ThenByDescending(static change => change.Stars)
        .ThenByDescending(static change => change.Watchers)
        .ThenBy(static change => change.ParentRepositoryNameWithOwner, StringComparer.OrdinalIgnoreCase)
        .ThenBy(static change => change.ForkRepositoryNameWithOwner, StringComparer.OrdinalIgnoreCase)
        .FirstOrDefault();
    public GitHubObservedStargazerAudienceOverlapData? StrongestStargazerAudienceOverlap => StargazerAudienceOverlaps
        .OrderByDescending(static overlap => overlap.SharedStargazerCount)
        .ThenByDescending(static overlap => overlap.OverlapRatio)
        .ThenBy(static overlap => overlap.RepositoryANameWithOwner, StringComparer.OrdinalIgnoreCase)
        .FirstOrDefault();

    private static double ComputeRepositoryMomentumScore(GitHubObservedRepositoryTrendData repository) {
        if (!repository.PreviousCapturedAtUtc.HasValue) {
            return 0d;
        }

        return Math.Abs(repository.StarDelta) * 5d
               + Math.Abs(repository.ForkDelta) * 4d
               + Math.Abs(repository.WatcherDelta) * 3d
               + Math.Abs(repository.OpenIssueDelta);
    }
}

/// <summary>
/// Single watched repository trend row.
/// </summary>
internal sealed class GitHubObservedRepositoryTrendData {
    public GitHubObservedRepositoryTrendData(
        string repositoryNameWithOwner,
        int stars,
        int forks,
        int watchers,
        int openIssues,
        int starDelta,
        int forkDelta,
        int watcherDelta,
        int openIssueDelta,
        DateTimeOffset currentCapturedAtUtc,
        DateTimeOffset? previousCapturedAtUtc,
        IReadOnlyList<GitHubObservedTrendPointData> trendPoints) {
        RepositoryNameWithOwner = repositoryNameWithOwner ?? throw new ArgumentNullException(nameof(repositoryNameWithOwner));
        Stars = Math.Max(0, stars);
        Forks = Math.Max(0, forks);
        Watchers = Math.Max(0, watchers);
        OpenIssues = Math.Max(0, openIssues);
        StarDelta = starDelta;
        ForkDelta = forkDelta;
        WatcherDelta = watcherDelta;
        OpenIssueDelta = openIssueDelta;
        CurrentCapturedAtUtc = currentCapturedAtUtc.ToUniversalTime();
        PreviousCapturedAtUtc = previousCapturedAtUtc?.ToUniversalTime();
        TrendPoints = trendPoints ?? Array.Empty<GitHubObservedTrendPointData>();
    }

    public string RepositoryNameWithOwner { get; }
    public int Stars { get; }
    public int Forks { get; }
    public int Watchers { get; }
    public int OpenIssues { get; }
    public int StarDelta { get; }
    public int ForkDelta { get; }
    public int WatcherDelta { get; }
    public int OpenIssueDelta { get; }
    public DateTimeOffset CurrentCapturedAtUtc { get; }
    public DateTimeOffset? PreviousCapturedAtUtc { get; }
    public IReadOnlyList<GitHubObservedTrendPointData> TrendPoints { get; }
}

/// <summary>
/// Single recent trend point derived from daily snapshot deltas.
/// </summary>
internal sealed class GitHubObservedTrendPointData {
    public static GitHubObservedTrendPointData Empty { get; } = new(default, 0d, 0, 0, 0);

    public GitHubObservedTrendPointData(
        DateTime dayUtc,
        double score,
        int starDelta,
        int forkDelta,
        int watcherDelta) {
        DayUtc = dayUtc;
        Score = score;
        StarDelta = starDelta;
        ForkDelta = forkDelta;
        WatcherDelta = watcherDelta;
    }

    public DateTime DayUtc { get; }
    public double Score { get; }
    public int StarDelta { get; }
    public int ForkDelta { get; }
    public int WatcherDelta { get; }
}

/// <summary>
/// Pairwise repository movement correlation derived from watched daily trend points.
/// </summary>
internal sealed class GitHubObservedCorrelationData {
    public GitHubObservedCorrelationData(
        string repositoryANameWithOwner,
        string repositoryBNameWithOwner,
        double correlation,
        int overlapDays,
        int sharedUpDays,
        int sharedDownDays,
        int opposingDays) {
        RepositoryANameWithOwner = repositoryANameWithOwner ?? throw new ArgumentNullException(nameof(repositoryANameWithOwner));
        RepositoryBNameWithOwner = repositoryBNameWithOwner ?? throw new ArgumentNullException(nameof(repositoryBNameWithOwner));
        Correlation = correlation;
        OverlapDays = Math.Max(0, overlapDays);
        SharedUpDays = Math.Max(0, sharedUpDays);
        SharedDownDays = Math.Max(0, sharedDownDays);
        OpposingDays = Math.Max(0, opposingDays);
    }

    public string RepositoryANameWithOwner { get; }
    public string RepositoryBNameWithOwner { get; }
    public double Correlation { get; }
    public int OverlapDays { get; }
    public int SharedUpDays { get; }
    public int SharedDownDays { get; }
    public int OpposingDays { get; }
}

/// <summary>
/// Pairwise star-delta correlation derived from watched daily star movement.
/// </summary>
internal sealed class GitHubObservedStarCorrelationData {
    public GitHubObservedStarCorrelationData(
        string repositoryANameWithOwner,
        string repositoryBNameWithOwner,
        double correlation,
        int overlapDays,
        int sharedGainDays,
        int sharedDropDays,
        int opposingDays,
        int repositoryARecentStarChange,
        int repositoryBRecentStarChange) {
        RepositoryANameWithOwner = repositoryANameWithOwner ?? throw new ArgumentNullException(nameof(repositoryANameWithOwner));
        RepositoryBNameWithOwner = repositoryBNameWithOwner ?? throw new ArgumentNullException(nameof(repositoryBNameWithOwner));
        Correlation = correlation;
        OverlapDays = Math.Max(0, overlapDays);
        SharedGainDays = Math.Max(0, sharedGainDays);
        SharedDropDays = Math.Max(0, sharedDropDays);
        OpposingDays = Math.Max(0, opposingDays);
        RepositoryARecentStarChange = repositoryARecentStarChange;
        RepositoryBRecentStarChange = repositoryBRecentStarChange;
    }

    public string RepositoryANameWithOwner { get; }
    public string RepositoryBNameWithOwner { get; }
    public double Correlation { get; }
    public int OverlapDays { get; }
    public int SharedGainDays { get; }
    public int SharedDropDays { get; }
    public int OpposingDays { get; }
    public int RepositoryARecentStarChange { get; }
    public int RepositoryBRecentStarChange { get; }
}

/// <summary>
/// Shared fork-owner overlap between two watched repositories.
/// </summary>
internal sealed class GitHubObservedForkNetworkOverlapData {
    public GitHubObservedForkNetworkOverlapData(
        string repositoryANameWithOwner,
        string repositoryBNameWithOwner,
        int sharedForkOwnerCount,
        int repositoryAForkOwnerCount,
        int repositoryBForkOwnerCount,
        double overlapRatio,
        IReadOnlyList<string>? sampleSharedForkOwners) {
        RepositoryANameWithOwner = repositoryANameWithOwner ?? throw new ArgumentNullException(nameof(repositoryANameWithOwner));
        RepositoryBNameWithOwner = repositoryBNameWithOwner ?? throw new ArgumentNullException(nameof(repositoryBNameWithOwner));
        SharedForkOwnerCount = Math.Max(0, sharedForkOwnerCount);
        RepositoryAForkOwnerCount = Math.Max(0, repositoryAForkOwnerCount);
        RepositoryBForkOwnerCount = Math.Max(0, repositoryBForkOwnerCount);
        OverlapRatio = Math.Max(0d, Math.Min(1d, overlapRatio));
        SampleSharedForkOwners = sampleSharedForkOwners ?? Array.Empty<string>();
    }

    public string RepositoryANameWithOwner { get; }
    public string RepositoryBNameWithOwner { get; }
    public int SharedForkOwnerCount { get; }
    public int RepositoryAForkOwnerCount { get; }
    public int RepositoryBForkOwnerCount { get; }
    public double OverlapRatio { get; }
    public IReadOnlyList<string> SampleSharedForkOwners { get; }
}

/// <summary>
/// Shared stargazer-audience overlap between two watched repositories.
/// </summary>
internal sealed class GitHubObservedStargazerAudienceOverlapData {
    public GitHubObservedStargazerAudienceOverlapData(
        string repositoryANameWithOwner,
        string repositoryBNameWithOwner,
        int sharedStargazerCount,
        int repositoryAStargazerCount,
        int repositoryBStargazerCount,
        double overlapRatio,
        IReadOnlyList<string>? sampleSharedStargazers) {
        RepositoryANameWithOwner = repositoryANameWithOwner ?? throw new ArgumentNullException(nameof(repositoryANameWithOwner));
        RepositoryBNameWithOwner = repositoryBNameWithOwner ?? throw new ArgumentNullException(nameof(repositoryBNameWithOwner));
        SharedStargazerCount = Math.Max(0, sharedStargazerCount);
        RepositoryAStargazerCount = Math.Max(0, repositoryAStargazerCount);
        RepositoryBStargazerCount = Math.Max(0, repositoryBStargazerCount);
        OverlapRatio = Math.Max(0d, Math.Min(1d, overlapRatio));
        SampleSharedStargazers = sampleSharedStargazers ?? Array.Empty<string>();
    }

    public string RepositoryANameWithOwner { get; }
    public string RepositoryBNameWithOwner { get; }
    public int SharedStargazerCount { get; }
    public int RepositoryAStargazerCount { get; }
    public int RepositoryBStargazerCount { get; }
    public double OverlapRatio { get; }
    public IReadOnlyList<string> SampleSharedStargazers { get; }
}
