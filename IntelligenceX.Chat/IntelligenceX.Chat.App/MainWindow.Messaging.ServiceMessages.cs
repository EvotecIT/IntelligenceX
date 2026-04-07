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
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.App.Conversation;
using IntelligenceX.Chat.App.Rendering;
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

    private void OnServiceMessage(ChatServiceMessage msg) {
        _ = _dispatcher.TryEnqueue(() => {
            var requestConversation = ResolveRequestConversation();
            switch (msg) {
                case ChatDeltaMessage delta:
                    if (!ShouldProcessLiveRequestMessage(delta.RequestId)) {
                        break;
                    }
                    if (!IsActiveTurnRequest(delta.RequestId)) {
                        // Kickoff/background deltas must not overwrite an existing assistant bubble.
                        break;
                    }
                    if (ShouldUseProvisionalEventsForActiveTurn(requestConversation)) {
                        // Once at least one provisional fragment has been observed for this turn,
                        // prefer provisional events to avoid double-appending duplicate content.
                        break;
                    }

                    MarkTurnDeltaStage(delta);
                    ApplyAssistantStreamingFragment(
                        requestConversation,
                        delta.Text,
                        preferProvisionalEvents: false,
                        renderReason: "chat_delta");
                    break;
                case ChatAssistantProvisionalMessage provisional:
                    if (!ShouldProcessLiveRequestMessage(provisional.RequestId)) {
                        break;
                    }
                    if (!IsActiveTurnRequest(provisional.RequestId)) {
                        break;
                    }

                    MarkTurnDeltaStage(new ChatDeltaMessage {
                        Kind = provisional.Kind,
                        RequestId = provisional.RequestId,
                        ThreadId = provisional.ThreadId,
                        Text = provisional.Text
                    });
                    ApplyAssistantStreamingFragment(
                        requestConversation,
                        provisional.Text,
                        preferProvisionalEvents: true,
                        renderReason: "assistant_provisional");
                    break;
                case ChatInterimResultMessage interim:
                    if (!ShouldProcessLiveRequestMessage(interim.RequestId)) {
                        break;
                    }
                    if (!IsActiveTurnRequest(interim.RequestId)) {
                        break;
                    }

                    ApplyInterimAssistantResult(requestConversation, interim.Text);
                    break;
                case ChatStatusMessage status:
                    if (!ShouldProcessLiveRequestMessage(status.RequestId)) {
                        break;
                    }

                    MarkTurnStatusStage(status);
                    var assistantStatusSignalChanged = ApplyActiveTurnStatusSignal(requestConversation, status.Status);
                    var routingInsightUpdated = ApplyToolRoutingInsight(status);
                    var routingPromptExposureUpdated = ApplyRoutingMetaPromptExposure(status);
                    var activityText = IsTerminalChatStatus(status.Status) ? null : FormatActivityText(status);
                    var timelineChanged = AppendActivityTimeline(status, activityText ?? string.Empty);
                    var normalizedActivityText = activityText ?? string.Empty;
                    var activityChanged = !string.Equals(_latestServiceActivityText, normalizedActivityText, StringComparison.Ordinal);
                    _latestServiceActivityText = normalizedActivityText;
                    var timelineLabelSource = activityText ?? FormatActivityText(status);
                    var assistantTimelineChanged = AppendActiveTurnAssistantTimelineLabel(
                        requestConversation,
                        BuildActivityTimelineLabel(status, timelineLabelSource));
                    if (activityChanged || timelineChanged) {
                        _ = SetActivityAsync(activityText, SnapshotActivityTimeline());
                    }
                    if (timelineChanged) {
                        RequestServiceDrivenSessionPublish();
                    }
                    if (routingPromptExposureUpdated) {
                        RequestServiceDrivenSessionPublish();
                    }
                    if ((assistantTimelineChanged || assistantStatusSignalChanged)
                        && string.Equals(requestConversation.Id, _activeConversationId, StringComparison.OrdinalIgnoreCase)) {
                        QueueTranscriptRender("chat_status_timeline");
                    }
                    if (routingInsightUpdated || routingPromptExposureUpdated) {
                        _ = PublishOptionsStateSafeAsync();
                    }
                    if (VerboseServiceLogs || _debugMode) {
                        AppendSystem(FormatStatusTrace(status));
                    }
                    break;
                case ChatMetricsMessage metrics:
                    if (!ShouldProcessLiveRequestMessage(metrics.RequestId) && !IsLatestTurnRequest(metrics.RequestId)) {
                        break;
                    }

                    ApplyTurnMetrics(metrics);
                    StartupLog.Write(
                        "TurnMetrics request_id="
                        + (metrics.RequestId ?? string.Empty).Trim()
                        + " duration_ms="
                        + metrics.DurationMs.ToString(CultureInfo.InvariantCulture)
                        + " ttft_ms="
                        + (metrics.TtftMs?.ToString(CultureInfo.InvariantCulture) ?? "null")
                        + " outcome="
                        + ((metrics.Outcome ?? string.Empty).Trim().Length == 0 ? "unknown" : metrics.Outcome!.Trim())
                        + " tool_calls="
                        + metrics.ToolCallsCount.ToString(CultureInfo.InvariantCulture)
                        + " tool_rounds="
                        + metrics.ToolRounds.ToString(CultureInfo.InvariantCulture));
                    RequestServiceDrivenSessionPublish();
                    if (VerboseServiceLogs || _debugMode) {
                        AppendSystem(FormatMetricsTrace(metrics));
                    }
                    break;
                case ChatGptLoginUrlMessage url:
                    _loginInProgress = true;
                    Interlocked.Exchange(ref _startupLoginSuccessMetadataSyncQueued, 0);
                    _ = SetStatusAsync(SessionStatus.CompleteSignInInBrowser());
                    _ = Windows.System.Launcher.LaunchUriAsync(new Uri(url.Url));
                    break;
                case ChatGptLoginPromptMessage prompt:
                    _ = ShowLoginPromptAsync(prompt);
                    break;
                case ChatGptLoginCompletedMessage done:
                    _loginInProgress = false;
                    _autoSignInAttempted = true;
                    SetInteractiveAuthenticationKnown(isAuthenticated: done.Ok);
                    if (ShouldResetEnsureLoginProbeCacheForAuthContextChange(
                            requiresInteractiveSignIn: RequiresInteractiveSignInForCurrentTransport(),
                            loginCompletedSuccessfully: done.Ok,
                            transportChanged: false,
                            runtimeExited: false)) {
                        ResetEnsureLoginProbeCache();
                    }
                    if (!done.Ok) {
                        _authenticatedAccountId = null;
                        Interlocked.Exchange(ref _startupLoginSuccessMetadataSyncQueued, 0);
                        ClearQueuedPromptUsageLimitBypassAfterSwitchAccount();
                    }
                    _isConnected = _client is not null;
                    if (!done.Ok && !string.IsNullOrWhiteSpace(done.Error)) {
                        AppendSystem(SystemNotice.LoginFailed(done.Error));
                    }
                    if (done.Ok) {
                        var shouldQueueMetadataSync = ShouldQueueDeferredStartupMetadataSyncAfterAuthenticationReady(
                            isConnected: _isConnected,
                            requiresInteractiveSignIn: RequiresInteractiveSignInForCurrentTransport(),
                            isAuthenticated: IsEffectivelyAuthenticatedForCurrentTransport(),
                            loginInProgress: _loginInProgress,
                            hasSessionPolicy: _sessionPolicy is not null);
                        if (shouldQueueMetadataSync
                            && ShouldQueueDeferredStartupMetadataSyncAfterLoginSuccess(
                                shouldWaitForAuthenticationBeforeDeferredStartupMetadataSync: false,
                                loginSuccessMetadataSyncAlreadyQueued: Volatile.Read(ref _startupLoginSuccessMetadataSyncQueued) != 0)
                            && Interlocked.CompareExchange(ref _startupLoginSuccessMetadataSyncQueued, 1, 0) == 0) {
                            QueueDeferredStartupConnectMetadataSync(requestRerunIfBusy: true);
                        }
                        QueuePostLoginCompletion();
                    }
                    _ = SetStatusAsync(done.Ok ? SessionStatus.Connected() : SessionStatus.SignInFailed());
                    break;
                case ErrorMessage err:
                    if (string.Equals(err.Code, "not_authenticated", StringComparison.OrdinalIgnoreCase)) {
                        SetInteractiveAuthenticationKnown(isAuthenticated: false);
                        Interlocked.Exchange(ref _startupLoginSuccessMetadataSyncQueued, 0);
                        _ = SetStatusAsync(SessionStatus.SignInRequired());
                    } else if (!string.IsNullOrWhiteSpace(err.RequestId)) {
                        var requestId = err.RequestId.Trim();
                        var isRelevantRequestError = ShouldProcessLiveRequestMessage(requestId) || IsLatestTurnRequest(requestId);
                        if (!isRelevantRequestError) {
                            break;
                        }

                        _ = SetStatusAsync(
                            "Last turn failed: " + SummarizeErrorForStatus(err.Error),
                            SessionStatusTone.Warn);
                        AppendSystem(SystemNotice.ServiceError(err.Error, err.Code));
                    } else {
                        _ = SetStatusAsync("Service warning. See System log for details.", SessionStatusTone.Warn);
                        AppendSystem(SystemNotice.ServiceError(err.Error, err.Code));
                    }
                    break;
            }
        });
    }

    private void ApplyAssistantStreamingFragment(ConversationRuntime conversation, string? fragment, bool preferProvisionalEvents, string renderReason) {
        var delta = fragment ?? string.Empty;
        if (delta.Length == 0) {
            return;
        }
        if (!TryAcceptActiveTurnDraftMutation(conversation)) {
            return;
        }

        var normalizedPreview = _assistantStreamingState.AppendDeltaAndNormalizePreview(
            delta,
            fromProvisionalEvent: preferProvisionalEvents);
        ReplaceLastAssistantText(
            conversation,
            normalizedPreview);
        conversation.UpdatedUtc = DateTime.UtcNow;
        BindActiveTurnAssistantMessage(conversation);
        SetActiveTurnAssistantChannel(conversation, AssistantBubbleChannelKind.DraftThinking);
        SetActiveTurnAssistantProvisional(conversation, provisional: true, preferProvisionalEvents);
        if (string.Equals(conversation.Id, _activeConversationId, StringComparison.OrdinalIgnoreCase)) {
            QueueTranscriptRender(renderReason);
        }
    }

    private void ApplyInterimAssistantResult(ConversationRuntime conversation, string? text) {
        var interimText = (text ?? string.Empty).Trim();
        if (interimText.Length == 0 || !TryAcceptActiveTurnDraftMutation(conversation) || !TryMarkActiveTurnInterimResult(interimText)) {
            return;
        }

        _ = TryGetLastAssistantText(conversation, out var latestAssistantText);
        var appendInterimBubble = ShouldAppendInterimAssistantResult(
            activeTurnReceivedDelta: _assistantStreamingState.HasReceivedDelta(),
            activeTurnBoundToConversation: IsActiveTurnBoundToConversation(conversation),
            interimAssistantText: interimText,
            latestAssistantText: latestAssistantText);
        if (appendInterimBubble) {
            AppendAssistantText(conversation, interimText);
        } else {
            ReplaceLastAssistantText(conversation, interimText);
        }
        conversation.UpdatedUtc = DateTime.UtcNow;
        BindActiveTurnAssistantMessage(conversation);
        SetActiveTurnAssistantChannel(conversation, AssistantBubbleChannelKind.DraftThinking);
        // Interim snapshots are draft-by-definition and should stay visually distinct
        // from the finalized assistant response.
        SetActiveTurnAssistantProvisional(conversation, provisional: true, preferProvisionalEvents: false);
        if (string.Equals(conversation.Id, _activeConversationId, StringComparison.OrdinalIgnoreCase)) {
            QueueTranscriptRender("assistant_interim_result");
        }
    }

    internal static bool ShouldAppendInterimAssistantResult(bool activeTurnReceivedDelta, bool activeTurnBoundToConversation) {
        // Interim snapshots should replace the active provisional draft when this turn has already streamed
        // assistant content; appending in that case creates duplicate assistant bubbles.
        if (activeTurnReceivedDelta && activeTurnBoundToConversation) {
            return false;
        }

        return true;
    }

    internal static bool ShouldAppendInterimAssistantResult(
        bool activeTurnReceivedDelta,
        bool activeTurnBoundToConversation,
        string? interimAssistantText,
        string? latestAssistantText) {
        if (!ShouldAppendInterimAssistantResult(activeTurnReceivedDelta, activeTurnBoundToConversation)) {
            return false;
        }

        var interimText = NormalizeAssistantSnapshotForAppendDecision(interimAssistantText);
        if (interimText.Length == 0) {
            return false;
        }

        var latestText = NormalizeAssistantSnapshotForAppendDecision(latestAssistantText);
        if (latestText.Length == 0) {
            return true;
        }

        if (string.Equals(interimText, latestText, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        if (AreNearDuplicateAssistantSnapshots(interimText, latestText)) {
            return false;
        }

        return true;
    }

    private void QueuePostLoginCompletion() {
        Task completionTask;
        lock (_postLoginCompletionSync) {
            if (_postLoginCompletionInFlightTask is { IsCompleted: false }) {
                return;
            }

            completionTask = RunPostLoginCompletionAsync();
            _postLoginCompletionInFlightTask = completionTask;
        }

        _ = completionTask.ContinueWith(
            completedTask => {
                lock (_postLoginCompletionSync) {
                    if (ReferenceEquals(_postLoginCompletionInFlightTask, completedTask)) {
                        _postLoginCompletionInFlightTask = null;
                    }
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private async Task RunPostLoginCompletionAsync() {
        try {
            await CompleteLoginAndDispatchQueuedTurnAsync().ConfigureAwait(false);
        } catch (OperationCanceledException) {
            // Ignore cancellation while app/session is transitioning.
        } catch (Exception ex) {
            if (VerboseServiceLogs || _debugMode) {
                await AppendSystemBestEffortAsync("Post-login completion failed: " + ex.Message).ConfigureAwait(false);
            }
        }
    }

    private async Task<bool> VerifyPostLoginAuthenticationAsync(bool prioritizeDispatchLatency) {
        var maxProbeAttempts = prioritizeDispatchLatency ? 2 : 3;
        var probeTimeout = prioritizeDispatchLatency ? EnsureLoginFastPathProbeTimeout : EnsureLoginPostLoginProbeTimeout;
        var verificationBudget = prioritizeDispatchLatency ? TimeSpan.FromSeconds(3) : TimeSpan.FromSeconds(5);
        var verificationStopwatch = Stopwatch.StartNew();
        var minimumRemainingBudget = TimeSpan.FromMilliseconds(100);
        var runtimePinCleared = false;
        var requireFreshProbe = true;
        for (var attempt = 0; attempt < maxProbeAttempts; attempt++) {
            var remainingBudget = verificationBudget - verificationStopwatch.Elapsed;
            if (remainingBudget <= minimumRemainingBudget) {
                break;
            }

            var effectiveProbeTimeout = probeTimeout < remainingBudget ? probeTimeout : remainingBudget;
            if (effectiveProbeTimeout <= TimeSpan.Zero) {
                break;
            }

            var allowCachedFallback = prioritizeDispatchLatency || attempt >= 1;
            if (await RefreshAuthenticationStateAsync(
                    updateStatus: true,
                    requireFreshProbe: requireFreshProbe,
                    allowCachedAuthenticatedFallback: allowCachedFallback,
                    probeTimeout: effectiveProbeTimeout)
                .ConfigureAwait(false)) {
                return true;
            }

            var hasExplicitUnauthenticatedProbe = HasExplicitUnauthenticatedEnsureLoginProbeSnapshot();
            requireFreshProbe = false;

            var hasAnotherProbeAttempt = attempt + 1 < maxProbeAttempts;
            if (!runtimePinCleared
                && RequiresInteractiveSignInForCurrentTransport()
                && hasAnotherProbeAttempt
                && hasExplicitUnauthenticatedProbe
                && (attempt >= 1 || prioritizeDispatchLatency)) {
                // After OAuth callback succeeds, runtime state may still carry a stale account pin.
                // Clear it once and continue probing before declaring sign-in failure.
                var pinResetTimeout = prioritizeDispatchLatency
                    ? RuntimeAccountPinResetFastTimeout
                    : RuntimeAccountPinResetRecoveryTimeout;
                _ = await TryClearNativeRuntimeAccountPinAsync(pinResetTimeout).ConfigureAwait(false);
                runtimePinCleared = true;
                requireFreshProbe = true;
            } else if (runtimePinCleared && hasExplicitUnauthenticatedProbe) {
                // A definitive unauthenticated probe even after runtime pin clear is unlikely to recover
                // within this verification loop; avoid burning the remaining budget on duplicate probes.
                break;
            }

            if (hasAnotherProbeAttempt) {
                var delayMs = prioritizeDispatchLatency
                    ? Math.Min(180, 60 * (attempt + 1))
                    : Math.Min(500, 120 * (attempt + 1));
                var plannedDelay = TimeSpan.FromMilliseconds(delayMs);
                remainingBudget = verificationBudget - verificationStopwatch.Elapsed;
                if (remainingBudget <= TimeSpan.Zero) {
                    break;
                }
                if (plannedDelay > remainingBudget) {
                    plannedDelay = remainingBudget;
                }
                if (plannedDelay > TimeSpan.Zero) {
                    await Task.Delay(plannedDelay).ConfigureAwait(false);
                }
            }
        }

        if (VerboseServiceLogs || _debugMode) {
            await AppendSystemBestEffortAsync(
                    "Post-login verification did not confirm an authenticated account after "
                    + maxProbeAttempts
                    + " probes.")
                .ConfigureAwait(false);
        }

        return false;
    }

    private async Task CompleteLoginAndDispatchQueuedTurnAsync() {
        var queuedPromptCount = GetQueuedPromptAfterLoginCount();
        var prioritizeDispatchLatency = queuedPromptCount > 0;
        var refreshSucceeded = false;
        try {
            refreshSucceeded = await VerifyPostLoginAuthenticationAsync(prioritizeDispatchLatency).ConfigureAwait(false);
        } catch (OperationCanceledException) {
            return;
        } catch (Exception ex) {
            if (VerboseServiceLogs || _debugMode) {
                try {
                    await RunOnUiThreadAsync(() => {
                        AppendSystem("Post-login verification failed: " + ex.Message);
                        return Task.CompletedTask;
                    }).ConfigureAwait(false);
                } catch {
                    // Best-effort diagnostic only.
                }
            }
        }

        if (RequiresInteractiveSignInForCurrentTransport() && !refreshSucceeded) {
            if (ShouldAttemptQueuedPromptDispatchAfterVerificationFailure(
                    requiresInteractiveSignIn: true,
                    queuedPromptCount: queuedPromptCount,
                    loginInProgress: _loginInProgress)) {
                SetInteractiveAuthenticationKnown(isAuthenticated: true);
                await SetStatusAsync(SessionStatus.ForConnectedAuth(isAuthenticated: true)).ConfigureAwait(false);
                await SetActivityAsync("Sign-in callback succeeded. Retrying queued prompt while account verification catches up.").ConfigureAwait(false);
                await HandlePostLoginCompletionAsync().ConfigureAwait(false);
                return;
            }

            await SetStatusAsync(SessionStatus.SignInRequired()).ConfigureAwait(false);
            await SetActivityAsync("Post-login verification did not confirm account state. Use Sign In or Switch Account.").ConfigureAwait(false);
            return;
        }

        if (queuedPromptCount == 0) {
            ClearQueuedPromptUsageLimitBypassAfterSwitchAccount();
        }

        await HandlePostLoginCompletionAsync().ConfigureAwait(false);
    }

    internal static bool ShouldAttemptQueuedPromptDispatchAfterVerificationFailure(
        bool requiresInteractiveSignIn,
        int queuedPromptCount,
        bool loginInProgress) {
        return requiresInteractiveSignIn
               && queuedPromptCount > 0
               && !loginInProgress;
    }

    private static string SummarizeErrorForStatus(string? message) {
        var normalized = (message ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return "Unknown error.";
        }

        // Keep top status compact and single-line while preserving the key failure reason.
        normalized = normalized.Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);
        while (normalized.Contains("  ", StringComparison.Ordinal)) {
            normalized = normalized.Replace("  ", " ", StringComparison.Ordinal);
        }

        const int maxLen = 140;
        if (normalized.Length <= maxLen) {
            return normalized;
        }

        return normalized[..maxLen].TrimEnd() + "...";
    }

    private async Task HandlePostLoginCompletionAsync() {
        var dispatched = await DispatchNextQueuedTurnAsync(honorAutoDispatch: true).ConfigureAwait(false);
        if (dispatched) {
            return;
        }

        var queuedTotal = GetQueuedTurnCount() + GetQueuedPromptAfterLoginCount();
        if (queuedTotal > 0 && !_queueAutoDispatchEnabled) {
            await SetStatusAsync($"Queued turns paused ({queuedTotal} waiting).").ConfigureAwait(false);
            return;
        }

        await MaybeStartModelKickoffAsync().ConfigureAwait(false);
    }

    private void ApplyTurnMetrics(ChatMetricsMessage metrics) {
        var normalizedRequestId = NormalizeRequestId(metrics.RequestId);
        var completedUtc = metrics.CompletedAtUtc.Kind == DateTimeKind.Utc
            ? metrics.CompletedAtUtc
            : DateTime.SpecifyKind(metrics.CompletedAtUtc, DateTimeKind.Utc);
        var startedUtc = metrics.StartedAtUtc.Kind == DateTimeKind.Utc
            ? metrics.StartedAtUtc
            : DateTime.SpecifyKind(metrics.StartedAtUtc, DateTimeKind.Utc);
        var outcome = (metrics.Outcome ?? string.Empty).Trim();
        if (outcome.Length == 0) {
            outcome = "unknown";
        }
        var normalizedDurationMs = Math.Max(0L, metrics.DurationMs);
        var completion = CompleteTurnLatencyTracking(
            normalizedRequestId,
            completedUtc,
            explicitDurationMs: normalizedDurationMs);
        if (completion is not null && string.Equals(outcome, "ok", StringComparison.OrdinalIgnoreCase)) {
            RegisterTurnSuccessReliability(completion);
        }

        lock (_turnDiagnosticsSync) {
            var prior = _lastTurnMetrics;
            var sameRequestAsPrior = prior is not null
                                     && string.Equals(prior.RequestId, normalizedRequestId, StringComparison.OrdinalIgnoreCase);
            var queueWaitMs = completion?.QueueWaitMs
                              ?? (sameRequestAsPrior ? prior!.QueueWaitMs : _activeTurnQueueWaitMs);
            var authProbeMs = completion?.AuthProbeMs
                              ?? (sameRequestAsPrior ? prior!.AuthProbeMs : null);
            var connectMs = completion?.ConnectMs
                            ?? (sameRequestAsPrior ? prior!.ConnectMs : null);
            var ensureThreadMs = metrics.EnsureThreadMs
                                 ?? (sameRequestAsPrior ? prior!.EnsureThreadMs : null);
            var weightedSubsetSelectionMs = metrics.WeightedSubsetSelectionMs
                                            ?? (sameRequestAsPrior ? prior!.WeightedSubsetSelectionMs : null);
            var resolveModelMs = metrics.ResolveModelMs
                                 ?? (sameRequestAsPrior ? prior!.ResolveModelMs : null);
            var dispatchToFirstStatusMs = completion?.DispatchToFirstStatusMs
                                         ?? (sameRequestAsPrior ? prior!.DispatchToFirstStatusMs : null);
            var dispatchToModelSelectedMs = completion?.DispatchToModelSelectedMs
                                            ?? (sameRequestAsPrior ? prior!.DispatchToModelSelectedMs : null);
            var dispatchToFirstToolRunningMs = completion?.DispatchToFirstToolRunningMs
                                              ?? (sameRequestAsPrior ? prior!.DispatchToFirstToolRunningMs : null);
            var dispatchToFirstDeltaMs = completion?.DispatchToFirstDeltaMs
                                        ?? (sameRequestAsPrior ? prior!.DispatchToFirstDeltaMs : null);
            var dispatchToLastDeltaMs = completion?.DispatchToLastDeltaMs
                                       ?? (sameRequestAsPrior ? prior!.DispatchToLastDeltaMs : null);
            var streamDurationMs = completion?.StreamDurationMs
                                   ?? (sameRequestAsPrior ? prior!.StreamDurationMs : null);
            if (!dispatchToFirstDeltaMs.HasValue && metrics.FirstDeltaAtUtc.HasValue) {
                var firstDeltaAtUtc = metrics.FirstDeltaAtUtc.Value.Kind == DateTimeKind.Utc
                    ? metrics.FirstDeltaAtUtc.Value
                    : DateTime.SpecifyKind(metrics.FirstDeltaAtUtc.Value, DateTimeKind.Utc);
                dispatchToFirstDeltaMs = TryComputeElapsedMs(startedUtc, firstDeltaAtUtc);
            }

            _lastTurnMetrics = new TurnMetricsSnapshot(
                RequestId: normalizedRequestId,
                CompletedUtc: completedUtc,
                DurationMs: normalizedDurationMs,
                TtftMs: metrics.TtftMs,
                QueueWaitMs: queueWaitMs,
                AuthProbeMs: authProbeMs,
                ConnectMs: connectMs,
                EnsureThreadMs: ensureThreadMs,
                WeightedSubsetSelectionMs: weightedSubsetSelectionMs,
                ResolveModelMs: resolveModelMs,
                DispatchToFirstStatusMs: dispatchToFirstStatusMs,
                DispatchToModelSelectedMs: dispatchToModelSelectedMs,
                DispatchToFirstToolRunningMs: dispatchToFirstToolRunningMs,
                DispatchToFirstDeltaMs: dispatchToFirstDeltaMs,
                DispatchToLastDeltaMs: dispatchToLastDeltaMs,
                StreamDurationMs: streamDurationMs,
                ToolCallsCount: Math.Max(0, metrics.ToolCallsCount),
                ToolRounds: Math.Max(0, metrics.ToolRounds),
                ProjectionFallbackCount: Math.Max(0, metrics.ProjectionFallbackCount),
                Outcome: outcome,
                ErrorCode: string.IsNullOrWhiteSpace(metrics.ErrorCode) ? null : metrics.ErrorCode.Trim(),
                PromptTokens: metrics.Usage?.PromptTokens,
                CompletionTokens: metrics.Usage?.CompletionTokens,
                TotalTokens: metrics.Usage?.TotalTokens,
                CachedPromptTokens: metrics.Usage?.CachedPromptTokens,
                ReasoningTokens: metrics.Usage?.ReasoningTokens,
                AutonomyCounters: NormalizeTurnCounterMetrics(metrics.AutonomyCounters),
                Model: string.IsNullOrWhiteSpace(metrics.Model) ? null : metrics.Model.Trim(),
                RequestedModel: string.IsNullOrWhiteSpace(metrics.RequestedModel) ? null : metrics.RequestedModel.Trim(),
                Transport: string.IsNullOrWhiteSpace(metrics.Transport) ? null : metrics.Transport.Trim(),
                EndpointHost: string.IsNullOrWhiteSpace(metrics.EndpointHost) ? null : metrics.EndpointHost.Trim());
        }

        UpdateAccountUsageFromMetrics(metrics.Usage);
    }

    private async Task PublishOptionsStateSafeAsync() {
        try {
            if (_dispatcher.HasThreadAccess) {
                await PublishOptionsStateAsync().ConfigureAwait(false);
            } else {
                var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
                if (!_dispatcher.TryEnqueue(() => {
                    try {
                        var publishTask = PublishOptionsStateAsync();
                        if (publishTask.IsCompletedSuccessfully) {
                            tcs.TrySetResult(null);
                            return;
                        }

                        _ = publishTask.ContinueWith(task => {
                            if (task.IsCanceled) {
                                tcs.TrySetCanceled();
                                return;
                            }

                            if (task.IsFaulted) {
                                tcs.TrySetException(task.Exception?.InnerException ?? task.Exception!);
                                return;
                            }

                            tcs.TrySetResult(null);
                        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                    } catch (Exception ex) {
                        tcs.TrySetException(ex);
                    }
                })) {
                    tcs.TrySetException(new InvalidOperationException("Failed to dispatch options refresh to UI thread."));
                }

                await tcs.Task.ConfigureAwait(false);
            }
        } catch (Exception ex) {
            if (VerboseServiceLogs || _debugMode) {
                try {
                    await RunOnUiThreadAsync(() => {
                        AppendSystem("Options refresh failed: " + ex.Message);
                        return Task.CompletedTask;
                    }).ConfigureAwait(false);
                } catch {
                    // best-effort logging only
                }
            }
        }
    }

    private void OnClientDisconnected(ChatServiceClient client) {
        _ = _dispatcher.TryEnqueue(async () => {
            if (!ReferenceEquals(_client, client)) {
                return;
            }

            await DisposeClientAsync().ConfigureAwait(false);
            var requiresInteractiveSignIn = RequiresInteractiveSignInForCurrentTransport();
            var preserveInteractiveAuthState = ShouldPreserveInteractiveAuthStateOnReconnect(
                requiresInteractiveSignIn: requiresInteractiveSignIn,
                isAuthenticated: _isAuthenticated,
                hasExplicitUnauthenticatedProbeSnapshot: HasExplicitUnauthenticatedEnsureLoginProbeSnapshot(),
                loginInProgress: _loginInProgress);
            var resetEnsureLoginProbeCache = ShouldResetEnsureLoginProbeCacheOnReconnectAuthReset(
                requiresInteractiveSignIn: requiresInteractiveSignIn,
                preserveInteractiveAuthState: preserveInteractiveAuthState);
            if (!preserveInteractiveAuthState) {
                SetInteractiveAuthenticationUnknown();
                _loginInProgress = false;
                Interlocked.Exchange(ref _startupLoginSuccessMetadataSyncQueued, 0);
            } else if (!_isAuthenticated) {
                _authenticatedAccountId = null;
            }
            ResetStartupMetadataFailureRecoveryDiagnostics();
            if (resetEnsureLoginProbeCache) {
                ResetEnsureLoginProbeCache();
            }
            ClearBackgroundSchedulerSnapshots();
            _isConnected = false;
            EndStartupMetadataSyncTracking();
            _autoSignInAttempted = _appState.OnboardingCompleted || AnyConversationHasMessages();
            // Keep the sidecar process alive on transient pipe disconnects.
            // This avoids unnecessary process churn while the client auto-reconnect loop runs.
            if (preserveInteractiveAuthState && _loginInProgress) {
                await SetStatusAsync(
                        AppendStartupStatusContext(
                            "Runtime connection dropped during sign-in. Reconnecting...",
                            StartupStatusPhaseStartupAuthWait,
                            StartupStatusCauseRuntimeDisconnect),
                        SessionStatusTone.Warn)
                    .ConfigureAwait(false);
            } else if (Volatile.Read(ref _startupFlowState) == StartupFlowStateRunning
                       || Volatile.Read(ref _startupConnectMetadataDeferredQueued) != 0
                       || Volatile.Read(ref _startupMetadataSyncInProgress) != 0) {
                await SetStatusAsync(
                        AppendStartupStatusContext(
                            "Runtime connection dropped. Reconnecting startup sync...",
                            StartupStatusPhaseStartupMetadataSync,
                            StartupStatusCauseRuntimeDisconnect),
                        SessionStatusTone.Warn)
                    .ConfigureAwait(false);
            } else {
                await SetStatusAsync(SessionStatus.Disconnected()).ConfigureAwait(false);
            }
            EnsureAutoReconnectLoop();
        });
    }

    private async Task<bool> EnsureConnectedAsync(
        TimeSpan? connectBudgetOverride = null,
        bool deferPostConnectMetadataSync = false) {
        if (_client is not null
            && await IsClientAliveAsync(
                    _client,
                    probeTimeout: AliveProbeFastTimeout,
                    cacheTtl: AliveProbeCacheTtl)
                .ConfigureAwait(false)) {
            _isConnected = true;
            return true;
        }

        var connectBudget = connectBudgetOverride.GetValueOrDefault(DispatchConnectBudget);
        if (connectBudget <= TimeSpan.Zero) {
            connectBudget = DispatchConnectBudget;
        }

        Task<bool> connectAttemptTask;
        var joinedExistingInFlight = false;
        var startedNewInFlightTask = false;
        lock (_ensureConnectedSync) {
            if (_ensureConnectedInFlightTask is { IsCompleted: false } inFlightTask) {
                connectAttemptTask = inFlightTask;
                joinedExistingInFlight = true;
            } else {
                connectAttemptTask = EnsureConnectedCoreAsync(
                    connectBudget,
                    deferPostConnectMetadataSync: deferPostConnectMetadataSync);
                _ensureConnectedInFlightTask = connectAttemptTask;
                startedNewInFlightTask = true;
            }
        }

        try {
            if (joinedExistingInFlight && connectBudget > TimeSpan.Zero) {
                try {
                    return await connectAttemptTask.WaitAsync(connectBudget).ConfigureAwait(false);
                } catch (TimeoutException) {
                    if (ShouldProbeExistingClientAfterJoinedConnectTimeout(joinedExistingInFlight, connectBudget)) {
                        var timedOutClient = _client;
                        if (timedOutClient is not null
                            && await IsClientAliveAsync(
                                    timedOutClient,
                                    probeTimeout: AliveProbeFastTimeout,
                                    cacheTtl: AliveProbeCacheTtl)
                                .ConfigureAwait(false)) {
                            _isConnected = true;
                            return true;
                        }
                    }

                    _isConnected = false;
                    if (VerboseServiceLogs || _debugMode) {
                        await AppendSystemBestEffortAsync(
                                "Connect probe timed out while waiting for an in-flight reconnect. Prompt will retry.")
                            .ConfigureAwait(false);
                    }
                    return false;
                }
            }

            return await connectAttemptTask.ConfigureAwait(false);
        } finally {
            lock (_ensureConnectedSync) {
                if (ReferenceEquals(_ensureConnectedInFlightTask, connectAttemptTask)
                    && ShouldResetEnsureConnectedInFlightTask(
                        startedNewInFlightTask: startedNewInFlightTask,
                        connectAttemptTaskCompleted: connectAttemptTask.IsCompleted)) {
                    _ensureConnectedInFlightTask = null;
                }
            }
        }
    }

    internal static bool ShouldResetEnsureConnectedInFlightTask(
        bool startedNewInFlightTask,
        bool connectAttemptTaskCompleted) {
        if (startedNewInFlightTask) {
            // Owner call must always clear in finally so a faulted/canceled attempt
            // never pins stale in-flight state for subsequent reconnects.
            return true;
        }

        // Joiners clear only after observing task completion; do not clear an active
        // in-flight connect attempt while it is still running.
        return connectAttemptTaskCompleted;
    }

    private async Task<bool> EnsureConnectedCoreAsync(TimeSpan connectBudget, bool deferPostConnectMetadataSync) {
        if (_client is not null
            && await IsClientAliveAsync(
                    _client,
                    probeTimeout: AliveProbeFastTimeout,
                    cacheTtl: AliveProbeCacheTtl)
                .ConfigureAwait(false)) {
            _isConnected = true;
            return true;
        }

        var hasTrackedRunningServiceProcess = _serviceProcess is not null && !_serviceProcess.HasExited;
        var prioritizeLatency = ShouldPrioritizeAutoReconnectLatency();
        if (ShouldApplyDispatchConnectFailureCooldown(hasTrackedRunningServiceProcess, prioritizeLatency)
            && TryGetDispatchConnectFailureCooldownRemaining(out _)) {
            _isConnected = false;
            return false;
        }

        await ConnectAsync(
                fromUserAction: false,
                connectBudgetOverride: connectBudget,
                deferPostConnectMetadataSync: deferPostConnectMetadataSync)
            .ConfigureAwait(false);
        var connected = _client is not null;
        _isConnected = connected;
        if (!connected) {
            await PublishSessionStateAsync().ConfigureAwait(false);
        }
        return connected;
    }

}
