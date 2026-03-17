using System.Windows;
using System.Windows.Controls;
using IntelligenceX.Tray.ViewModels;

namespace IntelligenceX.Tray.Views;

public partial class TrayPopupWindow : Window {
    public TrayPopupWindow() {
        InitializeComponent();
    }

    private void OnDeactivated(object? sender, EventArgs e) {
        Hide();
    }

    private void OnProviderTabClick(object sender, RoutedEventArgs e) {
        if (sender is RadioButton { Tag: ProviderViewModel provider } &&
            DataContext is MainViewModel mainVm) {
            mainVm.SelectedProvider = provider;
        }
    }
}
