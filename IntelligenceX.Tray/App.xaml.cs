using System.Windows;
using H.NotifyIcon;
using IntelligenceX.Telemetry.Limits;
using IntelligenceX.Telemetry.Usage;
using IntelligenceX.Tray.Services;
using IntelligenceX.Tray.ViewModels;
using IntelligenceX.Tray.Views;

namespace IntelligenceX.Tray;

public partial class App : Application {
    private TaskbarIcon? _trayIcon;
    private TrayPopupWindow? _popupWindow;
    private MainViewModel? _viewModel;

    protected override async void OnStartup(StartupEventArgs e) {
        base.OnStartup(e);

        try {
            var usageService = new UsageTelemetrySnapshotService();
            var limitService = new ProviderLimitSnapshotService();
            var gitHubService = new GitHubService();
            _viewModel = new MainViewModel(usageService, limitService, gitHubService);

            _popupWindow = new TrayPopupWindow {
                DataContext = _viewModel
            };

            // Create tray icon from XAML resource
            _trayIcon = (TaskbarIcon)FindResource("TrayIcon");
            _trayIcon.TrayLeftMouseUp += OnTrayLeftClick;
            _trayIcon.ForceCreate();

            // Build context menu
            _trayIcon.ContextMenu = CreateContextMenu();

            // Show the popup immediately on first launch so users know it works
            PositionPopupNearTray();
            _popupWindow.Show();
            _popupWindow.Activate();

            await _viewModel.InitializeAsync();
        } catch (Exception ex) {
            MessageBox.Show(
                $"IntelligenceX Tray failed to start:\n\n{ex}",
                "Startup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private System.Windows.Controls.ContextMenu CreateContextMenu() {
        var menu = new System.Windows.Controls.ContextMenu();
        if (TryFindResource("DarkContextMenuStyle") is Style cmStyle) {
            menu.Style = cmStyle;
        }

        var itemStyle = TryFindResource("DarkMenuItemStyle") as Style;

        var refreshItem = new System.Windows.Controls.MenuItem { Header = "Refresh Now" };
        if (itemStyle is not null) refreshItem.Style = itemStyle;
        refreshItem.Click += async (_, _) => {
            if (_viewModel is not null) await _viewModel.RefreshAsync();
        };

        var separator = new System.Windows.Controls.Separator();

        var aboutItem = new System.Windows.Controls.MenuItem { Header = "About IntelligenceX Tray" };
        if (itemStyle is not null) aboutItem.Style = itemStyle;
        aboutItem.Click += (_, _) => {
            MessageBox.Show(
                "IntelligenceX Usage Monitor\nVersion 0.1.0\n\nMonitors AI tool usage across Codex, Claude, Copilot, LM Studio and more.",
                "About IntelligenceX Tray",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        };

        var quitItem = new System.Windows.Controls.MenuItem { Header = "Quit" };
        if (itemStyle is not null) quitItem.Style = itemStyle;
        quitItem.Click += (_, _) => Shutdown();

        menu.Items.Add(refreshItem);
        menu.Items.Add(separator);
        menu.Items.Add(aboutItem);
        menu.Items.Add(quitItem);

        return menu;
    }

    private void OnTrayLeftClick(object? sender, RoutedEventArgs e) {
        if (_popupWindow is null) return;

        if (_popupWindow.IsVisible) {
            _popupWindow.Hide();
        } else {
            PositionPopupNearTray();
            _popupWindow.Show();
            _popupWindow.Activate();
        }
    }

    private void PositionPopupNearTray() {
        if (_popupWindow is null) return;

        var workArea = SystemParameters.WorkArea;
        _popupWindow.Left = workArea.Right - _popupWindow.Width - 8;
        _popupWindow.Top = workArea.Bottom - _popupWindow.Height - 8;
    }

    protected override void OnExit(ExitEventArgs e) {
        _viewModel?.Dispose();
        _trayIcon?.Dispose();
        _popupWindow?.Close();
        base.OnExit(e);
    }
}
