using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Telemetry.Usage;

namespace IntelligenceX.Telemetry.GitHub;

/// <summary>
/// Controls automatic refresh of watched repository snapshots, fork networks, and stargazer audiences.
/// </summary>
internal sealed class GitHubRepositoryWatchAutoSyncOptions {
    /// <summary>
    /// Gets or sets the maximum allowed age for a watched repository snapshot before it is refreshed.
    /// </summary>
    public TimeSpan SnapshotFreshnessWindow { get; set; } = TimeSpan.FromHours(6);

    /// <summary>
    /// Gets or sets the maximum allowed age for captured stargazer audiences before they are refreshed.
    /// </summary>
    public TimeSpan StargazerFreshnessWindow { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Gets or sets the maximum allowed age for useful fork snapshots before they are refreshed.
    /// </summary>
    public TimeSpan ForkFreshnessWindow { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// Gets or sets whether useful fork snapshots should be refreshed alongside repo snapshots.
    /// </summary>
    public bool IncludeForks { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of useful forks to capture per watched repository.
    /// </summary>
    public int ForkLimit { get; set; } = 10;

    /// <summary>
    /// Gets or sets whether stargazer audiences should be refreshed alongside repo snapshots.
    /// </summary>
    public bool IncludeStargazers { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum number of stargazer records to fetch per repository.
    /// </summary>
    public int StargazerLimit { get; set; } = 200;
}

/// <summary>
/// High-level status for an automatic watched-repository sync attempt.
/// </summary>
internal enum GitHubRepositoryWatchAutoSyncStatus {
    /// <summary>
    /// No usable GitHub token was available.
    /// </summary>
    NoToken = 0,

    /// <summary>
    /// The local telemetry database path could not be resolved.
    /// </summary>
    DatabaseUnavailable = 1,

    /// <summary>
    /// No enabled watched repositories were registered.
    /// </summary>
    NoWatches = 2,

    /// <summary>
    /// Local watched-repository telemetry was already fresh.
    /// </summary>
    Fresh = 3,

    /// <summary>
    /// Stale watched-repository telemetry was refreshed successfully.
    /// </summary>
    Updated = 4,

    /// <summary>
    /// The sync completed with partial failures.
    /// </summary>
    Partial = 5
}

/// <summary>
/// Result returned by automatic watched-repository sync operations.
/// </summary>
internal sealed class GitHubRepositoryWatchAutoSyncResult {
    /// <summary>
    /// Initializes an auto-sync result.
    /// </summary>
    public GitHubRepositoryWatchAutoSyncResult(
        GitHubRepositoryWatchAutoSyncStatus status,
        string message,
        string? databasePath = null,
        DateTimeOffset? capturedAtUtc = null,
        int watchCount = 0,
        int snapshotSyncCount = 0,
        int forkRepositorySyncCount = 0,
        int forkSnapshotCount = 0,
        int stargazerRepositorySyncCount = 0,
        int stargazerSnapshotCount = 0,
        IReadOnlyList<string>? syncedRepositories = null,
        IReadOnlyList<string>? failedRepositories = null) {
        Status = status;
        Message = string.IsNullOrWhiteSpace(message) ? status.ToString() : message.Trim();
        DatabasePath = string.IsNullOrWhiteSpace(databasePath) ? null : Path.GetFullPath(databasePath!);
        CapturedAtUtc = capturedAtUtc?.ToUniversalTime();
        WatchCount = Math.Max(0, watchCount);
        SnapshotSyncCount = Math.Max(0, snapshotSyncCount);
        ForkRepositorySyncCount = Math.Max(0, forkRepositorySyncCount);
        ForkSnapshotCount = Math.Max(0, forkSnapshotCount);
        StargazerRepositorySyncCount = Math.Max(0, stargazerRepositorySyncCount);
        StargazerSnapshotCount = Math.Max(0, stargazerSnapshotCount);
        SyncedRepositories = syncedRepositories ?? Array.Empty<string>();
        FailedRepositories = failedRepositories ?? Array.Empty<string>();
    }

    /// <summary>
    /// Gets the overall result status.
    /// </summary>
    public GitHubRepositoryWatchAutoSyncStatus Status { get; }

    /// <summary>
    /// Gets the human-readable outcome summary.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Gets the resolved telemetry database path when available.
    /// </summary>
    public string? DatabasePath { get; }

    /// <summary>
    /// Gets the UTC capture time used for this sync attempt.
    /// </summary>
    public DateTimeOffset? CapturedAtUtc { get; }

    /// <summary>
    /// Gets the number of enabled watches that were considered.
    /// </summary>
    public int WatchCount { get; }

    /// <summary>
    /// Gets how many repository snapshots were recorded.
    /// </summary>
    public int SnapshotSyncCount { get; }

    /// <summary>
    /// Gets how many repositories had their useful fork set refreshed.
    /// </summary>
    public int ForkRepositorySyncCount { get; }

    /// <summary>
    /// Gets the number of fork snapshot records written.
    /// </summary>
    public int ForkSnapshotCount { get; }

    /// <summary>
    /// Gets how many repositories had their stargazer audience refreshed.
    /// </summary>
    public int StargazerRepositorySyncCount { get; }

    /// <summary>
    /// Gets the number of stargazer snapshot records written.
    /// </summary>
    public int StargazerSnapshotCount { get; }

    /// <summary>
    /// Gets repositories that were refreshed successfully.
    /// </summary>
    public IReadOnlyList<string> SyncedRepositories { get; }

    /// <summary>
    /// Gets repositories that failed during the sync attempt.
    /// </summary>
    public IReadOnlyList<string> FailedRepositories { get; }

    /// <summary>
    /// Gets whether new data was written.
    /// </summary>
    public bool DidWriteData => SnapshotSyncCount > 0 || ForkRepositorySyncCount > 0 || StargazerRepositorySyncCount > 0;

    /// <summary>
    /// Gets whether the result should usually be surfaced to the user.
    /// </summary>
    public bool ShouldSurfaceStatus => Status == GitHubRepositoryWatchAutoSyncStatus.Updated
                                       || Status == GitHubRepositoryWatchAutoSyncStatus.Partial;
}

internal interface IGitHubRepositoryWatchAutoSyncClient : IDisposable {
    Task<GitHubRepoInfo?> FetchRepositoryAsync(string repositoryNameWithOwner, CancellationToken cancellationToken);
    Task<IReadOnlyList<GitHubRepositoryForkInfo>> FetchUsefulForksAsync(string repositoryNameWithOwner, int limit, CancellationToken cancellationToken);
    Task<IReadOnlyList<GitHubRepositoryStargazerInfo>> FetchStargazersAsync(string repositoryNameWithOwner, int limit, CancellationToken cancellationToken);
}

internal sealed class GitHubDashboardRepositoryWatchAutoSyncClient : IGitHubRepositoryWatchAutoSyncClient {
    private readonly GitHubDashboardService _dashboardService;

    public GitHubDashboardRepositoryWatchAutoSyncClient(GitHubDashboardService dashboardService) {
        _dashboardService = dashboardService ?? throw new ArgumentNullException(nameof(dashboardService));
    }

    public Task<GitHubRepoInfo?> FetchRepositoryAsync(string repositoryNameWithOwner, CancellationToken cancellationToken) {
        return _dashboardService.FetchRepositoryAsync(repositoryNameWithOwner, cancellationToken);
    }

    public Task<IReadOnlyList<GitHubRepositoryForkInfo>> FetchUsefulForksAsync(
        string repositoryNameWithOwner,
        int limit,
        CancellationToken cancellationToken) {
        return _dashboardService.FetchUsefulForksAsync(repositoryNameWithOwner, limit, cancellationToken);
    }

    public Task<IReadOnlyList<GitHubRepositoryStargazerInfo>> FetchStargazersAsync(
        string repositoryNameWithOwner,
        int limit,
        CancellationToken cancellationToken) {
        return _dashboardService.FetchStargazersAsync(repositoryNameWithOwner, limit, cancellationToken);
    }

    public void Dispose() {
        _dashboardService.Dispose();
    }
}

/// <summary>
/// Refreshes watched GitHub repository momentum and audience data when the local cache becomes stale.
/// </summary>
internal sealed class GitHubRepositoryWatchAutoSyncService {
    private readonly Func<string, IGitHubRepositoryWatchAutoSyncClient> _clientFactory;
    private readonly Func<string?> _databasePathResolver;
    private readonly Func<DateTimeOffset> _utcNow;

    /// <summary>
    /// Initializes the default watched-repository auto-sync service.
    /// </summary>
    public GitHubRepositoryWatchAutoSyncService()
        : this(
            token => new GitHubDashboardRepositoryWatchAutoSyncClient(new GitHubDashboardService(token)),
            () => UsageTelemetryPathResolver.ResolveDatabasePath(enabledByDefault: true),
            () => DateTimeOffset.UtcNow) {
    }

    internal GitHubRepositoryWatchAutoSyncService(
        Func<string, IGitHubRepositoryWatchAutoSyncClient> clientFactory,
        Func<string?> databasePathResolver,
        Func<DateTimeOffset> utcNow) {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _databasePathResolver = databasePathResolver ?? throw new ArgumentNullException(nameof(databasePathResolver));
        _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
    }

    /// <summary>
    /// Refreshes watched repositories when their local telemetry is stale.
    /// </summary>
    public async Task<GitHubRepositoryWatchAutoSyncResult> SyncIfNeededAsync(
        string? token,
        GitHubRepositoryWatchAutoSyncOptions? options = null,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(token)) {
            return new GitHubRepositoryWatchAutoSyncResult(
                GitHubRepositoryWatchAutoSyncStatus.NoToken,
                "GitHub watch auto sync skipped because no GitHub token is available.");
        }

        var resolvedDbPath = _databasePathResolver();
        if (string.IsNullOrWhiteSpace(resolvedDbPath)) {
            return new GitHubRepositoryWatchAutoSyncResult(
                GitHubRepositoryWatchAutoSyncStatus.DatabaseUnavailable,
                "GitHub watch auto sync skipped because the telemetry database path is unavailable.");
        }

        var dbPath = Path.GetFullPath(resolvedDbPath);
        options ??= new GitHubRepositoryWatchAutoSyncOptions();
#if NETSTANDARD2_0
        return new GitHubRepositoryWatchAutoSyncResult(
            GitHubRepositoryWatchAutoSyncStatus.DatabaseUnavailable,
            "GitHub watch auto sync is unavailable on this framework target.",
            dbPath);
#else
        using var watchStore = new SqliteGitHubRepositoryWatchStore(dbPath);
        using var snapshotStore = new SqliteGitHubRepositorySnapshotStore(dbPath);
        using var forkStore = new SqliteGitHubRepositoryForkSnapshotStore(dbPath);
        using var stargazerStore = new SqliteGitHubRepositoryStargazerSnapshotStore(dbPath);
        var watches = watchStore.GetAll()
            .Where(static watch => watch.Enabled)
            .OrderBy(static watch => watch.RepositoryNameWithOwner, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (watches.Length == 0) {
            return new GitHubRepositoryWatchAutoSyncResult(
                GitHubRepositoryWatchAutoSyncStatus.NoWatches,
                "GitHub watch auto sync skipped because no enabled watches are registered.",
                dbPath);
        }

        var nowUtc = _utcNow().ToUniversalTime();
        var snapshotFreshnessWindow = SanitizeWindow(options.SnapshotFreshnessWindow, TimeSpan.FromHours(6));
        var forkFreshnessWindow = SanitizeWindow(options.ForkFreshnessWindow, TimeSpan.FromHours(24));
        var stargazerFreshnessWindow = SanitizeWindow(options.StargazerFreshnessWindow, TimeSpan.FromHours(24));
        var staleSnapshotWatches = new List<GitHubRepositoryWatchRecord>();
        var staleForkRepositories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var staleStargazerRepositories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var watch in watches) {
            snapshotStore.TryGetLatest(watch.Id, out var latestSnapshot);
            var needsSnapshot = latestSnapshot is null
                                || nowUtc - latestSnapshot.CapturedAtUtc >= snapshotFreshnessWindow;
            if (needsSnapshot) {
                staleSnapshotWatches.Add(watch);
            }

            if (!options.IncludeStargazers) {
                if (!options.IncludeForks) {
                    continue;
                }
            }

            if (options.IncludeForks) {
                var latestForkCaptureAtUtc = forkStore.GetLatestCaptureAtUtcByParentRepository(watch.RepositoryNameWithOwner);
                var needsForks = !latestForkCaptureAtUtc.HasValue
                                 || nowUtc - latestForkCaptureAtUtc.Value >= forkFreshnessWindow
                                 || latestSnapshot is not null && latestForkCaptureAtUtc.Value < latestSnapshot.CapturedAtUtc;
                if (needsForks) {
                    staleForkRepositories.Add(watch.RepositoryNameWithOwner);
                }
            }

            if (!options.IncludeStargazers) {
                continue;
            }

            var latestStargazerCaptureAtUtc = stargazerStore.GetLatestCaptureAtUtcByRepository(watch.RepositoryNameWithOwner);
            var needsStargazers = !latestStargazerCaptureAtUtc.HasValue
                                  || nowUtc - latestStargazerCaptureAtUtc.Value >= stargazerFreshnessWindow
                                  || latestSnapshot is not null && latestStargazerCaptureAtUtc.Value < latestSnapshot.CapturedAtUtc;
            if (needsStargazers) {
                staleStargazerRepositories.Add(watch.RepositoryNameWithOwner);
            }
        }

        if (staleSnapshotWatches.Count == 0 && staleForkRepositories.Count == 0 && staleStargazerRepositories.Count == 0) {
            return new GitHubRepositoryWatchAutoSyncResult(
                GitHubRepositoryWatchAutoSyncStatus.Fresh,
                "Watched GitHub repo momentum is already fresh.",
                dbPath,
                watchCount: watches.Length);
        }

        var capturedAtUtc = nowUtc;
        var snapshotSyncCount = 0;
        var forkRepositorySyncCount = 0;
        var forkSnapshotCount = 0;
        var stargazerRepositorySyncCount = 0;
        var stargazerSnapshotCount = 0;
        var syncedRepositories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var failedRepositories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalizedToken = token;

        using var client = _clientFactory(normalizedToken!);
        var observabilityService = new GitHubRepositoryObservabilityService(watchStore, snapshotStore);

        foreach (var watch in staleSnapshotWatches) {
            cancellationToken.ThrowIfCancellationRequested();
            try {
                var repository = await client.FetchRepositoryAsync(watch.RepositoryNameWithOwner, cancellationToken).ConfigureAwait(false);
                if (repository is null) {
                    failedRepositories.Add(watch.RepositoryNameWithOwner);
                    continue;
                }

                var snapshot = CreateSnapshot(watch, repository, capturedAtUtc);
                observabilityService.RecordSnapshot(snapshot);
                snapshotSyncCount++;
                syncedRepositories.Add(watch.RepositoryNameWithOwner);
            } catch (OperationCanceledException) {
                throw;
            } catch {
                failedRepositories.Add(watch.RepositoryNameWithOwner);
            }
        }

        if (options.IncludeForks) {
            foreach (var repositoryNameWithOwner in staleForkRepositories.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)) {
                cancellationToken.ThrowIfCancellationRequested();
                try {
                    var forks = await client.FetchUsefulForksAsync(
                        repositoryNameWithOwner,
                        Math.Max(1, options.ForkLimit),
                        cancellationToken).ConfigureAwait(false);
                    forkStore.MarkParentRepositoryCaptured(repositoryNameWithOwner, capturedAtUtc);
                    foreach (var fork in forks) {
                        forkStore.Upsert(CreateForkSnapshot(repositoryNameWithOwner, fork, capturedAtUtc));
                        forkSnapshotCount++;
                    }

                    forkRepositorySyncCount++;
                    syncedRepositories.Add(repositoryNameWithOwner);
                } catch (OperationCanceledException) {
                    throw;
                } catch {
                    failedRepositories.Add(repositoryNameWithOwner);
                }
            }
        }

        if (options.IncludeStargazers) {
            foreach (var repositoryNameWithOwner in staleStargazerRepositories.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)) {
                cancellationToken.ThrowIfCancellationRequested();
                try {
                    var stargazers = await client.FetchStargazersAsync(
                        repositoryNameWithOwner,
                        Math.Max(1, options.StargazerLimit),
                        cancellationToken).ConfigureAwait(false);
                    stargazerStore.MarkRepositoryCaptured(repositoryNameWithOwner, capturedAtUtc);
                    foreach (var stargazer in stargazers) {
                        stargazerStore.Upsert(CreateStargazerSnapshot(repositoryNameWithOwner, stargazer, capturedAtUtc));
                        stargazerSnapshotCount++;
                    }

                    stargazerRepositorySyncCount++;
                    syncedRepositories.Add(repositoryNameWithOwner);
                } catch (OperationCanceledException) {
                    throw;
                } catch {
                    failedRepositories.Add(repositoryNameWithOwner);
                }
            }
        }

        if (snapshotSyncCount == 0 && forkRepositorySyncCount == 0 && stargazerRepositorySyncCount == 0) {
            return new GitHubRepositoryWatchAutoSyncResult(
                GitHubRepositoryWatchAutoSyncStatus.Partial,
                "GitHub watch auto sync could not refresh any stale repositories.",
                dbPath,
                capturedAtUtc,
                watches.Length,
                snapshotSyncCount,
                forkRepositorySyncCount,
                forkSnapshotCount,
                stargazerRepositorySyncCount,
                stargazerSnapshotCount,
                syncedRepositories.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
                failedRepositories.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToArray());
        }

        var status = failedRepositories.Count == 0
            ? GitHubRepositoryWatchAutoSyncStatus.Updated
            : GitHubRepositoryWatchAutoSyncStatus.Partial;
        return new GitHubRepositoryWatchAutoSyncResult(
            status,
            BuildResultMessage(snapshotSyncCount, forkRepositorySyncCount, forkSnapshotCount, stargazerRepositorySyncCount, stargazerSnapshotCount, failedRepositories.Count),
            dbPath,
            capturedAtUtc,
            watches.Length,
            snapshotSyncCount,
            forkRepositorySyncCount,
            forkSnapshotCount,
            stargazerRepositorySyncCount,
            stargazerSnapshotCount,
            syncedRepositories.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
            failedRepositories.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToArray());
#endif
    }

    private static string BuildResultMessage(
        int snapshotSyncCount,
        int forkRepositorySyncCount,
        int forkSnapshotCount,
        int stargazerRepositorySyncCount,
        int stargazerSnapshotCount,
        int failedRepositoryCount) {
        var parts = new List<string>();
        if (snapshotSyncCount > 0) {
            parts.Add("auto-synced " + snapshotSyncCount + " watched repo" + (snapshotSyncCount == 1 ? "" : "s"));
        }
        if (forkRepositorySyncCount > 0) {
            parts.Add("refreshed forks for " + forkRepositorySyncCount + " repo" + (forkRepositorySyncCount == 1 ? "" : "s"));
        }
        if (forkSnapshotCount > 0) {
            parts.Add("recorded " + forkSnapshotCount + " fork snapshot" + (forkSnapshotCount == 1 ? "" : "s"));
        }
        if (stargazerRepositorySyncCount > 0) {
            parts.Add("refreshed stargazers for " + stargazerRepositorySyncCount + " repo" + (stargazerRepositorySyncCount == 1 ? "" : "s"));
        }
        if (stargazerSnapshotCount > 0) {
            parts.Add("recorded " + stargazerSnapshotCount + " audience snapshot" + (stargazerSnapshotCount == 1 ? "" : "s"));
        }
        if (failedRepositoryCount > 0) {
            parts.Add(failedRepositoryCount + " repo" + (failedRepositoryCount == 1 ? "" : "s") + " failed");
        }

        return parts.Count == 0 ? "GitHub watch auto sync finished." : string.Join(" • ", parts) + ".";
    }

    private static TimeSpan SanitizeWindow(TimeSpan value, TimeSpan fallback) {
        return value <= TimeSpan.Zero ? fallback : value;
    }

    private static GitHubRepositorySnapshotRecord CreateSnapshot(
        GitHubRepositoryWatchRecord watch,
        GitHubRepoInfo repository,
        DateTimeOffset capturedAtUtc) {
        var snapshot = new GitHubRepositorySnapshotRecord(
            GitHubRepositorySnapshotRecord.CreateStableId(watch.Id, capturedAtUtc),
            watch.Id,
            repository.NameWithOwner,
            capturedAtUtc,
            repository.Stars,
            repository.Forks,
            repository.Watchers,
            repository.OpenIssues) {
            Description = repository.Description,
            PrimaryLanguage = repository.Language,
            Url = "https://github.com/" + repository.NameWithOwner,
            PushedAtUtc = repository.PushedAtUtc,
            IsArchived = repository.IsArchived,
            IsFork = repository.IsFork
        };

        return snapshot;
    }

    private static GitHubRepositoryForkSnapshotRecord CreateForkSnapshot(
        string parentRepositoryNameWithOwner,
        GitHubRepositoryForkInfo fork,
        DateTimeOffset capturedAtUtc) {
        return new GitHubRepositoryForkSnapshotRecord(
            GitHubRepositoryForkSnapshotRecord.CreateStableId(parentRepositoryNameWithOwner, fork.RepositoryNameWithOwner, capturedAtUtc),
            parentRepositoryNameWithOwner,
            fork.RepositoryNameWithOwner,
            capturedAtUtc,
            fork.Score,
            fork.Tier,
            fork.Stars,
            fork.Forks,
            fork.Watchers,
            fork.OpenIssues) {
            Url = fork.Url,
            Description = fork.Description,
            PrimaryLanguage = fork.PrimaryLanguage,
            PushedAtUtc = fork.PushedAtUtc,
            UpdatedAtUtc = fork.UpdatedAtUtc,
            CreatedAtUtc = fork.CreatedAtUtc,
            IsArchived = fork.IsArchived,
            ReasonsSummary = fork.Reasons.Count == 0 ? null : string.Join(" | ", fork.Reasons)
        };
    }

    private static GitHubRepositoryStargazerSnapshotRecord CreateStargazerSnapshot(
        string repositoryNameWithOwner,
        GitHubRepositoryStargazerInfo stargazer,
        DateTimeOffset capturedAtUtc) {
        return new GitHubRepositoryStargazerSnapshotRecord(
            GitHubRepositoryStargazerSnapshotRecord.CreateStableId(repositoryNameWithOwner, stargazer.Login, capturedAtUtc),
            repositoryNameWithOwner,
            stargazer.Login,
            capturedAtUtc) {
            StarredAtUtc = stargazer.StarredAtUtc,
            ProfileUrl = stargazer.ProfileUrl,
            AvatarUrl = stargazer.AvatarUrl
        };
    }
}
