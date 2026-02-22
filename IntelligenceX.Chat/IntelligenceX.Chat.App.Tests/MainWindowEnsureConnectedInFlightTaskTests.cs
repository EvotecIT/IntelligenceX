using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Verifies lifecycle reset behavior for shared in-flight connect task tracking.
/// </summary>
public sealed class MainWindowEnsureConnectedInFlightTaskTests {
    /// <summary>
    /// Owner-created in-flight connect attempts must always clear in finally,
    /// while joined in-flight attempts clear only after completion.
    /// </summary>
    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    [InlineData(false, false, false)]
    [InlineData(false, true, true)]
    public void ShouldResetEnsureConnectedInFlightTask_ReturnsExpectedValue(
        bool startedNewInFlightTask,
        bool connectAttemptTaskCompleted,
        bool expected) {
        var shouldReset = MainWindow.ShouldResetEnsureConnectedInFlightTask(
            startedNewInFlightTask,
            connectAttemptTaskCompleted);

        Assert.Equal(expected, shouldReset);
    }
}
