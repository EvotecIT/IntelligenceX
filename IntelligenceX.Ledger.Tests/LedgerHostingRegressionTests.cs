using System.Text.Json;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Hosting;
using IntelligenceX.Ledger.Storage;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class LedgerHostingRegressionTests : IDisposable {
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ix-ledger-hosting", Guid.NewGuid().ToString("N"));
    private static LedgerEvent Heartbeat(DateTimeOffset now) => new() {
        EventId = Guid.NewGuid(), Epoch = Guid.NewGuid(), Sequence = 1, ObservedAtUtc = now, Heartbeat = true,
        MachineId = "synthetic-machine", ReporterId = "synthetic-first-reporter", ItemId = "machine", Provider = "machine", Source = "watcher"
    };

    [Fact]
    public async Task NeverSeenReporterKeepsEnrollmentSinceAcrossHealthySiblingHeartbeats() {
        using var store = new LedgerStore(Path.Combine(_directory, "ledger.sqlite"), new(new()));
        await store.InitializeAsync();
        var settings = new LedgerSettings { DataDirectory = _directory, ReadToken = new('r', 32), WriteToken = new('w', 32),
            ExpectedReporters = new() { ["synthetic-machine"] = ["synthetic-first-reporter", "synthetic-second-reporter"] } };
        var runtime = new LedgerRuntime(store, settings);
        var now = DateTimeOffset.UtcNow;
        var initial = Assert.Single(await runtime.MachinesAsync(now, default));
        var enrolled = initial.Verdict!.SinceUtc;
        foreach (var minutes in new[] { 1, 2 }) {
            var observed = now.AddMinutes(minutes);
            await store.IngestAsync([Heartbeat(observed)], observed);
            var machine = Assert.Single(await runtime.MachinesAsync(observed, default));
            Assert.Equal("reporter-silent", machine.State);
            Assert.Equal(observed, machine.LastHeartbeatAtUtc);
            Assert.Equal(enrolled, machine.Verdict!.SinceUtc);
            Assert.Equal("first-observation", machine.Verdict.SinceBasis);
        }
    }

    [Fact]
    public async Task ShadowFileAdvancesCoverageForHeartbeatOnlyObservations() {
        using var store = new LedgerStore(Path.Combine(_directory, "ledger.sqlite"), new(new()));
        await store.InitializeAsync();
        using var worker = new ShadowReportWorker(store, new() { DataDirectory = _directory, ReadToken = new('r', 32), WriteToken = new('w', 32) });
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var now = DateTimeOffset.UtcNow;
        var path = Path.Combine(_directory, "shadow", now.UtcDateTime.ToString("yyyy-MM-dd") + ".json");
        await store.IngestAsync([Heartbeat(now)], now);
        await worker.StartAsync(default);
        try {
            async Task<long> Count() {
                try {
                    using var document = JsonDocument.Parse(await File.ReadAllBytesAsync(path, deadline.Token));
                    Assert.Empty(document.RootElement.GetProperty("transitions").EnumerateArray());
                    return document.RootElement.GetProperty("observationCoverage").GetProperty("eventCount").GetInt64();
                } catch (IOException) { return 0; }
            }
            while (await Count() != 1) await Task.Delay(50, deadline.Token);
            var received = DateTimeOffset.UtcNow;
            await store.IngestAsync([Heartbeat(received)], received);
            long count = 1;
            try { while ((count = await Count()) != 2) await Task.Delay(50, deadline.Token); }
            catch (OperationCanceledException) when (deadline.IsCancellationRequested) { }
            Assert.Equal(2, count);
            using var report = JsonDocument.Parse(await File.ReadAllBytesAsync(path));
            Assert.Equal(received, report.RootElement.GetProperty("observationCoverage").GetProperty("latestReceiptAtUtc").GetDateTimeOffset());
        } finally { await worker.StopAsync(default); }
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
