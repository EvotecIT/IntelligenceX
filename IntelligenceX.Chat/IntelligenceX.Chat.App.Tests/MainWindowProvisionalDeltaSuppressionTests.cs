using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Guards delta suppression so provisional mode does not drop early chat_delta content.
/// </summary>
public sealed class MainWindowProvisionalDeltaSuppressionTests {
    /// <summary>
    /// Ensures chat deltas are still accepted before any provisional fragment appears.
    /// </summary>
    [Fact]
    public void ShouldSuppressChatDeltaWhenProvisionalPreferred_DoesNotSuppressBeforeFirstProvisionalFragment() {
        var suppress = MainWindow.ShouldSuppressChatDeltaWhenProvisionalPreferred(
            provisionalModePreferredForTurn: true,
            hasReceivedProvisionalFragment: false);

        Assert.False(suppress);
    }

    /// <summary>
    /// Ensures chat deltas are suppressed once provisional mode is active and provisional text arrived.
    /// </summary>
    [Fact]
    public void ShouldSuppressChatDeltaWhenProvisionalPreferred_SuppressesAfterFirstProvisionalFragment() {
        var suppress = MainWindow.ShouldSuppressChatDeltaWhenProvisionalPreferred(
            provisionalModePreferredForTurn: true,
            hasReceivedProvisionalFragment: true);

        Assert.True(suppress);
    }

    /// <summary>
    /// Ensures suppression stays disabled when the turn is not using provisional-preferred mode.
    /// </summary>
    [Fact]
    public void ShouldSuppressChatDeltaWhenProvisionalPreferred_DoesNotSuppressWhenProvisionalModeIsOff() {
        var suppress = MainWindow.ShouldSuppressChatDeltaWhenProvisionalPreferred(
            provisionalModePreferredForTurn: false,
            hasReceivedProvisionalFragment: true);

        Assert.False(suppress);
    }
}
