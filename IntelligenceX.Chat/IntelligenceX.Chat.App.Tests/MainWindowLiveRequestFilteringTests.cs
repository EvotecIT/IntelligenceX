using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Guards live-request filtering so stale request traffic does not mutate active assistant state.
/// </summary>
public sealed class MainWindowLiveRequestFilteringTests {
    /// <summary>
    /// Ensures request matching remains case-insensitive and whitespace-tolerant.
    /// </summary>
    [Fact]
    public void IsRequestIdMatch_TrimsAndMatchesCaseInsensitive() {
        var match = MainWindow.IsRequestIdMatch("  ReQ_123  ", "req_123");

        Assert.True(match);
    }

    /// <summary>
    /// Ensures blank request IDs and blank expected IDs never match.
    /// </summary>
    [Fact]
    public void IsRequestIdMatch_ReturnsFalseForBlankOrMissingExpectedId() {
        Assert.False(MainWindow.IsRequestIdMatch(" ", "req_123"));
        Assert.False(MainWindow.IsRequestIdMatch("req_123", " "));
    }

    /// <summary>
    /// Ensures active turn traffic is accepted for live request processing.
    /// </summary>
    [Fact]
    public void ShouldProcessLiveRequestMessage_AcceptsActiveTurnRequestId() {
        var shouldProcess = MainWindow.ShouldProcessLiveRequestMessage(
            requestId: "req_active_turn",
            activeTurnRequestId: "REQ_ACTIVE_TURN",
            activeKickoffRequestId: "req_kickoff",
            isSending: false,
            modelKickoffInProgress: false);

        Assert.True(shouldProcess);
    }

    /// <summary>
    /// Ensures kickoff request traffic remains accepted while kickoff is active.
    /// </summary>
    [Fact]
    public void ShouldProcessLiveRequestMessage_AcceptsActiveKickoffRequestId() {
        var shouldProcess = MainWindow.ShouldProcessLiveRequestMessage(
            requestId: "req_kickoff",
            activeTurnRequestId: "req_active_turn",
            activeKickoffRequestId: "REQ_KICKOFF",
            isSending: false,
            modelKickoffInProgress: false);

        Assert.True(shouldProcess);
    }

    /// <summary>
    /// Ensures stale request IDs are rejected even when a turn is active.
    /// </summary>
    [Fact]
    public void ShouldProcessLiveRequestMessage_RejectsStaleRequestId() {
        var shouldProcess = MainWindow.ShouldProcessLiveRequestMessage(
            requestId: "req_stale",
            activeTurnRequestId: "req_active_turn",
            activeKickoffRequestId: "req_kickoff",
            isSending: true,
            modelKickoffInProgress: true);

        Assert.False(shouldProcess);
    }

    /// <summary>
    /// Ensures blank request IDs are accepted only during active send or kickoff phases.
    /// </summary>
    [Fact]
    public void ShouldProcessLiveRequestMessage_BlankRequestIdOnlyAllowedWhenTurnOrKickoffInFlight() {
        var allowedWhileSending = MainWindow.ShouldProcessLiveRequestMessage(
            requestId: " ",
            activeTurnRequestId: "req_active_turn",
            activeKickoffRequestId: "req_kickoff",
            isSending: true,
            modelKickoffInProgress: false);

        var allowedWhileKickoffRunning = MainWindow.ShouldProcessLiveRequestMessage(
            requestId: null,
            activeTurnRequestId: "req_active_turn",
            activeKickoffRequestId: "req_kickoff",
            isSending: false,
            modelKickoffInProgress: true);

        var blockedWhenIdle = MainWindow.ShouldProcessLiveRequestMessage(
            requestId: "",
            activeTurnRequestId: "req_active_turn",
            activeKickoffRequestId: "req_kickoff",
            isSending: false,
            modelKickoffInProgress: false);

        Assert.True(allowedWhileSending);
        Assert.True(allowedWhileKickoffRunning);
        Assert.False(blockedWhenIdle);
    }
}
