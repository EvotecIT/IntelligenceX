using System.Windows.Controls;
using System.Windows;
using IntelligenceX.Tray.ViewModels;

namespace IntelligenceX.Tray.Views;

/// <summary>
/// Presents the existing provider account snapshots in the main usage view.
/// Inspection never changes the account used by a chat or provider session.
/// </summary>
public partial class AccountLimitsSummaryControl : UserControl {
    private readonly System.Windows.Threading.DispatcherTimer _readingAgeTimer = new() { Interval = TimeSpan.FromSeconds(30) };

    public AccountLimitsSummaryControl() {
        InitializeComponent();
        _readingAgeTimer.Tick += (_, _) => RefreshReadingAge();
        Loaded += (_, _) => { RefreshReadingAge(); _readingAgeTimer.Start(); };
        Unloaded += (_, _) => _readingAgeTimer.Stop();
        IsVisibleChanged += (_, _) => RefreshReadingAge();
    }

    private void RefreshReadingAge() {
        if (IsVisible && DataContext is ProviderViewModel provider) provider.RefreshLimitReadingAge();
    }

    private void ManageResets_Click(object sender, RoutedEventArgs e) {
        if (sender is FrameworkElement { DataContext: ProviderLimitAccountViewModel { Snapshot: { } snapshot } account }
            && account.CanManageResets) {
            new BankedResetsWindow(account.ProviderId, snapshot) { Owner = Window.GetWindow(this) }.ShowDialog();
        }
    }
}
