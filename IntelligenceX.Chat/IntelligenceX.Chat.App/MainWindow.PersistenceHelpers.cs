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
            _appState.ExportVisualThemeMode = _exportVisualThemeMode;
            _appState.ExportDocxVisualMaxWidthPx = _exportDocxVisualMaxWidthPx;
            _appState.ExportLastDirectory = _lastExportDirectory;
            _appState.QueueAutoDispatchEnabled = _queueAutoDispatchEnabled;
            _appState.ProactiveModeEnabled = _proactiveModeEnabled;
            _appState.PersistentMemoryEnabled = _persistentMemoryEnabled;
            _appState.ShowAssistantTurnTrace = _showAssistantTurnTrace;
            _appState.ShowAssistantDraftBubbles = _showAssistantDraftBubbles;
            _appState.LocalProviderTransport = _localProviderTransport;
            _appState.LocalProviderBaseUrl = _localProviderBaseUrl;
            _appState.LocalProviderModel = _localProviderModel;
            _appState.LocalProviderOpenAIAuthMode = _localProviderOpenAIAuthMode;
            _appState.LocalProviderOpenAIBasicUsername = _localProviderOpenAIBasicUsername;
            _appState.LocalProviderOpenAIAccountId = _localProviderOpenAIAccountId;
            SyncNativeAccountSlotsToAppState();
            _appState.LocalProviderReasoningEffort = _localProviderReasoningEffort;
            _appState.LocalProviderReasoningSummary = _localProviderReasoningSummary;
            _appState.LocalProviderTextVerbosity = _localProviderTextVerbosity;
            _appState.LocalProviderTemperature = _localProviderTemperature;
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
            lock (_turnDiagnosticsSync) {
                SyncAccountUsageToAppStateLocked();
            }
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

    private static List<ChatMessageState> BuildMessageStateSnapshot(List<(string Role, string Text, DateTime Time, string? Model)> messages) {
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
                TimeUtc = m.Time.ToUniversalTime(),
                Model = string.IsNullOrWhiteSpace(m.Model) ? null : m.Model.Trim()
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
                RuntimeLabel = string.IsNullOrWhiteSpace(conversation.RuntimeLabel) ? null : conversation.RuntimeLabel.Trim(),
                ModelLabel = string.IsNullOrWhiteSpace(conversation.ModelLabel) ? null : conversation.ModelLabel.Trim(),
                ModelOverride = string.IsNullOrWhiteSpace(conversation.ModelOverride) ? null : conversation.ModelOverride.Trim(),
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

    internal static Task<bool> TryPreserveConnectCompletionAfterCancellationAsync(Task connectTask, bool allowSettlementGrace) {
        if (!allowSettlementGrace) {
            return Task.FromResult(false);
        }

        return TryAwaitConnectTaskSettlementAsync(connectTask, StartupConnectAttemptHardTimeoutGrace);
    }

    private static async Task ConnectClientWithTimeoutAsync(
        ChatServiceClient client,
        string pipeName,
        TimeSpan timeout,
        TimeSpan hardTimeout,
        bool allowSettlementGrace = true) {
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
        var completed = await Task.WhenAny(connectTask, hardTimeoutTask).ConfigureAwait(false);
        if (ReferenceEquals(completed, connectTask)) {
            hardTimeoutCts.Cancel();
            await connectTask.ConfigureAwait(false);
            return;
        }

        cts.Cancel();
        // Preserve original completion/failure if connect settles shortly after cancellation.
        if (await TryPreserveConnectCompletionAfterCancellationAsync(connectTask, allowSettlementGrace).ConfigureAwait(false)) {
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

    internal static bool IsAuthenticationRequiredError(Exception ex) {
        if (ex is ChatServiceRequestException requestEx
            && string.Equals(requestEx.Code, "not_authenticated", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        var message = (ex.Message ?? string.Empty).Trim();
        if (message.Length == 0) {
            return false;
        }

        return message.Contains("not authenticated", StringComparison.OrdinalIgnoreCase)
               || message.Contains("authentication required", StringComparison.OrdinalIgnoreCase)
               || message.Contains("login required", StringComparison.OrdinalIgnoreCase)
               || message.Contains("sign in", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMissingTransportThreadError(Exception ex) {
        return ChatThreadRecoveryHeuristics.IsMissingTransportThreadError(ex);
    }

    private static bool IsChatInProgressError(Exception ex) {
        var message = (ex.Message ?? string.Empty).Trim();
        if (message.Length == 0) {
            return false;
        }

        return message.Contains("chat request is already running", StringComparison.OrdinalIgnoreCase)
               || message.Contains("chat_in_progress", StringComparison.OrdinalIgnoreCase);
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

    private static int? TryGetInt32(JsonElement obj, string name) {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(name, out var el)) {
            return null;
        }

        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var parsedNumber)) {
            return parsedNumber;
        }

        if (el.ValueKind == JsonValueKind.String
            && int.TryParse(el.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedString)) {
            return parsedString;
        }

        return null;
    }

    private static long? TryGetInt64(JsonElement obj, string name) {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(name, out var el)) {
            return null;
        }

        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out var parsedNumber)) {
            return parsedNumber;
        }

        if (el.ValueKind == JsonValueKind.String
            && long.TryParse(el.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedString)) {
            return parsedString;
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
        _autonomyMaxToolRounds = NormalizeAutonomyInt(
            _appState.AutonomyMaxToolRounds,
            min: ChatRequestOptionLimits.MinToolRounds,
            max: ChatRequestOptionLimits.MaxToolRounds);
        _autonomyParallelTools = _appState.AutonomyParallelTools;
        _autonomyTurnTimeoutSeconds = NormalizeAutonomyInt(
            _appState.AutonomyTurnTimeoutSeconds,
            min: ChatRequestOptionLimits.MinTimeoutSeconds,
            max: ChatRequestOptionLimits.MaxTimeoutSeconds);
        _autonomyToolTimeoutSeconds = NormalizeAutonomyInt(
            _appState.AutonomyToolTimeoutSeconds,
            min: ChatRequestOptionLimits.MinTimeoutSeconds,
            max: ChatRequestOptionLimits.MaxTimeoutSeconds);
        _autonomyWeightedToolRouting = _appState.AutonomyWeightedToolRouting;
        _autonomyMaxCandidateTools = NormalizeAutonomyInt(
            _appState.AutonomyMaxCandidateTools,
            min: ChatRequestOptionLimits.MinCandidateTools,
            max: ChatRequestOptionLimits.MaxCandidateTools);
        _autonomyPlanExecuteReviewLoop = _appState.AutonomyPlanExecuteReviewLoop;
        _autonomyMaxReviewPasses = NormalizeAutonomyInt(
            _appState.AutonomyMaxReviewPasses,
            min: 0,
            max: ChatRequestOptionLimits.MaxReviewPasses);
        _autonomyModelHeartbeatSeconds = NormalizeAutonomyInt(
            _appState.AutonomyModelHeartbeatSeconds,
            min: 0,
            max: ChatRequestOptionLimits.MaxModelHeartbeatSeconds);

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

}
