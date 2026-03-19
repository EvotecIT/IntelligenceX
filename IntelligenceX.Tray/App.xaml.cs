using System.Windows;
using System.Runtime.InteropServices;
using System.IO;
using IntelligenceX.Presentation;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using IntelligenceX.Telemetry.Limits;
using IntelligenceX.Telemetry.Usage;
using IntelligenceX.Tray.Services;
using IntelligenceX.Tray.ViewModels;
using IntelligenceX.Tray.Views;
using System.Windows.Threading;
using System.ComponentModel;

namespace IntelligenceX.Tray;

public partial class App : Application {
    private TaskbarIcon? _trayIcon;
    private TrayPopupWindow? _popupWindow;
    private MainViewModel? _viewModel;
    private SqliteRawArtifactStore? _usageArtifactStore;
    private TrayThemeService? _themeService;
    private readonly List<(System.Windows.Controls.MenuItem Item, int Seconds)> _refreshIntervalItems = [];
    private readonly List<(System.Windows.Controls.MenuItem Item, string Mode)> _themeModeItems = [];
    private readonly List<(System.Windows.Controls.MenuItem Item, string Preset)> _accentPresetItems = [];
    private System.Windows.Controls.MenuItem? _notificationsItem;
    private System.Windows.Controls.MenuItem? _startWithWindowsItem;
    private System.Windows.Controls.MenuItem? _closeHidesToTrayItem;
    private string? _pendingNotificationProviderId;
    private DateTimeOffset _suppressTrayToggleUntilUtc;
    private bool _restoringPopupPlacement;
    private bool _isExiting;
    private readonly WindowsStartupRegistrationService _startupRegistrationService = new();

