using System;
using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Tests startup connect timeout policy selection for initial pipe probes.
/// </summary>
public sealed class MainWindowStartupConnectTimeoutPolicyTests {
    /// <summary>
    /// Ensures startup connect initial probe timeout is short on cold start,
    /// and remains conservative for user action or known-running sidecar reconnects.
    /// </summary>
    [Theory]
    [InlineData(true, true, 2000)]
    [InlineData(true, false, 2000)]
    [InlineData(false, true, 2000)]
    [InlineData(false, false, 350)]
    public void ResolveStartupInitialPipeConnectTimeout_ReturnsExpectedTimeout(
        bool fromUserAction,
        bool hasTrackedRunningServiceProcess,
        int expectedTimeoutMs) {
        var timeout = MainWindow.ResolveStartupInitialPipeConnectTimeout(fromUserAction, hasTrackedRunningServiceProcess);
        Assert.Equal(TimeSpan.FromMilliseconds(expectedTimeoutMs), timeout);
    }

    /// <summary>
    /// Ensures model/profile sync is deferred only during startup flow telemetry capture.
    /// </summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void ShouldDeferStartupModelProfileSync_ReturnsExpectedValue(
        bool captureStartupPhaseTelemetry,
        bool expected) {
        var shouldDefer = MainWindow.ShouldDeferStartupModelProfileSync(captureStartupPhaseTelemetry);
        Assert.Equal(expected, shouldDefer);
    }

    /// <summary>
    /// Ensures startup hello probe is deferred only during startup flow telemetry capture.
    /// </summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void ShouldDeferStartupHelloProbe_ReturnsExpectedValue(
        bool captureStartupPhaseTelemetry,
        bool expected) {
        var shouldDefer = MainWindow.ShouldDeferStartupHelloProbe(captureStartupPhaseTelemetry);
        Assert.Equal(expected, shouldDefer);
    }

    /// <summary>
    /// Ensures startup tool-catalog sync is deferred only during startup flow telemetry capture.
    /// </summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void ShouldDeferStartupToolCatalogSync_ReturnsExpectedValue(
        bool captureStartupPhaseTelemetry,
        bool expected) {
        var shouldDefer = MainWindow.ShouldDeferStartupToolCatalogSync(captureStartupPhaseTelemetry);
        Assert.Equal(expected, shouldDefer);
    }

    /// <summary>
    /// Ensures startup auth refresh is deferred only during startup flow telemetry capture.
    /// </summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void ShouldDeferStartupAuthRefresh_ReturnsExpectedValue(
        bool captureStartupPhaseTelemetry,
        bool expected) {
        var shouldDefer = MainWindow.ShouldDeferStartupAuthRefresh(captureStartupPhaseTelemetry);
        Assert.Equal(expected, shouldDefer);
    }
}
