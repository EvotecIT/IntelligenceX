using System.Text.Json;
using System.Text.Json.Nodes;
using DBAClientX;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Coordination;
using IntelligenceX.Ledger.Storage;
using Xunit;
using static IntelligenceX.Ledger.Tests.CoordinationLifecycleTests;
using static IntelligenceX.Ledger.Tests.CoordinationRequesterTests;

namespace IntelligenceX.Ledger.Tests;

public sealed class CoordinationRequesterStoreTests {
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task OldProjectionRestoresAndNewHashEnrollmentSurvivesRestartAndStoppedBackupWithDispatchHeld() {
        var root = LedgerTestPaths.NewDirectory("requester-restore");
        Directory.CreateDirectory(root);
        try {
            var source = Path.Combine(root, "source");
            var oldRestore = Path.Combine(root, "old-restored");
            var finalRestore = Path.Combine(root, "enrolled-restored");
            using (var ledger = new LedgerStore(Path.Combine(source, "ledger.sqlite"), new(new())))
                await ledger.InitializeAsync();
            using (var store = new CoordinationStore(Path.Combine(source, "coordination.sqlite"), Policy)) {
                await store.InitializeAsync();
                await store.ExecuteAsync((e, s) => { Initialize(e, s); return true; }, Now);
            }
            // Persist the actual pre-enrollment projection shape, then use the normal stopped backup/restore entry points.
            using (var database = new SQLite()) {
                await using var session = await database.OpenSessionAsync(Path.Combine(source, "coordination.sqlite"));
                var value = (string)(await session.ExecuteScalarAsync("SELECT json FROM coordination_state WHERE id=1"))!;
                var old = JsonNode.Parse(value)!;
                Assert.True(old.AsObject().Remove("requesters"));
                Assert.True(old.AsObject().Remove("requesterSuccessors"));
                await session.ExecuteNonQueryAsync("UPDATE coordination_state SET json=@json WHERE id=1",
                    new Dictionary<string, object?> { ["json"] = old.ToJsonString() });
            }
            await LedgerBackup.CreateStoppedAsync(source, Path.Combine(root, "old-backup"));
            await LedgerBackup.RestoreAsync(Path.Combine(root, "old-backup"), oldRestore);
            var path = Path.Combine(oldRestore, "coordination.sqlite");
            using (var store = new CoordinationStore(path, Policy)) {
                await store.InitializeAsync();
                await store.ExecuteAsync((e, s) => {
                    Assert.Empty(s.Requesters);
                    Assert.Empty(s.RequesterSuccessors);
                    Assert.True(s.Goals["task"].Paused);
                    Assert.False(s.Policy.Enabled);
                    Assert.Empty(s.Hosts);
                    var control = new CoordinationPrincipal("operator", "operator", "", []);
                    e.ReplacePolicy(s.Policy with { Enabled = true, Revision = s.Policy.Revision + 1 }, control, s.Policy.Revision);
                    e.ObserveHost(new("source-host", "boot", Now, ["review"]), SourceHost);
                    e.EnrollRequester(Enrollment with { ObservedAtUtc = Now.AddSeconds(1),
                        WorkerObservation = RequesterWorker with { ObservedAtUtc = Now.AddSeconds(1) } }, EnrollmentHost);
                    return true;
                }, Now.AddSeconds(1));
            }
            using (var store = new CoordinationStore(path, Policy)) {
                await store.InitializeAsync();
                await store.ExecuteAsync((e, s) => {
                    Assert.Equal(Enrollment.Id, e.AuthenticateRequester(Token)!.Id);
                    Assert.Equal(Enrollment.TokenSha256, s.Requesters[Enrollment.Id].TokenSha256);
                    Assert.Equal(s.Workers[Enrollment.WorkerId], s.Requesters[Enrollment.Id].WorkerObservation);
                    Assert.True(s.Workers[Enrollment.WorkerId].Eligible);
                    var successor = Enrollment with { Id = "next", GoalId = "next-goal", SourceRef = "next-source",
                        TokenSha256 = new('a', 64), ObservedAtUtc = Now.AddSeconds(2) };
                    e.EnrollRequester(successor, EnrollmentHost);
                    return true;
                }, Now.AddSeconds(2));
                var changes = JsonSerializer.Serialize(await store.ChangesAsync(0, 100), Json);
                Assert.DoesNotContain(Token, changes);
                Assert.DoesNotContain(Enrollment.TokenSha256, changes);
            }
            using (var database = new SQLite()) {
                await using var session = await database.OpenSessionAsync(path);
                var value = (string)(await session.ExecuteScalarAsync("SELECT json FROM coordination_state WHERE id=1"))!;
                Assert.DoesNotContain(Token, value);
                Assert.Contains(Enrollment.TokenSha256, value);
            }
            await LedgerBackup.CreateStoppedAsync(oldRestore, Path.Combine(root, "enrolled-backup"));
            await LedgerBackup.RestoreAsync(Path.Combine(root, "enrolled-backup"), finalRestore);
            using (var store = new CoordinationStore(Path.Combine(finalRestore, "coordination.sqlite"), Policy)) {
                await store.InitializeAsync();
                await store.ExecuteAsync((e, s) => {
                    var caller = e.AuthenticateRequester(Token)!;
                    Assert.Equal(Enrollment.Id, caller.Id);
                    Assert.Equal("next", s.RequesterSuccessors[Enrollment.Id]);
                    Assert.False(s.Requesters[Enrollment.Id].DispatchAllowed);
                    Assert.Equal("requester-dispatch-held", Assert.Throws<CoordinationConflictException>(() =>
                        e.SetGoal(new(Enrollment.GoalId, "", 0, false, false, "authorization", ["result"]), caller, 0)).Message);
                    Assert.False(s.Policy.Enabled);
                    Assert.Empty(s.Hosts);
                    Assert.False(s.Workers[Enrollment.WorkerId].Eligible);
                    return true;
                }, Now.AddSeconds(3));
            }
            using var collision = new CoordinationStore(Path.Combine(finalRestore, "coordination.sqlite"), Policy,
                [new(Enrollment.Id, "client", new('x', 32), ["sample"])]);
            Assert.Equal("requester-static-id-conflict", (await Assert.ThrowsAsync<CoordinationConflictException>(
                () => collision.InitializeAsync())).Message);
        } finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void StaticPrincipalCollisionIsRejectedAtRegistrationWithoutReservingAnIdPrefix() {
        var state = CreateState();
        var engine = new CoordinationEngine(state, Now, [new(Enrollment.Id, "client", new('s', 32), ["sample"])]);
        Assert.Equal("requester-static-id-conflict", Assert.Throws<CoordinationConflictException>(() =>
            engine.EnrollRequester(Enrollment, EnrollmentHost)).Message);
        Assert.Empty(state.Requesters);
        var prefixed = new CoordinationPrincipal("requester-" + new string('a', 64), "client", new('s', 32), ["sample"]);
        new CoordinationEngine(state, Now, [prefixed]).ValidateRequesterPrincipals();
        Assert.Equal(prefixed.Id, new CoordinationEngine(state, Now).SetGoal(new("legacy", "", 0, false, false,
            "authorization", ["result"]), prefixed, 0).Owner);
    }
}
