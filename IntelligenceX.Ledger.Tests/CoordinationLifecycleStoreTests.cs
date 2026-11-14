using System.Text.Json;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Coordination;
using Xunit;
using static IntelligenceX.Ledger.Tests.CoordinationLifecycleTests;

namespace IntelligenceX.Ledger.Tests;

public sealed class CoordinationLifecycleStoreTests {
    [Fact]
    public async Task NamedSessionReuseSurvivesRestartAndRequiresConsumedResultBeforeNextGrant() {
        var directory = LedgerTestPaths.NewDirectory("named-session-reuse");
        var path = Path.Combine(directory, "coordination.sqlite");
        var personal = new CoordinationWorker("personal-worker", "helper-host", "provider-a", true, null, Now,
            "personal-session", "personal-workspace", Candidate, BootId: "boot");
        var request = Help("first") with {
            Kind = "review", ReadOnly = true, MachineId = personal.MachineId, WorkerId = personal.Id, ReuseSession = true
        };
        CoordinationWork first;
        CoordinationSessionBinding firstBinding;
        try {
            using (var store = new CoordinationStore(path, Policy)) {
                await store.InitializeAsync();
                first = await store.ExecuteAsync((e, s) => {
                    Initialize(e, s);
                    e.ObserveWorker(personal, HelperHost);
                    return e.Submit(request, Requester);
                }, Now);
                Assert.Equal("admitted", first.State);
                firstBinding = await store.ExecuteAsync((e, s) => Start(e, s, first), Now);
                var queued = await store.ExecuteAsync((e, _) => e.Submit(request with { Id = "second" }, Requester), Now);
                Assert.Equal("queued", queued.State);
                Assert.Null(queued.GrantId);
            }
            using (var store = new CoordinationStore(path, Policy)) {
                await store.InitializeAsync();
                await store.ExecuteAsync((e, s) => {
                    Assert.True(s.Work["first"].Request.ReuseSession);
                    Assert.Equal(firstBinding, s.Bindings["first"]);
                    Assert.Equal("started", s.Work["first"].State);
                    Assert.Equal("queued", s.Work["second"].State);
                    // A provider limit is a known hold for later work and may still produce a terminal failure result.
                    e.ObserveWorker(personal with {
                        ObservedAtUtc = Now.AddSeconds(1), Eligible = false, UnavailableReason = "provider-limit"
                    }, HelperHost);
                    return true;
                }, Now.AddSeconds(1));
                await Assert.ThrowsAsync<CoordinationConflictException>(() => store.ExecuteAsync<bool>((e, _) => {
                    e.Receipt("first", "terminal", Execution(first, "first-result"), HelperHost);
                    throw new CoordinationConflictException("injected-after-reused-terminal");
                }, Now.AddSeconds(1)));
                await store.ExecuteAsync((_, s) => {
                    Assert.Equal("started", s.Work["first"].State);
                    Assert.Empty(s.ResultOutbox);
                    Assert.Empty(s.Retirements);
                    return true;
                }, Now.AddSeconds(1));
                var failedReceipt = Execution(first, "first-result") with { Succeeded = false };
                Assert.Equal("failed", (await store.ExecuteAsync((e, _) =>
                    e.Receipt("first", "terminal", failedReceipt, HelperHost), Now.AddSeconds(1))).State);
                await store.ExecuteAsync((e, _) => e.BeginResultDelivery("first", first.Generation, "first-send", SourceHost), Now.AddSeconds(1));
                await store.ExecuteAsync((e, _) => e.RecordResultDelivery("first", first.Generation,
                    new("first-send", "uncertain", "lost-response"), SourceHost), Now.AddSeconds(1));
            }
            using (var store = new CoordinationStore(path, Policy)) {
                await store.InitializeAsync();
                await store.ExecuteAsync((e, s) => {
                    Assert.Equal("uncertain", s.ResultOutbox["first"].State);
                    Assert.Null(s.ResultOutbox["first"].ConsumedAtUtc);
                    Assert.Equal("queued", s.Work["second"].State);
                    Assert.Empty(s.Retirements);
                    Assert.Equal("provider-limit", s.Workers[personal.Id].UnavailableReason);
                    // Restore host-attested eligibility while leaving the unconsumed result as the remaining hold.
                    e.ObserveWorker(personal with { ObservedAtUtc = Now.AddSeconds(2) }, HelperHost);
                    Assert.Equal("queued", s.Work["second"].State);
                    return true;
                }, Now.AddSeconds(2));
                await Assert.ThrowsAsync<CoordinationConflictException>(() => store.ExecuteAsync((e, _) =>
                    e.ConsumeResult("first", first.Generation + 1, "first-result", Candidate, Requester), Now.AddSeconds(2)));
                await store.ExecuteAsync((e, _) => e.ConsumeResult("first", first.Generation, "first-result", Candidate, Requester), Now.AddSeconds(2));
                var second = await store.ExecuteAsync((_, s) => s.Work["second"], Now.AddSeconds(2));
                Assert.Equal("admitted", second.State);
                Assert.Equal(personal.Id, second.Assignee);
                Assert.NotEqual(first.GrantId, second.GrantId);
                await store.ExecuteAsync((e, s) => Start(e, s, second), Now.AddSeconds(2));
                await Assert.ThrowsAsync<CoordinationConflictException>(() => store.ExecuteAsync((e, _) =>
                    e.BindSession(firstBinding with { RequestId = "second" }, HelperHost), Now.AddSeconds(2)));
                await Assert.ThrowsAsync<CoordinationConflictException>(() => store.ExecuteAsync((e, _) =>
                    e.Receipt("second", "terminal", Execution(first, "old-result"), HelperHost), Now.AddSeconds(2)));
                await store.ExecuteAsync((e, _) => e.Receipt("second", "terminal", Execution(second, "second-result"), HelperHost), Now.AddSeconds(2));
                await store.ExecuteAsync((e, _) => e.ConsumeResult("second", second.Generation, "second-result", Candidate, Requester), Now.AddSeconds(2));
            }
            using (var store = new CoordinationStore(path, Policy)) {
                await store.InitializeAsync();
                var final = await store.ExecuteAsync((_, s) => s, Now.AddSeconds(3));
                Assert.Equal(firstBinding, final.Bindings["first"]);
                Assert.Equal("second", final.Bindings["second"].RequestId);
                Assert.NotNull(final.ResultOutbox["first"].ConsumedAtUtc);
                Assert.NotNull(final.ResultOutbox["second"].ConsumedAtUtc);
                Assert.Equal("first-result", final.ResultOutbox["first"].ResultRef);
                Assert.Equal("second-result", final.ResultOutbox["second"].ResultRef);
                Assert.Empty(final.Retirements);
                Assert.False(final.Workers[personal.Id].TemporaryWorker);
                var changes = await store.ChangesAsync(0, 100);
                Assert.Equal(2, changes.Count(c => c.Stage == "terminal"));
                Assert.Equal(2, changes.Count(c => c.Stage == "result-intent"));
                Assert.DoesNotContain(changes, c => c.Stage == "retirement-required");
            }
        } finally {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task LegacyRequestWithoutReuseFieldRetainsTemporaryWorkerGuardsAfterRestart() {
        var directory = LedgerTestPaths.NewDirectory("legacy-session-ownership");
        var path = Path.Combine(directory, "coordination.sqlite");
        // Construct the historical wire shape by omitting the newly added optional field.
        var json = JsonSerializer.SerializeToNode(Help("legacy") with { WorkerId = "helper-worker" },
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!.AsObject();
        Assert.True(json.Remove("reuseSession"));
        var legacy = json.Deserialize<CoordinationRequest>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.False(legacy.ReuseSession);
        try {
            using (var store = new CoordinationStore(path, Policy)) {
                await store.InitializeAsync();
                await store.ExecuteAsync((e, s) => {
                    Initialize(e, s);
                    var work = e.Submit(legacy, Requester);
                    Start(e, s, work);
                    s.Workers["helper-worker"] = s.Workers["helper-worker"] with { TemporaryWorker = false };
                    return true;
                }, Now);
            }
            using (var store = new CoordinationStore(path, Policy)) {
                await store.InitializeAsync();
                var before = await store.ExecuteAsync((_, s) => s, Now);
                Assert.False(before.Work["legacy"].Request.ReuseSession);
                Assert.Equal("helper-worker", before.Work["legacy"].Request.WorkerId);
                Assert.Equal("native-binding-required", (await Assert.ThrowsAsync<CoordinationConflictException>(() =>
                    store.ExecuteAsync((e, s) => e.Receipt("legacy", "terminal", Execution(s.Work["legacy"], "result"), HelperHost), Now))).Message);
                var after = await store.ExecuteAsync((_, s) => s, Now);
                Assert.Equal(before.Sequence, after.Sequence);
                Assert.Equal(before.Work["legacy"].State, after.Work["legacy"].State);
                Assert.Empty(after.ResultOutbox);
                Assert.Empty(after.Retirements);
            }
        } finally {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RestartPreservesAtomicTerminalOutboxUncertainDeliveryAndIndependentRetirement() {
        var directory = LedgerTestPaths.NewDirectory("coordination-lifecycle");
        var path = Path.Combine(directory, "coordination.sqlite");
        try {
            using (var store = new CoordinationStore(path, Policy)) {
                await store.InitializeAsync();
                await store.ExecuteAsync((e, s) => { Initialize(e, s); return true; }, Now);
                var failure = await Assert.ThrowsAsync<CoordinationConflictException>(() => store.ExecuteAsync<bool>((e, s) => {
                    // Fault after terminal projection, outbox and retirement changes: none may commit.
                    Finish(e, s);
                    throw new CoordinationConflictException("injected-after-terminal");
                }, Now));
                Assert.Equal("injected-after-terminal", failure.Message);
                var empty = await store.ExecuteAsync((_, s) => (s.Work.Count, s.ResultOutbox.Count, s.Retirements.Count), Now);
                Assert.Equal((0, 0, 0), empty);
                await store.ExecuteAsync((e, s) => Finish(e, s), Now);
                await store.ExecuteAsync((e, _) => e.BeginResultDelivery("help", 1, "send-one", SourceHost), Now);
                await store.ExecuteAsync((e, _) => e.RecordResultDelivery("help", 1, new("send-one", "uncertain", "lost-response"), SourceHost), Now);
                await store.ExecuteAsync((e, s) => e.Retire(new(s.Bindings["help"], "waiting", "protection", WaitingOn: "protected-primary-detach"), HelperHost), Now);
            }
            using (var store = new CoordinationStore(path, Policy)) {
                await store.InitializeAsync();
                var snapshot = await store.ExecuteAsync((_, s) => s, Now.AddSeconds(1));
                Assert.Equal("uncertain", snapshot.ResultOutbox["help"].State);
                Assert.Equal("send-one", snapshot.ResultOutbox["help"].AttemptId);
                Assert.Equal("protected-primary-detach", snapshot.Retirements["help"].WaitingOn);
                Assert.Equal("helper-session", snapshot.Bindings["help"].SessionId);
                Assert.Equal(Candidate, snapshot.ResultOutbox["help"].Candidate);
                Assert.Equal("requester-session", snapshot.ResultOutbox["help"].ReturnTo.SessionId);
                await Assert.ThrowsAsync<CoordinationConflictException>(() => store.ExecuteAsync((e, _) =>
                    e.BeginResultDelivery("help", 1, "send-two", SourceHost), Now.AddSeconds(1)));
                await store.ExecuteAsync((e, s) => e.Receipt("help", "terminal", Execution(s.Work["help"], "result"), HelperHost), Now);
                var journal = await store.ChangesAsync(0, 100);
                Assert.Single(journal, c => c.Stage == "result-intent");
                Assert.Single(journal, c => c.Stage == "retirement-required");
                Assert.Single(journal, c => c.Stage == "terminal");
                Assert.Single(journal, c => c.Stage == "result-sending");
                Assert.Single(journal, c => c.Stage == "result-uncertain");
                Assert.DoesNotContain(journal, c => c.Stage == "result-consumed");
                await store.ExecuteAsync((e, _) => e.ConsumeResult("help", 1, "result", Candidate, Requester), Now);
                await store.ExecuteAsync((e, s) => e.Retire(new(s.Bindings["help"], "resume", "detach-event", WaitingOn: "protected-primary-detach"), HelperHost), Now);
                await store.ExecuteAsync((e, s) => { Clean(e, s.Bindings["help"]); return true; }, Now);
            }
            using (var store = new CoordinationStore(path, Policy)) {
                await store.InitializeAsync();
                var final = await store.ExecuteAsync((_, s) => s, Now);
                Assert.Equal("uncertain", final.ResultOutbox["help"].State);
                Assert.NotNull(final.ResultOutbox["help"].ConsumedAtUtc);
                Assert.NotNull(final.Retirements["help"].CompletedAtUtc);
                Assert.Equal("result", final.Retirements["help"].ResultRef);
                Assert.Equal("proof-verified", final.Retirements["help"].Evidence["verified"]);
            }
        } finally {
            // This unique fixture directory is created by the store above and all its handles are disposed.
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
