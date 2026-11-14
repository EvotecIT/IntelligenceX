using System.Net;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Coordination;
using Xunit;
using static IntelligenceX.Ledger.Tests.CoordinationLifecycleTests;

namespace IntelligenceX.Ledger.Tests;

public sealed class CoordinationReviewCandidateHttpTests {
    [Fact]
    public async Task PreparedSourceCannotReplaceAnAssignmentAdmittedAfterTheHostLookup() {
        var own = Requester with { Token = new('c', 32) };
        var host = ReviewHost with { Token = new('h', 32), Projects = ["sample"] };
        var reviewer = new CoordinationWorker("reviewer", "review-host", "claude", true, null,
            DateTimeOffset.UtcNow, "review-session", "review-workspace", Candidate, BootId: "boot");
        await CoordinationHttpTests.WithService(new() {
            InitialPolicy = Policy, Principals = [own, host]
        }, async (endpoint, _, token) => {
            using var reader = LedgerReadClient.ForCoordination(new(endpoint, host.Token));
            using var requester = LedgerReadClient.ForCoordination(new(endpoint, own.Token));
            Assert.Null((await reader.WorkerStatusAsync("claude", reviewer.SessionId!, token)).Assignment);
            var admitted = await requester.SubmitAsync(Help("admission-race") with {
                Kind = "review", Capability = "review", RequiredProvider = "claude", ReadOnly = true, ReuseSession = true
            }, true, token);
            Assert.Equal("admitted", admitted.State);
            var replacement = reviewer with { Candidate = Candidate with { Commit = new('b', 40) }, ObservedAtUtc = DateTimeOffset.UtcNow };
            var blocked = await Assert.ThrowsAsync<CoordinationCommandRejectedException>(() => reader.PrepareWorkerAsync(replacement, token));
            Assert.Equal("worker-assignment-active", blocked.ErrorCode);
            var retained = (await requester.RequestAsync(admitted.Request.Id, token)).Work;
            Assert.Equal("admitted", retained.State);
            Assert.Equal(admitted.GrantId, retained.GrantId);
            Assert.Equal(reviewer.Id, retained.Assignee);
            Assert.Equal(Candidate, (await reader.WorkerStatusAsync("claude", reviewer.SessionId!, token)).Worker?.Candidate);
            Assert.True((await reader.PrepareWorkerAsync(reviewer with { ObservedAtUtc = DateTimeOffset.UtcNow }, token)).Acknowledged);
            await reader.BindSessionAsync(new(admitted.Request.Id, admitted.Generation, admitted.GrantId!, reviewer.Id,
                reviewer.MachineId, reviewer.Provider, reviewer.SessionId!, reviewer.WorkspaceId!, Candidate), token);
            var result = await reader.PublishResultAsync(admitted.Request.Id,
                new(admitted.Generation, admitted.GrantId!, "boot", "Verified read-only source review."), token);
            await reader.ReceiptAsync(admitted.Request.Id, "terminal", Execution(admitted, result.ResultRef), token);
            blocked = await Assert.ThrowsAsync<CoordinationCommandRejectedException>(() => reader.PrepareWorkerAsync(
                replacement with { ObservedAtUtc = DateTimeOffset.UtcNow }, token));
            Assert.Equal("worker-assignment-active", blocked.ErrorCode);
            await requester.ConsumeResultAsync(admitted.Request.Id, new(admitted.Generation, result.ResultRef, Candidate), token);
            Assert.True((await reader.PrepareWorkerAsync(replacement with { ObservedAtUtc = DateTimeOffset.UtcNow }, token)).Acknowledged);
            Assert.Equal(replacement.Candidate, (await reader.WorkerStatusAsync("claude", reviewer.SessionId!, token)).Worker?.Candidate);
        }, (_, state) => {
            var observed = DateTimeOffset.UtcNow;
            var engine = new CoordinationEngine(state, observed);
            engine.SetGoal(new("task", "", 0, false, false, "authorization", ["result-verified"]), Requester, 0);
            foreach (var principal in new[] { SourceHost, ReviewHost })
                engine.ObserveHost(new(principal.MachineId!, "boot", observed, ["review"]), principal);
            engine.ObserveWorker(RequesterWorker with { ObservedAtUtc = observed }, SourceHost);
            engine.ObserveWorker(reviewer with { ObservedAtUtc = observed }, ReviewHost);
        });
    }

