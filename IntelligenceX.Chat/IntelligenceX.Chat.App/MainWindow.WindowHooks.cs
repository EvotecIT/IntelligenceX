using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace IntelligenceX.Chat.App;

public sealed partial class MainWindow : Window {
    private void InstallWindowMessageHook() {
        try {
            _windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
            if (_windowHandle == IntPtr.Zero) {
                StartupLog.Write("InstallWindowMessageHook skipped: hwnd not ready");
                return;
            }

            if (_windowProcDelegate is null) {
                _windowProcDelegate = WindowMessageProc;
            }

            var installedAny = false;
            if (TryHookWindow(_windowHandle)) {
                installedAny = true;
            }

            if (EnumChildWindows(_windowHandle, (child, _) => {
                TryHookWindow(child);
                return true;
            }, IntPtr.Zero)) {
                installedAny = true;
            }

            lock (_windowHookSync) {
                _windowHookInstalled = _hookedWindowProcs.Count > 0;
            }

            if (installedAny || _windowHookInstalled) {
                StartupLog.Write("InstallWindowMessageHook ok");
            }
        } catch (Exception ex) {
            StartupLog.Write("InstallWindowMessageHook failed: " + ex.Message);
        }
    }

    private void InstallGlobalWheelHook() {
        try {
            _windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
            if (_windowHandle == IntPtr.Zero) {
                StartupLog.Write("InstallGlobalWheelHook skipped: hwnd not ready");
                return;
            }

            if (_globalMouseHookHandle != IntPtr.Zero) {
                return;
            }

            if (_globalMouseProcDelegate is null) {
                _globalMouseProcDelegate = GlobalMouseHookProc;
            }

            var moduleHandle = GetModuleHandle(null);
            _globalMouseHookHandle = SetWindowsHookEx(WhMouseLl, _globalMouseProcDelegate, moduleHandle, 0);
            if (_globalMouseHookHandle == IntPtr.Zero) {
                var err = Marshal.GetLastWin32Error();
                StartupLog.Write("InstallGlobalWheelHook failed: " + err.ToString(CultureInfo.InvariantCulture));
                return;
            }

            StartupLog.Write("InstallGlobalWheelHook ok");
        } catch (Exception ex) {
            StartupLog.Write("InstallGlobalWheelHook failed: " + ex.Message);
        }
    }

    private void UninstallGlobalWheelHook() {
        try {
            if (_globalMouseHookHandle != IntPtr.Zero) {
                _ = UnhookWindowsHookEx(_globalMouseHookHandle);
                _globalMouseHookHandle = IntPtr.Zero;
            }
        } catch (Exception ex) {
            StartupLog.Write("UninstallGlobalWheelHook failed: " + ex.Message);
        } finally {
            _globalMouseProcDelegate = null;
        }
    }

    private bool TryHookWindow(IntPtr hWnd) {
        if (hWnd == IntPtr.Zero) {
            return false;
        }

        lock (_windowHookSync) {
            if (_hookedWindowProcs.ContainsKey(hWnd)) {
                return false;
            }
        }

        if (_windowProcDelegate is null) {
            return false;
        }

        var procPtr = Marshal.GetFunctionPointerForDelegate(_windowProcDelegate);
        var previousProc = SetWindowLongPtr(hWnd, GwlWndProc, procPtr);
        var err = Marshal.GetLastWin32Error();
        if (previousProc == IntPtr.Zero && err != 0) {
            StartupLog.Write("TryHookWindow failed for hwnd " + hWnd + ": " + err.ToString(CultureInfo.InvariantCulture));
            return false;
        }

        lock (_windowHookSync) {
            _hookedWindowProcs[hWnd] = previousProc;
        }

        return true;
    }

    private void UninstallWindowMessageHook() {
        try {
            List<KeyValuePair<IntPtr, IntPtr>> hooks;
            lock (_windowHookSync) {
                hooks = new List<KeyValuePair<IntPtr, IntPtr>>(_hookedWindowProcs);
                _hookedWindowProcs.Clear();
                _windowHookInstalled = false;
            }

            for (var i = 0; i < hooks.Count; i++) {
                var hwnd = hooks[i].Key;
                var originalProc = hooks[i].Value;
                if (hwnd == IntPtr.Zero || originalProc == IntPtr.Zero) {
                    continue;
                }

                _ = SetWindowLongPtr(hwnd, GwlWndProc, originalProc);
            }
        } catch (Exception ex) {
            StartupLog.Write("UninstallWindowMessageHook failed: " + ex.Message);
        } finally {
            _windowHookInstalled = false;
            _windowProcDelegate = null;
            _windowHandle = IntPtr.Zero;
        }
    }

