using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Coordination;
using Xunit;
using static IntelligenceX.Ledger.Tests.CoordinationLifecycleTests;

namespace IntelligenceX.Ledger.Tests;

/// <summary>A helper works for as long as it needs: its host's heartbeat renews the grant while the helper runs. Only a
/// host that stops reporting it (the helper or host died), a restart or a stop ends the grant, and admitted work that
/// never started goes back to the queue.</summary>
public sealed class CoordinationLongRunningGrantTests {
    private static readonly CoordinationPrincipal Operator = new("operator", "operator", "unused", []);
    private static CoordinationEngine At(CoordinationState state, double seconds) => new(state, Now.AddSeconds(seconds));

    private static void Heartbeat(CoordinationEngine e, CoordinationState state, double seconds, CoordinationActiveGrant[]? running,
        string machine = "helper-host", string boot = "boot") {
        var principal = machine == "helper-host" ? HelperHost : SourceHost;
        e.ObserveHost(new(machine, boot, Now.AddSeconds(seconds), state.Policy.Machines.Single(m => m.Id == machine).Capabilities) {
            ActiveGrants = running
        }, principal);
    }

    private static CoordinationActiveGrant Running(CoordinationWork work) => new(work.Request.Id, work.Generation, work.GrantId!);

    private static (CoordinationState State, CoordinationWork Work) StartedHelper() {
        var state = CreateState();
        var e = new CoordinationEngine(state, Now);
        var work = e.Submit(Help(), Requester);
        Start(e, state, work);
        return (state, state.Work["help"]);
    }

    [Fact]
    public void ARunningHelperReportedByItsHostKeepsItsGrantFarPastTheGrantWindow() {
        var (state, work) = StartedHelper();
        var window = state.Policy.GrantSeconds;
        // Two hours of heartbeats, each well inside the window, with no renew receipt at all.
        for (var t = 30.0; t <= 7200; t += window / 2.0) {
            var e = At(state, t);
            Heartbeat(e, state, t, [Running(work)]);
            e.ObserveWorker(RequesterWorker with { ObservedAtUtc = Now.AddSeconds(t) }, SourceHost);
        }
        var current = state.Work["help"];
        Assert.Equal("started", current.State);
        Assert.Null(current.Reason);
        Assert.Equal(work.GrantId, current.GrantId);
        Assert.True(current.RenewByUtc > Now.AddSeconds(7200));
    }

    [Fact]
    public void AHelperItsLiveHostNoLongerReportsIsStillReconciledWhenTheGrantLapses() {
        var (state, work) = StartedHelper();
        var window = state.Policy.GrantSeconds;
        Heartbeat(At(state, 30), state, 30, [Running(work)]);
        // The host keeps answering but the helper is gone from its list.
        for (var t = 60.0; t <= 30 + window + 30; t += 30) Heartbeat(At(state, t), state, t, []);
        var current = state.Work["help"];
        Assert.Equal("reconcile", current.State);
        Assert.Equal("grant-overdue", current.Reason);
    }

    [Fact]
    public void AHelperWhoseHostStoppedAnsweringIsReconciledWhenTheGrantLapses() {
        var (state, _) = StartedHelper();
        At(state, state.Policy.GrantSeconds + 1).Schedule();
        Assert.Equal("grant-overdue", state.Work["help"].Reason);
    }

    [Fact]
    public void ARestartedHostStillEndsTheGrantItReportsUnderItsOldBoot() {
        var (state, work) = StartedHelper();
        Heartbeat(At(state, 30), state, 30, [Running(work)], boot: "boot-2");
        Assert.Equal("reconcile", state.Work["help"].State);
        Assert.Equal("boot-changed", state.Work["help"].Reason);
    }

    [Fact]
    public void AHeartbeatDoesNotRenewAnotherGrantOrGeneration() {
        var (state, work) = StartedHelper();
        var before = state.Work["help"].RenewByUtc;
        Heartbeat(At(state, 30), state, 30, [new(work.Request.Id, work.Generation, "other-grant"),
            new(work.Request.Id, work.Generation + 1, work.GrantId!)]);
        Assert.Equal(before, state.Work["help"].RenewByUtc);
    }

