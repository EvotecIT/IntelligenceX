using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Coordination;
using IntelligenceX.Ledger.Hosting;
using IntelligenceX.Ledger.Storage;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class LedgerReadConcurrencyTests : IDisposable {
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);
    private readonly string _directory = LedgerTestPaths.NewDirectory("read-concurrency");
    private static readonly Guid SessionId = Guid.Parse("10000000-0000-4000-8000-0000000000c1");

    private static LedgerEvent Session(DateTimeOffset at, long sequence, bool turnOpen) => new() {
        EventId = Guid.NewGuid(), Epoch = Guid.Empty, Sequence = sequence, ReporterId = "synthetic-reporter",
        MachineId = "synthetic-machine", Provider = "claude", ItemId = "synthetic-session", ItemKind = "session",
        Source = "watcher", SourceVersion = "synthetic-parser", ObservedAtUtc = at, Session = new() {
            Title = "Synthetic session", SourceSessionId = SessionId, TurnId = "synthetic-turn", TurnOpen = turnOpen,
            TurnStartedAtUtc = at.AddMinutes(-1), TurnEndedAtUtc = turnOpen ? null : at,
            Outcome = turnOpen ? "unknown" : "completed", LastActivityAtUtc = at
        }
    };

    private static LedgerEvent Heartbeat(DateTimeOffset at) => new() {
        EventId = Guid.NewGuid(), Epoch = Guid.Empty, Sequence = 100, ReporterId = "synthetic-reporter",
        MachineId = "synthetic-machine", Provider = "machine", ItemId = "machine", Source = "watcher",
        ObservedAtUtc = at, Heartbeat = true
    };

    [Fact]
    public async Task SessionMachineAndReportReadsDoNotWaitForTheWriter() {
        using var store = new LedgerStore(Path.Combine(_directory, "ledger.sqlite"), new(new()));
        await store.InitializeAsync();
        var now = DateTimeOffset.UtcNow;
        await store.IngestAsync([Session(now, 1, turnOpen: true), Heartbeat(now)], now);
        var runtime = new LedgerRuntime(store, new LedgerSettings { DataDirectory = _directory, ReadToken = new('r', 32),
            WriteToken = new('w', 32), ExpectedReporters = new() { ["synthetic-machine"] = ["synthetic-reporter"] } });
        await runtime.MachinesAsync(now, default);
        await store.EvaluateAllAsync(now);

        // Simulates a long ingest or evaluation write holding the single writer.
        await store.WriterGate.WaitAsync();
        try {
            Assert.Equal("healthy", Assert.Single(await runtime.MachinesAsync(now, default).WaitAsync(Bound)).State);
            Assert.Equal("found", (await store.SessionStatusAsync(SessionId.ToString(), now).WaitAsync(Bound)).State);
            Assert.NotEmpty(await store.CoverageAsync(default).WaitAsync(Bound));
            Assert.NotEmpty(await store.HistoryAsync("synthetic-session", 0, 10, default).WaitAsync(Bound));
            await store.ReportAsync(DateOnly.FromDateTime(now.UtcDateTime), now, default).WaitAsync(Bound);
            // An unchanged re-evaluation writes nothing, so a work view never queues behind ingest.
            Assert.Single(await store.EvaluateAllAsync(now).WaitAsync(Bound), item => item.Id == "synthetic-session");
        } finally { store.WriterGate.Release(); }
    }

    [Fact]
    public async Task EvaluationKeepsEvidenceIngestedWhileItWasComputing() {
        using var store = new LedgerStore(Path.Combine(_directory, "ledger.sqlite"), new(new()));
        await store.InitializeAsync();
        var opened = new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);
        await store.IngestAsync([Session(opened, 1, turnOpen: true)], opened);
        var later = opened.AddDays(1);
        var ingested = false;
        store.BeforeEvaluationWrite = async token => {
            store.BeforeEvaluationWrite = null;
            await store.IngestAsync([Session(later, 2, turnOpen: false)], later, token);
            ingested = true;
        };

        await store.EvaluateAllAsync(later);

        Assert.True(ingested, "The evaluation found no changed row, so the concurrent write path was not exercised.");
        var status = await store.SessionStatusAsync(SessionId.ToString(), later);
        Assert.False(status.TurnOpen);
        Assert.Equal(later, status.LastActivityAtUtc);
    }

    [Fact]
    public async Task PolicyReadDoesNotWaitForACoordinationCommand() {
        using var store = new CoordinationStore(Path.Combine(_directory, "coordination.sqlite"),
            new(3, [new("synthetic-machine", 2, 1, 4, 4096, ["build"])]));
        await store.InitializeAsync();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var command = Task.Run(() => store.ExecuteAsync((_, state) => {
            entered.Set();
            release.Wait();
            return state.Policy.Revision;
        }, DateTimeOffset.UtcNow));
        try {
            Assert.True(entered.Wait(Bound));
            Assert.Equal(3, await store.ReadAsync(state => state.Policy.Revision).WaitAsync(Bound));
        } finally { release.Set(); }
        Assert.Equal(3, await command);
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
