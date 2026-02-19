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

    [Fact]
    public void Upsert_ShouldBoundRetentionDuringHighChurn() {
        var nowUtc = new DateTimeOffset(2026, 2, 19, 12, 0, 0, TimeSpan.Zero);
        var clockUtc = nowUtc;
        var store = new InMemoryToolAuthenticationProbeStore(
            maxRecords: 25,
            maxRecordAge: TimeSpan.FromMinutes(10),
            utcNowProvider: () => clockUtc);

        for (var i = 0; i < 250; i++) {
            var isRecent = i >= 200;
            store.Upsert(new ToolAuthenticationProbeRecord {
                ProbeId = $"probe-{i}",
                ProbedAtUtc = isRecent ? nowUtc.AddMinutes(-5) : nowUtc.AddMinutes(-20),
                IsSuccessful = true
            });
        }

        var retained = 0;
        for (var i = 0; i < 250; i++) {
            if (!store.TryGet($"probe-{i}", out var record)) {
                continue;
            }

            retained++;
            Assert.True(nowUtc - record.ProbedAtUtc <= TimeSpan.FromMinutes(10));
        }

        Assert.Equal(25, retained);

        clockUtc = nowUtc.AddMinutes(11);
        var retainedAfterExpiry = 0;
        for (var i = 0; i < 250; i++) {
            if (store.TryGet($"probe-{i}", out _)) {
                retainedAfterExpiry++;
            }
        }

        Assert.Equal(0, retainedAfterExpiry);
    }
}
