using System;
using System.IO;
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
            }
        } finally {
            TryDeleteUsageTelemetryTempDirectory(tempDir);
        }
    }
}
