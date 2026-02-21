using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.App.Conversation;
using IntelligenceX.Chat.App.Markdown;
using IntelligenceX.Chat.App.Theming;
using IntelligenceX.Chat.Client;
using Microsoft.UI.Input;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using OfficeIMO.MarkdownRenderer;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;

namespace IntelligenceX.Chat.App;

public sealed partial class MainWindow : Window {
    private void MinimizeWindow() {
        try {
            if (AppWindow?.Presenter is OverlappedPresenter overlapped) {
                overlapped.Minimize();
            }
        } catch {
            // Ignore.
        }
    }

    private void ToggleMaximizeWindow() {
        try {
            if (AppWindow?.Presenter is OverlappedPresenter overlapped) {
                if (overlapped.State == OverlappedPresenterState.Maximized) {
                    overlapped.Restore();
                } else {
                    overlapped.Maximize();
                }
            }
        } catch {
            // Ignore.
        }
    }

    private bool IsWindowMaximized() {
        try {
            return AppWindow?.Presenter is OverlappedPresenter overlapped
                   && overlapped.State == OverlappedPresenterState.Maximized;
        } catch {
            return false;
        }
    }

    private void BeginDragMoveWindow() {
        try {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            if (hwnd == IntPtr.Zero) {
                return;
            }

            // Ignore delayed drag requests when the button is no longer physically pressed.
            if ((GetAsyncKeyState(VkLButton) & unchecked((short)0x8000)) == 0) {
                return;
            }

            var dragWatchdogSequence = ArmDragMoveWatchdog(hwnd);
            var lParam = IntPtr.Zero;
            if (GetCursorPos(out var cursor)) {
                var packed = ((cursor.Y & 0xFFFF) << 16) | (cursor.X & 0xFFFF);
                lParam = (IntPtr)packed;
            }

            try {
                ReleaseCapture();
                _ = SendMessage(hwnd, WmNcLButtonDown, (IntPtr)HtCaption, lParam);
            } finally {
                CompleteDragMoveWatchdog(dragWatchdogSequence);
            }
        } catch {
            // Ignore.
        }
    }

    private void EnsureNativeTitleBarRegionSupport() {
        if (_nonClientPointerSource is not null) {
            return;
        }

        try {
            var appWindow = AppWindow;
            if (appWindow is null) {
                return;
            }

            _nonClientPointerSource = InputNonClientPointerSource.GetForWindowId(appWindow.Id);
        } catch (Exception ex) {
            StartupLog.Write("EnsureNativeTitleBarRegionSupport failed: " + ex.Message);
        }
    }

    private void UpdateNativeTitleBarRegions(JsonElement root) {
        try {
            EnsureNativeTitleBarRegionSupport();
            if (_nonClientPointerSource is null) {
                return;
            }

            if (!TryGetUiHostRect(root, "titleBarRect", out var titleBarRect)) {
                return;
            }

            var noDragRects = new List<UiHostRect>();
            if (root.TryGetProperty("noDragRects", out var noDragRectsElement)
                && noDragRectsElement.ValueKind == JsonValueKind.Array) {
                foreach (var noDrag in noDragRectsElement.EnumerateArray()) {
                    if (TryGetUiHostRect(noDrag, out var noDragRect)) {
                        noDragRects.Add(noDragRect);
                    }
                }
            }

            _cachedTitleBarRect = titleBarRect;
            _cachedNoDragRects.Clear();
            _cachedNoDragRects.AddRange(noDragRects);

            ApplyNativeTitleBarRegions(titleBarRect, noDragRects);
        } catch (Exception ex) {
            StartupLog.Write("UpdateNativeTitleBarRegions failed: " + ex.Message);
            _nativeTitleBarRegionsActive = false;
            if (_webViewReady) {
                _ = _webView.ExecuteScriptAsync("window.ixSetNativeTitlebarEnabled && window.ixSetNativeTitlebarEnabled(false);");
            }
        }
    }

    private void ReapplyCachedNativeTitleBarRegions() {
        if (!_cachedTitleBarRect.HasValue) {
            return;
        }

        EnsureNativeTitleBarRegionSupport();
        if (_nonClientPointerSource is null) {
            return;
        }

        try {
            ApplyNativeTitleBarRegions(_cachedTitleBarRect.Value, _cachedNoDragRects);
        } catch (Exception ex) {
            StartupLog.Write("ReapplyCachedNativeTitleBarRegions failed: " + ex.Message);
        }
    }

