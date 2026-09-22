using System.Windows;
using IntelligenceX.Desktop;

namespace IntelligenceX.Tray.Services;

/// <summary>Tray presentation for the shared bundle launcher.</summary>
internal static class CompanionActions {
    internal static void OpenChat() {
        try {
            if (DesktopCompanionLauncher.TryOpen(DesktopCompanion.Chat)) return;
            MessageBox.Show("IX Chat was not found in this bundle. Use the paired preview containing Chat and Tray folders, or open IX Chat separately.",
                "IX Tray · Open IX Chat", MessageBoxButton.OK, MessageBoxImage.Information);
        } catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or System.IO.IOException or UnauthorizedAccessException or InvalidOperationException) {
            MessageBox.Show("IX Chat could not be started. Check that its files are present and accessible, then retry.",
                "IX Tray · Open IX Chat", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
