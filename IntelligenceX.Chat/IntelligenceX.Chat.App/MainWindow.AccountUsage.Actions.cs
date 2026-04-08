using System.Threading.Tasks;

namespace IntelligenceX.Chat.App;

public sealed partial class MainWindow {
    private async Task ClearTrackedAccountUsageAsync() {
        lock (_turnDiagnosticsSync) {
            _accountUsageByKey.Clear();
            SyncAccountUsageToAppStateLocked();
        }

        await PublishOptionsStateAsync().ConfigureAwait(false);
        await PersistAppStateAsync().ConfigureAwait(false);

        if (_client is not null && _isConnected) {
            await RefreshAuthenticationStateAsync(updateStatus: false).ConfigureAwait(false);
        }

        await SetStatusAsync("Cleared tracked account usage history.").ConfigureAwait(false);
    }
}