    [Fact]
    public void AdmittedWorkThatNeverStartedReturnsToTheQueueInsteadOfFailing() {
        var state = CreateState();
        var e = new CoordinationEngine(state, Now);
        var admitted = e.Submit(Help(), Requester);
        Assert.Equal("admitted", admitted.State);
        var lapse = state.Policy.GrantSeconds + 1;
        // The host is alive but never took the work up.
        var later = At(state, lapse);
        Heartbeat(later, state, lapse, []);
        var requeued = state.Work["help"];
        Assert.NotEqual("reconcile", requeued.State);
        Assert.Null(state.Reconciliations.GetValueOrDefault("help"));
        Assert.Contains("requeued", requeued.Stages!.Keys);
        // The worker it was assigned is fresh again, so the same pass admits it with a new grant.
        later.ObserveWorker(state.Workers["helper-worker"] with { ObservedAtUtc = Now.AddSeconds(lapse) }, HelperHost);
        var again = state.Work["help"];
        Assert.Equal("admitted", again.State);
        Assert.True(again.Generation > admitted.Generation);
        Assert.NotEqual(admitted.GrantId, again.GrantId);
    }

    [Fact]
    public void WorkItsHostHasTakenUpIsNeitherRenewedNorRequeuedWhenItIsNotRunning() {
        var state = CreateState();
        var admitted = new CoordinationEngine(state, Now).Submit(Help(), Requester);
        // The host saved a run record (it may have created a thread) but no run is live.
        var held = new CoordinationActiveGrant("help", admitted.Generation, admitted.GrantId!, Running: false);
        Heartbeat(At(state, 30), state, 30, [held]);
        Assert.Equal(admitted.RenewByUtc, state.Work["help"].RenewByUtc);
        var lapse = state.Policy.GrantSeconds + 1;
        Heartbeat(At(state, lapse), state, lapse, [held]);
        Assert.Equal("reconcile", state.Work["help"].State);
        Assert.Equal("grant-overdue", state.Work["help"].Reason);
    }

    [Fact]
    public void RequeuedWorkCanStillBeCancelledAtItsCurrentGeneration() {
        var state = CreateState();
        var admitted = new CoordinationEngine(state, Now).Submit(Help(), Requester);
        var lapse = state.Policy.GrantSeconds + 1;
        var later = At(state, lapse);
        // Its worker hasn't been seen since, so the requeued work stays queued.
        Heartbeat(later, state, lapse, []);
        var requeued = state.Work["help"];
        Assert.Equal("queued", requeued.State);
        Assert.Equal(admitted.Generation, requeued.Generation);
        Assert.Throws<CoordinationConflictException>(() => later.Cancel("help", new(0, "not-needed"), Requester));
        Assert.Equal("cancelled", later.Cancel("help", new(requeued.Generation, "not-needed"), Requester).State);
    }

    [Fact]
    public void QueuedWorkWaitsForCapacityWithoutExpiring() {
        var state = CreateState();
        var e = new CoordinationEngine(state, Now);
        e.ReplacePolicy(state.Policy with { Revision = 2, Machines = state.Policy.Machines.Select(m =>
            m.Id == "helper-host" ? m with { AgentSlots = 1 } : m).ToArray() }, Operator, 1);
        var first = e.Submit(Help("first") with { ReadOnly = true }, Requester);
        Start(e, state, first);
        Assert.Equal("queued", e.Submit(Help("second") with { ReadOnly = true }, Requester).State);
        for (var t = 30.0; t <= 4 * 3600; t += 30) {
            var later = At(state, t);
            Heartbeat(later, state, t, [Running(state.Work["first"])]);
            later.ObserveWorker(RequesterWorker with { ObservedAtUtc = Now.AddSeconds(t) }, SourceHost);
        }
        Assert.Equal("queued", state.Work["second"].State);
        Assert.Equal(0, state.Work["second"].Generation);
        Assert.Equal("started", state.Work["first"].State);
    }

