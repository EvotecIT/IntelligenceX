using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.App.Conversation;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;

namespace IntelligenceX.Chat.App;

public sealed partial class MainWindow : Window {
    private async Task LoginAsync() {
        await StartLoginFlowIfNeededAsync().ConfigureAwait(false);
    }

    private async Task ReLoginFromMenuAsync() {
        await ReLoginAsync().ConfigureAwait(false);
    }

    private async Task SwitchAccountFromMenuAsync() {
        await SwitchAccountAsync().ConfigureAwait(false);
    }

    private async Task SendPromptAsync(
        string text,
        string? preferredConversationId = null,
        DateTime? queuedAtUtc = null,
        bool skipUserBubble = false) {
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

        if (_modelKickoffInProgress) {
            await CancelModelKickoffIfRunningAsync().ConfigureAwait(false);
        }

        var turn = await PrepareChatTurnAsync(text, skipUserBubble).ConfigureAwait(false);
        if (turn is null) {
            return;
        }

        // Keep user bubble rendering immediate, but still validate connectivity
        // before we enter active send state.
        if (!await EnsureConnectedAsync().ConfigureAwait(false)) {
            await ApplyTurnFailureAsync(turn, AssistantTurnOutcome.Disconnected()).ConfigureAwait(false);
            await SetStatusAsync(SessionStatus.Disconnected()).ConfigureAwait(false);
            return;
        }
        if (_client is null) {
            await ApplyTurnFailureAsync(turn, AssistantTurnOutcome.Disconnected()).ConfigureAwait(false);
            await SetStatusAsync(SessionStatus.Disconnected()).ConfigureAwait(false);
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
        _latestServiceActivityText = string.Empty;
        _activeTurnQueueWaitMs = queueWaitMs;
        ResetActivityTimeline();
        StartTurnWatchdog();
        CancellationTokenSource? turnRequestCts = null;
        try {
            turnRequestCts = new CancellationTokenSource();
            lock (_activeTurnLifecycleSync) {
                _activeTurnRequestId = requestId;
                _latestTurnRequestId = requestId;
                _cancelRequestedTurnRequestId = null;
                _activeTurnRequestCts = turnRequestCts;
                _activeRequestConversationId = turn.ConversationId;
            }
            ClearToolRoutingInsights();
            await SetActivityAsync("Sending request to runtime...").ConfigureAwait(false);
            try {
                await PublishSessionStateAsync().ConfigureAwait(false);
            } finally {
                // Ensure tools state is refreshed after routing reset even if session publish faults.
                await PublishOptionsStateSafeAsync().ConfigureAwait(false);
            }

            await ExecuteChatTurnWithReconnectAsync(turn, turnRequestCts.Token).ConfigureAwait(false);
        } finally {
            StopTurnWatchdog();
            _isSending = false;
            lock (_activeTurnLifecycleSync) {
                if (string.Equals(_activeTurnRequestId, requestId, StringComparison.Ordinal)) {
                    _activeTurnRequestId = null;
                    if (string.Equals(_activeRequestConversationId, turn.ConversationId, StringComparison.OrdinalIgnoreCase)) {
                        _activeRequestConversationId = null;
                    }
                }

                if (string.Equals(_cancelRequestedTurnRequestId, requestId, StringComparison.Ordinal)) {
                    _cancelRequestedTurnRequestId = null;
                }

                if (ReferenceEquals(_activeTurnRequestCts, turnRequestCts)) {
                    _activeTurnRequestCts = null;
                }

                if (turnRequestCts is not null) {
                    try {
                        turnRequestCts.Dispose();
                    } catch (ObjectDisposedException) {
                        // Cancellation may race with completion; disposed CTS is safe to ignore.
                    }
                }
            }
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
                } else {
                    await RestoreHeaderStatusAfterTurnIfNeededAsync(requestId).ConfigureAwait(false);
                }
            }
        }
    }

    private async Task RestoreHeaderStatusAfterTurnIfNeededAsync(string completedRequestId) {
        if (string.IsNullOrWhiteSpace(completedRequestId) || !IsLatestTurnRequest(completedRequestId)) {
            return;
        }

        var status = (_statusText ?? string.Empty).Trim();
        if (status.Length == 0) {
            return;
        }

        if (!string.Equals(status, "Sending request to runtime...", StringComparison.OrdinalIgnoreCase)
            && !status.StartsWith("Last turn failed:", StringComparison.OrdinalIgnoreCase)) {
            return;
        }

        await SetStatusAsync(SessionStatus.ForConnection(_isConnected, IsEffectivelyAuthenticatedForCurrentTransport())).ConfigureAwait(false);
    }

    private async Task SendPromptToConversationAsync(
        string text,
        string? conversationId,
        DateTime? queuedAtUtc = null,
        bool skipUserBubble = false) {
        var normalized = (conversationId ?? string.Empty).Trim();
        if (_isSending) {
            await SendPromptAsync(
                    text,
                    normalized.Length == 0 ? _activeConversationId : normalized,
                    queuedAtUtc,
                    skipUserBubble)
                .ConfigureAwait(false);
            return;
        }

        if (normalized.Length == 0 || string.Equals(normalized, _activeConversationId, StringComparison.OrdinalIgnoreCase)) {
            await SendPromptAsync(text, normalized, queuedAtUtc, skipUserBubble).ConfigureAwait(false);
            return;
        }

        var target = FindConversationById(normalized);
        if (target is null) {
            await SendPromptAsync(text, normalized, queuedAtUtc, skipUserBubble).ConfigureAwait(false);
            return;
        }

        await SwitchConversationAsync(target.Id).ConfigureAwait(false);
        await SendPromptAsync(text, normalized, queuedAtUtc, skipUserBubble).ConfigureAwait(false);
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

    private bool TryEnqueuePromptAfterLogin(
        string text,
        string? conversationId,
        out int queuedCount,
        bool skipUserBubbleOnDispatch = false) {
        var trimmedText = (text ?? string.Empty).Trim();
        var trimmedConversationId = (conversationId ?? string.Empty).Trim();
        lock (_queuedAfterLoginSync) {
            if (trimmedText.Length == 0 || _queuedTurnsAfterLogin.Count >= MaxQueuedTurns) {
                queuedCount = _queuedTurnsAfterLogin.Count;
                return false;
            }

            _queuedTurnsAfterLogin.Enqueue(new QueuedTurn(trimmedText, trimmedConversationId, DateTime.UtcNow, skipUserBubbleOnDispatch));
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

    private static string BuildUsageLimitQueuedPromptStatus(int? retryAfterMinutes) {
        if (retryAfterMinutes.HasValue && retryAfterMinutes.Value > 0) {
            return "Queued prompt paused by account limit (" + retryAfterMinutes.Value + "m remaining). Use Switch Account to run now.";
        }

        return "Queued prompt paused by account limit. Use Switch Account to run now.";
    }

    private async Task<bool> TryDispatchQueuedPromptAfterLoginAsync(bool honorAutoDispatch = true) {
        if (_isSending || !IsEffectivelyAuthenticatedForCurrentTransport() || _loginInProgress || (honorAutoDispatch && !_queueAutoDispatchEnabled)) {
            return false;
        }

        if (IsActiveUsageLimitDispatchBlocked(out var retryAfterMinutes)) {
            await SetStatusAsync(BuildUsageLimitQueuedPromptStatus(retryAfterMinutes)).ConfigureAwait(false);
            return false;
        }

        if (!TryDequeuePromptAfterLogin(out var queuedTurn)) {
            return false;
        }

        await SendPromptToConversationAsync(
                queuedTurn.Text,
                queuedTurn.ConversationId,
                queuedTurn.EnqueuedUtc,
                queuedTurn.SkipUserBubbleOnDispatch)
            .ConfigureAwait(false);
        return true;
    }

    private async Task<bool> DispatchNextQueuedTurnAsync(bool honorAutoDispatch) {
        if (_isSending || (honorAutoDispatch && !_queueAutoDispatchEnabled)) {
            return false;
        }

        if (TryDequeuePendingTurn(out var queuedTurn)) {
            await SendPromptToConversationAsync(
                    queuedTurn.Text,
                    queuedTurn.ConversationId,
                    queuedTurn.EnqueuedUtc,
                    queuedTurn.SkipUserBubbleOnDispatch)
                .ConfigureAwait(false);
            return true;
        }

        if (GetQueuedPromptAfterLoginCount() == 0) {
            return false;
        }

        if (!honorAutoDispatch && (!IsEffectivelyAuthenticatedForCurrentTransport() || _loginInProgress)) {
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

        if (!IsEffectivelyAuthenticatedForCurrentTransport() && queuedSignIn > 0) {
            await SetStatusAsync($"Waiting for sign-in... ({queuedSignIn}/{MaxQueuedTurns} queued)").ConfigureAwait(false);
            return;
        }

        if (queuedSignIn > 0 && IsActiveUsageLimitDispatchBlocked(out var retryAfterMinutes)) {
            await SetStatusAsync(BuildUsageLimitQueuedPromptStatus(retryAfterMinutes)).ConfigureAwait(false);
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
        string chatRequestId;
        lock (_activeTurnLifecycleSync) {
            if (!_isSending || string.IsNullOrWhiteSpace(_activeTurnRequestId)) {
                chatRequestId = string.Empty;
            } else {
                chatRequestId = _activeTurnRequestId!;
                _cancelRequestedTurnRequestId = chatRequestId;
                try {
                    _activeTurnRequestCts?.Cancel();
                } catch (ObjectDisposedException) {
                    // Turn completion may dispose the CTS before cancellation request arrives.
                }
            }
        }

        if (string.IsNullOrWhiteSpace(chatRequestId)) {
            await SetStatusAsync(SessionStatus.NoActiveTurnToCancel()).ConfigureAwait(false);
            return;
        }

        var client = _client;
        if (client is null) {
            await SetStatusAsync(SessionStatus.Disconnected()).ConfigureAwait(false);
            return;
        }

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

}
