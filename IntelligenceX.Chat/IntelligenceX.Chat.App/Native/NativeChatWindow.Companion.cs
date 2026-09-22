using IntelligenceX.Desktop;

namespace IntelligenceX.Chat.App.Native;

internal sealed partial class NativeChatWindow {
    private void OpenTray() {
        try {
            if (!DesktopCompanionLauncher.TryOpen(DesktopCompanion.Tray)) {
                _viewModel.SetHostStatus("IX Tray was not found in this bundle. Use the paired preview containing Chat and Tray folders, or open IX Tray separately.");
            }
        } catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or System.IO.IOException or UnauthorizedAccessException or InvalidOperationException) {
            _viewModel.SetHostStatus("IX Tray could not be started. Check that its files are present and accessible, then retry.");
        }
    }
}
