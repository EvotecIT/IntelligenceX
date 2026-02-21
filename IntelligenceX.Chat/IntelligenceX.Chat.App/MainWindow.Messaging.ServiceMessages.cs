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

                    MarkTurnDeltaStage(delta);
                    _assistantStreaming.Append(delta.Text);
                    _activeTurnReceivedDelta = true;
                    ReplaceLastAssistantText(
                        requestConversation,
                        TranscriptMarkdownNormalizer.NormalizeForStreamingPreview(_assistantStreaming.ToString()));
                    requestConversation.UpdatedUtc = DateTime.UtcNow;
                    if (string.Equals(requestConversation.Id, _activeConversationId, StringComparison.OrdinalIgnoreCase)) {
                        _ = RenderTranscriptAsync();
                    }
                    break;
                case ChatStatusMessage status:
                    if (!ShouldProcessLiveRequestMessage(status.RequestId)) {
                        break;
                    }

                    MarkTurnStatusStage(status);
                    var routingInsightUpdated = ApplyToolRoutingInsight(status);
                    var activityText = IsTerminalChatStatus(status.Status) ? null : FormatActivityText(status);
                    AppendActivityTimeline(status, activityText ?? string.Empty);
                    _latestServiceActivityText = activityText ?? string.Empty;
                    _ = SetActivityAsync(activityText, SnapshotActivityTimeline());
                    _ = PublishSessionStateAsync();
                    if (routingInsightUpdated) {
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
                    _ = PublishSessionStateAsync();
                    if (VerboseServiceLogs || _debugMode) {
                        AppendSystem(FormatMetricsTrace(metrics));
                    }
                    break;
                case ChatGptLoginUrlMessage url:
                    _loginInProgress = true;
                    _ = SetStatusAsync(SessionStatus.CompleteSignInInBrowser());
                    _ = Windows.System.Launcher.LaunchUriAsync(new Uri(url.Url));
                    break;
                case ChatGptLoginPromptMessage prompt:
                    _ = ShowLoginPromptAsync(prompt);
                    break;
                case ChatGptLoginCompletedMessage done:
                    _loginInProgress = false;
                    _autoSignInAttempted = true;
                    _isAuthenticated = done.Ok;
                    if (!done.Ok) {
                        _authenticatedAccountId = null;
                    }
                    _isConnected = _client is not null;
                    _ = SetStatusAsync(done.Ok ? SessionStatus.Connected() : SessionStatus.SignInFailed());
                    if (!done.Ok && !string.IsNullOrWhiteSpace(done.Error)) {
                        AppendSystem(SystemNotice.LoginFailed(done.Error));
                    }
                    if (done.Ok) {
                        _ = CompleteLoginAndDispatchQueuedTurnAsync();
                    }
                    break;
                case ErrorMessage err:
                    if (string.Equals(err.Code, "not_authenticated", StringComparison.OrdinalIgnoreCase)) {
                        _isAuthenticated = false;
                        _authenticatedAccountId = null;
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

    private async Task<bool> VerifyPostLoginAuthenticationAsync(bool prioritizeDispatchLatency) {
        var maxProbeAttempts = prioritizeDispatchLatency ? 2 : 5;
        var probeTimeout = prioritizeDispatchLatency ? EnsureLoginFastPathProbeTimeout : EnsureLoginFreshProbeTimeout;
        var runtimePinCleared = false;
        for (var attempt = 0; attempt < maxProbeAttempts; attempt++) {
            var allowCachedFallback = prioritizeDispatchLatency || attempt >= 2;
            if (await RefreshAuthenticationStateAsync(
                    updateStatus: true,
                    requireFreshProbe: true,
                    allowCachedAuthenticatedFallback: allowCachedFallback,
                    probeTimeout: probeTimeout)
                .ConfigureAwait(false)) {
                return true;
            }

            if (!runtimePinCleared
                && RequiresInteractiveSignInForCurrentTransport()
                && attempt >= 1) {
                // After OAuth callback succeeds, runtime state may still carry a stale account pin.
                // Clear it once and continue probing before declaring sign-in failure.
                _ = await TryClearNativeRuntimeAccountPinAsync().ConfigureAwait(false);
                runtimePinCleared = true;
            }

            if (attempt + 1 < maxProbeAttempts) {
                var delayMs = prioritizeDispatchLatency
                    ? Math.Min(500, 150 * (attempt + 1))
                    : Math.Min(2000, 250 * (attempt + 1));
                await Task.Delay(delayMs).ConfigureAwait(false);
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
            await SetStatusAsync(SessionStatus.SignInRequired()).ConfigureAwait(false);
            await SetActivityAsync("Post-login verification did not confirm account state. Use Sign In or Switch Account.").ConfigureAwait(false);
            return;
        }

        await HandlePostLoginCompletionAsync().ConfigureAwait(false);
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
                Model: string.IsNullOrWhiteSpace(metrics.Model) ? null : metrics.Model.Trim(),
                RequestedModel: string.IsNullOrWhiteSpace(metrics.RequestedModel) ? null : metrics.RequestedModel.Trim(),
                Transport: string.IsNullOrWhiteSpace(metrics.Transport) ? null : metrics.Transport.Trim(),
                EndpointHost: string.IsNullOrWhiteSpace(metrics.EndpointHost) ? null : metrics.EndpointHost.Trim());
        }

        UpdateAccountUsageFromMetrics(metrics.Usage);
    }

    private static string FormatMetricsTrace(ChatMetricsMessage metrics) {
        return "metrics: duration="
               + metrics.DurationMs.ToString(CultureInfo.InvariantCulture)
               + "ms"
               + (metrics.TtftMs is null ? string.Empty : " ttft=" + metrics.TtftMs.Value.ToString(CultureInfo.InvariantCulture) + "ms")
               + (metrics.Usage?.TotalTokens is null ? string.Empty : " tokens=" + metrics.Usage.TotalTokens.Value.ToString(CultureInfo.InvariantCulture))
               + " tools=" + metrics.ToolCallsCount.ToString(CultureInfo.InvariantCulture)
               + " rounds=" + metrics.ToolRounds.ToString(CultureInfo.InvariantCulture)
               + (string.IsNullOrWhiteSpace(metrics.RequestedModel) ? string.Empty : " requestedModel=" + metrics.RequestedModel.Trim())
               + (string.IsNullOrWhiteSpace(metrics.Model) ? string.Empty : " model=" + metrics.Model.Trim())
               + (string.IsNullOrWhiteSpace(metrics.Transport) ? string.Empty : " transport=" + metrics.Transport.Trim())
               + (string.IsNullOrWhiteSpace(metrics.EndpointHost) ? string.Empty : " endpoint=" + metrics.EndpointHost.Trim())
               + " outcome=" + (metrics.Outcome ?? "unknown");
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
            _isAuthenticated = false;
            _authenticatedAccountId = null;
            ResetEnsureLoginProbeCache();
            _loginInProgress = false;
            _isConnected = false;
            _autoSignInAttempted = _appState.OnboardingCompleted || AnyConversationHasMessages();
            // Keep the sidecar process alive on transient pipe disconnects.
            // This avoids unnecessary process churn while the client auto-reconnect loop runs.
            await SetStatusAsync(SessionStatus.Disconnected()).ConfigureAwait(false);
            EnsureAutoReconnectLoop();
        });
    }

    private async Task<bool> EnsureConnectedAsync() {
        if (_client is not null && await IsClientAliveAsync(_client).ConfigureAwait(false)) {
            _isConnected = true;
            return true;
        }

        await ConnectAsync(fromUserAction: false, connectBudgetOverride: DispatchConnectBudget).ConfigureAwait(false);
        var connected = _client is not null && await IsClientAliveAsync(_client).ConfigureAwait(false);
        _isConnected = connected;
        if (!connected) {
            await PublishSessionStateAsync().ConfigureAwait(false);
        }
        return connected;
    }

}