    private void ApplyNativeTitleBarRegions(UiHostRect titleBarRect, List<UiHostRect> noDragRects) {
        try {
            var nonClientPointerSource = _nonClientPointerSource;
            if (nonClientPointerSource is null) {
                return;
            }

            var scale = GetUiRasterizationScale();
            var captionRect = ScaleToRegionRect(titleBarRect, scale);
            if (captionRect.Width <= 0 || captionRect.Height <= 0) {
                return;
            }

            var passthroughRects = new List<RectInt32>();
            foreach (var noDragRect in noDragRects) {
                var scaled = ScaleToRegionRect(noDragRect, scale);
                if (!TryIntersectRect(captionRect, scaled, out var clipped)) {
                    continue;
                }

                if (clipped.Width > 0 && clipped.Height > 0) {
                    passthroughRects.Add(clipped);
                }
            }

            var captionRects = new List<RectInt32> { captionRect };
            for (var i = 0; i < passthroughRects.Count; i++) {
                captionRects = SubtractRectangles(captionRects, passthroughRects[i]);
                if (captionRects.Count == 0) {
                    break;
                }
            }

            nonClientPointerSource.SetRegionRects(NonClientRegionKind.Passthrough, passthroughRects.ToArray());
            nonClientPointerSource.SetRegionRects(NonClientRegionKind.Caption, captionRects.ToArray());

            if (!_nativeTitleBarRegionsActive) {
                _nativeTitleBarRegionsActive = true;
                if (_webViewReady) {
                    _ = _webView.ExecuteScriptAsync("window.ixSetNativeTitlebarEnabled && window.ixSetNativeTitlebarEnabled(true);");
                }
            }
        } catch (Exception ex) {
            StartupLog.Write("ApplyNativeTitleBarRegions failed: " + ex.Message);
            _nativeTitleBarRegionsActive = false;
            if (_webViewReady) {
                _ = _webView.ExecuteScriptAsync("window.ixSetNativeTitlebarEnabled && window.ixSetNativeTitlebarEnabled(false);");
            }
        }
    }

    private static bool TryGetUiHostRect(JsonElement root, string propertyName, out UiHostRect rect) {
        rect = default;
        if (!root.TryGetProperty(propertyName, out var rectElement)) {
            return false;
        }

        return TryGetUiHostRect(rectElement, out rect);
    }

    private static bool TryGetUiHostRect(JsonElement element, out UiHostRect rect) {
        rect = default;
        if (element.ValueKind != JsonValueKind.Object) {
            return false;
        }

        if (!TryGetDoubleValue(element, "x", out var x)
            || !TryGetDoubleValue(element, "y", out var y)
            || !TryGetDoubleValue(element, "width", out var width)
            || !TryGetDoubleValue(element, "height", out var height)) {
            return false;
        }

        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(width) || !double.IsFinite(height)) {
            return false;
        }

        if (width <= 0 || height <= 0) {
            return false;
        }

