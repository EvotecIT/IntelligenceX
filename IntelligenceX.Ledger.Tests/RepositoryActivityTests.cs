using System.Text.Json;
using DBAClientX;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Storage;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class RepositoryActivityTests : IDisposable {
    private readonly string _directory = LedgerTestPaths.NewDirectory("repository-activity");
    private static readonly DateTimeOffset Now = new(2026, 1, 12, 12, 0, 0, TimeSpan.Zero);
    private const string Repo = "synthetic/repository";
    private string Database => Path.Combine(_directory, "ledger.sqlite");

    [Theory]
    [InlineData("pull-request")]
    [InlineData("file")]
    public async Task HistoricalPrAndFileMatchesUseTheirOwnBranchLease(string dimension) {
        using var store = await OpenStoreAsync();
        await store.IngestAsync([
            Event("source", new() { TurnOpen = false, LastActivityAtUtc = Now.AddMinutes(-6),
                Repository = Observation() with { Worktree = "/synthetic/source", Branch = "feature/a", Files = [] } }),
            Event("earlier", new() { TurnOpen = false, LastActivityAtUtc = Now.AddMinutes(-5),
                Repository = Observation() with { Branch = "feature/c", Files = ["src/Earlier.cs"] } }),
            Event("later", new() { TurnOpen = true, LastActivityAtUtc = Now.AddMinutes(-1),
                Repository = Observation() with { Branch = "feature/b", PullRequests = ["https://example.test/pull/8"], Files = ["src/Later.cs"] } })
        ], Now);
        Task<RepositoryOverlap> Query() => store.RepositoryOverlapAsync(Repo,
            dimension == "pull-request" ? "feature/a" : null,
            dimension == "file" ? ["src/Earlier.cs"] : null, null, Now);
        Assert.Equal("possible", (await Query()).Verdict);
        await store.AcquireLeaseAsync(new("branch:" + Repo + ":feature/b", "synthetic-holder", "windows", "validation", 600), Now);
        Assert.Equal("possible", (await Query()).Verdict);
        await store.AcquireLeaseAsync(new("branch:" + Repo + ":feature/c", "synthetic-holder", "windows", "validation", 600), Now);
        Assert.Equal("conflict", (await Query()).Verdict);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EarlierBranchInSharedCheckoutRemainsPossibleDespiteAnotherLiveBranch(bool otherBranchLive) {
        using var store = await OpenStoreAsync();
        await store.IngestAsync([
            Event("earlier", new() { TurnOpen = false, Outcome = "completed", LastActivityAtUtc = Now.AddMinutes(-5),
                Repository = Observation() with { Branch = "feature/earlier", PullRequests = [], Files = [] } }),
            Event("later", new() { TurnOpen = otherBranchLive, LastActivityAtUtc = Now.AddMinutes(-1),
                Repository = Observation() with { Branch = "feature/later", PullRequests = [], Files = [] } })
        ], Now);
        var overlap = await store.RepositoryOverlapAsync(Repo, "feature/earlier", null, null, Now);
        Assert.Equal("possible", overlap.Verdict);
        Assert.Contains("same-branch", Assert.Single(overlap.Matches).Reasons);
        await store.AcquireLeaseAsync(new("branch:" + Repo + ":feature/later", "synthetic-holder", "windows", "validation", 600), Now);
        Assert.Equal("possible", (await store.RepositoryOverlapAsync(Repo, "feature/earlier", null, null, Now)).Verdict);
        await store.AcquireLeaseAsync(new("branch:" + Repo + ":feature/earlier", "synthetic-holder", "windows", "validation", 600), Now);
        Assert.Equal("conflict", (await store.RepositoryOverlapAsync(Repo, "feature/earlier", null, null, Now)).Verdict);
    }

    [Fact]
    public async Task CompactDefaultKeepsLiveIdentitiesAndRecentTitlesWithinThreeKilobytes() {
        using var store = await OpenStoreAsync();
        var observations = Enumerable.Range(0, 12).Select(index => Event("synthetic-worker-" + index.ToString("D20"), new() {
            TurnOpen = index == 0, Outcome = index == 0 ? "unknown" : "completed", LastActivityAtUtc = Now.AddMinutes(-index),
            Repository = Observation() with {
                Worktree = "/synthetic/repository/worktrees/feature-" + index / 2,
                Branch = "feature/synthetic-" + index / 2,
                Title = "Synthetic repository performance task " + index + " with a bounded preview",
                Files = Enumerable.Range(0, 9).Select(file => "Synthetic.Library/Components/Performance/ChangedComponent" + file + ".cs").ToArray()
            }
        })).ToArray();
        await store.IngestAsync(observations, Now);
        var compact = await store.RepositoryActivityAsync(Repo, Now);
        Assert.Equal(6, compact.Worktrees.Length);
        Assert.Single(compact.Worktrees.SelectMany(worktree => worktree.Sessions));
        Assert.Equal(11, compact.Worktrees.Sum(worktree => worktree.RecentSessionCount));
        Assert.Equal(11, compact.Worktrees.Sum(worktree => worktree.RecentTitles!.Length));
        Assert.All(compact.Worktrees, worktree => {
            Assert.Equal(9, worktree.FileCount);
            Assert.InRange(worktree.Files!.Length, 0, 5);
        });
        Assert.InRange(JsonSerializer.SerializeToUtf8Bytes(compact, LedgerReadJson.Default.RepositoryActivity).Length, 1, 3000);
        var full = await store.RepositoryActivityAsync(Repo, Now, details: true);
        Assert.Equal(12, full.Worktrees.Sum(worktree => worktree.Sessions.Length));
        Assert.All(full.Worktrees, worktree => Assert.Equal(5, worktree.Files!.Length));
        Assert.All(full.Worktrees, worktree => Assert.Null(worktree.RecentSessionCount));
        var overlap = await store.RepositoryOverlapAsync(Repo, null, null, "performance task", Now);
        Assert.NotEmpty(overlap.Matches);
        Assert.All(overlap.Matches, match => Assert.All(match.Worktree.Sessions, session => Assert.True(session.Live)));
    }

    [Fact]
    public async Task RepositoryLiveRequiresWorkingWindowProgressAndExcludesIncompleteTurns() {
        using var store = await OpenStoreAsync();
        (string Id, int? Age, string Outcome)[] cases = [
            ("working", 10, "unknown"), ("quiet", 30, "unknown"),
            ("stale", (int?)31, "unknown"), ("old-open", (int?)121, "unknown"),
            ("incomplete", (int?)10, "incomplete"), ("no-progress", (int?)null, "unknown")
        ];
        await store.IngestAsync(cases.Select(row => Event(row.Id, new() {
            TurnOpen = true, TurnId = "turn-" + row.Id, Outcome = row.Outcome,
            LastActivityAtUtc = row.Age is { } age ? Now.AddMinutes(-age) : null,
            Repository = Observation() with { Branch = row.Id, Worktree = "/synthetic/" + row.Id, PullRequests = [] }
        })).ToArray(), Now);
        var activity = await store.RepositoryActivityAsync(Repo, Now, details: true);
        var sessions = activity.Worktrees.SelectMany(worktree => worktree.Sessions).ToArray();
        Assert.Equal(["quiet", "working"], sessions.Where(session => session.Live).Select(session => session.AgentId).Order().ToArray());
        Assert.DoesNotContain(sessions, session => session.AgentId is "old-open" or "no-progress");
        Assert.Contains(sessions, session => session.AgentId == "stale" && !session.Live);
        Assert.Contains(sessions, session => session.AgentId == "incomplete" && !session.Live);
        Assert.Contains("stale-open-sessions", activity.Coverage);
        Assert.Contains("session-activity-unobserved", activity.Coverage);
        Assert.Equal("possible", (await store.RepositoryOverlapAsync(Repo, "stale", null, null, Now)).Verdict);
        await store.UpdateReporterHealthAsync(new("windows", "reporter", "v1", Now, [], [new("old-open", "turn-old-open", Now)]));
        Assert.Contains((await store.RepositoryActivityAsync(Repo, Now)).Worktrees.SelectMany(worktree => worktree.Sessions),
            session => session.AgentId == "old-open" && session.Live);
    }

    [Fact]
    public async Task SharedCheckoutGroupsSessionsAndAttributesFiveFilePreviewToWorktree() {
        using var store = await OpenStoreAsync();
        await store.IngestAsync(Enumerable.Range(0, 3).Select(index => Event("worker-" + index, new() {
            TurnOpen = true, LastActivityAtUtc = Now,
            Repository = Observation() with { Files = Enumerable.Range(0, 9).Select(i => "src/Changed" + i + ".cs").ToArray() }
        })).ToArray(), Now);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(await store.RepositoryActivityAsync(Repo, Now, details: true), LedgerReadJson.Default.RepositoryActivity));
        var worktree = Assert.Single(json.RootElement.GetProperty("worktrees").EnumerateArray());
        Assert.Equal("summary", json.RootElement.EnumerateObject().First().Name);
        Assert.StartsWith("3 live", json.RootElement.GetProperty("summary").GetString());
        Assert.Equal(9, worktree.GetProperty("fileCount").GetInt32());
        Assert.Equal(5, worktree.GetProperty("files").GetArrayLength());
        Assert.Equal(3, worktree.GetProperty("sessions").GetArrayLength());
        Assert.All(worktree.GetProperty("sessions").EnumerateArray(), session => {
            Assert.False(session.TryGetProperty("files", out _));
            Assert.False(session.TryGetProperty("worktree", out _));
        });
    }

    [Fact]
    public async Task DefaultRecentWindowIncludesTwoHourBoundaryAndLiveSessionsWithoutOlderHistory() {
        using var store = await OpenStoreAsync();
        await store.IngestAsync([
            Event("live-quiet", new() { TurnOpen = true, LastActivityAtUtc = Now.AddMinutes(-30), Repository = Observation() }),
            Event("boundary", new() { TurnOpen = false, LastActivityAtUtc = Now.AddHours(-2).ToOffset(TimeSpan.FromHours(3)), Repository = Observation() }),
            Event("too-old", new() { TurnOpen = false, LastActivityAtUtc = Now.AddHours(-2).AddTicks(-1), Repository = Observation() }),
            Event("day-old", new() { TurnOpen = false, Archived = true, LastActivityAtUtc = Now.AddHours(-24), Repository = Observation() })
        ], Now);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(await store.RepositoryActivityAsync(Repo, Now, details: true), LedgerReadJson.Default.RepositoryActivity));
        var sessions = json.RootElement.GetProperty("worktrees").EnumerateArray().SelectMany(w => w.GetProperty("sessions").EnumerateArray()).ToArray();
        Assert.Equal(["boundary", "live-quiet"], sessions.Select(s => s.GetProperty("agentId").GetString()!).Order().ToArray());
    }

    [Fact]
    public async Task OverlapGroupsSharedFilesOnceAndUsesEvidenceBeyondFiveNamePreview() {
        using var store = await OpenStoreAsync();
        await store.IngestAsync(Enumerable.Range(0, 3).Select(index => Event("worker-" + index, new() {
            TurnOpen = true, LastActivityAtUtc = Now, Repository = Observation() with { Files = Enumerable.Range(0, 9).Select(i => "src/Changed" + i + ".cs").ToArray() }
        })).ToArray(), Now);
        var overlap = await store.RepositoryOverlapAsync(Repo, null, ["src/Changed8.cs"], null, Now);
        Assert.Equal("conflict", overlap.Verdict);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(overlap, LedgerReadJson.Default.RepositoryOverlap));
        var match = Assert.Single(json.RootElement.GetProperty("matches").EnumerateArray());
        Assert.Equal("summary", json.RootElement.EnumerateObject().First().Name);
        Assert.Equal(3, match.GetProperty("worktree").GetProperty("sessions").GetArrayLength());
        Assert.Equal("src/Changed8.cs", Assert.Single(match.GetProperty("sharedFiles").EnumerateArray()).GetString());
    }

    [Fact]
    public async Task MissingWorkspacesOmitFilesAndLegacyCappedCountsRemainUnknown() {
        using var store = await OpenStoreAsync();
        var observations = Enumerable.Range(0, 8).Select(index => Event("workspace-" + index, new() {
            TurnOpen = true, LastActivityAtUtc = Now, Repository = Observation() with { Worktree = "/synthetic/" + index,
                Files = ["old.cs"], FilesCapped = true, GitState = index == 0 ? "workspace-missing" : "observed" }
        })).ToArray();
        await store.IngestAsync(observations, Now);
        var activity = await store.RepositoryActivityAsync(Repo, Now);
        Assert.Equal(8, activity.Worktrees.Length);
        Assert.All(activity.Worktrees, worktree => Assert.Null(worktree.FileCount));
        Assert.Contains("changed-file-count-unknown", activity.Coverage);
        Assert.Contains("changed-files-unknown", activity.Coverage);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(activity, LedgerReadJson.Default.RepositoryActivity));
        var missing = Assert.Single(json.RootElement.GetProperty("worktrees").EnumerateArray(), worktree =>
            worktree.TryGetProperty("gitState", out var state) && state.GetString() == "workspace-missing");
        Assert.False(missing.TryGetProperty("files", out _));
        Assert.Equal("possible", (await store.RepositoryOverlapAsync(Repo, null, ["unobserved.cs"], null, Now)).Verdict);
    }

    [Fact]
    public async Task OptInHistoryRetainsEveryLiveOr24HourSessionAcrossMachines() {
        using var store = await OpenStoreAsync();
        var events = Enumerable.Range(0, 8).Select(index => Event("live-" + index, new() {
            TurnOpen = true, LastActivityAtUtc = Now.AddMinutes(-index),
            Repository = Observation() with { Files = Enumerable.Range(0, 25).Select(i => "src/File" + i + ".cs").ToArray() }
        }, machine: index % 2 == 0 ? "windows" : "mac", provider: index % 2 == 0 ? "codex" : "claude")).ToList();
        events.Add(Event("boundary-ended", new() { TurnOpen = false,
            LastActivityAtUtc = Now.AddHours(-24).ToOffset(TimeSpan.FromHours(3)), Repository = Observation() }));
        events.Add(Event("boundary-archived", new() { TurnOpen = true, Archived = true,
            LastActivityAtUtc = Now.AddHours(-24), Repository = Observation() }));
        events.Add(Event("too-old", new() { TurnOpen = false,
            LastActivityAtUtc = Now.AddHours(-24).AddTicks(-1), Repository = Observation() }));
        events.Add(Event("old-archived", new() { TurnOpen = true, Archived = true,
            LastActivityAtUtc = Now.AddDays(-2), Repository = Observation() }));
        events.Add(Event("different-repo", new() { TurnOpen = true, Repository = Observation() with { Repo = Repo + "-extra" } }));
        await store.IngestAsync(events.ToArray(), Now);

        var activity = await store.RepositoryActivityAsync("  SYNTHETIC/REPOSITORY ", Now, recent24h: true, details: true);
        var sessions = activity.Worktrees.SelectMany(worktree => worktree.Sessions).ToArray();
        Assert.Equal(Repo, activity.Repo);
        Assert.Equal(10, sessions.Length);
        Assert.Equal(8, sessions.Count(session => session.Live));
        Assert.Equal(["boundary-archived", "boundary-ended"], sessions.Where(session => !session.Live).Select(session => session.AgentId).Order().ToArray());
        Assert.DoesNotContain(sessions, session => session.AgentId is "too-old" or "old-archived" or "different-repo");
        var worktree = Assert.Single(activity.Worktrees, worktree => worktree.Machine == "windows");
        var live = Assert.Single(worktree.Sessions, session => session.AgentId == "live-0");
        Assert.Equal("windows", worktree.Machine);
        Assert.Equal("codex", live.Provider);
        Assert.Equal("feature/work", worktree.Branch);
        Assert.Equal("/synthetic/worktree", worktree.Worktree);
        Assert.Equal("Repository activity", live.Title);
        Assert.Equal(["https://example.test/pull/7"], worktree.PullRequests);
        Assert.Equal("origin/main", worktree.BaseRef);
        Assert.Equal("observed", worktree.GitState);
        Assert.Equal(5, worktree.Files!.Length);
        Assert.Equal(25, worktree.FileCount);
        Assert.True(worktree.FilesCapped);
        Assert.Contains("changed-files-capped", activity.Coverage);
        Assert.Equal(["mac", "windows"], await store.ReportedRepositoryMachinesAsync());
    }

    [Fact]
    public async Task IndexTracksHealthActivityRepositoryChangesRemovalAndStartupBackfill() {
        using var store = await OpenStoreAsync();
        var original = Event("session", new() { TurnId = "turn", TurnOpen = true,
            LastActivityAtUtc = Now.AddDays(-2), Repository = Observation() });
        await store.IngestAsync([original], Now);
        await store.UpdateReporterHealthAsync(new("windows", "reporter", "v1", Now, [], [new("session", "turn", Now)]));
        Assert.Equal(Now, Assert.Single(Assert.Single((await store.RepositoryActivityAsync(Repo, Now)).Worktrees).Sessions).LastActivityAtUtc);
        var ended = original with { EventId = Guid.NewGuid(), Sequence = 2, ObservedAtUtc = Now.AddMinutes(1),
            Session = original.Session! with { TurnOpen = false, LastActivityAtUtc = Now,
                Repository = Observation() with { Repo = "SYNTHETIC/MOVED", Branch = "changed" } } };
        await store.IngestAsync([ended], Now.AddMinutes(1));
        Assert.Empty((await store.RepositoryActivityAsync(Repo, Now)).Worktrees);
        var moved = Assert.Single((await store.RepositoryActivityAsync("synthetic/moved", Now, details: true)).Worktrees);
        Assert.False(Assert.Single(moved.Sessions).Live);
        Assert.Equal("changed", moved.Branch);

        using (var sqlite = new SQLite()) {
            await using var db = await sqlite.OpenSessionAsync(Database);
            await db.ExecuteNonQueryAsync("DROP TABLE repository_sessions");
        }
        await store.InitializeAsync();
        Assert.Equal(JsonSerializer.Serialize(moved, LedgerJson.Options),
            JsonSerializer.Serialize(Assert.Single((await store.RepositoryActivityAsync("synthetic/moved", Now, details: true)).Worktrees), LedgerJson.Options));
        await store.IngestAsync([ended with { EventId = Guid.NewGuid(), Sequence = 3, ObservedAtUtc = Now.AddMinutes(2),
            Session = ended.Session! with { Repository = null } }], Now.AddMinutes(2));
        Assert.Empty((await store.RepositoryActivityAsync("synthetic/moved", Now)).Worktrees);
        Assert.Contains("repository-metadata-missing", (await store.RepositoryActivityAsync(Repo, Now)).Coverage);
    }

    [Fact]
    public async Task RepositoryLeasesIncludeExactKeysBranchesAndSessionHoldersWithoutTruncation() {
        using var store = await OpenStoreAsync();
        var nativeId = Guid.NewGuid();
        await store.IngestAsync([Event("item", new() { SourceSessionId = nativeId, TurnOpen = false,
            LastActivityAtUtc = Now.AddDays(-2), Repository = Observation() })], Now);
        var keys = new[] { "build:SYNTHETIC/REPOSITORY", "repo:" + Repo, "branch:" + Repo + ":feature/one", "bench:by-item" }
            .Concat(Enumerable.Range(0, 6).Select(index => "bench:by-native-" + index)).ToArray();
        foreach (var key in keys) {
            var holder = key == "bench:by-item" ? "item" : key.StartsWith("bench:", StringComparison.Ordinal) ? nativeId.ToString() : "independent";
            Assert.True((await store.AcquireLeaseAsync(new(key, holder, "windows", "validation", 600), Now)).Ok);
        }
        foreach (var key in new[] { "build:" + Repo + "-extra", "repo:" + Repo + ":other", "branch:" + Repo + "-extra:feature/one" })
            await store.AcquireLeaseAsync(new(key, "unrelated", "windows", "validation", 600), Now);
        await store.AcquireLeaseAsync(new("branch:" + Repo + ":expired", "independent", "windows", "validation", 1), Now.AddSeconds(-1));

        var activity = await store.RepositoryActivityAsync(Repo, Now);
        Assert.Empty(activity.Worktrees);
        Assert.Equal(keys.Order(StringComparer.Ordinal), activity.Leases.Select(lease => lease.Key));
        var overlap = await store.RepositoryOverlapAsync(Repo, "feature/one", null, null, Now);
        Assert.Equal("conflict", overlap.Verdict);
        Assert.Empty(overlap.Matches);
        Assert.Contains(overlap.Leases, lease => lease.Key == "branch:" + Repo + ":feature/one");
    }

    [Fact]
    public async Task LiveStructuralOverlapUsesExactBranchesAndCanonicalCaseSensitiveFiles() {
        using var store = await OpenStoreAsync();
        await store.IngestAsync([Event("live", new() { TurnOpen = true, LastActivityAtUtc = Now, Repository = Observation() })], Now);
        var branch = await store.RepositoryOverlapAsync(Repo, "feature/work", null, null, Now);
        Assert.Equal("conflict", branch.Verdict);
        Assert.Contains("same-branch", Assert.Single(branch.Matches).Reasons);
        var files = await store.RepositoryOverlapAsync(Repo, null, ["src/File.cs"], null, Now);
        Assert.Equal("conflict", files.Verdict);
        var match = Assert.Single(files.Matches);
        Assert.Contains("shared-changed-files", match.Reasons);
        Assert.Equal(["src/File.cs"], match.SharedFiles);
        Assert.Equal("none", (await store.RepositoryOverlapAsync(Repo, "FEATURE/work", ["src/file.cs", "src\\File.cs"], null, Now)).Verdict);
        await store.AcquireLeaseAsync(new("repo:" + Repo, "reservation-holder", "mac", "repository work", 600), Now);
        var reserved = await store.RepositoryOverlapAsync(Repo, "other-branch", ["other.cs"], "unrelated workload", Now);
        Assert.Equal("possible", reserved.Verdict);
        Assert.Contains("repository-lease-held", reserved.Coverage);
    }

    [Fact]
    public async Task RecentStructuralAndUnicodeLexicalMatchesStayPossibleUntilBranchLeaseIsHeld() {
        using var store = await OpenStoreAsync();
        await store.IngestAsync([Event("recent", new() { TurnOpen = false, Archived = true, LastActivityAtUtc = Now,
            Repository = Observation() with { Title = "État 修复 إصلاح" } })], Now);
        var recent = await store.RepositoryOverlapAsync(Repo, "feature/work", ["src/File.cs"], null, Now);
        Assert.Equal("possible", recent.Verdict);
        Assert.Equal(["same-branch", "same-pull-request-inferred-from-branch", "shared-changed-files"], Assert.Single(recent.Matches).Reasons);
        var text = await store.RepositoryOverlapAsync(Repo, null, null, "e\u0301tat，修复؟", Now);
        Assert.Equal("possible", text.Verdict);
        Assert.Equal(["lexical-title-token-overlap"], Assert.Single(text.Matches).Reasons);
        Assert.Empty((await store.RepositoryOverlapAsync(Repo, null, null, "État unrelated task", Now)).Matches);
        Assert.Empty((await store.RepositoryOverlapAsync(Repo, null, null, "État 修复 unrelated query words several other topics", Now)).Matches);
        await store.AcquireLeaseAsync(new("branch:" + Repo + ":feature/work", "independent", "mac", "validation", 600), Now);
        var held = await store.RepositoryOverlapAsync(Repo, null, ["src/File.cs"], null, Now);
        Assert.Equal("conflict", held.Verdict);
        Assert.Contains("branch-lease-held", Assert.Single(held.Matches).Reasons);
        Assert.Equal("possible", (await store.RepositoryOverlapAsync(Repo, null, null, "إصلاح", Now)).Verdict);
    }

    [Fact]
    public async Task MatchingBranchInfersPullRequestOverlapWithAnotherLiveSession() {
        using var store = await OpenStoreAsync();
        await store.IngestAsync([
            Event("target-branch", new() { TurnOpen = false, LastActivityAtUtc = Now, Repository = Observation() }),
            Event("same-pr", new() { TurnOpen = true, LastActivityAtUtc = Now, Repository = Observation() with { Branch = "other", Worktree = "/synthetic/other", Files = [] } })
        ], Now);
        var overlap = await store.RepositoryOverlapAsync(Repo, "feature/work", null, null, Now);
        Assert.Equal("conflict", overlap.Verdict);
        var match = Assert.Single(overlap.Matches, item => item.Worktree.Sessions.Any(session => session.AgentId == "same-pr"));
        Assert.Equal(["same-pull-request-inferred-from-branch"], match.Reasons);
    }

    [Fact]
    public async Task EmptyMissingMetadataAndIncompleteGitNeverInventObservedAbsence() {
        using var store = await OpenStoreAsync();
        var empty = await store.RepositoryOverlapAsync(Repo, "feature/work", null, null, Now);
        Assert.Equal("possible", empty.Verdict);
        Assert.Contains("repository-sessions-unobserved", empty.Coverage);
        await store.IngestAsync([Event("missing", new() { TurnOpen = true })], Now);
        Assert.Contains("repository-metadata-missing", (await store.RepositoryActivityAsync(Repo, Now)).Coverage);
        var states = new[] { "not-observed", "workspace-missing", "git-unavailable", "base-unknown" };
        await store.IngestAsync(states.Select(state => Event(state, new() { TurnOpen = true, LastActivityAtUtc = Now,
            Repository = Observation() with { Branch = null, Worktree = "/synthetic/" + state, Files = null, GitState = state } })).ToArray(), Now);
        var unknown = await store.RepositoryOverlapAsync(Repo, "unrelated", ["other.cs"], null, Now);
        Assert.Equal("possible", unknown.Verdict);
        Assert.Empty(unknown.Matches);
        Assert.Contains("changed-files-unknown", unknown.Coverage);
        Assert.Contains("git-unavailable-or-partial", unknown.Coverage);
        Assert.Contains("branch-unknown", unknown.Coverage);
        Assert.Equal(states.Length, (await store.RepositoryActivityAsync(Repo, Now)).Worktrees.Length);

        await store.IngestAsync([Event("pr-unknown", new() { TurnOpen = true, LastActivityAtUtc = Now,
            Repository = Observation() with { Worktree = "/synthetic/pr-unknown", PullRequests = [], PullRequestsKnown = null } })], Now);
        var unobservedPr = await store.RepositoryActivityAsync(Repo, Now);
        Assert.Contains("pull-requests-unobserved", unobservedPr.Coverage);
        Assert.Null(Assert.Single(unobservedPr.Worktrees, worktree => worktree.Worktree == "/synthetic/pr-unknown").PullRequestsKnown);
    }

    [Fact]
    public async Task HistoricalMissingMetadataDoesNotPolluteCurrentCoverage() {
        using var store = await OpenStoreAsync();
        await store.IngestAsync([
            Event("known", new() { TurnOpen = true, LastActivityAtUtc = Now, Repository = Observation() }),
            Event("historical", new() { TurnOpen = false, Archived = true, LastActivityAtUtc = Now.AddDays(-2) })
        ], Now);
        Assert.Empty((await store.RepositoryActivityAsync(Repo, Now)).Coverage);
        await store.IngestAsync([Event("recent-unknown", new() { TurnOpen = false, Archived = true,
            LastActivityAtUtc = Now.AddHours(-2) })], Now);
        Assert.Contains("repository-metadata-missing", (await store.RepositoryActivityAsync(Repo, Now)).Coverage);
    }

    private async Task<LedgerStore> OpenStoreAsync() {
        var store = new LedgerStore(Database, new(new()));
        await store.InitializeAsync();
        return store;
    }

    [Fact]
    public async Task SourceHealthQualifiesPrCompletenessWithoutRewritingSessionHistory() {
        using var store = await OpenStoreAsync();
        await store.IngestAsync([Event("session", new() { TurnOpen = true, LastActivityAtUtc = Now, Repository = Observation() })], Now);
        await store.UpdateReporterHealthAsync(new("windows", "reporter", "v1", Now, ["pull-requests-unavailable"]));
        var unavailable = await store.RepositoryActivityAsync(Repo, Now);
        Assert.Null(Assert.Single(unavailable.Worktrees).PullRequestsKnown);
        Assert.Contains("pull-requests-unobserved", unavailable.Coverage);
        Assert.Equal("possible", (await store.RepositoryOverlapAsync(Repo, "unrelated", null, null, Now)).Verdict);
        Assert.True(Assert.Single(await store.EvaluateAllAsync(Now)).Session!.Repository!.PullRequestsKnown);
        await store.UpdateReporterHealthAsync(new("windows", "reporter", "v1", Now.AddSeconds(1), []));
        var recovered = await store.RepositoryActivityAsync(Repo, Now.AddSeconds(1), details: true);
        Assert.True(Assert.Single(recovered.Worktrees).PullRequestsKnown);
        Assert.Empty(recovered.Coverage);
        Assert.Single(await store.HistoryAsync("session", 0, 100, default));
    }

    private static RepositoryObservation Observation() => new(Repo, "feature/work", "/synthetic/worktree", "Repository activity",
        ["https://example.test/pull/7"], ["src/File.cs"], false, "origin/main", "observed", true);

    private static LedgerEvent Event(string id, SessionFact session, string machine = "windows", string provider = "codex") => new() {
        EventId = Guid.NewGuid(), Epoch = Guid.NewGuid(), Sequence = 1, MachineId = machine, ReporterId = "reporter",
        ItemId = id, Provider = provider, Source = "watcher", ObservedAtUtc = Now, Session = session
    };

    public void Dispose() {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
