using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Coordination;
using Xunit;
using static IntelligenceX.Ledger.Tests.CoordinationLifecycleTests;

namespace IntelligenceX.Ledger.Tests;

public sealed class CoordinationOwnershipTests {
    private static readonly CoordinationPrincipal Operator = new("operator", "operator", "unused", []);

    [Theory]
    [InlineData("help")]
    [InlineData("review")]
    public void ExplicitNamedSessionReturnsResultWithoutRetiringPersonalWorker(string kind) {
        var state = CreateState(); var e = new CoordinationEngine(state, Now);
        var personal = new CoordinationWorker("personal-worker", "helper-host", "provider-a", true, null, Now,
            "personal-session", "personal-workspace", Candidate, BootId: "boot");
        e.ObserveWorker(personal, HelperHost);
        var request = Help("named") with {
            Kind = kind, MachineId = personal.MachineId, WorkerId = personal.Id, ReadOnly = true, ReuseSession = true
        };
        var work = e.Submit(request, Requester);
        Assert.Equal("admitted", work.State);
        Assert.Equal(personal.Id, work.Assignee);
        var binding = Start(e, state, work);
        var receipt = Execution(work, "named-result");
        e.Receipt(request.Id, "terminal", receipt, HelperHost);
        e.Receipt(request.Id, "terminal", receipt, HelperHost);
        Assert.Equal("succeeded", state.Work[request.Id].State);
        Assert.Equal("named-result", state.ResultOutbox[request.Id].ResultRef);
        Assert.Equal(request.ReturnTo, state.ResultOutbox[request.Id].ReturnTo);
        Assert.Empty(state.Retirements);
        Assert.Equal("binding-worker-not-temporary", Assert.Throws<CoordinationConflictException>(() =>
            e.RequireTemporaryWorker(binding)).Message);
        Assert.Throws<CoordinationConflictException>(() =>
            e.Retire(new(binding, "archived", "archive-proof"), HelperHost));
        e.BeginResultDelivery(request.Id, work.Generation, "named-send", SourceHost);
        e.RecordResultDelivery(request.Id, work.Generation, new("named-send", "submitted", "native-return"), SourceHost);
        e.ConsumeResult(request.Id, work.Generation, "named-result", Candidate, Requester);
        Assert.NotNull(state.ResultOutbox[request.Id].ConsumedAtUtc);
        Assert.Equal(binding, state.Bindings[request.Id]);
        Assert.Equal(personal, state.Workers[personal.Id]);
        Assert.Single(e.Changes, c => c.Stage == "terminal");
        Assert.Single(e.Changes, c => c.Stage == "result-intent");
        Assert.DoesNotContain(e.Changes, c => c.Stage == "retirement-required");
    }

    [Fact]
    public void ExplicitReuseOfTemporaryWorkerRetainsNormalRetirement() {
        var state = CreateState(); var e = new CoordinationEngine(state, Now);
        var work = e.Submit(Help() with { WorkerId = "helper-worker", MachineId = "helper-host", ReuseSession = true }, Requester);
        var binding = Start(e, state, work);
        e.Receipt(work.Request.Id, "terminal", Execution(work, "result"), HelperHost);
        Assert.Equal(binding, state.Retirements[work.Request.Id].Binding);
        Clean(e, binding);
        Assert.NotNull(state.Retirements[work.Request.Id].CompletedAtUtc);
        Assert.Null(state.ResultOutbox[work.Request.Id].ConsumedAtUtc);
    }

    [Fact]
    public void NewNamedRequestCannotReleaseConsumedLegacyPersonalBinding() {
        var state = CreateState(); var e = new CoordinationEngine(state, Now);
        var oldBinding = Finish(e, state);
        e.ConsumeResult("help", 1, "result", Candidate, Requester);
        Clean(e, oldBinding);
        // Historical projections lack explicit reuse permission, even with a named worker and consumed result.
        state.Workers[oldBinding.WorkerId] = state.Workers[oldBinding.WorkerId] with { TemporaryWorker = false };
        Assert.False(state.Work[oldBinding.RequestId].Request.ReuseSession);
        var next = e.Submit(Help("next") with {
            WorkerId = oldBinding.WorkerId, MachineId = oldBinding.MachineId, ReuseSession = true
        }, Requester);
        Assert.Equal("queued", next.State);
        Assert.Null(next.GrantId);
        Assert.Equal(oldBinding, state.Bindings[oldBinding.RequestId]);
        Assert.False(state.Bindings.ContainsKey(next.Request.Id));
    }

