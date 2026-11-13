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

    [Fact]
    public async Task RetainsEveryLiveOrRecentSessionAtInclusiveBoundaryAcrossMachines() {
        using var store = await OpenStoreAsync();
        var events = Enumerable.Range(0, 8).Select(index => Event("live-" + index, new() {
            TurnOpen = true, LastActivityAtUtc = index == 0 ? null : Now.AddDays(-4),
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

        var activity = await store.RepositoryActivityAsync("  SYNTHETIC/REPOSITORY ", Now);
        Assert.Equal(Repo, activity.Repo);
        Assert.Equal(10, activity.Sessions.Length);
        Assert.Equal(8, activity.Sessions.Count(session => session.Live));
        Assert.Equal(["boundary-archived", "boundary-ended"], activity.Sessions.Where(session => !session.Live).Select(session => session.AgentId).Order().ToArray());
        Assert.DoesNotContain(activity.Sessions, session => session.AgentId is "too-old" or "old-archived" or "different-repo");
        Assert.True(JsonSerializer.SerializeToUtf8Bytes(activity, LedgerJson.Options).Length > 1024);
        var live = Assert.Single(activity.Sessions, session => session.AgentId == "live-0");
        Assert.Equal("windows", live.Machine);
        Assert.Equal("codex", live.Provider);
        Assert.Equal("feature/work", live.Branch);
        Assert.Equal("/synthetic/worktree", live.Worktree);
        Assert.Equal("Repository activity", live.Title);
        Assert.Equal(["https://example.test/pull/7"], live.PullRequests);
        Assert.Equal("origin/main", live.BaseRef);
        Assert.Equal("observed", live.GitState);
        Assert.Equal(20, live.Files!.Length);
        Assert.Equal("src/File19.cs", live.Files[^1]);
        Assert.True(live.FilesCapped);
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
        Assert.Equal(Now, Assert.Single((await store.RepositoryActivityAsync(Repo, Now)).Sessions).LastActivityAtUtc);
        var ended = original with { EventId = Guid.NewGuid(), Sequence = 2, ObservedAtUtc = Now.AddMinutes(1),
            Session = original.Session! with { TurnOpen = false, LastActivityAtUtc = Now,
                Repository = Observation() with { Repo = "SYNTHETIC/MOVED", Branch = "changed" } } };
        await store.IngestAsync([ended], Now.AddMinutes(1));
        Assert.Empty((await store.RepositoryActivityAsync(Repo, Now)).Sessions);
        var moved = Assert.Single((await store.RepositoryActivityAsync("synthetic/moved", Now)).Sessions);
        Assert.False(moved.Live);
        Assert.Equal("changed", moved.Branch);

        using (var sqlite = new SQLite()) {
            await using var db = await sqlite.OpenSessionAsync(Database);
            await db.ExecuteNonQueryAsync("DROP TABLE repository_sessions");
        }
        await store.InitializeAsync();
        Assert.Equal(JsonSerializer.Serialize(moved, LedgerJson.Options),
            JsonSerializer.Serialize(Assert.Single((await store.RepositoryActivityAsync("synthetic/moved", Now)).Sessions), LedgerJson.Options));
        await store.IngestAsync([ended with { EventId = Guid.NewGuid(), Sequence = 3, ObservedAtUtc = Now.AddMinutes(2),
            Session = ended.Session! with { Repository = null } }], Now.AddMinutes(2));
        Assert.Empty((await store.RepositoryActivityAsync("synthetic/moved", Now)).Sessions);
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
        Assert.Empty(activity.Sessions);
        Assert.Equal(keys.Order(StringComparer.Ordinal), activity.Leases.Select(lease => lease.Key));
        var overlap = await store.RepositoryOverlapAsync(Repo, "feature/one", null, null, Now);
        Assert.Equal("conflict", overlap.Verdict);
        Assert.Empty(overlap.Matches);
        Assert.Contains(overlap.Leases, lease => lease.Key == "branch:" + Repo + ":feature/one");
    }

    [Fact]
    public async Task LiveStructuralOverlapUsesExactBranchesAndCanonicalCaseSensitiveFiles() {
        using var store = await OpenStoreAsync();
        await store.IngestAsync([Event("live", new() { TurnOpen = true, Repository = Observation() })], Now);
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
            Event("same-pr", new() { TurnOpen = true, Repository = Observation() with { Branch = "other", Files = [] } })
        ], Now);
        var overlap = await store.RepositoryOverlapAsync(Repo, "feature/work", null, null, Now);
        Assert.Equal("conflict", overlap.Verdict);
        var match = Assert.Single(overlap.Matches, item => item.Session.AgentId == "same-pr");
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
        await store.IngestAsync(states.Select(state => Event(state, new() { TurnOpen = true,
            Repository = Observation() with { Branch = null, Files = null, GitState = state } })).ToArray(), Now);
        var unknown = await store.RepositoryOverlapAsync(Repo, "unrelated", ["other.cs"], null, Now);
        Assert.Equal("possible", unknown.Verdict);
        Assert.Empty(unknown.Matches);
        Assert.Contains("changed-files-unknown", unknown.Coverage);
        Assert.Contains("git-unavailable-or-partial", unknown.Coverage);
        Assert.Contains("branch-unknown", unknown.Coverage);
        Assert.Equal(states.Length, (await store.RepositoryActivityAsync(Repo, Now)).Sessions.Length);

        await store.IngestAsync([Event("pr-unknown", new() { TurnOpen = true,
            Repository = Observation() with { PullRequests = [], PullRequestsKnown = null } })], Now);
        var unobservedPr = await store.RepositoryActivityAsync(Repo, Now);
        Assert.Contains("pull-requests-unobserved", unobservedPr.Coverage);
        Assert.Null(Assert.Single(unobservedPr.Sessions, s => s.AgentId == "pr-unknown").PullRequestsKnown);
    }

    [Fact]
    public async Task HistoricalMissingMetadataDoesNotPolluteCurrentCoverage() {
        using var store = await OpenStoreAsync();
        await store.IngestAsync([
            Event("known", new() { TurnOpen = true, Repository = Observation() }),
            Event("historical", new() { TurnOpen = false, Archived = true, LastActivityAtUtc = Now.AddDays(-2) })
        ], Now);
        Assert.Empty((await store.RepositoryActivityAsync(Repo, Now)).Coverage);
        await store.IngestAsync([Event("recent-unknown", new() { TurnOpen = false, Archived = true,
            LastActivityAtUtc = Now.AddHours(-24) })], Now);
        Assert.Contains("repository-metadata-missing", (await store.RepositoryActivityAsync(Repo, Now)).Coverage);
    }

    private async Task<LedgerStore> OpenStoreAsync() {
        var store = new LedgerStore(Database, new(new()));
        await store.InitializeAsync();
        return store;
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
