using System.Security.Cryptography;
using System.Text;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Coordination;
using Xunit;
using static IntelligenceX.Ledger.Tests.CoordinationLifecycleTests;

namespace IntelligenceX.Ledger.Tests;

public sealed class CoordinationRequesterTests {
    internal static readonly CoordinationPrincipal EnrollmentHost = SourceHost with {
        Projects = ["sample", "second-project"], AllowRequesterEnrollment = true
    };
    internal static readonly string Token = new('d', 64);
    internal static readonly RequesterEnrollment Enrollment = new("scoped-requester", RequesterWorker.Id,
        "native-goal", "native-source", "authorization", [new("sample", Candidate.Branch)],
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Token))), "boot", Now, true);

    internal static (CoordinationState State, CoordinationPrincipal Caller) Enrolled() {
        var state = CreateState();
        var engine = new CoordinationEngine(state, Now);
        engine.EnrollRequester(Enrollment, EnrollmentHost);
        var caller = engine.AuthenticateRequester(Token)!;
        engine.SetGoal(new(Enrollment.GoalId, "", 0, false, false, "authorization", ["result-verified"]), caller, 0);
        return (state, caller);
    }

    internal static CoordinationRequest ScopedHelp(string id = "help") => Help(id) with { GoalId = Enrollment.GoalId, ReadOnly = true };
    private static void Reject(string code, Action action) =>
        Assert.Equal(code, Assert.Throws<CoordinationConflictException>(action).Message);

    [Fact]
    public void RegistrationBindsImmutableIdentityAndRenewsOnlyTrustedSourceScopesWithIncreasingObservation() {
        var state = CreateState();
        var engine = new CoordinationEngine(state, Now);
        Reject("requester-enrollment-role-denied", () => engine.EnrollRequester(Enrollment, SourceHost));
        Reject("requester-project-not-authorized", () => engine.EnrollRequester(Enrollment, EnrollmentHost with { Projects = [] }));
        Reject("requester-worker-conflict", () => engine.EnrollRequester(Enrollment, EnrollmentHost with { MachineId = "helper-host" }));
        Assert.Empty(state.Requesters);
        engine.EnrollRequester(Enrollment, EnrollmentHost);
        var changes = engine.Changes.Count;
        Assert.Equal(Enrollment, engine.EnrollRequester(Enrollment, EnrollmentHost));
        Assert.Equal(changes, engine.Changes.Count);
        foreach (var mutation in new[] {
            Enrollment with { WorkerId = "helper-worker" }, Enrollment with { GoalId = "other-goal" },
            Enrollment with { SourceRef = "other-source" }, Enrollment with { AuthorizationRef = "other-authorization" },
            Enrollment with { TokenSha256 = new('a', 64) }
        }) {
            // A worker change must reach the immutable-binding guard on the same authenticated machine.
            if (mutation.WorkerId != Enrollment.WorkerId) {
                state.Workers["same-host-worker"] = RequesterWorker with { Id = "same-host-worker", SessionId = "other-session" };
                Reject("requester-identity-conflict", () => engine.EnrollRequester(mutation with { WorkerId = "same-host-worker" }, EnrollmentHost));
            } else Reject("requester-identity-conflict", () => engine.EnrollRequester(mutation, EnrollmentHost));
        }
        Reject("requester-observation-out-of-order", () => engine.EnrollRequester(Enrollment with { DispatchAllowed = false }, EnrollmentHost));
        var renewal = Enrollment with { ObservedAtUtc = Now.AddSeconds(1), Sources = [
            new("sample", Candidate.Branch), new("second-project", "feature/other") ], DispatchAllowed = false };
        var later = new CoordinationEngine(state, Now.AddSeconds(1));
        later.EnrollRequester(renewal, EnrollmentHost);
        Assert.Equal(renewal, state.Requesters[Enrollment.Id]);
        var principal = later.AuthenticateRequester(Token)!;
        Assert.Equal(Enrollment.Id, principal.Id);
        Assert.Equal("client", principal.Role);
        Assert.Equal(RequesterWorker.Id, principal.WorkerId);
        Assert.Equal(RequesterWorker.MachineId, principal.MachineId);
        Assert.Equal(new[] { "sample", "second-project" }, principal.Projects);
        Assert.Empty(principal.Token);
        Assert.Null(later.AuthenticateRequester(new('x', 64)));
    }

    [Theory]
    [InlineData("duplicate-sources", "invalid-requester-enrollment")]
    [InlineData("too-many-sources", "invalid-requester-enrollment")]
    [InlineData("foreign-project", "requester-project-not-authorized")]
    [InlineData("boot", "requester-observation-held")]
    [InlineData("stale", "requester-observation-held")]
    [InlineData("session", "requester-worker-conflict")]
    [InlineData("ineligible", "requester-observation-held")]
    public void RegistrationRejectsUnqualifiedIdentityAndSourceBounds(string condition, string code) {
        var state = CreateState();
        var enrollment = Enrollment;
        switch (condition) {
            case "duplicate-sources": enrollment = enrollment with { Sources = [enrollment.Sources[0], enrollment.Sources[0]] }; break;
            case "too-many-sources": enrollment = enrollment with { Sources = Enumerable.Range(0, 65).Select(i => new RequesterSourceScope("sample", "branch-" + i)).ToArray() }; break;
            case "foreign-project": enrollment = enrollment with { Sources = [new("foreign", "branch")] }; break;
            case "boot": enrollment = enrollment with { BootId = "old-boot" }; break;
            case "stale": enrollment = enrollment with { ObservedAtUtc = Now.AddSeconds(-31) }; break;
            case "session": state.Workers[RequesterWorker.Id] = RequesterWorker with { SessionId = null }; break;
            case "ineligible": state.Workers[RequesterWorker.Id] = RequesterWorker with { Eligible = false }; break;
        }
        Reject(code, () => new CoordinationEngine(state, Now).EnrollRequester(enrollment, EnrollmentHost));
        Assert.Empty(state.Requesters);
    }

    [Theory]
    [InlineData("host-stale")]
    [InlineData("worker-stale")]
    [InlineData("old-boot")]
    public void HeldEnrollmentStillRequiresFreshHostAndWorkerWithExactCurrentBoot(string condition) {
        var state = CreateState();
        var at = condition == "old-boot" ? Now.AddSeconds(1) : Now.AddSeconds(121);
        var engine = new CoordinationEngine(state, at);
        if (condition == "worker-stale") engine.ObserveHost(new("source-host", "boot", at, ["review"]), SourceHost);
        else if (condition == "host-stale") engine.ObserveWorker(RequesterWorker with {
            Eligible = false, UnavailableReason = "native-turn-held", ObservedAtUtc = at
        }, SourceHost);
        else engine.ObserveHost(new("source-host", "next-boot", at, ["review"]), SourceHost);
        Reject("requester-observation-held", () => engine.EnrollRequester(Enrollment with {
            DispatchAllowed = false, ObservedAtUtc = at
        }, EnrollmentHost));
        Assert.Empty(state.Requesters);
    }

    [Fact]
    public void ScopedClientCannotCreateAnotherGoalOrSubmitOutsideGoalAuthorizationOrSource() {
        var (state, caller) = Enrolled();
        var engine = new CoordinationEngine(state, Now);
        Reject("requester-scope-conflict", () => engine.SetGoal(state.Goals[Enrollment.GoalId] with { Id = "other-goal" }, caller, 0));
        foreach (var request in new[] {
            ScopedHelp() with { GoalId = "task" }, ScopedHelp() with { AuthorizationRef = "different" },
            ScopedHelp() with { Checkpoint = Candidate with { Branch = "feature/foreign" } }
        }) Reject("requester-scope-conflict", () => engine.Submit(request, caller));
        Reject("project-not-authorized", () => engine.Submit(ScopedHelp() with {
            Checkpoint = Candidate with { Project = "foreign" }
        }, caller));
        Assert.Empty(state.Work);
        var second = Candidate with { Project = "second-project", Branch = "feature/second" };
        var later = new CoordinationEngine(state, Now.AddSeconds(1));
        later.EnrollRequester(Enrollment with { ObservedAtUtc = Now.AddSeconds(1), Sources = [
            Enrollment.Sources[0], new(second.Project, second.Branch) ] }, EnrollmentHost);
        caller = later.AuthenticateRequester(Token)!;
        var requestInSecondSource = new CoordinationRequest("second", Enrollment.GoalId, "build", "review", "source-host", null,
            new(BuildSlots: 1), second, [], 1, Enrollment.AuthorizationRef);
        Assert.Equal("admitted", later.Submit(requestInSecondSource, caller).State);
    }

    [Theory]
    [InlineData("dispatch")]
    [InlineData("stale")]
    [InlineData("worker")]
    [InlineData("boot")]
    public void HeldSourceBlocksAdmissionRenewalAndNewWorkWhileRetainingOccupancyAndTerminalEvidence(string condition) {
        var (state, caller) = Enrolled();
        var engine = new CoordinationEngine(state, Now);
        var active = engine.Submit(ScopedHelp("active"), caller);
        Start(engine, state, active);
        Assert.Equal("queued", engine.Submit(ScopedHelp("queued"), caller).State);
        if (condition == "stale") {
            var refresh = new CoordinationEngine(state, Now.AddSeconds(119));
            refresh.ObserveHost(new("source-host", "boot", Now.AddSeconds(119), ["review"]), SourceHost);
            refresh.ObserveWorker(RequesterWorker with { ObservedAtUtc = Now.AddSeconds(119) }, SourceHost);
            refresh.ObserveHost(new("helper-host", "boot", Now.AddSeconds(119), ["platform-help", "review"]), HelperHost);
            refresh.ObserveWorker(state.Workers["helper-worker"] with { ObservedAtUtc = Now.AddSeconds(119) }, HelperHost);
            refresh.Receipt("active", "renew", Execution(active), HelperHost);
        }
        var laterTime = Now.AddSeconds(condition == "stale" ? 121 : 1);
        var later = new CoordinationEngine(state, laterTime);
        if (condition == "dispatch") later.EnrollRequester(Enrollment with { DispatchAllowed = false, ObservedAtUtc = laterTime }, EnrollmentHost);
        if (condition == "worker") later.ObserveWorker(RequesterWorker with { Eligible = false, UnavailableReason = "native-held", ObservedAtUtc = laterTime }, SourceHost);
        if (condition == "boot") later.ObserveHost(new("source-host", "next-boot", laterTime, ["review"]), SourceHost);
        later.Schedule();
        Assert.Equal("reconcile", state.Work["active"].State);
        Assert.Equal("requester-held", state.Work["active"].Reason);
        Assert.Equal("queued", state.Work["queued"].State);
        Assert.Equal("requester-held", state.Work["queued"].Reason);
        Assert.Equal(1, later.Summary(null).Machines.Single(m => m.Id == "helper-host").Used.AgentSlots);
        Reject("requester-dispatch-held", () => later.Submit(ScopedHelp("new"), caller));
        Reject("grant-needs-reconciliation", () => later.Receipt("active", "renew", Execution(active), HelperHost));
        later.Receipt("active", "terminal", Execution(active, "held-source-result"), HelperHost);
        Assert.Equal("succeeded", state.Work["active"].State);
        Reject("result-recipient-held", () => later.BeginResultDelivery("active", 1, "send", SourceHost));
        Assert.NotNull(later.ConsumeResult("active", 1, "held-source-result", Candidate, caller).ConsumedAtUtc);
    }

    [Fact]
    public void RootRequesterCanAdvanceCommitAndConsumeFrozenReviewWhileStaticRecipientKeepsExactCandidateGuard() {
        var (state, caller) = Enrolled();
        var engine = new CoordinationEngine(state, Now.AddSeconds(1));
        engine.ObserveWorker(RequesterWorker with { Candidate = Candidate with { Commit = new('b', 40) },
            ObservedAtUtc = Now.AddSeconds(1) }, SourceHost);
        var work = engine.Submit(ScopedHelp(), caller);
        Start(engine, state, work);
        engine.Receipt("help", "terminal", Execution(work, "result"), HelperHost);
        var later = new CoordinationEngine(state, Now.AddSeconds(2));
        later.ObserveWorker(RequesterWorker with { Candidate = Candidate with { Commit = new('c', 40) }, ObservedAtUtc = Now.AddSeconds(2) }, SourceHost);
        Assert.Equal("sending", later.BeginResultDelivery("help", 1, "return-old-review", SourceHost).State);
        Assert.Equal(Candidate, state.Work["help"].Request.Checkpoint);
        Assert.Equal(Candidate, state.ResultOutbox["help"].Candidate);
        Assert.NotNull(later.ConsumeResult("help", 1, "result", Candidate, caller).ConsumedAtUtc);
        Assert.True(later.SetGoal(state.Goals[Enrollment.GoalId] with { Accepted = true, ResultRef = "goal-result" }, caller, 1).Accepted);

        var legacy = CreateState();
        var original = new CoordinationEngine(legacy, Now);
        Finish(original, legacy);
        var advanced = new CoordinationEngine(legacy, Now.AddSeconds(1));
        advanced.ObserveWorker(RequesterWorker with { Candidate = Candidate with { Commit = new('b', 40) }, ObservedAtUtc = Now.AddSeconds(1) }, SourceHost);
        Reject("result-recipient-held", () => advanced.BeginResultDelivery("help", 1, "legacy-return", SourceHost));
    }

    [Fact]
    public void NewNativeSourceSupersedesOldDispatchWithoutLosingCredentialsHistoryOrOccupiedGrants() {
        var (state, caller) = Enrolled();
        var engine = new CoordinationEngine(state, Now);
        var active = engine.Submit(ScopedHelp("old-active"), caller);
        Start(engine, state, active);
        engine.Submit(ScopedHelp("old-queued"), caller);
        var successor = Enrollment with { Id = "next-requester", GoalId = "next-goal", SourceRef = "next-source",
            TokenSha256 = new('a', 64), ObservedAtUtc = Now.AddSeconds(1) };
        var later = new CoordinationEngine(state, Now.AddSeconds(1));
        later.EnrollRequester(successor, EnrollmentHost);
        Assert.False(state.Requesters[Enrollment.Id].DispatchAllowed);
        Assert.Equal(successor.Id, state.RequesterSuccessors[Enrollment.Id]);
        Assert.Equal(Enrollment.TokenSha256, state.Requesters[Enrollment.Id].TokenSha256);
        Assert.Equal(caller.Id, later.AuthenticateRequester(Token)!.Id);
        Assert.Equal("reconcile", state.Work["old-active"].State);
        Assert.Equal("requester-held", state.Work["old-queued"].Reason);
        Assert.Equal(1, later.Summary(null).Machines.Single(m => m.Id == "helper-host").Used.AgentSlots);
        Reject("requester-source-superseded", () => later.EnrollRequester(Enrollment with { ObservedAtUtc = Now.AddSeconds(2) }, EnrollmentHost));
        Reject("requester-session-already-enrolled", () => later.EnrollRequester(successor with {
            Id = "alias", TokenSha256 = new('b', 64), ObservedAtUtc = Now.AddSeconds(2)
        }, EnrollmentHost));
        later.Receipt("old-active", "terminal", Execution(active, "old-result"), HelperHost);
        Assert.NotNull(later.ConsumeResult("old-active", 1, "old-result", Candidate, caller).ConsumedAtUtc);
        Assert.True(later.SetGoal(state.Goals[Enrollment.GoalId] with {
            Accepted = false, Paused = true
        }, caller, 1).Paused);
    }

    [Fact]
    public void BoundNativeHelperRequiresItsActualWorkerAndParentGoalAndPreservesNestedReadOnlyGuard() {
        var (state, caller) = Enrolled();
        var engine = new CoordinationEngine(state, Now);
        var parent = engine.Submit(ScopedHelp("parent"), caller);
        Start(engine, state, parent);
        var host = HelperHost with { Projects = ["sample"], AllowRequesterEnrollment = true };
        var childEnrollment = Enrollment with { Id = "child-requester", WorkerId = "helper-worker", SourceRef = "child-source",
            TokenSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("child-token"))) };
        Reject("requester-assignment-conflict", () => engine.EnrollRequester(childEnrollment with { GoalId = "other-goal" }, host));
        var helper = state.Workers["helper-worker"];
        Reject("session-already-enrolled", () => engine.ObserveWorker(helper with {
            Id = "helper-alias", TemporaryWorker = false
        }, host));
        // A historical duplicate still cannot acquire a new requester identity around the existing binding.
        state.Workers["helper-alias"] = helper with { Id = "helper-alias", TemporaryWorker = false };
        Reject("requester-assignment-conflict", () => engine.EnrollRequester(childEnrollment with { WorkerId = "helper-alias" }, host));
        engine.EnrollRequester(childEnrollment, host);
        var childCaller = engine.AuthenticateRequester("child-token")!;
        var nested = ScopedHelp("nested") with { Capability = "review", ReturnTo = new(helper.Id, helper.MachineId,
            helper.Provider, helper.SessionId!, Candidate, "parent", parent.Generation) };
        Reject("write-help-source-handoff-required", () => engine.Submit(nested with { ReadOnly = false }, childCaller));
        Reject("parent-binding-conflict", () => engine.Submit(nested with {
            ReturnTo = nested.ReturnTo! with { ParentGeneration = parent.Generation + 1 }
        }, childCaller));
        Reject("parent-binding-required", () => engine.Submit(nested with {
            ReturnTo = nested.ReturnTo! with { ParentRequestId = null, ParentGeneration = 0 }
        }, childCaller));
        Assert.Equal("queued", engine.Submit(nested, childCaller).State);
        Assert.Equal(parent.Generation, state.Work["nested"].Request.ReturnTo!.ParentGeneration);
    }

    [Fact]
    public void AtomicSourceSwitchPreventsOldQueuedAdmissionAndRejectedObservationHasNoPartialEffects() {
        var (state, caller) = Enrolled();
        var setup = new CoordinationEngine(state, Now.AddSeconds(1));
        setup.ObserveWorker(state.Workers["helper-worker"] with {
            Eligible = false, UnavailableReason = "temporarily-unavailable", ObservedAtUtc = Now.AddSeconds(1)
        }, HelperHost);
        Assert.Equal("queued", setup.Submit(ScopedHelp("old-queued"), caller).State);
        setup.ObserveWorker(RequesterWorker with {
            Eligible = false, UnavailableReason = "native-turn-held", ObservedAtUtc = Now.AddSeconds(1)
        }, SourceHost);
        new CoordinationEngine(state, Now.AddSeconds(2)).ObserveWorker(state.Workers["helper-worker"] with {
            Eligible = true, UnavailableReason = null, ObservedAtUtc = Now.AddSeconds(2)
        }, HelperHost);
        var at = Now.AddSeconds(3);
        var freshWorker = RequesterWorker with { ObservedAtUtc = at };
        var nextToken = new string('e', 64);
        var next = Enrollment with { Id = "next-requester", GoalId = "next-goal", SourceRef = "next-source",
            TokenSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(nextToken))),
            ObservedAtUtc = at, WorkerObservation = freshWorker };
        // The two-command path reproduces the admission window on the same starting projection.
        var control = System.Text.Json.JsonSerializer.Deserialize<CoordinationState>(System.Text.Json.JsonSerializer.Serialize(state))!;
        new CoordinationEngine(control, at).ObserveWorker(freshWorker, SourceHost);
        Assert.Equal("admitted", control.Work["old-queued"].State);

        var engine = new CoordinationEngine(state, at);
        var before = System.Text.Json.JsonSerializer.Serialize(state);
        Reject("requester-session-already-enrolled", () => engine.EnrollRequester(next with { SourceRef = Enrollment.SourceRef }, EnrollmentHost));
        Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(state));
        Assert.Empty(engine.Changes);
        engine.EnrollRequester(next, EnrollmentHost);
        Assert.Equal(freshWorker, state.Workers[RequesterWorker.Id]);
        Assert.Equal("queued", state.Work["old-queued"].State);
        Assert.Equal("requester-held", state.Work["old-queued"].Reason);
        Assert.Equal(0, state.Work["old-queued"].Generation);
        Assert.Null(state.Work["old-queued"].GrantId);
        Assert.DoesNotContain(engine.Changes, c => c.RequestId == "old-queued" && c.Stage == "admitted");
        var current = engine.AuthenticateRequester(nextToken)!;
        engine.SetGoal(new(next.GoalId, "", 0, false, false, next.AuthorizationRef, ["result"]), current, 0);
        Assert.Equal("admitted", engine.Submit(ScopedHelp("new-work") with { GoalId = next.GoalId }, current).State);
        Assert.Null(state.Work["old-queued"].GrantId);
        // An identical atomic command is an acknowledgement replay, including its worker observation.
        var changes = engine.Changes.Count;
        Assert.Equal(next, engine.EnrollRequester(next, EnrollmentHost));
        Assert.Equal(changes, engine.Changes.Count);
    }
}
