using System;
using System.IO;
using IntelligenceX.Telemetry.Git;
using IntelligenceX.Telemetry.GitHub;

namespace IntelligenceX.Tests;

internal static partial class Program {
    private static void TestGitHubRepositoryWatchStoreLooksUpCanonicalRepository() {
        var store = new InMemoryGitHubRepositoryWatchStore();
        var watch = new GitHubRepositoryWatchRecord(
            GitHubRepositoryWatchRecord.CreateStableId("EvotecIT/IntelligenceX"),
            " EvotecIT / IntelligenceX ",
            new DateTimeOffset(2026, 3, 15, 9, 0, 0, TimeSpan.Zero)) {
            DisplayName = "IntelligenceX",
            Category = "core",
            Notes = "Main repo"
        };

        store.Upsert(watch);

        AssertEqual(true, store.TryGetByRepository("EvotecIT/IntelligenceX", out var resolved), "github watch canonical lookup");
        AssertEqual("EvotecIT/IntelligenceX", resolved.RepositoryNameWithOwner, "github watch canonical repository");
        AssertEqual("EvotecIT", resolved.Owner, "github watch owner");
        AssertEqual("IntelligenceX", resolved.Repository, "github watch repository");
    }

    private static void TestGitHubRepositorySnapshotAnalyticsBuildsDailyDeltasUsingLatestSnapshotPerDay() {
        var watchId = GitHubRepositoryWatchRecord.CreateStableId("EvotecIT/IntelligenceX");
        var deltas = GitHubRepositorySnapshotAnalytics.BuildDailyDeltas(new[] {
            new GitHubRepositorySnapshotRecord(
                GitHubRepositorySnapshotRecord.CreateStableId(watchId, new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero)),
                watchId,
                "EvotecIT/IntelligenceX",
                new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero),
                10,
                2,
                4,
                1),
            new GitHubRepositorySnapshotRecord(
                GitHubRepositorySnapshotRecord.CreateStableId(watchId, new DateTimeOffset(2026, 3, 10, 21, 0, 0, TimeSpan.Zero)),
                watchId,
                "EvotecIT/IntelligenceX",
                new DateTimeOffset(2026, 3, 10, 21, 0, 0, TimeSpan.Zero),
                11,
                2,
                5,
                1),
            new GitHubRepositorySnapshotRecord(
                GitHubRepositorySnapshotRecord.CreateStableId(watchId, new DateTimeOffset(2026, 3, 11, 8, 0, 0, TimeSpan.Zero)),
                watchId,
                "EvotecIT/IntelligenceX",
                new DateTimeOffset(2026, 3, 11, 8, 0, 0, TimeSpan.Zero),
                14,
                3,
                6,
                2),
            new GitHubRepositorySnapshotRecord(
                GitHubRepositorySnapshotRecord.CreateStableId(watchId, new DateTimeOffset(2026, 3, 12, 8, 0, 0, TimeSpan.Zero)),
                watchId,
                "EvotecIT/IntelligenceX",
                new DateTimeOffset(2026, 3, 12, 8, 0, 0, TimeSpan.Zero),
                13,
                3,
                7,
                1)
        });

        AssertEqual(3, deltas.Count, "github daily delta count");
        AssertEqual(11, deltas[0].Stars, "github daily delta first day uses latest same-day snapshot");
        AssertEqual(11, deltas[0].StarDelta, "github daily delta first day baseline");
        AssertEqual(3, deltas[1].StarDelta, "github daily delta second day stars");
        AssertEqual(1, deltas[1].ForkDelta, "github daily delta second day forks");
        AssertEqual(1, deltas[1].WatcherDelta, "github daily delta second day watchers");
        AssertEqual(-1, deltas[2].StarDelta, "github daily delta supports drops");
        AssertEqual(1, deltas[2].WatcherDelta, "github daily delta third day watchers");
        AssertEqual(-1, deltas[2].OpenIssueDelta, "github daily delta third day issues");
    }

    private static void TestGitHubObservabilitySummaryBuildsRepoMovementCorrelations() {
        var correlations = GitHubObservabilitySummaryService.BuildCorrelations(new[] {
            new GitHubObservedRepositoryTrendData(
                repositoryNameWithOwner: "EvotecIT/IntelligenceX",
                stars: 120,
                forks: 20,
                watchers: 12,
                openIssues: 3,
                starDelta: 5,
                forkDelta: 1,
                watcherDelta: 2,
                openIssueDelta: -1,
                currentCapturedAtUtc: new DateTimeOffset(2026, 03, 12, 10, 30, 0, TimeSpan.Zero),
                previousCapturedAtUtc: new DateTimeOffset(2026, 03, 11, 10, 30, 0, TimeSpan.Zero),
                trendPoints: new[] {
                    new GitHubObservedTrendPointData(new DateTime(2026, 03, 06), 2d, 1, 0, 0),
                    new GitHubObservedTrendPointData(new DateTime(2026, 03, 07), 4d, 1, 0, 1),
                    new GitHubObservedTrendPointData(new DateTime(2026, 03, 08), 6d, 1, 0, 1),
                    new GitHubObservedTrendPointData(new DateTime(2026, 03, 09), 2d, 0, 0, 0),
                    new GitHubObservedTrendPointData(new DateTime(2026, 03, 10), 7d, 1, 1, 0),
                    new GitHubObservedTrendPointData(new DateTime(2026, 03, 11), 5d, 1, 0, 0),
                    new GitHubObservedTrendPointData(new DateTime(2026, 03, 12), 8d, 1, 0, 1)
                }),
            new GitHubObservedRepositoryTrendData(
                repositoryNameWithOwner: "EvotecIT/PSWriteHTML",
                stars: 410,
                forks: 61,
                watchers: 20,
                openIssues: 4,
                starDelta: 4,
                forkDelta: 1,
                watcherDelta: 1,
                openIssueDelta: 0,
                currentCapturedAtUtc: new DateTimeOffset(2026, 03, 12, 10, 30, 0, TimeSpan.Zero),
                previousCapturedAtUtc: new DateTimeOffset(2026, 03, 11, 10, 30, 0, TimeSpan.Zero),
                trendPoints: new[] {
                    new GitHubObservedTrendPointData(new DateTime(2026, 03, 06), 3d, 1, 0, 0),
                    new GitHubObservedTrendPointData(new DateTime(2026, 03, 07), 5d, 1, 0, 1),
                    new GitHubObservedTrendPointData(new DateTime(2026, 03, 08), 7d, 1, 0, 1),
                    new GitHubObservedTrendPointData(new DateTime(2026, 03, 09), 3d, 0, 0, 0),
                    new GitHubObservedTrendPointData(new DateTime(2026, 03, 10), 8d, 1, 1, 0),
                    new GitHubObservedTrendPointData(new DateTime(2026, 03, 11), 6d, 1, 0, 0),
                    new GitHubObservedTrendPointData(new DateTime(2026, 03, 12), 9d, 1, 0, 1)
                }),
            new GitHubObservedRepositoryTrendData(
                repositoryNameWithOwner: "EvotecIT/GPOZaurr",
                stars: 533,
                forks: 72,
                watchers: 22,
                openIssues: 6,
                starDelta: -2,
                forkDelta: 0,
                watcherDelta: -1,
                openIssueDelta: 1,
                currentCapturedAtUtc: new DateTimeOffset(2026, 03, 12, 10, 30, 0, TimeSpan.Zero),
                previousCapturedAtUtc: new DateTimeOffset(2026, 03, 11, 10, 30, 0, TimeSpan.Zero),
                trendPoints: new[] {
                    new GitHubObservedTrendPointData(new DateTime(2026, 03, 06), -2d, -1, 0, 0),
                    new GitHubObservedTrendPointData(new DateTime(2026, 03, 07), -4d, -1, 0, -1),
                    new GitHubObservedTrendPointData(new DateTime(2026, 03, 08), -6d, -1, 0, -1),
                    new GitHubObservedTrendPointData(new DateTime(2026, 03, 09), -2d, 0, 0, 0),
                    new GitHubObservedTrendPointData(new DateTime(2026, 03, 10), -7d, -1, 0, 0),
                    new GitHubObservedTrendPointData(new DateTime(2026, 03, 11), -5d, -1, 0, 0),
                    new GitHubObservedTrendPointData(new DateTime(2026, 03, 12), -8d, -1, 0, -1)
                })
        });

        AssertEqual(3, correlations.Count, "github observability correlation count");
        AssertEqual(
            true,
            correlations.Any(static correlation =>
                string.Equals(correlation.RepositoryANameWithOwner, "EvotecIT/IntelligenceX", StringComparison.Ordinal) &&
                string.Equals(correlation.RepositoryBNameWithOwner, "EvotecIT/PSWriteHTML", StringComparison.Ordinal) &&
                correlation.Correlation > 0.95d),
            "github observability includes strong positive correlation");
        AssertEqual(
            true,
            correlations.Any(static correlation =>
                string.Equals(correlation.RepositoryANameWithOwner, "EvotecIT/IntelligenceX", StringComparison.Ordinal) &&
                string.Equals(correlation.RepositoryBNameWithOwner, "EvotecIT/GPOZaurr", StringComparison.Ordinal) &&
                correlation.Correlation < -0.95d),
            "github observability includes strong negative correlation");
    }

    private static void TestGitHubObservabilitySummaryBuildsSharedForkNetworkOverlaps() {
        var capturedAtUtc = new DateTimeOffset(2026, 03, 12, 10, 30, 0, TimeSpan.Zero);
        var overlaps = GitHubObservabilitySummaryService.BuildForkNetworkOverlaps(
            new[] {
                new GitHubRepositoryForkSnapshotRecord(
                    GitHubRepositoryForkSnapshotRecord.CreateStableId("EvotecIT/IntelligenceX", "alice/IntelligenceX", capturedAtUtc),
                    "EvotecIT/IntelligenceX",
                    "alice/IntelligenceX",
                    capturedAtUtc,
                    score: 82d,
                    tier: "rising",
                    stars: 12,
                    forks: 1,
                    watchers: 3,
                    openIssues: 0),
                new GitHubRepositoryForkSnapshotRecord(
                    GitHubRepositoryForkSnapshotRecord.CreateStableId("EvotecIT/IntelligenceX", "bob/IntelligenceX", capturedAtUtc),
                    "EvotecIT/IntelligenceX",
                    "bob/IntelligenceX",
                    capturedAtUtc,
                    score: 74d,
                    tier: "rising",
                    stars: 9,
                    forks: 1,
                    watchers: 2,
                    openIssues: 0),
                new GitHubRepositoryForkSnapshotRecord(
                    GitHubRepositoryForkSnapshotRecord.CreateStableId("EvotecIT/PSWriteHTML", "alice/PSWriteHTML", capturedAtUtc),
                    "EvotecIT/PSWriteHTML",
                    "alice/PSWriteHTML",
                    capturedAtUtc,
                    score: 78d,
                    tier: "rising",
                    stars: 10,
                    forks: 1,
                    watchers: 3,
                    openIssues: 0),
                new GitHubRepositoryForkSnapshotRecord(
                    GitHubRepositoryForkSnapshotRecord.CreateStableId("EvotecIT/PSWriteHTML", "bob/PSWriteHTML", capturedAtUtc),
                    "EvotecIT/PSWriteHTML",
                    "bob/PSWriteHTML",
                    capturedAtUtc,
                    score: 69d,
                    tier: "rising",
                    stars: 8,
                    forks: 1,
                    watchers: 2,
                    openIssues: 0),
                new GitHubRepositoryForkSnapshotRecord(
                    GitHubRepositoryForkSnapshotRecord.CreateStableId("EvotecIT/GPOZaurr", "carol/GPOZaurr", capturedAtUtc),
                    "EvotecIT/GPOZaurr",
                    "carol/GPOZaurr",
                    capturedAtUtc,
                    score: 55d,
                    tier: "steady",
                    stars: 4,
                    forks: 1,
                    watchers: 1,
                    openIssues: 0)
            },
            new[] { "EvotecIT/IntelligenceX", "EvotecIT/PSWriteHTML", "EvotecIT/GPOZaurr" });

        AssertEqual(1, overlaps.Count, "github fork network overlap count");
        AssertEqual("EvotecIT/IntelligenceX", overlaps[0].RepositoryANameWithOwner, "github fork network overlap repo a");
        AssertEqual("EvotecIT/PSWriteHTML", overlaps[0].RepositoryBNameWithOwner, "github fork network overlap repo b");
        AssertEqual(2, overlaps[0].SharedForkOwnerCount, "github fork network shared owner count");
        AssertEqual(true, overlaps[0].SampleSharedForkOwners.Contains("alice", StringComparer.OrdinalIgnoreCase), "github fork network sample contains alice");
        AssertEqual(true, overlaps[0].SampleSharedForkOwners.Contains("bob", StringComparer.OrdinalIgnoreCase), "github fork network sample contains bob");
    }

    private static void TestGitHubObservabilitySummaryBuildsSharedStargazerAudienceOverlaps() {
        var capturedAtUtc = new DateTimeOffset(2026, 03, 12, 10, 30, 0, TimeSpan.Zero);
        var overlaps = GitHubObservabilitySummaryService.BuildStargazerAudienceOverlaps(
            new[] {
                new GitHubRepositoryStargazerSnapshotRecord(
                    GitHubRepositoryStargazerSnapshotRecord.CreateStableId("EvotecIT/IntelligenceX", "alice", capturedAtUtc),
                    "EvotecIT/IntelligenceX",
                    "alice",
                    capturedAtUtc) {
                    StarredAtUtc = new DateTimeOffset(2026, 03, 10, 8, 0, 0, TimeSpan.Zero)
                },
                new GitHubRepositoryStargazerSnapshotRecord(
                    GitHubRepositoryStargazerSnapshotRecord.CreateStableId("EvotecIT/IntelligenceX", "bob", capturedAtUtc),
                    "EvotecIT/IntelligenceX",
                    "bob",
                    capturedAtUtc) {
                    StarredAtUtc = new DateTimeOffset(2026, 03, 11, 8, 0, 0, TimeSpan.Zero)
                },
                new GitHubRepositoryStargazerSnapshotRecord(
                    GitHubRepositoryStargazerSnapshotRecord.CreateStableId("EvotecIT/PSWriteHTML", "alice", capturedAtUtc),
                    "EvotecIT/PSWriteHTML",
                    "alice",
                    capturedAtUtc) {
                    StarredAtUtc = new DateTimeOffset(2026, 03, 09, 8, 0, 0, TimeSpan.Zero)
                },
                new GitHubRepositoryStargazerSnapshotRecord(
                    GitHubRepositoryStargazerSnapshotRecord.CreateStableId("EvotecIT/PSWriteHTML", "bob", capturedAtUtc),
                    "EvotecIT/PSWriteHTML",
                    "bob",
                    capturedAtUtc) {
                    StarredAtUtc = new DateTimeOffset(2026, 03, 12, 8, 0, 0, TimeSpan.Zero)
                },
                new GitHubRepositoryStargazerSnapshotRecord(
                    GitHubRepositoryStargazerSnapshotRecord.CreateStableId("EvotecIT/GPOZaurr", "carol", capturedAtUtc),
                    "EvotecIT/GPOZaurr",
                    "carol",
                    capturedAtUtc) {
                    StarredAtUtc = new DateTimeOffset(2026, 03, 08, 8, 0, 0, TimeSpan.Zero)
                }
            },
            new[] { "EvotecIT/IntelligenceX", "EvotecIT/PSWriteHTML", "EvotecIT/GPOZaurr" });

        AssertEqual(1, overlaps.Count, "github stargazer audience overlap count");
        AssertEqual("EvotecIT/IntelligenceX", overlaps[0].RepositoryANameWithOwner, "github stargazer audience repo a");
        AssertEqual("EvotecIT/PSWriteHTML", overlaps[0].RepositoryBNameWithOwner, "github stargazer audience repo b");
        AssertEqual(2, overlaps[0].SharedStargazerCount, "github stargazer shared count");
        AssertEqual(true, overlaps[0].SampleSharedStargazers.Contains("alice", StringComparer.OrdinalIgnoreCase), "github stargazer sample contains alice");
        AssertEqual(true, overlaps[0].SampleSharedStargazers.Contains("bob", StringComparer.OrdinalIgnoreCase), "github stargazer sample contains bob");
    }

    private static void TestGitHubObservabilitySummaryTracksStargazerCoverageStatus() {
        var summary = new GitHubObservabilitySummaryData(
            dbPath: @"C:\telemetry\usage.db",
            enabledWatchCount: 3,
            snapshotRepositoryCount: 3,
            comparableRepositoryCount: 3,
            totalStars: 120,
            totalForks: 20,
            totalWatchers: 12,
            positiveStarDelta: 2,
            positiveForkDelta: 1,
            positiveWatcherDelta: 1,
            changedRepositoryCount: 1,
            latestCaptureAtUtc: new DateTimeOffset(2026, 03, 12, 10, 30, 0, TimeSpan.Zero),
            repositories: Array.Empty<GitHubObservedRepositoryTrendData>(),
            correlations: Array.Empty<GitHubObservedCorrelationData>(),
            latestStargazerCaptureAtUtc: new DateTimeOffset(2026, 03, 11, 10, 30, 0, TimeSpan.Zero),
            stargazerSnapshotRepositoryCount: 2,
            laggingStargazerRepositoryCount: 1);

        AssertEqual(2, summary.StargazerSnapshotRepositoryCount, "github stargazer coverage captured repo count");
        AssertEqual(1, summary.MissingStargazerSnapshotRepositoryCount, "github stargazer coverage missing repo count");
        AssertEqual(1, summary.LaggingStargazerRepositoryCount, "github stargazer coverage lagging repo count");
        AssertEqual(true, summary.HasAnyStargazerSnapshots, "github stargazer coverage has snapshots");
        AssertEqual(false, summary.HasCompleteStargazerCoverage, "github stargazer coverage complete");
        AssertEqual(false, summary.HasFreshStargazerCoverage, "github stargazer coverage fresh");
        AssertEqual(true, summary.HasStaleStargazerCoverage, "github stargazer coverage stale");
    }

    private static void TestGitHubObservabilitySummaryTracksForkCoverageStatus() {
        var strongestForkChange = GitHubRepositoryForkHistoryAnalytics.CreateChange(
            new GitHubRepositoryForkSnapshotRecord(
                GitHubRepositoryForkSnapshotRecord.CreateStableId("EvotecIT/IntelligenceX", "alice/IntelligenceX", new DateTimeOffset(2026, 03, 10, 10, 30, 0, TimeSpan.Zero)),
                "EvotecIT/IntelligenceX",
                "alice/IntelligenceX",
                new DateTimeOffset(2026, 03, 10, 10, 30, 0, TimeSpan.Zero),
                score: 60d,
                tier: "medium",
                stars: 6,
                forks: 1,
                watchers: 2,
                openIssues: 0),
            new GitHubRepositoryForkSnapshotRecord(
                GitHubRepositoryForkSnapshotRecord.CreateStableId("EvotecIT/IntelligenceX", "alice/IntelligenceX", new DateTimeOffset(2026, 03, 11, 10, 30, 0, TimeSpan.Zero)),
                "EvotecIT/IntelligenceX",
                "alice/IntelligenceX",
                new DateTimeOffset(2026, 03, 11, 10, 30, 0, TimeSpan.Zero),
                score: 78d,
                tier: "high",
                stars: 10,
                forks: 1,
                watchers: 4,
                openIssues: 0));
        var summary = new GitHubObservabilitySummaryData(
            dbPath: @"C:\telemetry\usage.db",
            enabledWatchCount: 3,
            snapshotRepositoryCount: 3,
            comparableRepositoryCount: 3,
            totalStars: 120,
            totalForks: 20,
            totalWatchers: 12,
            positiveStarDelta: 2,
            positiveForkDelta: 1,
            positiveWatcherDelta: 1,
            changedRepositoryCount: 1,
            latestCaptureAtUtc: new DateTimeOffset(2026, 03, 12, 10, 30, 0, TimeSpan.Zero),
            repositories: Array.Empty<GitHubObservedRepositoryTrendData>(),
            correlations: Array.Empty<GitHubObservedCorrelationData>(),
            forkChanges: new[] { strongestForkChange },
            latestForkCaptureAtUtc: new DateTimeOffset(2026, 03, 11, 10, 30, 0, TimeSpan.Zero),
            forkSnapshotRepositoryCount: 2,
            laggingForkRepositoryCount: 1);

        AssertEqual(2, summary.ForkSnapshotRepositoryCount, "github fork coverage captured repo count");
        AssertEqual(1, summary.MissingForkSnapshotRepositoryCount, "github fork coverage missing repo count");
        AssertEqual(1, summary.LaggingForkRepositoryCount, "github fork coverage lagging repo count");
        AssertEqual(true, summary.HasAnyForkSnapshots, "github fork coverage has snapshots");
        AssertEqual(false, summary.HasCompleteForkCoverage, "github fork coverage complete");
        AssertEqual(false, summary.HasFreshForkCoverage, "github fork coverage fresh");
        AssertEqual(true, summary.HasStaleForkCoverage, "github fork coverage stale");
        AssertEqual("alice/IntelligenceX", summary.StrongestForkChange?.ForkRepositoryNameWithOwner, "github fork coverage strongest fork change");
    }

    private static void TestGitHubObservabilitySummaryBuildsStarCorrelations() {
        var correlations = GitHubObservabilitySummaryService.BuildStarCorrelations(new[] {
            new GitHubObservedRepositoryTrendData(
                repositoryNameWithOwner: "EvotecIT/IntelligenceX",
                stars: 120,
                forks: 20,
                watchers: 12,
                openIssues: 3,
                starDelta: 5,
                forkDelta: 1,
                watcherDelta: 2,
                openIssueDelta: -1,
                currentCapturedAtUtc: new DateTimeOffset(2026, 03, 12, 10, 30, 0, TimeSpan.Zero),
                previousCapturedAtUtc: new DateTimeOffset(2026, 03, 11, 10, 30, 0, TimeSpan.Zero),
                trendPoints: new[] {
                    new GitHubObservedTrendPointData(new DateTime(2026, 03, 06), 2d, 1, 0, 0),
                    new GitHubObservedTrendPointData(new DateTime(2026, 03, 07), 4d, 2, 0, 1),
                    new GitHubObservedTrendPointData(new DateTime(2026, 03, 08), 6d, 3, 0, 1),
                    new GitHubObservedTrendPointData(new DateTime(2026, 03, 09), 2d, 1, 0, 0),
                    new GitHubObservedTrendPointData(new DateTime(2026, 03, 10), 7d, 4, 1, 0),
                    new GitHubObservedTrendPointData(new DateTime(2026, 03, 11), 5d, 2, 0, 0),
                    new GitHubObservedTrendPointData(new DateTime(2026, 03, 12), 8d, 5, 0, 1)
                }),
            new GitHubObservedRepositoryTrendData(
                repositoryNameWithOwner: "EvotecIT/PSWriteHTML",
                stars: 410,
                forks: 61,
                watchers: 20,
                openIssues: 4,
                starDelta: 4,
                forkDelta: 1,
                watcherDelta: 1,
                openIssueDelta: 0,
                currentCapturedAtUtc: new DateTimeOffset(2026, 03, 12, 10, 30, 0, TimeSpan.Zero),
                previousCapturedAtUtc: new DateTimeOffset(2026, 03, 11, 10, 30, 0, TimeSpan.Zero),
                trendPoints: new[] {
                    new GitHubObservedTrendPointData(new DateTime(2026, 03, 06), 3d, 2, 0, 0),
                    new GitHubObservedTrendPointData(new DateTime(2026, 03, 07), 5d, 3, 0, 1),
                    new GitHubObservedTrendPointData(new DateTime(2026, 03, 08), 7d, 4, 0, 1),
                    new GitHubObservedTrendPointData(new DateTime(2026, 03, 09), 3d, 2, 0, 0),
                    new GitHubObservedTrendPointData(new DateTime(2026, 03, 10), 8d, 5, 1, 0),
                    new GitHubObservedTrendPointData(new DateTime(2026, 03, 11), 6d, 3, 0, 0),
                    new GitHubObservedTrendPointData(new DateTime(2026, 03, 12), 9d, 6, 0, 1)
                }),
            new GitHubObservedRepositoryTrendData(
                repositoryNameWithOwner: "EvotecIT/GPOZaurr",
                stars: 533,
                forks: 72,
                watchers: 22,
                openIssues: 6,
                starDelta: -2,
                forkDelta: 0,
                watcherDelta: -1,
                openIssueDelta: 1,
                currentCapturedAtUtc: new DateTimeOffset(2026, 03, 12, 10, 30, 0, TimeSpan.Zero),
                previousCapturedAtUtc: new DateTimeOffset(2026, 03, 11, 10, 30, 0, TimeSpan.Zero),
                trendPoints: new[] {
                    new GitHubObservedTrendPointData(new DateTime(2026, 03, 06), -2d, -1, 0, 0),
                    new GitHubObservedTrendPointData(new DateTime(2026, 03, 07), -4d, -2, 0, -1),
                    new GitHubObservedTrendPointData(new DateTime(2026, 03, 08), -6d, -3, 0, -1),
                    new GitHubObservedTrendPointData(new DateTime(2026, 03, 09), -2d, -1, 0, 0),
                    new GitHubObservedTrendPointData(new DateTime(2026, 03, 10), -7d, -4, 0, 0),
                    new GitHubObservedTrendPointData(new DateTime(2026, 03, 11), -5d, -2, 0, 0),
                    new GitHubObservedTrendPointData(new DateTime(2026, 03, 12), -8d, -5, 0, -1)
                })
        });

        AssertEqual(3, correlations.Count, "github observability star correlation count");
        AssertEqual(
            true,
            correlations.Any(static correlation =>
                string.Equals(correlation.RepositoryANameWithOwner, "EvotecIT/IntelligenceX", StringComparison.Ordinal) &&
                string.Equals(correlation.RepositoryBNameWithOwner, "EvotecIT/PSWriteHTML", StringComparison.Ordinal) &&
                correlation.Correlation > 0.95d &&
                correlation.SharedGainDays == 7),
            "github observability includes strong positive star correlation");
        AssertEqual(
            true,
            correlations.Any(static correlation =>
                string.Equals(correlation.RepositoryANameWithOwner, "EvotecIT/IntelligenceX", StringComparison.Ordinal) &&
                string.Equals(correlation.RepositoryBNameWithOwner, "EvotecIT/GPOZaurr", StringComparison.Ordinal) &&
                correlation.Correlation < -0.95d &&
                correlation.OpposingDays == 7),
            "github observability includes strong negative star correlation");
    }

    private static void TestGitHubLocalActivityCorrelationSummaryBuildsRepoSignals() {
        var summary = GitHubLocalActivityCorrelationSummaryBuilder.BuildFromDailySeries(
            new GitCodeChurnSummaryData(
                repositoryRootPath: @"C:\Support\GitHub\IntelligenceX",
                repositoryName: "IntelligenceX",
                recentAddedLines: 1_240,
                recentDeletedLines: 480,
                recentFilesModified: 37,
                recentCommitCount: 11,
                recentActiveDayCount: 4,
                previousAddedLines: 820,
                previousDeletedLines: 310,
                previousFilesModified: 24,
                previousCommitCount: 8,
                last30DaysAddedLines: 3_920,
                last30DaysDeletedLines: 1_840,
                last30DaysFilesModified: 114,
                last30DaysCommitCount: 36,
                last30DaysActiveDayCount: 14,
                latestCommitAtUtc: new DateTimeOffset(2026, 03, 12, 14, 45, 0, TimeSpan.Zero),
                trendDays: new[] {
                    new GitCodeChurnDayData(new DateTime(2026, 03, 06), 120, 40, 6, 2),
                    new GitCodeChurnDayData(new DateTime(2026, 03, 07), 0, 0, 0, 0),
                    new GitCodeChurnDayData(new DateTime(2026, 03, 08), 340, 110, 12, 3),
                    new GitCodeChurnDayData(new DateTime(2026, 03, 09), 0, 0, 0, 0),
                    new GitCodeChurnDayData(new DateTime(2026, 03, 10), 210, 90, 8, 2),
                    new GitCodeChurnDayData(new DateTime(2026, 03, 11), 180, 70, 5, 1),
                    new GitCodeChurnDayData(new DateTime(2026, 03, 12), 390, 170, 14, 3)
                }),
            new[] {
                new GitCodeUsageProviderSeriesData(
                    "codex",
                    "Codex",
                    new[] {
                        new GitCodeUsageDailyValueData(new DateTime(2026, 03, 06), 160, 2),
                        new GitCodeUsageDailyValueData(new DateTime(2026, 03, 07), 0, 0),
                        new GitCodeUsageDailyValueData(new DateTime(2026, 03, 08), 450, 4),
                        new GitCodeUsageDailyValueData(new DateTime(2026, 03, 09), 0, 0),
                        new GitCodeUsageDailyValueData(new DateTime(2026, 03, 10), 300, 3),
                        new GitCodeUsageDailyValueData(new DateTime(2026, 03, 11), 250, 2),
                        new GitCodeUsageDailyValueData(new DateTime(2026, 03, 12), 560, 5)
                    })
            },
            new GitHubObservabilitySummaryData(
                dbPath: @"C:\telemetry\usage.db",
                enabledWatchCount: 3,
                snapshotRepositoryCount: 3,
                comparableRepositoryCount: 3,
                totalStars: 1_063,
                totalForks: 153,
                totalWatchers: 54,
                positiveStarDelta: 5,
                positiveForkDelta: 1,
                positiveWatcherDelta: 2,
                changedRepositoryCount: 1,
                latestCaptureAtUtc: new DateTimeOffset(2026, 03, 12, 10, 30, 0, TimeSpan.Zero),
                repositories: new[] {
                    new GitHubObservedRepositoryTrendData(
                        repositoryNameWithOwner: "EvotecIT/IntelligenceX",
                        stars: 120,
                        forks: 20,
                        watchers: 12,
                        openIssues: 3,
                        starDelta: 5,
                        forkDelta: 1,
                        watcherDelta: 2,
                        openIssueDelta: -1,
                        currentCapturedAtUtc: new DateTimeOffset(2026, 03, 12, 10, 30, 0, TimeSpan.Zero),
                        previousCapturedAtUtc: new DateTimeOffset(2026, 03, 11, 10, 30, 0, TimeSpan.Zero),
                        trendPoints: new[] {
                            new GitHubObservedTrendPointData(new DateTime(2026, 03, 06), 2d, 1, 0, 0),
                            new GitHubObservedTrendPointData(new DateTime(2026, 03, 07), 0d, 0, 0, 0),
                            new GitHubObservedTrendPointData(new DateTime(2026, 03, 08), 6d, 1, 0, 1),
                            new GitHubObservedTrendPointData(new DateTime(2026, 03, 09), 0d, 0, 0, 0),
                            new GitHubObservedTrendPointData(new DateTime(2026, 03, 10), 7d, 1, 1, 0),
                            new GitHubObservedTrendPointData(new DateTime(2026, 03, 11), 5d, 1, 0, 0),
                            new GitHubObservedTrendPointData(new DateTime(2026, 03, 12), 8d, 1, 0, 1)
                        }),
                    new GitHubObservedRepositoryTrendData(
                        repositoryNameWithOwner: "EvotecIT/GPOZaurr",
                        stars: 533,
                        forks: 72,
                        watchers: 22,
                        openIssues: 6,
                        starDelta: -2,
                        forkDelta: 0,
                        watcherDelta: -1,
                        openIssueDelta: 1,
                        currentCapturedAtUtc: new DateTimeOffset(2026, 03, 12, 10, 30, 0, TimeSpan.Zero),
                        previousCapturedAtUtc: new DateTimeOffset(2026, 03, 11, 10, 30, 0, TimeSpan.Zero),
                        trendPoints: new[] {
                            new GitHubObservedTrendPointData(new DateTime(2026, 03, 06), -2d, -1, 0, 0),
                            new GitHubObservedTrendPointData(new DateTime(2026, 03, 07), 0d, 0, 0, 0),
                            new GitHubObservedTrendPointData(new DateTime(2026, 03, 08), -6d, -1, 0, -1),
                            new GitHubObservedTrendPointData(new DateTime(2026, 03, 09), 0d, 0, 0, 0),
                            new GitHubObservedTrendPointData(new DateTime(2026, 03, 10), -7d, -1, 0, 0),
                            new GitHubObservedTrendPointData(new DateTime(2026, 03, 11), -5d, -1, 0, 0),
                            new GitHubObservedTrendPointData(new DateTime(2026, 03, 12), -8d, -1, 0, -1)
                        })
                },
                correlations: Array.Empty<GitHubObservedCorrelationData>()));

        AssertEqual(true, summary.HasSignals, "github local activity correlation has signals");
        AssertNotNull(summary.StrongestPositiveCorrelation, "github local activity strongest positive exists");
        AssertNotNull(summary.StrongestNegativeCorrelation, "github local activity strongest negative exists");
        AssertEqual("EvotecIT/IntelligenceX", summary.StrongestPositiveCorrelation!.RepositoryNameWithOwner, "github local activity strongest positive repo");
        AssertEqual("EvotecIT/GPOZaurr", summary.StrongestNegativeCorrelation!.RepositoryNameWithOwner, "github local activity strongest negative repo");
    }

    private static void TestGitHubRepositoryClusterSummaryBuildsRelatedRepoSignals() {
        var gitHubSummary = new GitHubObservabilitySummaryData(
            dbPath: @"C:\telemetry\usage.db",
            enabledWatchCount: 3,
            snapshotRepositoryCount: 3,
            comparableRepositoryCount: 3,
            totalStars: 1_063,
            totalForks: 153,
            totalWatchers: 54,
            positiveStarDelta: 5,
            positiveForkDelta: 1,
            positiveWatcherDelta: 2,
            changedRepositoryCount: 1,
            latestCaptureAtUtc: new DateTimeOffset(2026, 03, 12, 10, 30, 0, TimeSpan.Zero),
            repositories: new[] {
                new GitHubObservedRepositoryTrendData(
                    repositoryNameWithOwner: "EvotecIT/IntelligenceX",
                    stars: 120,
                    forks: 20,
                    watchers: 12,
                    openIssues: 3,
                    starDelta: 5,
                    forkDelta: 1,
                    watcherDelta: 2,
                    openIssueDelta: -1,
                    currentCapturedAtUtc: new DateTimeOffset(2026, 03, 12, 10, 30, 0, TimeSpan.Zero),
                    previousCapturedAtUtc: new DateTimeOffset(2026, 03, 11, 10, 30, 0, TimeSpan.Zero),
                    trendPoints: new[] {
                        new GitHubObservedTrendPointData(new DateTime(2026, 03, 06), 2d, 1, 0, 0),
                        new GitHubObservedTrendPointData(new DateTime(2026, 03, 07), 4d, 2, 0, 1),
                        new GitHubObservedTrendPointData(new DateTime(2026, 03, 08), 6d, 3, 0, 1),
                        new GitHubObservedTrendPointData(new DateTime(2026, 03, 09), 2d, 1, 0, 0),
                        new GitHubObservedTrendPointData(new DateTime(2026, 03, 10), 7d, 4, 1, 0),
                        new GitHubObservedTrendPointData(new DateTime(2026, 03, 11), 5d, 2, 0, 0),
                        new GitHubObservedTrendPointData(new DateTime(2026, 03, 12), 8d, 5, 0, 1)
                    }),
                new GitHubObservedRepositoryTrendData(
                    repositoryNameWithOwner: "EvotecIT/PSWriteHTML",
                    stars: 410,
                    forks: 61,
                    watchers: 20,
                    openIssues: 4,
                    starDelta: 4,
                    forkDelta: 1,
                    watcherDelta: 1,
                    openIssueDelta: 0,
                    currentCapturedAtUtc: new DateTimeOffset(2026, 03, 12, 10, 30, 0, TimeSpan.Zero),
                    previousCapturedAtUtc: new DateTimeOffset(2026, 03, 11, 10, 30, 0, TimeSpan.Zero),
                    trendPoints: new[] {
                        new GitHubObservedTrendPointData(new DateTime(2026, 03, 06), 3d, 2, 0, 0),
                        new GitHubObservedTrendPointData(new DateTime(2026, 03, 07), 5d, 3, 0, 1),
                        new GitHubObservedTrendPointData(new DateTime(2026, 03, 08), 7d, 4, 0, 1),
                        new GitHubObservedTrendPointData(new DateTime(2026, 03, 09), 3d, 2, 0, 0),
                        new GitHubObservedTrendPointData(new DateTime(2026, 03, 10), 8d, 5, 1, 0),
                        new GitHubObservedTrendPointData(new DateTime(2026, 03, 11), 6d, 3, 0, 0),
                        new GitHubObservedTrendPointData(new DateTime(2026, 03, 12), 9d, 6, 0, 1)
                    }),
                new GitHubObservedRepositoryTrendData(
                    repositoryNameWithOwner: "EvotecIT/GPOZaurr",
                    stars: 533,
                    forks: 72,
                    watchers: 22,
                    openIssues: 6,
                    starDelta: -2,
                    forkDelta: 0,
                    watcherDelta: -1,
                    openIssueDelta: 1,
                    currentCapturedAtUtc: new DateTimeOffset(2026, 03, 12, 10, 30, 0, TimeSpan.Zero),
                    previousCapturedAtUtc: new DateTimeOffset(2026, 03, 11, 10, 30, 0, TimeSpan.Zero),
                    trendPoints: new[] {
                        new GitHubObservedTrendPointData(new DateTime(2026, 03, 06), -2d, -1, 0, 0),
                        new GitHubObservedTrendPointData(new DateTime(2026, 03, 07), -4d, -2, 0, -1),
                        new GitHubObservedTrendPointData(new DateTime(2026, 03, 08), -6d, -3, 0, -1),
                        new GitHubObservedTrendPointData(new DateTime(2026, 03, 09), -2d, -1, 0, 0),
                        new GitHubObservedTrendPointData(new DateTime(2026, 03, 10), -7d, -4, 0, 0),
                        new GitHubObservedTrendPointData(new DateTime(2026, 03, 11), -5d, -2, 0, 0),
                        new GitHubObservedTrendPointData(new DateTime(2026, 03, 12), -8d, -5, 0, -1)
                    })
            },
            correlations: Array.Empty<GitHubObservedCorrelationData>(),
            starCorrelations: new[] {
                new GitHubObservedStarCorrelationData(
                    "EvotecIT/IntelligenceX",
                    "EvotecIT/PSWriteHTML",
                    0.97d,
                    7,
                    5,
                    0,
                    0,
                    5,
                    4)
            },
            stargazerAudienceOverlaps: new[] {
                new GitHubObservedStargazerAudienceOverlapData(
                    "EvotecIT/IntelligenceX",
                    "EvotecIT/PSWriteHTML",
                    2,
                    5,
                    4,
                    0.50d,
                    new[] { "alice", "bob" })
            },
            forkNetworkOverlaps: new[] {
                new GitHubObservedForkNetworkOverlapData(
                    "EvotecIT/IntelligenceX",
                    "EvotecIT/PSWriteHTML",
                    2,
                    4,
                    3,
                    0.67d,
                    new[] { "alice", "bob" })
            },
            observedForkOwnerCount: 5);

        var localSummary = new GitHubLocalActivityCorrelationSummaryData(
            repositoryName: "IntelligenceX",
            watchedRepositoryCount: 3,
            recentChurnVolume: 1720,
            recentUsageTotal: 1720d,
            activeLocalDays: 4,
            repositoryCorrelations: new[] {
                new GitHubLocalActivityRepositoryCorrelationData("EvotecIT/IntelligenceX", 0.91d, 7, 4, 0, 3, 12d, 120, 20, 12, 5, 1, 2),
                new GitHubLocalActivityRepositoryCorrelationData("EvotecIT/PSWriteHTML", 0.74d, 7, 4, 0, 3, 9d, 410, 61, 20, 4, 1, 1)
            });

        var summary = GitHubRepositoryClusterSummaryBuilder.Build(gitHubSummary, localSummary);

        AssertEqual(true, summary.HasSignals, "github repo cluster summary has signals");
        AssertNotNull(summary.StrongestCluster, "github repo cluster strongest exists");
        AssertEqual("EvotecIT/IntelligenceX", summary.StrongestCluster!.RepositoryANameWithOwner, "github repo cluster repo a");
        AssertEqual("EvotecIT/PSWriteHTML", summary.StrongestCluster.RepositoryBNameWithOwner, "github repo cluster repo b");
        AssertEqual(4, summary.StrongestCluster.SupportingSignalCount, "github repo cluster supporting signals");
        AssertEqual(2, summary.StrongestCluster.SharedStargazerCount, "github repo cluster shared stargazers");
        AssertEqual(2, summary.StrongestCluster.SharedForkOwnerCount, "github repo cluster shared forkers");
        AssertEqual(2, summary.StrongestCluster.LocallyAlignedRepositoryCount, "github repo cluster local overlaps");
    }

    private static void TestGitHubRepositorySqliteStoresPersistAcrossReopen() {
        var tempDir = CreateUsageTelemetryTempDirectory();
        try {
            var dbPath = Path.Combine(tempDir, "github-observability.db");
            var watch = new GitHubRepositoryWatchRecord(
                GitHubRepositoryWatchRecord.CreateStableId("EvotecIT/IntelligenceX"),
                "EvotecIT/IntelligenceX",
                new DateTimeOffset(2026, 3, 15, 10, 0, 0, TimeSpan.Zero)) {
                DisplayName = "IntelligenceX",
                Category = "tray",
                Notes = "Primary watch"
            };
            var firstSnapshot = new GitHubRepositorySnapshotRecord(
                GitHubRepositorySnapshotRecord.CreateStableId(watch.Id, new DateTimeOffset(2026, 3, 15, 10, 5, 0, TimeSpan.Zero)),
                watch.Id,
                watch.RepositoryNameWithOwner,
                new DateTimeOffset(2026, 3, 15, 10, 5, 0, TimeSpan.Zero),
                120,
                20,
                14,
                6) {
                PrimaryLanguage = "C#",
                Url = "https://github.com/EvotecIT/IntelligenceX"
            };
            var secondSnapshot = new GitHubRepositorySnapshotRecord(
                GitHubRepositorySnapshotRecord.CreateStableId(watch.Id, new DateTimeOffset(2026, 3, 16, 10, 5, 0, TimeSpan.Zero)),
                watch.Id,
                watch.RepositoryNameWithOwner,
                new DateTimeOffset(2026, 3, 16, 10, 5, 0, TimeSpan.Zero),
                126,
                21,
                15,
                5) {
                PrimaryLanguage = "C#",
                Url = "https://github.com/EvotecIT/IntelligenceX",
                PushedAtUtc = new DateTimeOffset(2026, 3, 16, 9, 55, 0, TimeSpan.Zero)
            };

            using (var watchStore = new SqliteGitHubRepositoryWatchStore(dbPath))
            using (var snapshotStore = new SqliteGitHubRepositorySnapshotStore(dbPath)) {
                watchStore.Upsert(watch);
                snapshotStore.Upsert(firstSnapshot);
                snapshotStore.Upsert(secondSnapshot);
            }

            using (var reopenedWatchStore = new SqliteGitHubRepositoryWatchStore(dbPath))
            using (var reopenedSnapshotStore = new SqliteGitHubRepositorySnapshotStore(dbPath)) {
                AssertEqual(true, reopenedWatchStore.TryGet(watch.Id, out var persistedWatch), "github sqlite watch persisted");
                AssertEqual("tray", persistedWatch.Category, "github sqlite watch category");
                AssertEqual(true, reopenedSnapshotStore.TryGetLatest(watch.Id, out var latestSnapshot), "github sqlite latest snapshot");
                AssertEqual(126, latestSnapshot.Stars, "github sqlite latest snapshot stars");
                AssertEqual(21, latestSnapshot.Forks, "github sqlite latest snapshot forks");
                AssertEqual("C#", latestSnapshot.PrimaryLanguage, "github sqlite latest snapshot language");
                AssertEqual(2, reopenedSnapshotStore.GetByWatch(watch.Id).Count, "github sqlite snapshot count");
            }
        } finally {
            TryDeleteUsageTelemetryTempDirectory(tempDir);
        }
    }

    private static void TestGitHubRepositoryForkHistoryAnalyticsBuildsNewAndRisingStatuses() {
        var changes = GitHubRepositoryForkHistoryAnalytics.BuildLatestChanges(new[] {
            new GitHubRepositoryForkSnapshotRecord(
                GitHubRepositoryForkSnapshotRecord.CreateStableId("EvotecIT/IntelligenceX", "someone/IntelligenceX", new DateTimeOffset(2026, 3, 15, 10, 0, 0, TimeSpan.Zero)),
                "EvotecIT/IntelligenceX",
                "someone/IntelligenceX",
                new DateTimeOffset(2026, 3, 15, 10, 0, 0, TimeSpan.Zero),
                40d,
                "medium",
                8,
                1,
                2,
                1),
            new GitHubRepositoryForkSnapshotRecord(
                GitHubRepositoryForkSnapshotRecord.CreateStableId("EvotecIT/IntelligenceX", "someone/IntelligenceX", new DateTimeOffset(2026, 3, 16, 10, 0, 0, TimeSpan.Zero)),
                "EvotecIT/IntelligenceX",
                "someone/IntelligenceX",
                new DateTimeOffset(2026, 3, 16, 10, 0, 0, TimeSpan.Zero),
                56d,
                "high",
                12,
                2,
                5,
                1),
            new GitHubRepositoryForkSnapshotRecord(
                GitHubRepositoryForkSnapshotRecord.CreateStableId("EvotecIT/IntelligenceX", "newperson/IntelligenceX", new DateTimeOffset(2026, 3, 16, 10, 0, 0, TimeSpan.Zero)),
                "EvotecIT/IntelligenceX",
                "newperson/IntelligenceX",
                new DateTimeOffset(2026, 3, 16, 10, 0, 0, TimeSpan.Zero),
                34d,
                "low",
                2,
                0,
                1,
                0)
        });

        AssertEqual(2, changes.Count, "github fork history changes count");
        AssertEqual("rising", changes[0].Status, "github fork history rising status");
        AssertEqual(16d, changes[0].ScoreDelta, "github fork history rising score delta");
        AssertEqual(4, changes[0].StarDelta, "github fork history rising star delta");
        AssertEqual("new", changes[1].Status, "github fork history new status");
        AssertEqual(34d, changes[1].ScoreDelta, "github fork history new baseline score");
    }

    private static void TestGitHubRepositoryForkSqliteStorePersistsAcrossReopen() {
        var tempDir = CreateUsageTelemetryTempDirectory();
        try {
            var dbPath = Path.Combine(tempDir, "github-forks.db");
            var snapshot = new GitHubRepositoryForkSnapshotRecord(
                GitHubRepositoryForkSnapshotRecord.CreateStableId("EvotecIT/IntelligenceX", "someone/IntelligenceX", new DateTimeOffset(2026, 3, 16, 10, 0, 0, TimeSpan.Zero)),
                "EvotecIT/IntelligenceX",
                "someone/IntelligenceX",
                new DateTimeOffset(2026, 3, 16, 10, 0, 0, TimeSpan.Zero),
                72.5d,
                "high",
                18,
                3,
                9,
                2) {
                Url = "https://github.com/someone/IntelligenceX",
                Description = "High-signal fork",
                PrimaryLanguage = "C#",
                PushedAtUtc = new DateTimeOffset(2026, 3, 14, 12, 0, 0, TimeSpan.Zero),
                ReasonsSummary = "18 stars | updated within 14 days"
            };

            using (var store = new SqliteGitHubRepositoryForkSnapshotStore(dbPath)) {
                store.Upsert(snapshot);
            }

            using (var reopened = new SqliteGitHubRepositoryForkSnapshotStore(dbPath)) {
                var byParent = reopened.GetByParentRepository("EvotecIT/IntelligenceX");
                AssertEqual(1, byParent.Count, "github fork sqlite by parent count");
                AssertEqual("someone/IntelligenceX", byParent[0].ForkRepositoryNameWithOwner, "github fork sqlite fork repo");
                AssertEqual(72.5d, byParent[0].Score, "github fork sqlite score");
                AssertEqual("18 stars | updated within 14 days", byParent[0].ReasonsSummary, "github fork sqlite reason summary");

                var emptyCaptureAtUtc = new DateTimeOffset(2026, 3, 16, 12, 0, 0, TimeSpan.Zero);
                reopened.MarkParentRepositoryCaptured("EvotecIT/PSWriteHTML", emptyCaptureAtUtc);
                AssertEqual(emptyCaptureAtUtc, reopened.GetLatestCaptureAtUtcByParentRepository("EvotecIT/PSWriteHTML"), "github fork sqlite capture watermark persists without rows");
            }
        } finally {
            TryDeleteUsageTelemetryTempDirectory(tempDir);
        }
    }

    private static void TestGitHubRepositoryStargazerSqliteStorePersistsAcrossReopen() {
        var tempDir = CreateUsageTelemetryTempDirectory();
        try {
            var dbPath = Path.Combine(tempDir, "github-stargazers.db");
            var capturedAtUtc = new DateTimeOffset(2026, 3, 16, 10, 0, 0, TimeSpan.Zero);
            var snapshot = new GitHubRepositoryStargazerSnapshotRecord(
                GitHubRepositoryStargazerSnapshotRecord.CreateStableId("EvotecIT/IntelligenceX", "alice", capturedAtUtc),
                "EvotecIT/IntelligenceX",
                "alice",
                capturedAtUtc) {
                StarredAtUtc = new DateTimeOffset(2026, 3, 14, 12, 0, 0, TimeSpan.Zero),
                ProfileUrl = "https://github.com/alice",
                AvatarUrl = "https://avatars.githubusercontent.com/u/1?v=4"
            };

            using (var store = new SqliteGitHubRepositoryStargazerSnapshotStore(dbPath)) {
                store.Upsert(snapshot);
            }

            using (var reopened = new SqliteGitHubRepositoryStargazerSnapshotStore(dbPath)) {
                var byRepository = reopened.GetByRepository("EvotecIT/IntelligenceX");
                AssertEqual(1, byRepository.Count, "github stargazer sqlite by repository count");
                AssertEqual("alice", byRepository[0].StargazerLogin, "github stargazer sqlite login");
                AssertEqual("https://github.com/alice", byRepository[0].ProfileUrl, "github stargazer sqlite profile url");
                AssertEqual(1, reopened.GetByStargazer("alice").Count, "github stargazer sqlite by login count");
                AssertEqual(1, reopened.GetByStargazer("ALICE").Count, "github stargazer sqlite login lookup ignores case");

                var emptyCaptureAtUtc = capturedAtUtc.AddHours(2);
                reopened.MarkRepositoryCaptured("EvotecIT/PSWriteHTML", emptyCaptureAtUtc);
                AssertEqual(emptyCaptureAtUtc, reopened.GetLatestCaptureAtUtcByRepository("EvotecIT/PSWriteHTML"), "github stargazer sqlite capture watermark persists without rows");
            }
        } finally {
            TryDeleteUsageTelemetryTempDirectory(tempDir);
        }
    }
}
