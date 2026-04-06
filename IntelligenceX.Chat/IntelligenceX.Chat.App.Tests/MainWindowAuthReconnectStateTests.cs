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

    /// <summary>
    /// Invalidates cached ensure-login probe state when authoritative auth context changes.
    /// </summary>
    [Theory]
    [InlineData(true, true, false, false, true)]
    [InlineData(true, false, true, false, true)]
    [InlineData(true, false, false, true, true)]
    [InlineData(false, false, false, true, false)]
    [InlineData(true, false, false, false, false)]
    public void ShouldResetEnsureLoginProbeCacheForAuthContextChange_ReturnsExpectedValue(
        bool requiresInteractiveSignIn,
        bool loginCompletedSuccessfully,
        bool transportChanged,
        bool runtimeExited,
        bool expected) {
        var result = MainWindow.ShouldResetEnsureLoginProbeCacheForAuthContextChange(
            requiresInteractiveSignIn: requiresInteractiveSignIn,
            loginCompletedSuccessfully: loginCompletedSuccessfully,
            transportChanged: transportChanged,
            runtimeExited: runtimeExited);

        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Exposes explicit unauthenticated probe state only while it remains relevant to native auth UX.
    /// </summary>
    [Theory]
    [InlineData(true, false, false, true, false, true)]
    [InlineData(true, true, false, true, false, false)]
    [InlineData(true, false, true, true, false, false)]
    [InlineData(false, false, false, true, false, false)]
    [InlineData(true, false, false, false, false, false)]
    [InlineData(true, false, false, true, true, false)]
    public void ShouldExposeExplicitUnauthenticatedEnsureLoginProbeSnapshot_ReturnsExpectedValue(
        bool requiresInteractiveSignIn,
        bool isAuthenticated,
        bool loginInProgress,
        bool probeCacheHasValue,
        bool probeCachedIsAuthenticated,
        bool expected) {
        var result = MainWindow.ShouldExposeExplicitUnauthenticatedEnsureLoginProbeSnapshot(
            requiresInteractiveSignIn: requiresInteractiveSignIn,
            isAuthenticated: isAuthenticated,
            loginInProgress: loginInProgress,
            probeCacheHasValue: probeCacheHasValue,
            probeCachedIsAuthenticated: probeCachedIsAuthenticated);

        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Distinguishes settled sign-in requirements from the transient "still verifying" startup state.
    /// </summary>
    [Theory]
    [InlineData(false, true, false, false, false, 2)]
    [InlineData(true, false, false, false, false, 0)]
    [InlineData(true, true, false, false, false, 0)]
    [InlineData(true, true, false, true, false, 6)]
    [InlineData(true, true, false, false, true, 1)]
    public void ResolveConnectionStatus_ReturnsExpectedKind(
        bool isConnected,
        bool requiresInteractiveSignIn,
        bool isAuthenticated,
        bool loginInProgress,
        bool hasExplicitUnauthenticatedProbeSnapshot,
        int expectedKind) {
        var result = MainWindow.ResolveConnectionStatus(
            isConnected: isConnected,
            requiresInteractiveSignIn: requiresInteractiveSignIn,
            isAuthenticated: isAuthenticated,
            loginInProgress: loginInProgress,
            hasExplicitUnauthenticatedProbeSnapshot: hasExplicitUnauthenticatedProbeSnapshot);

        Assert.Equal(expectedKind, (int)result.Kind);
    }
}
