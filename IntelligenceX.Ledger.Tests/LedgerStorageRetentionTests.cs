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
    public async Task ObservationsLeftOutOfHistoryStillAcknowledgeRetriesAndRejectReusedIdentity() {
        using var store = new LedgerStore(Database, new(new()));
        await store.InitializeAsync();
        await store.IngestAsync([Session("synthetic-item", Now, open: false)], Now);
        var refresh = Session("synthetic-item", Now.AddMinutes(1), open: false);
        await store.IngestAsync([refresh], Now.AddMinutes(1));
        Assert.Single(await store.HistoryAsync("synthetic-item", 0, 100, default));

        await store.IngestAsync([refresh], Now.AddMinutes(2));
        await Assert.ThrowsAsync<EventConflictException>(() => store.IngestAsync([refresh with { Session = refresh.Session! with { Title = "Changed" } }], Now.AddMinutes(2)));
        await Assert.ThrowsAsync<EventConflictException>(() => store.IngestAsync(
            [Session("synthetic-other", Now.AddMinutes(2), open: false) with { Sequence = refresh.Sequence }], Now.AddMinutes(2)));
        Assert.Null((await store.EvaluateAllAsync(Now.AddMinutes(2))).Single(item => item.Id == "synthetic-item").Session!.Title);

        await store.PruneEventsAsync(Now + LedgerStore.EventRetention + TimeSpan.FromDays(1));
        Assert.Equal(0L, await Scalar("SELECT COUNT(*) FROM receipts"));
    }

    [Fact]
    public async Task ARepeatedDeliveryStageWithTheSameTimeAddsNoHistory() {
        using var store = new LedgerStore(Database, new(new()));
        await store.InitializeAsync();
        LedgerEvent Delivery(DateTimeOffset observed) => new() { EventId = Guid.NewGuid(), Epoch = _epoch, Sequence = ++_sequence,
            MachineId = "synthetic-machine", ReporterId = "synthetic-reporter", ItemId = "synthetic-delivery-item", Provider = "codex",
            Source = "evidence", ObservedAtUtc = observed, Delivery = new("synthetic-delivery", "submitted", Now) };
        await store.IngestAsync([Delivery(Now)], Now);
        await store.IngestAsync([Delivery(Now.AddMinutes(1))], Now.AddMinutes(1));
        Assert.Single(await store.HistoryAsync("synthetic-delivery-item", 0, 100, default));
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
        await store.IngestAsync([Session("synthetic-old", old, open: true) with { Session = null, ReviewMark = new(transitionId, "right") }], old);
        Assert.Contains(await store.DailyAsync(oldDay, default), row => (string)row.GetType().GetProperty("mark")!.GetValue(row)! == "right");

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
        await Scalar("PRAGMA wal_checkpoint(TRUNCATE)");
        var before = new FileInfo(Database).Length;
        await store.PruneEventsAsync(Now);
        await Scalar("PRAGMA wal_checkpoint(TRUNCATE)");
        Assert.Equal(0L, await Scalar("SELECT COUNT(*) FROM events"));
        // The file shrinks by about the deleted events' size once the checkpoint applies the vacuum; items stay.
        var returned = before - new FileInfo(Database).Length;
        Assert.True(returned > 500 * padding.Length * 3 / 4, $"{before} -> {new FileInfo(Database).Length}");
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
        var (before, after, checkpointed) = await LedgerStore.CompactAsync(Database);
        Assert.True(checkpointed);
        Assert.True(after < before / 4, $"{before} -> {after}");
        Assert.Equal(2L, await Scalar("PRAGMA auto_vacuum"));
    }

    [Fact]
    public async Task CompactCommandReadsTheDataDirectoryInAnyCaseAndMeasuresItsVolume() {
        using (var store = new LedgerStore(Database, new(new()))) await store.InitializeAsync();
        var config = Path.Combine(_directory, "service.json");
        File.WriteAllText(config, System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string> { ["DataDirectory"] = _directory }));
        Assert.Equal(0, await Program.Main(["compact", "--config", config]));
        var volume = Program.VolumeOf(Path.GetFullPath(Database));
        Assert.StartsWith(volume.RootDirectory.FullName, Path.GetFullPath(Database), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private async Task<long> Scalar(string sql) {
        using var database = new SQLite();
        await using var db = await database.OpenSessionAsync(Database);
        return Convert.ToInt64(await db.ExecuteScalarAsync(sql));
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
