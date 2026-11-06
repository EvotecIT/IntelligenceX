using System.Windows;
using IntelligenceX.Tray.Services;

namespace IntelligenceX.Tray.Views;

public partial class TrayPopupWindow {
    private LedgerWindow? _ledgerWindow;

    private void OnOpenChat(object sender, RoutedEventArgs e) => CompanionActions.OpenChat();
    private void OnOpenLedger(object sender, RoutedEventArgs e) {
        if (_ledgerWindow is null) {
            _ledgerWindow = new LedgerWindow { Owner = this };
            _ledgerWindow.Closed += (_, _) => _ledgerWindow = null;
            _ledgerWindow.Show();
        } else {
            if (_ledgerWindow.WindowState == WindowState.Minimized) _ledgerWindow.WindowState = WindowState.Normal;
            _ledgerWindow.Activate();
        }
    }
}