    [Theory]
    [InlineData("help", null, "helper-host")]
    [InlineData("help", "helper-worker", null)]
    [InlineData("review", null, "helper-host")]
    [InlineData("review", "helper-worker", null)]
    [InlineData("review", null, null)]
    [InlineData("agent", "helper-worker", "helper-host")]
    [InlineData("handoff", "helper-worker", "helper-host")]
    [InlineData("build", "helper-worker", "helper-host")]
    public void ReuseRequiresNamedHelpOrReviewWithDeliberateProviderSelection(string kind, string? worker, string? machine) {
        var state = CreateState(); var e = new CoordinationEngine(state, Now);
        var request = Help() with {
            Kind = kind, WorkerId = worker, MachineId = machine, ReadOnly = true, ReuseSession = true,
            RequiredProvider = kind == "review" ? null : "provider-a",
            ReturnTo = kind is "help" or "review" ? Help().ReturnTo : null
        };
        Assert.Equal("named-session-required", Assert.Throws<CoordinationConflictException>(() =>
            e.Submit(request, Requester)).Message);
        Assert.Empty(state.Work);
        Assert.Empty(state.Bindings);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("machine")]
    [InlineData("provider")]
    [InlineData("candidate")]
    [InlineData("unavailable")]
    [InlineData("provider-limit")]
    [InlineData("busy")]
    [InlineData("requester")]
    [InlineData("legacy")]
    public void NamedReuseCannotSubstituteAnEligibleTemporaryWorkerOrCreateFallback(string restriction) {
        var state = CreateState(); var e = new CoordinationEngine(state, Now);
        var personal = new CoordinationWorker("personal-worker", "helper-host", "provider-a", true, null, Now,
            "personal-session", "personal-workspace", Candidate, BootId: "boot");
        personal = restriction switch {
            "candidate" => personal with { Candidate = Candidate with { Commit = new('b', 40) } },
            "unavailable" => personal with { Eligible = false },
            "provider-limit" => personal with { Eligible = false, UnavailableReason = "provider-limit" },
            _ => personal
        };
        if (restriction != "missing") e.ObserveWorker(personal, HelperHost);
        if (restriction == "busy") {
            var busy = e.Submit(Help("busy") with { Kind = "agent", WorkerId = personal.Id, ReturnTo = null }, Requester);
            Assert.Equal(personal.Id, busy.Assignee);
        }
        e.ReplacePolicy(state.Policy with { Revision = 2, Machines = state.Policy.Machines.Select(m =>
            m with { AllowedCreateProviders = ["provider-a"] }).ToArray() }, Operator, 1);
        foreach (var host in new[] { HelperHost, SourceHost })
            e.ObserveHost(state.Hosts[host.MachineId!] with {
                ObservedAtUtc = Now.AddSeconds(1), CreatableProviders = ["provider-a"]
            }, host);
        var target = restriction == "requester" ? RequesterWorker : personal;
        var request = Help("restricted") with {
            Capability = "review", ReadOnly = true, ReuseSession = restriction != "legacy", WorkerId = target.Id,
            MachineId = restriction == "machine" ? "source-host" : target.MachineId,
            RequiredProvider = restriction == "provider" ? "provider-b" : "provider-a"
        };
        var work = e.Submit(request, Requester);
        Assert.Equal("queued", work.State);
        Assert.Null(work.Assignee);
        Assert.Null(work.GrantId);
        Assert.Equal("worker-unavailable", work.Reason);
        Assert.Throws<CoordinationConflictException>(() => Bind(e, work, state.Workers["helper-worker"], HelperHost));
        Assert.False(state.Bindings.ContainsKey(request.Id));
        Assert.False(state.Retirements.ContainsKey(request.Id));
    }