    [Fact]
    public void ABusyMachineKeepsItsHelperAndAdmitsNoSecondOneUntilTheLoadClears() {
        var state = new CoordinationState { Policy = Policy with { Machines = Policy.Machines.Select(m => m.Id == "helper-host"
            ? m with { AgentSlots = 4, CpuUnits = 16, AllowedCreateProviders = ["provider-a"] } : m).ToArray() } };
        var e = new CoordinationEngine(state, Now);
        e.SetGoal(new("task", "", 0, false, false, "authorization", ["result-verified"]), Requester, 0);
        void Observe(CoordinationEngine engine, double t, string? hold, CoordinationActiveGrant[]? running = null) {
            foreach (var host in new[] { SourceHost, HelperHost })
                engine.ObserveHost(new(host.MachineId!, "boot", Now.AddSeconds(t), state.Policy.Machines.Single(m => m.Id == host.MachineId).Capabilities,
                    CreatableProviders: host == HelperHost ? ["provider-a"] : null) {
                    LoadHold = host == HelperHost ? hold : null, ActiveGrants = host == HelperHost ? running ?? [] : null }, host);
            engine.ObserveWorker(RequesterWorker with { ObservedAtUtc = Now.AddSeconds(t) }, SourceHost);
        }
        Observe(e, 0, "memory-pressure");
        CoordinationRequest Create(string id) => Help(id) with { ReadOnly = true, Resources = new(AgentSlots: 1, CpuUnits: 1) };
        // One helper is always allowed, so a long sequence of work can run on a busy machine.
        Assert.Equal("admitted", e.Submit(Create("train"), Requester).State);
        Assert.Equal("queued", e.Submit(Create("other"), Requester).State);
        Assert.Equal("machine-busy", state.Work["other"].Reason);
        var train = state.Work["train"];
        // Load never stops the running helper, however long it works.
        for (var t = 30.0; t <= 3 * 3600; t += 30) Observe(At(state, t), t, "sustained-cpu", [new(train.Request.Id, train.Generation, train.GrantId!)]);
        Assert.Equal("admitted", state.Work["train"].State);
        Assert.Equal("queued", state.Work["other"].State);
        var clear = 3 * 3600 + 30.0;
        Observe(At(state, clear), clear, null, [new(train.Request.Id, train.Generation, train.GrantId!)]);
        Assert.Equal("admitted", state.Work["other"].State);
    }

    [Fact]
    public void FourAgentSlotsAdmitFourHelpersAtOnceOnOneMachine() {
        var state = new CoordinationState { Policy = Policy with { Machines = Policy.Machines.Select(m => m.Id == "helper-host"
            ? m with { AgentSlots = 4, CpuUnits = 16, AllowedCreateProviders = ["provider-a"] } : m).ToArray() } };
        var e = new CoordinationEngine(state, Now);
        e.SetGoal(new("task", "", 0, false, false, "authorization", ["result-verified"]), Requester, 0);
        foreach (var host in new[] { SourceHost, HelperHost })
            e.ObserveHost(new(host.MachineId!, "boot", Now, state.Policy.Machines.Single(m => m.Id == host.MachineId).Capabilities,
                CreatableProviders: host == HelperHost ? ["provider-a"] : null), host);
        e.ObserveWorker(RequesterWorker, SourceHost);
        CoordinationRequest Create(string id) => Help(id) with { ReadOnly = true, Resources = new(AgentSlots: 1, CpuUnits: 1) };
        var admitted = Enumerable.Range(1, 5).Select(i => e.Submit(Create("helper-" + i), Requester)).ToArray();
        Assert.Equal(4, admitted.Count(w => w.State == "admitted" && w.Machine == "helper-host"));
        Assert.Equal("queued", state.Work["helper-5"].State);
        Assert.Equal("capacity", state.Work["helper-5"].Reason);
        Assert.Equal(4, e.Summary(null).Machines.Single(m => m.Id == "helper-host").Used.AgentSlots);
        // Each gets its own grant, so each helper creates its own thread and workspace.
        Assert.Equal(4, admitted.Where(w => w.State == "admitted").Select(w => w.GrantId).Distinct().Count());
    }

    [Fact]
    public void TheRunningGrantsTravelWithTheHostObservation() {
        var host = new CoordinationHost("helper-host", "boot", Now, ["platform-help"]) { ActiveGrants = [new("help", 2, "grant")] };
        var json = System.Text.Json.JsonSerializer.Serialize(host, LedgerReadJson.Default.CoordinationHost);
        var read = System.Text.Json.JsonSerializer.Deserialize(json, LedgerReadJson.Default.CoordinationHost)!;
        Assert.Equal(host.ActiveGrants, read.ActiveGrants);
        Assert.DoesNotContain("activeGrants", System.Text.Json.JsonSerializer.Serialize(host with { ActiveGrants = null },
            LedgerReadJson.Default.CoordinationHost), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("capacity", true)]
    [InlineData("machine-busy", true)]
    [InlineData("machine-unavailable", true)]
    [InlineData("goal-held-or-changed", false)]
    [InlineData("requester-held", false)]
    [InlineData("dependency-not-accepted", false)]
    public void TheQueueNoticeSaysNothingNeedsDoingOnlyForMachineWaits(string reason, bool startsOnItsOwn) {
        var text = CoordinationEngine.QueuedNotice(reason);
        Assert.Equal(startsOnItsOwn, text.Contains("starts on its own", StringComparison.Ordinal));
        Assert.Equal(startsOnItsOwn, text.Contains("Nothing needs doing", StringComparison.Ordinal));
    }
}
