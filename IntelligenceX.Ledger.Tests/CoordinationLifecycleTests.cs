using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Coordination;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class CoordinationLifecycleTests {
    internal static readonly DateTimeOffset Now = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
    internal static readonly WorkCheckpoint Candidate = new("sample", "feature/sample", new('a', 40), null, "checkpoint", "validate");
    internal static readonly CoordinationPrincipal Requester = new("requester", "client", "unused", ["sample"], "source-host", "requester-worker");
    internal static readonly CoordinationPrincipal SourceHost = new("source-observer", "host", "unused", [], "source-host");
    internal static readonly CoordinationPrincipal HelperHost = new("helper-observer", "host", "unused", [], "helper-host");
    internal static readonly CoordinationPrincipal ReviewHost = new("review-observer", "host", "unused", [], "review-host");
    internal static readonly CoordinationWorker RequesterWorker = new("requester-worker", "source-host", "provider-a", true, null,
        Now, "requester-session", "requester-workspace", Candidate, BootId: "boot");
    internal static readonly CoordinationPolicy Policy = new(1, [
        new("source-host", 2, 1, 4, 4096, ["review"]),
        new("helper-host", 2, 1, 4, 4096, ["platform-help", "review"]),
        new("review-host", 2, 1, 4, 4096, ["review"]) ]);

    internal static CoordinationState CreateState() {
        var state = new CoordinationState { Policy = Policy };
        var e = new CoordinationEngine(state, Now);
        Initialize(e, state);
        return state;
    }

    internal static void Initialize(CoordinationEngine e, CoordinationState state) {
        e.SetGoal(new("task", "", 0, false, false, "authorization", ["result-verified"]), Requester, 0);
        foreach (var host in new[] { SourceHost, HelperHost, ReviewHost })
            e.ObserveHost(new(host.MachineId!, "boot", Now, state.Policy.Machines.Single(m => m.Id == host.MachineId).Capabilities), host);
        e.ObserveWorker(RequesterWorker, SourceHost);
        e.ObserveWorker(new("helper-worker", "helper-host", "provider-a", true, null, Now,
            "helper-session", "helper-workspace", Candidate, BootId: "boot", TemporaryWorker: true), HelperHost);
    }

    internal static CoordinationRequest Help(string id = "help") => new(id, "task", "help", "platform-help", null, null,
        new(AgentSlots: 1, CpuUnits: 2), Candidate, [], 1, "authorization", "provider-a", false,
        new(Requester.WorkerId!, Requester.MachineId!, "provider-a", "requester-session", Candidate));

    internal static CoordinationSessionBinding Bind(CoordinationEngine e, CoordinationWork work, CoordinationWorker worker,
        CoordinationPrincipal host) => e.BindSession(new(work.Request.Id, work.Generation, work.GrantId!, worker.Id,
            worker.MachineId, worker.Provider, worker.SessionId!, worker.WorkspaceId!, work.Request.Checkpoint), host);

    internal static ExecutionReceipt Execution(CoordinationWork work, string? result = null) =>
        new(work.GrantId!, work.Generation, "boot", "execution-" + work.Request.Id, result, result is null ? null : true, result is not null);

    internal static CoordinationSessionBinding Start(CoordinationEngine e, CoordinationState state, CoordinationWork work,
        CoordinationPrincipal? host = null) {
        host ??= HelperHost;
        var binding = Bind(e, work, state.Workers[work.Assignee!], host);
        e.Receipt(work.Request.Id, "submitted", Execution(work), host);
        e.Receipt(work.Request.Id, "started", Execution(work), host);
        return binding;
    }

    internal static CoordinationSessionBinding Finish(CoordinationEngine e, CoordinationState state) {
        var work = e.Submit(Help(), Requester);
        var binding = Start(e, state, work);
        e.Receipt("help", "terminal", Execution(work, "result"), HelperHost);
        return binding;
    }

    internal static void Clean(CoordinationEngine e, CoordinationSessionBinding binding, CoordinationPrincipal? host = null) {
        foreach (var stage in new[] { "source-preserved", "descendants-resolved", "processes-released", "archived", "workspace-released", "verified" })
            e.Retire(new(binding, stage, "proof-" + stage, Dirty: false, Unpublished: false), host ?? HelperHost);
    }

    [Fact]
    public void TerminalCreatesOneResultAndRetirementWhichCanFinishBeforeRequesterConsumption() {
        var state = CreateState(); var e = new CoordinationEngine(state, Now);
        var binding = Finish(e, state);
        e.Receipt("help", "terminal", Execution(state.Work["help"], "result"), HelperHost);
        Assert.Single(state.ResultOutbox);
        Assert.Single(state.Retirements);
        Assert.Equal("intent", state.ResultOutbox["help"].State);
        var used = e.Summary(null).Machines.Single(m => m.Id == "helper-host").Used;
        Assert.Equal(0, used.AgentSlots);
        Assert.Equal(2, used.CpuUnits);
        Clean(e, binding);
        Assert.NotNull(state.Retirements["help"].CompletedAtUtc);
        Assert.Equal(0, e.Summary(null).Machines.Single(m => m.Id == "helper-host").Used.CpuUnits);
        Assert.False(state.Work["help"].Stages!.ContainsKey("accepted"));
        e.BeginResultDelivery("help", 1, "send-one", SourceHost);
        e.RecordResultDelivery("help", 1, new("send-one", "submitted", "transport-receipt"), SourceHost);
        Assert.False(state.Work["help"].Stages!.ContainsKey("accepted"));
        Assert.Throws<CoordinationConflictException>(() => e.Accept("help", "result", Requester));
        e.ConsumeResult("help", 1, "result", Candidate, Requester);
        var goal = e.SetGoal(state.Goals["task"] with { ResultRef = "task-result", Accepted = true }, Requester, 1);
        Assert.True(goal.Accepted);
        Assert.Single(e.Changes, c => c.Stage == "result-intent");
        Assert.Single(e.Changes, c => c.Stage == "retirement-required");
    }

    [Theory]
    [InlineData("generation")]
    [InlineData("host")]
    [InlineData("session")]
    [InlineData("candidate")]
    [InlineData("worker")]
    public void BindingRejectsForeignAssignmentIdentity(string mismatch) {
        var state = CreateState(); var e = new CoordinationEngine(state, Now);
        var work = e.Submit(Help(), Requester);
        var binding = new CoordinationSessionBinding("help", 1, work.GrantId!, "helper-worker", "helper-host", "provider-a",
            "helper-session", "helper-workspace", Candidate);
        var caller = HelperHost;
        binding = mismatch switch {
            "generation" => binding with { Generation = 2 },
            "session" => binding with { SessionId = "foreign-session" },
            "candidate" => binding with { Candidate = Candidate with { Commit = new('b', 40) } },
            "worker" => binding with { WorkerId = "requester-worker" },
            _ => binding
        };
        if (mismatch == "host") caller = SourceHost;
        Assert.Throws<CoordinationConflictException>(() => e.BindSession(binding, caller));
        Assert.Empty(state.Bindings);
        Assert.Throws<CoordinationConflictException>(() => e.Receipt("help", "submitted", Execution(work), HelperHost));
        Assert.Null(state.Work["help"].ExecutionId);
    }

    [Theory]
    [InlineData("requester")]
    [InlineData("session")]
    [InlineData("candidate")]
    public void RequestReturnBindingMustMatchEnrolledRequesterAndObservedCandidate(string mismatch) {
        var state = CreateState(); var e = new CoordinationEngine(state, Now);
        var request = Help(); var caller = Requester;
        if (mismatch == "requester") caller = caller with { WorkerId = "helper-worker" };
        if (mismatch == "session") request = request with { ReturnTo = request.ReturnTo! with { SessionId = "foreign-session" } };
        if (mismatch == "candidate") {
            var changed = Candidate with { Commit = new('b', 40) };
            request = request with { Checkpoint = changed, ReturnTo = request.ReturnTo! with { Candidate = changed } };
        }
        Assert.Throws<CoordinationConflictException>(() => e.Submit(request, caller));
        Assert.Empty(state.Work);
    }

    [Fact]
    public void UncertainSendRequiresNegativeReconciliationAndNeverBecomesTransportAcceptance() {
        var state = CreateState(); var e = new CoordinationEngine(state, Now); Finish(e, state);
        e.BeginResultDelivery("help", 1, "send-one", SourceHost);
        Assert.Throws<CoordinationConflictException>(() => e.BeginResultDelivery("help", 1, "send-one", SourceHost));
        e.RecordResultDelivery("help", 1, new("send-one", "uncertain", "lost-response"), SourceHost);
        Assert.Throws<CoordinationConflictException>(() => e.BeginResultDelivery("help", 1, "send-two", SourceHost));
        Assert.Throws<CoordinationConflictException>(() => e.RecordResultDelivery("help", 1, new("send-one", "held", "busy"), SourceHost));
        Assert.Throws<CoordinationConflictException>(() => e.RecordResultDelivery("help", 1, new("foreign-send", "not-submitted", "not-found"), SourceHost));
        e.RecordResultDelivery("help", 1, new("send-one", "not-submitted", "destination-exclusion"), SourceHost);
        e.RecordResultDelivery("help", 1, new("send-one", "not-submitted", "destination-exclusion"), SourceHost);
        Assert.Throws<CoordinationConflictException>(() => e.BeginResultDelivery("help", 1, "send-one", SourceHost));
        e.BeginResultDelivery("help", 1, "send-two", SourceHost);
        e.RecordResultDelivery("help", 1, new("send-two", "submitted", "native-receipt"), SourceHost);
        e.RecordResultDelivery("help", 1, new("send-two", "submitted", "native-receipt"), SourceHost);
        Assert.False(state.Work["help"].Stages!.ContainsKey("accepted"));
        Assert.Single(e.Changes, c => c.Stage == "result-submitted");
        Assert.Throws<CoordinationConflictException>(() => e.BeginResultDelivery("help", 1, "send-three", SourceHost));
        Assert.Throws<CoordinationConflictException>(() => e.ConsumeResult("help", 1, "result", Candidate, SourceHost));
        Assert.Throws<CoordinationConflictException>(() => e.ConsumeResult("help", 2, "result", Candidate, Requester));
        e.ConsumeResult("help", 1, "result", Candidate, Requester);
        e.ConsumeResult("help", 1, "result", Candidate, Requester);
        Assert.Single(e.Changes, c => c.Stage == "result-consumed");
    }

    [Fact]
    public void ChangedRequesterCandidateHoldsDeliveryAndRejectsConsumptionOfOldResult() {
        var state = CreateState(); var e = new CoordinationEngine(state, Now); Finish(e, state);
        e.ObserveWorker(RequesterWorker with { Candidate = Candidate with { Commit = new('b', 40) }, ObservedAtUtc = Now.AddSeconds(1) }, SourceHost);
        var held = e.RecordResultDelivery("help", 1, new("hold", "held", "requester-candidate-changed"), SourceHost);
        e.RecordResultDelivery("help", 1, new("hold", "held", "requester-candidate-changed"), SourceHost);
        Assert.Single(e.Changes, c => c.Stage == "result-held");
        Assert.Equal("requester-candidate-changed", held.HeldOn);
        Assert.Throws<CoordinationConflictException>(() => e.BeginResultDelivery("help", 1, "send-one", SourceHost));
        Assert.Throws<CoordinationConflictException>(() => e.ConsumeResult("help", 1, "result", Candidate, Requester));
        Assert.Equal(new string('a', 40), state.ResultOutbox["help"].Candidate.Commit);
    }

    [Theory]
    [InlineData("client-route-unavailable")]
    [InlineData("protected-primary-detach")]
    public void AcceptedWorkKeepsItsIndependentCleanupHoldWithoutKeepingTheWorkerBusy(string condition) {
        var state = CreateState(); var e = new CoordinationEngine(state, Now); var binding = Finish(e, state);
        e.ConsumeResult("help", 1, "result", Candidate, Requester);
        e.Retire(new(binding, "waiting", "protection-receipt", WaitingOn: condition), HelperHost);
        foreach (var stage in new[] { "source-preserved", "descendants-resolved", "processes-released" })
            e.Retire(new(binding, stage, "proof-" + stage, Dirty: false, Unpublished: false), HelperHost);
        Assert.Equal(condition, state.Retirements["help"].WaitingOn);
        Assert.Single(e.Summary(null).Items);
        Assert.Equal(0, e.Summary(null).Machines.Single(m => m.Id == "helper-host").Used.AgentSlots);
        Assert.Throws<CoordinationConflictException>(() => e.Retire(new(binding, "verified", "cleanup", false, false), HelperHost));
        Assert.True(e.SetGoal(state.Goals["task"] with { Accepted = true, ResultRef = "task-result" }, Requester, 1).Accepted);
        Assert.Null(state.Retirements["help"].CompletedAtUtc);
        Assert.Single(e.Summary(null).Items);
        Assert.Throws<CoordinationConflictException>(() => e.Retire(new(binding, "resume", "event", WaitingOn: "other-condition"), HelperHost));
        e.Retire(new(binding, "resume", "qualified-event", WaitingOn: condition), HelperHost);
        Clean(e, binding);
        Assert.NotNull(state.Retirements["help"].CompletedAtUtc);
        Assert.True(state.Goals["task"].Accepted);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void DirtyOrUnpublishedSourceCannotBePreservedOrPassFinalCleanup(bool dirty, bool unpublished) {
        var state = CreateState(); var e = new CoordinationEngine(state, Now); var binding = Finish(e, state);
        var error = Assert.Throws<CoordinationConflictException>(() => e.Retire(new(binding, "source-preserved", "source-proof", dirty, unpublished), HelperHost));
        Assert.Equal("retirement-source-unsafe", error.Message);
        foreach (var stage in new[] { "source-preserved", "descendants-resolved", "processes-released", "archived", "workspace-released" })
            e.Retire(new(binding, stage, "proof-" + stage, false, false), HelperHost);
        Assert.Equal("retirement-source-unsafe", Assert.Throws<CoordinationConflictException>(() =>
            e.Retire(new(binding, "verified", "final-proof", dirty, unpublished), HelperHost)).Message);
        Assert.Null(state.Retirements["help"].CompletedAtUtc);
    }

    [Fact]
    public void ProcessReleaseAndArchiveAreIndependentRequiredCleanupEvidence() {
        var state = CreateState(); var e = new CoordinationEngine(state, Now); var binding = Finish(e, state);
        e.Retire(new(binding, "source-preserved", "source", false, false), HelperHost);
        e.Retire(new(binding, "descendants-resolved", "descendants"), HelperHost);
        Assert.Equal("retirement-release-incomplete", Assert.Throws<CoordinationConflictException>(() =>
            e.Retire(new(binding, "workspace-released", "workspace"), HelperHost)).Message);
        Assert.Equal(2, e.Summary(null).Machines.Single(m => m.Id == "helper-host").Used.CpuUnits);
        e.Retire(new(binding, "processes-released", "processes"), HelperHost);
        e.Retire(new(binding, "workspace-released", "workspace"), HelperHost);
        Assert.Equal("retirement-verification-incomplete", Assert.Throws<CoordinationConflictException>(() =>
            e.Retire(new(binding, "verified", "final", false, false), HelperHost)).Message);
        Assert.Null(state.Retirements["help"].CompletedAtUtc);
    }

    [Theory]
    [InlineData("generation")]
    [InlineData("session")]
    [InlineData("host")]
    public void ForeignRetirementCannotCleanAnotherWorker(string mismatch) {
        var state = CreateState(); var e = new CoordinationEngine(state, Now); var binding = Finish(e, state);
        if (mismatch == "generation") binding = binding with { Generation = 2 };
        if (mismatch == "session") binding = binding with { SessionId = "other-session" };
        Assert.Equal("retirement-owner-conflict", Assert.Throws<CoordinationConflictException>(() =>
            e.Retire(new(binding, "archived", "archive"), mismatch == "host" ? SourceHost : HelperHost)).Message);
        Assert.Empty(state.Retirements["help"].Evidence);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ParentRetirementRequiresConsumedDescendantsAndTheirOwnedCleanup(bool reuseReviewer) {
        var state = CreateState(); var e = new CoordinationEngine(state, Now);
        e.ObserveWorker(new("reviewer", "review-host", "provider-b", true, null, Now, "review-session", "review-workspace", Candidate, BootId: "boot", TemporaryWorker: !reuseReviewer), ReviewHost);
        var parent = e.Submit(Help(), Requester); var parentBinding = Start(e, state, parent);
        var childCaller = new CoordinationPrincipal("helper-client", "client", "unused", ["sample"], "helper-host", "helper-worker");
        var review = Help("review") with { Kind = "review", Capability = "review", RequiredProvider = "provider-b", ReadOnly = true,
            WorkerId = reuseReviewer ? "reviewer" : null, MachineId = reuseReviewer ? "review-host" : null, ReuseSession = reuseReviewer,
            ReturnTo = new("helper-worker", "helper-host", "provider-a", "helper-session", Candidate, "help", 1) };
        var child = e.Submit(review, childCaller); var childBinding = Start(e, state, child, ReviewHost);
        e.ObserveWorker(new("grandchild-worker", "source-host", "provider-c", true, null, Now,
            "grandchild-session", "grandchild-workspace", Candidate, BootId: "boot", TemporaryWorker: true), SourceHost);
        var reviewer = new CoordinationPrincipal("reviewer-client", "client", "unused", ["sample"], "review-host", "reviewer");
        var grandchild = e.Submit(Help("grandchild") with {
            Kind = "review", Capability = "review", RequiredProvider = "provider-c", ReadOnly = true,
            WorkerId = "grandchild-worker", MachineId = "source-host",
            ReturnTo = new("reviewer", "review-host", "provider-b", "review-session", Candidate, "review", 1)
        }, reviewer);
        var grandchildBinding = Start(e, state, grandchild, SourceHost);
        e.Receipt("help", "terminal", Execution(parent, "result"), HelperHost);
        Assert.Equal("retirement-descendants-unresolved", Assert.Throws<CoordinationConflictException>(() =>
            e.Retire(new(parentBinding, "descendants-resolved", "descendants"), HelperHost)).Message);
        e.Receipt("review", "terminal", Execution(child, "review-result"), ReviewHost);
        Assert.Throws<CoordinationConflictException>(() => e.Retire(new(parentBinding, "descendants-resolved", "descendants"), HelperHost));
        e.ConsumeResult("review", 1, "review-result", Candidate, childCaller);
        Assert.Throws<CoordinationConflictException>(() => e.Retire(new(parentBinding, "descendants-resolved", "descendants"), HelperHost));
        e.Receipt("grandchild", "terminal", Execution(grandchild, "grandchild-result"), SourceHost);
        Clean(e, grandchildBinding, SourceHost);
        Assert.Throws<CoordinationConflictException>(() => e.Retire(new(parentBinding, "descendants-resolved", "descendants"), HelperHost));
        e.ConsumeResult("grandchild", 1, "grandchild-result", Candidate, reviewer);
        if (reuseReviewer) Assert.False(state.Retirements.ContainsKey("review"));
        else Clean(e, childBinding, ReviewHost);
        Clean(e, parentBinding);
        Assert.NotNull(state.Retirements["help"].CompletedAtUtc);
        Assert.Throws<CoordinationConflictException>(() => e.BeginResultDelivery("review", 1, "send-child", HelperHost));
        Assert.Equal("intent", state.ResultOutbox["review"].State);
        Assert.NotNull(state.ResultOutbox["review"].ConsumedAtUtc);
    }

    [Fact]
    public void ReviewRoutesOnlyToObservedEligibleProviderAndCanShareWriterCandidate() {
        var state = CreateState(); var e = new CoordinationEngine(state, Now);
        var writer = e.Submit(Help(), Requester); Start(e, state, writer);
        e.ObserveWorker(new("local-reviewer", "source-host", "provider-b", false, "unknown-quota", Now,
            "local-review-session", "local-review-workspace", Candidate, BootId: "boot", TemporaryWorker: true), SourceHost);
        e.ObserveWorker(new("remote-reviewer", "review-host", "provider-b", false, "usage-limited", Now,
            "remote-review-session", "remote-review-workspace", Candidate, BootId: "boot", TemporaryWorker: true), ReviewHost);
        var request = Help("review") with { Kind = "review", Capability = "review", RequiredProvider = "provider-b", ReadOnly = true };
        Assert.Equal("queued", e.Submit(request, Requester).State);
        Assert.Equal("worker-unavailable", state.Work["review"].Reason);
        e.ObserveWorker(state.Workers["remote-reviewer"] with { Eligible = true, UnavailableReason = null, ObservedAtUtc = Now.AddSeconds(1) }, ReviewHost);
        Assert.Equal("admitted", state.Work["review"].State);
        Assert.Equal("review-host", state.Work["review"].Machine);
        Assert.Equal("remote-reviewer", state.Work["review"].Assignee);
        Assert.Equal("started", state.Work["help"].State);
    }

    [Fact]
    public void StopSurvivesTerminalResultAndBlocksAutomaticDeliveryAndNewAssignments() {
        var state = CreateState(); var e = new CoordinationEngine(state, Now);
        var work = e.Submit(Help(), Requester); Start(e, state, work);
        e.SetGoal(state.Goals["task"] with { Stopped = true }, Requester, 1); e.Schedule();
        Assert.Equal("reconcile", state.Work["help"].State);
        Assert.Throws<CoordinationConflictException>(() => e.Receipt("help", "renew", Execution(work), HelperHost));
        e.Receipt("help", "terminal", Execution(work, "result"), HelperHost);
        Assert.Single(state.ResultOutbox); Assert.Single(state.Retirements);
        Assert.Throws<CoordinationConflictException>(() => e.BeginResultDelivery("help", 1, "send", SourceHost));
        Assert.Throws<CoordinationConflictException>(() => e.Submit(Help("another") with { ControlRevision = 2 }, Requester));
        Assert.True(state.Goals["task"].Stopped);
    }

    [Fact]
    public void SubstantiveConsumptionDuringSendRetainsIndependentLateTransportReceipt() {
        var state = CreateState(); var e = new CoordinationEngine(state, Now); Finish(e, state);
        e.BeginResultDelivery("help", 1, "send", SourceHost);
        e.ConsumeResult("help", 1, "result", Candidate, Requester);
        var receipt = e.RecordResultDelivery("help", 1, new("send", "submitted", "native-receipt"), SourceHost);
        Assert.Equal("submitted", receipt.State);
        Assert.NotNull(receipt.ConsumedAtUtc);
        Assert.True(state.Work["help"].Stages!.ContainsKey("accepted"));
        Assert.Throws<CoordinationConflictException>(() => e.BeginResultDelivery("help", 1, "again", SourceHost));
        Assert.Null(state.Retirements["help"].CompletedAtUtc);
    }

    [Fact]
    public void ChangedWorkerSourceReconcilesWithoutPublishingWrongCandidateResult() {
        var state = CreateState(); var e = new CoordinationEngine(state, Now);
        var work = e.Submit(Help(), Requester); Start(e, state, work);
        e.ObserveWorker(state.Workers["helper-worker"] with {
            Candidate = Candidate with { Commit = new('b', 40) }, ObservedAtUtc = Now.AddSeconds(1)
        }, HelperHost);
        Assert.Equal("reconcile", state.Work["help"].State);
        Assert.Equal("worker-source-changed", state.Work["help"].Reason);
        Assert.Throws<CoordinationConflictException>(() => e.Receipt("help", "terminal", Execution(work, "wrong-source-result"), HelperHost));
        Assert.Empty(state.ResultOutbox);
        Assert.Equal(1, e.Summary(null).Machines.Single(m => m.Id == "helper-host").Used.AgentSlots);
    }

    [Fact]
    public void NewHostBootRequiresFreshWorkerEligibilityBeforeAdmission() {
        var state = CreateState(); var e = new CoordinationEngine(state, Now);
        e.ObserveHost(new("helper-host", "next-boot", Now.AddSeconds(1), ["platform-help", "review"]), HelperHost);
        Assert.Equal("queued", e.Submit(Help(), Requester).State);
        Assert.Equal("worker-unavailable", state.Work["help"].Reason);
        Assert.Equal("host-restarted", state.Workers["helper-worker"].UnavailableReason);
        e.ObserveWorker(state.Workers["helper-worker"] with { Eligible = true, UnavailableReason = null, ObservedAtUtc = Now.AddSeconds(2), BootId = "next-boot" }, HelperHost);
        Assert.Equal("admitted", state.Work["help"].State);
        Assert.Equal("next-boot", state.Work["help"].BootId);
    }

    [Fact]
    public void PendingRetirementRetainsItsHostAndSourceOwnershipUntilWorkspaceRelease() {
        var state = CreateState(); var e = new CoordinationEngine(state, Now); var binding = Finish(e, state);
        var reduced = Policy with { Revision = 2, Machines = Policy.Machines.Where(m => m.Id != "helper-host").ToArray() };
        Assert.Equal("occupied-machine-removal", Assert.Throws<CoordinationConflictException>(() =>
            e.ReplacePolicy(reduced, new("operator", "operator", "unused", []), 1)).Message);
        e.ObserveWorker(new("next-helper", "helper-host", "provider-a", true, null, Now,
            "next-session", "next-workspace", Candidate, BootId: "boot", TemporaryWorker: true), HelperHost);
        Assert.Equal("queued", e.Submit(Help("next"), Requester).State);
        Assert.Equal("branch-occupied", state.Work["next"].Reason);
        foreach (var stage in new[] { "source-preserved", "descendants-resolved", "processes-released", "archived" })
            e.Retire(new(binding, stage, "proof-" + stage, false, false), HelperHost);
        Assert.Equal("queued", state.Work["next"].State);
        e.Retire(new(binding, "workspace-released", "workspace"), HelperHost);
        Assert.Equal("admitted", state.Work["next"].State);
        Assert.Equal("next-helper", state.Work["next"].Assignee);
        Assert.Null(state.Retirements["help"].CompletedAtUtc);
    }
}