    [Theory]
    [InlineData("host")]
    [InlineData("worker")]
    [InlineData("session")]
    [InlineData("workspace")]
    [InlineData("candidate")]
    [InlineData("generation")]
    public void NamedSessionBindingRequiresExactAssignmentAndCandidate(string mismatch) {
        var state = CreateState(); var e = new CoordinationEngine(state, Now);
        var personal = new CoordinationWorker("personal-worker", "helper-host", "provider-a", true, null, Now,
            "personal-session", "personal-workspace", Candidate, BootId: "boot");
        e.ObserveWorker(personal, HelperHost);
        var work = e.Submit(Help("named") with {
            WorkerId = personal.Id, MachineId = personal.MachineId, ReuseSession = true
        }, Requester);
        var binding = new CoordinationSessionBinding(work.Request.Id, work.Generation, work.GrantId!, personal.Id,
            personal.MachineId, personal.Provider, personal.SessionId!, personal.WorkspaceId!, Candidate);
        var invalid = mismatch switch {
            "host" => binding with { MachineId = "source-host" },
            "worker" => binding with { WorkerId = "helper-worker", SessionId = "helper-session", WorkspaceId = "helper-workspace" },
            "session" => binding with { SessionId = "other-session" },
            "workspace" => binding with { WorkspaceId = "other-workspace" },
            "candidate" => binding with { Candidate = Candidate with { Commit = new('b', 40) } },
            _ => binding with { Generation = work.Generation + 1 }
        };
        var sequence = state.Sequence;
        Assert.Throws<CoordinationConflictException>(() => e.BindSession(invalid, HelperHost));
        Assert.Equal(sequence, state.Sequence);
        Assert.Empty(state.Bindings);
        Assert.Equal("native-binding-required", Assert.Throws<CoordinationConflictException>(() =>
            e.Receipt(work.Request.Id, "submitted", Execution(work), HelperHost)).Message);
        Assert.Equal(binding, e.BindSession(binding, HelperHost));
    }

