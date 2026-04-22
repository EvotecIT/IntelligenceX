using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Diagnostics;
using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IntelligenceX.Tray.ViewModels;
using Microsoft.Win32;

namespace IntelligenceX.Tray.Views;

public partial class TrayPopupWindow : Window {
    private const double MinimumPopupWidth = 400;
    private const double MaximumPopupWidth = 560;
    private const double MinimumPopupHeight = 640;
    private const double MaximumPopupHeight = 840;
    private const int MaxExportPixelWidth = 8192;
    private const int MaxExportPixelHeight = 32768;
    private const long MaxExportPixelCount = 40_000_000;
    private const double MinExportScale = 0.2d;

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

    private void OnGitHubRepoSortClick(object sender, RoutedEventArgs e) {
        if (sender is not FrameworkElement { Tag: string rawSort, DataContext: GitHubViewModel gitHub }) {
            return;
        }

        if (Enum.TryParse<GitHubRepoSortMode>(rawSort, ignoreCase: true, out var sort)) {
            gitHub.SetRepoSort(sort);
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

    private void OnSavePngButtonClick(object sender, RoutedEventArgs e) {
        if (ProviderContentPanel.DataContext is not ProviderViewModel provider) {
            return;
        }

        PrepareForTrayOpen(TimeSpan.FromMinutes(2));
        try {
            var dialog = new SaveFileDialog {
                Filter = "PNG images (*.png)|*.png|All files (*.*)|*.*",
                FileName = BuildPngFileName(provider)
            };
            if (dialog.ShowDialog(this) != true) {
                return;
            }

            SaveElementAsPng(ProviderContentPanel, dialog.FileName);
            provider.ActionStatusMessage = "Saved PNG to " + dialog.FileName;
        } catch (Exception ex) {
            provider.ActionStatusMessage = "PNG save failed: " + ex.Message;
        }
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

    private void SaveElementAsPng(FrameworkElement element, string fileName) {
        element.UpdateLayout();

        var width = Math.Ceiling(element.ActualWidth);
        var height = Math.Ceiling(element.ActualHeight);
        if (width <= 0d || height <= 0d) {
            throw new InvalidOperationException("Nothing visible to export.");
        }

        var dpi = VisualTreeHelper.GetDpi(element);
        // Clamp oversized exports before allocating the bitmap so tall provider panels cannot exhaust UI-thread memory.
        var exportScale = CalculateSafePngExportScale(width, height, dpi);

        var pixelWidth = Math.Max(1, (int)Math.Ceiling(width * dpi.DpiScaleX * exportScale));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(height * dpi.DpiScaleY * exportScale));
        var bitmap = new RenderTargetBitmap(
            pixelWidth,
            pixelHeight,
            96d * dpi.DpiScaleX * exportScale,
            96d * dpi.DpiScaleY * exportScale,
            PixelFormats.Pbgra32);

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen()) {
            var background = TryFindResource("BackgroundBrush") as Brush ?? Brushes.Transparent;
            context.DrawRectangle(background, null, new Rect(0d, 0d, width, height));
            context.DrawRectangle(
                new VisualBrush(element) {
                    AlignmentX = AlignmentX.Left,
                    AlignmentY = AlignmentY.Top,
                    Stretch = Stretch.None
                },
                null,
                new Rect(0d, 0d, width, height));
        }

        bitmap.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(fileName);
        encoder.Save(stream);
    }

    private static double CalculateSafePngExportScale(double width, double height, DpiScale dpi) {
        var exportScale = 1d;
        exportScale = Math.Min(exportScale, MaxExportPixelWidth / Math.Max(1d, width * dpi.DpiScaleX));
        exportScale = Math.Min(exportScale, MaxExportPixelHeight / Math.Max(1d, height * dpi.DpiScaleY));

        var nativePixelCount = width * dpi.DpiScaleX * height * dpi.DpiScaleY;
        if (nativePixelCount > MaxExportPixelCount) {
            exportScale = Math.Min(exportScale, Math.Sqrt(MaxExportPixelCount / nativePixelCount));
        }

        if (exportScale < MinExportScale) {
            throw new InvalidOperationException("The selected content is too large to export safely as a single PNG.");
        }

        return exportScale;
    }

    private static string BuildPngFileName(ProviderViewModel provider) {
        return SanitizeFileSegment(provider.DisplayName)
               + "-"
               + provider.SelectedRange.ToString().ToLowerInvariant()
               + "-"
               + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
               + ".png";
    }

    private static string SanitizeFileSegment(string? value) {
        var segment = string.IsNullOrWhiteSpace(value) ? "usage" : value.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars()) {
            segment = segment.Replace(invalid, '-');
        }

        return string.IsNullOrWhiteSpace(segment) ? "usage" : segment;
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
