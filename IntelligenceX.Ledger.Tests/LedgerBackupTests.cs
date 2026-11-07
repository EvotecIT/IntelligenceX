using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Rules;
using IntelligenceX.Ledger.Storage;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class LedgerBackupTests {
    [Fact]
    public async Task OverlappingCopiesPublishOneMatchingDatabaseAndManifest() {
        var root = Path.Combine(Path.GetTempPath(), "ledger-backup-race-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try {
            var sources = new[] { Path.Combine(root, "first.sqlite"), Path.Combine(root, "second.sqlite") };
            for (var index = 0; index < sources.Length; index++) {
                using var source = new LedgerStore(sources[index], new(new()));
                await source.InitializeAsync();
                for (var sequence = 1; sequence <= index + 1; sequence++) {
                    var now = DateTimeOffset.UtcNow;
                    await source.IngestAsync([new() { EventId = Guid.NewGuid(), Epoch = Guid.NewGuid(), Sequence = sequence,
                        ReporterId = "synthetic-reporter", MachineId = "synthetic-machine", ItemId = "synthetic-item",
                        Provider = "codex", Source = "watcher", ObservedAtUtc = now, Session = new() { TurnOpen = true } }], now);
                }
            }
            var destination = Path.Combine(root, "accepted.sqlite");
            var copies = Enumerable.Range(0, 8).Select(index => Task.Run(async () => {
                try { return await LedgerBackup.CreateAsync(sources[index % 2], destination); }
                catch (Exception error) when (error is IOException or InvalidDataException) { return null; }
            })).ToArray();
            var winner = Assert.Single((await Task.WhenAll(copies)).OfType<LedgerBackupManifest>());
            var published = System.Text.Json.JsonSerializer.Deserialize<LedgerBackupManifest>(
                await File.ReadAllTextAsync(destination + ".json"), LedgerJson.Options);
            Assert.Equal(winner, published);
            var bytes = await File.ReadAllBytesAsync(destination);
            Assert.Equal(winner.Sha256, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant());
            Assert.Empty(Directory.GetFiles(root, "*.partial*"));
        } finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task OnlineBackupRestoresCommittedHistoryAndReplayReceiptsWithoutFollowingLaterWrites() {
        var root = Path.Combine(Path.GetTempPath(), "ledger-backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try {
            var source = Path.Combine(root, "live.sqlite");
            var target = Path.Combine(root, "backup.sqlite");
            var now = DateTimeOffset.UtcNow;
            var observation = new LedgerEvent { EventId = Guid.NewGuid(), Epoch = Guid.NewGuid(), Sequence = 1,
                ReporterId = "synthetic-reporter", MachineId = "synthetic-machine", ItemId = "synthetic-item",
                Provider = "codex", Source = "watcher", ObservedAtUtc = now,
                Delivery = new("synthetic-delivery", "submitted", now) };
            using var writer = new LedgerStore(source, new(new()));
            await writer.InitializeAsync();
            await writer.IngestAsync([observation], now);

            var manifest = await LedgerBackup.CreateAsync(source, target);
            await writer.IngestAsync([observation with { EventId = Guid.NewGuid(), Sequence = 2,
                Delivery = new("synthetic-delivery", "started", now.AddSeconds(1)) }], now.AddSeconds(1));

            Assert.Equal(1, manifest.Events);
            Assert.Equal(1, manifest.LastEventOrdinal);
            Assert.Equal(1, manifest.Items);
            Assert.Equal(new FileInfo(target).Length, manifest.Bytes);
            Assert.Equal(64, manifest.Sha256.Length);
            Assert.True(File.Exists(target + ".json"));
            await Assert.ThrowsAsync<InvalidDataException>(() => LedgerBackup.CreateAsync(source, target));
            using var restored = new LedgerStore(target, new(new()));
            await restored.InitializeAsync();
            Assert.Equal([observation.EventId], await restored.IngestAsync([observation], now.AddMinutes(1)));
            var item = Assert.Single(await restored.EvaluateAllAsync(now.AddMinutes(1)));
            Assert.Equal("unknown", item.Deliveries["synthetic-delivery"].Stages["started"].Knowledge);
            Assert.Single(await restored.HistoryAsync(item.Id, 0, 100, default));
            Assert.Equal(2, (await writer.HistoryAsync(item.Id, 0, 100, default)).Length);
        } finally { Directory.Delete(root, recursive: true); }
    }
}
