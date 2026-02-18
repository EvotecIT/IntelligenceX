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
    private async Task PersistAppStateAsync(bool allowDuringShutdown = false) {
        if (!_appStateLoaded || (_shutdownRequested && !allowDuringShutdown)) {
            return;
        }

        await _stateWriteGate.WaitAsync().ConfigureAwait(false);
        try {
            var activeConversation = GetActiveConversation();
            _appState.ProfileName = _appProfileName;
            _appState.TimestampMode = _timestampMode;
            CaptureAutonomyOverridesIntoAppState();
            _appState.ExportSaveMode = _exportSaveMode;
            _appState.ExportDefaultFormat = _exportDefaultFormat;
            _appState.ExportLastDirectory = _lastExportDirectory;
            _appState.QueueAutoDispatchEnabled = _queueAutoDispatchEnabled;
            _appState.ProactiveModeEnabled = _proactiveModeEnabled;
            _appState.PersistentMemoryEnabled = _persistentMemoryEnabled;
            _appState.LocalProviderTransport = _localProviderTransport;
            _appState.LocalProviderBaseUrl = _localProviderBaseUrl;
            _appState.LocalProviderModel = _localProviderModel;
            CaptureModelCatalogCacheIntoAppState();
            _appState.MemoryFacts = NormalizeMemoryFacts(_appState.MemoryFacts);
            _appState.ActiveConversationId = _activeConversationId;
            _appState.ThreadId = activeConversation.ThreadId;
            if (string.IsNullOrWhiteSpace(_sessionThemeOverride)) {
                _appState.ThemePreset = _themePreset;
            }
            _appState.DisabledTools = BuildDisabledToolsList();
            _appState.Messages = BuildMessageStateSnapshot(activeConversation.Messages);
            _appState.Conversations = BuildConversationStateSnapshot();
            await _stateStore.UpsertAsync(_appProfileName, _appState, CancellationToken.None).ConfigureAwait(false);
            _knownProfiles.Add(_appProfileName);
        } catch (Exception ex) {
            if (VerboseServiceLogs || _debugMode) {
                AppendSystem(SystemNotice.StateSaveFailed(ex.Message));
            }
        } finally {
            _stateWriteGate.Release();
        }
    }

    private void QueuePersistAppState() {
        if (!_appStateLoaded || _shutdownRequested) {
            return;
        }

        var shouldStartWorker = false;
        lock (_persistDebounceSync) {
            _persistDebounceCts?.Cancel();
            _persistDebounceCts?.Dispose();
            _persistDebounceCts = new CancellationTokenSource();
            _persistDebounceRequested = true;
            if (!_persistDebounceWorkerRunning) {
                _persistDebounceWorkerRunning = true;
                shouldStartWorker = true;
            }
        }

        if (shouldStartWorker) {
            _persistDebounceWorkerTask = Task.Run(PersistDebounceWorkerAsync);
        }
    }

    private async Task PersistDebounceWorkerAsync() {
        while (true) {
            CancellationToken token;
            lock (_persistDebounceSync) {
                if (!_persistDebounceRequested || _shutdownRequested) {
                    _persistDebounceWorkerRunning = false;
                    return;
                }

                token = _persistDebounceCts?.Token ?? CancellationToken.None;
                _persistDebounceRequested = false;
            }

            try {
                await Task.Delay(PersistDebounceInterval, token).ConfigureAwait(false);
            } catch (OperationCanceledException) {
                // A newer queued request superseded this delay window.
                continue;
            }

            try {
                await PersistAppStateAsync().ConfigureAwait(false);
            } catch (ObjectDisposedException) {
                // Best-effort background save path during shutdown.
            } catch (Exception ex) {
                if (VerboseServiceLogs || _debugMode) {
                    try {
                        await RunOnUiThreadAsync(() => {
                            AppendSystem(SystemNotice.StateSaveFailed(ex.Message));
                            return Task.CompletedTask;
                        }).ConfigureAwait(false);
                    } catch {
                        // Best-effort diagnostics only.
                    }
                }
            }

            var persistRequestedDuringSave = false;
            lock (_persistDebounceSync) {
                persistRequestedDuringSave = _persistDebounceRequested && !_shutdownRequested;
            }

            // If new state arrived while persisting, loop immediately and keep the worker alive.
            if (persistRequestedDuringSave) {
                continue;
            }
        }
    }

    private async Task CancelQueuedPersistAppStateAsync() {
        CancellationTokenSource? cts;
        Task? workerTask;
        lock (_persistDebounceSync) {
            cts = _persistDebounceCts;
            workerTask = _persistDebounceWorkerTask;
            _persistDebounceCts = null;
            _persistDebounceRequested = false;
        }

        if (cts is not null) {
            try {
                cts.Cancel();
            } finally {
                cts.Dispose();
            }
        }

        if (workerTask is null) {
            return;
        }

        try {
            await workerTask.ConfigureAwait(false);
        } catch (OperationCanceledException) {
            // Debounce worker cancellation is expected during shutdown teardown.
        } finally {
            lock (_persistDebounceSync) {
                if (ReferenceEquals(_persistDebounceWorkerTask, workerTask)) {
                    _persistDebounceWorkerTask = null;
                }
            }
        }
    }

    private static List<string> BuildDisabledToolsList(Dictionary<string, bool> toolStates) {
        var list = new List<string>();
        foreach (var pair in toolStates) {
            if (!pair.Value) {
                list.Add(pair.Key);
            }
        }
        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list;
    }

    private List<string> BuildDisabledToolsList() {
        return BuildDisabledToolsList(_toolStates);
    }

    private static List<ChatMessageState> BuildMessageStateSnapshot(List<(string Role, string Text, DateTime Time)> messages) {
        var result = new List<ChatMessageState>(Math.Min(messages.Count, MaxMessagesPerConversation));
        var start = Math.Max(0, messages.Count - MaxMessagesPerConversation);
        for (var i = start; i < messages.Count; i++) {
            var m = messages[i];
            if (string.IsNullOrWhiteSpace(m.Text)) {
                continue;
            }

            result.Add(new ChatMessageState {
                Role = m.Role,
                Text = m.Text,
                TimeUtc = m.Time.ToUniversalTime()
            });
        }

        return result;
    }

    private List<ChatConversationState> BuildConversationStateSnapshot() {
        if (_conversations.Count == 0) {
            return new List<ChatConversationState>();
        }

        var ordered = new List<ConversationRuntime>(_conversations);
        ordered.Sort(static (a, b) => b.UpdatedUtc.CompareTo(a.UpdatedUtc));
        if (ordered.Count > MaxConversations) {
            ConversationRuntime? systemConversation = null;
            for (var i = 0; i < ordered.Count; i++) {
                if (IsSystemConversation(ordered[i])) {
                    systemConversation = ordered[i];
                    break;
                }
            }

            if (systemConversation is null) {
                ordered.RemoveRange(MaxConversations, ordered.Count - MaxConversations);
            } else {
                var userConversationLimit = Math.Max(1, MaxConversations - 1);
                var trimmed = new List<ConversationRuntime>(MaxConversations) { systemConversation };
                for (var i = 0; i < ordered.Count && trimmed.Count < MaxConversations; i++) {
                    var conversation = ordered[i];
                    if (ReferenceEquals(conversation, systemConversation) || IsSystemConversation(conversation)) {
                        continue;
                    }

                    trimmed.Add(conversation);
                    if (trimmed.Count >= userConversationLimit + 1) {
                        break;
                    }
                }

                trimmed.Sort(static (a, b) => b.UpdatedUtc.CompareTo(a.UpdatedUtc));
                ordered = trimmed;
            }
        }

        var conversations = new List<ChatConversationState>(ordered.Count);
        foreach (var conversation in ordered) {
            var title = ComputeConversationTitle(conversation.Title, conversation.Messages);
            var updatedUtc = conversation.UpdatedUtc == default
                ? (conversation.Messages.Count > 0 ? conversation.Messages[^1].Time.ToUniversalTime() : DateTime.UtcNow)
                : EnsureUtc(conversation.UpdatedUtc);
            conversations.Add(new ChatConversationState {
                Id = conversation.Id,
                Title = title,
                ThreadId = conversation.ThreadId,
                Messages = BuildMessageStateSnapshot(conversation.Messages),
                UpdatedUtc = updatedUtc
            });
        }

        return conversations;
    }

    private string NextId() {
        return Interlocked.Increment(ref _nextRequestId).ToString();
    }

    internal static async Task<bool> TryAwaitConnectTaskSettlementAsync(Task connectTask, TimeSpan graceTimeout) {
        if (connectTask is null) {
            throw new ArgumentNullException(nameof(connectTask));
        }

        if (graceTimeout <= TimeSpan.Zero) {
            return connectTask.IsCompleted;
        }

        try {
            await connectTask.WaitAsync(graceTimeout).ConfigureAwait(false);
            return true;
        } catch (TimeoutException) {
            return false;
        }
    }

    private static async Task ConnectClientWithTimeoutAsync(
        ChatServiceClient client,
        string pipeName,
        TimeSpan timeout,
        TimeSpan hardTimeout) {
        if (timeout <= TimeSpan.Zero) {
            throw new TimeoutException("Timed out waiting for service pipe.");
        }

        var resolvedHardTimeout = hardTimeout <= TimeSpan.Zero || hardTimeout < timeout
            ? timeout
            : hardTimeout;

        using var cts = new CancellationTokenSource(timeout);
        using var hardTimeoutCts = new CancellationTokenSource();
        var connectTask = client.ConnectAsync(pipeName, cts.Token);
        var hardTimeoutTask = Task.Delay(resolvedHardTimeout, hardTimeoutCts.Token);
        var completed = await Task.WhenAny(connectTask, hardTimeoutTask).ConfigureAwait(true);
        if (ReferenceEquals(completed, connectTask)) {
            hardTimeoutCts.Cancel();
            await connectTask.ConfigureAwait(true);
            return;
        }

        cts.Cancel();
        // Preserve original completion/failure if connect settles shortly after cancellation.
        if (await TryAwaitConnectTaskSettlementAsync(connectTask, StartupConnectAttemptHardTimeoutGrace).ConfigureAwait(true)) {
            return;
        }

        throw new TimeoutException("Timed out waiting for service pipe.");
    }

    private static string FormatConnectError(Exception ex) {
        return ex is OperationCanceledException or TimeoutException ? "Timed out waiting for service pipe." : ex.Message;
    }

    private static bool IsDisconnectedError(Exception ex) {
        if (ex is IOException || ex is ObjectDisposedException || ex is OperationCanceledException) {
            return true;
        }

        if (ex is InvalidOperationException inv && inv.Message.Contains("Not connected", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        return ex.Message.Contains("Disconnected", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUsageLimitError(Exception ex) {
        var message = ex.Message ?? string.Empty;
        return message.Contains("usage limit", StringComparison.OrdinalIgnoreCase)
               || message.Contains("quota", StringComparison.OrdinalIgnoreCase)
               || message.Contains("(429)", StringComparison.OrdinalIgnoreCase)
               || message.Contains(" 429", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsCanceledTurn(string requestId, Exception ex) {
        if (string.IsNullOrWhiteSpace(requestId) || string.IsNullOrWhiteSpace(_cancelRequestedTurnRequestId)) {
            return false;
        }

        if (!string.Equals(requestId, _cancelRequestedTurnRequestId, StringComparison.Ordinal)) {
            return false;
        }

        if (ex is OperationCanceledException) {
            return true;
        }

        var message = ex.Message ?? string.Empty;
        return message.Contains("canceled", StringComparison.OrdinalIgnoreCase)
               || message.Contains("cancelled", StringComparison.OrdinalIgnoreCase)
               || message.Contains("chat_canceled", StringComparison.OrdinalIgnoreCase);
    }

    private static DateTime EnsureUtc(DateTime value) {
        if (value == default) {
            return DateTime.UtcNow;
        }

        return value.Kind switch {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }

    private static bool TryParseJsonObject(string? json, out JsonElement root) {
        root = default;
        if (string.IsNullOrWhiteSpace(json)) {
            return false;
        }

        try {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) {
                return false;
            }
            root = doc.RootElement.Clone();
            return true;
        } catch {
            return false;
        }
    }

    private static string? TryGetString(JsonElement obj, string name) {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(name, out var el)) {
            return null;
        }
        return el.ValueKind == JsonValueKind.String ? el.GetString() : null;
    }

    private static bool? TryGetBoolean(JsonElement obj, string name) {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(name, out var el)) {
            return null;
        }

        if (el.ValueKind == JsonValueKind.True) {
            return true;
        }

        if (el.ValueKind == JsonValueKind.False) {
            return false;
        }

        if (el.ValueKind == JsonValueKind.String && bool.TryParse(el.GetString(), out var parsed)) {
            return parsed;
        }

        return null;
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

    private static string ResolveAppProfileName(string? value) {
        var name = (value ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(name) ? "default" : name;
    }

    private void CaptureAutonomyOverridesIntoAppState() {
        _appState.AutonomyMaxToolRounds = _autonomyMaxToolRounds;
        _appState.AutonomyParallelTools = _autonomyParallelTools;
        _appState.AutonomyTurnTimeoutSeconds = _autonomyTurnTimeoutSeconds;
        _appState.AutonomyToolTimeoutSeconds = _autonomyToolTimeoutSeconds;
        _appState.AutonomyWeightedToolRouting = _autonomyWeightedToolRouting;
        _appState.AutonomyMaxCandidateTools = _autonomyMaxCandidateTools;
        _appState.AutonomyPlanExecuteReviewLoop = _autonomyPlanExecuteReviewLoop;
        _appState.AutonomyMaxReviewPasses = _autonomyMaxReviewPasses;
        _appState.AutonomyModelHeartbeatSeconds = _autonomyModelHeartbeatSeconds;
    }

    private void RestoreAutonomyOverridesFromAppState() {
        _autonomyMaxToolRounds = NormalizeAutonomyInt(_appState.AutonomyMaxToolRounds, min: 1, max: 64);
        _autonomyParallelTools = _appState.AutonomyParallelTools;
        _autonomyTurnTimeoutSeconds = NormalizeAutonomyInt(_appState.AutonomyTurnTimeoutSeconds, min: 0, max: 3600);
        _autonomyToolTimeoutSeconds = NormalizeAutonomyInt(_appState.AutonomyToolTimeoutSeconds, min: 0, max: 3600);
        _autonomyWeightedToolRouting = _appState.AutonomyWeightedToolRouting;
        _autonomyMaxCandidateTools = NormalizeAutonomyInt(_appState.AutonomyMaxCandidateTools, min: 0, max: 64);
        _autonomyPlanExecuteReviewLoop = _appState.AutonomyPlanExecuteReviewLoop;
        _autonomyMaxReviewPasses = NormalizeAutonomyInt(_appState.AutonomyMaxReviewPasses, min: 0, max: 3);
        _autonomyModelHeartbeatSeconds = NormalizeAutonomyInt(_appState.AutonomyModelHeartbeatSeconds, min: 0, max: 60);

        _appState.AutonomyMaxToolRounds = _autonomyMaxToolRounds;
        _appState.AutonomyParallelTools = _autonomyParallelTools;
        _appState.AutonomyTurnTimeoutSeconds = _autonomyTurnTimeoutSeconds;
        _appState.AutonomyToolTimeoutSeconds = _autonomyToolTimeoutSeconds;
        _appState.AutonomyWeightedToolRouting = _autonomyWeightedToolRouting;
        _appState.AutonomyMaxCandidateTools = _autonomyMaxCandidateTools;
        _appState.AutonomyPlanExecuteReviewLoop = _autonomyPlanExecuteReviewLoop;
        _appState.AutonomyMaxReviewPasses = _autonomyMaxReviewPasses;
        _appState.AutonomyModelHeartbeatSeconds = _autonomyModelHeartbeatSeconds;
    }

    private static string? NormalizeTheme(string? value) {
        return ThemeContract.Normalize(value);
    }

    private static string ResolveTimestampMode(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return "seconds";
        }

        var normalized = value.Trim();
        if (string.Equals(normalized, "date-minutes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "date_minutes", StringComparison.OrdinalIgnoreCase)) {
            return "date-minutes";
        }

        if (string.Equals(normalized, "date-seconds", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "date_seconds", StringComparison.OrdinalIgnoreCase)) {
            return "date-seconds";
        }

        if (string.Equals(normalized, "minutes", StringComparison.OrdinalIgnoreCase)) {
            return "minutes";
        }

        if (string.Equals(normalized, "seconds", StringComparison.OrdinalIgnoreCase)) {
            return "seconds";
        }

        return "custom";
    }

    private static string ResolveTimestampFormat(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return "HH:mm:ss";
        }

        var normalized = value.Trim();
        if (string.Equals(normalized, "date-minutes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "date_minutes", StringComparison.OrdinalIgnoreCase)) {
            return "yyyy-MM-dd HH:mm";
        }

        if (string.Equals(normalized, "date-seconds", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "date_seconds", StringComparison.OrdinalIgnoreCase)) {
            return "yyyy-MM-dd HH:mm:ss";
        }

        if (string.Equals(normalized, "minutes", StringComparison.OrdinalIgnoreCase)) {
            return "HH:mm";
        }

        if (string.Equals(normalized, "seconds", StringComparison.OrdinalIgnoreCase)) {
            return "HH:mm:ss";
        }

        try {
            _ = DateTime.Now.ToString(normalized, CultureInfo.InvariantCulture);
            return normalized;
        } catch {
            return "HH:mm:ss";
        }
    }

    private static GlobalWheelHookMode ResolveGlobalWheelHookMode(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return GlobalWheelHookMode.Auto;
        }

        var normalized = value.Trim();
        if (string.Equals(normalized, "always", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "on", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "1", StringComparison.OrdinalIgnoreCase)) {
            return GlobalWheelHookMode.Always;
        }

        if (string.Equals(normalized, "off", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "false", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "0", StringComparison.OrdinalIgnoreCase)) {
            return GlobalWheelHookMode.Off;
        }

        return GlobalWheelHookMode.Auto;
    }

    private static int? NormalizeAutonomyInt(int? value, int min, int max) {
        if (!value.HasValue) {
            return null;
        }

        var v = value.Value;
        if (v < min || v > max) {
            return null;
        }

        return v;
    }

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
            CleanupStaleServiceStaging(runtimeRoot, stagedDir);
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

    private string BuildToolRunMarkdown(ToolRunDto tools) {
        return ToolRunMarkdownFormatter.Format(tools, ResolveToolDisplayName);
    }

    private string ResolveToolDisplayName(string? name) {
        if (!string.IsNullOrWhiteSpace(name)) {
            var key = name.Trim();
            if (_toolDisplayNames.TryGetValue(key, out var displayName) && !string.IsNullOrWhiteSpace(displayName)) {
                return displayName;
            }
        }

        return FormatToolDisplayName(name);
    }

    private static string FormatToolDisplayName(string? name) {
        if (string.IsNullOrWhiteSpace(name)) {
            return "Tool";
        }

        var tokens = name.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0) {
            return name;
        }

        var sb = new StringBuilder();
        for (var i = 0; i < tokens.Length; i++) {
            var token = tokens[i];
            var upper = token.ToUpperInvariant();
            var segment = upper switch {
                "AD" => "AD",
                "DN" => "DN",
                "LDAP" => "LDAP",
                "CSV" => "CSV",
                "TSV" => "TSV",
                "CPU" => "CPU",
                "ID" => "ID",
                "GUID" => "GUID",
                "DNS" => "DNS",
                "OU" => "OU",
                _ => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(token.ToLowerInvariant())
            };

            if (i > 0) {
                sb.Append(' ');
            }
            sb.Append(segment);
        }

        return sb.ToString();
    }

    private static string[] NormalizeTags(string[]? tags) {
        if (tags is null || tags.Length == 0) {
            return Array.Empty<string>();
        }

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in tags) {
            if (string.IsNullOrWhiteSpace(tag)) {
                continue;
            }

            set.Add(tag.Trim());
        }

        if (set.Count == 0) {
            return Array.Empty<string>();
        }

        var list = new List<string>(set);
        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list.ToArray();
    }

    private static string InferToolCategory(string toolName) {
        if (string.IsNullOrWhiteSpace(toolName)) {
            return "general";
        }

        var idx = toolName.IndexOf('_');
        if (idx <= 0) {
            return "general";
        }

        var prefix = toolName.Substring(0, idx);
        return prefix.ToLowerInvariant() switch {
            "ad" => "active-directory",
            "eventlog" => "event-log",
            "system" => "system",
            "fs" => "file-system",
            "email" => "email",
            "wsl" => "system",
            _ => "general"
        };
    }

    private string[] BuildKnownProfiles() {
        var set = new HashSet<string>(_knownProfiles, StringComparer.OrdinalIgnoreCase) { _appProfileName };
        var list = new List<string>(set);
        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list.ToArray();
    }

}