    [Fact]
    public async Task HostsDiscoverOnlyAuthorizedReadyReviewCandidatesAcrossBoundedReadOnlyPages() {
        var own = Requester with { Token = new('c', 32) };
        var host = ReviewHost with { Token = new('h', 32), Projects = ["sample"] };
        var foreign = new CoordinationPrincipal("foreign-requester", "client", new('f', 32), ["private-sample"],
            "source-host", "foreign-requester-worker");
        var otherScope = host with { Id = "other-scope-host", Token = new('s', 32), Projects = ["private-sample"] };
        var unscoped = host with { Id = "unscoped-host", Token = new('u', 32), Projects = [] };
        await CoordinationHttpTests.WithService(new() {
            InitialPolicy = Policy,
            Principals = [own, host, foreign, otherScope, unscoped]
        }, async (endpoint, settings, token) => {
            using var reader = LedgerReadClient.ForCoordination(new(endpoint, host.Token));
            using var foreignReader = LedgerReadClient.ForCoordination(new(endpoint, otherScope.Token));
            using var unscopedReader = LedgerReadClient.ForCoordination(new(endpoint, unscoped.Token));
            using var clientReader = LedgerReadClient.ForCoordination(new(endpoint, own.Token));
            using var database = new DBAClientX.SQLite();
            await using var session = await database.OpenSessionAsync(Path.Combine(settings.DataDirectory, "coordination.sqlite"), token);
            async Task<string> State() => (string)(await session.ExecuteScalarAsync(
                "SELECT json FROM coordination_state WHERE id=1", cancellationToken: token))!;
            var before = await State();
            var first = await reader.ReviewCandidatesAsync(token);
            Assert.Equal(Enumerable.Range(0, 16).Select(i => "review-" + i), first.Items.Select(w => w.Request.Id));
            Assert.Equal(first.Items[^1].Order, first.NextOrder);
            var last = await reader.ReviewCandidatesAsync(token, first.NextOrder!.Value);
            Assert.Equal("review-16", Assert.Single(last.Items).Request.Id);
            Assert.Null(last.NextOrder);
            Assert.All(first.Items.Concat(last.Items), work => {
                Assert.Equal("queued", work.State);
                Assert.Equal(0, work.Generation);
                Assert.Null(work.Assignee);
                Assert.Null(work.GrantId);
                Assert.Equal("sample", work.Request.Checkpoint.Project);
            });
            Assert.Equal("foreign-review", Assert.Single((await foreignReader.ReviewCandidatesAsync(token)).Items).Request.Id);
            Assert.Empty((await unscopedReader.ReviewCandidatesAsync(token)).Items);
            var absent = await reader.WorkerStatusAsync("claude", "absent-session", token);
            Assert.Null(absent.Worker);
            Assert.Null(absent.Assignment);
            Assert.Equal(HttpStatusCode.Conflict, (await Assert.ThrowsAsync<HttpRequestException>(() =>
                clientReader.ReviewCandidatesAsync(token))).StatusCode);
            Assert.Equal(before, await State());
        }, (engine, state) => {
            Initialize(engine, state);
            var request = Help() with { Kind = "review", Capability = "review", RequiredProvider = "claude",
                ReadOnly = true, ReuseSession = true };
            for (var i = 0; i < 17; i++) engine.Submit(request with { Id = "review-" + i }, Requester);
            engine.Submit(request with { Id = "other-machine", MachineId = "helper-host" }, Requester);
            engine.ObserveWorker(new("pinned-worker", "helper-host", "claude", false, "usage-limit", Now,
                "pinned-session", "pinned-workspace", Candidate, BootId: "boot"), HelperHost);
            engine.Submit(request with { Id = "other-worker", WorkerId = "pinned-worker" }, Requester);
            engine.Submit(request with { Id = "different-capability", Capability = "platform-help" }, Requester);
            engine.Submit(Help("ordinary-help"), Requester);
            foreach (var pause in new[] { true, false }) {
                var name = pause ? "paused" : "control-changed";
                var goal = engine.SetGoal(new(name, "", 0, false, false, name + "-authorization", ["result"]), Requester, 0);
                engine.Submit(request with { Id = name + "-review", GoalId = goal.Id,
                    AuthorizationRef = goal.AuthorizationRef, ControlRevision = goal.ControlRevision }, Requester);
                engine.SetGoal(goal with { Paused = pause, AcceptanceCriteria = ["updated-result"] }, Requester, goal.Revision);
            }
            var dependency = engine.Submit(Help("unaccepted-dependency"), Requester);
            engine.Submit(request with { Id = "waiting-dependency", Dependencies = [dependency.Request.Id] }, Requester);
            var foreignCandidate = Candidate with { Project = "private-sample" };
            engine.ObserveWorker(RequesterWorker with { Id = foreign.WorkerId!, Candidate = foreignCandidate,
                SessionId = "foreign-session", WorkspaceId = "foreign-workspace" }, SourceHost);
            var foreignGoal = engine.SetGoal(new("foreign-goal", "", 0, false, false, "foreign-authorization", ["result"]), foreign, 0);
            engine.Submit(request with { Id = "foreign-review", GoalId = foreignGoal.Id,
                AuthorizationRef = foreignGoal.AuthorizationRef, ControlRevision = foreignGoal.ControlRevision,
                Checkpoint = foreignCandidate, ReturnTo = new(foreign.WorkerId!, "source-host", "provider-a",
                    "foreign-session", foreignCandidate) }, foreign);
        });
    }
}
