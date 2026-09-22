using IntelligenceX.Telemetry.Limits;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class LimitNotificationWindowTrackerTests {
    [Fact]
    public void RearmsAfterActualResetWithoutDuplicatingDriftingReadings() {
        var tracker = new LimitNotificationWindowTracker();
        var now = new DateTimeOffset(2026, 9, 22, 10, 0, 0, TimeSpan.Zero);
        var reset = now.AddHours(1);
        const string key = "codex|account|weekly|Exhausted";
        var active = new HashSet<string> { key };

        Assert.True(tracker.Observe(key, reset, now));
        Assert.False(tracker.Observe(key, reset.AddSeconds(30), now.AddMinutes(2)));
        Assert.False(tracker.Observe(key, reset.AddHours(5), now.AddMinutes(10)));
        Assert.True(tracker.Observe(key, reset.AddHours(5), reset.AddSeconds(1)));
        Assert.False(tracker.Observe(key, reset.AddHours(5).AddSeconds(30), reset.AddMinutes(10)));

        tracker.RetainObserved(active, active);
        Assert.False(tracker.Observe(key, reset.AddHours(5), reset.AddMinutes(11)));
        tracker.RetainObserved(new HashSet<string>(), active);
        Assert.True(tracker.Observe(key, reset.AddHours(5), reset.AddMinutes(12)));
    }

    [Fact]
    public void UnavailableReadingPreservesGenerationButObservedRecoveryCanRearm() {
        var tracker = new LimitNotificationWindowTracker();
        var now = DateTimeOffset.UtcNow;
        const string warning = "codex|account|weekly|Warning";
        const string exhausted = "codex|account|weekly|Exhausted";
        var observed = new HashSet<string> { warning, exhausted };
        Assert.True(tracker.Observe(warning, now.AddDays(1), now));

        tracker.RetainObserved(new HashSet<string>(), new HashSet<string>()); // Account API unavailable.
        Assert.False(tracker.Observe(warning, now.AddDays(1), now.AddMinutes(1)));
        Assert.True(tracker.Observe(exhausted, now.AddDays(1), now.AddMinutes(2)));
        tracker.RetainObserved(new HashSet<string> { warning, exhausted }, observed);
        Assert.False(tracker.Observe(warning, now.AddDays(1), now.AddMinutes(3))); // Back below exhausted.

        tracker.RetainObserved(new HashSet<string>(), observed); // Measured below warning.
        Assert.True(tracker.Observe(warning, now.AddDays(1), now.AddMinutes(4)));
    }

    [Fact]
    public void UnknownResetTimeDoesNotInventAnotherGeneration() {
        var tracker = new LimitNotificationWindowTracker();
        var now = DateTimeOffset.UtcNow;
        Assert.True(tracker.Observe("provider|account|weekly|Warning", null, now));
        Assert.False(tracker.Observe("provider|account|weekly|Warning", now.AddHours(5), now.AddMinutes(1)));
        Assert.False(tracker.Observe("provider|account|weekly|Warning", now.AddHours(5).AddSeconds(10), now.AddMinutes(2)));
        tracker.Clear();
        Assert.True(tracker.Observe("provider|account|weekly|Warning", null, now.AddMinutes(3)));
    }
}
