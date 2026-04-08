using System;
using System.Collections.Generic;
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
        var dispatchStartedUtc = DateTime.UtcNow;
        text = (text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text)) {
            return;
        }

        MarkStartupInteractivePriorityRequested();

        // If a sign-in-queued turn matches this manual resend, drop the queued copy so
        // post-login auto-dispatch does not produce a duplicate assistant turn.
        if (!skipUserBubble && !queuedAtUtc.HasValue) {
            var targetConversationId = string.IsNullOrWhiteSpace(preferredConversationId)
                ? _activeConversationId
                : preferredConversationId;
            _ = TryRemoveEquivalentQueuedPromptAfterLogin(text, targetConversationId, out _);
        }

        if (!TryBeginTurnDispatchStartup()) {
            var queueConversationId = string.IsNullOrWhiteSpace(preferredConversationId)
                ? _activeConversationId
                : preferredConversationId;
            if (ShouldSuppressEquivalentManualPromptDuringActiveQueuedDispatch(
                    incomingText: text,
                    incomingConversationId: queueConversationId,
                    incomingIsQueuedDispatch: queuedAtUtc.HasValue,
                    incomingSkipUserBubble: skipUserBubble,
                    activeDispatchFromQueuedAfterLogin: GetActiveTurnFromQueuedAfterLoginForDispatchDedup(),
                    activeDispatchText: GetActiveTurnNormalizedPromptTextForDispatchDedup(),
                    activeDispatchConversationId: GetActiveTurnPromptConversationIdForDispatchDedup(),
                    startupScopeConversationId: _activeConversationId)) {
                await SetStatusAsync("Equivalent queued prompt is already running.").ConfigureAwait(false);
                await PublishSessionStateAsync().ConfigureAwait(false);
                return;
            }

            if (TryEnqueuePendingTurn(text, queueConversationId, out var queuedCount)) {
                await SetStatusAsync($"Queued next turn ({queuedCount}/{MaxQueuedTurns})").ConfigureAwait(false);
            } else {
                await SetStatusAsync("Turn queue is full. Wait for the current turn to finish or press Stop.").ConfigureAwait(false);
            }

            await PublishSessionStateAsync().ConfigureAwait(false);
            return;
        }

        var dispatchRequestId = string.Empty;
        Task<bool>? connectWarmupTask = null;
        DateTime? connectWarmupStartedUtc = null;
        try {
            long? queueWaitMs = null;
            if (queuedAtUtc.HasValue && queuedAtUtc.Value.Kind == DateTimeKind.Utc) {
                var elapsed = DateTime.UtcNow - queuedAtUtc.Value;
                if (elapsed.TotalMilliseconds > 0) {
                    queueWaitMs = (long)Math.Round(elapsed.TotalMilliseconds);
                }
            }

            // Start reconnect warm-up in parallel with turn preparation so auth checks
            // and user bubble rendering do not wait on pipe/service startup.
            if (_client is null || !_isConnected) {
                connectWarmupStartedUtc = DateTime.UtcNow;
                connectWarmupTask = EnsureConnectedAsync(deferPostConnectMetadataSync: true);
            }

            var turn = await PrepareChatTurnAsync(text, skipUserBubble).ConfigureAwait(false);
            if (turn is null) {
                return;
            }

            dispatchRequestId = turn.RequestId;

            var activeUsageIdentity = ResolveActiveUsageIdentity();
            if (TryGetActiveProviderCircuitOpen(activeUsageIdentity, out var circuitRemaining, out _)) {
                var waitSeconds = Math.Max(1, (int)Math.Ceiling(circuitRemaining.TotalSeconds));
                await ApplyTurnFailureAsync(
                        turn,
                        AssistantTurnOutcome.Error(
                            "Provider cooldown active (" + waitSeconds + "s). "
                            + "Retry in about " + waitSeconds + " seconds."))
                    .ConfigureAwait(false);
                await SetStatusAsync(
                        "Provider cooldown active (" + waitSeconds + "s). Retrying now would likely fail.",
                        SessionStatusTone.Warn)
                    .ConfigureAwait(false);
                await SetActivityAsync(
                        "Runtime is cooling down after transient failures. Retry in about " + waitSeconds + "s.")
                    .ConfigureAwait(false);
                await PublishSessionStateAsync().ConfigureAwait(false);
                return;
            }

            RegisterTurnDispatchStart(turn.RequestId, activeUsageIdentity, dispatchStartedUtc, queueWaitMs, turn.AuthProbeMs);

            if (_modelKickoffInProgress) {
                await CancelModelKickoffIfRunningAsync().ConfigureAwait(false);
            }

            // Keep user bubble rendering immediate, but still validate connectivity
            // before we enter active send state.
            var connectStartedUtc = connectWarmupStartedUtc ?? DateTime.UtcNow;
            var connected = connectWarmupTask is null
                // Hot path: if we already have a connected client, avoid an eager liveness
                // probe/reconnect before dispatch. Request-level recovery handles stale pipes.
                ? (_client is not null && _isConnected
                    ? true
                    : await EnsureConnectedAsync(deferPostConnectMetadataSync: true).ConfigureAwait(false))
                : await connectWarmupTask.ConfigureAwait(false);
            connectWarmupTask = null;
            var connectCompletedUtc = DateTime.UtcNow;
            MarkTurnConnectStage(turn.RequestId, connectStartedUtc, connectCompletedUtc, connected);
            if (!connected) {
                await ApplyTurnFailureAsync(turn, AssistantTurnOutcome.Disconnected()).ConfigureAwait(false);
                await SetStatusAsync(SessionStatus.Disconnected()).ConfigureAwait(false);
                return;
            }
            if (_client is null) {
                MarkTurnConnectStage(turn.RequestId, connectStartedUtc, DateTime.UtcNow, connected: false);
                await ApplyTurnFailureAsync(turn, AssistantTurnOutcome.Disconnected()).ConfigureAwait(false);
                await SetStatusAsync(SessionStatus.Disconnected()).ConfigureAwait(false);
                return;
            }

            ResetActiveTurnAssistantVisuals(turn.ConversationId);
            PromoteTurnDispatchStartupToSending();
            var requestId = turn.RequestId;
            _latestServiceActivityText = string.Empty;
            _activeTurnQueueWaitMs = queueWaitMs;
            ResetActivityTimeline();
            StartTurnWatchdog();
            CancellationTokenSource? turnRequestCts = null;
            try {
                turnRequestCts = new CancellationTokenSource();
                var activeTurnFromQueuedAfterLogin = queuedAtUtc.HasValue && skipUserBubble;
                var activeTurnPromptText = NormalizeQueuedPromptTextForDispatch(turn.UserText);
                var activeTurnPromptConversationId = NormalizeQueuedPromptConversationId(turn.ConversationId);
                lock (_activeTurnLifecycleSync) {
                    _activeTurnRequestId = requestId;
                    _latestTurnRequestId = requestId;
                    _cancelRequestedTurnRequestId = null;
                    _activeTurnRequestCts = turnRequestCts;
                    _activeRequestConversationId = turn.ConversationId;
                    _activeTurnFromQueuedAfterLogin = activeTurnFromQueuedAfterLogin;
                    _activeTurnNormalizedPromptText = activeTurnPromptText;
                    _activeTurnPromptConversationId = activeTurnPromptConversationId;
                }
                ClearToolRoutingInsights();
                await SetActivityAsync("Sending request to runtime...").ConfigureAwait(false);
                // Keep dispatch path hot: publish state asynchronously while request starts.
                _ = PublishTurnStartUiStateBestEffortAsync();

                await ExecuteChatTurnWithReconnectAsync(turn, turnRequestCts.Token).ConfigureAwait(false);
            } finally {
                StopTurnWatchdog();
                CompleteActiveTurnDispatchSend();
                lock (_activeTurnLifecycleSync) {
                    if (string.Equals(_activeTurnRequestId, requestId, StringComparison.Ordinal)) {
                        _activeTurnRequestId = null;
                        if (string.Equals(_activeRequestConversationId, turn.ConversationId, StringComparison.OrdinalIgnoreCase)) {
                            _activeRequestConversationId = null;
                        }

                        _activeTurnFromQueuedAfterLogin = false;
                        _activeTurnNormalizedPromptText = string.Empty;
                        _activeTurnPromptConversationId = string.Empty;
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
                _assistantStreamingState.ClearReceivedDelta();
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
        } finally {
            if (connectWarmupTask is not null) {
                _ = ObserveConnectWarmupCompletionAsync(connectWarmupTask);
            }

            if (TryClearTurnDispatchStartup()) {
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
                    } else if (!string.IsNullOrWhiteSpace(dispatchRequestId)) {
                        await RestoreHeaderStatusAfterTurnIfNeededAsync(dispatchRequestId).ConfigureAwait(false);
                    }
                }
            }
        }
    }

    private async Task ObserveConnectWarmupCompletionAsync(Task<bool> connectWarmupTask) {
        try {
            _ = await connectWarmupTask.ConfigureAwait(false);
        } catch (Exception ex) {
            if (VerboseServiceLogs || _debugMode) {
                await AppendSystemBestEffortAsync("Background connect warm-up failed: " + ex.Message).ConfigureAwait(false);
            }
        }
    }

    private async Task PublishTurnStartUiStateBestEffortAsync() {
        try {
            try {
                await PublishSessionStateAsync().ConfigureAwait(false);
            } finally {
                // Ensure tools state is refreshed after routing reset even if session publish faults.
                await PublishOptionsStateSafeAsync().ConfigureAwait(false);
            }
        } catch (Exception ex) {
            if (VerboseServiceLogs || _debugMode) {
                await AppendSystemBestEffortAsync("Turn-start UI state publish failed: " + ex.Message).ConfigureAwait(false);
            }
        }
    }

    private async Task RestoreHeaderStatusAfterTurnIfNeededAsync(string completedRequestId) {
        var isLatestTurnRequest = !string.IsNullOrWhiteSpace(completedRequestId) && IsLatestTurnRequest(completedRequestId);
        var shouldRestore = ShouldRestoreConnectionStatusAfterTurn(
            currentStatus: _statusText,
            isLatestTurnRequest: isLatestTurnRequest,
            startupMetadataSyncQueued: Volatile.Read(ref _startupConnectMetadataDeferredQueued) != 0,
            startupMetadataSyncInProgress: Volatile.Read(ref _startupMetadataSyncInProgress) != 0,
            startupFlowState: Volatile.Read(ref _startupFlowState));
        if (!shouldRestore) {
            return;
        }

        await SetStatusAsync(ResolveConnectionStatusForCurrentTransport()).ConfigureAwait(false);
    }

    internal static bool ShouldRestoreConnectionStatusAfterTurn(
        string? currentStatus,
        bool isLatestTurnRequest,
        bool startupMetadataSyncQueued,
        bool startupMetadataSyncInProgress,
        int startupFlowState) {
        if (!isLatestTurnRequest) {
            return false;
        }

        var status = (currentStatus ?? string.Empty).Trim();
        if (status.Length == 0) {
            return false;
        }

        if (string.Equals(status, "Sending request to runtime...", StringComparison.OrdinalIgnoreCase)
            || status.StartsWith("Last turn failed:", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        if (startupMetadataSyncQueued
            || startupMetadataSyncInProgress
            || startupFlowState == StartupFlowStateRunning) {
            return false;
        }

        if (status.StartsWith("Starting runtime...", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        return status.IndexOf("(phase " + StartupStatusPhaseStartupConnect, StringComparison.OrdinalIgnoreCase) >= 0
               || status.IndexOf("(phase " + StartupStatusPhaseStartupMetadataSync, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private async Task SendPromptToConversationAsync(
        string text,
        string? conversationId,
        DateTime? queuedAtUtc = null,
        bool skipUserBubble = false) {
        var normalized = (conversationId ?? string.Empty).Trim();
        if (IsTurnDispatchInProgress()) {
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
            target = CreateConversationRuntime(DefaultConversationTitle, normalized);
            _conversations.Add(target);
            TrimConversationsToLimit();
            ActivateConversation(target.Id);
            _modelKickoffAttempted = _messages.Count > 0;
            _autoSignInAttempted = _appState.OnboardingCompleted || AnyConversationHasMessages();
        } else {
            await SwitchConversationAsync(target.Id).ConfigureAwait(false);
        }

        await SendPromptAsync(text, normalized, queuedAtUtc, skipUserBubble).ConfigureAwait(false);
    }

    private bool GetActiveTurnFromQueuedAfterLoginForDispatchDedup() {
        lock (_activeTurnLifecycleSync) {
            return _activeTurnFromQueuedAfterLogin;
        }
    }

    private string GetActiveTurnNormalizedPromptTextForDispatchDedup() {
        lock (_activeTurnLifecycleSync) {
            return _activeTurnNormalizedPromptText;
        }
    }

    private string GetActiveTurnPromptConversationIdForDispatchDedup() {
        lock (_activeTurnLifecycleSync) {
            return _activeTurnPromptConversationId;
        }
    }

    internal static bool ShouldSuppressEquivalentManualPromptDuringActiveQueuedDispatch(
        string? incomingText,
        string? incomingConversationId,
        bool incomingIsQueuedDispatch,
        bool incomingSkipUserBubble,
        bool activeDispatchFromQueuedAfterLogin,
        string? activeDispatchText,
        string? activeDispatchConversationId,
        string? startupScopeConversationId) {
        if (incomingSkipUserBubble || incomingIsQueuedDispatch || !activeDispatchFromQueuedAfterLogin) {
            return false;
        }

        return AreQueuedPromptsEquivalentForDispatch(
            activeDispatchText,
            activeDispatchConversationId,
            incomingText,
            incomingConversationId,
            allowOneSidedMissingConversationId: true,
            allowBothMissingConversationIdsInStartupScope: true,
            startupScopeConversationId: startupScopeConversationId);
    }

    private bool TryEnqueuePendingTurn(string text, string? conversationId, out int queuedCount) {
        var trimmedText = (text ?? string.Empty).Trim();
        var trimmedConversationId = (conversationId ?? string.Empty).Trim();
        var enqueued = false;
        lock (_pendingTurnQueueSync) {
            if (trimmedText.Length == 0 || _pendingTurns.Count >= MaxQueuedTurns) {
                queuedCount = _pendingTurns.Count;
                return false;
            }

            _pendingTurns.Enqueue(new QueuedTurn(trimmedText, trimmedConversationId, DateTime.UtcNow));
            queuedCount = _pendingTurns.Count;
            enqueued = true;
        }

        if (enqueued) {
            QueuePersistAppState();
        }

        return enqueued;
    }

    private bool TryDequeuePendingTurn(out QueuedTurn queuedTurn) {
        var dequeued = false;
        lock (_pendingTurnQueueSync) {
            if (_pendingTurns.Count == 0) {
                queuedTurn = null!;
                return false;
            }

            queuedTurn = _pendingTurns.Dequeue();
            dequeued = true;
        }

        if (dequeued) {
            QueuePersistAppState();
        }

        return dequeued;
    }

    private int GetQueuedTurnCount() {
        lock (_pendingTurnQueueSync) {
            return _pendingTurns.Count;
        }
    }

    private int ClearPendingTurns() {
        var cleared = 0;
        lock (_pendingTurnQueueSync) {
            cleared = _pendingTurns.Count;
            _pendingTurns.Clear();
        }

        if (cleared > 0) {
            QueuePersistAppState();
        }

        return cleared;
    }

    private bool TryEnqueuePromptAfterLogin(
        string text,
        string? conversationId,
        out int queuedCount,
        bool skipUserBubbleOnDispatch = false) {
        var trimmedText = (text ?? string.Empty).Trim();
        var trimmedConversationId = (conversationId ?? string.Empty).Trim();
        var startupScopeConversationId = trimmedConversationId.Length == 0
            ? NormalizeQueuedPromptConversationId(_activeConversationId)
            : trimmedConversationId;
        var enqueued = false;
        lock (_queuedAfterLoginSync) {
            if (trimmedText.Length == 0 || _queuedTurnsAfterLogin.Count >= MaxQueuedTurns) {
                queuedCount = _queuedTurnsAfterLogin.Count;
                return false;
            }

            foreach (var pending in _queuedTurnsAfterLogin) {
                if (!AreQueuedPromptsEquivalentForDispatch(
                        pending.Text,
                        pending.ConversationId,
                        trimmedText,
                        trimmedConversationId,
                        allowOneSidedMissingConversationId: true,
                        allowBothMissingConversationIdsInStartupScope: true,
                        startupScopeConversationId: startupScopeConversationId)) {
                    continue;
                }

                queuedCount = _queuedTurnsAfterLogin.Count;
                return true;
            }

            _queuedTurnsAfterLogin.Enqueue(new QueuedTurn(trimmedText, trimmedConversationId, DateTime.UtcNow, skipUserBubbleOnDispatch));
            queuedCount = _queuedTurnsAfterLogin.Count;
            enqueued = true;
        }

        if (enqueued) {
            QueuePersistAppState();
        }

        return enqueued;
    }

    private bool TryRemoveEquivalentQueuedPromptAfterLogin(
        string text,
        string? conversationId,
        out int remainingCount) {
        var normalizedText = NormalizeQueuedPromptTextForDispatch(text);
        var normalizedConversationId = NormalizeQueuedPromptConversationId(conversationId);
        var startupScopeConversationId = normalizedConversationId.Length == 0
            ? NormalizeQueuedPromptConversationId(_activeConversationId)
            : normalizedConversationId;
        var removed = false;
        lock (_queuedAfterLoginSync) {
            if (_queuedTurnsAfterLogin.Count == 0
                || normalizedText.Length == 0) {
                remainingCount = _queuedTurnsAfterLogin.Count;
                return false;
            }

            var originalCount = _queuedTurnsAfterLogin.Count;
            for (var i = 0; i < originalCount; i++) {
                var pending = _queuedTurnsAfterLogin.Dequeue();
                if (!removed
                    && AreQueuedPromptsEquivalentForDispatch(
                        pending.Text,
                        pending.ConversationId,
                        normalizedText,
                        normalizedConversationId,
                        allowOneSidedMissingConversationId: true,
                        allowBothMissingConversationIdsInStartupScope: true,
                        startupScopeConversationId: startupScopeConversationId)) {
                    removed = true;
                    continue;
                }

                _queuedTurnsAfterLogin.Enqueue(pending);
            }

            remainingCount = _queuedTurnsAfterLogin.Count;
        }

        if (removed) {
            QueuePersistAppState();
        }

        return removed;
    }

    internal static bool AreQueuedPromptsEquivalentForDispatch(
        string? leftText,
        string? leftConversationId,
        string? rightText,
        string? rightConversationId,
        bool allowOneSidedMissingConversationId = false,
        bool allowBothMissingConversationIdsInStartupScope = false,
        string? startupScopeConversationId = null) {
        var normalizedLeftText = NormalizeQueuedPromptTextForDispatch(leftText);
        var normalizedRightText = NormalizeQueuedPromptTextForDispatch(rightText);
        if (normalizedLeftText.Length == 0 || normalizedRightText.Length == 0) {
            return false;
        }

        if (!string.Equals(normalizedLeftText, normalizedRightText, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        var normalizedLeftConversationId = NormalizeQueuedPromptConversationId(leftConversationId);
        var normalizedRightConversationId = NormalizeQueuedPromptConversationId(rightConversationId);
        if (normalizedLeftConversationId.Length == 0 && normalizedRightConversationId.Length == 0) {
            // Startup/login recovery can capture queued and manual prompt attempts before
            // any stable conversation id is assigned. Allow callers in that bounded path
            // to dedupe identical text-only replays.
            return allowBothMissingConversationIdsInStartupScope;
        }

        if (string.Equals(normalizedLeftConversationId, normalizedRightConversationId, StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        if (!allowOneSidedMissingConversationId) {
            return false;
        }

        var normalizedScopeConversationId = NormalizeQueuedPromptConversationId(startupScopeConversationId);
        if (normalizedScopeConversationId.Length == 0) {
            return false;
        }

        var scopedConversationId = normalizedLeftConversationId.Length == 0
            ? normalizedRightConversationId
            : normalizedLeftConversationId;
        if (scopedConversationId.Length == 0) {
            return false;
        }

        // Startup/login queue entries can be captured before a stable conversation id is assigned.
        // Only startup/login-gated callers should opt into this one-sided-empty-id fallback,
        // and only for the active scoped conversation.
        return (normalizedLeftConversationId.Length == 0 || normalizedRightConversationId.Length == 0)
               && string.Equals(scopedConversationId, normalizedScopeConversationId, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeQueuedPromptTextForDispatch(string? text) {
        var source = (text ?? string.Empty).Trim();
        if (source.Length == 0) {
            return string.Empty;
        }

        var sb = new System.Text.StringBuilder(source.Length);
        var inWhitespace = false;
        for (var i = 0; i < source.Length; i++) {
            var ch = source[i];
            if (char.IsWhiteSpace(ch)) {
                if (!inWhitespace) {
                    sb.Append(' ');
                    inWhitespace = true;
                }
                continue;
            }

            inWhitespace = false;
            sb.Append(ch);
        }

        return sb.ToString().Trim();
    }

    private static string NormalizeQueuedPromptConversationId(string? conversationId) {
        return (conversationId ?? string.Empty).Trim();
    }

    private bool TryDequeuePromptAfterLogin(out QueuedTurn queuedTurn) {
        var dequeued = false;
        lock (_queuedAfterLoginSync) {
            if (_queuedTurnsAfterLogin.Count == 0) {
                queuedTurn = null!;
                return false;
            }

            queuedTurn = _queuedTurnsAfterLogin.Dequeue();
            dequeued = true;
        }

        if (dequeued) {
            QueuePersistAppState();
        }

        return dequeued;
    }

    private int GetQueuedPromptAfterLoginCount() {
        lock (_queuedAfterLoginSync) {
            return _queuedTurnsAfterLogin.Count;
        }
    }

    private int ClearQueuedPromptsAfterLogin() {
        var cleared = 0;
        lock (_queuedAfterLoginSync) {
            cleared = _queuedTurnsAfterLogin.Count;
            _queuedTurnsAfterLogin.Clear();
        }

        if (cleared > 0) {
            QueuePersistAppState();
        }

        return cleared;
    }

    private List<ChatQueuedTurnState> BuildPendingTurnStateSnapshot() {
        lock (_pendingTurnQueueSync) {
            var snapshot = new List<ChatQueuedTurnState>(_pendingTurns.Count);
            foreach (var pending in _pendingTurns) {
                snapshot.Add(new ChatQueuedTurnState {
                    Text = pending.Text,
                    ConversationId = pending.ConversationId,
                    EnqueuedUtc = pending.EnqueuedUtc,
                    SkipUserBubbleOnDispatch = pending.SkipUserBubbleOnDispatch
                });
            }
            return snapshot;
        }
    }

    private List<ChatQueuedTurnState> BuildQueuedAfterLoginStateSnapshot() {
        lock (_queuedAfterLoginSync) {
            var snapshot = new List<ChatQueuedTurnState>(_queuedTurnsAfterLogin.Count);
            foreach (var pending in _queuedTurnsAfterLogin) {
                snapshot.Add(new ChatQueuedTurnState {
                    Text = pending.Text,
                    ConversationId = pending.ConversationId,
                    EnqueuedUtc = pending.EnqueuedUtc,
                    SkipUserBubbleOnDispatch = pending.SkipUserBubbleOnDispatch
                });
            }
            return snapshot;
        }
    }

    private void RestoreQueuedTurnsFromState(ChatAppState state) {
        if (state is null) {
            return;
        }

        lock (_pendingTurnQueueSync) {
            _pendingTurns.Clear();
            if (state.PendingTurns is { Count: > 0 }) {
                foreach (var pending in state.PendingTurns) {
                    if (pending is null) {
                        continue;
                    }

                    var text = (pending.Text ?? string.Empty).Trim();
                    if (text.Length == 0) {
                        continue;
                    }

                    if (_pendingTurns.Count >= MaxQueuedTurns) {
                        break;
                    }

                    var conversationId = (pending.ConversationId ?? string.Empty).Trim();
                    var enqueuedUtc = pending.EnqueuedUtc.Kind == DateTimeKind.Utc
                        ? pending.EnqueuedUtc
                        : pending.EnqueuedUtc.ToUniversalTime();
                    _pendingTurns.Enqueue(new QueuedTurn(text, conversationId, enqueuedUtc, pending.SkipUserBubbleOnDispatch));
                }
            }
        }

        lock (_queuedAfterLoginSync) {
            _queuedTurnsAfterLogin.Clear();
            if (state.QueuedTurnsAfterLogin is { Count: > 0 }) {
                foreach (var pending in state.QueuedTurnsAfterLogin) {
                    if (pending is null) {
                        continue;
                    }

                    var text = (pending.Text ?? string.Empty).Trim();
                    if (text.Length == 0) {
                        continue;
                    }

                    if (_queuedTurnsAfterLogin.Count >= MaxQueuedTurns) {
                        break;
                    }

                    var conversationId = (pending.ConversationId ?? string.Empty).Trim();
                    var enqueuedUtc = pending.EnqueuedUtc.Kind == DateTimeKind.Utc
                        ? pending.EnqueuedUtc
                        : pending.EnqueuedUtc.ToUniversalTime();
                    _queuedTurnsAfterLogin.Enqueue(new QueuedTurn(text, conversationId, enqueuedUtc, pending.SkipUserBubbleOnDispatch));
                }
            }
        }
    }

    private static string BuildUsageLimitQueuedPromptStatus(int? retryAfterMinutes) {
        _ = retryAfterMinutes;
        return SessionStatusFormatter.Format(SessionStatus.UsageLimitReached());
    }

    private static string BuildUsageLimitQueuedPromptActivity(string? accountLabel, int? retryAfterMinutes) {
        var normalizedAccountLabel = (accountLabel ?? string.Empty).Trim();
        var accountText = normalizedAccountLabel.Length == 0
            ? "this account"
            : normalizedAccountLabel;
        if (retryAfterMinutes.HasValue && retryAfterMinutes.Value > 0) {
            return "Queued prompt paused because " + accountText + " is usage-limited (" + retryAfterMinutes.Value + "m remaining). Use Switch Account to run now.";
        }

        return "Queued prompt paused because " + accountText + " is usage-limited. Use Switch Account to run now.";
    }

    private bool IsTurnDispatchInProgress() {
        lock (_activeTurnLifecycleSync) {
            return IsTurnDispatchInProgress(_isSending, _turnStartupInProgress);
        }
    }

    internal static bool IsTurnDispatchInProgress(bool isSending, bool turnStartupInProgress) {
        return isSending || turnStartupInProgress;
    }

    internal static bool TryBeginTurnDispatchStartup(ref bool isSending, ref bool turnStartupInProgress) {
        if (IsTurnDispatchInProgress(isSending, turnStartupInProgress)) {
            return false;
        }

        turnStartupInProgress = true;
        return true;
    }

    internal static void PromoteTurnDispatchStartupToSending(ref bool isSending, ref bool turnStartupInProgress) {
        isSending = true;
        turnStartupInProgress = false;
    }

    internal static bool TryClearTurnDispatchStartup(ref bool turnStartupInProgress) {
        if (!turnStartupInProgress) {
            return false;
        }

        turnStartupInProgress = false;
        return true;
    }

    private bool TryBeginTurnDispatchStartup() {
        lock (_activeTurnLifecycleSync) {
            return TryBeginTurnDispatchStartup(ref _isSending, ref _turnStartupInProgress);
        }
    }

    private void PromoteTurnDispatchStartupToSending() {
        lock (_activeTurnLifecycleSync) {
            PromoteTurnDispatchStartupToSending(ref _isSending, ref _turnStartupInProgress);
        }
    }

    private bool TryClearTurnDispatchStartup() {
        lock (_activeTurnLifecycleSync) {
            return TryClearTurnDispatchStartup(ref _turnStartupInProgress);
        }
    }

    private void CompleteActiveTurnDispatchSend() {
        lock (_activeTurnLifecycleSync) {
            _isSending = false;
        }
    }

    private bool TryConsumeQueuedPromptUsageLimitBypassAfterSwitchAccount() {
        if (!_queuedPromptUsageLimitBypassAfterSwitchAccount) {
            return false;
        }

        _queuedPromptUsageLimitBypassAfterSwitchAccount = false;
        return true;
    }

    private void ClearQueuedPromptUsageLimitBypassAfterSwitchAccount() {
        _queuedPromptUsageLimitBypassAfterSwitchAccount = false;
    }

    private async Task<bool> TryDispatchQueuedPromptAfterLoginAsync(bool honorAutoDispatch = true) {
        if (IsTurnDispatchInProgress() || !IsEffectivelyAuthenticatedForCurrentTransport() || _loginInProgress || (honorAutoDispatch && !_queueAutoDispatchEnabled)) {
            return false;
        }

        if (IsActiveUsageLimitDispatchBlocked(out var retryAfterMinutes)) {
            if (TryConsumeQueuedPromptUsageLimitBypassAfterSwitchAccount()) {
                ClearUsageLimitDispatchBlockForActiveAccount();
            } else {
                var activeUsageLabel = ResolveActiveUsageLabelForDisplay();
                await SetStatusAsync(BuildUsageLimitQueuedPromptStatus(retryAfterMinutes)).ConfigureAwait(false);
                await SetActivityAsync(BuildUsageLimitQueuedPromptActivity(activeUsageLabel, retryAfterMinutes)).ConfigureAwait(false);
                return false;
            }
        } else {
            ClearQueuedPromptUsageLimitBypassAfterSwitchAccount();
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
        if (IsTurnDispatchInProgress() || (honorAutoDispatch && !_queueAutoDispatchEnabled)) {
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
            ClearQueuedPromptUsageLimitBypassAfterSwitchAccount();
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
        if (IsTurnDispatchInProgress()) {
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
            return;
        }

        if (!IsEffectivelyAuthenticatedForCurrentTransport() && queuedSignIn > 0) {
            await SetStatusAsync($"Waiting for sign-in... ({queuedSignIn}/{MaxQueuedTurns} queued)").ConfigureAwait(false);
            return;
        }

        if (queuedSignIn > 0 && IsActiveUsageLimitDispatchBlocked(out var retryAfterMinutes)) {
            var activeUsageLabel = ResolveActiveUsageLabelForDisplay();
            await SetStatusAsync(BuildUsageLimitQueuedPromptStatus(retryAfterMinutes)).ConfigureAwait(false);
            await SetActivityAsync(BuildUsageLimitQueuedPromptActivity(activeUsageLabel, retryAfterMinutes)).ConfigureAwait(false);
            return;
        }

        await SetStatusAsync("Queued turns are waiting.").ConfigureAwait(false);
    }

    private async Task ClearQueuedTurnsAsync() {
        var clearedPending = ClearPendingTurns();
        var clearedSignIn = ClearQueuedPromptsAfterLogin();
        var clearedTotal = clearedPending + clearedSignIn;
        if (clearedTotal <= 0) {
            await SetStatusAsync("No queued turns to clear.").ConfigureAwait(false);
            return;
        }

        await SetStatusAsync($"Cleared queued turns ({clearedTotal} removed).").ConfigureAwait(false);
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

        text = RuntimeToolingSupportSnapshotBuilder.BuildClipboardText(
            text,
            BuildRuntimeToolingSupportSnapshot());

        var dp = new DataPackage();
        dp.SetText(text);
        Clipboard.SetContent(dp);
        Clipboard.Flush();
        AppendSystem("Startup log copied to clipboard.");
    }

}
