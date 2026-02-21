using IntelligenceX.Chat.App;
using IntelligenceX.Chat.App.Conversation;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Ensures provider transient-failure classification remains stable for circuit policy.
/// </summary>
public sealed class MainWindowTransientFailureClassificationTests {
    /// <summary>
    /// Ensures disconnect outcomes always count as transient provider failures.
    /// </summary>
    [Fact]
    public void ShouldCountAsTransientProviderFailure_ReturnsTrue_ForDisconnectedOutcome() {
        var result = MainWindow.ShouldCountAsTransientProviderFailure(AssistantTurnOutcome.Disconnected());

        Assert.True(result);
    }

    /// <summary>
    /// Ensures timeout-like transport errors are treated as transient.
    /// </summary>
    [Theory]
    [InlineData("Timed out waiting for service pipe.")]
    [InlineData("Connection reset by peer.")]
    [InlineData("Service temporarily unavailable (503).")]
    public void ShouldCountAsTransientProviderFailure_ReturnsTrue_ForTransientErrorMessages(string detail) {
        var result = MainWindow.ShouldCountAsTransientProviderFailure(AssistantTurnOutcome.Error(detail));

        Assert.True(result);
    }

    /// <summary>
    /// Ensures expected non-transient outcomes do not trip the provider circuit.
    /// </summary>
    [Fact]
    public void ShouldCountAsTransientProviderFailure_ReturnsFalse_ForUsageLimitAndAuthErrors() {
        Assert.False(MainWindow.ShouldCountAsTransientProviderFailure(AssistantTurnOutcome.UsageLimit("usage limit reached")));
        Assert.False(MainWindow.ShouldCountAsTransientProviderFailure(AssistantTurnOutcome.Error("not authenticated")));
    }
}
