using System.Text.Json;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Coordination;
using Xunit;
using static IntelligenceX.Ledger.Tests.CoordinationLifecycleTests;

namespace IntelligenceX.Ledger.Tests;

public sealed class CoordinationCancellationTests {
    internal static readonly CoordinationCancellation Cancellation = new(0, "obsolete-source");

    internal static CoordinationWork Queue(CoordinationEngine engine, CoordinationState state) {
        state.Policy = state.Policy with { Enabled = false };
        return engine.Submit(Help("obsolete") with { Kind = "review", ReadOnly = true }, Requester);
    }

    [Fact]
    public void CancelsImmutableQueuedIntentIdempotentlyAndRetainsGoalAcceptance() {
        var state = CreateState();
        var engine = new CoordinationEngine(state, Now);
        var queued = Queue(engine, state);
        Assert.Equal("queued", queued.State);
        // Cancellation remains available after the original source snapshot becomes stale.
        engine.ObserveWorker(RequesterWorker with {
            Candidate = Candidate with { Commit = new('b', 40) }, ObservedAtUtc = Now.AddSeconds(1)
        }, SourceHost);
        var goal = state.Goals["task"];
        Assert.Equal("goal-acceptance-incomplete", Assert.Throws<CoordinationConflictException>(() =>
            engine.SetGoal(goal with { Accepted = true, ResultRef = "task-result" }, Requester, goal.Revision)).Message);
        var cancelled = engine.Cancel("obsolete", Cancellation, Requester);
        Assert.Equal("cancelled", cancelled.State);
        Assert.Equal(0, cancelled.Generation);
        Assert.Equal("obsolete-source", cancelled.Reason);
        Assert.Same(queued.Request, cancelled.Request);
        Assert.Equal(goal, state.Goals["task"]);
        Assert.Null(cancelled.GrantId);
        Assert.Null(cancelled.ExecutionId);
        Assert.Null(cancelled.ResultRef);
        Assert.Empty(state.Bindings);
        Assert.Empty(state.ResultOutbox);
        Assert.Empty(state.Reconciliations);
        Assert.Empty(state.Retirements);
        Assert.Empty(engine.Summary("sample").Items);
        var changes = engine.Changes.Where(c => c.RequestId == "obsolete").ToArray();
        Assert.Equal(new[] { "requested", "cancelled", "cancellation-accepted", "accepted" }, changes.Select(c => c.Stage));
        Assert.All(changes.Skip(1), change => {
            Assert.Equal(Requester.Id, change.Principal);
            Assert.Equal(0, change.Generation);
            Assert.Equal(Cancellation.ReasonRef, change.EvidenceRef);
        });
        var sequence = state.Sequence;
        Assert.Equal(cancelled, engine.Cancel("obsolete", Cancellation, Requester));
        Assert.Equal(sequence, state.Sequence);
        Assert.Equal("cancellation-receipt-conflict", Assert.Throws<CoordinationConflictException>(() =>
            engine.Cancel("obsolete", Cancellation with { ReasonRef = "different-reason" }, Requester)).Message);
        Assert.Equal(cancelled, engine.Submit(queued.Request, Requester));
        Assert.Equal("request-id-conflict", Assert.Throws<CoordinationConflictException>(() =>
            engine.Submit(queued.Request with { Checkpoint = Candidate with { Commit = new('b', 40) } }, Requester)).Message);
        Assert.True(engine.SetGoal(goal with { Accepted = true, ResultRef = "task-result" }, Requester, goal.Revision).Accepted);
    }

