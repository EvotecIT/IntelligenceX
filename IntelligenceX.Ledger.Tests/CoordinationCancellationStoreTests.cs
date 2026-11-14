using System.Text.Json;
using IntelligenceX.Ledger.Coordination;
using Xunit;
using static IntelligenceX.Ledger.Tests.CoordinationLifecycleTests;
using static IntelligenceX.Ledger.Tests.CoordinationCancellationTests;

namespace IntelligenceX.Ledger.Tests;

public sealed class CoordinationCancellationStoreTests {
    [Fact]
    public async Task CancellationCommitsAcceptanceAndHistoryAtomicallyAndReplaysAfterRestart() {
        var directory = LedgerTestPaths.NewDirectory("cancellation");
        var path = Path.Combine(directory, "coordination.sqlite");
        string original;
        long sequence;
        try {
            using (var store = new CoordinationStore(path, Policy)) {
                await store.InitializeAsync();
                original = await store.ExecuteAsync((engine, state) => {
                    Initialize(engine, state);
                    return JsonSerializer.Serialize(Queue(engine, state).Request);
                }, Now);
                sequence = (await store.ExecuteAsync((_, state) => state.Sequence, Now));
                await Assert.ThrowsAsync<CoordinationConflictException>(() => store.ExecuteAsync<bool>((engine, _) => {
                    engine.Cancel("obsolete", Cancellation, Requester);
                    throw new CoordinationConflictException("injected-before-commit");
                }, Now));
            }
            using (var store = new CoordinationStore(path, Policy)) {
                await store.InitializeAsync();
                await store.ExecuteAsync((_, state) => {
                    Assert.Equal("queued", state.Work["obsolete"].State);
                    Assert.Equal(sequence, state.Sequence);
                    return true;
                }, Now);
                Assert.Empty(await store.ChangesAsync(sequence, 100));
                await store.ExecuteAsync((engine, _) => engine.Cancel("obsolete", Cancellation, Requester), Now);
            }
            using (var store = new CoordinationStore(path, Policy)) {
                await store.InitializeAsync();
                var changes = await store.ChangesAsync(sequence, 100);
                Assert.Equal(new[] { "cancelled", "cancellation-accepted", "accepted" }, changes.Select(c => c.Stage));
                Assert.All(changes, c => {
                    Assert.Equal(Requester.Id, c.Principal);
                    Assert.Equal(Cancellation.ReasonRef, c.EvidenceRef);
                });
                var final = await store.ExecuteAsync((engine, state) => {
                    var work = engine.Cancel("obsolete", Cancellation, Requester);
                    Assert.Equal("cancelled", work.State);
                    Assert.Equal(0, work.Generation);
                    Assert.Equal(Cancellation.ReasonRef, work.Reason);
                    Assert.Equal(original, JsonSerializer.Serialize(work.Request));
                    Assert.True(work.Stages!.ContainsKey("cancellation-accepted"));
                    Assert.True(work.Stages.ContainsKey("accepted"));
                    Assert.Null(work.ResultRef);
                    Assert.Empty(state.Bindings);
                    Assert.Empty(state.ResultOutbox);
                    Assert.Empty(state.Retirements);
                    Assert.Empty(state.Reconciliations);
                    return state.Sequence;
                }, Now.AddSeconds(1));
                Assert.Equal(sequence + 3, final);
                await Assert.ThrowsAsync<CoordinationConflictException>(() => store.ExecuteAsync((engine, _) =>
                    engine.Cancel("obsolete", Cancellation with { ReasonRef = "different-reason" }, Requester), Now));
                Assert.Equal(changes, await store.ChangesAsync(sequence, 100));
            }
        } finally {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
