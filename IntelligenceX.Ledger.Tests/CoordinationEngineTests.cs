using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Coordination;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class CoordinationEngineTests {
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly CoordinationPrincipal Client = new("requester", "client", "unused", ["sample"]);
    private static readonly CoordinationPrincipal Host = new("host", "host", "unused", [], "machine-a");
    private static readonly CoordinationPrincipal Operator = new("operator", "operator", "unused", []);
    private static CoordinationState State() {
        var state = new CoordinationState { Policy = new(1, [new("machine-a", 2, 2, 8, 8192, ["dotnet-build"], [0, 1, 2, 3])]) };
        var engine = new CoordinationEngine(state, Now);
        engine.SetGoal(new("goal", "", 0, false, false, "authorization", ["verified-build"]), Client, 0);
        engine.ObserveHost(new("machine-a", "boot-a", Now, ["dotnet-build"], [0, 1, 2, 3]), Host);
        return state;
    }
    private static CoordinationRequest Request(string id, ResourceDemand? demand = null, string[]? dependencies = null) =>
        new(id, "goal", "build", "dotnet-build", null, null, demand ?? new(BuildSlots: 1, CpuUnits: 4, MemoryMiB: 1024),
            new("sample", "feature/" + id, new string('a', 40), null, "checkpoint", "build"), dependencies ?? [], 1, "authorization");
    private static ExecutionReceipt Receipt(CoordinationWork w, string? result = null, bool? success = null) =>
        new(w.GrantId!, w.Generation, "boot-a", "process-a-start-a", result, success, success is not null);

    [Fact]
    public void EnforcesCapacityAndRetainsExpiredOccupancy() {
        var state = State();
        var e = new CoordinationEngine(state, Now);
        Assert.Equal("admitted", e.Submit(Request("first"), Client).State);
        Assert.Equal("admitted", e.Submit(Request("second"), Client).State);
        Assert.Equal("queued", e.Submit(Request("third"), Client).State);
        var later = new CoordinationEngine(state, Now.AddSeconds(121));
        later.Schedule();
        Assert.Equal("reconcile", state.Work["first"].State);
        Assert.Equal("queued", state.Work["third"].State);
        Assert.Equal(8, Assert.Single(later.Summary(null).Machines).Used.CpuUnits);
    }

    [Fact]
    public void UnavailableFutureAssigneeDoesNotBlockOtherWorkOnItsMachine() {
        var state = State(); var e = new CoordinationEngine(state, Now);
        e.ObserveWorker(new("retired-worker", "machine-a", "codex", false, "archived", Now), Host);
        var unavailable = Request("unavailable", new(AgentSlots: 1, CpuUnits: 1)) with { Kind = "agent", WorkerId = "retired-worker" };
        Assert.Equal("queued", e.Submit(unavailable, Client).State);
        var ready = e.Submit(Request("ready"), Client);
        Assert.Equal("admitted", ready.State);
        Assert.Equal("worker-unavailable", state.Work["unavailable"].Reason);
        Assert.Equal(4, Assert.Single(e.Summary(null).Machines).Used.CpuUnits);
        Assert.Null(state.Work["unavailable"].GrantId);
    }

    [Fact]
    public void ExclusiveBenchmarkDoesNotGetBypassedByLaterBuilds() {
        var state = State(); var e = new CoordinationEngine(state, Now);
        var first = e.Submit(Request("first"), Client);
        var benchmark = Request("benchmark", new(BuildSlots: 1, CpuUnits: 8, Exclusive: true)) with { Kind = "benchmark" };
        Assert.Equal("queued", e.Submit(benchmark, Client).State);
        Assert.Equal("queued", e.Submit(Request("later", new(BuildSlots: 1, CpuUnits: 1)), Client).State);
        e.Receipt("first", "submitted", Receipt(first), Host);
        e.Receipt("first", "started", Receipt(first), Host);
        e.Receipt("first", "terminal", Receipt(first, "result", true), Host);
        Assert.Equal("admitted", state.Work["benchmark"].State);
        Assert.Equal("queued", state.Work["later"].State);
    }

    [Theory]
    [InlineData("boot-b", false)]
    [InlineData("boot-a", true)]
    public void DelayedWorkerObservationRequiresCurrentHostBoot(string heartbeatBoot, bool sameBoot) {
        var state = State();
        var worker = new CoordinationWorker("worker", "machine-a", "codex", true, null, Now, BootId: "boot-a");
        new CoordinationEngine(state, Now).ObserveWorker(worker, Host);
        var delayed = worker with { ObservedAtUtc = Now.AddSeconds(1) };
        new CoordinationEngine(state, Now.AddSeconds(2)).ObserveHost(
            new("machine-a", heartbeatBoot, Now.AddSeconds(2), ["dotnet-build"]), Host);
        var e = new CoordinationEngine(state, Now.AddSeconds(3));
        if (sameBoot) {
            Assert.True(state.Workers[worker.Id].Eligible);
            e.ObserveWorker(delayed, Host);
            Assert.Equal(delayed, state.Workers[worker.Id]);
        } else {
            Assert.False(state.Workers[worker.Id].Eligible);
            Assert.Equal("worker-boot-conflict", Assert.Throws<CoordinationConflictException>(() =>
                e.ObserveWorker(delayed, Host)).Message);
            Assert.False(state.Workers[worker.Id].Eligible);
            Assert.Equal("host-restarted", state.Workers[worker.Id].UnavailableReason);
        }
        var work = e.Submit(Request("delayed", new(AgentSlots: 1, CpuUnits: 1)) with {
            Kind = "agent", WorkerId = worker.Id
        }, Client);
        Assert.Equal(sameBoot ? "admitted" : "queued", work.State);
        if (sameBoot) Assert.Equal(heartbeatBoot, work.BootId);
        else {
            Assert.Equal("worker-unavailable", work.Reason);
            Assert.Null(work.GrantId);
        }
    }

    [Fact]
    public void PolicyDecreaseBlocksNewWorkWithoutTerminatingActiveJob() {
        var state = State(); var e = new CoordinationEngine(state, Now);
        var first = e.Submit(Request("first"), Client);
        e.Receipt("first", "submitted", Receipt(first), Host);
        e.Receipt("first", "started", Receipt(first), Host);
        var m = state.Policy.Machines[0] with { CpuUnits = 2, BuildSlots = 1 };
        e.ReplacePolicy(state.Policy with { Revision = 2, Machines = [m] }, Operator, 1);
        Assert.Equal("started", state.Work["first"].State);
        Assert.Equal("queued", e.Submit(Request("second", new(BuildSlots: 1, CpuUnits: 1)), Client).State);
        Assert.Throws<CoordinationConflictException>(() => e.ReplacePolicy(state.Policy with { Revision = 3 }, Client, 2));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("boot-b", true)]
    [InlineData("boot-a", false)]
    public void EligibleWorkerObservationRequiresAttestedEnrolledBoot(string? bootId, bool hostEnrolled) {
        var state = State();
        if (!hostEnrolled) state.Hosts.Clear();
        var e = new CoordinationEngine(state, Now);
        var worker = new CoordinationWorker("worker", "machine-a", "codex", true, null, Now, BootId: bootId);
        Assert.Equal("worker-boot-conflict", Assert.Throws<CoordinationConflictException>(() =>
            e.ObserveWorker(worker, Host)).Message);
        Assert.Empty(state.Workers);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("boot-b")]
    public void LoadedEligibleWorkerWithoutCurrentHostBootCannotBeAdmitted(string? bootId) {
        var state = State();
        state.Workers.Add("worker", new("worker", "machine-a", "codex", true, null, Now, BootId: bootId));
        var work = new CoordinationEngine(state, Now).Submit(Request("loaded", new(AgentSlots: 1, CpuUnits: 1)) with {
            Kind = "agent", WorkerId = "worker"
        }, Client);
        Assert.Equal("queued", work.State);
        Assert.Equal("worker-unavailable", work.Reason);
        Assert.Null(work.GrantId);
    }

    [Fact]
    public void DependencyRequiresExactRequesterAcceptance() {
        var state = State(); var e = new CoordinationEngine(state, Now);
        var first = e.Submit(Request("first"), Client);
        e.Submit(Request("dependent", dependencies: ["first"]), Client);
        e.Receipt("first", "submitted", Receipt(first), Host);
        e.Receipt("first", "started", Receipt(first), Host);
        e.Receipt("first", "terminal", Receipt(first, "result", true), Host);
        Assert.Equal("queued", state.Work["dependent"].State);
        Assert.Throws<CoordinationConflictException>(() => e.Accept("first", "other-result", Client));
        e.Accept("first", "result", Client);
        Assert.Equal("admitted", state.Work["dependent"].State);
    }

    [Fact]
    public void WorkerCannotClearRetainedStopOrPauseThroughItsCheckpoint() {
        var state = State(); var e = new CoordinationEngine(state, Now);
        var stopped = e.SetGoal(state.Goals["goal"] with { Stopped = true, Paused = true }, Operator, 1);
        Assert.Throws<CoordinationConflictException>(() => e.SetGoal(stopped with { Stopped = false, Paused = false }, Client, stopped.Revision));
        Assert.True(state.Goals["goal"].Stopped);
        Assert.True(state.Goals["goal"].Paused);
        var resumed = e.SetGoal(stopped with { Stopped = false, Paused = false }, Operator, stopped.Revision);
        Assert.False(resumed.Stopped);
        Assert.Equal(stopped.Revision + 1, resumed.Revision);
    }

    [Fact]
    public void StopRejectsFurtherExecutionButAllowsVerifiedTerminalReconciliation() {
        var state = State(); var e = new CoordinationEngine(state, Now);
        var first = e.Submit(Request("first"), Client);
        e.Receipt("first", "submitted", Receipt(first), Host);
        var goal = state.Goals["goal"];
        e.SetGoal(goal with { Stopped = true }, Client, goal.Revision);
        e.Schedule();
        Assert.Equal("reconcile", state.Work["first"].State);
        Assert.Throws<CoordinationConflictException>(() => e.Receipt("first", "started", Receipt(first), Host));
        e.Receipt("first", "terminal", Receipt(first, "cancelled-job", false), Host);
        Assert.Equal("failed", state.Work["first"].State);
        Assert.Equal(0, Assert.Single(e.Summary(null).Machines).Used.CpuUnits);
    }

    [Fact]
    public void RejectsDuplicateIdentityChangesForeignHostsAndStaleReceipts() {
        var state = State(); var e = new CoordinationEngine(state, Now);
        var first = e.Submit(Request("first"), Client);
        Assert.Equal(first, e.Submit(Request("first"), Client));
        Assert.Throws<CoordinationConflictException>(() => e.Submit(Request("first") with { Capability = "different" }, Client));
        Assert.Throws<CoordinationConflictException>(() => e.Receipt("first", "submitted", Receipt(first) with { Generation = 2 }, Host));
        Assert.Throws<CoordinationConflictException>(() => e.Receipt("first", "submitted", Receipt(first), Host with { MachineId = "machine-b" }));
        Assert.Throws<CoordinationConflictException>(() => e.Receipt("first", "started", Receipt(first), Host));
        Assert.Null(state.Work["first"].ExecutionId);
    }
}
