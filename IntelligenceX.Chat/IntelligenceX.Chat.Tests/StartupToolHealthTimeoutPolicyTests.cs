using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Service;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class StartupToolHealthTimeoutPolicyTests {
    [Theory]
    [InlineData(0, ToolPackSourceKind.OpenSource, 4)]
    [InlineData(0, ToolPackSourceKind.Builtin, 4)]
    [InlineData(0, ToolPackSourceKind.ClosedSource, 8)]
    public void ResolveStartupToolHealthTimeoutSeconds_UsesSafeDefaultsPerSourceKind(
        int configuredTimeoutSeconds,
        ToolPackSourceKind sourceKind,
        int expectedTimeoutSeconds) {
        var timeout = ChatServiceSession.ResolveStartupToolHealthTimeoutSeconds(configuredTimeoutSeconds, sourceKind);

        Assert.Equal(expectedTimeoutSeconds, timeout);
    }

    [Theory]
    [InlineData(1, ToolPackSourceKind.OpenSource, 2)]
    [InlineData(60, ToolPackSourceKind.OpenSource, 10)]
    [InlineData(1, ToolPackSourceKind.ClosedSource, 4)]
    [InlineData(60, ToolPackSourceKind.ClosedSource, 20)]
    public void ResolveStartupToolHealthTimeoutSeconds_ClampsConfiguredRanges(
        int configuredTimeoutSeconds,
        ToolPackSourceKind sourceKind,
        int expectedTimeoutSeconds) {
        var timeout = ChatServiceSession.ResolveStartupToolHealthTimeoutSeconds(configuredTimeoutSeconds, sourceKind);

        Assert.Equal(expectedTimeoutSeconds, timeout);
    }

    [Theory]
    [InlineData(4, ToolPackSourceKind.OpenSource, 8)]
    [InlineData(10, ToolPackSourceKind.OpenSource, 12)]
    [InlineData(8, ToolPackSourceKind.ClosedSource, 16)]
    [InlineData(20, ToolPackSourceKind.ClosedSource, 30)]
    public void ResolveStartupToolHealthRetryTimeoutSeconds_IncreasesWithBoundedCap(
        int initialTimeoutSeconds,
        ToolPackSourceKind sourceKind,
        int expectedTimeoutSeconds) {
        var timeout = ChatServiceSession.ResolveStartupToolHealthRetryTimeoutSeconds(initialTimeoutSeconds, sourceKind);

        Assert.Equal(expectedTimeoutSeconds, timeout);
    }

    [Theory]
    [InlineData(0, ToolPackSourceKind.ClosedSource, "testimox", 12)]
    [InlineData(2, ToolPackSourceKind.ClosedSource, "testimox", 12)]
    [InlineData(60, ToolPackSourceKind.ClosedSource, "testimox", 30)]
    public void ResolveStartupToolHealthTimeoutSeconds_UsesHigherFloorForTestimoX(
        int configuredTimeoutSeconds,
        ToolPackSourceKind sourceKind,
        string packId,
        int expectedTimeoutSeconds) {
        var timeout = ChatServiceSession.ResolveStartupToolHealthTimeoutSeconds(configuredTimeoutSeconds, sourceKind, packId);

        Assert.Equal(expectedTimeoutSeconds, timeout);
    }

    [Theory]
    [InlineData(0, ToolPackSourceKind.ClosedSource, "testimox", 20)]
    [InlineData(8, ToolPackSourceKind.ClosedSource, "testimox", 20)]
    [InlineData(24, ToolPackSourceKind.ClosedSource, "testimox", 30)]
    public void ResolveStartupToolHealthRetryTimeoutSeconds_UsesHigherFloorForTestimoX(
        int initialTimeoutSeconds,
        ToolPackSourceKind sourceKind,
        string packId,
        int expectedTimeoutSeconds) {
        var timeout = ChatServiceSession.ResolveStartupToolHealthRetryTimeoutSeconds(initialTimeoutSeconds, sourceKind, packId);

        Assert.Equal(expectedTimeoutSeconds, timeout);
    }

    [Theory]
    [InlineData(ToolPackSourceKind.ClosedSource, "tool_timeout", true)]
    [InlineData(ToolPackSourceKind.ClosedSource, "access_denied", false)]
    [InlineData(ToolPackSourceKind.OpenSource, "tool_timeout", false)]
    public void ShouldDowngradeStartupToolHealthFailure_OnlyClosedSourceTimeouts(
        ToolPackSourceKind sourceKind,
        string errorCode,
        bool expected) {
        var result = ChatServiceSession.ShouldDowngradeStartupToolHealthFailure(sourceKind, errorCode);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void ShouldSkipStartupToolHealthProbe_TrueWhenNextProbeIsInFuture() {
        var now = new DateTime(2026, 2, 17, 12, 0, 0, DateTimeKind.Utc);
        var nextProbe = now.AddMinutes(5);

        var result = ChatServiceSession.ShouldSkipStartupToolHealthProbe(now, nextProbe);

        Assert.True(result);
    }

    [Fact]
    public void ShouldSkipStartupToolHealthProbe_FalseWhenNextProbeIsDue() {
        var now = new DateTime(2026, 2, 17, 12, 0, 0, DateTimeKind.Utc);
        var nextProbe = now.AddMinutes(-1);

        var result = ChatServiceSession.ShouldSkipStartupToolHealthProbe(now, nextProbe);

        Assert.False(result);
    }

    [Fact]
    public void ComputeNextStartupToolHealthProbeUtc_UsesLongerBackoffForTestimoXTimeouts() {
        var now = new DateTime(2026, 2, 17, 12, 0, 0, DateTimeKind.Utc);

        var next = ChatServiceSession.ComputeNextStartupToolHealthProbeUtc(
            nowUtc: now,
            sourceKind: ToolPackSourceKind.ClosedSource,
            packId: "testimox",
            errorCode: "tool_timeout",
            consecutiveFailures: 1);

        Assert.Equal(now.AddMinutes(20), next);
    }

    [Fact]
    public void ComputeNextStartupToolHealthProbeUtc_ExponentiallyBacksOffOnRepeatedFailures() {
        var now = new DateTime(2026, 2, 17, 12, 0, 0, DateTimeKind.Utc);

        var next = ChatServiceSession.ComputeNextStartupToolHealthProbeUtc(
            nowUtc: now,
            sourceKind: ToolPackSourceKind.OpenSource,
            packId: "system",
            errorCode: "tool_timeout",
            consecutiveFailures: 3);

        Assert.Equal(now.AddMinutes(20), next);
    }
}
