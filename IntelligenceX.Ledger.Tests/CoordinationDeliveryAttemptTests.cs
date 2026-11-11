using System.Text.Json.Nodes;
using DBAClientX;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Coordination;
using Xunit;
using static IntelligenceX.Ledger.Tests.CoordinationLifecycleTests;

namespace IntelligenceX.Ledger.Tests;

public sealed class CoordinationDeliveryAttemptTests {
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RestartRejectsHistoricalAttemptReuseAndLateReceiptsIncludingLegacySnapshots(bool legacySnapshot) {
        var directory = LedgerTestPaths.NewDirectory("delivery-attempts");
        var path = Path.Combine(directory, "coordination.sqlite");
        var firstNegative = new CoordinationDeliveryReceipt("attempt-a", "not-submitted", "excluded-a");
        try {
            using (var store = new CoordinationStore(path, Policy)) {
                await store.InitializeAsync();
                await store.ExecuteAsync((e, s) => {
                    Initialize(e, s);
                    Finish(e, s);
                    e.BeginResultDelivery("help", 1, "attempt-a", SourceHost);
                    var first = e.RecordResultDelivery("help", 1, firstNegative, SourceHost);
                    Assert.Equal(first, e.RecordResultDelivery("help", 1, firstNegative, SourceHost));
                    e.BeginResultDelivery("help", 1, "attempt-b", SourceHost);
                    e.RecordResultDelivery("help", 1, new("attempt-b", "not-submitted", "excluded-b"), SourceHost);
                    return true;
                }, Now);
            }
            if (legacySnapshot) {
                // Preserve the real append-only journal while reproducing the pre-history projection shape.
                using var database = new SQLite();
                await using var db = await database.OpenSessionAsync(path);
                var value = (string)(await db.ExecuteScalarAsync("SELECT json FROM coordination_state WHERE id=1"))!;
                var projection = JsonNode.Parse(value)!.AsObject();
                projection.Remove("resultDeliveryAttempts");
                await db.ExecuteNonQueryAsync("UPDATE coordination_state SET json=@json WHERE id=1",
                    new Dictionary<string, object?> { ["json"] = projection.ToJsonString() });
            }
            using (var store = new CoordinationStore(path, Policy)) {
                await store.InitializeAsync();
                var error = await Assert.ThrowsAsync<CoordinationConflictException>(() => store.ExecuteAsync((e, _) =>
                    e.BeginResultDelivery("help", 1, "attempt-a", SourceHost), Now));
                Assert.Equal("delivery-needs-reconciliation", error.Message);
                var rollback = await Assert.ThrowsAsync<CoordinationConflictException>(() => store.ExecuteAsync<bool>((e, _) => {
                    e.BeginResultDelivery("help", 1, "attempt-c", SourceHost);
                    throw new CoordinationConflictException("after-begin");
                }, Now));
                Assert.Equal("after-begin", rollback.Message);
                await store.ExecuteAsync((e, s) => {
                    Assert.Equal("intent", s.ResultOutbox["help"].State);
                    Assert.Equal("attempt-b", s.ResultOutbox["help"].AttemptId);
                    e.RecordResultDelivery("help", 1, new("hold", "held", "destination-busy"), SourceHost);
                    Assert.Equal("delivery-needs-reconciliation", Assert.Throws<CoordinationConflictException>(() =>
                        e.BeginResultDelivery("help", 1, "attempt-a", SourceHost)).Message);
                    return e.BeginResultDelivery("help", 1, "attempt-c", SourceHost);
                }, Now);
            }
            using (var store = new CoordinationStore(path, Policy)) {
                await store.InitializeAsync();
                var error = await Assert.ThrowsAsync<CoordinationConflictException>(() => store.ExecuteAsync((e, _) =>
                    e.RecordResultDelivery("help", 1, firstNegative, SourceHost), Now));
                Assert.Equal("delivery-attempt-conflict", error.Message);
                await store.ExecuteAsync((e, s) => {
                    Assert.Equal("sending", s.ResultOutbox["help"].State);
                    Assert.Equal("attempt-c", s.ResultOutbox["help"].AttemptId);
                    var receipt = new CoordinationDeliveryReceipt("attempt-c", "uncertain", "uncertain-c");
                    var uncertain = e.RecordResultDelivery("help", 1, receipt, SourceHost);
                    Assert.Equal(uncertain, e.RecordResultDelivery("help", 1, receipt, SourceHost));
                    var submitted = receipt with { Stage = "submitted", EvidenceRef = "submitted-c" };
                    var delivered = e.RecordResultDelivery("help", 1, submitted, SourceHost);
                    Assert.Equal(delivered, e.RecordResultDelivery("help", 1, submitted, SourceHost));
                    Assert.Equal("submitted", delivered.State);
                    Assert.Null(delivered.ConsumedAtUtc);
                    return true;
                }, Now);
                var journal = await store.ChangesAsync(0, 100);
                Assert.Equal(new[] { "attempt-a", "attempt-b", "attempt-c" },
                    journal.Where(c => c.Stage == "result-sending").Select(c => c.EvidenceRef));
                Assert.Equal(2, journal.Count(c => c.Stage == "result-not-submitted"));
                Assert.Single(journal, c => c.Stage == "result-uncertain");
                Assert.Single(journal, c => c.Stage == "result-submitted");
                await store.ExecuteAsync((e, s) => {
                    e.ConsumeResult("help", 1, "result", Candidate, Requester);
                    Clean(e, s.Bindings["help"]);
                    e.ObserveWorker(new("other-helper", "helper-host", "provider-a", true, null, Now,
                        "other-session", "other-workspace", Candidate, BootId: "boot", TemporaryWorker: true), HelperHost);
                    var other = e.Submit(Help("other"), Requester);
                    Assert.Equal("admitted", other.State);
                    Start(e, s, other);
                    e.Receipt("other", "terminal", Execution(other, "other-result"), HelperHost);
                    Assert.Equal("sending", e.BeginResultDelivery("other", other.Generation, "attempt-a", SourceHost).State);
                    return true;
                }, Now);
            }
        } finally {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
