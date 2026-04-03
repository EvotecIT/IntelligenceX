using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using IntelligenceX.Cli.GitHub;
using IntelligenceX.Json;
using IntelligenceX.Telemetry.GitHub;
using IntelligenceX.Telemetry.Usage;

namespace IntelligenceX.Cli.Telemetry;

internal static class GitHubTelemetryCliRunner {
    public static async Task<int> RunAsync(string[] args) {
        if (args.Length == 0 || IsHelp(args[0])) {
            PrintHelp();
            return args.Length == 0 ? 1 : 0;
        }

        var command = args[0].Trim().ToLowerInvariant();
        var rest = args.Skip(1).ToArray();
        try {
            if (string.Equals(command, "watches", StringComparison.OrdinalIgnoreCase)) {
                return await RunWatchesAsync(rest).ConfigureAwait(false);
            }
            if (string.Equals(command, "snapshots", StringComparison.OrdinalIgnoreCase)) {
                return RunSnapshots(rest);
            }
            if (string.Equals(command, "forks", StringComparison.OrdinalIgnoreCase)) {
                return await RunForksAsync(rest).ConfigureAwait(false);
            }
            if (string.Equals(command, "stargazers", StringComparison.OrdinalIgnoreCase)) {
                return await RunStargazersAsync(rest).ConfigureAwait(false);
            }
            if (string.Equals(command, "dashboard", StringComparison.OrdinalIgnoreCase)) {
                return RunDashboard(rest);
            }

            return PrintHelpReturn();
        } catch (Exception ex) {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    public static void PrintHelp() {
        Console.WriteLine("Usage:");
        Console.WriteLine("  intelligencex telemetry github watches list [options]");
        Console.WriteLine("  intelligencex telemetry github watches add [options]");
        Console.WriteLine("  intelligencex telemetry github watches sync [options]");
        Console.WriteLine("  intelligencex telemetry github snapshots list [options]");
        Console.WriteLine("  intelligencex telemetry github forks discover [options]");
        Console.WriteLine("  intelligencex telemetry github forks history [options]");
        Console.WriteLine("  intelligencex telemetry github stargazers capture [options]");
        Console.WriteLine("  intelligencex telemetry github stargazers list [options]");
        Console.WriteLine("  intelligencex telemetry github dashboard [options]");
        Console.WriteLine();
        Console.WriteLine("Common options:");
        Console.WriteLine("  --db <path>           SQLite telemetry database path");
        Console.WriteLine("                        Defaults to the runtime telemetry path when omitted.");
        Console.WriteLine("  --json                Emit normalized JSON instead of text");
        Console.WriteLine();
        Console.WriteLine("Watch add options:");
        Console.WriteLine("  --repo <owner/name>   Repository to watch");
        Console.WriteLine("  --display-name <txt>  Optional user-facing label");
        Console.WriteLine("  --category <txt>      Optional grouping label");
        Console.WriteLine("  --notes <txt>         Optional freeform notes");
        Console.WriteLine("  --disabled            Register the watch in disabled state");
        Console.WriteLine();
        Console.WriteLine("Watch sync options:");
        Console.WriteLine("  --repo <owner/name>   Limit sync to one or more repositories");
        Console.WriteLine("  --captured-at <iso>   Override snapshot capture time");
        Console.WriteLine("  --forks               Also discover and persist useful forks for synced repositories");
        Console.WriteLine("  --fork-limit <n>      Maximum forks to record per repository when --forks is used (default: 10)");
        Console.WriteLine("  --stargazers          Also capture and persist recent stargazers for synced repositories");
        Console.WriteLine("  --stargazer-limit <n> Maximum stargazers to record per repository when --stargazers is used (default: 200)");
        Console.WriteLine();
        Console.WriteLine("Snapshot list options:");
        Console.WriteLine("  --repo <owner/name>   Limit snapshots to one repository");
        Console.WriteLine("  --watch-id <id>       Limit snapshots to one watch identifier");
        Console.WriteLine();
        Console.WriteLine("Fork discover options:");
        Console.WriteLine("  --repo <owner/name>   Repository whose forks should be ranked");
        Console.WriteLine("  --limit <n>           Maximum ranked forks to return (default: 20)");
        Console.WriteLine("  --captured-at <iso>   Override recorded capture time");
        Console.WriteLine("  --record              Persist the discovered forks into the telemetry DB");
        Console.WriteLine();
        Console.WriteLine("Fork history options:");
        Console.WriteLine("  --repo <owner/name>   Parent repository whose fork history should be summarized");
        Console.WriteLine("  --limit <n>           Maximum latest changes to return (default: 20)");
        Console.WriteLine();
        Console.WriteLine("Stargazer capture options:");
        Console.WriteLine("  --repo <owner/name>   Repository whose recent stargazers should be captured");
        Console.WriteLine("  --limit <n>           Maximum stargazers to fetch (default: 200)");
        Console.WriteLine("  --captured-at <iso>   Override recorded capture time");
        Console.WriteLine("  --record              Persist the captured stargazers into the telemetry DB");
        Console.WriteLine();
        Console.WriteLine("Stargazer list options:");
        Console.WriteLine("  --repo <owner/name>   Limit snapshots to one repository");
        Console.WriteLine("  --login <user>        Limit snapshots to one stargazer login");
        Console.WriteLine();
        Console.WriteLine("Dashboard options:");
        Console.WriteLine("  --repo <owner/name>   Limit the dashboard to one or more watched repositories");
        Console.WriteLine("  --limit <n>           Maximum daily deltas and fork changes per repository (default: 5)");
    }

    internal static async Task<int> RunSyncAsyncForTest(
        string[] args,
        Func<IReadOnlyList<string>, Task<GitHubRepositoryImpactSummary>> fetchRepositoryImpactAsync,
        Func<string, int, Task<IReadOnlyList<GitHubRepositoryForkInsight>>>? discoverForksAsync = null,
        Func<string, int, Task<IReadOnlyList<GitHubRepositoryStargazerRecord>>>? discoverStargazersAsync = null,
        Func<DateTimeOffset>? utcNow = null) {
        var options = WatchSyncOptions.Parse(args);
        return await RunWatchesSyncCoreAsync(
                options,
                fetchRepositoryImpactAsync,
                discoverForksAsync ?? ((_, _) => Task.FromResult<IReadOnlyList<GitHubRepositoryForkInsight>>(Array.Empty<GitHubRepositoryForkInsight>())),
                discoverStargazersAsync ?? ((_, _) => Task.FromResult<IReadOnlyList<GitHubRepositoryStargazerRecord>>(Array.Empty<GitHubRepositoryStargazerRecord>())),
                utcNow ?? (() => DateTimeOffset.UtcNow))
            .ConfigureAwait(false);
    }

    internal static async Task<int> RunForksDiscoverAsyncForTest(
        string[] args,
        Func<string, int, Task<IReadOnlyList<GitHubRepositoryForkInsight>>> discoverForksAsync) {
        var options = ForkDiscoverOptions.Parse(args);
        return await RunForkDiscoverCoreAsync(options, discoverForksAsync).ConfigureAwait(false);
    }

    internal static async Task<int> RunStargazersCaptureAsyncForTest(
        string[] args,
        Func<string, int, Task<IReadOnlyList<GitHubRepositoryStargazerRecord>>> discoverStargazersAsync) {
        var options = StargazerCaptureOptions.Parse(args);
        return await RunStargazersCaptureCoreAsync(options, discoverStargazersAsync).ConfigureAwait(false);
    }

    private static Task<int> RunWatchesAsync(string[] args) {
        if (args.Length == 0 || IsHelp(args[0])) {
            PrintHelp();
            return Task.FromResult(args.Length == 0 ? 1 : 0);
        }

        var command = args[0].Trim().ToLowerInvariant();
        var rest = args.Skip(1).ToArray();
        return command switch {
            "list" => Task.FromResult(RunWatchesList(rest)),
            "add" => Task.FromResult(RunWatchesAdd(rest)),
            "sync" => RunWatchesSyncAsync(rest),
            _ => Task.FromResult(PrintHelpReturn())
        };
    }

    private static int RunWatchesList(string[] args) {
        var options = WatchListOptions.Parse(args);
        var dbPath = ResolveDatabasePath(options.DatabasePath);
        using var watchStore = new SqliteGitHubRepositoryWatchStore(dbPath);
        using var snapshotStore = new SqliteGitHubRepositorySnapshotStore(dbPath);
        var service = new GitHubRepositoryObservabilityService(watchStore, snapshotStore);
        var watches = service.GetWatches(enabledOnly: options.EnabledOnly);
        if (!string.IsNullOrWhiteSpace(options.RepositoryNameWithOwner)) {
            watches = watches
                .Where(watch => string.Equals(
                    watch.RepositoryNameWithOwner,
                    GitHubRepositoryIdentity.NormalizeNameWithOwner(options.RepositoryNameWithOwner!),
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        if (options.Json) {
            var watchesArray = new JsonArray();
            foreach (var watch in watches) {
                watchesArray.Add(ToJson(watch));
            }

            var payload = new JsonObject()
                .Add("dbPath", dbPath)
                .Add("watches", watchesArray);
            Console.WriteLine(JsonLite.Serialize(JsonValue.From(payload)));
            return 0;
        }

        Console.WriteLine("GitHub repository watches");
        Console.WriteLine("Database: " + dbPath);
        if (watches.Count == 0) {
            Console.WriteLine("Watches: none");
            return 0;
        }

        foreach (var watch in watches) {
            Console.WriteLine($"- [{(watch.Enabled ? "enabled" : "disabled")}] {watch.RepositoryNameWithOwner} ({watch.Id})");
            if (!string.IsNullOrWhiteSpace(watch.DisplayName)) {
                Console.WriteLine($"  name: {watch.DisplayName}");
            }
            if (!string.IsNullOrWhiteSpace(watch.Category)) {
                Console.WriteLine($"  category: {watch.Category}");
            }
            if (!string.IsNullOrWhiteSpace(watch.Notes)) {
                Console.WriteLine($"  notes: {watch.Notes}");
            }
        }

        return 0;
    }

    private static int RunWatchesAdd(string[] args) {
        var options = WatchAddOptions.Parse(args);
        var dbPath = ResolveDatabasePath(options.DatabasePath);
        using var watchStore = new SqliteGitHubRepositoryWatchStore(dbPath);
        using var snapshotStore = new SqliteGitHubRepositorySnapshotStore(dbPath);
        var service = new GitHubRepositoryObservabilityService(watchStore, snapshotStore);
        var watch = service.EnsureWatch(
            options.RepositoryNameWithOwner!,
            displayName: NormalizeOptional(options.DisplayName),
            category: NormalizeOptional(options.Category),
            notes: NormalizeOptional(options.Notes),
            enabled: !options.Disabled);

        if (options.Json) {
            var payload = new JsonObject()
                .Add("dbPath", dbPath)
                .Add("watch", ToJson(watch));
            Console.WriteLine(JsonLite.Serialize(JsonValue.From(payload)));
            return 0;
        }

        Console.WriteLine($"Registered GitHub watch {watch.Id} for {watch.RepositoryNameWithOwner}.");
        return 0;
    }

    private static Task<int> RunWatchesSyncAsync(string[] args) {
        var options = WatchSyncOptions.Parse(args);
        return RunWatchesSyncCoreAsync(
            options,
            owners => new GitHubRepositoryImpactClient().GetRepositoryImpactAsync(owners),
            (repositoryNameWithOwner, limit) => new GitHubRepositoryForkDiscoveryClient().GetUsefulForksAsync(repositoryNameWithOwner, limit),
            (repositoryNameWithOwner, limit) => new GitHubRepositoryStargazerDiscoveryClient().GetStargazersAsync(repositoryNameWithOwner, limit),
            () => DateTimeOffset.UtcNow);
    }

    private static async Task<int> RunWatchesSyncCoreAsync(
        WatchSyncOptions options,
        Func<IReadOnlyList<string>, Task<GitHubRepositoryImpactSummary>> fetchRepositoryImpactAsync,
        Func<string, int, Task<IReadOnlyList<GitHubRepositoryForkInsight>>> discoverForksAsync,
        Func<string, int, Task<IReadOnlyList<GitHubRepositoryStargazerRecord>>> discoverStargazersAsync,
        Func<DateTimeOffset> utcNow) {
        var dbPath = ResolveDatabasePath(options.DatabasePath);
        using var watchStore = new SqliteGitHubRepositoryWatchStore(dbPath);
        using var snapshotStore = new SqliteGitHubRepositorySnapshotStore(dbPath);
        using var forkStore = new SqliteGitHubRepositoryForkSnapshotStore(dbPath);
        using var stargazerStore = new SqliteGitHubRepositoryStargazerSnapshotStore(dbPath);
        var service = new GitHubRepositoryObservabilityService(watchStore, snapshotStore);
        var watches = service.GetWatches(enabledOnly: true);
        if (options.Repositories.Count > 0) {
            var filter = options.Repositories
                .Select(GitHubRepositoryIdentity.NormalizeNameWithOwner)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            watches = watches
                .Where(watch => filter.Contains(watch.RepositoryNameWithOwner))
                .ToArray();
        }

        if (watches.Count == 0) {
            throw new InvalidOperationException("No enabled GitHub watches matched the requested sync scope.");
        }

        var repositoriesByOwner = new Dictionary<string, GitHubRepositoryImpactRepository>(StringComparer.OrdinalIgnoreCase);
        foreach (var ownerGroup in watches
                     .Select(static watch => watch.Owner)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(static owner => owner, StringComparer.OrdinalIgnoreCase)) {
            var summary = await fetchRepositoryImpactAsync(new[] { ownerGroup }).ConfigureAwait(false);
            foreach (var repository in summary.Owners.SelectMany(static owner => owner.Repositories)) {
                repositoriesByOwner[repository.NameWithOwner] = repository;
            }
        }

        var capturedAtUtc = options.CapturedAtUtc ?? utcNow();
        var results = new List<(GitHubRepositoryWatchRecord Watch, GitHubRepositorySnapshotRecord Snapshot, GitHubRepositorySnapshotDelta Delta)>();
        var forkResults = new List<(string RepositoryNameWithOwner, GitHubRepositoryForkSnapshotRecord[] Snapshots)>();
        var stargazerResults = new List<(string RepositoryNameWithOwner, GitHubRepositoryStargazerSnapshotRecord[] Snapshots)>();
        foreach (var watch in watches.OrderBy(static watch => watch.RepositoryNameWithOwner, StringComparer.OrdinalIgnoreCase)) {
            if (!repositoriesByOwner.TryGetValue(watch.RepositoryNameWithOwner, out var repository)) {
                continue;
            }

            var snapshot = GitHubRepositoryObservabilityMapper.CreateSnapshot(repository, capturedAtUtc, watch.Id);
            var delta = service.RecordSnapshot(snapshot);
            results.Add((watch, snapshot, delta));

            if (options.IncludeForks) {
                var insights = await discoverForksAsync(watch.RepositoryNameWithOwner, options.ForkLimit).ConfigureAwait(false);
                var recordedForkSnapshots = new List<GitHubRepositoryForkSnapshotRecord>(insights.Count);
                foreach (var insight in insights) {
                    var forkSnapshot = GitHubRepositoryObservabilityMapper.CreateForkSnapshot(
                        watch.RepositoryNameWithOwner,
                        insight,
                        capturedAtUtc);
                    forkStore.Upsert(forkSnapshot);
                    recordedForkSnapshots.Add(forkSnapshot);
                }

                forkStore.MarkParentRepositoryCaptured(watch.RepositoryNameWithOwner, capturedAtUtc);
                forkResults.Add((watch.RepositoryNameWithOwner, recordedForkSnapshots.ToArray()));
            }

            if (!options.IncludeStargazers) {
                continue;
            }

            var stargazers = await discoverStargazersAsync(watch.RepositoryNameWithOwner, options.StargazerLimit).ConfigureAwait(false);
            var recordedStargazerSnapshots = new List<GitHubRepositoryStargazerSnapshotRecord>(stargazers.Count);
            foreach (var stargazer in stargazers) {
                var stargazerSnapshot = GitHubRepositoryObservabilityMapper.CreateStargazerSnapshot(
                    watch.RepositoryNameWithOwner,
                    stargazer,
                    capturedAtUtc);
                stargazerStore.Upsert(stargazerSnapshot);
                recordedStargazerSnapshots.Add(stargazerSnapshot);
            }

            stargazerStore.MarkRepositoryCaptured(watch.RepositoryNameWithOwner, capturedAtUtc);
            stargazerResults.Add((watch.RepositoryNameWithOwner, recordedStargazerSnapshots.ToArray()));
        }

        if (results.Count == 0) {
            throw new InvalidOperationException("The requested GitHub watches did not resolve to public repositories.");
        }

        if (options.Json) {
            var snapshotsArray = new JsonArray();
            foreach (var result in results) {
                snapshotsArray.Add(new JsonObject()
                    .Add("watch", ToJson(result.Watch))
                    .Add("snapshot", ToJson(result.Snapshot))
                    .Add("delta", ToJson(result.Delta)));
            }

            var payload = new JsonObject()
                .Add("dbPath", dbPath)
                .Add("capturedAtUtc", capturedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))
                .Add("syncedCount", results.Count)
                .Add("forksIncluded", options.IncludeForks)
                .Add("stargazersIncluded", options.IncludeStargazers)
                .Add("snapshots", snapshotsArray);
            if (options.IncludeForks) {
                var forksArray = new JsonArray();
                foreach (var forkResult in forkResults) {
                    var recordedArray = new JsonArray();
                    foreach (var snapshot in forkResult.Snapshots) {
                        recordedArray.Add(ToJson(snapshot));
                    }

                    forksArray.Add(new JsonObject()
                        .Add("repositoryNameWithOwner", forkResult.RepositoryNameWithOwner)
                        .Add("recordedCount", forkResult.Snapshots.Length)
                        .Add("snapshots", recordedArray));
                }

                payload.Add("forks", forksArray);
            }
            if (options.IncludeStargazers) {
                var stargazersArray = new JsonArray();
                foreach (var stargazerResult in stargazerResults) {
                    var recordedArray = new JsonArray();
                    foreach (var snapshot in stargazerResult.Snapshots) {
                        recordedArray.Add(ToJson(snapshot));
                    }

                    stargazersArray.Add(new JsonObject()
                        .Add("repositoryNameWithOwner", stargazerResult.RepositoryNameWithOwner)
                        .Add("recordedCount", stargazerResult.Snapshots.Length)
                        .Add("snapshots", recordedArray));
                }

                payload.Add("stargazers", stargazersArray);
            }
            Console.WriteLine(JsonLite.Serialize(JsonValue.From(payload)));
            return 0;
        }

        Console.WriteLine($"Synced {results.Count} GitHub watch{(results.Count == 1 ? string.Empty : "es")}.");
        foreach (var result in results) {
            Console.WriteLine($"- {result.Snapshot.RepositoryNameWithOwner} | stars {result.Snapshot.Stars} ({FormatSigned(result.Delta.StarDelta)}) | forks {result.Snapshot.Forks} ({FormatSigned(result.Delta.ForkDelta)}) | watchers {result.Snapshot.Watchers} ({FormatSigned(result.Delta.WatcherDelta)})");
        }
        if (options.IncludeForks) {
            foreach (var forkResult in forkResults) {
                Console.WriteLine($"  useful forks recorded for {forkResult.RepositoryNameWithOwner}: {forkResult.Snapshots.Length}");
            }
        }
        if (options.IncludeStargazers) {
            foreach (var stargazerResult in stargazerResults) {
                Console.WriteLine($"  stargazer snapshots recorded for {stargazerResult.RepositoryNameWithOwner}: {stargazerResult.Snapshots.Length}");
            }
        }

        return 0;
    }

    private static int RunSnapshots(string[] args) {
        if (args.Length == 0 || IsHelp(args[0])) {
            PrintHelp();
            return args.Length == 0 ? 1 : 0;
        }

        var command = args[0].Trim().ToLowerInvariant();
        var rest = args.Skip(1).ToArray();
        return command switch {
            "list" => RunSnapshotsList(rest),
            _ => PrintHelpReturn()
        };
    }

    private static Task<int> RunForksAsync(string[] args) {
        if (args.Length == 0 || IsHelp(args[0])) {
            PrintHelp();
            return Task.FromResult(args.Length == 0 ? 1 : 0);
        }

        var command = args[0].Trim().ToLowerInvariant();
        var rest = args.Skip(1).ToArray();
        return command switch {
            "discover" => RunForksDiscoverAsync(rest),
            "history" => Task.FromResult(RunForksHistory(rest)),
            _ => Task.FromResult(PrintHelpReturn())
        };
    }

    private static Task<int> RunStargazersAsync(string[] args) {
        if (args.Length == 0 || IsHelp(args[0])) {
            PrintHelp();
            return Task.FromResult(args.Length == 0 ? 1 : 0);
        }

        var command = args[0].Trim().ToLowerInvariant();
        var rest = args.Skip(1).ToArray();
        return command switch {
            "capture" => RunStargazersCaptureAsync(rest),
            "list" => Task.FromResult(RunStargazersList(rest)),
            _ => Task.FromResult(PrintHelpReturn())
        };
    }

    private static int RunSnapshotsList(string[] args) {
        var options = SnapshotListOptions.Parse(args);
        var dbPath = ResolveDatabasePath(options.DatabasePath);
        using var watchStore = new SqliteGitHubRepositoryWatchStore(dbPath);
        using var snapshotStore = new SqliteGitHubRepositorySnapshotStore(dbPath);
        var service = new GitHubRepositoryObservabilityService(watchStore, snapshotStore);

        string? watchId = NormalizeOptional(options.WatchId);
        if (!string.IsNullOrWhiteSpace(options.RepositoryNameWithOwner)) {
            if (!watchStore.TryGetByRepository(options.RepositoryNameWithOwner!, out var watch)) {
                throw new InvalidOperationException("No GitHub watch is registered for " + options.RepositoryNameWithOwner + ".");
            }
            watchId = watch.Id;
        }

        var snapshots = service.GetSnapshots(watchId);
        if (options.Json) {
            var snapshotsArray = new JsonArray();
            foreach (var snapshot in snapshots) {
                snapshotsArray.Add(ToJson(snapshot));
            }

            var payload = new JsonObject()
                .Add("dbPath", dbPath)
                .Add("snapshots", snapshotsArray);
            Console.WriteLine(JsonLite.Serialize(JsonValue.From(payload)));
            return 0;
        }

        Console.WriteLine("GitHub repository snapshots");
        Console.WriteLine("Database: " + dbPath);
        if (snapshots.Count == 0) {
            Console.WriteLine("Snapshots: none");
            return 0;
        }

        foreach (var snapshot in snapshots) {
            Console.WriteLine($"- {snapshot.CapturedAtUtc:yyyy-MM-dd HH:mm:ss}Z | {snapshot.RepositoryNameWithOwner} | stars={snapshot.Stars} forks={snapshot.Forks} watchers={snapshot.Watchers} issues={snapshot.OpenIssues}");
        }

        return 0;
    }

    private static int RunDashboard(string[] args) {
        var options = DashboardOptions.Parse(args);
        var dbPath = ResolveDatabasePath(options.DatabasePath);
        using var watchStore = new SqliteGitHubRepositoryWatchStore(dbPath);
        using var snapshotStore = new SqliteGitHubRepositorySnapshotStore(dbPath);
        using var forkStore = new SqliteGitHubRepositoryForkSnapshotStore(dbPath);
        var service = new GitHubRepositoryObservabilityService(watchStore, snapshotStore);
        var watches = service.GetWatches(enabledOnly: true);
        if (options.Repositories.Count > 0) {
            var filter = options.Repositories
                .Select(GitHubRepositoryIdentity.NormalizeNameWithOwner)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            watches = watches
                .Where(watch => filter.Contains(watch.RepositoryNameWithOwner))
                .ToArray();
        }

        var entries = new List<(GitHubRepositoryWatchRecord Watch, int SnapshotCount, GitHubRepositorySnapshotRecord? LatestSnapshot, GitHubRepositorySnapshotDelta? LatestDelta, GitHubRepositorySnapshotDelta[] DailyDeltas, GitHubRepositoryForkChange[] ForkChanges)>(watches.Count);
        foreach (var watch in watches.OrderBy(static watch => watch.RepositoryNameWithOwner, StringComparer.OrdinalIgnoreCase)) {
            var snapshots = service.GetSnapshots(watch.Id);
            var latestSnapshot = snapshots.Count > 0 ? snapshots[snapshots.Count - 1] : null;
            var latestDelta = latestSnapshot is null
                ? null
                : GitHubRepositorySnapshotAnalytics.CreateDelta(
                    snapshots.Count > 1 ? snapshots[snapshots.Count - 2] : null,
                    latestSnapshot);
            var dailyDeltas = GitHubRepositorySnapshotAnalytics.BuildDailyDeltas(snapshots)
                .OrderByDescending(static delta => delta.CurrentCapturedAtUtc)
                .Take(options.Limit)
                .ToArray();
            var forkSnapshots = forkStore.GetByParentRepository(watch.RepositoryNameWithOwner);
            var latestForkCaptureAtUtc = forkSnapshots.Count == 0
                ? (DateTimeOffset?)null
                : forkSnapshots.Max(static snapshot => snapshot.CapturedAtUtc);
            var forkChanges = GitHubRepositoryForkHistoryAnalytics.BuildLatestChanges(forkSnapshots)
                .Where(change => latestForkCaptureAtUtc is null || change.CurrentCapturedAtUtc == latestForkCaptureAtUtc.Value)
                .OrderBy(static change => GetForkDashboardPriority(change.Status))
                .ThenByDescending(static change => change.CurrentCapturedAtUtc)
                .ThenByDescending(static change => change.Score)
                .ThenBy(static change => change.ForkRepositoryNameWithOwner, StringComparer.OrdinalIgnoreCase)
                .Take(options.Limit)
                .ToArray();
            entries.Add((watch, snapshots.Count, latestSnapshot, latestDelta, dailyDeltas, forkChanges));
        }

        if (options.Json) {
            var repositoriesArray = new JsonArray();
            foreach (var entry in entries) {
                var dailyDeltasArray = new JsonArray();
                foreach (var dailyDelta in entry.DailyDeltas) {
                    dailyDeltasArray.Add(ToJson(dailyDelta));
                }

                var forkChangesArray = new JsonArray();
                foreach (var forkChange in entry.ForkChanges) {
                    forkChangesArray.Add(ToJson(forkChange));
                }

                var repository = new JsonObject()
                    .Add("watch", ToJson(entry.Watch))
                    .Add("snapshotCount", entry.SnapshotCount)
                    .Add("dailyDeltas", dailyDeltasArray)
                    .Add("forkChanges", forkChangesArray);
                if (entry.LatestSnapshot is not null) {
                    repository.Add("latestSnapshot", ToJson(entry.LatestSnapshot));
                } else {
                    repository.Add("latestSnapshot", (string?)null);
                }
                if (entry.LatestDelta is not null) {
                    repository.Add("latestDelta", ToJson(entry.LatestDelta));
                } else {
                    repository.Add("latestDelta", (string?)null);
                }
                repositoriesArray.Add(repository);
            }

            var payload = new JsonObject()
                .Add("dbPath", dbPath)
                .Add("generatedAtUtc", DateTimeOffset.UtcNow.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))
                .Add("limit", options.Limit)
                .Add("repositories", repositoriesArray);
            Console.WriteLine(JsonLite.Serialize(JsonValue.From(payload)));
            return 0;
        }

        Console.WriteLine("GitHub observability dashboard");
        Console.WriteLine("Database: " + dbPath);
        if (entries.Count == 0) {
            Console.WriteLine("Repositories: none");
            return 0;
        }

        foreach (var entry in entries) {
            if (entry.LatestSnapshot is null || entry.LatestDelta is null) {
                Console.WriteLine($"- {entry.Watch.RepositoryNameWithOwner} | no snapshots yet");
                continue;
            }

            Console.WriteLine($"- {entry.Watch.RepositoryNameWithOwner} | stars {entry.LatestSnapshot.Stars} ({FormatSigned(entry.LatestDelta.StarDelta)}) | forks {entry.LatestSnapshot.Forks} ({FormatSigned(entry.LatestDelta.ForkDelta)}) | watchers {entry.LatestSnapshot.Watchers} ({FormatSigned(entry.LatestDelta.WatcherDelta)})");
            if (entry.DailyDeltas.Length > 0) {
                var latestDaily = entry.DailyDeltas[0];
                Console.WriteLine($"  daily: {latestDaily.CurrentCapturedAtUtc:yyyy-MM-dd} | stars {FormatSigned(latestDaily.StarDelta)} | forks {FormatSigned(latestDaily.ForkDelta)} | watchers {FormatSigned(latestDaily.WatcherDelta)}");
            }
            if (entry.ForkChanges.Length > 0) {
                var topForks = string.Join(", ", entry.ForkChanges.Select(static change =>
                    change.ForkRepositoryNameWithOwner + " " + change.Status + " (" + FormatSigned(change.ScoreDelta) + ")"));
                Console.WriteLine("  forks: " + topForks);
            }
        }

        return 0;
    }

    private static Task<int> RunForksDiscoverAsync(string[] args) {
        var options = ForkDiscoverOptions.Parse(args);
        return RunForkDiscoverCoreAsync(
            options,
            (repositoryNameWithOwner, limit) => new GitHubRepositoryForkDiscoveryClient().GetUsefulForksAsync(repositoryNameWithOwner, limit));
    }

    private static async Task<int> RunForkDiscoverCoreAsync(
        ForkDiscoverOptions options,
        Func<string, int, Task<IReadOnlyList<GitHubRepositoryForkInsight>>> discoverForksAsync) {
        var insights = await discoverForksAsync(options.RepositoryNameWithOwner!, options.Limit).ConfigureAwait(false);
        var capturedAtUtc = options.CapturedAtUtc ?? DateTimeOffset.UtcNow;
        var recordedSnapshots = Array.Empty<GitHubRepositoryForkSnapshotRecord>();
        if (options.Record) {
            var dbPath = ResolveDatabasePath(options.DatabasePath);
            using var store = new SqliteGitHubRepositoryForkSnapshotStore(dbPath);
            var snapshots = new List<GitHubRepositoryForkSnapshotRecord>(insights.Count);
            foreach (var insight in insights) {
                var snapshot = GitHubRepositoryObservabilityMapper.CreateForkSnapshot(
                    options.RepositoryNameWithOwner!,
                    insight,
                    capturedAtUtc);
                store.Upsert(snapshot);
                snapshots.Add(snapshot);
            }

            recordedSnapshots = snapshots.ToArray();
        }

        if (options.Json) {
            var forksArray = new JsonArray();
            foreach (var insight in insights) {
                forksArray.Add(ToJson(insight));
            }

            var payload = new JsonObject()
                .Add("repositoryNameWithOwner", options.RepositoryNameWithOwner)
                .Add("limit", options.Limit)
                .Add("recorded", options.Record)
                .Add("capturedAtUtc", options.Record ? capturedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) : null)
                .Add("forks", forksArray);
            if (options.Record) {
                var recordedArray = new JsonArray();
                foreach (var snapshot in recordedSnapshots) {
                    recordedArray.Add(ToJson(snapshot));
                }
                payload.Add("recordedSnapshots", recordedArray);
            }
            Console.WriteLine(JsonLite.Serialize(JsonValue.From(payload)));
            return 0;
        }

        Console.WriteLine("Useful forks for " + options.RepositoryNameWithOwner);
        if (insights.Count == 0) {
            Console.WriteLine("Forks: none");
            return 0;
        }

        foreach (var insight in insights) {
            Console.WriteLine($"- {insight.Fork.RepositoryNameWithOwner} | score={insight.Score.ToString("0.##", CultureInfo.InvariantCulture)} | tier={insight.Tier} | stars={insight.Fork.Stars} watchers={insight.Fork.Watchers} forks={insight.Fork.Forks}");
        }

        if (options.Record) {
            Console.WriteLine("Recorded " + recordedSnapshots.Length.ToString(CultureInfo.InvariantCulture) + " fork snapshot(s).");
        }

        return 0;
    }

    private static int RunForksHistory(string[] args) {
        var options = ForkHistoryOptions.Parse(args);
        var dbPath = ResolveDatabasePath(options.DatabasePath);
        using var store = new SqliteGitHubRepositoryForkSnapshotStore(dbPath);
        var changes = GitHubRepositoryForkHistoryAnalytics.BuildLatestChanges(
            store.GetByParentRepository(options.RepositoryNameWithOwner!))
            .Take(options.Limit)
            .ToArray();

        if (options.Json) {
            var changesArray = new JsonArray();
            foreach (var change in changes) {
                changesArray.Add(ToJson(change));
            }

            var payload = new JsonObject()
                .Add("dbPath", dbPath)
                .Add("repositoryNameWithOwner", options.RepositoryNameWithOwner)
                .Add("changes", changesArray);
            Console.WriteLine(JsonLite.Serialize(JsonValue.From(payload)));
            return 0;
        }

        Console.WriteLine("Fork history for " + options.RepositoryNameWithOwner);
        if (changes.Length == 0) {
            Console.WriteLine("Changes: none");
            return 0;
        }

        foreach (var change in changes) {
            Console.WriteLine($"- {change.ForkRepositoryNameWithOwner} | status={change.Status} | score={change.Score.ToString("0.##", CultureInfo.InvariantCulture)} ({FormatSigned(change.ScoreDelta)}) | stars={change.Stars} ({FormatSigned(change.StarDelta)}) | watchers={change.Watchers} ({FormatSigned(change.WatcherDelta)})");
        }

        return 0;
    }

    private static Task<int> RunStargazersCaptureAsync(string[] args) {
        var options = StargazerCaptureOptions.Parse(args);
        return RunStargazersCaptureCoreAsync(
            options,
            (repositoryNameWithOwner, limit) => new GitHubRepositoryStargazerDiscoveryClient().GetStargazersAsync(repositoryNameWithOwner, limit));
    }

    private static async Task<int> RunStargazersCaptureCoreAsync(
        StargazerCaptureOptions options,
        Func<string, int, Task<IReadOnlyList<GitHubRepositoryStargazerRecord>>> discoverStargazersAsync) {
        var stargazers = await discoverStargazersAsync(options.RepositoryNameWithOwner!, options.Limit).ConfigureAwait(false);
        var capturedAtUtc = options.CapturedAtUtc ?? DateTimeOffset.UtcNow;
        var recordedSnapshots = Array.Empty<GitHubRepositoryStargazerSnapshotRecord>();
        if (options.Record) {
            var dbPath = ResolveDatabasePath(options.DatabasePath);
            using var store = new SqliteGitHubRepositoryStargazerSnapshotStore(dbPath);
            var snapshots = new List<GitHubRepositoryStargazerSnapshotRecord>(stargazers.Count);
            foreach (var stargazer in stargazers) {
                var snapshot = GitHubRepositoryObservabilityMapper.CreateStargazerSnapshot(
                    options.RepositoryNameWithOwner!,
                    stargazer,
                    capturedAtUtc);
                store.Upsert(snapshot);
                snapshots.Add(snapshot);
            }

            recordedSnapshots = snapshots.ToArray();
        }

        if (options.Json) {
            var stargazersArray = new JsonArray();
            foreach (var stargazer in stargazers) {
                stargazersArray.Add(ToJson(stargazer));
            }

            var payload = new JsonObject()
                .Add("repositoryNameWithOwner", options.RepositoryNameWithOwner)
                .Add("limit", options.Limit)
                .Add("recorded", options.Record)
                .Add("capturedAtUtc", options.Record ? capturedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) : null)
                .Add("stargazers", stargazersArray);
            if (options.Record) {
                var recordedArray = new JsonArray();
                foreach (var snapshot in recordedSnapshots) {
                    recordedArray.Add(ToJson(snapshot));
                }
                payload.Add("recordedSnapshots", recordedArray);
            }
            Console.WriteLine(JsonLite.Serialize(JsonValue.From(payload)));
            return 0;
        }

        Console.WriteLine("Recent stargazers for " + options.RepositoryNameWithOwner);
        if (stargazers.Count == 0) {
            Console.WriteLine("Stargazers: none");
            return 0;
        }

        foreach (var stargazer in stargazers) {
            Console.WriteLine($"- {stargazer.Login} | starredAt={NormalizeOptional(stargazer.StarredAt) ?? "unknown"}");
        }

        if (options.Record) {
            Console.WriteLine("Recorded " + recordedSnapshots.Length.ToString(CultureInfo.InvariantCulture) + " stargazer snapshot(s).");
        }

        return 0;
    }

    private static int RunStargazersList(string[] args) {
        var options = StargazerListOptions.Parse(args);
        var dbPath = ResolveDatabasePath(options.DatabasePath);
        using var store = new SqliteGitHubRepositoryStargazerSnapshotStore(dbPath);

        IReadOnlyList<GitHubRepositoryStargazerSnapshotRecord> snapshots;
        if (!string.IsNullOrWhiteSpace(options.RepositoryNameWithOwner)) {
            snapshots = store.GetByRepository(options.RepositoryNameWithOwner!);
        } else if (!string.IsNullOrWhiteSpace(options.StargazerLogin)) {
            snapshots = store.GetByStargazer(options.StargazerLogin!);
        } else {
            snapshots = store.GetAll();
        }

        if (options.Json) {
            var snapshotsArray = new JsonArray();
            foreach (var snapshot in snapshots) {
                snapshotsArray.Add(ToJson(snapshot));
            }

            var payload = new JsonObject()
                .Add("dbPath", dbPath)
                .Add("snapshots", snapshotsArray);
            Console.WriteLine(JsonLite.Serialize(JsonValue.From(payload)));
            return 0;
        }

        Console.WriteLine("GitHub stargazer snapshots");
        Console.WriteLine("Database: " + dbPath);
        if (snapshots.Count == 0) {
            Console.WriteLine("Snapshots: none");
            return 0;
        }

        foreach (var snapshot in snapshots) {
            Console.WriteLine($"- {snapshot.CapturedAtUtc:yyyy-MM-dd HH:mm:ss}Z | {snapshot.RepositoryNameWithOwner} | {snapshot.StargazerLogin}");
        }

        return 0;
    }

    private static JsonObject ToJson(GitHubRepositoryWatchRecord watch) {
        return new JsonObject()
            .Add("id", watch.Id)
            .Add("repositoryNameWithOwner", watch.RepositoryNameWithOwner)
            .Add("owner", watch.Owner)
            .Add("repository", watch.Repository)
            .Add("displayName", watch.DisplayName)
            .Add("category", watch.Category)
            .Add("notes", watch.Notes)
            .Add("enabled", watch.Enabled)
            .Add("createdAtUtc", watch.CreatedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
    }

    private static JsonObject ToJson(GitHubRepositorySnapshotRecord snapshot) {
        return new JsonObject()
            .Add("id", snapshot.Id)
            .Add("watchId", snapshot.WatchId)
            .Add("repositoryNameWithOwner", snapshot.RepositoryNameWithOwner)
            .Add("capturedAtUtc", snapshot.CapturedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))
            .Add("stars", snapshot.Stars)
            .Add("forks", snapshot.Forks)
            .Add("watchers", snapshot.Watchers)
            .Add("openIssues", snapshot.OpenIssues)
            .Add("description", snapshot.Description)
            .Add("primaryLanguage", snapshot.PrimaryLanguage)
            .Add("url", snapshot.Url)
            .Add("pushedAtUtc", snapshot.PushedAtUtc?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))
            .Add("isArchived", snapshot.IsArchived)
            .Add("isFork", snapshot.IsFork);
    }

    private static JsonObject ToJson(GitHubRepositorySnapshotDelta delta) {
        return new JsonObject()
            .Add("repositoryNameWithOwner", delta.RepositoryNameWithOwner)
            .Add("previousCapturedAtUtc", delta.PreviousCapturedAtUtc?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))
            .Add("currentCapturedAtUtc", delta.CurrentCapturedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))
            .Add("stars", delta.Stars)
            .Add("forks", delta.Forks)
            .Add("watchers", delta.Watchers)
            .Add("openIssues", delta.OpenIssues)
            .Add("starDelta", delta.StarDelta)
            .Add("forkDelta", delta.ForkDelta)
            .Add("watcherDelta", delta.WatcherDelta)
            .Add("openIssueDelta", delta.OpenIssueDelta);
    }

