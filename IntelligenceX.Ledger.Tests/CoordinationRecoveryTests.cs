using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Coordination;
using Xunit;
using static IntelligenceX.Ledger.Tests.CoordinationLifecycleTests;

namespace IntelligenceX.Ledger.Tests;

public sealed class CoordinationRecoveryTests {
    private static readonly CoordinationPrincipal Operator = new("operator", "operator", "unused", []);
    private static CoordinationPolicy RecoveryPolicy => Policy with {
        Machines = Policy.Machines.Select(m => m.Id == "helper-host"
            ? m with { AgentSlots = 1, AllowedCreateProviders = ["provider-a"] } : m).ToArray()
    };

    private static CoordinationState CreationState() {
        var state = new CoordinationState { Policy = RecoveryPolicy };
        var e = new CoordinationEngine(state, Now);
        Initialize(e, state);
        // Creation fixtures enroll only the root requester; no native helper has been created.
        state.Workers.Remove("helper-worker");
        return state;
    }

    private static CoordinationWork Create(CoordinationEngine e, string id = "help") =>
        e.Submit(Help(id) with { ReadOnly = true }, Requester);

    private static CoordinationReconciliation Exclusion(CoordinationWork work, DateTimeOffset observed,
        string boot = "boot", CoordinationSessionBinding? binding = null) =>
        new(work.Request.Id, work.Generation, work.GrantId!, work.BootId!, boot, observed,
            binding is null ? "never-created" : "execution-excluded", "native-exclusion", "recovery-result",
            NativeExclusionVerified: true, Binding: binding, ExecutionId: work.ExecutionId);

