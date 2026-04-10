using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Guards tracked-account actions so native auth refreshes do not immediately rehydrate cleared usage state.
/// </summary>
public sealed class MainWindowAccountUsageActionTests {
    /// <summary>
    /// Ensures clearing tracked accounts does not immediately rehydrate native usage from a connected interactive-auth probe.
    /// </summary>
    [Fact]
    public void ShouldRefreshAuthenticationStateAfterClearingTrackedAccountUsage_SkipsConnectedNativeInteractiveSession() {
        var shouldRefresh = MainWindow.ShouldRefreshAuthenticationStateAfterClearingTrackedAccountUsage(
            isConnected: true,
            hasClient: true,
            requiresInteractiveSignIn: true);

        Assert.False(shouldRefresh);
    }

    /// <summary>
    /// Ensures non-interactive transports can still refresh auth state after clearing tracked accounts.
    /// </summary>
    [Fact]
    public void ShouldRefreshAuthenticationStateAfterClearingTrackedAccountUsage_AllowsConnectedNonInteractiveRefresh() {
        var shouldRefresh = MainWindow.ShouldRefreshAuthenticationStateAfterClearingTrackedAccountUsage(
            isConnected: true,
            hasClient: true,
            requiresInteractiveSignIn: false);

        Assert.True(shouldRefresh);
    }
}
