using System;
using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Guards usage-limit queue dispatch gating to prevent replay loops.
/// </summary>
public sealed class MainWindowUsageLimitDispatchTests {
    /// <summary>
    /// Ensures future retry windows block queued dispatch and surface remaining minutes.
    /// </summary>
    [Fact]
    public void IsUsageLimitDispatchBlocked_ReturnsTrueWhenRetryWindowIsInFuture() {
        var nowUtc = new DateTime(2026, 2, 21, 10, 0, 0, DateTimeKind.Utc);
        var retryAfterUtc = nowUtc.AddMinutes(11);

        var blocked = MainWindow.IsUsageLimitDispatchBlocked(
            nowUtc,
            usageLimitHitUtc: null,
            usageLimitRetryAfterUtc: retryAfterUtc,
            rateLimitReached: null,
            out var retryAfterMinutes);

        Assert.True(blocked);
        Assert.Equal(11, retryAfterMinutes);
    }

    /// <summary>
    /// Ensures expired retry windows do not keep dispatch blocked when hard limit signals are absent.
    /// </summary>
    [Fact]
    public void IsUsageLimitDispatchBlocked_ReturnsFalseWhenRetryWindowExpiredAndNoHardLimitSignal() {
        var nowUtc = new DateTime(2026, 2, 21, 10, 0, 0, DateTimeKind.Utc);
        var retryAfterUtc = nowUtc.AddMinutes(-1);

        var blocked = MainWindow.IsUsageLimitDispatchBlocked(
            nowUtc,
            usageLimitHitUtc: null,
            usageLimitRetryAfterUtc: retryAfterUtc,
            rateLimitReached: false,
            out var retryAfterMinutes);

        Assert.False(blocked);
        Assert.Null(retryAfterMinutes);
    }

    /// <summary>
    /// Ensures explicit provider limit flags block dispatch even without retry-after hints.
    /// </summary>
    [Fact]
    public void IsUsageLimitDispatchBlocked_ReturnsTrueWhenRateLimitReachedIsTrueWithoutRetryAfter() {
        var nowUtc = new DateTime(2026, 2, 21, 10, 0, 0, DateTimeKind.Utc);

        var blocked = MainWindow.IsUsageLimitDispatchBlocked(
            nowUtc,
            usageLimitHitUtc: null,
            usageLimitRetryAfterUtc: null,
            rateLimitReached: true,
            out var retryAfterMinutes);

        Assert.True(blocked);
        Assert.Null(retryAfterMinutes);
    }

    /// <summary>
    /// Ensures recent usage-limit hits trigger a short cooldown when providers omit retry-after.
    /// </summary>
    [Fact]
    public void IsUsageLimitDispatchBlocked_ReturnsTrueDuringRecentFallbackCooldown() {
        var nowUtc = new DateTime(2026, 2, 21, 10, 0, 0, DateTimeKind.Utc);

        var blocked = MainWindow.IsUsageLimitDispatchBlocked(
            nowUtc,
            usageLimitHitUtc: nowUtc.AddSeconds(-30),
            usageLimitRetryAfterUtc: null,
            rateLimitReached: null,
            out var retryAfterMinutes);

        Assert.True(blocked);
        Assert.Null(retryAfterMinutes);
    }

    /// <summary>
    /// Ensures stale usage-limit hits do not keep dispatch blocked forever.
    /// </summary>
    [Fact]
    public void IsUsageLimitDispatchBlocked_ReturnsFalseWhenFallbackCooldownElapsed() {
        var nowUtc = new DateTime(2026, 2, 21, 10, 0, 0, DateTimeKind.Utc);

        var blocked = MainWindow.IsUsageLimitDispatchBlocked(
            nowUtc,
            usageLimitHitUtc: nowUtc.AddMinutes(-10),
            usageLimitRetryAfterUtc: null,
            rateLimitReached: null,
            out var retryAfterMinutes);

        Assert.False(blocked);
        Assert.Null(retryAfterMinutes);
    }
}