    private static CoordinationEngine Refresh(CoordinationState state, DateTimeOffset at, string boot = "boot") {
        var e = new CoordinationEngine(state, at);
        e.ObserveHost(state.Hosts["helper-host"] with { BootId = boot, ObservedAtUtc = at,
            CreatableProviders = ["provider-a"] }, HelperHost);
        return e;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RecoveryCannotRetireALegacyPersonalWorker(bool existingBinding) {
        var state = new CoordinationState { Policy = RecoveryPolicy };
        var e = new CoordinationEngine(state, Now);
        Initialize(e, state);
        var work = e.Submit(Help(), Requester);
        var binding = Start(e, state, work);
        // A projection written before the ownership field was introduced loads with the default false.
        state.Workers[binding.WorkerId] = state.Workers[binding.WorkerId] with { TemporaryWorker = false };
        if (!existingBinding) state.Bindings.Remove(work.Request.Id);
        var later = Now.AddSeconds(122);
        e = Refresh(state, later);
        var before = state.Work[work.Request.Id];
        var changes = e.Changes.Count;
        var error = Assert.Throws<CoordinationConflictException>(() =>
            e.Reconcile(Exclusion(before, later, binding: binding), HelperHost));
        Assert.Equal("reconciliation-worker-not-temporary", error.Message);
        Assert.Equal(before, state.Work[work.Request.Id]);
        Assert.Equal(changes, e.Changes.Count);
        Assert.Equal(existingBinding, state.Bindings.ContainsKey(work.Request.Id));
        Assert.Empty(state.Reconciliations);
        Assert.Empty(state.Retirements);
        Assert.Empty(state.ResultOutbox);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitNeverCreatedExclusionReleasesExpiredOrOldBootGrantWithoutReplay(bool reboot) {
        var state = CreationState();
        var e = Refresh(state, Now.AddSeconds(1));
        var work = Create(e);
        Assert.Null(work.Assignee);
        Assert.Equal("reconciliation-not-required", Assert.Throws<CoordinationConflictException>(() =>
            e.Reconcile(Exclusion(work, Now.AddSeconds(1)), HelperHost)).Message);
        Assert.Equal("queued", Create(e, "next").State);
        var later = Now.AddSeconds(reboot ? 2 : 122);
        var boot = reboot ? "new-boot" : "boot";
        e = Refresh(state, later, boot);
        Assert.Equal("reconcile", state.Work["help"].State);
        Assert.Equal(1, e.Summary(null).Machines.Single(m => m.Id == "helper-host").Used.AgentSlots);
        Assert.Equal(reboot ? "host-boot-conflict" : "native-binding-required",
            Assert.Throws<CoordinationConflictException>(() => e.Receipt("help", "submitted", Execution(work), HelperHost)).Message);
        var receipt = Exclusion(work, later, boot);
        var recovered = e.Reconcile(receipt, HelperHost);
        Assert.Equal("cancelled", recovered.State);
        Assert.Equal(work.Generation, recovered.Generation);
        Assert.Equal(work.GrantId, recovered.GrantId);
        Assert.Empty(state.Retirements);
        Assert.Equal("recovery-result", state.ResultOutbox["help"].ResultRef);
        Assert.Equal("admitted", state.Work["next"].State);
        var nextGrant = state.Work["next"].GrantId;
        Assert.Equal(recovered, e.Reconcile(receipt, HelperHost));
        Assert.Throws<CoordinationConflictException>(() => e.Reconcile(receipt with { Generation = work.Generation + 1 }, HelperHost));
        Assert.Throws<CoordinationConflictException>(() => e.Reconcile(receipt with { RequestId = "next" }, HelperHost));
        Assert.Throws<CoordinationConflictException>(() => e.Receipt("next", "submitted", Execution(work), HelperHost));
        Assert.Equal(nextGrant, state.Work["next"].GrantId);
        Assert.Equal("admitted", state.Work["next"].State);
        Assert.Single(e.Changes, c => c.Stage == "reconciled");
    }

    [Theory]
    [InlineData("unknown-exclusion", "reconciliation-exclusion-required")]
    [InlineData("missing-evidence", "reconciliation-exclusion-required")]
    [InlineData("old-observation", "reconciliation-host-conflict")]
    [InlineData("current-boot", "reconciliation-host-conflict")]
    [InlineData("original-boot", "reconciliation-grant-conflict")]
    [InlineData("generation", "reconciliation-grant-conflict")]
    [InlineData("grant", "reconciliation-grant-conflict")]
    [InlineData("foreign-host", "reconciliation-grant-conflict")]
    [InlineData("operator", "reconciliation-grant-conflict")]
    public void UnknownOrForeignExclusionPreservesTheExactReservation(string mismatch, string reason) {
        var state = CreationState(); var e = Refresh(state, Now.AddSeconds(1)); var work = Create(e);
        var later = Now.AddSeconds(122); e = Refresh(state, later, "new-boot");
        var receipt = Exclusion(work, later, "new-boot");
        receipt = mismatch switch {
            "unknown-exclusion" => receipt with { NativeExclusionVerified = false },
            "missing-evidence" => receipt with { EvidenceRef = "" },
            "old-observation" => receipt with { ObservedAtUtc = Now },
            "current-boot" => receipt with { CurrentBootId = "boot" },
            "original-boot" => receipt with { OriginalBootId = "other-boot" },
            "generation" => receipt with { Generation = 2 },
            "grant" => receipt with { GrantId = "other-grant" },
            _ => receipt
        };
        var caller = mismatch == "operator" ? Operator : mismatch == "foreign-host" ? SourceHost : HelperHost;
        Assert.Equal(reason, Assert.Throws<CoordinationConflictException>(() => e.Reconcile(receipt, caller)).Message);
        Assert.Equal("reconcile", state.Work["help"].State);
        Assert.Equal(work.GrantId, state.Work["help"].GrantId);
        Assert.Equal(1, e.Summary(null).Machines.Single(m => m.Id == "helper-host").Used.AgentSlots);
        Assert.Empty(state.Reconciliations);
        Assert.Empty(state.ResultOutbox);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CreatedWorkerRequiresExactBindingAndRetirementEvenWhenOriginalBindingExpired(bool bound) {
        var state = new CoordinationState { Policy = RecoveryPolicy }; var e = new CoordinationEngine(state, Now);
        Initialize(e, state);
        CoordinationWork work;
        CoordinationSessionBinding binding;
        if (bound) {
            work = e.Submit(Help(), Requester);
            binding = Start(e, state, work);
            work = state.Work["help"];
        } else {
            state.Workers.Remove("helper-worker");
            e = Refresh(state, Now.AddSeconds(1));
            work = e.Submit(Help(), Requester);
            e.ObserveWorker(new("created-worker", "helper-host", "provider-a", true, null, Now.AddSeconds(2),
                "created-session", "created-workspace", Candidate, BootId: "boot", TemporaryWorker: true), HelperHost);
            binding = new("help", work.Generation, work.GrantId!, "created-worker", "helper-host", "provider-a",
                "created-session", "created-workspace", Candidate);
        }
        Assert.Equal("queued", e.Submit(Help("next"), Requester).State);
        var later = Now.AddSeconds(122); e = Refresh(state, later, "new-boot");
        if (!bound) Assert.Throws<CoordinationConflictException>(() => e.BindSession(binding, HelperHost));
        Assert.Throws<CoordinationConflictException>(() => e.Reconcile(
            Exclusion(work, later, "new-boot") with { Disposition = "execution-excluded" }, HelperHost));
        if (bound) Assert.Equal("reconciliation-native-exists", Assert.Throws<CoordinationConflictException>(() =>
            e.Reconcile(Exclusion(work, later, "new-boot"), HelperHost)).Message);
        var receipt = Exclusion(work, later, "new-boot", binding);
        Assert.Equal("failed", e.Reconcile(receipt, HelperHost).State);
        var retirement = Assert.Single(state.Retirements).Value;
        Assert.Equal(binding, retirement.Binding);
        Assert.Empty(retirement.Evidence);
        Assert.Null(retirement.CompletedAtUtc);
        var used = e.Summary(null).Machines.Single(m => m.Id == "helper-host").Used;
        Assert.Equal(0, used.AgentSlots);
        Assert.Equal(2, used.CpuUnits);
        Assert.Equal("queued", state.Work["next"].State);
        Assert.Equal("branch-occupied", state.Work["next"].Reason);
        Assert.Throws<CoordinationConflictException>(() => e.Reconcile(receipt with { Disposition = "never-created" }, HelperHost));
        Clean(e, binding);
        Assert.Equal("admitted", state.Work["next"].State);
        var next = state.Work["next"];
        Assert.Throws<CoordinationConflictException>(() => e.Receipt("next", "submitted", Execution(work), HelperHost));
        Assert.Equal(next, state.Work["next"]);
    }

    [Fact]
    public void GoalOwnerCanAcceptConsumedFailedPredecessorAndSuccessfulFollowUp() {
        var state = CreateState();
        var e = new CoordinationEngine(state, Now);
        var failed = e.Submit(Help("failed-attempt"), Requester);
        var failedBinding = Start(e, state, failed);
        e.Receipt(failed.Request.Id, "terminal", Execution(failed, "failure-result") with { Succeeded = false }, HelperHost);
        var acceptance = state.Goals["task"] with { Accepted = true, ResultRef = "recovery-result" };
        Assert.Equal("goal-acceptance-incomplete", Assert.Throws<CoordinationConflictException>(() =>
            e.SetGoal(acceptance, Requester, 1)).Message);
        e.ConsumeResult(failed.Request.Id, failed.Generation, "failure-result", Candidate, Requester);

        var checkpoint = Candidate with { PredecessorRequest = failed.Request.Id };
        e.ObserveWorker(new("recovery-worker", "helper-host", "provider-a", true, null, Now,
            "recovery-session", "recovery-workspace", checkpoint, BootId: "boot", TemporaryWorker: true), HelperHost);
        var followUp = e.Submit(Help("successful-follow-up") with { Checkpoint = checkpoint }, Requester);
        Assert.Equal("queued", followUp.State);
        Assert.Equal("goal-acceptance-incomplete", Assert.Throws<CoordinationConflictException>(() =>
            e.SetGoal(acceptance, Requester, 1)).Message);
        Clean(e, failedBinding);
        var preservedFailure = state.Work[failed.Request.Id];
        Assert.Equal("failed", preservedFailure.State);
        Assert.Equal("failure-result", preservedFailure.ResultRef);
        Assert.True(preservedFailure.Stages!.ContainsKey("accepted"));

        followUp = state.Work[followUp.Request.Id];
        Assert.Equal("admitted", followUp.State);
        Assert.Equal("recovery-worker", followUp.Assignee);
        Assert.Equal("goal-acceptance-incomplete", Assert.Throws<CoordinationConflictException>(() =>
            e.SetGoal(acceptance, Requester, 1)).Message);
        Start(e, state, followUp);
        Assert.Equal("started", state.Work[followUp.Request.Id].State);
        Assert.Equal("goal-acceptance-incomplete", Assert.Throws<CoordinationConflictException>(() =>
            e.SetGoal(acceptance, Requester, 1)).Message);

        e = Refresh(state, Now.AddSeconds(122));
        Assert.Equal("reconcile", state.Work[followUp.Request.Id].State);
        Assert.Equal("goal-acceptance-incomplete", Assert.Throws<CoordinationConflictException>(() =>
            e.SetGoal(acceptance, Requester, 1)).Message);
        e.Receipt(followUp.Request.Id, "terminal", Execution(followUp, "success-result"), HelperHost);
        Assert.Equal("succeeded", state.Work[followUp.Request.Id].State);
        Assert.Null(state.ResultOutbox[followUp.Request.Id].ConsumedAtUtc);
        Assert.Equal("goal-acceptance-incomplete", Assert.Throws<CoordinationConflictException>(() =>
            e.SetGoal(acceptance, Requester, 1)).Message);
        Assert.Equal(1, state.Goals["task"].Revision);
        Assert.False(state.Goals["task"].Accepted);
        Assert.Equal("result-consumption-conflict", Assert.Throws<CoordinationConflictException>(() =>
            e.ConsumeResult(followUp.Request.Id, followUp.Generation, "wrong-result", checkpoint, Requester)).Message);
        e.ConsumeResult(followUp.Request.Id, followUp.Generation, "success-result", checkpoint, Requester);
        var pendingRetirement = state.Retirements[followUp.Request.Id];

        var accepted = e.SetGoal(acceptance, Requester, 1);

        Assert.True(accepted.Accepted);
        Assert.Equal(Requester.Id, accepted.Owner);
        Assert.Equal(2, accepted.Revision);
        Assert.Equal("recovery-result", accepted.ResultRef);
        Assert.Equal(acceptance.AcceptanceCriteria, accepted.AcceptanceCriteria);
        Assert.Equal(2, state.Work.Count);
        Assert.Equal(preservedFailure, state.Work[failed.Request.Id]);
        Assert.Equal(failed.Request.Id, state.Work[followUp.Request.Id].Request.Checkpoint.PredecessorRequest);
        Assert.Equal("succeeded", state.Work[followUp.Request.Id].State);
        Assert.All(state.ResultOutbox.Values, result => Assert.NotNull(result.ConsumedAtUtc));
        Assert.NotNull(state.Retirements[failed.Request.Id].CompletedAtUtc);
        Assert.Equal(pendingRetirement, state.Retirements[followUp.Request.Id]);
        Assert.Null(pendingRetirement.CompletedAtUtc);
    }

    [Fact]
    public void OrdinaryGoalCheckpointKeepsQueuedActiveAndPendingReturnAuthorization() {
        var state = new CoordinationState { Policy = RecoveryPolicy }; var e = new CoordinationEngine(state, Now);
        Initialize(e, state);
        e.ObserveHost(state.Hosts["helper-host"] with { ObservedAtUtc = Now.AddSeconds(1),
            CreatableProviders = ["provider-a"] }, HelperHost);
        var work = e.Submit(Help(), Requester); Start(e, state, work);
        Assert.Equal("queued", Create(e, "next").State);
        var goal = e.SetGoal(state.Goals["task"] with { ResultRef = "checkpoint-result" }, Requester, 1);
        e.Schedule();
        Assert.Equal("started", state.Work["help"].State);
        e.Receipt("help", "renew", Execution(work), HelperHost);
        e.Receipt("help", "terminal", Execution(work, "result"), HelperHost);
        Assert.Equal("admitted", state.Work["next"].State);
        e.SetGoal(goal with { ResultRef = "later-checkpoint" }, Requester, goal.Revision);
        Assert.Equal("sending", e.BeginResultDelivery("help", 1, "send", SourceHost).State);
        Assert.Equal(1, state.Work["help"].Request.ControlRevision);
        Assert.Equal(1, state.Goals["task"].ControlRevision);
        Assert.Equal(3, state.Goals["task"].Revision);
    }

    [Fact]
    public void ResumeRequiresExactOperatorReauthorizationAndNeverRestartsActiveOrUncertainWork() {
        var state = new CoordinationState { Policy = RecoveryPolicy }; var e = new CoordinationEngine(state, Now);
        Initialize(e, state);
        e.ObserveHost(state.Hosts["helper-host"] with { ObservedAtUtc = Now.AddSeconds(1),
            CreatableProviders = ["provider-a"] }, HelperHost);
        var work = e.Submit(Help(), Requester); Start(e, state, work);
        var queued = Create(e, "next");
        var paused = e.SetGoal(state.Goals["task"] with { Paused = true }, Operator, 1);
        var authorization = new CoordinationReauthorization("next", 0, 1, paused.ControlRevision, "operator-resume");
        Assert.Equal("reauthorization-goal-held", Assert.Throws<CoordinationConflictException>(() => e.Reauthorize(authorization, Operator)).Message);
        Assert.Throws<CoordinationConflictException>(() => e.SetGoal(paused with { Paused = false }, Requester, paused.Revision));
        var resumed = e.SetGoal(paused with { Paused = false }, Operator, paused.Revision);
        authorization = authorization with { ControlRevision = resumed.ControlRevision };
        Assert.Equal("queued", state.Work["next"].State);
        Assert.Equal("reauthorization-role-denied", Assert.Throws<CoordinationConflictException>(() => e.Reauthorize(authorization, Requester)).Message);
        Assert.Throws<CoordinationConflictException>(() => e.Reauthorize(authorization with { Generation = 1 }, Operator));
        Assert.Throws<CoordinationConflictException>(() => e.Reauthorize(authorization with { ExpectedControlRevision = 2 }, Operator));
        e.Reauthorize(authorization, Operator);
        Assert.Equal(queued.Request, state.Work["next"].Request);
        Assert.Equal("reconcile", state.Work["help"].State);
        Assert.Equal("reauthorization-native-operation-held", Assert.Throws<CoordinationConflictException>(() =>
            e.Reauthorize(new("help", 1, 1, resumed.ControlRevision, "resume"), Operator)).Message);
        Assert.Throws<CoordinationConflictException>(() => e.Receipt("help", "renew", Execution(work), HelperHost));
        e.Receipt("help", "terminal", Execution(work, "result"), HelperHost);
        Assert.Equal("admitted", state.Work["next"].State);
        Assert.Throws<CoordinationConflictException>(() => e.BeginResultDelivery("help", 1, "send", SourceHost));
        e.Reauthorize(new("help", 1, 1, resumed.ControlRevision, "return-resume"), Operator);
        e.BeginResultDelivery("help", 1, "send", SourceHost);
        e.RecordResultDelivery("help", 1, new("send", "uncertain", "lost-response"), SourceHost);
        var stopped = e.SetGoal(resumed with { Stopped = true }, Operator, resumed.Revision);
        Assert.Throws<CoordinationConflictException>(() => e.Reauthorize(
            new("help", 1, resumed.ControlRevision, stopped.ControlRevision, "resume"), Operator));
        var again = e.SetGoal(stopped with { Stopped = false }, Operator, stopped.Revision);
        Assert.Equal("reauthorization-native-operation-held", Assert.Throws<CoordinationConflictException>(() => e.Reauthorize(
            new("help", 1, resumed.ControlRevision, again.ControlRevision, "resume"), Operator)).Message);
        Assert.Throws<CoordinationConflictException>(() => e.BeginResultDelivery("help", 1, "send-again", SourceHost));
        Assert.Equal("send", state.ResultOutbox["help"].AttemptId);
        Assert.Equal("uncertain", state.ResultOutbox["help"].State);
    }

    [Fact]
    public async Task StoreReopenRetainsAtomicRecoveryRetirementAndExactReauthorization() {
        var directory = LedgerTestPaths.NewDirectory("coordination-recovery");
        var path = Path.Combine(directory, "coordination.sqlite");
        var later = Now.AddSeconds(2);
        try {
            using (var store = new CoordinationStore(path, RecoveryPolicy)) {
                await store.InitializeAsync();
                await store.ExecuteAsync((e, s) => {
                    Initialize(e, s); var work = e.Submit(Help(), Requester); Start(e, s, work);
                    Create(e, "next");
                    var paused = e.SetGoal(s.Goals["task"] with { Paused = true }, Operator, 1);
                    var resumed = e.SetGoal(paused with { Paused = false }, Operator, paused.Revision);
                    e.Reauthorize(new("next", 0, 1, resumed.ControlRevision, "resume"), Operator);
                    return true;
                }, Now);
                await store.ExecuteAsync((e, s) => {
                    e.ObserveHost(s.Hosts["helper-host"] with { BootId = "new-boot", ObservedAtUtc = later,
                        CreatableProviders = ["provider-a"] }, HelperHost);
                    return true;
                }, later);
                var failure = await Assert.ThrowsAsync<CoordinationConflictException>(() => store.ExecuteAsync<bool>((e, s) => {
                    e.Reconcile(Exclusion(s.Work["help"], later, "new-boot", s.Bindings["help"]), HelperHost);
                    throw new CoordinationConflictException("after-recovery");
                }, later));
                Assert.Equal("after-recovery", failure.Message);
                await store.ExecuteAsync((e, s) => {
                    Assert.Empty(s.Reconciliations); Assert.Empty(s.Retirements); Assert.Empty(s.ResultOutbox);
                    Assert.Equal("reconcile", s.Work["help"].State);
                    return e.Reconcile(Exclusion(s.Work["help"], later, "new-boot", s.Bindings["help"]), HelperHost);
                }, later);
            }
            using var reopened = new CoordinationStore(path, RecoveryPolicy);
            await reopened.InitializeAsync();
            var snapshot = await reopened.ExecuteAsync((e, s) => {
                e.Reconcile(s.Reconciliations["help"], HelperHost);
                Assert.Throws<CoordinationConflictException>(() => e.Receipt("help", "started", Execution(s.Work["help"]), HelperHost));
                return s;
            }, later);
            Assert.Equal("failed", snapshot.Work["help"].State);
            Assert.Equal("boot", snapshot.Reconciliations["help"].OriginalBootId);
            Assert.Equal("new-boot", snapshot.Reconciliations["help"].CurrentBootId);
            Assert.Equal("native-exclusion", snapshot.Reconciliations["help"].EvidenceRef);
            Assert.Empty(snapshot.Retirements["help"].Evidence);
            Assert.Null(snapshot.Retirements["help"].CompletedAtUtc);
            Assert.Equal("helper-session", snapshot.Retirements["help"].Binding.SessionId);
            Assert.Equal("admitted", snapshot.Work["next"].State);
            Assert.Equal(1, snapshot.Work["next"].Request.ControlRevision);
            Assert.Equal(3, snapshot.Work["next"].AuthorizedControlRevision);
            var journal = await reopened.ChangesAsync(0, 100);
            Assert.Single(journal, c => c.Stage == "reconciled");
            Assert.Single(journal, c => c.Stage == "reauthorized");
            Assert.Single(journal, c => c.Stage == "retirement-required");
        } finally {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
