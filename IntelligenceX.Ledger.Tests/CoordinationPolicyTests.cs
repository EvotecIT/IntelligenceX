using System.Text.Json;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Coordination;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class CoordinationPolicyTests : IDisposable {
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly CoordinationPrincipal Operator = new("synthetic-editor", "operator", "unused", []);
    private static CoordinationPolicy Policy => new(1, [new("machine.one", 2, 2, 8, 8192, ["test"])]);
    private readonly string _root = LedgerTestPaths.NewDirectory("policy");

    [Fact]
    public async Task PathEditPersistsItsValuesEditorTimeAndRevisionAcrossRestart() {
        var path = Path.Combine(_root, "coordination.sqlite");
        using (var store = new CoordinationStore(path, Policy)) {
            await store.InitializeAsync();
            var updated = await store.ExecuteAsync((engine, _) => engine.SetPolicyValue(
                new("machines.machine.one.buildSlots", Value("3"), 1), Operator), Now);
            Assert.Equal(2, updated.Revision);
            Assert.Equal(3, Assert.Single(updated.Machines).BuildSlots);
            var change = Assert.Single(await store.ChangesAsync(0, 100));
            Assert.Equal(Operator.Id, change.Principal);
            Assert.Equal(Now, change.AtUtc);
            var capacity = Assert.Single(change.PolicyChanges!, value => value.Path == "machines.machine.one.buildSlots");
            Assert.Equal(2, capacity.OldValue.GetInt32());
            Assert.Equal(3, capacity.NewValue.GetInt32());
        }
        using var restarted = new CoordinationStore(path, Policy);
        await restarted.InitializeAsync();
        var restored = await restarted.ExecuteAsync((_, state) => state.Policy, Now.AddMinutes(1));
        Assert.Equal(2, restored.Revision);
        Assert.Equal(3, Assert.Single(restored.Machines).BuildSlots);
        Assert.Single(await restarted.ChangesAsync(0, 100));
    }

    [Fact]
    public async Task NoOpRoleDenialAndStaleRevisionCannotCreateAChange() {
        using var store = new CoordinationStore(Path.Combine(_root, "coordination.sqlite"), Policy);
        await store.InitializeAsync();
        var noOp = await store.ExecuteAsync((engine, _) => engine.SetPolicyValue(
            new("machines.machine.one.buildSlots", Value("2"), 1), Operator), Now);
        Assert.Equal(1, noOp.Revision);
        var reader = Operator with { Role = "client" };
        var denied = await Assert.ThrowsAsync<CoordinationConflictException>(() => store.ExecuteAsync((engine, _) =>
            engine.SetPolicyValue(new("machines.machine.one.buildSlots", Value("3"), 1), reader), Now));
        Assert.Equal("policy-role-denied", denied.Message);
        var stale = await Assert.ThrowsAsync<CoordinationConflictException>(() => store.ExecuteAsync((engine, _) =>
            engine.SetPolicyValue(new("machines.machine.one.buildSlots", Value("3"), 0), Operator), Now));
        Assert.Equal("policy-revision-conflict", stale.Message);
        Assert.Empty(await store.ChangesAsync(0, 100));
        var unchanged = await store.ExecuteAsync((_, state) => state.Policy, Now);
        Assert.Equal(1, unchanged.Revision);
        Assert.Equal(2, Assert.Single(unchanged.Machines).BuildSlots);
    }

    [Theory]
    [InlineData("session.idleArchiveHours", "-1", "invalid-session-policy")]
    [InlineData("lease.maximumTtlMinutes.bench", "-1", "invalid-lease-policy")]
    [InlineData("load.heavyCpuPercent", "101", "invalid-load-policy")]
    [InlineData("routing.engine", "[]", "invalid-routing-policy")]
    [InlineData("priority.benchmark", "\"important\"", "invalid-priority-policy")]
    public async Task InvalidPolicyValueRollsBackWithoutAnAuditEntry(string path, string value, string reason) {
        using var store = new CoordinationStore(Path.Combine(_root, "coordination.sqlite"), Policy);
        await store.InitializeAsync();
        var error = await Assert.ThrowsAsync<CoordinationConflictException>(() => store.ExecuteAsync((engine, _) =>
            engine.SetPolicyValue(new(path, Value(value), 1), Operator), Now));
        Assert.Equal(reason, error.Message);
        Assert.Empty(await store.ChangesAsync(0, 100));
        Assert.Equal(1, await store.ExecuteAsync((_, state) => state.Policy.Revision, Now));
    }

    [Fact]
    public void LegacyPolicyJsonReceivesTheCentralDefaults() {
        var legacy = """
            {"revision":1,"machines":[{"id":"synthetic","agentSlots":2,"buildSlots":1,"cpuUnits":8,"memoryMiB":8192,"capabilities":["test"]}]}
            """;
        var policy = JsonSerializer.Deserialize(legacy, LedgerReadJson.Default.CoordinationPolicy)!;
        Assert.Equal(48, policy.Session.IdleArchiveHours);
        Assert.Equal(40, policy.Session.FreshAfterTurns);
        Assert.Equal(1440, policy.Lease.TakeoverIdleMinutesHuman);
        Assert.Equal(new[] { "claude", "codex" }, policy.Routing["engine"]);
        Assert.Equal(120, policy.Lease.MaximumTtlMinutes["bench"]);
        CoordinationValidation.Policy(policy);
    }

    private static JsonElement Value(string json) {
        using var value = JsonDocument.Parse(json);
        return value.RootElement.Clone();
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
