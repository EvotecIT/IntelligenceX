using IntelligenceX.Ledger.Context;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Hosting;
using IntelligenceX.Ledger.Storage;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class LedgerWorkProjectionTests {
    [Fact]
    public async Task CachedViewRetainsItsObservationTimeAndReevaluatesTimeBoundariesAfterExpiry() {
        var root = Path.Combine(Path.GetTempPath(), "ledger-work-view-" + Guid.NewGuid().ToString("N"));
        try {
            using var store = new LedgerStore(Path.Combine(root, "ledger.sqlite"), new(new()));
            await store.InitializeAsync();
            var now = new DateTimeOffset(2026, 1, 12, 12, 0, 0, TimeSpan.Zero);
            var observation = new LedgerEvent { EventId = Guid.NewGuid(), Epoch = Guid.NewGuid(), Sequence = 1,
                ReporterId = "synthetic-reporter", MachineId = "synthetic-machine", ItemId = "synthetic-item",
                Provider = "codex", Source = "watcher", ObservedAtUtc = now,
                Session = new() { TurnId = "synthetic-turn", TurnOpen = true, LastActivityAtUtc = now.AddMinutes(-9) } };
            await store.IngestAsync([observation, observation with { EventId = Guid.NewGuid(), Sequence = 2, ItemId = "machine",
                Provider = "machine", Session = null, Heartbeat = true }], now);
            var settings = new LedgerSettings { DataDirectory = root, ReadToken = new('r', 32), WriteToken = new('w', 32),
                ExpectedReporters = new() { ["synthetic-machine"] = ["synthetic-reporter"] } };
            var runtime = new LedgerRuntime(store, settings);
            var context = new ProjectContextService(null);

            var initial = await runtime.WorkAsync(context, now, default);
            var cached = await runtime.WorkAsync(context, now.AddSeconds(5), default);
            var later = await runtime.WorkAsync(context, now.AddMinutes(2), default);

            Assert.Equal("active", Assert.Single(initial.Items).Item.ReasonCode);
            Assert.Equal(now, cached.AsOfUtc);
            Assert.Equal("active", Assert.Single(cached.Items).Item.ReasonCode);
            Assert.Equal(now.AddMinutes(2), later.AsOfUtc);
            Assert.Equal("quiet", Assert.Single(later.Items).Item.ReasonCode);
            Assert.Contains("work-view-cache:max-age-10s", later.Coverage);
        } finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }
}