        rect = new UiHostRect(x, y, width, height);
        return true;
    }

    private static bool TryGetDoubleValue(JsonElement root, string propertyName, out double value) {
        value = 0;
        if (!root.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.Number) {
            return false;
        }

        return element.TryGetDouble(out value);
    }

    private double GetUiRasterizationScale() {
        try {
            var scale = _webView.XamlRoot?.RasterizationScale ?? 1.0;
            return scale > 0 ? scale : 1.0;
        } catch {
            return 1.0;
        }
    }

    private static RectInt32 ScaleToRegionRect(UiHostRect rect, double scale) {
        var x = (int)Math.Round(rect.X * scale, MidpointRounding.AwayFromZero);
        var y = (int)Math.Round(rect.Y * scale, MidpointRounding.AwayFromZero);
        var width = (int)Math.Round(rect.Width * scale, MidpointRounding.AwayFromZero);
        var height = (int)Math.Round(rect.Height * scale, MidpointRounding.AwayFromZero);

        if (width <= 0 && rect.Width > 0) {
            width = 1;
        }
        if (height <= 0 && rect.Height > 0) {
            height = 1;
        }

        return new RectInt32(x, y, Math.Max(0, width), Math.Max(0, height));
    }

    private static List<RectInt32> SubtractRectangles(List<RectInt32> sourceRects, RectInt32 cutout) {
        var result = new List<RectInt32>();
        for (var i = 0; i < sourceRects.Count; i++) {
            var source = sourceRects[i];
            if (!TryIntersectRect(source, cutout, out var intersection)) {
                result.Add(source);
                continue;
            }

            var sourceRight = source.X + source.Width;
            var sourceBottom = source.Y + source.Height;
            var intersectionRight = intersection.X + intersection.Width;
            var intersectionBottom = intersection.Y + intersection.Height;

            if (intersection.Y > source.Y) {
                result.Add(new RectInt32(source.X, source.Y, source.Width, intersection.Y - source.Y));
            }

            if (intersectionBottom < sourceBottom) {
                result.Add(new RectInt32(source.X, intersectionBottom, source.Width, sourceBottom - intersectionBottom));
            }

            if (intersection.X > source.X) {
                result.Add(new RectInt32(source.X, intersection.Y, intersection.X - source.X, intersection.Height));
            }

            if (intersectionRight < sourceRight) {
                result.Add(new RectInt32(intersectionRight, intersection.Y, sourceRight - intersectionRight, intersection.Height));
            }
        }

        return result;
    }

    private static bool TryIntersectRect(RectInt32 first, RectInt32 second, out RectInt32 intersection) {
        var left = Math.Max(first.X, second.X);
        var top = Math.Max(first.Y, second.Y);
        var right = Math.Min(first.X + first.Width, second.X + second.Width);
        var bottom = Math.Min(first.Y + first.Height, second.Y + second.Height);

        if (right <= left || bottom <= top) {
            intersection = default;
            return false;
        }

        intersection = new RectInt32(left, top, right - left, bottom - top);
        return true;
    }

    private Task RunOnUiThreadAsync(Func<Task> work) {
        if (_dispatcher.HasThreadAccess) {
            return work();
        }

        var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_dispatcher.TryEnqueue(async () => {
            try {
                await work().ConfigureAwait(false);
                tcs.TrySetResult(null);
            } catch (Exception ex) {
                tcs.TrySetException(ex);
            }
        })) {
            tcs.TrySetException(new InvalidOperationException("Failed to dispatch work to UI thread."));
        }

        return tcs.Task;
    }

    private static string? ResolveServiceSourceDirectory() {
        var bestDir = string.Empty;
        var bestTicks = long.MinValue;

        TryPick(Path.Combine(AppContext.BaseDirectory, "service"), ref bestDir, ref bestTicks);
        TryPick(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "service")), ref bestDir, ref bestTicks);

        return string.IsNullOrWhiteSpace(bestDir) ? null : bestDir;
    }

    private static void TryPick(string dir, ref string bestDir, ref long bestTicks) {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) {
            return;
        }

        var exe = Path.Combine(dir, "IntelligenceX.Chat.Service.exe");
        var dll = Path.Combine(dir, "IntelligenceX.Chat.Service.dll");
        if (!File.Exists(exe) && !File.Exists(dll)) {
            return;
        }

        var marker = File.Exists(dll) ? dll : exe;
        long ticks;
        try {
            ticks = File.GetLastWriteTimeUtc(marker).Ticks;
        } catch {
            ticks = long.MinValue;
        }

        if (ticks > bestTicks) {
            bestTicks = ticks;
            bestDir = dir;
        }
    }

    private string? EnsureStagedServiceDirectory(string serviceSourceDir) {
        if (string.IsNullOrWhiteSpace(serviceSourceDir) || !Directory.Exists(serviceSourceDir)) {
            return null;
        }

        try {
            var runtimeRoot = Path.Combine(Path.GetTempPath(), "IntelligenceX.Chat", "service-runtime");
            var stageKey = BuildServiceStageKey(serviceSourceDir);
            var stagedDir = Path.Combine(runtimeRoot, stageKey);

            if (!string.IsNullOrWhiteSpace(_stagedServiceDir)
                && PathsEqual(_stagedServiceDir, stagedDir)
                && HasServicePayload(_stagedServiceDir)) {
                TouchDirectory(_stagedServiceDir);
                return _stagedServiceDir;
            }

            Directory.CreateDirectory(runtimeRoot);
            if (!HasServicePayload(stagedDir)) {
                var tempDir = stagedDir + ".tmp-" + Guid.NewGuid().ToString("N");
                DirectoryCopy(serviceSourceDir, tempDir);

                if (!Directory.Exists(stagedDir)) {
                    Directory.Move(tempDir, stagedDir);
                } else if (Directory.Exists(tempDir)) {
                    Directory.Delete(tempDir, recursive: true);
                }
            }

            if (!HasServicePayload(stagedDir)) {
                return null;
            }

            _stagedServiceDir = stagedDir;
            TouchDirectory(stagedDir);
            QueueStaleServiceStagingCleanup(runtimeRoot, stagedDir);
            return stagedDir;
        } catch (Exception ex) {
            AppendSystem(SystemNotice.ServiceStagingError(ex.Message));
            return null;
        }
    }

    private static void DirectoryCopy(string sourceDir, string destinationDir) {
        Directory.CreateDirectory(destinationDir);

        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories)) {
            var relative = Path.GetRelativePath(sourceDir, file);
            var target = Path.Combine(destinationDir, relative);
            var parent = Path.GetDirectoryName(target);
            if (!string.IsNullOrWhiteSpace(parent)) {
                Directory.CreateDirectory(parent);
            }
            File.Copy(file, target, overwrite: true);
        }
    }

    private static bool HasServicePayload(string? dir) {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) {
            return false;
        }

        return File.Exists(Path.Combine(dir, "IntelligenceX.Chat.Service.exe"))
               || File.Exists(Path.Combine(dir, "IntelligenceX.Chat.Service.dll"));
    }

    private static string BuildServiceStageKey(string serviceSourceDir) {
        var dll = Path.Combine(serviceSourceDir, "IntelligenceX.Chat.Service.dll");
        var exe = Path.Combine(serviceSourceDir, "IntelligenceX.Chat.Service.exe");
        var marker = File.Exists(dll) ? dll : exe;

        long ticks = 0;
        long length = 0;
        try {
            var info = new FileInfo(marker);
            ticks = info.Exists ? info.LastWriteTimeUtc.Ticks : 0;
            length = info.Exists ? info.Length : 0;
        } catch {
            // Ignore and keep defaults.
        }

        var fingerprint = Path.GetFullPath(serviceSourceDir).ToUpperInvariant()
                         + "|"
                         + Path.GetFileName(marker).ToUpperInvariant()
                         + "|"
                         + ticks.ToString(CultureInfo.InvariantCulture)
                         + "|"
                         + length.ToString(CultureInfo.InvariantCulture);

        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(fingerprint));
        var key = Convert.ToHexString(hash.AsSpan(0, 8));
        return "v1-" + key.ToLowerInvariant();
    }

    private static bool PathsEqual(string left, string right) {
        try {
            var l = Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var r = Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(l, r, StringComparison.OrdinalIgnoreCase);
        } catch {
            return false;
        }
    }

    private static void TouchDirectory(string? dir) {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) {
            return;
        }

        try {
            Directory.SetLastWriteTimeUtc(dir, DateTime.UtcNow);
        } catch {
            // Ignore.
        }
    }

    private void QueueStaleServiceStagingCleanup(string runtimeRoot, string keepDir) {
        if (Interlocked.CompareExchange(ref _serviceStagingCleanupInFlight, 1, 0) != 0) {
            return;
        }

        _ = Task.Run(() => {
            try {
                CleanupStaleServiceStaging(runtimeRoot, keepDir);
            } finally {
                Interlocked.Exchange(ref _serviceStagingCleanupInFlight, 0);
            }
        });
    }

    private static void CleanupStaleServiceStaging(string runtimeRoot, string keepDir) {
        try {
            if (!Directory.Exists(runtimeRoot)) {
                return;
            }

            var keep = Path.GetFullPath(keepDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var dirs = new List<DirectoryInfo>(new DirectoryInfo(runtimeRoot).EnumerateDirectories());
            dirs.Sort(static (a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));

            var retained = 0;
            for (var i = 0; i < dirs.Count; i++) {
                var dir = dirs[i];
                var fullPath = dir.FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                if (dir.Name.Contains(".tmp-", StringComparison.OrdinalIgnoreCase)) {
                    if ((DateTime.UtcNow - dir.LastWriteTimeUtc) > TimeSpan.FromMinutes(10)) {
                        TryDeleteDirectory(fullPath);
                    }
                    continue;
                }

                if (string.Equals(fullPath, keep, StringComparison.OrdinalIgnoreCase)) {
                    retained++;
                    continue;
                }

                if (retained < 3) {
                    retained++;
                    continue;
                }

                TryDeleteDirectory(fullPath);
            }
        } catch {
            // Ignore cleanup failures.
        }
    }

    private static void TryDeleteDirectory(string dir) {
        try {
            if (Directory.Exists(dir)) {
                Directory.Delete(dir, recursive: true);
            }
        } catch {
            // Ignore.
        }
    }

}
