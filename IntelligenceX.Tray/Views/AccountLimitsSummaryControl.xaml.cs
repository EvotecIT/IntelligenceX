using System.Windows.Controls;

namespace IntelligenceX.Tray.Views;

/// <summary>
/// Presents the existing provider account snapshots in the main usage view.
/// Inspection never changes the account used by a chat or provider session.
/// </summary>
public partial class AccountLimitsSummaryControl : UserControl {
    public AccountLimitsSummaryControl() {
        InitializeComponent();
    }
}
