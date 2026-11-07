using IntelligenceX.Ledger.Context;
using IntelligenceX.Ledger.Contracts;
using IntelligenceX.Ledger.Hosting;
using IntelligenceX.Ledger.Storage;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class LedgerWorkProjectionTests {
    [Theory]
    [InlineData("resolved-retry", false)]
    [InlineData("resolved-candidate", false)]
    [InlineData("current-turn", true)]
    public async Task CurrentWorkUsesTheCurrentScopedDeclarationEpisode(string scenario, bool included) {
        var root = LedgerTestPaths.NewDirectory("declaration-membership");
        try {
            using var store = new LedgerStore(Path.Combine(root, "ledger.sqlite"), new(new()));
            await store.InitializeAsync();
            var now = new DateTimeOffset(2026, 1, 12, 12, 0, 0, TimeSpan.Zero);
            var old = now.AddDays(-4);
            var scope = new TaskScope("synthetic-task", 1, "synthetic-turn");
            var opening = new WorkerDeclaration { Id = "synthetic-opening", EpisodeId = "synthetic-first", Scope = scope,
                CheckpointRef = "synthetic-checkpoint", ConditionRef = "synthetic-condition", Kind = "operation",
                NextActor = "worker", Blocking = true, RecordedAtUtc = old, ReviewAfterUtc = now.AddHours(1),
                CandidateRef = scenario == "resolved-candidate" ? "synthetic-old-candidate" : null };
            var observation = new LedgerEvent { EventId = Guid.NewGuid(), Epoch = Guid.NewGuid(), Sequence = 2,
                ReporterId = "synthetic-reporter", MachineId = "synthetic-machine", ItemId = "synthetic-item",
                Provider = "codex", Source = "evidence", ObservedAtUtc = old, Declaration = opening };
            if (scenario != "current-turn") await store.IngestAsync([observation with { EventId = Guid.NewGuid(), Sequence = 1,
                Declaration = null, Session = new() { Scope = scope, TurnId = scope.TurnId, TurnOpen = false,
                    Outcome = "completed", TurnEndedAtUtc = old, LastActivityAtUtc = old } }], old);
            await store.IngestAsync([observation], old);
            if (scenario != "current-turn") {
                await store.IngestAsync([observation with { EventId = Guid.NewGuid(), Sequence = 3,
                    ObservedAtUtc = old.AddHours(1), Declaration = opening with {
                        Id = "synthetic-resolution", EpisodeId = "synthetic-second", RecordedAtUtc = old.AddHours(1),
                        CandidateRef = scenario == "resolved-candidate" ? "synthetic-new-candidate" : null, Resolved = true } }], old.AddHours(1));
            }
            await store.IngestAsync([observation with { EventId = Guid.NewGuid(), Sequence = 4, ItemId = "machine",
                Provider = "machine", Declaration = null, ObservedAtUtc = now, Heartbeat = true }], now);
            var settings = new LedgerSettings { DataDirectory = root, ReadToken = new('r', 32), WriteToken = new('w', 32),
                ExpectedReporters = new() { ["synthetic-machine"] = ["synthetic-reporter"] } };

            var view = await new LedgerRuntime(store, settings).WorkAsync(new ProjectContextService(null), now, default);

            Assert.Equal(included ? 1 : 0, view.Items.Length);
            if (included) Assert.Equal("active", Assert.Single(view.Items).Item.ReasonCode);
            Assert.Single(await store.EvaluateAllAsync(now));
        } finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task HistoricalObservationGapsStayOutOfCurrentWorkWhileRecentUnknownAndClaimedArchivedWorkRemain() {
        var root = Path.Combine(Path.GetTempPath(), "ledger-work-membership-" + Guid.NewGuid().ToString("N"));
        try {
            using var store = new LedgerStore(Path.Combine(root, "ledger.sqlite"), new(new()));
            await store.InitializeAsync();
            var now = new DateTimeOffset(2026, 1, 12, 12, 0, 0, TimeSpan.Zero);
            var old = now.AddDays(-4);
            LedgerEvent Observation(string id, long sequence, SessionFact session, WorkFact? work = null) => new() {
                EventId = Guid.NewGuid(), Epoch = Guid.NewGuid(), Sequence = sequence,
                ReporterId = "synthetic-reporter", MachineId = "synthetic-machine", ItemId = id,
                Provider = "codex", Source = "watcher", ObservedAtUtc = old, Session = session, Work = work
            };
            await store.IngestAsync([
                Observation("historical-archived", 1, new() { Archived = true, ArchiveObservedAtUtc = old }),
                Observation("historical-unknown", 2, new()),
                Observation("recent-unknown", 3, new() { LastActivityAtUtc = now }) with { ObservedAtUtc = now },
                Observation("claimed-archived", 4, new() { Archived = true, ArchiveObservedAtUtc = old },
                    new() { ActiveClaim = true, ClaimsAtUtc = now }),
                Observation("machine", 5, new()) with { ObservedAtUtc = now, Provider = "machine", Session = null, Heartbeat = true }
            ], now);
            var settings = new LedgerSettings { DataDirectory = root, ReadToken = new('r', 32), WriteToken = new('w', 32),
                ExpectedReporters = new() { ["synthetic-machine"] = ["synthetic-reporter"] } };

            var view = await new LedgerRuntime(store, settings).WorkAsync(new ProjectContextService(null), now, default);

            Assert.Equal(2, view.Items.Length);
            Assert.DoesNotContain(view.Items, row => row.Item.Id.StartsWith("historical-", StringComparison.Ordinal));
            Assert.Equal("unknown", Assert.Single(view.Items, row => row.Item.Id == "recent-unknown").Item.ReasonCode);
            Assert.Equal("stuck", Assert.Single(view.Items, row => row.Item.Id == "recent-unknown").Item.Verdict);
            Assert.True(Assert.Single(view.Items, row => row.Item.Id == "claimed-archived").Item.Availability!.Archived);
            Assert.Equal(4, (await store.EvaluateAllAsync(now)).Length);
        } finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task BoundedListIncludesWorkingAlongsideALargerStuckBacklogAndRetainsFullCounts() {
        var root = Path.Combine(Path.GetTempPath(), "ledger-work-groups-" + Guid.NewGuid().ToString("N"));
        try {
            using var store = new LedgerStore(Path.Combine(root, "ledger.sqlite"), new(new()));
            await store.InitializeAsync();
            var now = new DateTimeOffset(2026, 1, 12, 12, 0, 0, TimeSpan.Zero);
            var events = Enumerable.Range(1, 21).Select(index => new LedgerEvent {
                EventId = Guid.NewGuid(), Epoch = Guid.NewGuid(), Sequence = index,
                ReporterId = "synthetic-reporter", MachineId = "synthetic-machine", ItemId = "synthetic-" + index,
                Provider = "codex", Source = "watcher", ObservedAtUtc = now,
                Session = new() { TurnId = "synthetic-turn", TurnOpen = index == 21, Outcome = index == 21 ? "unknown" : "failed",
                    TurnEndedAtUtc = index == 21 ? null : now.AddMinutes(-10), LastActivityAtUtc = now.AddMinutes(-1) }
            }).ToArray();
            await store.IngestAsync(events, now);
            var settings = new LedgerSettings { DataDirectory = root, ReadToken = new('r', 32), WriteToken = new('w', 32),
                ExpectedReporters = new() { ["synthetic-machine"] = ["synthetic-reporter"] } };
            var runtime = new LedgerRuntime(store, settings);

            var view = await runtime.WorkAsync(new ProjectContextService(null), now, default, limit: 2);

            Assert.Equal(2, view.Items.Length);
            Assert.Contains(view.Items, row => row.Item.Verdict == "working");
            Assert.Contains(view.Items, row => row.Item.Verdict == "stuck");
            Assert.Equal(20, view.Counts["stuck"]);
            Assert.Equal(1, view.Counts["working"]);
            Assert.True(view.Truncated);
        } finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

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
            var corrected = await runtime.WorkAsync(context, now.AddSeconds(-1), default);
            var later = await runtime.WorkAsync(context, now.AddMinutes(2), default);

            Assert.Equal("active", Assert.Single(initial.Items).Item.ReasonCode);
            Assert.Equal(now, cached.AsOfUtc);
            Assert.Equal("active", Assert.Single(cached.Items).Item.ReasonCode);
            Assert.Equal(now.AddSeconds(-1), corrected.AsOfUtc);
            Assert.Equal(now.AddMinutes(2), later.AsOfUtc);
            Assert.Equal("quiet", Assert.Single(later.Items).Item.ReasonCode);
            Assert.Contains("work-view-cache:max-age-10s", later.Coverage);
        } finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }
}
