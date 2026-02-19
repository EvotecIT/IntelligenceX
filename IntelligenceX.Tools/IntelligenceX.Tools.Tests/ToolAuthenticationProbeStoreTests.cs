using System;
using IntelligenceX.Tools;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class ToolAuthenticationProbeStoreTests {
    [Fact]
    public void TryGet_ShouldEvictExpiredRecord() {
        var nowUtc = new DateTimeOffset(2026, 2, 19, 12, 0, 0, TimeSpan.Zero);
        var store = new InMemoryToolAuthenticationProbeStore(
            maxRecords: 10,
            maxRecordAge: TimeSpan.FromMinutes(5),
            utcNowProvider: () => nowUtc);

        store.Upsert(new ToolAuthenticationProbeRecord {
            ProbeId = "probe-expired",
            ProbedAtUtc = nowUtc.AddMinutes(-6),
            IsSuccessful = true
        });

        Assert.False(store.TryGet("probe-expired", out _));
    }

    [Fact]
    public void Upsert_ShouldEvictOldestRecordsWhenCapacityExceeded() {
        var nowUtc = new DateTimeOffset(2026, 2, 19, 12, 0, 0, TimeSpan.Zero);
        var store = new InMemoryToolAuthenticationProbeStore(
            maxRecords: 2,
            maxRecordAge: TimeSpan.FromHours(1),
            utcNowProvider: () => nowUtc);

        store.Upsert(new ToolAuthenticationProbeRecord {
            ProbeId = "probe-1",
            ProbedAtUtc = nowUtc.AddMinutes(-30),
            IsSuccessful = true
        });
        store.Upsert(new ToolAuthenticationProbeRecord {
            ProbeId = "probe-2",
            ProbedAtUtc = nowUtc.AddMinutes(-20),
            IsSuccessful = true
        });
        store.Upsert(new ToolAuthenticationProbeRecord {
            ProbeId = "probe-3",
            ProbedAtUtc = nowUtc.AddMinutes(-10),
            IsSuccessful = true
        });

        Assert.False(store.TryGet("probe-1", out _));
        Assert.True(store.TryGet("probe-2", out _));
        Assert.True(store.TryGet("probe-3", out _));
    }
}
