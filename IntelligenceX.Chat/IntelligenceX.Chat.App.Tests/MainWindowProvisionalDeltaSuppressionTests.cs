using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Guards delta suppression so provisional mode does not drop early chat_delta content.
/// </summary>
public sealed class MainWindowProvisionalDeltaSuppressionTests {
    [Fact]
    public void ShouldSuppressChatDeltaWhenProvisionalPreferred_DoesNotSuppressBeforeFirstProvisionalFragment() {
        var suppress = MainWindow.ShouldSuppressChatDeltaWhenProvisionalPreferred(
            provisionalModePreferredForTurn: true,
            hasReceivedProvisionalFragment: false);

        Assert.False(suppress);
    }

    [Fact]
    public void ShouldSuppressChatDeltaWhenProvisionalPreferred_SuppressesAfterFirstProvisionalFragment() {
        var suppress = MainWindow.ShouldSuppressChatDeltaWhenProvisionalPreferred(
            provisionalModePreferredForTurn: true,
            hasReceivedProvisionalFragment: true);

        Assert.True(suppress);
    }

    [Fact]
    public void ShouldSuppressChatDeltaWhenProvisionalPreferred_DoesNotSuppressWhenProvisionalModeIsOff() {
        var suppress = MainWindow.ShouldSuppressChatDeltaWhenProvisionalPreferred(
            provisionalModePreferredForTurn: false,
            hasReceivedProvisionalFragment: true);

        Assert.False(suppress);
    }
}
