using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Runtime.InteropServices;

namespace IntelligenceX.Chat.App;

/// <summary>
/// WinUI 3 application root (code-only; no XAML).
/// </summary>
public sealed class App : Application {
    private const int ForegroundRetryMaxAttempts = 5;
    private const int ForegroundRetryIntervalMs = 160;
    private const int SwShow = 5;
    private const int SwRestore = 9;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private static readonly IntPtr HwndTopMost = new(-1);
    private static readonly IntPtr HwndNoTopMost = new(-2);
    private Window? _window;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

    /// <summary>
    /// Initializes the app.
    /// </summary>
    public App() {
        StartupLog.Write("App.ctor");
    }

    /// <inheritdoc />
    protected override void OnLaunched(LaunchActivatedEventArgs args) {
        StartupLog.Write("App.OnLaunched enter");
        if (IsTruthy(Environment.GetEnvironmentVariable("IXCHAT_MIN_WINDOW"))) {
            _window = new Window {
                Title = "IntelligenceX Chat (Min Window)",
                Content = new TextBlock {
                    Text = "Minimal window mode",
                    Margin = new Thickness(24)
                }
            };
            StartupLog.Write("Min window constructed");
        } else if (IsTruthy(Environment.GetEnvironmentVariable("IXCHAT_WEBVIEW_SMOKE"))) {
            _window = new WebViewSmokeWindow();
            StartupLog.Write("WebView smoke window constructed");
        } else {
            _window = new MainWindow();
            StartupLog.Write("MainWindow constructed");
        }

        _window.Activate();
        StartupLog.Write("Window activated");
        EnsureWindowForeground(_window);
    }

    private static bool IsTruthy(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return false;
        }

        var v = value.Trim();
        return string.Equals(v, "1", StringComparison.OrdinalIgnoreCase)
               || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase)
               || string.Equals(v, "yes", StringComparison.OrdinalIgnoreCase)
               || string.Equals(v, "on", StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureWindowForeground(Window window) {
        if (TryBringToForeground(window, 1)) {
            return;
        }

        var dispatcher = window.DispatcherQueue;
        if (dispatcher is null) {
            return;
        }

        var attempt = 1;
        var timer = dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(ForegroundRetryIntervalMs);
        timer.IsRepeating = true;
        var timerCleanedUp = false;

        void CleanupTimer() {
            if (timerCleanedUp) {
                return;
            }

            timerCleanedUp = true;
            timer.Stop();
            timer.Tick -= OnTick;
            window.Closed -= OnWindowClosed;
        }

        void OnWindowClosed(object sender, WindowEventArgs args) {
            CleanupTimer();
        }

        void OnTick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object _) {
            attempt++;
            if (attempt >= ForegroundRetryMaxAttempts || TryBringToForeground(window, attempt)) {
                CleanupTimer();
            }
        }

        window.Closed += OnWindowClosed;
        timer.Tick += OnTick;
        timer.Start();
    }

    private static bool TryBringToForeground(Window window, int attempt) {
        try {
            window.Activate();
            if (!TryGetWindowHandle(window, attempt, out var handle)) {
                return false;
            }

            var showRequestOk = ShowWindow(handle, SwShow);
            LogWin32CallFailureIfAny(showRequestOk, "ShowWindow(SW_SHOW)", attempt);
            if (IsIconic(handle)) {
                var restoreRequestOk = ShowWindow(handle, SwRestore);
                LogWin32CallFailureIfAny(restoreRequestOk, "ShowWindow(SW_RESTORE)", attempt);
            }

            // Topmost flip nudges z-order without leaving the window always-on-top.
            LogSetWindowPosFailureIfAny(
                SetWindowPos(handle, HwndTopMost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate),
                "topmost",
                attempt);
            LogSetWindowPosFailureIfAny(
                SetWindowPos(handle, HwndNoTopMost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpShowWindow),
                "notopmost",
                attempt);

            // Windows may reject foreground handoff (focus-stealing protections).
            // Treat this as best-effort and keep activation + z-order nudges in place.
            var foregroundRequestOk = SetForegroundWindow(handle);
            if (!foregroundRequestOk) {
                var error = Marshal.GetLastWin32Error();
                StartupLog.Write("SetForegroundWindow failed attempt=" + attempt + " error=" + error);
            }

            var active = GetForegroundWindow() == handle;
            StartupLog.Write("Window foreground request attempt=" + attempt + " requested=" + foregroundRequestOk + " active=" + active);
            return active;
        } catch (Exception ex) {
            StartupLog.Write("Window foreground request failed: " + ex.GetType().Name + ": " + ex.Message);
            return false;
        }
    }

    private static void LogSetWindowPosFailureIfAny(bool result, string stage, int attempt) {
        if (result) {
            return;
        }

        var error = Marshal.GetLastWin32Error();
        StartupLog.Write("SetWindowPos failed stage=" + stage + " attempt=" + attempt + " error=" + error);
    }

    private static void LogWin32CallFailureIfAny(bool result, string apiName, int attempt) {
        if (result) {
            return;
        }

        var error = Marshal.GetLastWin32Error();
        if (error == 0) {
            return;
        }

        StartupLog.Write(apiName + " failed attempt=" + attempt + " error=" + error);
    }

    private static bool TryGetWindowHandle(Window window, int attempt, out IntPtr handle) {
        handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        if (handle == IntPtr.Zero) {
            StartupLog.Write("Window foreground request skipped (no hwnd), attempt=" + attempt);
            return false;
        }

        if (!IsWindow(handle)) {
            StartupLog.Write("Window foreground request skipped (invalid hwnd), attempt=" + attempt);
            handle = IntPtr.Zero;
            return false;
        }

        return true;
    }
}
