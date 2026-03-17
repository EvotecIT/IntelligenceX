using System.Windows;
using System.Windows.Controls;
using System.Diagnostics;
using IntelligenceX.Tray.ViewModels;

namespace IntelligenceX.Tray.Views;

public partial class TrayPopupWindow : Window {
    private DateTimeOffset _suppressDeactivateUntilUtc;

    public TrayPopupWindow() {
        InitializeComponent();
    }

    public void PrepareForTrayOpen(TimeSpan? suppressDeactivateFor = null) {
        _suppressDeactivateUntilUtc = DateTimeOffset.UtcNow + (suppressDeactivateFor ?? TimeSpan.FromMilliseconds(450));
    }

    private void OnDeactivated(object? sender, EventArgs e) {
        if (DateTimeOffset.UtcNow < _suppressDeactivateUntilUtc) {
            return;
        }

        Hide();
    }

    private void OnProviderTabClick(object sender, RoutedEventArgs e) {
        if (sender is RadioButton { Tag: ProviderViewModel provider } &&
            DataContext is MainViewModel mainVm) {
            mainVm.SelectedProvider = provider;
        }
    }

    private void OnRangeChipClick(object sender, RoutedEventArgs e) {
        if (sender is not FrameworkElement { Tag: string rawRange, DataContext: ProviderViewModel provider }) {
            return;
        }

        if (Enum.TryParse<ProviderTimeRange>(rawRange, ignoreCase: true, out var range)) {
            provider.SetSelectedRange(range);
        }
    }

    private void OnEventSortChipClick(object sender, RoutedEventArgs e) {
        if (sender is not FrameworkElement { Tag: string rawSort, DataContext: ProviderViewModel provider }) {
            return;
        }

        if (Enum.TryParse<ProviderEventSort>(rawSort, ignoreCase: true, out var sort)) {
            provider.SetEventSort(sort);
        }
    }

    private void OnProviderComparisonSortChipClick(object sender, RoutedEventArgs e) {
        if (sender is not FrameworkElement { Tag: string rawSort, DataContext: ProviderViewModel provider }) {
            return;
        }

        if (Enum.TryParse<ProviderComparisonSort>(rawSort, ignoreCase: true, out var sort)) {
            provider.SetProviderComparisonSort(sort);
        }
    }

    private void OnOpenUrlClick(object sender, RoutedEventArgs e) {
        if (sender is not FrameworkElement { Tag: string url } || string.IsNullOrWhiteSpace(url)) {
            return;
        }

        try {
            Process.Start(new ProcessStartInfo {
                FileName = url,
                UseShellExecute = true
            });
        } catch (Exception ex) {
            MessageBox.Show(
                "Unable to open link.\n\n" + ex.Message,
                "Open Link",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OnProviderJumpClick(object sender, RoutedEventArgs e) {
        if (sender is not FrameworkElement { Tag: string providerId } || string.IsNullOrWhiteSpace(providerId)) {
            return;
        }

        if (DataContext is MainViewModel mainVm) {
            mainVm.FocusProvider(providerId);
        }
    }

    private void OnToggleFavoriteProviderClick(object sender, RoutedEventArgs e) {
        if (sender is not FrameworkElement { Tag: string providerId } || string.IsNullOrWhiteSpace(providerId)) {
            return;
        }

        if (DataContext is MainViewModel mainVm) {
            mainVm.ToggleFavoriteProvider(providerId);
        }
    }
}
