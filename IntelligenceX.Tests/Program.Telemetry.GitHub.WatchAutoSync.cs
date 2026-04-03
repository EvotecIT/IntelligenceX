using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Telemetry.GitHub;

namespace IntelligenceX.Tests;

internal static partial class Program {
    private static void TestGitHubRepositoryWatchAutoSyncServiceSyncsStaleSnapshotsAndStargazers() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-gh-watch-auto-sync-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var dbPath = Path.Combine(temp, "usage.db");
        try {
            var watch = new GitHubRepositoryWatchRecord(
                GitHubRepositoryWatchRecord.CreateStableId("EvotecIT/IntelligenceX"),
                "EvotecIT/IntelligenceX",
                new DateTimeOffset(2026, 04, 01, 9, 0, 0, TimeSpan.Zero));
            using (var watchStore = new SqliteGitHubRepositoryWatchStore(dbPath)) {
                watchStore.Upsert(watch);
            }

            var nowUtc = new DateTimeOffset(2026, 04, 03, 12, 0, 0, TimeSpan.Zero);
            var service = new GitHubRepositoryWatchAutoSyncService(
                _ => new FakeGitHubRepositoryWatchAutoSyncClient(
                    (repositoryNameWithOwner, _) => Task.FromResult<GitHubRepoInfo?>(
                        new GitHubRepoInfo(
                            repositoryNameWithOwner,
                            stars: 144,
                            forks: 21,
                            description: "Repo description",
                            language: "C#",
                            languageColor: null,
                            watchers: 13,
                            openIssues: 4,
                            pushedAtUtc: nowUtc.AddHours(-2),
                            isArchived: false,
                            isFork: false)),
                    (_, _, _) => Task.FromResult<IReadOnlyList<GitHubRepositoryForkInfo>>(new[] {
                        new GitHubRepositoryForkInfo(
                            "EvotecIT/IntelligenceX",
                            "alice/IntelligenceX",
                            score: 83d,
                            tier: "high",
                            stars: 12,
                            forks: 1,
                            watchers: 3,
                            openIssues: 0,
                            url: "https://github.com/alice/IntelligenceX",
                            description: "Community fork",
                            primaryLanguage: "C#",
                            pushedAtUtc: nowUtc.AddHours(-6),
                            updatedAtUtc: nowUtc.AddHours(-4),
                            createdAtUtc: nowUtc.AddDays(-10),
                            reasons: new[] { "12 stars", "active" })
                    }),
                    (_, limit, _) => Task.FromResult<IReadOnlyList<GitHubRepositoryStargazerInfo>>(new[] {
                        new GitHubRepositoryStargazerInfo("alice", "https://github.com/alice", null, nowUtc.AddDays(-2)),
                        new GitHubRepositoryStargazerInfo("bob", "https://github.com/bob", null, nowUtc.AddDays(-1))
                    })),
                () => dbPath,
                () => nowUtc);

            var result = service.SyncIfNeededAsync("token-value", cancellationToken: CancellationToken.None).GetAwaiter().GetResult();

            AssertEqual(GitHubRepositoryWatchAutoSyncStatus.Updated, result.Status, "github watch auto sync status");
            AssertEqual(1, result.SnapshotSyncCount, "github watch auto sync snapshot count");
            AssertEqual(1, result.ForkRepositorySyncCount, "github watch auto sync fork repo count");
            AssertEqual(1, result.ForkSnapshotCount, "github watch auto sync fork snapshot count");
            AssertEqual(1, result.StargazerRepositorySyncCount, "github watch auto sync stargazer repo count");
            AssertEqual(2, result.StargazerSnapshotCount, "github watch auto sync stargazer snapshot count");

            using var snapshotStore = new SqliteGitHubRepositorySnapshotStore(dbPath);
            var snapshots = snapshotStore.GetByWatch(watch.Id);
            AssertEqual(1, snapshots.Count, "github watch auto sync recorded snapshot");
            AssertEqual(144, snapshots[0].Stars, "github watch auto sync snapshot stars");
            AssertEqual(21, snapshots[0].Forks, "github watch auto sync snapshot forks");
            AssertEqual(13, snapshots[0].Watchers, "github watch auto sync snapshot watchers");

            using var forkStore = new SqliteGitHubRepositoryForkSnapshotStore(dbPath);
            var forks = forkStore.GetByParentRepository("EvotecIT/IntelligenceX");
            AssertEqual(1, forks.Count, "github watch auto sync recorded forks");
            AssertEqual("alice/IntelligenceX", forks[0].ForkRepositoryNameWithOwner, "github watch auto sync includes useful fork");
            AssertEqual(83d, forks[0].Score, "github watch auto sync fork score");

            using var stargazerStore = new SqliteGitHubRepositoryStargazerSnapshotStore(dbPath);
            var stargazers = stargazerStore.GetByRepository("EvotecIT/IntelligenceX");
            AssertEqual(2, stargazers.Count, "github watch auto sync recorded stargazers");
            AssertEqual(true, stargazers.Any(static snapshot => string.Equals(snapshot.StargazerLogin, "alice", StringComparison.OrdinalIgnoreCase)), "github watch auto sync includes alice");
            AssertEqual(true, stargazers.Any(static snapshot => string.Equals(snapshot.StargazerLogin, "bob", StringComparison.OrdinalIgnoreCase)), "github watch auto sync includes bob");
        } finally {
            try {
                if (Directory.Exists(temp)) {
                    Directory.Delete(temp, recursive: true);
                }
            } catch {
                // best-effort cleanup
            }
        }
    }

    private static void TestGitHubRepositoryWatchAutoSyncServiceSkipsFreshRepositories() {
        var temp = Path.Combine(Path.GetTempPath(), "ix-gh-watch-auto-sync-fresh-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var dbPath = Path.Combine(temp, "usage.db");
        try {
            var watch = new GitHubRepositoryWatchRecord(
                GitHubRepositoryWatchRecord.CreateStableId("EvotecIT/IntelligenceX"),
                "EvotecIT/IntelligenceX",
                new DateTimeOffset(2026, 04, 01, 9, 0, 0, TimeSpan.Zero));
            var capturedAtUtc = new DateTimeOffset(2026, 04, 03, 11, 30, 0, TimeSpan.Zero);
            using (var watchStore = new SqliteGitHubRepositoryWatchStore(dbPath)) {
                watchStore.Upsert(watch);
            }

            using (var snapshotStore = new SqliteGitHubRepositorySnapshotStore(dbPath)) {
                snapshotStore.Upsert(new GitHubRepositorySnapshotRecord(
                    GitHubRepositorySnapshotRecord.CreateStableId(watch.Id, capturedAtUtc),
                    watch.Id,
                    watch.RepositoryNameWithOwner,
                    capturedAtUtc,
                    140,
                    20,
                    12,
                    3));
            }

            using (var forkStore = new SqliteGitHubRepositoryForkSnapshotStore(dbPath)) {
                forkStore.Upsert(new GitHubRepositoryForkSnapshotRecord(
                    GitHubRepositoryForkSnapshotRecord.CreateStableId(watch.RepositoryNameWithOwner, "alice/IntelligenceX", capturedAtUtc),
                    watch.RepositoryNameWithOwner,
                    "alice/IntelligenceX",
                    capturedAtUtc,
                    score: 80d,
                    tier: "high",
                    stars: 12,
                    forks: 1,
                    watchers: 3,
                    openIssues: 0));
            }

            using (var stargazerStore = new SqliteGitHubRepositoryStargazerSnapshotStore(dbPath)) {
                stargazerStore.Upsert(new GitHubRepositoryStargazerSnapshotRecord(
                    GitHubRepositoryStargazerSnapshotRecord.CreateStableId(watch.RepositoryNameWithOwner, "alice", capturedAtUtc),
                    watch.RepositoryNameWithOwner,
                    "alice",
                    capturedAtUtc));
            }

            var clientCreated = 0;
            var service = new GitHubRepositoryWatchAutoSyncService(
                _ => {
                    clientCreated++;
                    return new FakeGitHubRepositoryWatchAutoSyncClient(
                        (_, _) => Task.FromResult<GitHubRepoInfo?>(null),
                        (_, _, _) => Task.FromResult<IReadOnlyList<GitHubRepositoryForkInfo>>(Array.Empty<GitHubRepositoryForkInfo>()),
                        (_, _, _) => Task.FromResult<IReadOnlyList<GitHubRepositoryStargazerInfo>>(Array.Empty<GitHubRepositoryStargazerInfo>()));
                },
                () => dbPath,
                () => capturedAtUtc.AddMinutes(20));

            var result = service.SyncIfNeededAsync("token-value", cancellationToken: CancellationToken.None).GetAwaiter().GetResult();

            AssertEqual(GitHubRepositoryWatchAutoSyncStatus.Fresh, result.Status, "github watch auto sync fresh status");
            AssertEqual(0, clientCreated, "github watch auto sync does not create client when fresh");
            AssertEqual(0, result.SnapshotSyncCount, "github watch auto sync fresh snapshot count");
            AssertEqual(0, result.ForkRepositorySyncCount, "github watch auto sync fresh fork repo count");
            AssertEqual(0, result.StargazerRepositorySyncCount, "github watch auto sync fresh stargazer repo count");
        } finally {
            try {
                if (Directory.Exists(temp)) {
                    Directory.Delete(temp, recursive: true);
                }
            } catch {
                // best-effort cleanup
            }
        }
    }

    private sealed class FakeGitHubRepositoryWatchAutoSyncClient : IGitHubRepositoryWatchAutoSyncClient {
        private readonly Func<string, CancellationToken, Task<GitHubRepoInfo?>> _fetchRepositoryAsync;
        private readonly Func<string, int, CancellationToken, Task<IReadOnlyList<GitHubRepositoryForkInfo>>> _fetchUsefulForksAsync;
        private readonly Func<string, int, CancellationToken, Task<IReadOnlyList<GitHubRepositoryStargazerInfo>>> _fetchStargazersAsync;

        public FakeGitHubRepositoryWatchAutoSyncClient(
            Func<string, CancellationToken, Task<GitHubRepoInfo?>> fetchRepositoryAsync,
            Func<string, int, CancellationToken, Task<IReadOnlyList<GitHubRepositoryForkInfo>>> fetchUsefulForksAsync,
            Func<string, int, CancellationToken, Task<IReadOnlyList<GitHubRepositoryStargazerInfo>>> fetchStargazersAsync) {
            _fetchRepositoryAsync = fetchRepositoryAsync;
            _fetchUsefulForksAsync = fetchUsefulForksAsync;
            _fetchStargazersAsync = fetchStargazersAsync;
        }

        public Task<GitHubRepoInfo?> FetchRepositoryAsync(string repositoryNameWithOwner, CancellationToken cancellationToken) {
            return _fetchRepositoryAsync(repositoryNameWithOwner, cancellationToken);
        }

        public Task<IReadOnlyList<GitHubRepositoryForkInfo>> FetchUsefulForksAsync(
            string repositoryNameWithOwner,
            int limit,
            CancellationToken cancellationToken) {
            return _fetchUsefulForksAsync(repositoryNameWithOwner, limit, cancellationToken);
        }

        public Task<IReadOnlyList<GitHubRepositoryStargazerInfo>> FetchStargazersAsync(
            string repositoryNameWithOwner,
            int limit,
            CancellationToken cancellationToken) {
            return _fetchStargazersAsync(repositoryNameWithOwner, limit, cancellationToken);
        }

        public void Dispose() {
        }
    }
}
