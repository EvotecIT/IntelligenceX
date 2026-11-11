using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Coordination;
using IntelligenceX.Ledger.Hosting;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class CoordinationHttpTests {
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task CancellationRoundTripsThroughRealHttpAndTypedClientWithExplicitGeneration() {
        var clientToken = new string('c', 32);
        await WithService(new() {
            InitialPolicy = new(1, [new("synthetic-machine", 1, 1, 4, 4096, ["reservation"])]),
            Principals = [new("requester", "client", clientToken, ["sample"])]
        }, async (endpoint, settings, token) => {
            using var client = Client(endpoint, clientToken);
            var goal = await client.SetGoalAsync(new(new("synthetic-goal", "", 0, false, false, "authorization", ["accepted"]), 0), token);
            var request = new CoordinationRequest("obsolete", goal.Id, "build", "reservation", "synthetic-machine", null,
                new(BuildSlots: 1), new("sample", "feature/sample", new('a', 40), null, "checkpoint", "validate"),
                [], goal.ControlRevision, "authorization");
            Assert.Equal("queued", (await client.SubmitAsync(request, true, token)).State);
            using var http = new HttpClient { BaseAddress = new(endpoint) };
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.WriteToken);
            using (var denied = await http.PostAsJsonAsync("api/coordination/v1/requests/obsolete/cancel",
                new CoordinationCancellation(0, "obsolete-source"), Json, token))
                Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", clientToken);
            foreach (var body in new[] { "{\"reasonRef\":\"obsolete-source\"}",
                "{\"expectedGeneration\":0}", "{\"expectedGeneration\":0,\"reasonRef\":\"obsolete-source\",\"extra\":true}" }) {
                using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
                using var malformed = await http.PostAsync("api/coordination/v1/requests/obsolete/cancel", content, token);
                Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
            }
            var command = new CoordinationCancellation(0, "obsolete-source");
            var cancelled = await client.CancelAsync("obsolete", command, token);
            Assert.Equal("cancelled", cancelled.State);
            Assert.Equal(command.ReasonRef, cancelled.Reason);
            Assert.Equal(JsonSerializer.Serialize(request, Json), JsonSerializer.Serialize(cancelled.Request, Json));
            using (var replay = await http.PostAsJsonAsync("api/coordination/v1/requests/obsolete/cancel", command, Json, token)) {
                Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
                var json = await replay.Content.ReadFromJsonAsync<JsonElement>(Json, token);
                Assert.Equal(0, json.GetProperty("generation").GetInt64());
                Assert.Equal("obsolete-source", json.GetProperty("reason").GetString());
                Assert.True(json.GetProperty("stages").TryGetProperty("cancellation-accepted", out _));
                Assert.Equal(JsonValueKind.Null, json.GetProperty("resultRef").ValueKind);
            }
            var view = await client.RequestAsync("obsolete", token);
            Assert.Null(view.Attempt);
            Assert.Null(view.Result);
            Assert.Null(view.Retirement);
            Assert.Equal("cancelled", view.Work.State);
            using var conflict = await http.PostAsJsonAsync("api/coordination/v1/requests/obsolete/cancel",
                command with { ReasonRef = "changed-reason" }, Json, token);
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
            var error = await conflict.Content.ReadFromJsonAsync<JsonElement>(Json, token);
            Assert.Equal("cancellation-receipt-conflict", error.GetProperty("error").GetString());
        });
    }

    [Fact]
    public async Task RealApiSeparatesCredentialsAndPreviewsWithoutAdmittingReservations() {
        var clientToken = new string('c', 32); var hostToken = new string('h', 32);
        await WithService(new() {
            InitialPolicy = new(1, [new("synthetic-machine", 2, 1, 4, 4096, ["reservation"])]),
            Principals = [new("requester", "client", clientToken, ["sample"]), new("observer", "host", hostToken, [], "synthetic-machine")]
        }, async (endpoint, settings, token) => {
            using var http = new HttpClient { BaseAddress = new(endpoint) };
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.WriteToken);
            using (var denied = await http.GetAsync("api/coordination/v1/requests", token))
                Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
            using var client = Client(endpoint, clientToken);
            var goal = await client.SetGoalAsync(new(new("synthetic-goal", "", 0, false, false, "authorization", ["accepted-result"]), 0), token);
            Assert.Equal(1, goal.Revision);
            using var observer = Client(endpoint, hostToken);
            await observer.HostAsync(new("synthetic-machine", "synthetic-boot", DateTimeOffset.UtcNow, ["reservation"]), token);
            var request = new CoordinationRequest("synthetic-request", goal.Id, "build", "reservation", null, null,
                new(BuildSlots: 1, CpuUnits: 4), new("sample", "feature/sample", new string('a', 40), null, "checkpoint", "existing-tools"),
                [], goal.Revision, "authorization");
            Assert.Equal("admitted", (await client.SubmitAsync(request, false, token)).State);
            Assert.Empty((await client.CoordinationStatusAsync("sample", token)).Counts);
            var admitted = await client.SubmitAsync(request, true, token);
            Assert.Equal("admitted", admitted.State);
            Assert.Equal(admitted.GrantId, (await client.SubmitAsync(request, true, token)).GrantId);
            Assert.Equal(1, (await client.CoordinationStatusAsync("sample", token)).Counts["admitted"]);
            Assert.Null(admitted.ExecutionId);
            Assert.False(admitted.Stages!.ContainsKey("started"));
            using (var denied = await http.PostAsJsonAsync("api/coordination/v1/requests", request, Json, token))
                Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", clientToken);
            using (var denied = await http.GetAsync("api/coordination/v1/summary?project=other-project", token))
                Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode);
            using (var denied = await http.PostAsJsonAsync("api/coordination/v1/policy", new CoordinationPolicyCommand(
                new(2, [new("synthetic-machine", 100, 100, 100, 4096, ["reservation"])]), 1), Json, token))
                Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode);
        });
    }

    [Fact]
    public async Task CrossHostHelpReturnsToItsRequesterAndRetiresIndependentlyWithoutAckTurn() {
        var requesterToken = new string('c', 32); var sourceToken = new string('s', 32);
        var targetToken = new string('t', 32); var otherToken = new string('o', 32);
        await WithService(new() {
            InitialPolicy = new(1, [
                new("source-host", 2, 1, 4, 4096, ["review"]),
                new("target-host", 2, 1, 4, 4096, ["platform-help"], AllowedCreateProviders: ["provider-a"]) ]),
            Principals = [
                new("requester", "client", requesterToken, ["sample"], "source-host", "requester-worker"),
                new("source", "host", sourceToken, [], "source-host"),
                new("target", "host", targetToken, [], "target-host"),
                new("operator", "operator", new('p', 32), ["sample"]),
                new("other-requester", "client", otherToken, ["sample"], "source-host", "other-worker") ]
        }, async (endpoint, _, token) => {
            using var requester = Client(endpoint, requesterToken);
            using var source = Client(endpoint, sourceToken);
            using var target = Client(endpoint, targetToken);
            using var other = Client(endpoint, otherToken);
            using var control = Client(endpoint, new('p', 32));
            var candidate = new WorkCheckpoint("sample", "feature/sample", new('a', 40), null, "checkpoint", "validate");
            var goal = await requester.SetGoalAsync(new(new("task", "", 0, false, false, "authorization", ["result", "retirement"]), 0), token);
            var now = DateTimeOffset.UtcNow;
            await source.HostAsync(new("source-host", "source-boot", now, ["review"]), token);
            await target.HostAsync(new("target-host", "target-boot", now, ["platform-help"]), token);
            await source.WorkerAsync(new("requester-worker", "source-host", "provider-a", true, null, now,
                "requester-session", "requester-workspace", candidate, "source-boot"), token);
            await target.WorkerAsync(new("helper-worker", "target-host", "provider-a", true, null, now,
                "helper-session", "helper-workspace", candidate, "target-boot", TemporaryWorker: true), token);
            var request = new CoordinationRequest("help", goal.Id, "help", "platform-help", null, null,
                new(AgentSlots: 1, CpuUnits: 2), candidate, [], goal.Revision, "authorization", "provider-a", false,
                new("requester-worker", "source-host", "provider-a", "requester-session", candidate));
            var work = await requester.SubmitAsync(request, true, token);
            Assert.Equal("target-host", work.Machine);
            Assert.Equal("helper-worker", work.Assignee);
            var binding = new CoordinationSessionBinding(work.Request.Id, work.Generation, work.GrantId!, work.Assignee!,
                work.Machine!, "provider-a", "helper-session", "helper-workspace", candidate);
            Assert.Equal(binding, await target.BindSessionAsync(binding, token));
            var execution = new ExecutionReceipt(work.GrantId!, work.Generation, "target-boot", "helper-execution");
            await target.ReceiptAsync("help", "submitted", execution, token);
            await target.ReceiptAsync("help", "started", execution, token);
            const string answer = "The platform check passed. The sample command preserves its expected output.";
            var upload = new CoordinationArtifactCommand(work.Generation, work.GrantId!, "target-boot", answer);
            var artifact = await target.PublishResultAsync("help", upload, token);
            Assert.Equal(answer, artifact.Content);
            Assert.Equal(artifact, await target.PublishResultAsync("help", upload, token));
            await Assert.ThrowsAsync<HttpRequestException>(() => source.PublishResultAsync("help", upload, token));
            await Assert.ThrowsAsync<HttpRequestException>(() => target.PublishResultAsync("help", upload with { Generation = work.Generation + 1 }, token));
            await Assert.ThrowsAsync<HttpRequestException>(() => target.PublishResultAsync("help", upload with { Content = "Changed answer" }, token));
            await target.ReceiptAsync("help", "terminal", execution with {
                ResultRef = artifact.ResultRef, Succeeded = true, ExclusionVerified = true }, token);

            Assert.Equal(artifact.ResultRef, Assert.Single((await source.ResultsAsync(token)).Items).ResultRef);
            Assert.Equal(artifact, await requester.ResultContentAsync("help", token));
            Assert.Equal(artifact, await source.ResultContentAsync("help", token));
            await Assert.ThrowsAsync<HttpRequestException>(() => other.ResultContentAsync("help", token));
            Assert.Empty((await source.AssignedAsync(token)).Items);
            Assert.Empty((await other.ResultsAsync(token)).Items);
            var returned = await requester.RequestAsync("help", token);
            Assert.Equal("succeeded", returned.Work.State);
            Assert.Equal("intent", returned.Result!.State);
            Assert.NotNull(returned.Retirement);
            var paused = await control.SetGoalAsync(new(goal with { Paused = true }, goal.Revision), token);
            var resumed = await control.SetGoalAsync(new(paused with { Paused = false }, paused.Revision), token);
            await Assert.ThrowsAsync<HttpRequestException>(() => source.BeginResultAsync("help", new(work.Generation, "stale-control-attempt"), token));
            var authorization = new CoordinationReauthorization("help", work.Generation, goal.ControlRevision, resumed.ControlRevision, "explicit-resume");
            await Assert.ThrowsAsync<HttpRequestException>(() => requester.ReauthorizeAsync(authorization, token));
            await control.ReauthorizeAsync(authorization, token);
            goal = resumed;
            await source.BeginResultAsync("help", new(work.Generation, "return-attempt"), token);
            await source.ResultReceiptAsync("help", new(work.Generation,
                new("return-attempt", "submitted", "native-return-receipt")), token);
            Assert.Equal("submitted", (await requester.RequestAsync("help", token)).Result!.State);
            Assert.Null((await requester.RequestAsync("help", token)).Result!.ConsumedAtUtc);

            foreach (var stage in new[] { "source-preserved", "descendants-resolved", "processes-released" })
                await target.RetireAsync(new(binding, stage, "proof-" + stage, Dirty: false, Unpublished: false), token);
            await target.RetireAsync(new(binding, "waiting", "protected-checkout-proof", WaitingOn: "protected-primary"), token);
            Assert.Equal("protected-primary", (await requester.RequestAsync("help", token)).Retirement!.WaitingOn);
            Assert.Equal(0, (await requester.CoordinationStatusAsync("sample", token)).Machines.Single(m => m.Id == "target-host").Used.AgentSlots);
            await target.RetireAsync(new(binding, "resume", "workspace-release-event", WaitingOn: "protected-primary"), token);
            foreach (var stage in new[] { "archived", "workspace-released", "verified" })
                await target.RetireAsync(new(binding, stage, "proof-" + stage, Dirty: false, Unpublished: false), token);
            Assert.Empty((await target.RetirementsAsync(token)).Items);
            Assert.NotNull((await requester.RequestAsync("help", token)).Retirement!.CompletedAtUtc);
            Assert.Null((await requester.RequestAsync("help", token)).Result!.ConsumedAtUtc);

            var consumed = await requester.ConsumeResultAsync("help", new(work.Generation, artifact.ResultRef, candidate), token);
            Assert.NotNull(consumed.ConsumedAtUtc);
            Assert.Empty((await source.ResultsAsync(token)).Items);
            Assert.True((await requester.SetGoalAsync(new(goal with { ResultRef = artifact.ResultRef, Accepted = true }, goal.Revision), token)).Accepted);

            // A new creation grant survives a reboot as occupied until this exact
            // authenticated exclusion proves no native helper was created.
            await target.HostAsync(new("target-host", "target-boot", DateTimeOffset.UtcNow, ["platform-help"], CreatableProviders: ["provider-a"]), token);
            var recoveryGoal = await requester.SetGoalAsync(new(new("recovery-task", "", 0, false, false, "authorization", ["reconcile"]), 0), token);
            var pending = await requester.SubmitAsync(request with { Id = "creation-recovery", GoalId = recoveryGoal.Id, ReadOnly = true }, true, token);
            Assert.Equal("worker-creation-required", pending.Reason);
            Assert.Null(pending.Assignee);
            await target.HostAsync(new("target-host", "new-boot", DateTimeOffset.UtcNow, ["platform-help"], CreatableProviders: ["provider-a"]), token);
            Assert.Equal("reconcile", (await requester.RequestAsync(pending.Request.Id, token)).Work.State);
            var failureAnswer = await target.PublishResultAsync(pending.Request.Id,
                new(pending.Generation, pending.GrantId!, "new-boot", "No native helper was created; the old reservation was reconciled after reboot."), token);
            var proof = new CoordinationReconciliation(pending.Request.Id, pending.Generation, pending.GrantId!,
                "target-boot", "new-boot", DateTimeOffset.UtcNow, "never-created", "qualified-negative-observation", failureAnswer.ResultRef, true);
            await Assert.ThrowsAsync<HttpRequestException>(() => source.ReconcileAsync(proof, token));
            await Assert.ThrowsAsync<HttpRequestException>(() => target.ReconcileAsync(proof with { Generation = pending.Generation + 1 }, token));
            Assert.Equal("cancelled", (await target.ReconcileAsync(proof, token)).State);
            Assert.Equal(failureAnswer, await requester.ResultContentAsync(pending.Request.Id, token));
            Assert.Equal(0, (await requester.CoordinationStatusAsync("sample", token)).Machines.Single(m => m.Id == "target-host").Used.AgentSlots);

            await source.WorkerAsync(new("other-worker", "source-host", "provider-a", true, null, DateTimeOffset.UtcNow,
                "other-session", "other-workspace", candidate, "source-boot"), token);
            var isolatedGoal = await other.SetGoalAsync(new(new("isolated-task", "", 0, false, false, "authorization", ["own-answer"]), 0), token);
            await target.WorkerAsync(new("isolated-worker", "target-host", "provider-a", true, null, DateTimeOffset.UtcNow,
                "isolated-session", "isolated-workspace", candidate, "new-boot", TemporaryWorker: true), token);
            var isolated = await other.SubmitAsync(request with { Id = "isolated-help", GoalId = isolatedGoal.Id, ReadOnly = true,
                ReturnTo = new("other-worker", "source-host", "provider-a", "other-session", candidate) }, true, token);
            var isolatedBinding = new CoordinationSessionBinding(isolated.Request.Id, isolated.Generation, isolated.GrantId!, isolated.Assignee!,
                "target-host", "provider-a", "isolated-session", "isolated-workspace", candidate);
            await target.BindSessionAsync(isolatedBinding, token);
            var isolatedExecution = new ExecutionReceipt(isolated.GrantId!, isolated.Generation, "new-boot", "isolated-execution");
            await target.ReceiptAsync(isolated.Request.Id, "submitted", isolatedExecution, token);
            await target.ReceiptAsync(isolated.Request.Id, "started", isolatedExecution, token);
            await Assert.ThrowsAsync<HttpRequestException>(() => target.ReceiptAsync(isolated.Request.Id, "terminal",
                isolatedExecution with { ResultRef = artifact.ResultRef, Succeeded = true, ExclusionVerified = true }, token));
            Assert.Equal("started", (await other.RequestAsync(isolated.Request.Id, token)).Work.State);
            await Assert.ThrowsAsync<HttpRequestException>(() => other.ResultContentAsync(isolated.Request.Id, token));
            await Assert.ThrowsAsync<HttpRequestException>(() => other.ResultContentAsync("help", token));
            var ownAnswer = await target.PublishResultAsync(isolated.Request.Id,
                new(isolated.Generation, isolated.GrantId!, "new-boot", "This answer belongs to the isolated request."), token);
            await target.ReceiptAsync(isolated.Request.Id, "terminal", isolatedExecution with {
                ResultRef = ownAnswer.ResultRef, Succeeded = true, ExclusionVerified = true }, token);
            Assert.Equal(ownAnswer, await other.ResultContentAsync(isolated.Request.Id, token));
        });
    }

    [Fact]
    public async Task DiscoveryContinuesBeyondSixteenHeldResultsRetirementsAndAssignments() {
        await WithService(new() {
            InitialPolicy = new(1, [new("source", 1, 1, 4, 4096, ["source"]), new("target", 32, 32, 4, 4096, ["help"])]),
            Principals = [new("client", "client", new('c', 32), ["sample"], "source", "root-worker"),
                new("source-host", "host", new('s', 32), [], "source"), new("target-host", "host", new('t', 32), [], "target")]
        }, async (endpoint, _, token) => {
            using var client = Client(endpoint, new('c', 32));
            using var source = Client(endpoint, new('s', 32));
            using var target = Client(endpoint, new('t', 32));
            var candidate = new WorkCheckpoint("sample", "feature/sample", new('a', 40), null, "checkpoint", "validate");
            var now = DateTimeOffset.UtcNow;
            await source.HostAsync(new("source", "source-boot", now, ["source"]), token);
            await target.HostAsync(new("target", "target-boot", now, ["help"]), token);
            await source.WorkerAsync(new("root-worker", "source", "provider", true, null, now,
                "root-session", "root-workspace", candidate, "source-boot"), token);
            var goal = await client.SetGoalAsync(new(new("page-task", "", 0, false, false, "authorization", ["answers"]), 0), token);
            var back = new CoordinationReturnBinding("root-worker", "source", "provider", "root-session", candidate);
            for (var index = 0; index < 17; index++) {
                var id = "help-" + index;
                await target.WorkerAsync(new("worker-" + index, "target", "provider", true, null, DateTimeOffset.UtcNow,
                    "session-" + index, "workspace-" + index, candidate, "target-boot", TemporaryWorker: true), token);
                var work = await client.SubmitAsync(new(id, goal.Id, "help", "help", null, null, new(AgentSlots: 1),
                    candidate, [], goal.ControlRevision, "authorization", "provider", true, back), true, token);
                var binding = new CoordinationSessionBinding(id, work.Generation, work.GrantId!, work.Assignee!, "target", "provider",
                    "session-" + index, "workspace-" + index, candidate);
                await target.BindSessionAsync(binding, token);
                var execution = new ExecutionReceipt(work.GrantId!, work.Generation, "target-boot", "execution-" + index);
                await target.ReceiptAsync(id, "submitted", execution, token);
                await target.ReceiptAsync(id, "started", execution, token);
                var answer = await target.PublishResultAsync(id, new(work.Generation, work.GrantId!, "target-boot", "Answer " + index), token);
                await target.ReceiptAsync(id, "terminal", execution with { ResultRef = answer.ResultRef, Succeeded = true, ExclusionVerified = true }, token);
                if (index < 16) {
                    await source.ResultReceiptAsync(id, new(work.Generation, new("pre-send-" + index, "held", "recipient-held")), token);
                    await target.RetireAsync(new(binding, "waiting", "protected-workspace", WaitingOn: "protected-primary"), token);
                }
            }
            var results = await source.ResultsAsync(token);
            Assert.Equal(16, results.Items.Length); Assert.NotNull(results.NextOrder);
            Assert.All(results.Items, item => Assert.Equal("held", item.State));
            var resultEnd = await source.ResultsAsync(token, results.NextOrder!.Value);
            Assert.Equal("help-16", Assert.Single(resultEnd.Items).RequestId); Assert.Null(resultEnd.NextOrder);
            var retirement = await target.RetirementsAsync(token);
            Assert.Equal(16, retirement.Items.Length); Assert.NotNull(retirement.NextOrder);
            Assert.All(retirement.Items, item => Assert.Equal("protected-primary", item.WaitingOn));
            var retirementEnd = await target.RetirementsAsync(token, retirement.NextOrder!.Value);
            Assert.Equal("help-16", Assert.Single(retirementEnd.Items).Binding.RequestId); Assert.Null(retirementEnd.NextOrder);
            for (var index = 0; index < 17; index++)
                await client.SubmitAsync(new("reservation-" + index, goal.Id, "build", "help", "target", null,
                    new(BuildSlots: 1), candidate, [], goal.ControlRevision, "authorization", ReadOnly: true), true, token);
            var assigned = await target.AssignedAsync(token);
            Assert.Equal(16, assigned.Items.Length); Assert.NotNull(assigned.NextOrder);
            var assignedEnd = await target.AssignedAsync(token, assigned.NextOrder!.Value);
            Assert.Equal("reservation-16", Assert.Single(assignedEnd.Items).Request.Id); Assert.Null(assignedEnd.NextOrder);
        });
    }

    private static LedgerReadClient Client(string endpoint, string token) => LedgerReadClient.ForCoordination(new(endpoint, token));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RetirementPagesHoldUnsafeJobsAndContinueSafeCleanup(bool missingWorker) {
        CoordinationSessionBinding? retainedBinding = null;
        long boundaryOrder = 0;
        await WithService(new() {
            InitialPolicy = CoordinationLifecycleTests.Policy,
            Principals = [CoordinationLifecycleTests.HelperHost with { Token = new('h', 32) }]
        }, async (endpoint, settings, token) => {
            using var http = new HttpClient { BaseAddress = new(endpoint) };
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new('h', 32));
            using var responsePage = await http.GetAsync("api/coordination/v1/retirements", token);
            Assert.Equal(HttpStatusCode.OK, responsePage.StatusCode);
            var pageJson = await responsePage.Content.ReadFromJsonAsync<JsonElement>(Json, token);
            Assert.Equal(boundaryOrder, pageJson.GetProperty("nextOrder").GetInt64());
            var holds = pageJson.GetProperty("holds").EnumerateArray().ToArray();
            Assert.Equal(new[] { "help", "safe-14" }, holds.Select(hold => hold.GetProperty("requestId").GetString()));
            Assert.All(holds, hold => {
                Assert.Equal("binding-worker-not-temporary", hold.GetProperty("reason").GetString());
                Assert.Equal(2, hold.EnumerateObject().Count());
            });
            using var client = Client(endpoint, new('h', 32));
            var page = await client.RetirementsAsync(token);
            Assert.Equal(Enumerable.Range(0, 14).Select(index => "safe-" + index), page.Items.Select(item => item.Binding.RequestId));
            Assert.Equal(new[] { new CoordinationRetirementHold("help", "binding-worker-not-temporary"),
                new CoordinationRetirementHold("safe-14", "binding-worker-not-temporary") }, page.Holds);
            Assert.Equal(boundaryOrder, page.NextOrder);
            var end = await client.RetirementsAsync(token, page.NextOrder!.Value);
            var safe = Assert.Single(end.Items);
            Assert.Equal("safe-15", safe.Binding.RequestId);
            Assert.Empty(end.Holds);
            Assert.Null(end.NextOrder);
            await client.RetireAsync(new(safe.Binding, "source-preserved", "safe-source-proof", Dirty: false, Unpublished: false), token);
            foreach (var route in new[] { "requests/help/retirement", "requests/help", "requests/safe-14/retirement", "requests/safe-14" }) {
                using var response = await http.GetAsync("api/coordination/v1/" + route, token);
                Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
                var error = await response.Content.ReadFromJsonAsync<JsonElement>(Json, token);
                Assert.Equal("binding-worker-not-temporary", error.GetProperty("error").GetString());
                Assert.False(error.TryGetProperty("binding", out _));
                Assert.False(error.TryGetProperty("items", out _));
            }
            using (var denied = await http.PostAsJsonAsync("api/coordination/v1/requests/help/retirement",
                new CoordinationRetirementReceipt(retainedBinding!, "archived", "unsafe-archive"), Json, token))
                Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode);
            // The service retains its writer lease. Inspect its committed projection through the
            // existing read-only database boundary, without opening another coordination writer.
            using var database = new DBAClientX.SQLite();
            await using var session = await database.OpenSessionAsync(Path.Combine(settings.DataDirectory, "coordination.sqlite"), token);
            var value = (string)(await session.ExecuteScalarAsync("SELECT json FROM coordination_state WHERE id=1", cancellationToken: token))!;
            var state = JsonSerializer.Deserialize<CoordinationState>(value, Json)!;
            Assert.Equal(retainedBinding, state.Bindings["help"]);
            Assert.Equal(retainedBinding, state.Retirements["help"].Binding);
            Assert.Empty(state.Retirements["help"].Evidence);
            Assert.Null(state.Retirements["help"].CompletedAtUtc);
            Assert.Empty(state.Retirements["safe-14"].Evidence);
            Assert.Null(state.Retirements["safe-14"].CompletedAtUtc);
            Assert.Equal("safe-source-proof", state.Retirements["safe-15"].Evidence["source-preserved"]);
        }, (engine, state) => {
            CoordinationLifecycleTests.Initialize(engine, state);
            retainedBinding = CoordinationLifecycleTests.Finish(engine, state);
            for (var index = 0; index < 16; index++) {
                var worker = state.Workers[retainedBinding.WorkerId] with {
                    Id = "safe-worker-" + index, SessionId = "safe-session-" + index, WorkspaceId = "safe-workspace-" + index
                };
                engine.ObserveWorker(worker, CoordinationLifecycleTests.HelperHost);
                var work = engine.Submit(CoordinationLifecycleTests.Help("safe-" + index) with {
                    WorkerId = worker.Id, Resources = new(AgentSlots: 1), ReadOnly = true
                }, CoordinationLifecycleTests.Requester);
                CoordinationLifecycleTests.Start(engine, state, work);
                engine.Receipt(work.Request.Id, "terminal", CoordinationLifecycleTests.Execution(work, "result-" + index),
                    CoordinationLifecycleTests.HelperHost);
            }
            boundaryOrder = state.Work["safe-14"].Order;
            // Retain historical jobs whose enrollment is personal or absent after reload.
            foreach (var workerId in new[] { retainedBinding.WorkerId, state.Bindings["safe-14"].WorkerId }) {
                if (missingWorker) state.Workers.Remove(workerId);
                else state.Workers[workerId] = state.Workers[workerId] with { TemporaryWorker = false };
            }
        });
    }

    private static async Task WithService(CoordinationConfiguration enrollment,
        Func<string, LedgerSettings, CancellationToken, Task> run,
        Action<CoordinationEngine, CoordinationState>? initialize = null) {
        var directory = LedgerTestPaths.NewDirectory("coordination-http");
        Directory.CreateDirectory(directory);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var endpoint = $"http://127.0.0.1:{port}/";
        var enrollmentPath = Path.Combine(directory, "enrollment.json");
        await File.WriteAllTextAsync(enrollmentPath, JsonSerializer.Serialize(enrollment, Json));
        var settings = new LedgerSettings { DataDirectory = directory, Port = port,
            ReadToken = new('r', 32), WriteToken = new('w', 32), CoordinationConfigPath = enrollmentPath,
            ExpectedReporters = new() { ["synthetic-machine"] = ["synthetic-reporter"] } };
        var config = Path.Combine(directory, "settings.json");
        await File.WriteAllTextAsync(config, JsonSerializer.Serialize(settings, Json));
        if (initialize is not null) {
            using var store = new CoordinationStore(Path.Combine(directory, "coordination.sqlite"), enrollment.InitialPolicy);
            await store.InitializeAsync();
            await store.ExecuteAsync((engine, state) => { initialize(engine, state); return true; }, CoordinationLifecycleTests.Now);
        }
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { typeof(Program).Assembly.Location, "serve", "--config", config }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        try {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var readiness = new HttpClient { BaseAddress = new(endpoint) };
            readiness.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ReadToken);
            while (true) {
                if (process.HasExited) throw new InvalidOperationException(await stderr);
                try {
                    using var response = await readiness.GetAsync("api/ledger/v1/machines", deadline.Token);
                    response.EnsureSuccessStatusCode();
                    break;
                } catch (HttpRequestException) { await Task.Delay(50, deadline.Token); }
            }
            await run(endpoint, settings, deadline.Token);
        } finally {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(); await Task.WhenAll(stdout, stderr);
            Directory.Delete(directory, true);
        }
    }
}
