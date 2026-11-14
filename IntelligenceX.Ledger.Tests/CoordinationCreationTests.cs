using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Coordination;
using Xunit;
using static IntelligenceX.Ledger.Tests.CoordinationLifecycleTests;

namespace IntelligenceX.Ledger.Tests;

public sealed class CoordinationCreationTests {
    private static CoordinationPolicy CreationPolicy(int slots = 1) => Policy with {
        Machines = Policy.Machines.Select(m => m.Id == "helper-host"
            ? m with { AgentSlots = slots, AllowedCreateProviders = ["provider-a"] } : m).ToArray()
    };

    private static void Enroll(CoordinationEngine e, CoordinationState state, string[]? creatableProviders) {
        e.SetGoal(new("task", "", 0, false, false, "authorization", ["result-verified"]), Requester, 0);
        foreach (var host in new[] { SourceHost, HelperHost })
            e.ObserveHost(new(host.MachineId!, "boot", Now,
                state.Policy.Machines.Single(m => m.Id == host.MachineId).Capabilities,
                CreatableProviders: host == HelperHost ? creatableProviders : null), host);
        e.ObserveWorker(RequesterWorker, SourceHost);
    }

    private static CoordinationState Creator(int slots = 1, string[]? allowed = null, string[]? observed = null) {
        var policy = CreationPolicy(slots);
        if (allowed is not null)
            policy = policy with { Machines = policy.Machines.Select(m => m.Id == "helper-host" ? m with { AllowedCreateProviders = allowed } : m).ToArray() };
        var state = new CoordinationState { Policy = policy };
        Enroll(new(state, Now), state, observed ?? ["provider-a"]);
        return state;
    }

    private static CoordinationRequest Request(string id = "create", string kind = "help") => Help(id) with {
        Kind = kind, Capability = kind == "review" ? "review" : "platform-help", ReadOnly = true,
        Resources = new(AgentSlots: 1, CpuUnits: 1)
    };

    private static CoordinationWorker NewWorker(string id = "new-worker") => new(id, "helper-host", "provider-a", true, null,
        Now.AddSeconds(1), "session-" + id, "workspace-" + id, Candidate, BootId: "boot", TemporaryWorker: true);

    [Theory]
    [InlineData("help")]
    [InlineData("review")]
    public void OneSlotReservesOnlyOneCreationBeforeAnyNativeWorkerExists(string kind) {
        var state = Creator(); var e = new CoordinationEngine(state, Now);
        var first = e.Submit(Request(kind: kind), Requester);
        Assert.Equal("admitted", first.State);
        Assert.Equal("worker-creation-required", first.Reason);
        Assert.Equal("provider-a", first.Provider);
        Assert.Null(first.Assignee);
        Assert.Null(first.ExecutionId);
        Assert.Empty(state.Bindings);
        Assert.Equal(first, e.Submit(Request(kind: kind), Requester));
        Assert.Equal("queued", e.Submit(Request("second", kind), Requester).State);
        Assert.Equal("capacity", state.Work["second"].Reason);
        Assert.Equal(1, e.Summary(null).Machines.Single(m => m.Id == "helper-host").Used.AgentSlots);
        Assert.Single(e.Changes, c => c.Stage == "admitted");
    }

