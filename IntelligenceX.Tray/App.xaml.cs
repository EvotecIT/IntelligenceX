using System.Drawing;
using System.Windows;
using H.NotifyIcon;
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

        var usageService = new UsageDataService();
        _viewModel = new MainViewModel(usageService);

        _popupWindow = new TrayPopupWindow {
            DataContext = _viewModel
        };

        _trayIcon = new TaskbarIcon {
            ToolTipText = "IntelligenceX Usage Monitor",
            ContextMenu = CreateContextMenu(),
            Icon = CreateTrayIcon()
        };

        _trayIcon.TrayLeftMouseUp += OnTrayLeftClick;

        await _viewModel.InitializeAsync();
    }

    private System.Windows.Controls.ContextMenu CreateContextMenu() {
        var menu = new System.Windows.Controls.ContextMenu();
        menu.Style = (Style)FindResource("DarkContextMenuStyle");

        var refreshItem = new System.Windows.Controls.MenuItem { Header = "Refresh Now" };
        refreshItem.Style = (Style)FindResource("DarkMenuItemStyle");
        refreshItem.Click += async (_, _) => {
            if (_viewModel is not null) {
                await _viewModel.RefreshAsync();
            }
        };

        var separator = new System.Windows.Controls.Separator();

        var aboutItem = new System.Windows.Controls.MenuItem { Header = "About IntelligenceX Tray" };
        aboutItem.Style = (Style)FindResource("DarkMenuItemStyle");
        aboutItem.Click += (_, _) => {
            MessageBox.Show(
                "IntelligenceX Usage Monitor\nVersion 0.1.0\n\nMonitors AI tool usage across Codex, Claude, Copilot, LM Studio and more.",
                "About IntelligenceX Tray",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        };

        var quitItem = new System.Windows.Controls.MenuItem { Header = "Quit" };
        quitItem.Style = (Style)FindResource("DarkMenuItemStyle");
        quitItem.Click += (_, _) => Shutdown();

        menu.Items.Add(refreshItem);
        menu.Items.Add(separator);
        menu.Items.Add(aboutItem);
        menu.Items.Add(quitItem);

        return menu;
    }

    private void OnTrayLeftClick(object? sender, RoutedEventArgs e) {
        if (_popupWindow is null) {
            return;
        }

        if (_popupWindow.IsVisible) {
            _popupWindow.Hide();
        } else {
            PositionPopupNearTray();
            _popupWindow.Show();
            _popupWindow.Activate();
        }
    }

    private void PositionPopupNearTray() {
        if (_popupWindow is null) {
            return;
        }

        var workArea = SystemParameters.WorkArea;
        _popupWindow.Left = workArea.Right - _popupWindow.Width - 8;
        _popupWindow.Top = workArea.Bottom - _popupWindow.Height - 8;
    }

    private static Icon CreateTrayIcon() {
        // Generate a simple 16x16 icon programmatically since we don't ship an .ico file.
        var bitmap = new Bitmap(16, 16);
        using (var graphics = Graphics.FromImage(bitmap)) {
            graphics.Clear(Color.FromArgb(26, 26, 46));
            using var brush = new SolidBrush(Color.FromArgb(155, 233, 168));
            graphics.FillEllipse(brush, 2, 2, 12, 12);
            using var innerBrush = new SolidBrush(Color.FromArgb(64, 196, 99));
            graphics.FillEllipse(innerBrush, 5, 5, 6, 6);
        }

        return Icon.FromHandle(bitmap.GetHicon());
    }

    protected override void OnExit(ExitEventArgs e) {
        _viewModel?.Dispose();
        _trayIcon?.Dispose();
        _popupWindow?.Close();
        base.OnExit(e);
    }
}
