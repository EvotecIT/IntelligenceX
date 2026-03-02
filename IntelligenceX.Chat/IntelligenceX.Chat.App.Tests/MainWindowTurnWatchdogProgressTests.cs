using System;
using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Verifies long-turn watchdog progress hint mapping and threshold selection.
/// </summary>
public sealed class MainWindowTurnWatchdogProgressTests {
    /// <summary>
    /// Ensures watchdog hint-threshold selection favors earlier visibility before first status/token.
    /// </summary>
    [Theory]
    [InlineData(false, false, 8)]
    [InlineData(true, false, 12)]
    [InlineData(true, true, 20)]
    public void ResolveTurnWatchdogHintThreshold_ReturnsExpectedSeconds(
        bool hasFirstStatus,
        bool hasFirstDelta,
        int expectedSeconds) {
        var threshold = MainWindow.ResolveTurnWatchdogHintThreshold(hasFirstStatus, hasFirstDelta);
        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), threshold);
    }

    /// <summary>
    /// Ensures watchdog progress labels describe the current waiting phase.
    /// </summary>
    [Theory]
    [InlineData(false, false, false, false, null, "Waiting for runtime acknowledgement...")]
    [InlineData(true, false, false, false, "phase_execute", "Runtime acknowledged request. Waiting for first token...")]
    [InlineData(true, true, false, false, "model_selected", "Model selected. Waiting for first token...")]
    [InlineData(true, true, true, false, "tool_running", "Tool execution is running...")]
    [InlineData(true, true, true, true, "phase_execute", "Streaming response...")]
    public void BuildTurnWatchdogProgressLabel_ReturnsExpectedValue(
        bool hasFirstStatus,
        bool hasModelSelected,
        bool hasFirstToolRunning,
        bool hasFirstDelta,
        string? firstStatusCode,
        string expected) {
        var label = MainWindow.BuildTurnWatchdogProgressLabel(
            hasFirstStatus,
            hasModelSelected,
            hasFirstToolRunning,
            hasFirstDelta,
            firstStatusCode);

        Assert.Equal(expected, label);
    }

    /// <summary>
    /// Ensures watchdog status text includes activity context and elapsed seconds.
    /// </summary>
    [Fact]
    public void BuildTurnWatchdogStatusText_FormatsElapsedAndActivity() {
        var status = MainWindow.BuildTurnWatchdogStatusText(
            "Model selected. Waiting for first token...",
            elapsedSeconds: 27);

        Assert.Equal(
            "Runtime processing request... Model selected. Waiting for first token... (27s elapsed - press Stop to cancel)",
            status);
    }
}
