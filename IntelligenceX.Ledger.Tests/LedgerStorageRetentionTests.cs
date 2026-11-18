using DBAClientX;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Storage;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class LedgerStorageRetentionTests : IDisposable {
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private readonly string _directory = LedgerTestPaths.NewDirectory("storage-retention");
    private string Database => Path.Combine(_directory, "ledger.sqlite");
    private readonly Guid _epoch = Guid.NewGuid();
    private long _sequence;

    private LedgerEvent Session(string item, DateTimeOffset at, bool open, string? question = null) => new() {
        EventId = Guid.NewGuid(), Epoch = _epoch, Sequence = ++_sequence, MachineId = "synthetic-machine",
        ReporterId = "synthetic-reporter", ItemId = item, Provider = "codex", Source = "watcher", ObservedAtUtc = at,
        Session = new() { TurnId = "synthetic-turn", TurnOpen = open, Outcome = open ? "unknown" : "completed",
            LastActivityAtUtc = at, Title = question }
    };

    [Fact]
    public async Task RepeatsThatOnlyRefreshTimesUpdateTheItemWithoutAddingHistory() {
        using var store = new LedgerStore(Database, new(new()));
        await store.InitializeAsync();
        await store.IngestAsync([Session("synthetic-item", Now, open: false)], Now);
        await store.IngestAsync([Session("synthetic-item", Now.AddMinutes(1), open: false)], Now.AddMinutes(1));
        await store.IngestAsync([Session("synthetic-item", Now.AddMinutes(2), open: false)], Now.AddMinutes(2));
        Assert.Single(await store.HistoryAsync("synthetic-item", 0, 100, default));
        var item = Assert.Single(await store.EvaluateAllAsync(Now.AddMinutes(2)));
        Assert.Equal(Now.AddMinutes(2), item.Session!.LastActivityAtUtc);

        await store.IngestAsync([Session("synthetic-item", Now.AddMinutes(3), open: false, question: "Renamed")], Now.AddMinutes(3));
        Assert.Equal(2, (await store.HistoryAsync("synthetic-item", 0, 100, default)).Length);
    }

    [Fact]
    public async Task TransitionsAndTheirReviewMarksExpireAfterThirtyDaysWhileRecentOnesStay() {
        using var store = new LedgerStore(Database, new(new()));
        await store.InitializeAsync();
        var old = Now - LedgerStore.TransitionRetention - TimeSpan.FromDays(1);
        await store.IngestAsync([Session("synthetic-old", old, open: true)], old);
        await store.IngestAsync([Session("synthetic-new", Now, open: true)], Now);
        var oldDay = DateOnly.FromDateTime(old.UtcDateTime);
        var oldTransitions = await store.DailyAsync(oldDay, default);
        Assert.NotEmpty(oldTransitions);
        var transitionId = (long)oldTransitions[0].GetType().GetProperty("transitionId")!.GetValue(oldTransitions[0])!;
        await store.IngestAsync([Session("synthetic-old", old, open: true) with { Session = null, ReviewMark = new(transitionId, "correct") }], old);

        await store.PruneEventsAsync(Now);
        Assert.Empty(await store.DailyAsync(oldDay, default));
        Assert.NotEmpty(await store.DailyAsync(DateOnly.FromDateTime(Now.UtcDateTime), default));
        Assert.Equal(0L, await Scalar("SELECT COUNT(*) FROM reviews"));
    }

    [Fact]
    public async Task NewDatabasesReturnFreedPagesAsRetentionRuns() {
        using var store = new LedgerStore(Database, new(new()));
        await store.InitializeAsync();
        Assert.Equal(2L, await Scalar("PRAGMA auto_vacuum"));
        var old = Now - LedgerStore.EventRetention - TimeSpan.FromDays(1);
        var padding = new string('x', 8000);
        for (var batch = 0; batch < 5; batch++)
            await store.IngestAsync([.. Enumerable.Range(0, 100).Select(i => Session($"synthetic-{batch}-{i}", old, open: false, question: padding))], old);
        await store.PruneEventsAsync(Now);
        Assert.Equal(0L, await Scalar("SELECT COUNT(*) FROM events"));
        Assert.True(await Scalar("PRAGMA freelist_count") < 50);
    }

    [Fact]
    public async Task CompactShrinksAnExistingDatabaseAndSwitchesItToIncrementalVacuum() {
        Directory.CreateDirectory(_directory);
        using (var database = new SQLite())
        await using (var db = await database.OpenSessionAsync(Database)) {
            await db.ExecuteNonQueryAsync("PRAGMA auto_vacuum=NONE");
            await db.ExecuteNonQueryAsync("CREATE TABLE filler(value TEXT NOT NULL)");
            for (var i = 0; i < 400; i++) await db.ExecuteNonQueryAsync("INSERT INTO filler(value) VALUES('" + new string('x', 8000) + "')");
            await db.ExecuteNonQueryAsync("DELETE FROM filler");
        }
        var (before, after) = await LedgerStore.CompactAsync(Database);
        Assert.True(after < before / 4, $"{before} -> {after}");
        Assert.Equal(2L, await Scalar("PRAGMA auto_vacuum"));
    }

    private async Task<long> Scalar(string sql) {
        using var database = new SQLite();
        await using var db = await database.OpenSessionAsync(Database);
        return Convert.ToInt64(await db.ExecuteScalarAsync(sql));
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
