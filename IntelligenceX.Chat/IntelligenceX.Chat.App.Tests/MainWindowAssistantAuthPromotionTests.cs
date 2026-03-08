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

    /// <summary>
    /// Ensures switching to an old conversation can opportunistically clear a stale auth badge.
    /// </summary>
    [Fact]
    public void ShouldRefreshAuthenticationStateAfterConversationSwitch_PromotesForConnectedNativeSessionWithoutExplicitUnauthenticatedProbe() {
        var result = MainWindow.ShouldRefreshAuthenticationStateAfterConversationSwitch(
            requiresInteractiveSignIn: true,
            isConnected: true,
            isAuthenticated: false,
            loginInProgress: false,
            hasExplicitUnauthenticatedProbeSnapshot: false);

        Assert.True(result);
    }

    /// <summary>
    /// Ensures conversation switches do not hide an explicit unauthenticated state.
    /// </summary>
    [Theory]
    [InlineData(false, true, false, false, false)]
    [InlineData(true, false, false, false, false)]
    [InlineData(true, true, true, false, false)]
    [InlineData(true, true, false, true, false)]
    [InlineData(true, true, false, false, true)]
    public void ShouldRefreshAuthenticationStateAfterConversationSwitch_StaysFalseWhenRefreshWouldBeUnsafe(
        bool requiresInteractiveSignIn,
        bool isConnected,
        bool isAuthenticated,
        bool loginInProgress,
        bool hasExplicitUnauthenticatedProbeSnapshot) {
        var result = MainWindow.ShouldRefreshAuthenticationStateAfterConversationSwitch(
            requiresInteractiveSignIn,
            isConnected,
            isAuthenticated,
            loginInProgress,
            hasExplicitUnauthenticatedProbeSnapshot);

        Assert.False(result);
    }
}
