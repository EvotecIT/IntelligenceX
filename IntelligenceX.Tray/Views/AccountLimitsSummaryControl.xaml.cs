using System.Windows.Controls;
using System.Windows;
using IntelligenceX.Tray.ViewModels;

namespace IntelligenceX.Tray.Views;

/// <summary>
/// Presents the existing provider account snapshots in the main usage view.
/// Inspection never changes the account used by a chat or provider session.
/// </summary>
public partial class AccountLimitsSummaryControl : UserControl {
    public AccountLimitsSummaryControl() {
        InitializeComponent();
    }

    private void ManageResets_Click(object sender, RoutedEventArgs e) {
        if (sender is FrameworkElement { DataContext: ProviderLimitAccountViewModel { Snapshot: { } snapshot } account }
            && account.CanManageResets) {
            new BankedResetsWindow(account.ProviderId, snapshot) { Owner = Window.GetWindow(this) }.ShowDialog();
        }
    }
}
