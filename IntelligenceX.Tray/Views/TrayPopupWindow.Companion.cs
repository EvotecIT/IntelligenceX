using System.Windows;
using IntelligenceX.Tray.Services;

namespace IntelligenceX.Tray.Views;

public partial class TrayPopupWindow {
    private void OnOpenChat(object sender, RoutedEventArgs e) => CompanionActions.OpenChat();
}
