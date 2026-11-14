using System.Text.Json;
using DBAClientX;
using IntelligenceX.Ledger.Rules;
using IntelligenceX.Ledger.Storage;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class LedgerReportRangeTests {
    [Fact]
    public async Task DailyReportUsesRecordedRangeIndexAndPreservesOrdinalOrderAndMarks() {
        var directory = LedgerTestPaths.NewDirectory("report-range");
        var path = Path.Combine(directory, "ledger.sqlite");
        try {
            using var store = new LedgerStore(path, new LedgerRules(new()));
            await store.InitializeAsync();
            using var database = new SQLite();
            await using var db = await database.OpenSessionAsync(path);
            await db.ExecuteNonQueryAsync("""
                WITH RECURSIVE entries(n) AS (VALUES(1) UNION ALL SELECT n+1 FROM entries WHERE n<10000)
                INSERT INTO transitions(item,recorded,json)
                SELECT 'synthetic-item',CASE WHEN n IN(9997,9999) THEN '2030-01-02T00:00:00.0000000+00:00'
                    ELSE '2030-01-01T00:00:00.0000000+00:00' END,'{"value":"synthetic"}' FROM entries;
                INSERT INTO reviews(transition,mark,recorded) VALUES(9999,'right','2030-01-02T00:01:00.0000000+00:00');
                """);
            var rows = await store.DailyAsync(new(2030, 1, 2), CancellationToken.None);
            using var json = JsonDocument.Parse(JsonSerializer.Serialize(rows));
            Assert.Equal(2, json.RootElement.GetArrayLength());
            Assert.Equal(9997, json.RootElement[0].GetProperty("transitionId").GetInt64());
            Assert.Equal("unmarked", json.RootElement[0].GetProperty("mark").GetString());
            Assert.Equal(9999, json.RootElement[1].GetProperty("transitionId").GetInt64());
            Assert.Equal("right", json.RootElement[1].GetProperty("mark").GetString());
            var plan = await db.QueryAsListAsync("""
                EXPLAIN QUERY PLAN SELECT t.ordinal,t.json,r.mark FROM transitions t LEFT JOIN reviews r ON r.transition=t.ordinal
                WHERE t.recorded>=@start AND t.recorded<@end ORDER BY t.ordinal
                """, r => r.GetString(3), new Dictionary<string, object?> {
                    ["start"] = "2030-01-02T00:00:00.0000000+00:00", ["end"] = "2030-01-03T00:00:00.0000000+00:00"
                });
            Assert.Contains(plan, row => row.Contains("SEARCH t USING INDEX", StringComparison.Ordinal));
            Assert.DoesNotContain(plan, row => row.StartsWith("SCAN t", StringComparison.Ordinal));
        } finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