    [Theory]
    [InlineData("help")]
    [InlineData("review")]
    public void HistoricalNamedNativeBindingRemainsProtectedWithoutExplicitReuse(string kind) {
        var state = CreateState(); var e = new CoordinationEngine(state, Now);
        var work = e.Submit(Help("legacy") with { Kind = kind, WorkerId = "helper-worker", ReadOnly = true }, Requester);
        var binding = Start(e, state, work);
        // Old projections may already have a named binding without the new opt-in or ownership attestation.
        state.Workers[binding.WorkerId] = state.Workers[binding.WorkerId] with { TemporaryWorker = false };
        Assert.False(work.Request.ReuseSession);
        var sequence = state.Sequence;
        Assert.Equal("native-binding-required", Assert.Throws<CoordinationConflictException>(() =>
            e.Receipt(work.Request.Id, "terminal", Execution(work, "legacy-result"), HelperHost)).Message);
        var later = Now.AddSeconds(122);
        e = new CoordinationEngine(state, later);
        e.ObserveHost(state.Hosts["helper-host"] with { ObservedAtUtc = later }, HelperHost);
        Assert.Equal("reconcile", state.Work[work.Request.Id].State);
        var beforeRecovery = state.Sequence;
        Assert.Equal("reconciliation-worker-not-temporary", Assert.Throws<CoordinationConflictException>(() =>
            e.Reconcile(new(work.Request.Id, work.Generation, work.GrantId!, "boot", "boot", later,
                "execution-excluded", "exclusion", "legacy-result", true, binding, Execution(work).ExecutionId), HelperHost)).Message);
        Assert.Equal(beforeRecovery, state.Sequence);
        Assert.True(beforeRecovery > sequence);
        Assert.Empty(state.ResultOutbox);
        Assert.Empty(state.Retirements);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void NamedSessionRecoveryPreservesResultAndRejectsForeignBindingWithoutRetirement(bool alreadyBound, bool sourceChanged) {
        var state = CreateState(); var e = new CoordinationEngine(state, Now);
        var personal = new CoordinationWorker("personal-worker", "helper-host", "provider-a", true, null, Now,
            "personal-session", "personal-workspace", Candidate, BootId: "boot");
        e.ObserveWorker(personal, HelperHost);
        var work = e.Submit(Help("recovery") with {
            MachineId = personal.MachineId, WorkerId = personal.Id, ReuseSession = true
        }, Requester);
        Assert.Equal("admitted", work.State);
        var binding = new CoordinationSessionBinding(work.Request.Id, work.Generation, work.GrantId!, personal.Id,
            personal.MachineId, personal.Provider, personal.SessionId!, personal.WorkspaceId!, Candidate);
        if (alreadyBound) Start(e, state, work);
        if (sourceChanged) {
            e.ObserveWorker(personal with { Candidate = Candidate with { Commit = new('b', 40) },
                ObservedAtUtc = Now.AddSeconds(1) }, HelperHost);
            if (alreadyBound) Assert.Equal("worker-source-changed", state.Work[work.Request.Id].Reason);
        }
        var later = Now.AddSeconds(122);
        e = new CoordinationEngine(state, later);
        e.ObserveHost(state.Hosts["helper-host"] with { ObservedAtUtc = later }, HelperHost);
        var receipt = new CoordinationReconciliation(work.Request.Id, work.Generation, work.GrantId!, "boot", "boot",
            later, "execution-excluded", "native-exclusion", "recovery-result", true, binding,
            alreadyBound ? Execution(work).ExecutionId : null);
        foreach (var foreign in new[] {
            binding with { WorkerId = "helper-worker", SessionId = "helper-session", WorkspaceId = "helper-workspace" },
            binding with { MachineId = "source-host" },
            binding with { Candidate = Candidate with { Commit = new('b', 40) } },
            binding with { SessionId = "foreign-session" },
            binding with { Generation = binding.Generation + 1 }
        }) {
            var sequence = state.Sequence;
            Assert.Throws<CoordinationConflictException>(() => e.Reconcile(receipt with { Binding = foreign }, HelperHost));
            Assert.Equal(sequence, state.Sequence);
            Assert.Empty(state.Reconciliations);
            Assert.Empty(state.ResultOutbox);
        }
        Assert.Throws<CoordinationConflictException>(() => e.Reconcile(receipt with {
            Disposition = "never-created", Binding = null
        }, HelperHost));
        var recovered = e.Reconcile(receipt, HelperHost);
        Assert.Equal("failed", recovered.State);
        Assert.Equal(binding, state.Bindings[work.Request.Id]);
        Assert.Equal("recovery-result", state.ResultOutbox[work.Request.Id].ResultRef);
        Assert.Equal(recovered, e.Reconcile(receipt, HelperHost));
        Assert.Empty(state.Retirements);
    }

    [Theory]
    [InlineData("agent")]
    [InlineData("handoff")]
    public void ConsumedNamedReviewAllowsOrdinaryReservationOnTheSamePersonalWorker(string kind) {
        var state = CreateState(); var e = new CoordinationEngine(state, Now);
        var personal = new CoordinationWorker("personal-worker", "helper-host", "provider-a", true, null, Now,
            "personal-session", "personal-workspace", Candidate, BootId: "boot");
        e.ObserveWorker(personal, HelperHost);
        var work = e.Submit(Help("named-review") with { Kind = "review", ReadOnly = true,
            WorkerId = personal.Id, MachineId = personal.MachineId, ReuseSession = true }, Requester);
        var binding = Start(e, state, work);
        e.Receipt(work.Request.Id, "terminal", Execution(work, "review-result"), HelperHost);
        var ordinary = Help("ordinary") with { Kind = kind, WorkerId = personal.Id, ReturnTo = null };
        Assert.Equal("queued", e.Submit(ordinary, Requester).State);
        e.ConsumeResult(work.Request.Id, work.Generation, "review-result", Candidate, Requester);
        Assert.Equal("admitted", state.Work[ordinary.Id].State);
        Assert.Equal(personal.Id, state.Work[ordinary.Id].Assignee);
        Assert.False(state.Work[ordinary.Id].Request.ReuseSession);
        Assert.Equal(binding, state.Bindings[work.Request.Id]);
        Assert.Empty(state.Retirements);
    }

    [Fact]
    public void ConsumedNamedAssignmentAllowsWorkerToRequestHelpAsRootAgain() {
        var state = CreateState(); var e = new CoordinationEngine(state, Now);
        var personal = new CoordinationWorker("personal-worker", "helper-host", "provider-a", true, null, Now,
            "personal-session", "personal-workspace", Candidate, BootId: "boot");
        e.ObserveWorker(personal, HelperHost);
        var root = new CoordinationPrincipal("personal-client", "client", "unused", ["sample"], personal.MachineId, personal.Id);
        e.ObserveWorker(new("review-helper", "review-host", "provider-b", true, null, Now,
            "review-session", "review-workspace", Candidate, BootId: "boot", TemporaryWorker: true), ReviewHost);
        e.SetGoal(new("personal-goal", "", 0, false, false, "authorization", ["result-verified"]), root, 0);
        var work = e.Submit(Help("borrowed") with {
            WorkerId = personal.Id, MachineId = personal.MachineId, ReadOnly = true, ReuseSession = true
        }, Requester);
        var binding = Start(e, state, work);
        var rootRequest = Help("root-help") with {
            GoalId = "personal-goal", ReadOnly = true, MachineId = "review-host", Capability = "review", RequiredProvider = "provider-b",
            ReturnTo = new(personal.Id, personal.MachineId, personal.Provider,
                personal.SessionId!, Candidate)
        };
        Assert.Equal("parent-binding-required", Assert.Throws<CoordinationConflictException>(() => e.Submit(rootRequest, root)).Message);
        var child = Help("child") with {
            ReadOnly = true, ReturnTo = rootRequest.ReturnTo! with { ParentRequestId = work.Request.Id, ParentGeneration = work.Generation }
        };
        Assert.Equal("admitted", e.Submit(child, root).State);
        e.Receipt(work.Request.Id, "terminal", Execution(work, "borrowed-result"), HelperHost);
        Assert.Equal("parent-binding-required", Assert.Throws<CoordinationConflictException>(() => e.Submit(rootRequest, root)).Message);
        e.ConsumeResult(work.Request.Id, work.Generation, "borrowed-result", Candidate, Requester);
        Assert.Equal("admitted", e.Submit(rootRequest, root).State);
        Assert.Equal(binding, state.Bindings[work.Request.Id]);
        Assert.False(state.Retirements.ContainsKey(work.Request.Id));
    }

    [Theory]
    [InlineData("help", false)]
    [InlineData("review", false)]
    [InlineData("help", true)]
    [InlineData("review", true)]
    public void AnotherRootCannotAssignRequesterAwaitingItsOwnResult(string kind, bool allowCreation) {
        var state = CreateState(); var e = new CoordinationEngine(state, Now);
        Finish(e, state);
        Assert.Null(state.ResultOutbox["help"].ConsumedAtUtc);
        Assert.False(state.Workers[RequesterWorker.Id].TemporaryWorker);
        var other = new CoordinationPrincipal("other-root", "client", "unused", ["sample"], "review-host", "other-root-worker");
        e.ObserveWorker(new(other.WorkerId!, "review-host", "provider-b", true, null, Now,
            "other-root-session", "other-root-workspace", Candidate, BootId: "boot"), ReviewHost);
        e.SetGoal(new("other-task", "", 0, false, false, "authorization", ["result-verified"]), other, 0);
        if (allowCreation) {
            e.ReplacePolicy(state.Policy with { Revision = 2, Machines = state.Policy.Machines.Select(m =>
                m.Id == "source-host" ? m with { AllowedCreateProviders = ["provider-a"] } : m).ToArray() }, Operator, 1);
            e.ObserveHost(state.Hosts["source-host"] with {
                ObservedAtUtc = Now.AddSeconds(1), CreatableProviders = ["provider-a"]
            }, SourceHost);
        }
        var request = Help("other-help") with {
            GoalId = "other-task", Kind = kind, Capability = "review", MachineId = "source-host", ReadOnly = true,
            ReturnTo = new(other.WorkerId!, "review-host", "provider-b", "other-root-session", Candidate)
        };
        var pending = e.Submit(request, other);
        Assert.Null(pending.Assignee);
        Assert.Equal(allowCreation ? "admitted" : "queued", pending.State);
        Assert.Equal(allowCreation ? "worker-creation-required" : "worker-unavailable", pending.Reason);
        Assert.Equal(allowCreation, pending.GrantId is not null);
        Assert.DoesNotContain(state.Bindings.Values, b => b.WorkerId == RequesterWorker.Id);

        var temporary = new CoordinationWorker("source-helper", "source-host", "provider-a", true, null, Now.AddSeconds(2),
            "source-helper-session", "source-helper-workspace", Candidate, BootId: "boot", TemporaryWorker: true);
        e.ObserveWorker(temporary, SourceHost);
        var admitted = state.Work[request.Id];
        Assert.Equal("admitted", admitted.State);
        var binding = Bind(e, admitted, temporary, SourceHost);
        Assert.Equal(temporary.Id, state.Work[request.Id].Assignee);
        e.Receipt(request.Id, "submitted", Execution(admitted), SourceHost);
        e.Receipt(request.Id, "terminal", Execution(admitted, "other-result"), SourceHost);
        Assert.Equal(binding, state.Retirements[request.Id].Binding);
        Assert.DoesNotContain(state.Retirements.Values, r => r.Binding.WorkerId == RequesterWorker.Id);
        e.ConsumeResult("help", 1, "result", Candidate, Requester);
        Assert.NotNull(state.ResultOutbox["help"].ConsumedAtUtc);
        Assert.True(state.Workers[RequesterWorker.Id].Eligible);
        Assert.False(state.Workers[RequesterWorker.Id].TemporaryWorker);
    }

    [Theory]
    [InlineData("agent")]
    [InlineData("help")]
    [InlineData("review")]
    public void ExactBindingRejectsNonTemporaryWorkerEvenWithAnAdmittedReservation(string kind) {
        var state = CreateState(); var e = new CoordinationEngine(state, Now);
        Finish(e, state);
        e.ReplacePolicy(state.Policy with { Revision = 2, Machines = state.Policy.Machines.Select(m =>
            m.Id == "helper-host" ? m with { AllowedCreateProviders = ["provider-a"] } : m).ToArray() }, Operator, 1);
        e.ObserveHost(state.Hosts["helper-host"] with {
            ObservedAtUtc = Now.AddSeconds(1), CreatableProviders = ["provider-a"]
        }, HelperHost);
        var borrowed = new CoordinationWorker("borrowed", "helper-host", "provider-a", true, null, Now.AddSeconds(2),
            "borrowed-session", "borrowed-workspace", Candidate, BootId: "boot");
        e.ObserveWorker(borrowed, HelperHost);
        var request = Help("binding") with {
            Kind = kind, Capability = "review", MachineId = "helper-host", ReadOnly = true,
            WorkerId = kind == "agent" ? borrowed.Id : null, ReturnTo = kind == "agent" ? null : Help().ReturnTo
        };
        var admitted = e.Submit(request, Requester);
        Assert.Equal("admitted", admitted.State);
        // General non-native reservations still select eligible personal workers; native creation reserves no worker.
        Assert.Equal(kind == "agent" ? borrowed.Id : null, admitted.Assignee);
        var error = Assert.Throws<CoordinationConflictException>(() => Bind(e, admitted, borrowed, HelperHost));
        Assert.Equal("binding-worker-not-temporary", error.Message);
        Assert.False(state.Bindings.ContainsKey(request.Id));
        Assert.False(state.Retirements.ContainsKey(request.Id));
        Assert.Equal(admitted, state.Work[request.Id]);
        if (kind == "agent") {
            e.Receipt(request.Id, "submitted", Execution(admitted), HelperHost);
            Assert.Equal("succeeded", e.Receipt(request.Id, "terminal", Execution(admitted, "ordinary-result"), HelperHost).State);
            Assert.False(state.Retirements.ContainsKey(request.Id));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LegacyBoundReservationCannotTerminalizeWithoutTemporaryOwnership(bool missingWorker) {
        var state = CreateState(); var e = new CoordinationEngine(state, Now);
        var admitted = e.Submit(Help("legacy-agent") with {
            Kind = "agent", WorkerId = "helper-worker", ReturnTo = null
        }, Requester);
        var binding = Start(e, state, admitted);
        // Reconstruct a persisted binding from before temporary ownership was required.
        if (missingWorker) state.Workers.Remove(binding.WorkerId);
        else state.Workers[binding.WorkerId] = state.Workers[binding.WorkerId] with { TemporaryWorker = false };
        var before = state.Work[admitted.Request.Id];
        var sequence = state.Sequence;

        Assert.Equal("binding-worker-not-temporary", Assert.Throws<CoordinationConflictException>(() =>
            e.Receipt(admitted.Request.Id, "terminal", Execution(admitted, "legacy-result"), HelperHost)).Message);

        Assert.Equal(before, state.Work[admitted.Request.Id]);
        Assert.Equal(binding, state.Bindings[admitted.Request.Id]);
        Assert.Equal(sequence, state.Sequence);
        Assert.Empty(state.Retirements);
        Assert.Empty(state.ResultOutbox);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void RetainedRetirementRequiresTemporaryOwnershipBeforeArchiveOrReplay(bool missingWorker, bool replay) {
        var state = CreateState(); var e = new CoordinationEngine(state, Now);
        var binding = Finish(e, state);
        var receipt = new CoordinationRetirementReceipt(binding, "archived", "archive-proof");
        if (replay) e.Retire(receipt, HelperHost);
        if (missingWorker) state.Workers.Remove(binding.WorkerId);
        else state.Workers[binding.WorkerId] = state.Workers[binding.WorkerId] with { TemporaryWorker = false };
        var work = state.Work[binding.RequestId];
        var job = state.Retirements[binding.RequestId];
        var sequence = state.Sequence;

        Assert.Equal("binding-worker-not-temporary", Assert.Throws<CoordinationConflictException>(() =>
            e.Retire(receipt, HelperHost)).Message);

        Assert.Equal(work, state.Work[binding.RequestId]);
        Assert.Equal(binding, state.Bindings[binding.RequestId]);
        Assert.Equal(job, state.Retirements[binding.RequestId]);
        Assert.Equal(sequence, state.Sequence);
        Assert.Equal(replay, job.Evidence.ContainsKey("archived"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WorkerOwnershipCategoryIsImmutableAcrossObservationsAndSessionAliases(bool temporary) {
        var state = CreateState(); var e = new CoordinationEngine(state, Now);
        var original = state.Workers[temporary ? "helper-worker" : RequesterWorker.Id];
        var host = temporary ? HelperHost : SourceHost;
        var changed = original with { TemporaryWorker = !temporary, ObservedAtUtc = Now.AddSeconds(1) };
        Assert.Equal("worker-identity-conflict", Assert.Throws<CoordinationConflictException>(() =>
            e.ObserveWorker(changed, host)).Message);
        Assert.Equal(original, state.Workers[original.Id]);
        Assert.Equal("session-already-enrolled", Assert.Throws<CoordinationConflictException>(() =>
            e.ObserveWorker(changed with { Id = "alias-worker" }, host)).Message);
        Assert.False(state.Workers.ContainsKey("alias-worker"));
        var refreshed = original with { ObservedAtUtc = Now.AddSeconds(2) };
        e.ObserveWorker(refreshed, host);
        Assert.Equal(refreshed, state.Workers[original.Id]);
    }
}
