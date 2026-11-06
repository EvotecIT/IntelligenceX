using System.Windows;
using IntelligenceX.Tray.Services;

namespace IntelligenceX.Tray.Views;

public partial class TrayPopupWindow {
    private void OnOpenChat(object sender, RoutedEventArgs e) => CompanionActions.OpenChat();
    private void OnOpenLedger(object sender, RoutedEventArgs e) => new LedgerWindow { Owner = this }.Show();
}
