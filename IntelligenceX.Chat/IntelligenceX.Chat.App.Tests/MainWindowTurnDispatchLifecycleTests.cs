using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Verifies turn-dispatch lifecycle state transitions used by queue/send paths.
/// </summary>
public sealed class MainWindowTurnDispatchLifecycleTests {
    /// <summary>
    /// Busy-state helper should report sending and startup states consistently.
    /// </summary>
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void IsTurnDispatchInProgress_ReturnsExpectedValue(
        bool isSending,
        bool turnStartupInProgress,
        bool expected) {
        var actual = MainWindow.IsTurnDispatchInProgress(isSending, turnStartupInProgress);

        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Idle dispatch should claim startup ownership exactly once.
    /// </summary>
    [Fact]
    public void TryBeginTurnDispatchStartup_ClaimsStartup_WhenIdle() {
        var isSending = false;
        var turnStartupInProgress = false;

        var claimed = MainWindow.TryBeginTurnDispatchStartup(ref isSending, ref turnStartupInProgress);

        Assert.True(claimed);
        Assert.False(isSending);
        Assert.True(turnStartupInProgress);
    }

    /// <summary>
    /// Startup claim must be rejected when a turn is already active.
    /// </summary>
    [Fact]
    public void TryBeginTurnDispatchStartup_ReturnsFalse_WhenAlreadySending() {
        var isSending = true;
        var turnStartupInProgress = false;

        var claimed = MainWindow.TryBeginTurnDispatchStartup(ref isSending, ref turnStartupInProgress);

        Assert.False(claimed);
        Assert.True(isSending);
        Assert.False(turnStartupInProgress);
    }

    /// <summary>
    /// Promotion should keep dispatch busy while moving startup into active send.
    /// </summary>
    [Fact]
    public void PromoteTurnDispatchStartupToSending_PromotesWithoutIdleGap() {
        var isSending = false;
        var turnStartupInProgress = false;
        Assert.True(MainWindow.TryBeginTurnDispatchStartup(ref isSending, ref turnStartupInProgress));
        Assert.True(MainWindow.IsTurnDispatchInProgress(isSending, turnStartupInProgress));

        MainWindow.PromoteTurnDispatchStartupToSending(ref isSending, ref turnStartupInProgress);

        Assert.True(isSending);
        Assert.False(turnStartupInProgress);
        Assert.True(MainWindow.IsTurnDispatchInProgress(isSending, turnStartupInProgress));
    }

    /// <summary>
    /// Startup clear should only report true when a pending startup state existed.
    /// </summary>
    [Fact]
    public void TryClearTurnDispatchStartup_ReturnsExpectedValue() {
        var turnStartupInProgress = true;
        var cleared = MainWindow.TryClearTurnDispatchStartup(ref turnStartupInProgress);

        Assert.True(cleared);
        Assert.False(turnStartupInProgress);

        cleared = MainWindow.TryClearTurnDispatchStartup(ref turnStartupInProgress);
        Assert.False(cleared);
        Assert.False(turnStartupInProgress);
    }

    /// <summary>
    /// Completed turns should restore normal connection status for stale startup/loading text,
    /// but only when startup metadata sync is no longer active.
    /// </summary>
    [Theory]
    [InlineData("Sending request to runtime...", true, false, false, 2, true)]
    [InlineData("Last turn failed: provider unavailable", true, false, false, 2, true)]
    [InlineData("Runtime connected. Loading tool packs in background... (phase startup_metadata_sync, cause metadata_sync)", true, false, false, 2, true)]
    [InlineData("Starting runtime... loading tool packs 3/10 (eventlog)", true, false, false, 2, true)]
    [InlineData("Runtime connected. Finish sign-in in browser to continue loading tool packs... (phase startup_auth_wait, cause auth_wait)", true, false, false, 2, false)]
    [InlineData("Runtime connected. Loading tool packs in background... (phase startup_metadata_sync, cause metadata_sync)", true, true, false, 2, false)]
    [InlineData("Runtime connected. Loading tool packs in background... (phase startup_metadata_sync, cause metadata_sync)", true, false, true, 2, false)]
    [InlineData("Runtime connected. Loading tool packs in background... (phase startup_metadata_sync, cause metadata_sync)", true, false, false, 1, false)]
    [InlineData("Sending request to runtime...", false, false, false, 2, false)]
    public void ShouldRestoreConnectionStatusAfterTurn_ReturnsExpectedValue(
        string status,
        bool isLatestTurnRequest,
        bool startupMetadataSyncQueued,
        bool startupMetadataSyncInProgress,
        int startupFlowState,
        bool expected) {
        var shouldRestore = MainWindow.ShouldRestoreConnectionStatusAfterTurn(
            currentStatus: status,
            isLatestTurnRequest: isLatestTurnRequest,
            startupMetadataSyncQueued: startupMetadataSyncQueued,
            startupMetadataSyncInProgress: startupMetadataSyncInProgress,
            startupFlowState: startupFlowState);

        Assert.Equal(expected, shouldRestore);
    }
}
