using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Verifies interactive-auth reconnect state preservation decisions.
/// </summary>
public sealed class MainWindowAuthReconnectStateTests {
    /// <summary>
    /// Preserves state while interactive login is in progress.
    /// </summary>
    [Fact]
    public void ShouldPreserveInteractiveAuthStateOnReconnect_PreservesWhenLoginInProgress() {
        var result = MainWindow.ShouldPreserveInteractiveAuthStateOnReconnect(
            requiresInteractiveSignIn: true,
            isAuthenticated: false,
            hasExplicitUnauthenticatedProbeSnapshot: true,
            loginInProgress: true);

        Assert.True(result);
    }

    /// <summary>
    /// Preserves state when we were authenticated and have no explicit unauthenticated probe.
    /// </summary>
    [Fact]
    public void ShouldPreserveInteractiveAuthStateOnReconnect_PreservesAuthenticatedWithoutExplicitUnauthenticatedProbe() {
        var result = MainWindow.ShouldPreserveInteractiveAuthStateOnReconnect(
            requiresInteractiveSignIn: true,
            isAuthenticated: true,
            hasExplicitUnauthenticatedProbeSnapshot: false,
            loginInProgress: false);

        Assert.True(result);
    }

    /// <summary>
    /// Avoids preserving stale authenticated state when an explicit unauthenticated probe exists.
    /// </summary>
    [Fact]
    public void ShouldPreserveInteractiveAuthStateOnReconnect_DoesNotPreserveWithExplicitUnauthenticatedProbe() {
        var result = MainWindow.ShouldPreserveInteractiveAuthStateOnReconnect(
            requiresInteractiveSignIn: true,
            isAuthenticated: true,
            hasExplicitUnauthenticatedProbeSnapshot: true,
            loginInProgress: false);

        Assert.False(result);
    }

    /// <summary>
    /// Never preserves interactive state for non-interactive transports.
    /// </summary>
    [Fact]
    public void ShouldPreserveInteractiveAuthStateOnReconnect_DoesNotPreserveForNonInteractiveTransport() {
        var result = MainWindow.ShouldPreserveInteractiveAuthStateOnReconnect(
            requiresInteractiveSignIn: false,
            isAuthenticated: true,
            hasExplicitUnauthenticatedProbeSnapshot: false,
            loginInProgress: false);

        Assert.False(result);
    }

    /// <summary>
    /// Resets cached unauthenticated/authenticated probe state whenever reconnect logic clears auth state.
    /// </summary>
    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void ShouldResetEnsureLoginProbeCacheOnReconnectAuthReset_ReturnsExpectedValue(
        bool requiresInteractiveSignIn,
        bool preserveInteractiveAuthState,
        bool expected) {
        var result = MainWindow.ShouldResetEnsureLoginProbeCacheOnReconnectAuthReset(
            requiresInteractiveSignIn: requiresInteractiveSignIn,
            preserveInteractiveAuthState: preserveInteractiveAuthState);

        Assert.Equal(expected, result);
    }
}
