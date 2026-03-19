using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Diagnostics;
using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Media;
using IntelligenceX.Tray.ViewModels;

namespace IntelligenceX.Tray.Views;

public partial class TrayPopupWindow : Window {
    private const double MinimumPopupWidth = 400;
    private const double MaximumPopupWidth = 560;
    private const double MinimumPopupHeight = 640;
    private const double MaximumPopupHeight = 840;

    private bool _isPrimed;
    private DateTimeOffset _suppressDeactivateUntilUtc;

    public event EventHandler? ManualPlacementCommitted;
    public event EventHandler? MinimizeRequested;
    public event EventHandler? CloseRequested;
    public event EventHandler<CancelEventArgs>? CloseInterceptRequested;

    public TrayPopupWindow() {
        InitializeComponent();
        ApplyAdaptiveSizing();
    }

    public void PrimeForFastShow() {
        if (_isPrimed || IsVisible) {
            return;
        }

        var originalOpacity = Opacity;
        var originalShowActivated = ShowActivated;
        var originalLeft = Left;
        var originalTop = Top;
        try {
            PrepareForTrayOpen(TimeSpan.FromMilliseconds(900));
            ShowActivated = false;
            Opacity = 0;
            Left = -10000;
            Top = -10000;
            Show();
            UpdateLayout();
            Hide();
            _isPrimed = true;
        } finally {
            Opacity = originalOpacity;
            ShowActivated = originalShowActivated;
            Left = originalLeft;
            Top = originalTop;
        }
    }

    public void PrepareForTrayOpen(TimeSpan? suppressDeactivateFor = null) {
        ApplyAdaptiveSizing();
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

    private void OnShowGitHubAccountEditorClick(object sender, RoutedEventArgs e) {
        if (DataContext is MainViewModel { GitHub: { } gitHub }) {
            gitHub.BeginAccountSwitch();
        }
    }

    private void OnHideGitHubAccountEditorClick(object sender, RoutedEventArgs e) {
        if (DataContext is MainViewModel { GitHub: { } gitHub }) {
            gitHub.EndAccountSwitch();
        }
    }

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e) {
        UpdateClipGeometry();
    }

    private void OnHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
        if (e.ChangedButton != MouseButton.Left || IsDragSourceInteractive(e.OriginalSource as DependencyObject)) {
            return;
        }

        try {
            DragMove();
            ManualPlacementCommitted?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        } catch (InvalidOperationException) {
            // Ignore clicks that do not turn into a drag.
        }
    }

    private void OnMinimizeButtonClick(object sender, RoutedEventArgs e) {
        MinimizeRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnCloseButtonClick(object sender, RoutedEventArgs e) {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e) {
        CloseInterceptRequested?.Invoke(this, e);
    }

    private void ApplyAdaptiveSizing() {
        var workArea = SystemParameters.WorkArea;
        Width = Clamp(workArea.Width * 0.38, MinimumPopupWidth, MaximumPopupWidth);
        Height = Clamp(workArea.Height * 0.74, MinimumPopupHeight, MaximumPopupHeight);
        UpdateClipGeometry();
    }

    private void UpdateClipGeometry() {
        if (PopupClipGeometry is null) {
            return;
        }

        var width = Math.Max(0d, ActualWidth > 0 ? ActualWidth - 12d : Width - 12d);
        var height = Math.Max(0d, ActualHeight > 0 ? ActualHeight - 12d : Height - 12d);
        PopupClipGeometry.Rect = new Rect(0d, 0d, width, height);
    }

    private static double Clamp(double value, double minimum, double maximum) {
        return Math.Max(minimum, Math.Min(maximum, value));
    }

    private static bool IsDragSourceInteractive(DependencyObject? source) {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current)) {
            if (current is ButtonBase or TextBoxBase or ScrollBar or Slider) {
                return true;
            }
        }

        return false;
    }
}
