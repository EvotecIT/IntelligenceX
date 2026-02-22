using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Verifies turn-latency notice thresholds and rendered diagnostics text.
/// </summary>
public sealed class MainWindowTurnLatencyNoticeTests {
    /// <summary>
    /// Ensures slow-turn and first-turn threshold policy stays stable.
    /// </summary>
    [Theory]
    [InlineData(500, false, false)]
    [InlineData(1100, true, false)]
    [InlineData(1200, true, true)]
    [InlineData(2000, true, true)]
    [InlineData(4499, false, false)]
    [InlineData(4500, false, true)]
    public void ShouldEmitTurnLatencySystemNotice_ReturnsExpectedValue(
        long durationMs,
        bool emitForFirstTurn,
        bool expected) {
        var shouldEmit = MainWindow.ShouldEmitTurnLatencySystemNotice(durationMs, emitForFirstTurn);
        Assert.Equal(expected, shouldEmit);
    }

    /// <summary>
    /// Ensures first-turn diagnostics use dedicated prefix and dominant-phase classification.
    /// </summary>
    [Fact]
    public void BuildTurnLatencySystemNotice_FormatsFirstTurnNotice() {
        var text = MainWindow.BuildTurnLatencySystemNotice(
            durationMs: 2400,
            queueWaitMs: 80,
            authProbeMs: 70,
            connectMs: 50,
            dispatchToFirstDeltaMs: 1400,
            streamDurationMs: 700,
            firstTurnNotice: true);

        Assert.StartsWith("First turn latency: total 2400ms", text);
        Assert.Contains("dominant: model/runtime.", text);
    }

    /// <summary>
    /// Ensures slow-turn diagnostics keep the slow-turn prefix and classify preflight dominance.
    /// </summary>
    [Fact]
    public void BuildTurnLatencySystemNotice_FormatsSlowTurnNotice() {
        var text = MainWindow.BuildTurnLatencySystemNotice(
            durationMs: 5000,
            queueWaitMs: 300,
            authProbeMs: 300,
            connectMs: 300,
            dispatchToFirstDeltaMs: 1000,
            streamDurationMs: 3000);

        Assert.StartsWith("Slow turn: total 5000ms", text);
        Assert.Contains("dominant: preflight.", text);
    }
}
