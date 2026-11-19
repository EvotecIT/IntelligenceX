using DBAClientX;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Storage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class LedgerStabilityTests {
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private readonly string _directory = LedgerTestPaths.NewDirectory("stability");
    private string Database => Path.Combine(_directory, "ledger.sqlite");

    private static LedgerEvent RepositorySession(string item, long sequence, Guid epoch) => new() {
        EventId = Guid.NewGuid(), Epoch = epoch, Sequence = sequence, MachineId = "synthetic-machine",
        ReporterId = "synthetic-reporter", ItemId = item, Provider = "codex", Source = "watcher", ObservedAtUtc = Now,
        Session = new() { TurnId = "synthetic-turn", TurnOpen = true, LastActivityAtUtc = Now,
            Repository = new("example/project", "feature/" + item, "synthetic-checkout", "Synthetic " + item, [], [], false, "main", "observed") }
    };

    [Fact]
    public async Task HealthReportsAStuckWriterWithinItsBudget() {
        using var store = new LedgerStore(Database, new(new()));
        await store.InitializeAsync();
        Assert.True((await store.ProbeAsync(TimeSpan.FromSeconds(5))).Healthy);

        // Another connection holds the database write lock, so the next ingest waits for it while holding the write gate.
        using var blocker = new SqliteConnection($"Data Source={Database};Pooling=False");
        blocker.Open();
        using (var begin = blocker.CreateCommand()) { begin.CommandText = "BEGIN IMMEDIATE"; begin.ExecuteNonQuery(); }
        // SQLite calls complete synchronously, so the ingest runs on its own thread to overlap the probe.
        var stuck = Task.Run(() => store.IngestAsync([RepositorySession("synthetic-stuck", 1, Guid.NewGuid())], Now));
        // Under a loaded test run the ingest can take a while to start, so wait until it actually holds the gate.
        using (var entering = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
            while (store.WriterGate.CurrentCount != 0) await Task.Delay(10, entering.Token);

        var started = DateTimeOffset.UtcNow;
        var probe = await store.ProbeAsync(TimeSpan.FromSeconds(1));
        Assert.False(probe.Healthy);
        Assert.Equal("write-gate-busy", probe.Reason);
        Assert.True(DateTimeOffset.UtcNow - started < TimeSpan.FromSeconds(5));

        using (var rollback = blocker.CreateCommand()) { rollback.CommandText = "ROLLBACK"; rollback.ExecuteNonQuery(); }
        await stuck;
        Assert.True((await store.ProbeAsync(TimeSpan.FromSeconds(5))).Healthy);
    }

    [Fact]
    public async Task StartupRebuildsDerivedSessionTablesOncePerVersion() {
        var epoch = Guid.NewGuid();
        using (var store = new LedgerStore(Database, new(new()))) {
            await store.InitializeAsync();
            await store.IngestAsync([RepositorySession("synthetic-a", 1, epoch), RepositorySession("synthetic-b", 2, epoch)], Now);
        }
        Assert.Equal(LedgerStore.ProjectionVersion, await Scalar("PRAGMA user_version"));

        // A database from before the version mark gets its derived rows rebuilt from items, with progress reported.
        // Its derived rows can be missing, outdated or orphaned; the rebuild replaces all of them.
        await Execute("""
            DELETE FROM repository_sessions WHERE item='synthetic-a'; DELETE FROM session_status WHERE item='synthetic-a';
            UPDATE session_lookup SET title='outdated' WHERE item='synthetic-b';
            INSERT INTO session_lookup(item,native,title,machine,provider) VALUES('synthetic-gone',NULL,'gone','synthetic-machine','codex');
            PRAGMA user_version=0
            """);
        var steps = new List<StartupStep>();
        using (var store = new LedgerStore(Database, new(new()))) await store.InitializeAsync(progress: new Collect(steps));
        Assert.Equal(2L, await Scalar("SELECT COUNT(*) FROM repository_sessions"));
        Assert.Equal(2L, await Scalar("SELECT COUNT(*) FROM session_status"));
        Assert.Equal(2L, await Scalar("SELECT COUNT(*) FROM session_lookup"));
        Assert.Equal(0L, await Scalar("SELECT COUNT(*) FROM session_lookup WHERE title IN ('outdated','gone')"));
        Assert.Contains(steps, step => step.Phase == "rebuilding-projections");
        Assert.Equal(LedgerStore.ProjectionVersion, await Scalar("PRAGMA user_version"));

        // Once marked, a restart leaves the derived tables to ingest instead of rescanning every item.
        await Execute("DELETE FROM repository_sessions");
        steps.Clear();
        using (var store = new LedgerStore(Database, new(new()))) await store.InitializeAsync(progress: new Collect(steps));
        Assert.Equal(0L, await Scalar("SELECT COUNT(*) FROM repository_sessions"));
        Assert.DoesNotContain(steps, step => step.Phase == "rebuilding-projections");
        Assert.Equal("ledger-ready", steps[^1].Phase);
    }

    [Fact]
    public async Task ClosingRequestConnectionsLeavesTheWalForIncrementalCheckpoints() {
        using var store = new LedgerStore(Database, new(new()));
        await store.InitializeAsync();
        await store.IngestAsync([RepositorySession("synthetic-a", 1, Guid.NewGuid())], Now);
        // Without the store's own open connection, the ingest's closing connection would checkpoint and delete the WAL.
        Assert.True(File.Exists(Database + "-wal"));
    }

    private sealed class Collect(List<StartupStep> steps) : IProgress<StartupStep> {
        public void Report(StartupStep value) => steps.Add(value);
    }

    private async Task<long> Scalar(string sql) {
        using var database = new SQLite();
        await using var db = await database.OpenSessionAsync(Database);
        return Convert.ToInt64(await db.ExecuteScalarAsync(sql));
    }

    private async Task Execute(string sql) {
        using var database = new SQLite();
        await using var db = await database.OpenSessionAsync(Database);
        await db.ExecuteNonQueryAsync(sql);
    }
}