    [Theory]
    [InlineData("no-policy")]
    [InlineData("empty-policy")]
    [InlineData("other-policy-provider")]
    [InlineData("unknown-creator")]
    [InlineData("unavailable-creator")]
    [InlineData("other-observed-provider")]
    [InlineData("stale-host")]
    [InlineData("disabled-host")]
    [InlineData("missing-capability")]
    public void CreationRequiresExplicitPolicyAndFreshMatchingObservedCapability(string condition) {
        var state = Creator();
        var machine = state.Policy.Machines.Single(m => m.Id == "helper-host");
        machine = condition switch {
            "no-policy" => machine with { AllowedCreateProviders = null },
            "empty-policy" => machine with { AllowedCreateProviders = [] },
            "other-policy-provider" => machine with { AllowedCreateProviders = ["provider-b"] },
            "disabled-host" => machine with { Enabled = false },
            _ => machine
        };
        var e = new CoordinationEngine(state, Now);
        e.ReplacePolicy(state.Policy with { Revision = 2,
            Machines = state.Policy.Machines.Select(m => m.Id == machine.Id ? machine : m).ToArray()
        }, new("operator", "operator", "unused", []), 1);
        if (condition is "unknown-creator" or "unavailable-creator" or "other-observed-provider" or "missing-capability") {
            var host = state.Hosts["helper-host"];
            e.ObserveHost(host with { ObservedAtUtc = Now.AddSeconds(1),
                CreatableProviders = condition switch {
                    "unknown-creator" => null, "unavailable-creator" => [],
                    "other-observed-provider" => ["provider-b"], _ => host.CreatableProviders
                }, Capabilities = condition == "missing-capability" ? ["review"] : host.Capabilities
            }, HelperHost);
        }
        if (condition == "stale-host") {
            var later = Now.AddSeconds(121); e = new CoordinationEngine(state, later);
            e.ObserveHost(state.Hosts["source-host"] with { ObservedAtUtc = later }, SourceHost);
            e.ObserveWorker(RequesterWorker with { ObservedAtUtc = later }, SourceHost);
        }
        var result = e.Submit(Request(), Requester);
        Assert.Equal("queued", result.State);
        Assert.Null(result.GrantId);
        Assert.Null(result.Provider);
        Assert.Equal(0, e.Summary(null).Machines.Single(m => m.Id == "helper-host").Used.AgentSlots);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CreationNeverOverridesAnExplicitUnknownOrUnavailableWorker(bool enrolled) {
        var state = Creator(); var e = new CoordinationEngine(state, Now);
        if (enrolled) e.ObserveWorker(NewWorker("named-worker") with { Eligible = false, UnavailableReason = "usage-limited" }, HelperHost);
        var result = e.Submit(Request() with { WorkerId = "named-worker" }, Requester);
        Assert.Equal("queued", result.State);
        Assert.Equal("worker-unavailable", result.Reason);
        Assert.Null(result.GrantId);
        Assert.Null(result.Provider);
    }

    [Fact]
    public void ExistingEligibleWorkerIsPreferredOverAnEarlierCreationHost() {
        var state = Creator(); var e = new CoordinationEngine(state, Now);
        e.ObserveWorker(new("existing-worker", "source-host", "provider-a", true, null, Now,
            "existing-session", "existing-workspace", Candidate, BootId: "boot", TemporaryWorker: true), SourceHost);
        var result = e.Submit(Request(kind: "review"), Requester);
        Assert.Equal("admitted", result.State);
        Assert.Equal("source-host", result.Machine);
        Assert.Equal("existing-worker", result.Assignee);
        Assert.Equal("provider-a", result.Provider);
        Assert.Null(result.Reason);
    }

    [Theory]
    [InlineData("grant")]
    [InlineData("generation")]
    [InlineData("provider")]
    [InlineData("candidate")]
    [InlineData("host")]
    [InlineData("session")]
    public void CreationBindingRejectsForeignAssignmentProviderCandidateAndSession(string mismatch) {
        var state = Creator(); var e = new CoordinationEngine(state, Now); var admitted = e.Submit(Request(), Requester);
        var worker = NewWorker(); e.ObserveWorker(worker, HelperHost);
        var binding = new CoordinationSessionBinding(admitted.Request.Id, admitted.Generation, admitted.GrantId!, worker.Id,
            worker.MachineId, worker.Provider, worker.SessionId!, worker.WorkspaceId!, Candidate);
        binding = mismatch switch {
            "grant" => binding with { GrantId = "foreign-grant" },
            "generation" => binding with { Generation = 2 },
            "provider" => binding with { Provider = "provider-b" },
            "candidate" => binding with { Candidate = Candidate with { Commit = new('b', 40) } },
            "session" => binding with { SessionId = "foreign-session" },
            _ => binding
        };
        Assert.Throws<CoordinationConflictException>(() => e.BindSession(binding, mismatch == "host" ? SourceHost : HelperHost));
        Assert.Null(state.Work["create"].Assignee);
        Assert.Empty(state.Bindings);
        Assert.Equal(admitted.GrantId, state.Work["create"].GrantId);
        Assert.Equal(1, e.Summary(null).Machines.Single(m => m.Id == "helper-host").Used.AgentSlots);
    }

    [Fact]
    public void NewlyObservedSessionBindsExactReservedGrantWithoutBeingStolenOrRebound() {
        var state = Creator(2); var e = new CoordinationEngine(state, Now); var admitted = e.Submit(Request(), Requester);
        Assert.Equal("queued", e.Submit(Request("competing") with { WorkerId = "new-worker" }, Requester).State);
        var worker = NewWorker(); e.ObserveWorker(worker, HelperHost);
        Assert.Equal("queued", state.Work["competing"].State);
        var binding = Bind(e, admitted, worker, HelperHost);
        Assert.Equal(binding, e.BindSession(binding, HelperHost));
        var current = state.Work["create"];
        Assert.Equal(admitted.Generation, current.Generation);
        Assert.Equal(admitted.GrantId, current.GrantId);
        Assert.Equal("new-worker", current.Assignee);
        Assert.Null(current.Reason);
        Assert.Single(e.Changes, c => c.Stage == "session-bound");
        var other = NewWorker("another-worker"); e.ObserveWorker(other, HelperHost);
        Assert.Throws<CoordinationConflictException>(() => Bind(e, admitted, other, HelperHost));
        e.Receipt("create", "submitted", Execution(current), HelperHost);
        e.Receipt("create", "terminal", Execution(current, "result"), HelperHost);
        Assert.Single(state.ResultOutbox);
        Assert.Single(state.Retirements);
        Assert.Equal(binding, state.Retirements["create"].Binding);
    }

    [Fact]
    public async Task RestartRetainsCreationReservationProviderAndExactGrantForLaterBinding() {
        var directory = LedgerTestPaths.NewDirectory("coordination-creation");
        var path = Path.Combine(directory, "coordination.sqlite");
        CoordinationWork admitted;
        try {
            using (var store = new CoordinationStore(path, CreationPolicy())) {
                await store.InitializeAsync();
                await store.ExecuteAsync((e, s) => { Enroll(e, s, ["provider-a"]); return true; }, Now);
                admitted = await store.ExecuteAsync((e, _) => e.Submit(Request(), Requester), Now);
            }
            using var restarted = new CoordinationStore(path, CreationPolicy());
            await restarted.InitializeAsync();
            var worker = NewWorker();
            var binding = await restarted.ExecuteAsync((e, _) => {
                e.ObserveWorker(worker, HelperHost);
                return Bind(e, admitted, worker, HelperHost);
            }, Now.AddSeconds(1));
            Assert.Equal(admitted.GrantId, binding.GrantId);
            Assert.Equal(admitted.Generation, binding.Generation);
            Assert.Equal("provider-a", binding.Provider);
            var journal = await restarted.ChangesAsync(0, 100);
            Assert.Single(journal, c => c.Stage == "admitted");
            Assert.Single(journal, c => c.Stage == "session-bound");
        } finally {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
