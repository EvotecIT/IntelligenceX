using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Verifies auth-state promotion rules after successful assistant output.
/// </summary>
public sealed class MainWindowAssistantAuthPromotionTests {
    /// <summary>
    /// Ensures a successful native-runtime assistant response can clear a stale unauthenticated badge.
    /// </summary>
    [Fact]
    public void ShouldPromoteAuthenticatedStateFromFinalAssistantTurn_PromotesWhenNativeConnectedAndNotAuthenticated() {
        var result = MainWindow.ShouldPromoteAuthenticatedStateFromFinalAssistantTurn(
            requiresInteractiveSignIn: true,
            isConnected: true,
            isAuthenticated: false,
            loginInProgress: false);

        Assert.True(result);
    }

    /// <summary>
    /// Ensures non-native transports and already-authenticated sessions are left unchanged.
    /// </summary>
    [Theory]
    [InlineData(false, true, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, true, false)]
    [InlineData(true, true, false, true)]
    public void ShouldPromoteAuthenticatedStateFromFinalAssistantTurn_StaysFalseWhenPromotionWouldBeUnsafe(
        bool requiresInteractiveSignIn,
        bool isConnected,
        bool isAuthenticated,
        bool loginInProgress) {
        var result = MainWindow.ShouldPromoteAuthenticatedStateFromFinalAssistantTurn(
            requiresInteractiveSignIn,
            isConnected,
            isAuthenticated,
            loginInProgress);

        Assert.False(result);
    }
}
