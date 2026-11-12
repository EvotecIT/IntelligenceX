using System.Text.Json;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Coordination;
using Xunit;
using static IntelligenceX.Ledger.Tests.CoordinationLifecycleTests;

namespace IntelligenceX.Ledger.Tests;

public sealed class CoordinationTaskBriefTests {
    [Theory]
    [InlineData("omitted", "help", true)]
    [InlineData("null", "review", true)]
    [InlineData("plain-text", "help", true)]
    [InlineData("plain-text", "review", true)]
    [InlineData("ascii-limit", "help", true)]
    [InlineData("ascii-over-limit", "help", false)]
    [InlineData("utf8-limit", "review", true)]
    [InlineData("utf8-over-limit", "review", false)]
    [InlineData("empty", "help", false)]
    [InlineData("whitespace", "review", false)]
    public async Task AdmissionAndRestartReplayPreserveBoundedImmutableTaskBrief(string input, string kind, bool accepted) {
        string? brief = input switch {
            "omitted" or "null" => null,
            "plain-text" => "Review the current candidate.\nReturn the findings and verification. Résumé.",
            "ascii-limit" => new('a', 8192),
            "ascii-over-limit" => new('a', 8193),
            "utf8-limit" => new('é', 4096),
            "utf8-over-limit" => new string('é', 4096) + "a",
            "empty" => "",
            "whitespace" => " \t\r\n\u2003",
            _ => throw new ArgumentOutOfRangeException(nameof(input))
        };
        var request = Help() with { Kind = kind, ReadOnly = kind == "review", TaskBrief = brief };
        var json = JsonSerializer.SerializeToNode(request, LedgerReadJson.Default.CoordinationRequest)!.AsObject();
        if (input == "omitted") Assert.True(json.Remove("taskBrief"));
        request = json.Deserialize(LedgerReadJson.Default.CoordinationRequest)!;
        Assert.Equal(brief, request.TaskBrief);

        var directory = LedgerTestPaths.NewDirectory("task-brief");
        var path = Path.Combine(directory, "coordination.sqlite");
        try {
            CoordinationWork admitted;
            using (var store = new CoordinationStore(path, Policy)) {
                await store.InitializeAsync();
                await store.ExecuteAsync((engine, state) => { Initialize(engine, state); return true; }, Now);
                var before = await store.ExecuteAsync((_, state) => state.Sequence, Now);
                if (!accepted) {
                    var error = await Assert.ThrowsAsync<CoordinationConflictException>(() =>
                        store.ExecuteAsync((engine, _) => engine.Submit(request, Requester), Now));
                    Assert.Equal("invalid-task-brief", error.Message);
                    var rejected = await store.ExecuteAsync((_, state) => state, Now);
                    Assert.Empty(rejected.Work);
                    Assert.Equal(before, rejected.Sequence);
                    Assert.Empty(await store.ChangesAsync(before, 100));
                    return;
                }
                admitted = await store.ExecuteAsync((engine, _) => engine.Submit(request, Requester), Now);
                Assert.Equal("admitted", admitted.State);
                Assert.Equal(brief, admitted.Request.TaskBrief);
            }

            using (var store = new CoordinationStore(path, Policy)) {
                await store.InitializeAsync();
                var before = await store.ExecuteAsync((_, state) => state.Sequence, Now);
                var replay = await store.ExecuteAsync((engine, _) => engine.Submit(request, Requester), Now.AddSeconds(1));
                Assert.Equal(admitted.GrantId, replay.GrantId);
                Assert.Equal(admitted.Generation, replay.Generation);
                Assert.Equal(admitted.State, replay.State);
                Assert.Equal(request.Checkpoint, replay.Request.Checkpoint);
                Assert.Equal(brief, replay.Request.TaskBrief);
                var error = await Assert.ThrowsAsync<CoordinationConflictException>(() => store.ExecuteAsync(
                    (engine, _) => engine.Submit(request with { TaskBrief = "Changed instructions." }, Requester), Now.AddSeconds(1)));
                Assert.Equal("request-id-conflict", error.Message);
                var retained = await store.ExecuteAsync((_, state) => state, Now);
                Assert.Equal(brief, retained.Work[request.Id].Request.TaskBrief);
                Assert.Equal(before, retained.Sequence);
                Assert.Empty(await store.ChangesAsync(before, 100));
            }
        } finally {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
