using IntelligenceX.Chat.Abstractions.Storage;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Verifies that desktop persistence surfaces consume the shared state-path owner.
/// </summary>
public sealed class ChatStatePathParityTests {
    /// <summary>
    /// Ensures app state and startup cache defaults cannot drift into separate location policies.
    /// </summary>
    [Fact]
    public void DesktopStateStores_UseTheSharedStatePathOwner() {
        Assert.Equal(ChatStatePaths.GetDefaultPath("app-state.db"), ChatAppStateStore.GetDefaultDbPath());
        Assert.Equal(
            ChatStatePaths.GetDefaultPath("startup-webview-budget-cache-v1.json"),
            MainWindow.ResolveStartupWebViewBudgetCachePath());
    }
}