    private IntPtr GlobalMouseHookProc(int nCode, IntPtr wParam, IntPtr lParam) {
        try {
            var message = (uint)wParam.ToInt64();
            var isWheelMessage = message == WmMouseWheel || message == WmMouseHWheel;
            if (nCode >= 0 && _webViewReady && isWheelMessage && lParam != IntPtr.Zero && IsForegroundOwnedByCurrentProcess()) {
                var hook = Marshal.PtrToStructure<MouseLowLevelHookStruct>(lParam);
                var delta = (short)((hook.MouseData >> 16) & 0xFFFF);
                if (delta != 0) {
                    if (!_globalWheelObservedLogged) {
                        _globalWheelObservedLogged = true;
                        StartupLog.Write("GlobalMouseHookProc observed first wheel event");
                    }

                    QueueWheelForward(delta, fromGlobalHook: true);
                }
            }
        } catch (Exception ex) {
            StartupLog.Write("GlobalMouseHookProc failed: " + ex.Message);
        }

        return CallNextHookEx(_globalMouseHookHandle, nCode, wParam, lParam);
    }

    private static bool IsForegroundOwnedByCurrentProcess() {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero) {
            return false;
        }

        _ = GetWindowThreadProcessId(foreground, out var processId);
        if (processId == 0) {
            return false;
        }

