using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.App.Conversation;
using IntelligenceX.Chat.Client;
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

    private async Task ConnectAsync(bool fromUserAction = false) {
        await _connectGate.WaitAsync().ConfigureAwait(false);
        try {
            if (_client is not null && await IsClientAliveAsync(_client).ConfigureAwait(false)) {
                _isConnected = true;
                StopAutoReconnectLoop();
                await SetStatusAsync(SessionStatus.ForConnectedAuth(_isAuthenticated)).ConfigureAwait(false);
                return;
            }

            _isConnected = false;
            await SetStatusAsync(SessionStatus.Connecting()).ConfigureAwait(false);
            await DisposeClientAsync().ConfigureAwait(false);
            _isAuthenticated = false;
            _loginInProgress = false;

            var pipeName = _pipeName;
            if (_serviceProcess is not null && !_serviceProcess.HasExited && !string.Equals(_servicePipeName, pipeName, StringComparison.OrdinalIgnoreCase)) {
                pipeName = _servicePipeName!;
            }

            var client = new ChatServiceClient();
            client.MessageReceived += OnServiceMessage;
            client.Disconnected += OnClientDisconnected;
            Exception? initialConnectException = null;

            try {
                await ConnectClientWithTimeoutAsync(client, pipeName, TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            } catch (Exception ex) {
                initialConnectException = ex;

                if (await EnsureServiceRunningAsync(pipeName).ConfigureAwait(false)) {
                    try {
                        await ConnectClientWithTimeoutAsync(client, pipeName, TimeSpan.FromSeconds(15)).ConfigureAwait(false);
                    } catch (Exception ex2) {
                        await client.DisposeAsync().ConfigureAwait(false);
                        _isConnected = false;
                        await SetStatusAsync(SessionStatus.ConnectFailed()).ConfigureAwait(false);
                        EnsureAutoReconnectLoop();
                        if (VerboseServiceLogs || _debugMode) {
                            AppendSystem(SystemNotice.ConnectProbeFailed(FormatConnectError(initialConnectException)));
                        }
                        if (fromUserAction || _debugMode) {
                            AppendSystem(SystemNotice.ConnectFailedAfterSidecarStart(FormatConnectError(ex2)));
                        }
                        return;
                    }
                } else {
                    await client.DisposeAsync().ConfigureAwait(false);
                    _isConnected = false;
                    await SetStatusAsync(SessionStatus.ConnectFailed()).ConfigureAwait(false);
                    EnsureAutoReconnectLoop();
                    if (fromUserAction || _debugMode) {
                        AppendSystem(SystemNotice.ConnectFailed(FormatConnectError(initialConnectException)));
                        AppendSystem(SystemNotice.ServiceSidecarUnavailable());
                    }
                    return;
                }
            }

            _client = client;
            _isConnected = true;
            StopAutoReconnectLoop();
            await SetStatusAsync(SessionStatus.Connected()).ConfigureAwait(false);

            try {
                var hello = await _client.RequestAsync<HelloMessage>(new HelloRequest { RequestId = NextId() }, CancellationToken.None).ConfigureAwait(false);
                _sessionPolicy = hello.Policy;
            } catch (Exception ex) {
                _sessionPolicy = null;
                if (VerboseServiceLogs || _debugMode) {
                    AppendSystem(SystemNotice.HelloFailed(ex.Message));
                }
            }

            try {
                var toolList = await _client.RequestAsync<ToolListMessage>(new ListToolsRequest { RequestId = NextId() }, CancellationToken.None).ConfigureAwait(false);
                UpdateToolCatalog(toolList.Tools);
            } catch (Exception ex) {
                if (VerboseServiceLogs || _debugMode) {
                    AppendSystem(SystemNotice.ListToolsFailed(ex.Message));
                }
            }

            AppendStartupToolHealthWarningsFromPolicy();

            _ = await RefreshAuthenticationStateAsync(updateStatus: true).ConfigureAwait(false);
            try {
                await SyncConnectedServiceProfileAndModelsAsync(
                    forceModelRefresh: false,
                    setProfileNewThread: false,
                    appendWarnings: false).ConfigureAwait(false);
            } catch (Exception ex) {
                if (VerboseServiceLogs || _debugMode) {
                    AppendSystem("Model/profile sync failed: " + ex.Message);
                }
            }
        } finally {
            _connectGate.Release();
        }
    }

    private async Task LoginAsync() {
        await StartLoginFlowIfNeededAsync().ConfigureAwait(false);
    }

    private async Task ReLoginFromMenuAsync() {
        await ReLoginAsync().ConfigureAwait(false);
    }

    private async Task SwitchAccountFromMenuAsync() {
        await SwitchAccountAsync().ConfigureAwait(false);
    }

    private async Task SendPromptAsync(string text, string? preferredConversationId = null, DateTime? queuedAtUtc = null) {
        text = (text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text)) {
            return;
        }

        if (_isSending) {
            var queueConversationId = string.IsNullOrWhiteSpace(preferredConversationId)
                ? _activeConversationId
                : preferredConversationId;
            if (TryEnqueuePendingTurn(text, queueConversationId, out var queuedCount)) {
                await SetStatusAsync($"Queued next turn ({queuedCount}/{MaxQueuedTurns})").ConfigureAwait(false);
            } else {
                await SetStatusAsync("Turn queue is full. Wait for the current turn to finish or press Stop.").ConfigureAwait(false);
            }

            await PublishSessionStateAsync().ConfigureAwait(false);
            return;
        }

        if (!await EnsureConnectedAsync().ConfigureAwait(false)) {
            await SetStatusAsync(SessionStatus.ForConnection(_isConnected, _isAuthenticated)).ConfigureAwait(false);
            return;
        }

        if (_client is null) {
            await SetStatusAsync(SessionStatus.ForConnection(_isConnected, _isAuthenticated)).ConfigureAwait(false);
            return;
        }

        var turn = await PrepareChatTurnAsync(text).ConfigureAwait(false);
        if (turn is null) {
            return;
        }

        long? queueWaitMs = null;
        if (queuedAtUtc.HasValue && queuedAtUtc.Value.Kind == DateTimeKind.Utc) {
            var elapsed = DateTime.UtcNow - queuedAtUtc.Value;
            if (elapsed.TotalMilliseconds > 0) {
                queueWaitMs = (long)Math.Round(elapsed.TotalMilliseconds);
            }
        }

        var requestId = turn.RequestId;
        _isSending = true;
        _activeTurnRequestId = requestId;
        _latestTurnRequestId = requestId;
        _cancelRequestedTurnRequestId = null;
        _latestServiceActivityText = string.Empty;
        _activeTurnQueueWaitMs = queueWaitMs;
        ResetActivityTimeline();
        StartTurnWatchdog();
        _activeRequestConversationId = turn.ConversationId;
        ClearToolRoutingInsights();
        try {
            await PublishSessionStateAsync().ConfigureAwait(false);
        } finally {
            // Ensure tools state is refreshed after routing reset even if session publish faults.
            await PublishOptionsStateSafeAsync().ConfigureAwait(false);
        }

        try {
            await ExecuteChatTurnWithReconnectAsync(turn).ConfigureAwait(false);
        } finally {
            StopTurnWatchdog();
            _isSending = false;
            _activeTurnRequestId = null;
            _cancelRequestedTurnRequestId = null;
            _activeRequestConversationId = null;
            _activeTurnReceivedDelta = false;
            _activeTurnQueueWaitMs = null;
            try {
                await PublishSessionStateAsync().ConfigureAwait(false);
            } finally {
                await PublishOptionsStateSafeAsync().ConfigureAwait(false);
            }

            var dispatchedNextQueuedTurn = await DispatchNextQueuedTurnAsync(honorAutoDispatch: true).ConfigureAwait(false);
            if (!dispatchedNextQueuedTurn) {
                var queuedTotal = GetQueuedTurnCount() + GetQueuedPromptAfterLoginCount();
                if (!_queueAutoDispatchEnabled && queuedTotal > 0) {
                    await SetStatusAsync($"Queued turns paused ({queuedTotal} waiting).").ConfigureAwait(false);
                }
            }
        }
    }

    private async Task SendPromptToConversationAsync(string text, string? conversationId, DateTime? queuedAtUtc = null) {
        var normalized = (conversationId ?? string.Empty).Trim();
        if (_isSending) {
            await SendPromptAsync(text, normalized.Length == 0 ? _activeConversationId : normalized, queuedAtUtc).ConfigureAwait(false);
            return;
        }

        if (normalized.Length == 0 || string.Equals(normalized, _activeConversationId, StringComparison.OrdinalIgnoreCase)) {
            await SendPromptAsync(text, normalized, queuedAtUtc).ConfigureAwait(false);
            return;
        }

        var target = FindConversationById(normalized);
        if (target is null) {
            await SendPromptAsync(text, normalized, queuedAtUtc).ConfigureAwait(false);
            return;
        }

        await SwitchConversationAsync(target.Id).ConfigureAwait(false);
        await SendPromptAsync(text, normalized, queuedAtUtc).ConfigureAwait(false);
    }

    private bool TryEnqueuePendingTurn(string text, string? conversationId, out int queuedCount) {
        var trimmedText = (text ?? string.Empty).Trim();
        var trimmedConversationId = (conversationId ?? string.Empty).Trim();
        lock (_pendingTurnQueueSync) {
            if (trimmedText.Length == 0 || _pendingTurns.Count >= MaxQueuedTurns) {
                queuedCount = _pendingTurns.Count;
                return false;
            }

            _pendingTurns.Enqueue(new QueuedTurn(trimmedText, trimmedConversationId, DateTime.UtcNow));
            queuedCount = _pendingTurns.Count;
            return true;
        }
    }

    private bool TryDequeuePendingTurn(out QueuedTurn queuedTurn) {
        lock (_pendingTurnQueueSync) {
            if (_pendingTurns.Count == 0) {
                queuedTurn = null!;
                return false;
            }

            queuedTurn = _pendingTurns.Dequeue();
            return true;
        }
    }

    private int GetQueuedTurnCount() {
        lock (_pendingTurnQueueSync) {
            return _pendingTurns.Count;
        }
    }

    private int ClearPendingTurns() {
        lock (_pendingTurnQueueSync) {
            var cleared = _pendingTurns.Count;
            _pendingTurns.Clear();
            return cleared;
        }
    }

    private bool TryEnqueuePromptAfterLogin(string text, string? conversationId, out int queuedCount) {
        var trimmedText = (text ?? string.Empty).Trim();
        var trimmedConversationId = (conversationId ?? string.Empty).Trim();
        lock (_queuedAfterLoginSync) {
            if (trimmedText.Length == 0 || _queuedTurnsAfterLogin.Count >= MaxQueuedTurns) {
                queuedCount = _queuedTurnsAfterLogin.Count;
                return false;
            }

            _queuedTurnsAfterLogin.Enqueue(new QueuedTurn(trimmedText, trimmedConversationId, DateTime.UtcNow));
            queuedCount = _queuedTurnsAfterLogin.Count;
            return true;
        }
    }

    private bool TryDequeuePromptAfterLogin(out QueuedTurn queuedTurn) {
        lock (_queuedAfterLoginSync) {
            if (_queuedTurnsAfterLogin.Count == 0) {
                queuedTurn = null!;
                return false;
            }

            queuedTurn = _queuedTurnsAfterLogin.Dequeue();
            return true;
        }
    }

    private int GetQueuedPromptAfterLoginCount() {
        lock (_queuedAfterLoginSync) {
            return _queuedTurnsAfterLogin.Count;
        }
    }

    private int ClearQueuedPromptsAfterLogin() {
        lock (_queuedAfterLoginSync) {
            var cleared = _queuedTurnsAfterLogin.Count;
            _queuedTurnsAfterLogin.Clear();
            return cleared;
        }
    }

    private async Task<bool> TryDispatchQueuedPromptAfterLoginAsync(bool honorAutoDispatch = true) {
        if (_isSending || !_isAuthenticated || _loginInProgress || (honorAutoDispatch && !_queueAutoDispatchEnabled)) {
            return false;
        }

        if (!TryDequeuePromptAfterLogin(out var queuedTurn)) {
            return false;
        }

        await SendPromptToConversationAsync(queuedTurn.Text, queuedTurn.ConversationId, queuedTurn.EnqueuedUtc).ConfigureAwait(false);
        return true;
    }

    private async Task<bool> DispatchNextQueuedTurnAsync(bool honorAutoDispatch) {
        if (_isSending || (honorAutoDispatch && !_queueAutoDispatchEnabled)) {
            return false;
        }

        if (TryDequeuePendingTurn(out var queuedTurn)) {
            await SendPromptToConversationAsync(queuedTurn.Text, queuedTurn.ConversationId, queuedTurn.EnqueuedUtc).ConfigureAwait(false);
            return true;
        }

        if (GetQueuedPromptAfterLoginCount() == 0) {
            return false;
        }

        if (!honorAutoDispatch && (!_isAuthenticated || _loginInProgress)) {
            var started = await StartLoginFlowIfNeededAsync().ConfigureAwait(false);
            if (started) {
                var queuedCount = GetQueuedPromptAfterLoginCount();
                if (queuedCount > 0) {
                    await SetStatusAsync($"Waiting for sign-in... ({queuedCount}/{MaxQueuedTurns} queued)").ConfigureAwait(false);
                }
            }
            return false;
        }

        return await TryDispatchQueuedPromptAfterLoginAsync(honorAutoDispatch).ConfigureAwait(false);
    }

    private async Task RunNextQueuedTurnAsync() {
        if (_isSending) {
            await SetStatusAsync("Current turn is still running.").ConfigureAwait(false);
            return;
        }

        var dispatched = await DispatchNextQueuedTurnAsync(honorAutoDispatch: false).ConfigureAwait(false);
        if (dispatched) {
            return;
        }

        var queuedTurns = GetQueuedTurnCount();
        var queuedSignIn = GetQueuedPromptAfterLoginCount();
        var queuedTotal = queuedTurns + queuedSignIn;
        if (queuedTotal == 0) {
            await SetStatusAsync("No queued turns.").ConfigureAwait(false);
            await PublishSessionStateAsync().ConfigureAwait(false);
            return;
        }

        if (!_isAuthenticated && queuedSignIn > 0) {
            await SetStatusAsync($"Waiting for sign-in... ({queuedSignIn}/{MaxQueuedTurns} queued)").ConfigureAwait(false);
            return;
        }

        await SetStatusAsync("Queued turns are waiting.").ConfigureAwait(false);
        await PublishSessionStateAsync().ConfigureAwait(false);
    }

    private async Task ClearQueuedTurnsAsync() {
        var clearedPending = ClearPendingTurns();
        var clearedSignIn = ClearQueuedPromptsAfterLogin();
        var clearedTotal = clearedPending + clearedSignIn;
        if (clearedTotal <= 0) {
            await SetStatusAsync("No queued turns to clear.").ConfigureAwait(false);
            await PublishSessionStateAsync().ConfigureAwait(false);
            return;
        }

        await SetStatusAsync($"Cleared queued turns ({clearedTotal} removed).").ConfigureAwait(false);
        await PublishSessionStateAsync().ConfigureAwait(false);
    }

    private async Task CancelActiveTurnAsync() {
        if (!_isSending || string.IsNullOrWhiteSpace(_activeTurnRequestId)) {
            await SetStatusAsync(SessionStatus.NoActiveTurnToCancel()).ConfigureAwait(false);
            return;
        }

        var client = _client;
        if (client is null) {
            await SetStatusAsync(SessionStatus.Disconnected()).ConfigureAwait(false);
            return;
        }

        var chatRequestId = _activeTurnRequestId!;
        _cancelRequestedTurnRequestId = chatRequestId;
        await SetStatusAsync(SessionStatus.Canceling()).ConfigureAwait(false);
        await PublishSessionStateAsync().ConfigureAwait(false);

        try {
            var cancelRequest = new CancelChatRequest {
                RequestId = NextId(),
                ChatRequestId = chatRequestId
            };

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            _ = await client.RequestAsync<AckMessage>(cancelRequest, cts.Token).ConfigureAwait(false);
        } catch (Exception ex) {
            if (VerboseServiceLogs || _debugMode) {
                AppendSystem(SystemNotice.CancelRequestFailed(ex.Message));
            }
        }
    }

    private async Task SendClipboardAsync() {
        var data = Clipboard.GetContent();
        if (!data.Contains(StandardDataFormats.Text)) {
            await SetStatusAsync(SessionStatus.ClipboardHasNoText()).ConfigureAwait(true);
            return;
        }

        var text = (await data.GetTextAsync().AsTask().ConfigureAwait(true)).Trim();
        if (string.IsNullOrWhiteSpace(text)) {
            await SetStatusAsync(SessionStatus.ClipboardEmpty()).ConfigureAwait(true);
            return;
        }

        if (_pendingLoginPrompt is { } prompt) {
            await SubmitLoginPromptAsync(prompt.LoginId, prompt.PromptId, text).ConfigureAwait(true);
            _pendingLoginPrompt = null;
            return;
        }

        await SendPromptAsync(text).ConfigureAwait(true);
    }

    private void CopyStartupLogToClipboard() {
        var logPath = Path.Combine(Path.GetTempPath(), "IntelligenceX.Chat", "app-startup.log");
        if (!File.Exists(logPath)) {
            AppendSystem("Startup log not found: " + logPath);
            return;
        }

        string text;
        try {
            text = File.ReadAllText(logPath);
        } catch (Exception ex) {
            AppendSystem("Failed to read startup log: " + ex.Message);
            return;
        }

        if (string.IsNullOrWhiteSpace(text)) {
            AppendSystem("Startup log is empty: " + logPath);
            return;
        }

        var dp = new DataPackage();
        dp.SetText(text);
        Clipboard.SetContent(dp);
        Clipboard.Flush();
        AppendSystem("Startup log copied to clipboard.");
    }

    private async Task RestartSidecarAsync() {
        AppendSystem("Restarting local runtime...");
        _isConnected = false;
        _isAuthenticated = false;
        _loginInProgress = false;
        StopAutoReconnectLoop();
        await SetStatusAsync(SessionStatus.Connecting()).ConfigureAwait(true);
        await DisposeClientAsync().ConfigureAwait(true);
        StopServiceIfOwned();
        await ConnectAsync(fromUserAction: true).ConfigureAwait(true);
    }

    private static string NormalizeRequestId(string? requestId) {
        return (requestId ?? string.Empty).Trim();
    }

    private bool IsActiveTurnRequest(string? requestId) {
        var id = NormalizeRequestId(requestId);
        if (id.Length == 0) {
            return false;
        }

        return !string.IsNullOrWhiteSpace(_activeTurnRequestId)
               && string.Equals(id, _activeTurnRequestId, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsLatestTurnRequest(string? requestId) {
        var id = NormalizeRequestId(requestId);
        if (id.Length == 0) {
            return false;
        }

        return !string.IsNullOrWhiteSpace(_latestTurnRequestId)
               && string.Equals(id, _latestTurnRequestId, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsActiveKickoffRequest(string? requestId) {
        var id = NormalizeRequestId(requestId);
        if (id.Length == 0) {
            return false;
        }

        return !string.IsNullOrWhiteSpace(_activeKickoffRequestId)
               && string.Equals(id, _activeKickoffRequestId, StringComparison.OrdinalIgnoreCase);
    }

    private bool ShouldProcessLiveRequestMessage(string? requestId) {
        var id = NormalizeRequestId(requestId);
        if (id.Length == 0) {
            return _isSending || _modelKickoffInProgress;
        }

        return IsActiveTurnRequest(id) || IsActiveKickoffRequest(id);
    }

    private static bool IsTerminalChatStatus(string? status) {
        var normalized = (status ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        return string.Equals(normalized, "completed", StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalized, "done", StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalized, "finished", StringComparison.OrdinalIgnoreCase);
    }

    private void AppendStartupToolHealthWarningsFromPolicy() {
        var warnings = _sessionPolicy?.StartupWarnings;
        if (warnings is not { Length: > 0 }) {
            return;
        }

        var toolHealthWarnings = warnings
            .Where(static warning => warning.Contains("[tool health]", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (toolHealthWarnings.Length == 0) {
            return;
        }

        var signature = string.Join("|", toolHealthWarnings.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase));
        if (!_startupToolHealthWarningSignatures.Add(signature)) {
            return;
        }

        const int maxShown = 4;
        var shown = toolHealthWarnings.Length <= maxShown
            ? toolHealthWarnings
            : toolHealthWarnings.Take(maxShown).ToArray();

        var lines = new List<string>(shown.Length + 5) {
            "[warning] Tool health checks need attention",
            string.Empty,
            $"Found {toolHealthWarnings.Length} startup tool health warning(s):"
        };
        for (var i = 0; i < shown.Length; i++) {
            lines.Add("- " + shown[i].Trim());
        }
        if (toolHealthWarnings.Length > shown.Length) {
            lines.Add($"- +{toolHealthWarnings.Length - shown.Length} more");
        }
        lines.Add(string.Empty);
        lines.Add("Check the runtime policy panel for the full startup warning list.");

        AppendSystem(string.Join(Environment.NewLine, lines));
    }

}
