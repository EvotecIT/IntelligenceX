using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Coordination;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class CoordinationRequesterHttpTests {
    [Fact]
    public async Task HostAcknowledgementReturnsTheActiveGrantWindowThroughTheTypedClient() {
        var hostToken = new string('h', 32);
        await CoordinationHttpTests.WithService(new() {
            InitialPolicy = new(1, [new("synthetic-machine", 2, 1, 4, 4096, ["review"])], GrantSeconds: 37),
            Principals = [new("host", "host", hostToken, ["sample"], "synthetic-machine")]
        }, async (endpoint, _, token) => {
            using var host = LedgerReadClient.ForCoordination(new(endpoint, hostToken));
            var acknowledged = await host.HostAsync(new("synthetic-machine", "boot", DateTimeOffset.UtcNow, ["review"]), token);
            Assert.True(acknowledged.Acknowledged);
            Assert.Equal(37, acknowledged.GrantSeconds);
            Assert.Equal(HttpStatusCode.Conflict, (await Assert.ThrowsAsync<HttpRequestException>(() =>
                host.PolicyAsync(token))).StatusCode);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ScopeRemovalPreservesOnlyOriginalRequestersDurableEvidenceAndConsumption(bool removeAllScopes) {
        var hostToken = new string('h', 32);
        var requesterToken = new string('d', 64);
        var otherToken = new string('o', 32);
        await CoordinationHttpTests.WithService(new() {
            InitialPolicy = new(1, [new("synthetic-machine", 2, 2, 8, 4096, ["review"])]),
            Principals = [new("host", "host", hostToken, ["sample", "next-project"], "synthetic-machine", AllowRequesterEnrollment: true),
                new("other-requester", "client", otherToken, ["sample"])]
        }, async (endpoint, _, token) => {
            using var host = LedgerReadClient.ForCoordination(new(endpoint, hostToken));
            using var requester = LedgerReadClient.ForCoordination(new(endpoint, requesterToken));
            using var other = LedgerReadClient.ForCoordination(new(endpoint, otherToken));
            var now = DateTimeOffset.UtcNow;
            var candidate = new WorkCheckpoint("sample", "feature/sample", new('a', 40), null, "checkpoint", "validate");
            await host.HostAsync(new("synthetic-machine", "boot", now, ["review"]), token);
            await host.WorkerAsync(new("personal", "synthetic-machine", "provider", true, null, now,
                "session", "workspace", candidate, "boot"), token);
            await host.WorkerAsync(new("helper", "synthetic-machine", "provider", true, null, now,
                "helper-session", "helper-workspace", candidate, "boot", TemporaryWorker: true), token);
            var enrollment = new RequesterEnrollment("requester", "personal", "native-goal", "native-source", "authorization",
                [new("sample", candidate.Branch)], Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(requesterToken))), "boot", now, true);
            await host.EnrollRequesterAsync(enrollment, token);
            var goal = await requester.SetGoalAsync(new(new(enrollment.GoalId, "", 0, false, false, "authorization", ["result"]), 0), token);
            var request = new CoordinationRequest("review", goal.Id, "review", "review", "synthetic-machine", "helper",
                new(AgentSlots: 1), candidate, [], goal.ControlRevision, "authorization", ReadOnly: true,
                ReturnTo: new("personal", "synthetic-machine", "provider", "session", candidate));
            var work = await requester.SubmitAsync(request, true, token);
            Assert.Equal("admitted", work.State);
            await host.BindSessionAsync(new(request.Id, work.Generation, work.GrantId!, "helper", "synthetic-machine", "provider",
                "helper-session", "helper-workspace", candidate), token);
            var execution = new ExecutionReceipt(work.GrantId!, work.Generation, "boot", "execution");
            await host.ReceiptAsync(request.Id, "submitted", execution, token);
            await host.ReceiptAsync(request.Id, "started", execution, token);
            var artifact = await host.PublishResultAsync(request.Id, new(work.Generation, work.GrantId!, "boot", "Verified review result."), token);
            await host.ReceiptAsync(request.Id, "terminal", execution with {
                ResultRef = artifact.ResultRef, Succeeded = true, ExclusionVerified = true
            }, token);
            var foreignGoal = await other.SetGoalAsync(new(new("foreign-goal", "", 0, false, false, "authorization", ["result"]), 0), token);
            await other.SubmitAsync(new("foreign-work", foreignGoal.Id, "build", "review", "synthetic-machine", null,
                new(BuildSlots: 1), candidate, [], foreignGoal.ControlRevision, "authorization"), true, token);
            await host.EnrollRequesterAsync(enrollment with { Sources = removeAllScopes ? [] : [new("next-project", "feature/next")],
                ObservedAtUtc = DateTimeOffset.UtcNow }, token);
            Assert.Equal(goal.Id, (await requester.GoalAsync(goal.Id, token)).Id);
            Assert.Equal("succeeded", (await requester.RequestAsync(request.Id, token)).Work.State);
            Assert.Equal(artifact.ResultRef, (await requester.ResultAsync(request.Id, token)).ResultRef);
            Assert.Equal(artifact, await requester.ResultContentAsync(request.Id, token));
            Assert.Equal(request.Id, Assert.Single((await requester.ResultsAsync(token)).Items).RequestId);
            Assert.Equal(request.Id, Assert.Single((await requester.AssignedAsync(token)).Items).Request.Id);
            Assert.Equal(request.Id, Assert.Single((await requester.CoordinationStatusAsync("sample", token)).Items).Request.Id);
            Assert.Contains((await requester.ChangesAsync(0, token)).Changes, c => c.RequestId == request.Id && c.Stage == "terminal");
            Assert.Equal("project-not-authorized", (await Assert.ThrowsAsync<CoordinationCommandRejectedException>(() =>
                requester.SubmitAsync(request with { Id = "removed-source" }, true, token))).ErrorCode);
            Assert.Equal("result-recipient-held", (await Assert.ThrowsAsync<CoordinationCommandRejectedException>(() =>
                host.BeginResultAsync(request.Id, new(work.Generation, "removed-source-send"), token))).ErrorCode);
            foreach (var read in new Func<Task>[] {
                async () => { await requester.RequestAsync("foreign-work", token); },
                async () => { await requester.GoalAsync(foreignGoal.Id, token); },
                async () => { await other.ResultAsync(request.Id, token); },
                async () => { await other.ResultContentAsync(request.Id, token); }
            }) Assert.Equal(HttpStatusCode.NotFound, (await Assert.ThrowsAsync<HttpRequestException>(read)).StatusCode);
            Assert.NotNull((await requester.ConsumeResultAsync(request.Id,
                new(work.Generation, artifact.ResultRef, candidate), token)).ConsumedAtUtc);
        });
    }

    [Fact]
    public async Task ScopedHostRegistersHashOnlyClientWhoseHeldSourceRetainsHistoryAndDeniesNewDispatch() {
        var hostToken = new string('h', 32);
        var unqualifiedToken = new string('u', 32);
        var requesterToken = new string('n', 64);
        await CoordinationHttpTests.WithService(new() {
            InitialPolicy = new(1, [new("synthetic-machine", 1, 1, 4, 4096, ["reservation"]),
                new("other-machine", 1, 1, 4, 4096, ["reservation"])]),
            Principals = [new("host", "host", hostToken, ["sample"], "synthetic-machine", AllowRequesterEnrollment: true),
                new("unqualified", "host", unqualifiedToken, ["sample"], "synthetic-machine"),
                new("other-host", "host", new('o', 32), ["sample"], "other-machine")]
        }, async (endpoint, settings, token) => {
            using var host = LedgerReadClient.ForCoordination(new(endpoint, hostToken));
            using var unqualified = LedgerReadClient.ForCoordination(new(endpoint, unqualifiedToken));
            using var requester = LedgerReadClient.ForCoordination(new(endpoint, requesterToken));
            using var otherHost = LedgerReadClient.ForCoordination(new(endpoint, new('o', 32)));
            var candidate = new WorkCheckpoint("sample", "feature/sample", new('a', 40), null, "checkpoint", "validate");
            var now = DateTimeOffset.UtcNow;
            await host.HostAsync(new("synthetic-machine", "boot", now, ["reservation"]), token);
            Assert.Null((await host.WorkerStatusAsync("provider", "session", token)).Worker);
            var worker = new CoordinationWorker("personal", "synthetic-machine", "provider", false, "native-turn-held", now,
                "session", "workspace", candidate, "boot");
            var enrollment = new RequesterEnrollment("requester-" + new string('1', 64), "personal", "native-goal",
                "native-source", "authorization", [new("sample", "feature/sample")],
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(requesterToken))), "boot", now, false, worker);
            Assert.Equal("requester-enrollment-role-denied", (await Assert.ThrowsAsync<CoordinationCommandRejectedException>(
                () => unqualified.EnrollRequesterAsync(enrollment, token))).ErrorCode);
            Assert.Equal("requester-project-not-authorized", (await Assert.ThrowsAsync<CoordinationCommandRejectedException>(
                () => host.EnrollRequesterAsync(enrollment with { Sources = [new("foreign", "feature/sample")] }, token))).ErrorCode);
            var registered = await host.EnrollRequesterAsync(enrollment, token);
            Assert.Equal(enrollment.Id, registered.Id);
            Assert.Equal(enrollment.TokenSha256, registered.TokenSha256);
            Assert.Equal(worker, (await host.WorkerStatusAsync("provider", "session", token)).Worker);
            Assert.Null((await otherHost.WorkerStatusAsync("provider", "session", token)).Worker);
            Assert.Equal(enrollment.ObservedAtUtc, (await host.EnrollRequesterAsync(enrollment, token)).ObservedAtUtc);
            using var raw = new HttpClient { BaseAddress = new(endpoint) };
            raw.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", hostToken);
            var invalidBody = JsonSerializer.SerializeToNode(enrollment, LedgerReadJson.Default.RequesterEnrollment)!;
            invalidBody["machineId"] = "injected-machine";
            using (var invalid = await raw.PostAsJsonAsync("api/coordination/v1/requesters", invalidBody, token))
                Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            var goalCommand = new CoordinationGoalCommand(new(enrollment.GoalId, "", 0, false, false,
                enrollment.AuthorizationRef, ["result"]), 0);
            Assert.Equal("requester-dispatch-held", (await Assert.ThrowsAsync<CoordinationCommandRejectedException>(
                () => requester.SetGoalAsync(goalCommand, token))).ErrorCode);
            worker = worker with { Eligible = true, UnavailableReason = null, ObservedAtUtc = DateTimeOffset.UtcNow };
            enrollment = enrollment with { DispatchAllowed = true, ObservedAtUtc = DateTimeOffset.UtcNow, WorkerObservation = worker };
            await host.EnrollRequesterAsync(enrollment, token);
            var goal = await requester.SetGoalAsync(goalCommand, token);
            Assert.Equal(enrollment.Id, goal.Owner);
            Assert.Equal(HttpStatusCode.Conflict, (await Assert.ThrowsAsync<HttpRequestException>(
                () => requester.WorkerStatusAsync("provider", "session", token))).StatusCode);
            var request = new CoordinationRequest("first", goal.Id, "build", "reservation", "synthetic-machine", null,
                new(BuildSlots: 1), candidate, [], goal.ControlRevision, enrollment.AuthorizationRef);
            Assert.Equal("admitted", (await requester.SubmitAsync(request, true, token)).State);
            Assert.Equal("requester-scope-conflict", (await Assert.ThrowsAsync<CoordinationCommandRejectedException>(
                () => requester.SubmitAsync(request with { Id = "foreign-branch", Checkpoint = candidate with { Branch = "other" } }, true, token))).ErrorCode);
            Assert.Equal("requester-scope-conflict", (await Assert.ThrowsAsync<CoordinationCommandRejectedException>(
                () => requester.SetGoalAsync(new(goal with { Id = "other-goal" }, 0), token))).ErrorCode);
            Assert.Equal("requester-enrollment-role-denied", (await Assert.ThrowsAsync<CoordinationCommandRejectedException>(
                () => requester.EnrollRequesterAsync(enrollment, token))).ErrorCode);
            var heldAt = DateTimeOffset.UtcNow;
            var hold = enrollment with { DispatchAllowed = false, ObservedAtUtc = heldAt,
                WorkerObservation = worker with { Eligible = false, UnavailableReason = "native-turn-held", ObservedAtUtc = heldAt } };
            await host.EnrollRequesterAsync(hold, token);
            Assert.False((await host.WorkerStatusAsync("provider", "session", token)).Worker!.Eligible);
            var historical = await requester.RequestAsync("first", token);
            Assert.Equal("reconcile", historical.Work.State);
            Assert.Equal("requester-held", historical.Work.Reason);
            Assert.Equal("requester-dispatch-held", (await Assert.ThrowsAsync<CoordinationCommandRejectedException>(
                () => requester.SubmitAsync(request with { Id = "held" }, true, token))).ErrorCode);
            raw.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new string('z', 64));
            using (var denied = await raw.GetAsync("api/coordination/v1/requests/first", token))
                Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
            raw.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ReadToken);
            using (var denied = await raw.PostAsJsonAsync("api/coordination/v1/requesters", enrollment, token))
                Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        });
    }
}
