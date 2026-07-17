using Microsoft.UI.Xaml;
using System.Threading.Tasks;

namespace IntelligenceX.Chat.App.Native;

internal sealed partial class NativeChatWindow {
    /// <summary>
    /// Opens the existing shared configuration workspace instead of introducing a second provider/profile settings brain.
    /// </summary>
    private async Task OpenSharedSettingsWorkspaceAsync() {
        if (_settingsWindow is not null) {
            await _settingsWindow.OpenOptionsAsync().ConfigureAwait(true);
            WindowForegroundActivator.EnsureWindowForeground(_settingsWindow);
            return;
        }

        var settingsWindow = new MainWindow(openOptionsOnLaunch: true) {
            Title = "IntelligenceX Chat - Runtime settings"
        };
        _settingsWindow = settingsWindow;
        settingsWindow.Closed += (_, _) => _settingsWindow = null;
        settingsWindow.Activate();
        WindowForegroundActivator.EnsureWindowForeground(settingsWindow);
        StartupLog.Write("Shared runtime settings workspace opened from native chat.");
    }
}