    private static JsonObject ToJson(GitHubRepositoryForkInsight insight) {
        var reasons = new JsonArray();
        foreach (var reason in insight.Reasons) {
            reasons.Add(reason);
        }

        return new JsonObject()
            .Add("repositoryNameWithOwner", insight.Fork.RepositoryNameWithOwner)
            .Add("url", insight.Fork.Url)
            .Add("score", insight.Score)
            .Add("tier", insight.Tier)
            .Add("stars", insight.Fork.Stars)
            .Add("forks", insight.Fork.Forks)
            .Add("watchers", insight.Fork.Watchers)
            .Add("openIssues", insight.Fork.OpenIssues)
            .Add("description", insight.Fork.Description)
            .Add("primaryLanguage", insight.Fork.PrimaryLanguage)
            .Add("pushedAt", insight.Fork.PushedAt)
            .Add("updatedAt", insight.Fork.UpdatedAt)
            .Add("createdAt", insight.Fork.CreatedAt)
            .Add("isArchived", insight.Fork.IsArchived)
            .Add("reasons", reasons);
    }

    private static JsonObject ToJson(GitHubRepositoryForkSnapshotRecord snapshot) {
        return new JsonObject()
            .Add("id", snapshot.Id)
            .Add("parentRepositoryNameWithOwner", snapshot.ParentRepositoryNameWithOwner)
            .Add("forkRepositoryNameWithOwner", snapshot.ForkRepositoryNameWithOwner)
            .Add("capturedAtUtc", snapshot.CapturedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))
            .Add("score", snapshot.Score)
            .Add("tier", snapshot.Tier)
            .Add("stars", snapshot.Stars)
            .Add("forks", snapshot.Forks)
            .Add("watchers", snapshot.Watchers)
            .Add("openIssues", snapshot.OpenIssues)
            .Add("url", snapshot.Url)
            .Add("description", snapshot.Description)
            .Add("primaryLanguage", snapshot.PrimaryLanguage)
            .Add("pushedAtUtc", snapshot.PushedAtUtc?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))
            .Add("updatedAtUtc", snapshot.UpdatedAtUtc?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))
            .Add("createdAtUtc", snapshot.CreatedAtUtc?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))
            .Add("isArchived", snapshot.IsArchived)
            .Add("reasonsSummary", snapshot.ReasonsSummary);
    }

    private static JsonObject ToJson(GitHubRepositoryStargazerRecord stargazer) {
        return new JsonObject()
            .Add("login", stargazer.Login)
            .Add("profileUrl", stargazer.ProfileUrl)
            .Add("avatarUrl", stargazer.AvatarUrl)
            .Add("starredAt", stargazer.StarredAt);
    }

    private static JsonObject ToJson(GitHubRepositoryStargazerSnapshotRecord snapshot) {
        return new JsonObject()
            .Add("id", snapshot.Id)
            .Add("repositoryNameWithOwner", snapshot.RepositoryNameWithOwner)
            .Add("stargazerLogin", snapshot.StargazerLogin)
            .Add("capturedAtUtc", snapshot.CapturedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))
            .Add("starredAtUtc", snapshot.StarredAtUtc?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))
            .Add("profileUrl", snapshot.ProfileUrl)
            .Add("avatarUrl", snapshot.AvatarUrl);
    }

    private static JsonObject ToJson(GitHubRepositoryForkChange change) {
        return new JsonObject()
            .Add("parentRepositoryNameWithOwner", change.ParentRepositoryNameWithOwner)
            .Add("forkRepositoryNameWithOwner", change.ForkRepositoryNameWithOwner)
            .Add("previousCapturedAtUtc", change.PreviousCapturedAtUtc?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))
            .Add("currentCapturedAtUtc", change.CurrentCapturedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))
            .Add("score", change.Score)
            .Add("scoreDelta", change.ScoreDelta)
            .Add("stars", change.Stars)
            .Add("starDelta", change.StarDelta)
            .Add("watchers", change.Watchers)
            .Add("watcherDelta", change.WatcherDelta)
            .Add("tier", change.Tier)
            .Add("status", change.Status);
    }

    private static string ResolveDatabasePath(string? explicitPath) {
        var resolved = UsageTelemetryPathResolver.ResolveDatabasePath(explicitPath, enabledByDefault: true);
        if (string.IsNullOrWhiteSpace(resolved)) {
            throw new InvalidOperationException("Telemetry database path is unavailable.");
        }

        return resolved!;
    }

    private static string? NormalizeOptional(string? value) {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string FormatSigned(int value) {
        return value >= 0 ? "+" + value.ToString(CultureInfo.InvariantCulture) : value.ToString(CultureInfo.InvariantCulture);
    }

    private static string FormatSigned(double value) {
        return value >= 0d
            ? "+" + value.ToString("0.##", CultureInfo.InvariantCulture)
            : value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static int GetForkDashboardPriority(string? status) {
        return status?.Trim().ToLowerInvariant() switch {
            "new" => 0,
            "rising" => 1,
            "archived" => 2,
            "cooling" => 3,
            "steady" => 4,
            _ => 5
        };
    }

    private static bool IsHelp(string value) {
        return value.Equals("help", StringComparison.OrdinalIgnoreCase)
               || value.Equals("-h", StringComparison.OrdinalIgnoreCase)
               || value.Equals("--help", StringComparison.OrdinalIgnoreCase);
    }

    private static int PrintHelpReturn() {
        PrintHelp();
        return 1;
    }

    private class CommonOptions {
        public string? DatabasePath { get; set; }
        public bool Json { get; set; }
    }

    private sealed class WatchListOptions : CommonOptions {
        public bool EnabledOnly { get; set; }
        public string? RepositoryNameWithOwner { get; set; }

        public static WatchListOptions Parse(string[] args) {
            var options = new WatchListOptions();
            for (var i = 0; i < args.Length; i++) {
                switch (args[i]) {
                    case "--db":
                        options.DatabasePath = RequireValue(args, ref i, "--db");
                        break;
                    case "--repo":
                        options.RepositoryNameWithOwner = RequireValue(args, ref i, "--repo");
                        break;
                    case "--enabled-only":
                        options.EnabledOnly = true;
                        break;
                    case "--json":
                        options.Json = true;
                        break;
                    default:
                        throw new InvalidOperationException("Unknown option for telemetry github watches list: " + args[i]);
                }
            }

            return options;
        }
    }

    private sealed class WatchAddOptions : CommonOptions {
        public string? RepositoryNameWithOwner { get; set; }
        public string? DisplayName { get; set; }
        public string? Category { get; set; }
        public string? Notes { get; set; }
        public bool Disabled { get; set; }

        public static WatchAddOptions Parse(string[] args) {
            var options = new WatchAddOptions();
            for (var i = 0; i < args.Length; i++) {
                switch (args[i]) {
                    case "--db":
                        options.DatabasePath = RequireValue(args, ref i, "--db");
                        break;
                    case "--repo":
                        options.RepositoryNameWithOwner = RequireValue(args, ref i, "--repo");
                        break;
                    case "--display-name":
                        options.DisplayName = RequireValue(args, ref i, "--display-name");
                        break;
                    case "--category":
                        options.Category = RequireValue(args, ref i, "--category");
                        break;
                    case "--notes":
                        options.Notes = RequireValue(args, ref i, "--notes");
                        break;
                    case "--disabled":
                        options.Disabled = true;
                        break;
                    case "--json":
                        options.Json = true;
                        break;
                    default:
                        throw new InvalidOperationException("Unknown option for telemetry github watches add: " + args[i]);
                }
            }

            if (string.IsNullOrWhiteSpace(options.RepositoryNameWithOwner)) {
                throw new InvalidOperationException("Missing required --repo for telemetry github watches add.");
            }

            return options;
        }
    }

    private sealed class WatchSyncOptions : CommonOptions {
        public List<string> Repositories { get; } = new();
        public DateTimeOffset? CapturedAtUtc { get; set; }
        public bool IncludeForks { get; set; }
        public int ForkLimit { get; set; } = 10;
        public bool IncludeStargazers { get; set; }
        public int StargazerLimit { get; set; } = 200;

        public static WatchSyncOptions Parse(string[] args) {
            var options = new WatchSyncOptions();
            for (var i = 0; i < args.Length; i++) {
                switch (args[i]) {
                    case "--db":
                        options.DatabasePath = RequireValue(args, ref i, "--db");
                        break;
                    case "--repo":
                        options.Repositories.Add(RequireValue(args, ref i, "--repo"));
                        break;
                    case "--captured-at":
                        options.CapturedAtUtc = ParseDateTimeOffset(RequireValue(args, ref i, "--captured-at"), "--captured-at");
                        break;
                    case "--forks":
                        options.IncludeForks = true;
                        break;
                    case "--fork-limit":
                        options.ForkLimit = ParsePositiveInt32(RequireValue(args, ref i, "--fork-limit"), "--fork-limit");
                        break;
                    case "--stargazers":
                        options.IncludeStargazers = true;
                        break;
                    case "--stargazer-limit":
                        options.StargazerLimit = ParsePositiveInt32(RequireValue(args, ref i, "--stargazer-limit"), "--stargazer-limit");
                        break;
                    case "--json":
                        options.Json = true;
                        break;
                    default:
                        throw new InvalidOperationException("Unknown option for telemetry github watches sync: " + args[i]);
                }
            }

            return options;
        }
    }

    private sealed class SnapshotListOptions : CommonOptions {
        public string? RepositoryNameWithOwner { get; set; }
        public string? WatchId { get; set; }

        public static SnapshotListOptions Parse(string[] args) {
            var options = new SnapshotListOptions();
            for (var i = 0; i < args.Length; i++) {
                switch (args[i]) {
                    case "--db":
                        options.DatabasePath = RequireValue(args, ref i, "--db");
                        break;
                    case "--repo":
                        options.RepositoryNameWithOwner = RequireValue(args, ref i, "--repo");
                        break;
                    case "--watch-id":
                        options.WatchId = RequireValue(args, ref i, "--watch-id");
                        break;
                    case "--json":
                        options.Json = true;
                        break;
                    default:
                        throw new InvalidOperationException("Unknown option for telemetry github snapshots list: " + args[i]);
                }
            }

            return options;
        }
    }

    private sealed class DashboardOptions : CommonOptions {
        public List<string> Repositories { get; } = new();
        public int Limit { get; set; } = 5;

        public static DashboardOptions Parse(string[] args) {
            var options = new DashboardOptions();
            for (var i = 0; i < args.Length; i++) {
                switch (args[i]) {
                    case "--db":
                        options.DatabasePath = RequireValue(args, ref i, "--db");
                        break;
                    case "--repo":
                        options.Repositories.Add(RequireValue(args, ref i, "--repo"));
                        break;
                    case "--limit":
                        options.Limit = ParsePositiveInt32(RequireValue(args, ref i, "--limit"), "--limit");
                        break;
                    case "--json":
                        options.Json = true;
                        break;
                    default:
                        throw new InvalidOperationException("Unknown option for telemetry github dashboard: " + args[i]);
                }
            }

            return options;
        }
    }

    private sealed class StargazerCaptureOptions : CommonOptions {
        public string? RepositoryNameWithOwner { get; set; }
        public int Limit { get; set; } = 200;
        public DateTimeOffset? CapturedAtUtc { get; set; }
        public bool Record { get; set; }

        public static StargazerCaptureOptions Parse(string[] args) {
            var options = new StargazerCaptureOptions();
            for (var i = 0; i < args.Length; i++) {
                switch (args[i]) {
                    case "--db":
                        options.DatabasePath = RequireValue(args, ref i, "--db");
                        break;
                    case "--repo":
                        options.RepositoryNameWithOwner = RequireValue(args, ref i, "--repo");
                        break;
                    case "--limit":
                        options.Limit = ParsePositiveInt32(RequireValue(args, ref i, "--limit"), "--limit");
                        break;
                    case "--captured-at":
                        options.CapturedAtUtc = ParseDateTimeOffset(RequireValue(args, ref i, "--captured-at"), "--captured-at");
                        break;
                    case "--record":
                        options.Record = true;
                        break;
                    case "--json":
                        options.Json = true;
                        break;
                    default:
                        throw new InvalidOperationException("Unknown option for telemetry github stargazers capture: " + args[i]);
                }
            }

            if (string.IsNullOrWhiteSpace(options.RepositoryNameWithOwner)) {
                throw new InvalidOperationException("Missing required --repo for telemetry github stargazers capture.");
            }

            return options;
        }
    }

    private sealed class StargazerListOptions : CommonOptions {
        public string? RepositoryNameWithOwner { get; set; }
        public string? StargazerLogin { get; set; }

        public static StargazerListOptions Parse(string[] args) {
            var options = new StargazerListOptions();
            for (var i = 0; i < args.Length; i++) {
                switch (args[i]) {
                    case "--db":
                        options.DatabasePath = RequireValue(args, ref i, "--db");
                        break;
                    case "--repo":
                        options.RepositoryNameWithOwner = RequireValue(args, ref i, "--repo");
                        break;
                    case "--login":
                        options.StargazerLogin = RequireValue(args, ref i, "--login");
                        break;
                    case "--json":
                        options.Json = true;
                        break;
                    default:
                        throw new InvalidOperationException("Unknown option for telemetry github stargazers list: " + args[i]);
                }
            }

            return options;
        }
    }

    private sealed class ForkDiscoverOptions : CommonOptions {
        public string? RepositoryNameWithOwner { get; set; }
        public int Limit { get; set; } = 20;
        public DateTimeOffset? CapturedAtUtc { get; set; }
        public bool Record { get; set; }

        public static ForkDiscoverOptions Parse(string[] args) {
            var options = new ForkDiscoverOptions();
            for (var i = 0; i < args.Length; i++) {
                switch (args[i]) {
                    case "--db":
                        options.DatabasePath = RequireValue(args, ref i, "--db");
                        break;
                    case "--repo":
                        options.RepositoryNameWithOwner = RequireValue(args, ref i, "--repo");
                        break;
                    case "--limit":
                        options.Limit = ParsePositiveInt32(RequireValue(args, ref i, "--limit"), "--limit");
                        break;
                    case "--captured-at":
                        options.CapturedAtUtc = ParseDateTimeOffset(RequireValue(args, ref i, "--captured-at"), "--captured-at");
                        break;
                    case "--record":
                        options.Record = true;
                        break;
                    case "--json":
                        options.Json = true;
                        break;
                    default:
                        throw new InvalidOperationException("Unknown option for telemetry github forks discover: " + args[i]);
                }
            }

            if (string.IsNullOrWhiteSpace(options.RepositoryNameWithOwner)) {
                throw new InvalidOperationException("Missing required --repo for telemetry github forks discover.");
            }

            return options;
        }
    }

    private sealed class ForkHistoryOptions : CommonOptions {
        public string? RepositoryNameWithOwner { get; set; }
        public int Limit { get; set; } = 20;

        public static ForkHistoryOptions Parse(string[] args) {
            var options = new ForkHistoryOptions();
            for (var i = 0; i < args.Length; i++) {
                switch (args[i]) {
                    case "--db":
                        options.DatabasePath = RequireValue(args, ref i, "--db");
                        break;
                    case "--repo":
                        options.RepositoryNameWithOwner = RequireValue(args, ref i, "--repo");
                        break;
                    case "--limit":
                        options.Limit = ParsePositiveInt32(RequireValue(args, ref i, "--limit"), "--limit");
                        break;
                    case "--json":
                        options.Json = true;
                        break;
                    default:
                        throw new InvalidOperationException("Unknown option for telemetry github forks history: " + args[i]);
                }
            }

            if (string.IsNullOrWhiteSpace(options.RepositoryNameWithOwner)) {
                throw new InvalidOperationException("Missing required --repo for telemetry github forks history.");
            }

            return options;
        }
    }

    private static string RequireValue(string[] args, ref int index, string optionName) {
        if (index + 1 >= args.Length) {
            throw new InvalidOperationException("Missing value for " + optionName + ".");
        }

        index++;
        return args[index];
    }

    private static DateTimeOffset ParseDateTimeOffset(string value, string optionName) {
        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)) {
            throw new InvalidOperationException("Invalid value for " + optionName + ": " + value);
        }

        return parsed.ToUniversalTime();
    }

    private static int ParsePositiveInt32(string value, string optionName) {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0) {
            throw new InvalidOperationException("Invalid value for " + optionName + ": " + value);
        }

        return parsed;
    }
}