    [Theory]
    [InlineData("foreign-client", "cancel-owner-conflict")]
    [InlineData("host", "cancel-owner-conflict")]
    [InlineData("operator", "cancel-owner-conflict")]
    [InlineData("project", "cancel-owner-conflict")]
    [InlineData("generation", "cancel-generation-conflict")]
    [InlineData("negative-generation", "cancel-generation-conflict")]
    [InlineData("reason", "invalid-cancellation-reason")]
    [InlineData("long-reason", "invalid-cancellation-reason")]
    public void ValidatesOwnerGenerationAndReasonBeforeMutationAndReplay(string mismatch, string expected) {
        var state = CreateState();
        var engine = new CoordinationEngine(state, Now);
        Queue(engine, state);
        var caller = mismatch switch {
            "foreign-client" => Requester with { Id = "another-requester" },
            "host" => Requester with { Role = "host" },
            "operator" => Requester with { Role = "operator" },
            "project" => Requester with { Projects = ["another-project"] },
            _ => Requester
        };
        var command = mismatch switch {
            "generation" => Cancellation with { ExpectedGeneration = 1 },
            "negative-generation" => Cancellation with { ExpectedGeneration = -1 },
            "reason" => Cancellation with { ReasonRef = "private/path" },
            "long-reason" => Cancellation with { ReasonRef = new('a', 129) },
            _ => Cancellation
        };
        foreach (var replay in new[] { false, true }) {
            if (replay) engine.Cancel("obsolete", Cancellation, Requester);
            var before = JsonSerializer.Serialize(state);
            var count = engine.Changes.Count;
            Assert.Equal(expected, Assert.Throws<CoordinationConflictException>(() =>
                engine.Cancel("obsolete", command, caller)).Message);
            Assert.Equal(before, JsonSerializer.Serialize(state));
            Assert.Equal(count, engine.Changes.Count);
        }
    }

    [Theory]
    [InlineData("admitted")]
    [InlineData("submitted")]
    [InlineData("started")]
    [InlineData("reconcile")]
    [InlineData("succeeded")]
    public void RejectsCancellationAfterAdmission(string stage) {
        var state = CreateState();
        var engine = new CoordinationEngine(state, Now);
        var work = engine.Submit(Help(), Requester);
        if (stage != "admitted") {
            Bind(engine, work, state.Workers["helper-worker"], HelperHost);
            engine.Receipt("help", "submitted", Execution(work), HelperHost);
        }
        if (stage is "started" or "succeeded") engine.Receipt("help", "started", Execution(work), HelperHost);
        if (stage == "succeeded") engine.Receipt("help", "terminal", Execution(work, "answer"), HelperHost);
        if (stage == "reconcile") {
            engine = new CoordinationEngine(state, Now.AddSeconds(121));
            engine.Schedule();
        }
        Assert.Equal(stage, state.Work["help"].State);
        var before = JsonSerializer.Serialize(state);
        foreach (var generation in new long[] { 0, work.Generation })
            Assert.Equal("cancel-generation-conflict", Assert.Throws<CoordinationConflictException>(() =>
                engine.Cancel("help", Cancellation with { ExpectedGeneration = generation }, Requester)).Message);
        Assert.Equal(before, JsonSerializer.Serialize(state));
    }