    protected override void OnStartup(StartupEventArgs e) {
        base.OnStartup(e);

        try {
            _usageArtifactStore = new SqliteRawArtifactStore(ResolveTrayUsageCachePath());
            var usageService = new UsageTelemetrySnapshotService(_usageArtifactStore);
            var limitService = new ProviderLimitSnapshotService();
            var gitHubService = new GitHubService();
            var preferencesStore = new TrayPreferencesStore();
            var usageSnapshotStore = new TrayUsageSnapshotStore();
            _viewModel = new MainViewModel(usageService, limitService, gitHubService, preferencesStore, usageSnapshotStore);
            _viewModel.NotificationRequested += OnNotificationRequested;
            _viewModel.ThemeModeChanged += OnThemeModeChanged;
            _viewModel.AccentPresetChanged += OnAccentPresetChanged;
            _viewModel.SyncStartWithWindowsState(_startupRegistrationService.IsEnabled());

            _themeService = new TrayThemeService(this);
            _themeService.ThemeChanged += OnThemeChanged;
            _themeService.ApplyAppearance(_viewModel.ThemeMode, _viewModel.AccentPreset);

            _popupWindow = CreatePopupWindow();

            // Create tray icon from XAML resource
            _trayIcon = (TaskbarIcon)FindResource("TrayIcon");
            _trayIcon.TrayLeftMouseUp += OnTrayLeftClick;
            _trayIcon.TrayBalloonTipClicked += OnTrayBalloonTipClicked;
            _trayIcon.ForceCreate();

            // Build context menu
            _trayIcon.ContextMenu = CreateContextMenu();
            Dispatcher.InvokeAsync(
                async () => await StartBackgroundInitializationAsync(),
                DispatcherPriority.Background);
        } catch (Exception ex) {
            MessageBox.Show(
                $"IntelligenceX Tray failed to start:\n\n{ex}",
                "Startup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private async Task StartBackgroundInitializationAsync() {
        if (_viewModel is null) {
            return;
        }

        try {
            await _viewModel.InitializeAsync();
        } catch (Exception ex) {
            MessageBox.Show(
                $"IntelligenceX Tray failed to initialize:\n\n{ex}",
                "Startup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private TrayPopupWindow CreatePopupWindow() {
        if (_viewModel is null) {
            throw new InvalidOperationException("The tray popup cannot be created before the view model is ready.");
        }

        var popupWindow = new TrayPopupWindow {
            DataContext = _viewModel
        };
        popupWindow.ManualPlacementCommitted += OnPopupManualPlacementCommitted;
        popupWindow.MinimizeRequested += OnPopupMinimizeRequested;
        popupWindow.CloseRequested += OnPopupCloseRequested;
        popupWindow.CloseInterceptRequested += OnPopupCloseInterceptRequested;
        popupWindow.PrimeForFastShow();
        return popupWindow;
    }

    private void OnThemeModeChanged(object? sender, EventArgs e) {
        _themeService?.ApplyAppearance(_viewModel?.ThemeMode, _viewModel?.AccentPreset);
    }

    private void OnAccentPresetChanged(object? sender, EventArgs e) {
        _themeService?.ApplyAppearance(_viewModel?.ThemeMode, _viewModel?.AccentPreset);
    }

    private void OnThemeChanged(object? sender, EventArgs e) {
        if (_trayIcon is null && _popupWindow is null) {
            return;
        }

        RebuildThemedShell(reopenPopup: _popupWindow?.IsVisible == true);
    }

    private void RebuildThemedShell(bool reopenPopup) {
        if (_viewModel is null) {
            return;
        }

        if (_popupWindow is not null) {
            _popupWindow.ManualPlacementCommitted -= OnPopupManualPlacementCommitted;
            _popupWindow.MinimizeRequested -= OnPopupMinimizeRequested;
            _popupWindow.CloseRequested -= OnPopupCloseRequested;
            _popupWindow.CloseInterceptRequested -= OnPopupCloseInterceptRequested;
            _popupWindow.Hide();
            _popupWindow.Close();
            _popupWindow = null;
        }

        _popupWindow = CreatePopupWindow();

        if (_trayIcon is not null) {
            _trayIcon.ContextMenu = CreateContextMenu();
        }

        if (reopenPopup) {
            ShowPopup();
        }
    }

    private System.Windows.Controls.ContextMenu CreateContextMenu() {
        _refreshIntervalItems.Clear();
        _themeModeItems.Clear();
        _accentPresetItems.Clear();
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

        var themeItem = new System.Windows.Controls.MenuItem { Header = "Theme" };
        if (itemStyle is not null) themeItem.Style = itemStyle;
        AddThemeModeItem(themeItem, itemStyle, "Auto", TrayThemeService.SystemMode);
        AddThemeModeItem(themeItem, itemStyle, "Dark", TrayThemeService.DarkMode);
        AddThemeModeItem(themeItem, itemStyle, "Light", TrayThemeService.LightMode);

        var accentItem = new System.Windows.Controls.MenuItem { Header = "Accent" };
        if (itemStyle is not null) accentItem.Style = itemStyle;
        AddAccentPresetItem(accentItem, itemStyle, "Violet", TrayThemeService.DefaultAccentPreset);
        AddAccentPresetItem(accentItem, itemStyle, "Ocean", TrayThemeService.OceanAccentPreset);
        AddAccentPresetItem(accentItem, itemStyle, "Forest", TrayThemeService.ForestAccentPreset);
        AddAccentPresetItem(accentItem, itemStyle, "Sunset", TrayThemeService.SunsetAccentPreset);

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

        _startWithWindowsItem = new System.Windows.Controls.MenuItem {
            Header = "Start With Windows",
            IsCheckable = true
        };
        if (itemStyle is not null) _startWithWindowsItem.Style = itemStyle;
        _startWithWindowsItem.Click += (_, _) => ToggleStartWithWindows();

        _closeHidesToTrayItem = new System.Windows.Controls.MenuItem {
            Header = "Close Button Hides To Tray",
            IsCheckable = true
        };
        if (itemStyle is not null) _closeHidesToTrayItem.Style = itemStyle;
        _closeHidesToTrayItem.Click += (_, _) => {
            if (_viewModel is not null && _closeHidesToTrayItem is not null) {
                _viewModel.SetCloseHidesToTray(_closeHidesToTrayItem.IsChecked);
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
        quitItem.Click += (_, _) => ExitApplication();

        menu.Items.Add(openItem);
        menu.Items.Add(refreshItem);
        menu.Items.Add(themeItem);
        menu.Items.Add(accentItem);
        menu.Items.Add(autoRefreshItem);
        menu.Items.Add(cacheItem);
        menu.Items.Add(_notificationsItem);
        menu.Items.Add(_startWithWindowsItem);
        menu.Items.Add(_closeHidesToTrayItem);
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

    private void AddThemeModeItem(System.Windows.Controls.MenuItem parent, Style? itemStyle, string header, string mode) {
        var item = new System.Windows.Controls.MenuItem {
            Header = header,
            IsCheckable = true
        };
        if (itemStyle is not null) item.Style = itemStyle;
        item.Click += (_, _) => _viewModel?.SetThemeMode(mode);
        parent.Items.Add(item);
        _themeModeItems.Add((item, mode));
    }

    private void AddAccentPresetItem(System.Windows.Controls.MenuItem parent, Style? itemStyle, string header, string preset) {
        var item = new System.Windows.Controls.MenuItem {
            Header = header,
            IsCheckable = true
        };
        if (itemStyle is not null) item.Style = itemStyle;
        item.Click += (_, _) => _viewModel?.SetAccentPreset(preset);
        parent.Items.Add(item);
        _accentPresetItems.Add((item, preset));
    }

    private void UpdateContextMenuState() {
        if (_viewModel is null) {
            return;
        }

        foreach (var (item, seconds) in _refreshIntervalItems) {
            item.IsChecked = _viewModel.AutoRefreshIntervalSeconds == seconds;
        }

        foreach (var (item, mode) in _themeModeItems) {
            item.IsChecked = string.Equals(_viewModel.ThemeMode, mode, StringComparison.Ordinal);
        }

        foreach (var (item, preset) in _accentPresetItems) {
            item.IsChecked = string.Equals(_viewModel.AccentPreset, preset, StringComparison.Ordinal);
        }

        if (_notificationsItem is not null) {
            _notificationsItem.IsChecked = _viewModel.NotificationsEnabled;
        }

        if (_closeHidesToTrayItem is not null) {
            _closeHidesToTrayItem.IsChecked = _viewModel.CloseHidesToTray;
        }

        if (_startWithWindowsItem is not null) {
            _startWithWindowsItem.IsChecked = _startupRegistrationService.IsEnabled();
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
        PositionPopupForOpen();
        _popupWindow.Show();
        _popupWindow.Activate();
    }

    private void PositionPopupForOpen() {
        if (!TryRestoreSavedPopupPlacement()) {
            PositionPopupNearTray();
        }
    }

    private void PositionPopupNearTray() {
        if (_popupWindow is null) return;

        if (!TryGetPopupPlacementContext(out var workArea, out var cursor)) {
            workArea = new PopupBounds(
                SystemParameters.WorkArea.Left,
                SystemParameters.WorkArea.Top,
                SystemParameters.WorkArea.Right,
                SystemParameters.WorkArea.Bottom);
            cursor = new PopupPoint(workArea.Right - 8, workArea.Bottom - 8);
        }

        var placement = PopupPlacementMath.PlaceNearCursor(
            workArea,
            _popupWindow.Width,
            _popupWindow.Height,
            cursor.X,
            cursor.Y);
        ApplyPopupPlacement(placement.Left, placement.Top);
    }

    private bool TryRestoreSavedPopupPlacement() {
        if (_popupWindow is null || _viewModel is null) {
            return false;
        }

        var placement = _viewModel.GetSavedWindowPlacement();
        if (placement is null) {
            return false;
        }

        var clampedPlacement = ClampPopupPlacement(placement.Left, placement.Top);
        ApplyPopupPlacement(clampedPlacement.X, clampedPlacement.Y);
        _viewModel.SaveWindowPlacement(clampedPlacement.X, clampedPlacement.Y);
        return true;
    }

    private void ApplyPopupPlacement(double left, double top) {
        if (_popupWindow is null) {
            return;
        }

        _restoringPopupPlacement = true;
        try {
            _popupWindow.Left = left;
            _popupWindow.Top = top;
        } finally {
            _restoringPopupPlacement = false;
        }
    }

    private PopupPoint ClampPopupPlacement(double left, double top) {
        if (_popupWindow is null) {
            return new PopupPoint(left, top);
        }

        var virtualLeft = SystemParameters.VirtualScreenLeft;
        var virtualTop = SystemParameters.VirtualScreenTop;
        var virtualRight = virtualLeft + SystemParameters.VirtualScreenWidth;
        var virtualBottom = virtualTop + SystemParameters.VirtualScreenHeight;
        var maxLeft = Math.Max(virtualLeft, virtualRight - _popupWindow.Width);
        var maxTop = Math.Max(virtualTop, virtualBottom - _popupWindow.Height);

        return new PopupPoint(
            Math.Clamp(left, virtualLeft, maxLeft),
            Math.Clamp(top, virtualTop, maxTop));
    }

    private void OnPopupManualPlacementCommitted(object? sender, EventArgs e) {
        if (_popupWindow is null || _viewModel is null || _restoringPopupPlacement) {
            return;
        }

        var clampedPlacement = ClampPopupPlacement(_popupWindow.Left, _popupWindow.Top);
        ApplyPopupPlacement(clampedPlacement.X, clampedPlacement.Y);
        _viewModel.SaveWindowPlacement(clampedPlacement.X, clampedPlacement.Y);
    }

    private void OnPopupMinimizeRequested(object? sender, EventArgs e) {
        _popupWindow?.Hide();
    }

    private void OnPopupCloseRequested(object? sender, EventArgs e) {
        if (_viewModel?.CloseHidesToTray == true) {
            _popupWindow?.Hide();
            return;
        }

        ExitApplication();
    }

    private void OnPopupCloseInterceptRequested(object? sender, CancelEventArgs e) {
        if (_isExiting) {
            return;
        }

        e.Cancel = true;
        if (_viewModel?.CloseHidesToTray == true) {
            _popupWindow?.Hide();
            return;
        }

        ExitApplication();
    }

    private void ToggleStartWithWindows() {
        if (_viewModel is null) {
            return;
        }

        var targetEnabled = _startWithWindowsItem?.IsChecked ?? false;
        var applied = _startupRegistrationService.SetEnabled(targetEnabled);
        if (applied) {
            _viewModel.SetStartWithWindows(targetEnabled);
            UpdateContextMenuState();
            return;
        }

        MessageBox.Show(
            "Unable to update the Windows startup registration for the tray app.",
            "Startup Registration",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        UpdateContextMenuState();
    }

    private void ExitApplication() {
        if (_isExiting) {
            return;
        }

        _isExiting = true;
        Shutdown();
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

    private static bool TryGetPopupPlacementContext(out PopupBounds workArea, out PopupPoint cursor) {
        workArea = default;
        cursor = default;
        if (!GetCursorPos(out var point)) {
            return false;
        }

        var monitor = MonitorFromPoint(point, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero) {
            return false;
        }

        var monitorInfo = new NativeMonitorInfo();
        monitorInfo.CbSize = Marshal.SizeOf<NativeMonitorInfo>();
        if (!GetMonitorInfo(monitor, ref monitorInfo)) {
            return false;
        }

        var dpiX = 96d;
        var dpiY = 96d;
        try {
            if (GetDpiForMonitor(monitor, MonitorDpiTypeEffective, out var monitorDpiX, out var monitorDpiY) == 0) {
                dpiX = monitorDpiX;
                dpiY = monitorDpiY;
            }
        } catch (DllNotFoundException) {
        } catch (EntryPointNotFoundException) {
        }

        workArea = PopupPlacementMath.ConvertPixelBoundsToDips(
            monitorInfo.Work.Left,
            monitorInfo.Work.Top,
            monitorInfo.Work.Right,
            monitorInfo.Work.Bottom,
            dpiX,
            dpiY);
        cursor = PopupPlacementMath.ConvertPixelsToDips(point.X, point.Y, dpiX, dpiY);
        return true;
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

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref NativeMonitorInfo monitorInfo);

    [DllImport("Shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    private const uint MonitorDefaultToNearest = 2;
    private const int MonitorDpiTypeEffective = 0;

    private struct NativePoint {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct NativeMonitorInfo {
        public int CbSize;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    protected override void OnExit(ExitEventArgs e) {
        if (_viewModel is not null) {
            _viewModel.NotificationRequested -= OnNotificationRequested;
            _viewModel.ThemeModeChanged -= OnThemeModeChanged;
            _viewModel.AccentPresetChanged -= OnAccentPresetChanged;
        }

        if (_themeService is not null) {
            _themeService.ThemeChanged -= OnThemeChanged;
            _themeService.Dispose();
            _themeService = null;
        }

        if (_trayIcon is not null) {
            _trayIcon.TrayBalloonTipClicked -= OnTrayBalloonTipClicked;
        }

        _viewModel?.Dispose();
        _usageArtifactStore?.Dispose();
        _usageArtifactStore = null;
        _trayIcon?.Dispose();
        if (_popupWindow is not null) {
            _popupWindow.ManualPlacementCommitted -= OnPopupManualPlacementCommitted;
            _popupWindow.MinimizeRequested -= OnPopupMinimizeRequested;
            _popupWindow.CloseRequested -= OnPopupCloseRequested;
            _popupWindow.CloseInterceptRequested -= OnPopupCloseInterceptRequested;
            _popupWindow.Close();
        }
        base.OnExit(e);
    }

    private static string ResolveTrayUsageCachePath() {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var directory = Path.Combine(localAppData, "IntelligenceX", "Tray");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "usage-artifacts.db");
    }
}
