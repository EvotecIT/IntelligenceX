using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace IntelligenceX.Chat.App;

/// <summary>
/// Performs a bounded, best-effort foreground handoff after a WinUI window is launched.
/// </summary>
internal static class WindowForegroundActivator {
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

    [DllImport("user32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

    internal static void EnsureWindowForeground(Window window) {
        ArgumentNullException.ThrowIfNull(window);
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
        var cleanedUp = false;

        void Cleanup() {
            if (cleanedUp) return;
            cleanedUp = true;
            timer.Stop();
            timer.Tick -= OnTick;
            window.Closed -= OnWindowClosed;
        }

        void OnWindowClosed(object sender, WindowEventArgs args) => Cleanup();

        void OnTick(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args) {
            attempt++;
            if (TryBringToForeground(window, attempt) || attempt >= ForegroundRetryMaxAttempts) {
                Cleanup();
            }
        }

        window.Closed += OnWindowClosed;
        timer.Tick += OnTick;
        timer.Start();
    }

    private static bool TryBringToForeground(Window window, int attempt) {
        try {
            window.Activate();
            var handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
            if (handle == IntPtr.Zero || !IsWindow(handle)) {
                StartupLog.Write("Window foreground request skipped (HWND unavailable), attempt=" + attempt);
                return false;
            }

            _ = ShowWindow(handle, SwShow);
            if (IsIconic(handle)) {
                _ = ShowWindow(handle, SwRestore);
            }

            _ = SetWindowPos(handle, HwndTopMost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
            _ = SetWindowPos(handle, HwndNoTopMost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpShowWindow);
            var requested = SetForegroundWindow(handle);
            var active = GetForegroundWindow() == handle;
            StartupLog.Write("Window foreground request attempt=" + attempt + " requested=" + requested + " active=" + active);
            return active;
        } catch (Exception ex) {
            StartupLog.Write("Window foreground request failed: " + ex.GetType().Name + ": " + ex.Message);
            return false;
        }
    }
}
