using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Storage;
using DBAClientX;
using System.Text.Json;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class ResourcePolicyTests : IDisposable {
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private readonly string _root = LedgerTestPaths.NewDirectory("resource-policy");
    private string Database => Path.Combine(_root, "ledger.sqlite");
    private static CoordinationPolicy Policy => new(1, [new("synthetic", 4, 2, 8, 8192, ["build"])]);

    [Fact]
    public async Task CountedQueueSurvivesRestartPromotesInOrderAndCancelsByExactId() {
        var policy = Policy;
        Lease[] active;
        LeaseReply[] queued;
        using (var store = new LedgerStore(Database, new(new()), _ => Task.FromResult(policy))) {
            await store.InitializeAsync();
            var replies = await Task.WhenAll(Enumerable.Range(0, 4).Select(i => store.AcquireLeaseAsync(
                new("build:synthetic", "worker-" + i, "synthetic", "compile", 600), Now)));
            active = replies.Where(reply => reply.Ok).Select(reply => reply.Lease!).ToArray();
            queued = replies.Where(reply => !reply.Ok).OrderBy(reply => reply.QueuePosition).ToArray();
            Assert.Equal(2, active.Length);
            Assert.Equal(new int?[] { 1, 2 }, queued.Select(reply => reply.QueuePosition));
            Assert.All(queued, reply => { Assert.Equal("queued", reply.State); Assert.Equal(600, reply.EstimatedWaitSeconds); });
            Assert.Equal(2, (await store.WhoAsync("build:synthetic", false, Now)).Total);
        }
        using var restarted = new LedgerStore(Database, new(new()), _ => Task.FromResult(policy));
        await restarted.InitializeAsync();
        Assert.True((await restarted.ReleaseLeaseAsync(new(active[0].Key, active[0].LeaseId), Now.AddSeconds(1))).Ok);
        var holders = await restarted.WhoAsync(active[0].Key, false, Now.AddSeconds(1));
        Assert.Equal(2, holders.Total);
        Assert.Contains(holders.Leases, lease => lease.LeaseId == queued[0].Lease!.LeaseId && lease.AcquiredAtUtc == Now.AddSeconds(1));
        Assert.DoesNotContain(holders.Leases, lease => lease.LeaseId == queued[1].Lease!.LeaseId);
        Assert.True((await restarted.ReleaseLeaseAsync(new(queued[1].Lease!.Key, queued[1].Lease!.LeaseId), Now.AddSeconds(1))).Ok);
        await restarted.MaintainLeasesAsync(Now.AddMinutes(11));
        Assert.Empty((await restarted.WhoAsync(active[0].Key, false, Now.AddMinutes(11))).Leases);
    }

    [Theory]
    [InlineData("agent", 30)]
    [InlineData("human", 1440)]
    [InlineData("unknown", 1440)]
    public async Task BranchTakeoverUsesOriginAndFencesThePreviousHolder(string origin, int idleMinutes) {
        var policy = Policy;
        policy.Lease.MaximumTtlMinutes["branch"] = 2880;
        using var store = new LedgerStore(Database, new(new()), _ => Task.FromResult(policy));
        await store.InitializeAsync();
        await Session(store, "first", origin, Now);
        var first = (await store.AcquireLeaseAsync(new("branch:example/repo:feature", "first", "synthetic", "edit", 172800), Now)).Lease!;
        var denied = await store.AcquireLeaseAsync(new(first.Key, "second", "synthetic", "continue", 600), Now.AddMinutes(idleMinutes).AddSeconds(-1));
        Assert.Equal("busy", denied.State);
        Assert.Equal(Now, denied.HolderLastActivityAtUtc);
        Assert.Equal(Now.AddMinutes(idleMinutes), denied.TakeoverAtUtc);
        var taken = await store.AcquireLeaseAsync(new(first.Key, "second", "synthetic", "continue", 600), Now.AddMinutes(idleMinutes));
        Assert.True(taken.Ok);
        Assert.Equal("first", taken.Lease!.PreviousHolder);
        Assert.Equal(Now.AddMinutes(idleMinutes), taken.Lease.TakenOverAtUtc);
        Assert.False((await store.ReleaseLeaseAsync(new(first.Key, first.LeaseId), Now.AddMinutes(idleMinutes))).Ok);
        Assert.False((await store.RenewLeaseAsync(new(first.Key, first.LeaseId, 600), Now.AddMinutes(idleMinutes))).Ok);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BenchmarkExcludesHeavyWorkWithoutBlockingBranchLeasesAndRenewalRequiresLiveEvidence(bool remoteResource) {
        var policy = Policy with { Machines = [.. Policy.Machines, new("synthetic-other", 1, 1, 4, 8192, ["build"])] };
        var machine = remoteResource ? "synthetic-other" : "synthetic";
        using var store = new LedgerStore(Database, new(new()), _ => Task.FromResult(policy));
        await store.InitializeAsync();
        var bench = await store.AcquireLeaseAsync(new("bench:" + machine, "bench-worker", machine, "measure", 600), Now);
        Assert.True(bench.Ok);
        Assert.True((await store.AcquireLeaseAsync(new("branch:example/repo:feature", "editor", "synthetic", "edit", 600), Now)).Ok);
        Assert.Equal("queued", (await store.AcquireLeaseAsync(new("build:" + machine, "builder", machine, "compile", 600), Now)).State);
        Assert.Equal("holder-not-live", (await store.RenewLeaseAsync(new(bench.Lease!.Key, bench.Lease.LeaseId, 60), Now)).State);
        await Session(store, "bench-worker", "agent", Now);
        Assert.True((await store.RenewLeaseAsync(new(bench.Lease.Key, bench.Lease.LeaseId, 60), Now)).Ok);
        Assert.True((await store.ReleaseLeaseAsync(new(bench.Lease.Key, bench.Lease.LeaseId), Now)).Ok);
        Assert.Single((await store.WhoAsync("build:" + machine, false, Now)).Leases);
    }

    [Fact]
    public async Task ProbePersistenceAndPolicyChangesControlHeavyAdmissionWithoutRawEvents() {
        var policy = Policy;
        using var store = new LedgerStore(Database, new(new()), _ => Task.FromResult(policy));
        await store.InitializeAsync();
        async Task Observe(DateTimeOffset at, double cpu) => await store.UpdateReporterHealthAsync(new("synthetic", "reporter", "v1", at, []) {
            Probe = new("available") { CpuUse = cpu, SampledAtUtc = at, ObservedAtUtc = at }
        });
        await Observe(Now, .96);
        Assert.Equal("yes", (await store.MachineStatusAsync("synthetic", "healthy", Now)).Resources!.CanStartHeavy);
        await Observe(Now.AddMinutes(2), .96);
        var loaded = (await store.MachineStatusAsync("synthetic", "healthy", Now.AddMinutes(2))).Resources!;
        Assert.Equal("no", loaded.CanStartHeavy);
        Assert.Equal("sustained-cpu", loaded.Reason);
        for (var minute = 4; minute <= 16; minute += 2) await Observe(Now.AddMinutes(minute), .96);
        Assert.True((await store.MachineStatusAsync("synthetic", "healthy", Now.AddMinutes(16))).Resources!.SustainedHighCpu);
        policy = policy with { Revision = 2, Machines = [policy.Machines[0] with { BuildSlots = 3 }] };
        var edited = (await store.MachineStatusAsync("synthetic", "healthy", Now.AddMinutes(16))).Resources!;
        Assert.Equal(3, edited.BuildSlots);
        Assert.Equal("no", edited.CanStartHeavy);
        Assert.True(edited.SustainedHighCpu);
        await Observe(Now.AddMinutes(16.5), .96);
        Assert.True((await store.MachineStatusAsync("synthetic", "healthy", Now.AddMinutes(16.5))).Resources!.SustainedHighCpu);
        await Observe(Now.AddMinutes(17), .1);
        Assert.Equal("yes", (await store.MachineStatusAsync("synthetic", "healthy", Now.AddMinutes(17))).Resources!.CanStartHeavy);
        Assert.Equal(3, (await store.MachineStatusAsync("synthetic", "healthy", Now.AddMinutes(17))).Resources!.BuildSlots);
        Assert.Empty(await store.HistoryAsync("reporter", 0, 100, default));
    }

    [Fact]
    public async Task DistinctTaskKeysShareTheMachineBuildLimit() {
        using var store = new LedgerStore(Database, new(new()), _ => Task.FromResult(Policy));
        await store.InitializeAsync();
        Assert.True((await store.AcquireLeaseAsync(new("build:example/a", "first", "synthetic", "compile", 600), Now)).Ok);
        Assert.True((await store.AcquireLeaseAsync(new("build:example/b", "second", "synthetic", "compile", 600), Now)).Ok);
        Assert.Equal("queued", (await store.AcquireLeaseAsync(new("build:example/c", "third", "synthetic", "compile", 600), Now)).State);
    }

    [Fact]
    public async Task QueuedPromotionUsesTheCurrentMaximumTtl() {
        var policy = Policy;
        using var store = new LedgerStore(Database, new(new()), _ => Task.FromResult(policy));
        await store.InitializeAsync();
        var first = (await store.AcquireLeaseAsync(new("build:synthetic", "first", "synthetic", "compile", 600), Now)).Lease!;
        await store.AcquireLeaseAsync(new("build:synthetic", "second", "synthetic", "compile", 600), Now);
        var waiting = await store.AcquireLeaseAsync(new("build:synthetic", "third", "synthetic", "compile", 3600), Now);
        Assert.Equal("queued", waiting.State);
        policy.Lease.MaximumTtlMinutes["build"] = 5;
        await store.ReleaseLeaseAsync(new(first.Key, first.LeaseId), Now.AddSeconds(1));
        var promoted = Assert.Single((await store.WhoAsync(first.Key, false, Now.AddSeconds(1))).Leases, lease => lease.Holder == "third");
        Assert.Equal(Now.AddSeconds(301), promoted.ExpiresAtUtc);
    }

    [Fact]
    public async Task ExpiryAndReacquisitionShareTheContinuousBenchmarkBudget() {
        using var store = new LedgerStore(Database, new(new()), _ => Task.FromResult(Policy));
        await store.InitializeAsync();
        Assert.True((await store.AcquireLeaseAsync(new("bench:synthetic", "first", "synthetic", "measure", 7200), Now)).Ok);
        var afterExpiry = await store.AcquireLeaseAsync(new("bench:synthetic", "second", "synthetic", "measure", 3600), Now.AddHours(2));
        Assert.Equal("queued", afterExpiry.State);
        Assert.Equal("benchmark-budget", afterExpiry.Reason);
    }

    [Fact]
    public async Task AnEarlierBenchmarkDrainsHeavyWorkBeforeLaterBuilds() {
        using var store = new LedgerStore(Database, new(new()), _ => Task.FromResult(Policy));
        await store.InitializeAsync();
        var first = (await store.AcquireLeaseAsync(new("build:synthetic", "first", "synthetic", "compile", 600), Now)).Lease!;
        var second = (await store.AcquireLeaseAsync(new("build:synthetic", "second", "synthetic", "compile", 600), Now)).Lease!;
        var bench = await store.AcquireLeaseAsync(new("bench:synthetic", "bench", "synthetic", "measure", 600), Now);
        var build = await store.AcquireLeaseAsync(new("build:synthetic", "third", "synthetic", "compile", 600), Now);
        await store.ReleaseLeaseAsync(new(first.Key, first.LeaseId), Now.AddSeconds(1));
        Assert.Single((await store.WhoAsync(first.Key, false, Now.AddSeconds(1))).Leases);
        await store.ReleaseLeaseAsync(new(second.Key, second.LeaseId), Now.AddSeconds(2));
        Assert.Equal(bench.Lease!.LeaseId, Assert.Single((await store.WhoAsync("bench:synthetic", false, Now.AddSeconds(2))).Leases).LeaseId);
        Assert.Empty((await store.WhoAsync("build:synthetic", false, Now.AddSeconds(2))).Leases);
        Assert.True((await store.ReleaseLeaseAsync(new(build.Lease!.Key, build.Lease.LeaseId), Now.AddSeconds(2))).Ok);
    }

    [Fact]
    public async Task LegacyBenchmarkKeepsItsIdAndUnknownStartAndCanShortenWithoutInventingBudget() {
        var now = DateTimeOffset.UtcNow;
        var legacyId = Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(_root);
        using (var sqlite = new SQLite()) {
            await using var db = await sqlite.OpenSessionAsync(Database);
            await db.ExecuteNonQueryAsync("CREATE TABLE leases(key TEXT PRIMARY KEY,id TEXT NOT NULL,machine TEXT NOT NULL,expires TEXT NOT NULL,json TEXT NOT NULL)");
            var json = JsonSerializer.Serialize(new { key = "bench:synthetic", holder = "first", machine = "synthetic", purpose = "measure", leaseId = legacyId, expiresAtUtc = now.AddMinutes(10) });
            await db.ExecuteNonQueryAsync("INSERT INTO leases VALUES(@key,@id,@machine,@expires,@json)", new Dictionary<string, object?> {
                ["key"] = "bench:synthetic", ["id"] = legacyId, ["machine"] = "synthetic", ["expires"] = now.AddMinutes(10).ToString("O"), ["json"] = json
            });
        }
        using var store = new LedgerStore(Database, new(new()), _ => Task.FromResult(Policy));
        await store.InitializeAsync();
        var restored = Assert.Single((await store.WhoAsync("bench:synthetic", false, now)).Leases);
        Assert.Equal(legacyId, restored.LeaseId);
        Assert.Null(restored.AcquiredAtUtc);
        await Session(store, "first", "agent", now);
        Assert.True((await store.RenewLeaseAsync(new(restored.Key, legacyId, 60), now)).Ok);
        var extended = await store.RenewLeaseAsync(new(restored.Key, legacyId, 600), now);
        Assert.Equal("lease-start-unobserved", extended.State);
        await store.ReleaseLeaseAsync(new(restored.Key, legacyId), now.AddSeconds(1));
        var next = await store.AcquireLeaseAsync(new(restored.Key, "second", "synthetic", "measure", 600), now.AddSeconds(1));
        Assert.Equal("queued", next.State);
        Assert.Equal("benchmark-history-unqualified", next.Reason);
    }

    [Theory]
    [InlineData(false, "acquired")]
    [InlineData(true, "queued")]
    public async Task FreshAdmissionUsesTheSameAdjustedBenchmarkBudgetAsPromotion(bool lowerTtl, string expected) {
        var policy = Policy with { Bench = new(MaxPerDayHours: 1) };
        using var store = new LedgerStore(Database, new(new()), _ => Task.FromResult(policy));
        await store.InitializeAsync();
        await store.AcquireLeaseAsync(new("bench:synthetic", "earlier-bench", "synthetic", "measure", 3300), Now);
        var current = Now.AddMinutes(56).AddSeconds(1);
        await store.AcquireLeaseAsync(new("build:synthetic", "first", "synthetic", "compile", 600), current);
        Assert.Equal("queued", (await store.AcquireLeaseAsync(new("bench:synthetic", "waiting", "synthetic", "measure", 600), current)).State);
        if (lowerTtl) policy.Lease.MaximumTtlMinutes["bench"] = 1;
        Assert.Equal(expected, (await store.AcquireLeaseAsync(new("build:synthetic", "second", "synthetic", "compile", 600), current)).State);
    }

    [Fact]
    public async Task BranchHandoffRequiresPushedEvidenceAndFencesRetriesAcrossMachines() {
        using var store = new LedgerStore(Database, new(new()), _ => Task.FromResult(Policy));
        await store.InitializeAsync();
        await Session(store, "first", "human", Now);
        var first = (await store.AcquireLeaseAsync(new("branch:example/repo:feature", "first", "synthetic", "edit", 600), Now)).Lease!;
        var handoff = new LeaseHandoff(new('a', 40), "Run the platform checks", "https://example.test/pull/1");
        var command = new LeaseAcquire(first.Key, "second", "synthetic-other", "platform-help", 600, first.LeaseId, handoff);
        Assert.Equal("checkpoint-unverified", (await store.AcquireLeaseAsync(command, Now)).State);
        var evidence = new LedgerEvent { EventId = Guid.NewGuid(), Epoch = Guid.NewGuid(), Sequence = 1,
            MachineId = "synthetic", ReporterId = "reporter", ItemId = "first", Provider = "codex", Source = "watcher", ObservedAtUtc = Now.AddSeconds(1),
            Session = new() { Origin = "human", TurnOpen = true, LastActivityAtUtc = Now.AddSeconds(1), Repository =
                new("example/repo", "feature", "synthetic-checkout", "Synthetic work", [], [], false, "origin/main", "observed",
                    Dirty: false, Unpushed: true, Commit: handoff.Commit) } };
        await store.IngestAsync([evidence], Now.AddSeconds(1));
        Assert.Equal("checkpoint-unverified", (await store.AcquireLeaseAsync(command, Now.AddSeconds(1))).State);
        await store.IngestAsync([evidence with { EventId = Guid.NewGuid(), Sequence = 2, ObservedAtUtc = Now.AddSeconds(2),
            Session = evidence.Session! with { Repository = evidence.Session.Repository! with { Unpushed = false } } }], Now.AddSeconds(2));
        var moved = await store.AcquireLeaseAsync(command, Now.AddSeconds(2));
        Assert.True(moved.Ok);
        Assert.Equal("synthetic-other", moved.Lease!.Machine);
        Assert.Equal(handoff, moved.Lease.Handoff);
        Assert.Equal(first.LeaseId, moved.Lease.PreviousLeaseId);
        Assert.Equal(moved.Lease.LeaseId, (await store.AcquireLeaseAsync(command, Now.AddSeconds(3))).Lease!.LeaseId);
        Assert.Equal("lease-conflict", (await store.AcquireLeaseAsync(command with { Handoff = handoff with { NextStep = "Different work" } }, Now.AddSeconds(3))).State);
        Assert.False((await store.ReleaseLeaseAsync(new(first.Key, first.LeaseId), Now.AddSeconds(3))).Ok);
        var change = Assert.Single((await store.SessionStatusAsync("first", Now.AddSeconds(3))).Takeovers!);
        Assert.Equal("second", change.Holder);
        Assert.Equal(Now.AddSeconds(2), change.AtUtc);
    }

    private static Task<Guid[]> Session(LedgerStore store, string holder, string origin, DateTimeOffset at) => store.IngestAsync([new() {
        EventId = Guid.NewGuid(), Epoch = Guid.NewGuid(), Sequence = 1, MachineId = "synthetic", ReporterId = "reporter",
        ItemId = holder, Provider = "codex", Source = "watcher", ObservedAtUtc = at,
        Session = new() { Origin = origin, LastActivityAtUtc = at, TurnOpen = true }
    }], at);

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
