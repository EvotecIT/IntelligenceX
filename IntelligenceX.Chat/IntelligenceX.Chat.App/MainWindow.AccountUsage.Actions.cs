using System.Threading.Tasks;

namespace IntelligenceX.Chat.App;

public sealed partial class MainWindow {
    internal static bool ShouldRefreshAuthenticationStateAfterClearingTrackedAccountUsage(
        bool isConnected,
        bool hasClient,
        bool requiresInteractiveSignIn) {
        return isConnected
               && hasClient
               && !requiresInteractiveSignIn;
    }

    private async Task ClearTrackedAccountUsageAsync() {
        lock (_turnDiagnosticsSync) {
            _accountUsageByKey.Clear();
            SyncAccountUsageToAppStateLocked();
        }

        await PublishOptionsStateAsync().ConfigureAwait(false);
        await PersistAppStateAsync().ConfigureAwait(false);

        if (ShouldRefreshAuthenticationStateAfterClearingTrackedAccountUsage(
                isConnected: _isConnected,
                hasClient: _client is not null,
                requiresInteractiveSignIn: RequiresInteractiveSignInForCurrentTransport())) {
            await RefreshAuthenticationStateAsync(updateStatus: false).ConfigureAwait(false);
        }

        await SetStatusAsync("Cleared tracked account usage history.").ConfigureAwait(false);
    }
}