        return processId == (uint)Environment.ProcessId;
    }

    private IntPtr WindowMessageProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam) {
        try {
            if (_webViewReady && (msg == WmMouseWheel || msg == WmMouseHWheel || msg == WmPointerWheel || msg == WmPointerHWheel)) {
                var delta = ExtractWheelDelta(wParam);
                if (delta != 0) {
                    RecordNativeWheelObserved();
                    QueueWheelForward(delta, fromGlobalHook: false);
                }
            }
        } catch (Exception ex) {
            StartupLog.Write("WindowMessageProc failed: " + ex.Message);
        }

        IntPtr originalProc;
        lock (_windowHookSync) {
            if (!_hookedWindowProcs.TryGetValue(hWnd, out originalProc)) {
                originalProc = IntPtr.Zero;
            }
        }

        if (originalProc != IntPtr.Zero) {
            return CallWindowProc(originalProc, hWnd, msg, wParam, lParam);
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private static int ExtractWheelDelta(IntPtr wParam) {
        var value = wParam.ToInt64();
        return (short)((value >> 16) & 0xFFFF);
    }

    private void QueueWheelForward(int delta, bool fromGlobalHook) {
        if (delta == 0) {
            Interlocked.Increment(ref _wheelZeroDeltaIgnoredEvents);
            return;
        }

        if (fromGlobalHook) {
            Interlocked.Increment(ref _wheelGlobalQueuedEvents);
        } else {
            Interlocked.Increment(ref _wheelPointerQueuedEvents);
        }

        bool shouldScheduleFlush;
        lock (_wheelForwardSync) {
            _queuedWheelDelta += delta;
            _queuedWheelFromGlobal |= fromGlobalHook;
            _queuedWheelFromPointer |= !fromGlobalHook;
            shouldScheduleFlush = !_wheelForwardFlushScheduled;
            if (shouldScheduleFlush) {
                _wheelForwardFlushScheduled = true;
            }
        }

        if (!shouldScheduleFlush) {
            return;
        }

        _ = Task.Run(async () => {
            try {
                await Task.Delay(WheelForwardCoalesceInterval).ConfigureAwait(false);
                await RunOnUiThreadAsync(() => {
                    FlushQueuedWheelForward();
                    return Task.CompletedTask;
                }).ConfigureAwait(false);
            } catch (OperationCanceledException) {
                // Ignore.
            } catch (Exception ex) {
                StartupLog.Write("QueueWheelForward flush failed: " + ex.Message);
            }
        });
    }

    private void FlushQueuedWheelForward() {
        int delta;
        bool fromGlobal;
        bool fromPointer;
        lock (_wheelForwardSync) {
            delta = _queuedWheelDelta;
            fromGlobal = _queuedWheelFromGlobal;
            fromPointer = _queuedWheelFromPointer;
            _queuedWheelDelta = 0;
            _queuedWheelFromGlobal = false;
            _queuedWheelFromPointer = false;
            _wheelForwardFlushScheduled = false;
        }

        if (!_webViewReady || delta == 0) {
            Interlocked.Increment(ref _wheelDroppedNotReadyEvents);
            return;
        }

        Interlocked.Increment(ref _wheelForwardedBatches);
        Interlocked.Add(ref _wheelForwardedAbsDelta, Math.Abs((long)delta));
        if (fromPointer) {
            Interlocked.Increment(ref _wheelForwardedPointerBatches);
        }

        if (fromGlobal) {
            Interlocked.Increment(ref _wheelForwardedGlobalBatches);
        }

        if (fromPointer) {
            _ = _webView.ExecuteScriptAsync(UiBridgeScripts.BuildWheelDiagnosticRecordScript(delta));
        }

        if (fromGlobal) {
            _ = _webView.ExecuteScriptAsync(UiBridgeScripts.BuildWheelGlobalDiagnosticRecordScript(delta));
        }

        _ = _webView.ExecuteScriptAsync(UiBridgeScripts.BuildWheelForwardScript(delta));
    }

    private void RecordNativeWheelObserved() {
        if (_nativeWheelObserved) {
            return;
        }

        _nativeWheelObserved = true;
        StartupLog.Write("Native wheel path observed.");
    }

    private void RefreshGlobalWheelHookPolicy() {
        if (_shutdownRequested) {
            UninstallGlobalWheelHook();
            return;
        }

        switch (WheelHookMode) {
            case GlobalWheelHookMode.Off:
                UninstallGlobalWheelHook();
                break;
            case GlobalWheelHookMode.Always:
                InstallGlobalWheelHook();
                break;
            case GlobalWheelHookMode.Auto:
            default:
                // Keep global hook active while app window is active as a reliability fallback.
                // JS dedupe will skip forwarded host wheel if native wheel already applied.
                if (_webViewReady && _windowIsActive) {
                    InstallGlobalWheelHook();
                } else {
                    UninstallGlobalWheelHook();
                }
                break;
        }
    }

    private int ArmDragMoveWatchdog(IntPtr hwnd) {
        if (hwnd == IntPtr.Zero) {
            return 0;
        }

        int sequence;
        lock (_dragMoveWatchdogSync) {
            sequence = unchecked(_dragMoveWatchdogSequence + 1);
            if (sequence == 0) {
                sequence = 1;
            }

            _dragMoveWatchdogSequence = sequence;
            _dragMoveWatchdogInFlight = true;
        }

        Interlocked.Increment(ref _dragMoveWatchdogArmCount);

        _ = Task.Run(async () => {
            try {
                await Task.Delay(DragMoveWatchdogInterval).ConfigureAwait(false);
                TryForceEndDragMove(hwnd, sequence);
            } catch (Exception ex) {
                StartupLog.Write("ArmDragMoveWatchdog failed: " + ex.Message);
            }
        });

        return sequence;
    }

    private void CompleteDragMoveWatchdog(int sequence) {
        if (sequence == 0) {
            return;
        }

        lock (_dragMoveWatchdogSync) {
            if (_dragMoveWatchdogSequence == sequence) {
                _dragMoveWatchdogInFlight = false;
            }
        }
    }

    private void TryForceEndDragMove(IntPtr hwnd, int sequence) {
        lock (_dragMoveWatchdogSync) {
            if (!_dragMoveWatchdogInFlight || _dragMoveWatchdogSequence != sequence) {
                return;
            }
        }

        // If the button is still physically pressed, user is actively dragging.
        if ((GetAsyncKeyState(VkLButton) & unchecked((short)0x8000)) != 0) {
            return;
        }

        lock (_dragMoveWatchdogSync) {
            if (!_dragMoveWatchdogInFlight || _dragMoveWatchdogSequence != sequence) {
                return;
            }

            _dragMoveWatchdogInFlight = false;
        }

        try {
            ReleaseCapture();
            if (hwnd != IntPtr.Zero) {
                _ = PostMessage(hwnd, WmLButtonUp, IntPtr.Zero, IntPtr.Zero);
                _ = PostMessage(hwnd, WmNcLButtonUp, (IntPtr)HtCaption, IntPtr.Zero);
            }

            var forced = Interlocked.Increment(ref _dragMoveWatchdogForcedReleaseCount);
            StartupLog.Write("DragMove watchdog forced release #" + forced.ToString(CultureInfo.InvariantCulture));
        } catch (Exception ex) {
            StartupLog.Write("TryForceEndDragMove failed: " + ex.Message);
        }
    }

    private void LogInputReliabilityTelemetry(string phase) {
        try {
            var pointerQueued = Interlocked.Read(ref _wheelPointerQueuedEvents);
            var globalQueued = Interlocked.Read(ref _wheelGlobalQueuedEvents);
            var zeroDeltaIgnored = Interlocked.Read(ref _wheelZeroDeltaIgnoredEvents);
            var droppedNotReady = Interlocked.Read(ref _wheelDroppedNotReadyEvents);
            var forwardedBatches = Interlocked.Read(ref _wheelForwardedBatches);
            var forwardedPointerBatches = Interlocked.Read(ref _wheelForwardedPointerBatches);
            var forwardedGlobalBatches = Interlocked.Read(ref _wheelForwardedGlobalBatches);
            var forwardedAbsDelta = Interlocked.Read(ref _wheelForwardedAbsDelta);
            var dragWatchdogArmed = Interlocked.Read(ref _dragMoveWatchdogArmCount);
            var dragWatchdogForced = Interlocked.Read(ref _dragMoveWatchdogForcedReleaseCount);

            StartupLog.Write(
                "InputTelemetry(" + phase + "): " +
                "wheel.pointerQueued=" + pointerQueued.ToString(CultureInfo.InvariantCulture) +
                ", wheel.globalQueued=" + globalQueued.ToString(CultureInfo.InvariantCulture) +
                ", wheel.zeroDeltaIgnored=" + zeroDeltaIgnored.ToString(CultureInfo.InvariantCulture) +
                ", wheel.droppedNotReady=" + droppedNotReady.ToString(CultureInfo.InvariantCulture) +
                ", wheel.forwardedBatches=" + forwardedBatches.ToString(CultureInfo.InvariantCulture) +
                ", wheel.forwardedPointerBatches=" + forwardedPointerBatches.ToString(CultureInfo.InvariantCulture) +
                ", wheel.forwardedGlobalBatches=" + forwardedGlobalBatches.ToString(CultureInfo.InvariantCulture) +
                ", wheel.forwardedAbsDelta=" + forwardedAbsDelta.ToString(CultureInfo.InvariantCulture) +
                ", drag.watchdogArmed=" + dragWatchdogArmed.ToString(CultureInfo.InvariantCulture) +
                ", drag.watchdogForced=" + dragWatchdogForced.ToString(CultureInfo.InvariantCulture));
        } catch (Exception ex) {
            StartupLog.Write("LogInputReliabilityTelemetry failed: " + ex.Message);
        }
    }

    private void EnsureNativeTitleBarEventSubscriptions() {
        try {
            var appWindow = AppWindow;
            if (!ReferenceEquals(_trackedAppWindow, appWindow)) {
                if (_trackedAppWindow is not null) {
                    _trackedAppWindow.Changed -= OnAppWindowChanged;
                }

                _trackedAppWindow = appWindow;
                if (_trackedAppWindow is not null) {
                    _trackedAppWindow.Changed += OnAppWindowChanged;
                }
            }
        } catch (Exception ex) {
            StartupLog.Write("EnsureNativeTitleBarEventSubscriptions(appWindow) failed: " + ex.Message);
        }

        try {
            var xamlRoot = _webView.XamlRoot;
            if (!ReferenceEquals(_trackedXamlRoot, xamlRoot)) {
                if (_trackedXamlRoot is not null) {
                    _trackedXamlRoot.Changed -= OnWebViewXamlRootChanged;
                }

                _trackedXamlRoot = xamlRoot;
                if (_trackedXamlRoot is not null) {
                    _trackedXamlRoot.Changed += OnWebViewXamlRootChanged;
                }
            }
        } catch (Exception ex) {
            StartupLog.Write("EnsureNativeTitleBarEventSubscriptions(xamlRoot) failed: " + ex.Message);
        }
    }

    private void DetachNativeTitleBarEventSubscriptions() {
        try {
            if (_trackedAppWindow is not null) {
                _trackedAppWindow.Changed -= OnAppWindowChanged;
            }
        } catch (Exception ex) {
            StartupLog.Write("DetachNativeTitleBarEventSubscriptions(appWindow) failed: " + ex.Message);
        } finally {
            _trackedAppWindow = null;
        }

        try {
            if (_trackedXamlRoot is not null) {
                _trackedXamlRoot.Changed -= OnWebViewXamlRootChanged;
            }
        } catch (Exception ex) {
            StartupLog.Write("DetachNativeTitleBarEventSubscriptions(xamlRoot) failed: " + ex.Message);
        } finally {
            _trackedXamlRoot = null;
        }
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args) {
        if (_shutdownRequested) {
            return;
        }

        if (args.DidPositionChange || args.DidSizeChange || args.DidPresenterChange) {
            RequestTitleBarMetricsRefresh();
        }
    }

    private void OnWebViewXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) {
        if (_shutdownRequested) {
            return;
        }

        ReapplyCachedNativeTitleBarRegions();
        RequestTitleBarMetricsRefresh();
    }

    private void RequestTitleBarMetricsRefresh() {
        if (!_webViewReady || _shutdownRequested) {
            return;
        }

        if (_titleBarMetricsRefreshScheduled) {
            return;
        }

        _titleBarMetricsRefreshScheduled = true;
        _ = Task.Run(async () => {
            try {
                await Task.Delay(32).ConfigureAwait(false);
                await RunOnUiThreadAsync(() => {
                    _titleBarMetricsRefreshScheduled = false;
                    if (_webViewReady) {
                        _ = _webView.ExecuteScriptAsync("window.ixPostTitlebarMetrics && window.ixPostTitlebarMetrics();");
                    }
                    return Task.CompletedTask;
                }).ConfigureAwait(false);
            } catch {
                _titleBarMetricsRefreshScheduled = false;
            }
        });
    }
}
