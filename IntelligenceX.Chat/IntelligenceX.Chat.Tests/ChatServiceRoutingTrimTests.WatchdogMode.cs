using IntelligenceX.Chat.Service;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed partial class ChatServiceRoutingTrimTests {
    [Fact]
    public void ShouldTraceNoToolExecutionWatchdogDecision_TracksContractTurns() {
        var shouldTrace = ChatServiceSession.ShouldTraceNoToolExecutionWatchdogDecision(
            executionContractApplies: true,
            continuationFollowUpTurn: false,
            compactFollowUpTurn: false);

        Assert.True(shouldTrace);
    }

    [Fact]
    public void ShouldTraceNoToolExecutionWatchdogDecision_SkipsWhenNoContractAndNoFollowUpSignals() {
        var shouldTrace = ChatServiceSession.ShouldTraceNoToolExecutionWatchdogDecision(
            executionContractApplies: false,
            continuationFollowUpTurn: false,
            compactFollowUpTurn: false);

        Assert.False(shouldTrace);
    }

    [Fact]
    public void ResolveNoToolExecutionWatchdogMode_PrefersContractWhenExecutionContractApplies() {
        var mode = ChatServiceSession.ResolveNoToolExecutionWatchdogMode(
            executionContractApplies: true,
            continuationFollowUpTurn: true,
            compactFollowUpTurn: true);

        Assert.Equal("contract", mode);
    }

    [Fact]
    public void ResolveNoToolExecutionWatchdogMode_PrefersCompactFollowUpWhenBothFollowUpFlagsAreTrue() {
        var mode = ChatServiceSession.ResolveNoToolExecutionWatchdogMode(
            executionContractApplies: false,
            continuationFollowUpTurn: true,
            compactFollowUpTurn: true);

        Assert.Equal("compact_follow_up", mode);
    }

    [Fact]
    public void ResolveNoToolExecutionWatchdogMode_UsesContinuationWhenCompactFollowUpIsFalse() {
        var mode = ChatServiceSession.ResolveNoToolExecutionWatchdogMode(
            executionContractApplies: false,
            continuationFollowUpTurn: true,
            compactFollowUpTurn: false);

        Assert.Equal("follow_up", mode);
    }
}
