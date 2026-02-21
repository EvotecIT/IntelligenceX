using System;
using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Guards latency percentile aggregation used by session reliability diagnostics.
/// </summary>
public sealed class MainWindowLatencyPercentilePolicyTests {
    /// <summary>
    /// Ensures missing samples produce no percentile output.
    /// </summary>
    [Fact]
    public void ComputeLatencyPercentiles_ReturnsNulls_WhenNoSamples() {
        var (p50, p95) = MainWindow.ComputeLatencyPercentiles(Array.Empty<long>());

        Assert.Null(p50);
        Assert.Null(p95);
    }

    /// <summary>
    /// Ensures percentile calculation is order-insensitive and uses nearest-rank selection.
    /// </summary>
    [Fact]
    public void ComputeLatencyPercentiles_ReturnsExpectedNearestRankValues() {
        var (p50, p95) = MainWindow.ComputeLatencyPercentiles(new long[] { 450, 120, 300, 800, 50 });

        Assert.Equal(300L, p50);
        Assert.Equal(800L, p95);
    }

    /// <summary>
    /// Ensures non-positive samples are ignored so telemetry noise does not skew percentiles.
    /// </summary>
    [Fact]
    public void ComputeLatencyPercentiles_IgnoresNonPositiveSamples() {
        var (p50, p95) = MainWindow.ComputeLatencyPercentiles(new long[] { 0, -1, -20 });

        Assert.Null(p50);
        Assert.Null(p95);
    }
}
