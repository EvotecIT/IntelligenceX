using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Coordination;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class CoordinationStoreTests : IDisposable {
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ix-coordination-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly CoordinationPolicy Policy = new(1, [new("machine", 2, 1, 4, 4096, ["build"])]);
    private static readonly CoordinationPrincipal Client = new("client", "client", "unused", ["sample"]);
    private static CoordinationRequest Request(string id) => new(id, "goal", "build", "build", "machine", null,
        new(BuildSlots: 1, CpuUnits: 2), new("sample", "feature/" + id, new string('a', 40), null, "checkpoint", "build"), [], 1, "authorization");

    [Fact]
    public async Task RestartPreservesRequestIdentityJournalAndUncertainOccupancy() {
        var path = Path.Combine(_directory, "coordination.sqlite");
        string grant;
        using (var store = new CoordinationStore(path, Policy)) {
            await store.InitializeAsync();
            await store.ExecuteAsync((e, _) => e.SetGoal(new("goal", "", 0, false, false, "authorization", ["criterion"]), Client, 0), Now);
            await store.ExecuteAsync((e, _) => { e.ObserveHost(new("machine", "boot", Now, ["build"]), new("host", "host", "unused", [], "machine")); return true; }, Now);
            grant = (await store.ExecuteAsync((e, _) => e.Submit(Request("first"), Client), Now)).GrantId!;
            Assert.Throws<IOException>(() => { using var competing = new CoordinationStore(path, Policy); competing.InitializeAsync().GetAwaiter().GetResult(); });
        }
        using var restarted = new CoordinationStore(path, Policy);
        await restarted.InitializeAsync();
        var retry = await restarted.ExecuteAsync((e, _) => e.Submit(Request("first"), Client), Now.AddSeconds(1));
        Assert.Equal(grant, retry.GrantId);
        await restarted.ExecuteAsync((e, _) => e.Submit(Request("second"), Client), Now.AddSeconds(1));
        var summary = await restarted.ExecuteAsync((e, _) => { e.Schedule(); return e.Summary("sample"); }, Now.AddMinutes(3));
        Assert.Equal(1, summary.Counts["reconcile"]);
        Assert.Equal(1, summary.Counts["queued"]);
        var journal = await restarted.ChangesAsync(0, 100);
        Assert.Single(journal, c => c.RequestId == "first" && c.Stage == "requested");
        Assert.Single(journal, c => c.RequestId == "first" && c.Stage == "admitted");
        Assert.Single(journal, c => c.RequestId == "first" && c.Stage == "reconciliation-required");
    }

    [Fact]
    public async Task RejectedTransactionRollsBackProjectionAndJournal() {
        using var store = new CoordinationStore(Path.Combine(_directory, "coordination.sqlite"), Policy);
        await store.InitializeAsync();
        await Assert.ThrowsAsync<CoordinationConflictException>(() => store.ExecuteAsync<bool>((e, _) => {
            e.SetGoal(new("goal", "", 0, false, false, "authorization", ["criterion"]), Client, 0);
            throw new CoordinationConflictException("injected-command-failure");
        }, Now));
        Assert.False(await store.ExecuteAsync((_, s) => s.Goals.ContainsKey("goal"), Now));
        Assert.Empty(await store.ChangesAsync(0, 100));
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
