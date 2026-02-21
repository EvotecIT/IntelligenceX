using System;
using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Verifies provider circuit-breaker policy for repeated transient failures.
/// </summary>
public sealed class MainWindowProviderCircuitPolicyTests {
    /// <summary>
    /// Ensures cooldown grows exponentially and respects max clamp.
    /// </summary>
    [Theory]
    [InlineData(1, 12)]
    [InlineData(2, 24)]
    [InlineData(3, 48)]
    [InlineData(4, 96)]
    [InlineData(5, 120)]
    [InlineData(8, 120)]
    public void ResolveCircuitBreakerCooldown_ReturnsExpectedCooldown(int openCount, int expectedSeconds) {
        var cooldown = MainWindow.ResolveCircuitBreakerCooldown(openCount);

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), cooldown);
    }

    /// <summary>
    /// Ensures transient failures below threshold do not open the circuit.
    /// </summary>
    [Fact]
    public void RegisterCircuitTransientFailure_DoesNotOpenBeforeThreshold() {
        var nowUtc = new DateTime(2026, 2, 21, 12, 0, 0, DateTimeKind.Utc);

        var transition = MainWindow.RegisterCircuitTransientFailure(
            nowUtc,
            previousConsecutiveFailures: 1,
            previousOpenCount: 0,
            previousOpenUntilUtc: null);

        Assert.Equal(2, transition.ConsecutiveFailures);
        Assert.Equal(0, transition.OpenCount);
        Assert.False(transition.OpenedNow);
        Assert.Null(transition.OpenUntilUtc);
    }

    /// <summary>
    /// Ensures threshold crossing opens circuit and sets the first cooldown window.
    /// </summary>
    [Fact]
    public void RegisterCircuitTransientFailure_OpensAtThreshold() {
        var nowUtc = new DateTime(2026, 2, 21, 12, 0, 0, DateTimeKind.Utc);

        var transition = MainWindow.RegisterCircuitTransientFailure(
            nowUtc,
            previousConsecutiveFailures: 2,
            previousOpenCount: 0,
            previousOpenUntilUtc: null);

        Assert.Equal(3, transition.ConsecutiveFailures);
        Assert.Equal(1, transition.OpenCount);
        Assert.True(transition.OpenedNow);
        Assert.Equal(nowUtc.AddSeconds(12), transition.OpenUntilUtc);
    }

    /// <summary>
    /// Ensures open-window resolution reports remaining time while the circuit is active.
    /// </summary>
    [Fact]
    public void TryResolveCircuitOpenWindow_ReturnsRemainingWhenOpen() {
        var nowUtc = new DateTime(2026, 2, 21, 12, 0, 0, DateTimeKind.Utc);
        var openUntilUtc = nowUtc.AddSeconds(30);

        var isOpen = MainWindow.TryResolveCircuitOpenWindow(nowUtc, openUntilUtc, out var remaining);

        Assert.True(isOpen);
        Assert.Equal(TimeSpan.FromSeconds(30), remaining);
    }
}