    [Theory]
    [InlineData("grant")]
    [InlineData("execution")]
    [InlineData("machine")]
    [InlineData("assignee")]
    [InlineData("provider")]
    [InlineData("boot")]
    [InlineData("deadline")]
    [InlineData("result")]
    [InlineData("binding")]
    [InlineData("outbox")]
    [InlineData("reconciliation")]
    [InlineData("retirement")]
    [InlineData("admitted")]
    [InlineData("submitted")]
    [InlineData("started")]
    [InlineData("uncertain")]
    [InlineData("accepted")]
    public void RejectsContradictoryLegacyQueuedProjection(string contradiction) {
        var state = CreateState();
        var engine = new CoordinationEngine(state, Now);
        var work = Queue(engine, state);
        var binding = new CoordinationSessionBinding("obsolete", 0, "old-grant", "helper-worker", "helper-host",
            "provider-a", "helper-session", "helper-workspace", Candidate);
        state.Work["obsolete"] = contradiction switch {
            "grant" => work with { GrantId = "old-grant" },
            "execution" => work with { ExecutionId = "old-execution" },
            "machine" => work with { Machine = "helper-host" },
            "assignee" => work with { Assignee = "helper-worker" },
            "provider" => work with { Provider = "provider-a" },
            "boot" => work with { BootId = "boot" },
            "deadline" => work with { RenewByUtc = Now },
            "result" => work with { ResultRef = "old-result" },
            _ => work
        };
        if (contradiction == "binding") state.Bindings.Add("obsolete", binding);
        if (contradiction == "outbox") state.ResultOutbox.Add("obsolete", new("obsolete", 0, "old-result", Candidate, work.Request.ReturnTo!));
        if (contradiction == "reconciliation") state.Reconciliations.Add("obsolete",
            new("obsolete", 0, "old-grant", "boot", "next-boot", Now, "never-created", "exclusion", "old-result", true));
        if (contradiction == "retirement") state.Retirements.Add("obsolete", new(binding, "old-result", []));
        if (contradiction is "admitted" or "submitted" or "started" or "uncertain" or "accepted")
            state.Work["obsolete"].Stages!.Add(contradiction, Now);
        var before = JsonSerializer.Serialize(state);
        Assert.Equal("cancel-requires-unadmitted-request", Assert.Throws<CoordinationConflictException>(() =>
            engine.Cancel("obsolete", Cancellation, Requester)).Message);
        Assert.Equal(before, JsonSerializer.Serialize(state));
    }

    [Fact]
    public void CancellationSchedulesNextReadyWorkWithoutSatisfyingSuccessDependencies() {
        var state = CreateState();
        var engine = new CoordinationEngine(state, Now);
        Queue(engine, state);
        var next = Help("next") with { ReadOnly = true };
        engine.Submit(next, Requester);
        engine.Submit(next with { Id = "dependent", Dependencies = ["obsolete"] }, Requester);
        state.Policy = state.Policy with { Enabled = true };
        engine.Cancel("obsolete", Cancellation, Requester);
        Assert.Equal("admitted", state.Work["next"].State);
        Assert.Equal("queued", state.Work["dependent"].State);
        Assert.Equal("dependency-not-accepted", state.Work["dependent"].Reason);
    }

    [Fact]
    public void ParentRetirementResolvesOnlyExplicitAcceptedUnadmittedCancellation() {
        var state = CreateState();
        var engine = new CoordinationEngine(state, Now);
        var parent = engine.Submit(Help(), Requester);
        var binding = Start(engine, state, parent);
        var childCaller = new CoordinationPrincipal("helper-client", "client", "unused", ["sample"], "helper-host", "helper-worker");
        var child = engine.Submit(Help("review") with { Kind = "review", Capability = "review", ReadOnly = true,
            RequiredProvider = "provider-b", ReturnTo = new("helper-worker", "helper-host", "provider-a", "helper-session", Candidate, "help", 1) }, childCaller);
        Assert.Equal("queued", child.State);
        engine.Receipt("help", "terminal", Execution(parent, "answer"), HelperHost);
        Assert.Equal("retirement-descendants-unresolved", Assert.Throws<CoordinationConflictException>(() =>
            engine.Retire(new(binding, "descendants-resolved", "descendants"), HelperHost)).Message);
        var cancelled = engine.Cancel("review", Cancellation, childCaller);
        foreach (var stage in new[] { "cancelled", "cancellation-accepted", "accepted" }) {
            var stages = new Dictionary<string, DateTimeOffset>(cancelled.Stages!);
            stages.Remove(stage);
            state.Work["review"] = cancelled with { Stages = stages };
            Assert.Equal("retirement-descendants-unresolved", Assert.Throws<CoordinationConflictException>(() =>
                engine.Retire(new(binding, "descendants-resolved", "descendants"), HelperHost)).Message);
        }
        state.Work["review"] = cancelled;
        Clean(engine, binding);
        Assert.NotNull(state.Retirements["help"].CompletedAtUtc);
        Assert.False(state.ResultOutbox.ContainsKey("review"));
        Assert.False(state.Retirements.ContainsKey("review"));
        Assert.Null(state.ResultOutbox["help"].ConsumedAtUtc);
    }
}
