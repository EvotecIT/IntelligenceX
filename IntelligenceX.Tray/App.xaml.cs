using System.Windows;
using System.Runtime.InteropServices;
using H.NotifyIcon;
using H.NotifyIcon.Core;
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
    private readonly List<(System.Windows.Controls.MenuItem Item, int Seconds)> _refreshIntervalItems = [];
    private System.Windows.Controls.MenuItem? _notificationsItem;
    private string? _pendingNotificationProviderId;
    private DateTimeOffset _suppressTrayToggleUntilUtc;

    protected override async void OnStartup(StartupEventArgs e) {
        base.OnStartup(e);

        try {
            var usageService = new UsageTelemetrySnapshotService();
            var limitService = new ProviderLimitSnapshotService();
            var gitHubService = new GitHubService();
            var preferencesStore = new TrayPreferencesStore();
            _viewModel = new MainViewModel(usageService, limitService, gitHubService, preferencesStore);
            _viewModel.NotificationRequested += OnNotificationRequested;

            _popupWindow = new TrayPopupWindow {
                DataContext = _viewModel
            };

            // Create tray icon from XAML resource
            _trayIcon = (TaskbarIcon)FindResource("TrayIcon");
            _trayIcon.TrayLeftMouseUp += OnTrayLeftClick;
            _trayIcon.TrayBalloonTipClicked += OnTrayBalloonTipClicked;
            _trayIcon.ForceCreate();

            // Build context menu
            _trayIcon.ContextMenu = CreateContextMenu();

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

        var openItem = new System.Windows.Controls.MenuItem { Header = "Open Dashboard" };
        if (itemStyle is not null) openItem.Style = itemStyle;
        openItem.Click += (_, _) => ShowPopup();

        var refreshItem = new System.Windows.Controls.MenuItem { Header = "Refresh Now" };
        if (itemStyle is not null) refreshItem.Style = itemStyle;
        refreshItem.Click += async (_, _) => {
            if (_viewModel is not null) await _viewModel.RefreshAsync();
        };

        var autoRefreshItem = new System.Windows.Controls.MenuItem { Header = "Auto Refresh" };
        if (itemStyle is not null) autoRefreshItem.Style = itemStyle;
        AddRefreshIntervalItem(autoRefreshItem, itemStyle, "1 minute", 60);
        AddRefreshIntervalItem(autoRefreshItem, itemStyle, "2 minutes", 120);
        AddRefreshIntervalItem(autoRefreshItem, itemStyle, "5 minutes", 300);
        AddRefreshIntervalItem(autoRefreshItem, itemStyle, "10 minutes", 600);
        AddRefreshIntervalItem(autoRefreshItem, itemStyle, "Manual only", 0);

        var cacheItem = new System.Windows.Controls.MenuItem { Header = "Open OpenAI Cache" };
        if (itemStyle is not null) cacheItem.Style = itemStyle;
        cacheItem.Click += async (_, _) => {
            if (_viewModel is not null) await _viewModel.OpenOpenAiCacheAsync();
        };

        _notificationsItem = new System.Windows.Controls.MenuItem {
            Header = "Limit Notifications",
            IsCheckable = true
        };
        if (itemStyle is not null) _notificationsItem.Style = itemStyle;
        _notificationsItem.Click += (_, _) => {
            if (_viewModel is not null && _notificationsItem is not null) {
                _viewModel.SetNotificationsEnabled(_notificationsItem.IsChecked);
            }
        };

        var separator = new System.Windows.Controls.Separator();
        if (TryFindResource("DarkSeparatorStyle") is Style separatorStyle) {
            separator.Style = separatorStyle;
        }

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

        menu.Items.Add(openItem);
        menu.Items.Add(refreshItem);
        menu.Items.Add(autoRefreshItem);
        menu.Items.Add(cacheItem);
        menu.Items.Add(_notificationsItem);
        menu.Items.Add(separator);
        menu.Items.Add(aboutItem);
        menu.Items.Add(quitItem);
        menu.Opened += (_, _) => UpdateContextMenuState();

        return menu;
    }

    private void AddRefreshIntervalItem(System.Windows.Controls.MenuItem parent, Style? itemStyle, string header, int seconds) {
        var item = new System.Windows.Controls.MenuItem {
            Header = header,
            IsCheckable = true
        };
        if (itemStyle is not null) item.Style = itemStyle;
        item.Click += (_, _) => _viewModel?.SetAutoRefreshIntervalSeconds(seconds);
        parent.Items.Add(item);
        _refreshIntervalItems.Add((item, seconds));
    }

    private void UpdateContextMenuState() {
        if (_viewModel is null) {
            return;
        }

        foreach (var (item, seconds) in _refreshIntervalItems) {
            item.IsChecked = _viewModel.AutoRefreshIntervalSeconds == seconds;
        }

        if (_notificationsItem is not null) {
            _notificationsItem.IsChecked = _viewModel.NotificationsEnabled;
        }
    }

    private void OnTrayLeftClick(object? sender, RoutedEventArgs e) {
        if (_popupWindow is null) return;

        if (DateTimeOffset.UtcNow < _suppressTrayToggleUntilUtc) {
            return;
        }

        if (_popupWindow.IsVisible) {
            _popupWindow.Hide();
            _suppressTrayToggleUntilUtc = DateTimeOffset.UtcNow.AddMilliseconds(250);
        } else {
            ShowPopup();
        }
    }

    private void ShowPopup() {
        if (_popupWindow is null) {
            return;
        }

        _popupWindow.PrepareForTrayOpen();
        _suppressTrayToggleUntilUtc = DateTimeOffset.UtcNow.AddMilliseconds(500);
        PositionPopupNearTray();
        _popupWindow.Show();
        _popupWindow.Activate();
    }

    private void PositionPopupNearTray() {
        if (_popupWindow is null) return;

        var workArea = SystemParameters.WorkArea;
        if (!TryGetCursorPosition(out var cursorX, out var cursorY)) {
            cursorX = workArea.Right - 8;
            cursorY = workArea.Bottom - 8;
        }

        var targetLeft = cursorX - _popupWindow.Width + 18;
        var targetTop = cursorY - _popupWindow.Height - 12;

        _popupWindow.Left = Math.Max(workArea.Left + 8, Math.Min(targetLeft, workArea.Right - _popupWindow.Width - 8));
        _popupWindow.Top = Math.Max(workArea.Top + 8, Math.Min(targetTop, workArea.Bottom - _popupWindow.Height - 8));
    }

    private static bool TryGetCursorPosition(out double x, out double y) {
        if (GetCursorPos(out var point)) {
            x = point.X;
            y = point.Y;
            return true;
        }

        x = 0;
        y = 0;
        return false;
    }

    private void OnNotificationRequested(object? sender, TrayNotificationRequestedEventArgs e) {
        _pendingNotificationProviderId = e.ProviderId;
        _trayIcon?.ShowNotification(
            e.Title,
            e.Message,
            e.IsCritical ? NotificationIcon.Error : NotificationIcon.Warning);
    }

    private void OnTrayBalloonTipClicked(object? sender, RoutedEventArgs e) {
        if (_viewModel is not null && !string.IsNullOrWhiteSpace(_pendingNotificationProviderId)) {
            _viewModel.FocusProvider(_pendingNotificationProviderId);
        }

        ShowPopup();
        _pendingNotificationProviderId = null;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    private struct NativePoint {
        public int X;
        public int Y;
    }

    protected override void OnExit(ExitEventArgs e) {
        if (_viewModel is not null) {
            _viewModel.NotificationRequested -= OnNotificationRequested;
        }

        if (_trayIcon is not null) {
            _trayIcon.TrayBalloonTipClicked -= OnTrayBalloonTipClicked;
        }

        _viewModel?.Dispose();
        _trayIcon?.Dispose();
        _popupWindow?.Close();
        base.OnExit(e);
    }
}
