using IntelligenceX.Ledger.Reporting;
using IntelligenceX.Telemetry.Agents;
using Xunit;

namespace IntelligenceX.Ledger.Tests;

public sealed class ReporterArchiveCacheTests {
    [Fact]
    public void ArchiveEvidenceFollowsTheSelectedOriginAndRetainsItsOwnObservationTime() {
        var now = new DateTimeOffset(2026, 1, 12, 12, 0, 0, TimeSpan.Zero);
        var copied = new AgentSessionState { Provider = "codex", Id = "synthetic-session", SourceRootId = "synthetic-copy",
            ArchiveStateKnown = true, IsArchived = true };
        var active = copied with { SourceRootId = "synthetic-active", IsArchived = false };
        var cache = new SessionArchiveCache();

        cache.Observe([copied, active], now);

        Assert.True(cache.For(copied)!.Archived);
        Assert.False(cache.For(active)!.Archived);
        Assert.Equal(now, cache.For(active)!.ObservedAtUtc);
        Assert.Null(cache.For(active with { SourceRootId = "synthetic-unobserved" }));
        Assert.Null(cache.For(active with { SourceRootId = null }));
        Assert.Null(cache.For(active with { Provider = "claude" }));

        var later = now.AddMinutes(5);
        cache.Observe([copied with { IsArchived = false }, active], later);

        Assert.False(cache.For(copied)!.Archived);
        Assert.Equal(later, cache.For(copied)!.ObservedAtUtc);
        Assert.Equal(now, cache.For(active)!.ObservedAtUtc);
        cache.Observe([active with { ArchiveStateKnown = false }], later.AddMinutes(1));
        Assert.Null(cache.For(active));
        Assert.Null(cache.For(copied));
    }
}
