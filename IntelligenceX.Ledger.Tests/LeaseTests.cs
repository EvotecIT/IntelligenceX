using System.Text.Json;
using DBAClientX;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Storage;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class LeaseTests : IDisposable {
    private readonly string _directory = LedgerTestPaths.NewDirectory("leases");
    private static readonly DateTimeOffset Now = new(2026, 1, 12, 12, 0, 0, TimeSpan.Zero);
    private string Database => Path.Combine(_directory, "ledger.sqlite");

    [Fact]
    public async Task ExclusiveLeaseSurvivesRestartExpiresAndFencesThePreviousHolder() {
        Lease first;
        using (var store = new LedgerStore(Database, new(new()))) {
            await store.InitializeAsync();
            var contenders = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => store.AcquireLeaseAsync(
                new("resource:synthetic", "agent-" + i, "synthetic", "exclusive", 600), Now)));
            first = Assert.Single(contenders, r => r.Ok).Lease!;
            Assert.All(contenders.Where(r => !r.Ok), r => { Assert.Equal("busy", r.State); Assert.Equal(first, r.Lease); });
            Assert.Equal(first, Assert.Single((await store.WhoAsync("resource:", true, Now)).Leases));
        }
        using var restarted = new LedgerStore(Database, new(new()));
        await restarted.InitializeAsync();
        Assert.Equal(first, Assert.Single((await restarted.WhoAsync(first.Key, false, Now.AddSeconds(599))).Leases));
        await restarted.IngestAsync([new() { EventId = Guid.NewGuid(), Epoch = Guid.NewGuid(), Sequence = 1,
            MachineId = "synthetic", ReporterId = "reporter", ItemId = first.Holder, Provider = "codex", Source = "watcher",
            ObservedAtUtc = Now.AddSeconds(599), Session = new() { TurnOpen = true, LastActivityAtUtc = Now.AddSeconds(599) } }], Now.AddSeconds(599));
        var renewed = await restarted.RenewLeaseAsync(new(first.Key, first.LeaseId, 60), Now.AddSeconds(599));
        Assert.True(renewed.Ok);
        Assert.Equal(Now.AddSeconds(659), renewed.Lease!.ExpiresAtUtc);
        Assert.Empty((await restarted.WhoAsync(first.Key, false, Now.AddSeconds(659))).Leases);
        Assert.False((await restarted.RenewLeaseAsync(new(first.Key, first.LeaseId, 60), Now.AddSeconds(659))).Ok);
        var successor = await restarted.AcquireLeaseAsync(new(first.Key, "successor", "synthetic", "benchmark", 30), Now.AddSeconds(659));
        Assert.True(successor.Ok);
        Assert.NotEqual(first.LeaseId, successor.Lease!.LeaseId);
        Assert.False((await restarted.ReleaseLeaseAsync(new(first.Key, first.LeaseId), Now.AddSeconds(660))).Ok);
        Assert.Equal(successor.Lease, Assert.Single((await restarted.WhoAsync(first.Key, false, Now.AddSeconds(660))).Leases));
        Assert.True((await restarted.ReleaseLeaseAsync(new(first.Key, successor.Lease.LeaseId), Now.AddSeconds(660))).Ok);
        Assert.Empty((await restarted.WhoAsync(first.Key, false, Now.AddSeconds(660))).Leases);
    }

    [Fact]
    public async Task HealthRefreshAndLegacyPingsStayOutOfHistoryWhilePruneRetainsProjectionsAndTransitions() {
        using var store = new LedgerStore(Database, new(new()));
        await store.InitializeAsync();
        var observation = new LedgerEvent { EventId = Guid.NewGuid(), Epoch = Guid.NewGuid(), Sequence = 1,
            MachineId = "synthetic", ReporterId = "reporter", ItemId = "session", Provider = "codex", Source = "watcher", ObservedAtUtc = Now,
            Session = new() { SourceSessionId = Guid.NewGuid(), TurnOpen = true, LastActivityAtUtc = Now } };
        await store.IngestAsync([observation], Now.AddDays(-8));
        await store.UpdateReporterHealthAsync(new("synthetic", "reporter", "v1", Now, ["queue-not-visible"]));
        await store.UpdateReporterHealthAsync(new("synthetic", "reporter", "v1", Now.AddMinutes(1), []));
        await store.IngestAsync([observation with { EventId = Guid.NewGuid(), Sequence = 2, ItemId = "machine", Session = null,
            Heartbeat = true, ObservedAtUtc = Now.AddMinutes(2) }], Now.AddMinutes(2));
        using var db = new SQLite();
        await using var sql = await db.OpenSessionAsync(Database);
        Assert.Equal(1L, Convert.ToInt64(await sql.ExecuteScalarAsync("SELECT COUNT(*) FROM reporter_health")));
        Assert.Equal(1L, Convert.ToInt64(await sql.ExecuteScalarAsync("SELECT COUNT(*) FROM events")));
        Assert.Equal(Now.AddMinutes(2), (await store.HeartbeatsAsync(default))["reporter"]);
        Assert.Empty(Assert.Single(await store.CoverageAsync(default)).Coverage!);
        var transitionCount = Convert.ToInt64(await sql.ExecuteScalarAsync("SELECT COUNT(*) FROM transitions"));
        Assert.True(transitionCount > 0);
        Assert.Equal(1, await store.PruneEventsAsync(Now, 1));
        Assert.Equal(0L, Convert.ToInt64(await sql.ExecuteScalarAsync("SELECT COUNT(*) FROM events")));
        Assert.Equal(1L, Convert.ToInt64(await sql.ExecuteScalarAsync("SELECT COUNT(*) FROM items")));
        Assert.Equal(transitionCount, Convert.ToInt64(await sql.ExecuteScalarAsync("SELECT COUNT(*) FROM transitions")));
        Assert.Equal(1, (await store.MachineStatusAsync("synthetic", "healthy", Now)).LiveSessions);
    }

    [Fact]
    public async Task ActivityRefreshAdvancesOnlyTheMatchingOpenTurnWithoutAddingRawEvents() {
        using var store = new LedgerStore(Database, new(new()));
        await store.InitializeAsync();
        var state = new LedgerEvent { EventId = Guid.NewGuid(), Epoch = Guid.NewGuid(), Sequence = 1,
            MachineId = "synthetic", ReporterId = "reporter", ItemId = "session", Provider = "codex", Source = "watcher", ObservedAtUtc = Now,
            Session = new() { TurnId = "turn-1", TurnOpen = true, LastActivityAtUtc = Now.AddHours(-1) } };
        await store.IngestAsync([state], Now);
        Assert.Equal("no-progress", Assert.Single(await store.EvaluateAllAsync(Now)).Current!.ReasonCode);
        await store.UpdateReporterHealthAsync(new("synthetic", "reporter", "v1", Now, [], [new("session", "turn-1", Now)]));
        var current = Assert.Single(await store.EvaluateAllAsync(Now));
        Assert.Equal("working", current.Current!.Value);
        Assert.Equal(Now, current.Session!.LastActivityAtUtc);
        var progress = state with { EventId = Guid.NewGuid(), Sequence = 2, ObservedAtUtc = Now.AddMinutes(1),
            Session = state.Session with { LastActivityAtUtc = Now.AddMinutes(1) } };
        await store.IngestAsync([progress], Now.AddMinutes(1));
        Assert.Single(await store.HistoryAsync("session", 0, 100, default));
        Assert.Equal(Now.AddMinutes(1), Assert.Single(await store.EvaluateAllAsync(Now.AddMinutes(1))).Session!.LastActivityAtUtc);
        await Assert.ThrowsAsync<EventConflictException>(() => store.IngestAsync([progress with { Sequence = 1 }], Now.AddMinutes(1)));
        var successor = progress with { EventId = Guid.NewGuid(), Sequence = 3,
            Session = progress.Session! with { TurnId = "turn-2", LastActivityAtUtc = Now.AddMinutes(2) }, ObservedAtUtc = Now.AddMinutes(2) };
        await store.IngestAsync([successor], Now.AddMinutes(2));
        await store.UpdateReporterHealthAsync(new("synthetic", "reporter", "v1", Now.AddMinutes(3), [], [new("session", "turn-1", Now.AddMinutes(3))]));
        Assert.Equal(Now.AddMinutes(2), Assert.Single(await store.EvaluateAllAsync(Now.AddMinutes(3))).Session!.LastActivityAtUtc);
        Assert.Equal(2, (await store.HistoryAsync("session", 0, 100, default)).Length);
    }

    [Fact]
    public async Task SourceRechecksRefreshUnchangedFactsWithoutHistoryAndRejectUnjournaledChanges() {
        using var store = new LedgerStore(Database, new(new()));
        await store.InitializeAsync();
        var global = new LedgerEvent { EventId = Guid.NewGuid(), Epoch = Guid.NewGuid(), Sequence = 1,
            MachineId = "synthetic", ReporterId = "reporter", ItemId = "global", Provider = "project", Source = "watcher", ObservedAtUtc = Now,
            GlobalWork = new(Now, [], Now, []) };
        var session = global with { EventId = Guid.NewGuid(), Sequence = 2, ItemId = "session", Provider = "codex", GlobalWork = null,
            Session = new() { TurnOpen = false, Outcome = "completed", TurnEndedAtUtc = Now, RepositoryBranchKey = "branch" } };
        await store.IngestAsync([global, session], Now);
        Assert.Equal("done", Assert.Single(await store.EvaluateAllAsync(Now)).Current!.Value);
        var later = Now.AddHours(2);
        var refresh = global with { EventId = Guid.NewGuid(), ObservedAtUtc = later, GlobalWork = global.GlobalWork! with { ClaimsAtUtc = later, PullRequestsAtUtc = later } };
        await store.IngestAsync([refresh], later, journal: false);
        Assert.Single(await store.HistoryAsync("global", 0, 100, default));
        Assert.Equal(later, Assert.Single(await store.EvaluateAllAsync(later)).Work!.ClaimsAtUtc);
        await Assert.ThrowsAsync<EventConflictException>(() => store.IngestAsync([
            refresh with { GlobalWork = refresh.GlobalWork! with { ActiveClaimItems = ["session"] } }
        ], later, journal: false));
        Assert.False(Assert.Single(await store.EvaluateAllAsync(later)).Work!.ActiveClaim);
        await Assert.ThrowsAsync<ArgumentException>(() => store.IngestAsync([session], later, journal: false));
    }

    [Fact]
    public async Task QuickQueriesBoundTheirOutputAndQualifyLimitsWithoutInventingSharedCredits() {
        using var store = new LedgerStore(Database, new(new()));
        await store.InitializeAsync();
        for (var i = 0; i < 6; i++) await store.AcquireLeaseAsync(new("build:synthetic/" + i, new string('a', 120), "synthetic", new string('p', 90), 600), Now);
        var who = await store.WhoAsync("build:", true, Now);
        Assert.Equal(6, who.Total);
        Assert.NotEmpty(who.Leases);
        Assert.True(JsonSerializer.SerializeToUtf8Bytes(who, LedgerJson.Options).Length < 1024);
        Assert.Empty((await store.WhoAsync("build:synthetic/_", true, Now)).Leases);
        await Assert.ThrowsAsync<ArgumentException>(() => store.AcquireLeaseAsync(new("bench:synthetic", "agent", "synthetic", "bench", -1), Now));
        Assert.Empty((await store.BudgetStatusAsync()).Limits);
        var events = Enumerable.Range(0, 3).Select(i => new LedgerEvent { EventId = Guid.NewGuid(), Epoch = Guid.NewGuid(), Sequence = 1,
            MachineId = "synthetic", ReporterId = "reporter", ItemId = "session-" + i, Provider = "claude", Source = "watcher", ObservedAtUtc = Now,
            Session = new() { SourceSessionId = Guid.NewGuid(), UsageLimit = true, TurnOpen = false, Archived = i == 2 } }).ToArray();
        await store.IngestAsync(events, Now);
        var budget = await store.BudgetStatusAsync();
        var group = Assert.Single(budget.Limits);
        Assert.Null(group.Account);
        Assert.Equal(2, group.Sessions);
        Assert.Contains("credits-and-reset-unknown", budget.Coverage);
        var machine = await store.MachineStatusAsync("synthetic", "healthy", Now);
        Assert.Equal(2, machine.LimitedSessions);
        Assert.Equal(0, machine.LiveSessions);
        Assert.Equal("unavailable", machine.Probe.State);
        Assert.Equal("not-reported", machine.Probe.Reason);
        Assert.Equal(6, machine.TotalLeases);
        Assert.True(JsonSerializer.SerializeToUtf8Bytes(machine, LedgerJson.Options).Length < 1024);
        await store.UpdateReporterHealthAsync(new("synthetic", "reporter", "v1", Now, [], Probe: new("available") {
            ObservedAtUtc = Now, SampledAtUtc = Now.AddMinutes(-3), CpuUse = .5
        }));
        var staleProbe = await store.MachineStatusAsync("synthetic", "healthy", Now);
        Assert.Equal("unavailable", staleProbe.Probe.State);
        Assert.Equal("probe-sample-stale", staleProbe.Probe.Reason);
        Assert.Null(staleProbe.Probe.CpuUse);
        await store.UpdateReporterHealthAsync(new("synthetic", "reporter", "v1", Now, [], Probe: new("available") {
            ObservedAtUtc = Now, SampledAtUtc = Now, CpuUse = .5,
            Volumes = Enumerable.Range(0, 8).Select(i => new ProbeVolumeView("/" + new string('v', 254) + i, 1024, 2048)).ToArray()
        }));
        var boundedProbe = await store.MachineStatusAsync("synthetic", "healthy", Now);
        Assert.True(JsonSerializer.SerializeToUtf8Bytes(boundedProbe, LedgerJson.Options).Length < 1024);
        Assert.Equal("available", boundedProbe.Probe.State);
        Assert.Equal(.5, boundedProbe.Probe.CpuUse);
        Assert.True(boundedProbe.Probe.VolumesCapped);
        Assert.True(boundedProbe.Probe.Volumes!.Length < 8);
        Assert.Equal(6, boundedProbe.TotalLeases);
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
