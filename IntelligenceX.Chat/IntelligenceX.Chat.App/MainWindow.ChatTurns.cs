using System;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.App.Conversation;
using IntelligenceX.Chat.App.Rendering;
using IntelligenceX.Chat.Client;
using Microsoft.UI.Xaml;

namespace IntelligenceX.Chat.App;

public sealed partial class MainWindow : Window {
    private const string ExecutionContractMarker = "ix:execution-contract:v1";
    private const int MaxExecutionContractHistoryScan = 12;
    private const int InterimFinalNearDuplicateSuffixThresholdChars = 24;
    private sealed record ChatTurnContext(
        ConversationRuntime Conversation,
        string ConversationId,
        string RequestId,
        string UserText,
        string RequestText,
        string? AssistantModelLabel,
        long? AuthProbeMs);

    private async Task<ChatTurnContext?> PrepareChatTurnAsync(string text, bool skipUserBubble) {
        var conversation = GetActiveConversation();
        var conversationId = conversation.Id;

        if (!skipUserBubble) {
            await AppendUserMessageAsync(conversation, text).ConfigureAwait(false);
        }

        await SetActivityAsync("Checking account and runtime status...").ConfigureAwait(false);

        var dispatchAuthProbeOutcome = DispatchAuthenticationProbeOutcome.Authenticated;
        long? authProbeMs = null;
        if (!IsEffectivelyAuthenticatedForCurrentTransport()) {
            var authProbeStartedUtc = DateTime.UtcNow;
            dispatchAuthProbeOutcome = await ProbeAuthenticationStateForDispatchAsync(EnsureLoginFastPathProbeTimeout).ConfigureAwait(false);
            authProbeMs = TryComputeElapsedMs(authProbeStartedUtc, DateTime.UtcNow);
        }

        var requireSignInBeforeDispatch = !IsEffectivelyAuthenticatedForCurrentTransport()
                                          && dispatchAuthProbeOutcome == DispatchAuthenticationProbeOutcome.Unauthenticated;
        if (requireSignInBeforeDispatch) {
            // User bubble is already rendered for this prompt, so retries after sign-in
            // must reuse that bubble instead of appending duplicate user messages.
            var promptQueued = TryEnqueuePromptAfterLogin(text, conversationId, out var queuedCount, skipUserBubbleOnDispatch: true);
            var loginStarted = await StartLoginFlowIfNeededAsync(skipPreLoginAuthProbe: true).ConfigureAwait(false);
            if (loginStarted) {
                var waitingText = promptQueued
                    ? $"Waiting for sign-in... ({queuedCount}/{MaxQueuedTurns} queued)"
                    : "Waiting for sign-in... (queue full)";
                await SetStatusAsync(waitingText).ConfigureAwait(false);
            } else {
                await SetStatusAsync(SessionStatus.SignInRequired()).ConfigureAwait(false);
            }
            if (!loginStarted) {
                AppendSystem(SystemNotice.SignInRequiredBeforeSendingMessages());
                await SetActivityAsync("Sign-in required. Prompt will run after login.").ConfigureAwait(false);
            } else if (!promptQueued) {
                AppendSystem("Sign-in queue is full. Complete sign-in or wait for queued prompts to run.");
                await SetActivityAsync("Sign-in queue is full. Waiting for available retry slot.").ConfigureAwait(false);
            } else {
                await SetActivityAsync("Prompt queued for retry after sign-in.").ConfigureAwait(false);
            }

            return null;
        }

        _assistantStreamingState.Reset();
        var transport = NormalizeLocalProviderTransport(_localProviderTransport);
        var baseUrl = (_localProviderBaseUrl ?? string.Empty).Trim();
        var preset = DetectCompatibleProviderPreset(baseUrl);
        var copilotConnected = string.Equals(transport, TransportCompatibleHttp, StringComparison.OrdinalIgnoreCase)
                               && baseUrl.Contains("api.githubcopilot.com", StringComparison.OrdinalIgnoreCase);
        conversation.RuntimeLabel = ResolveRuntimeProviderLabelForState(transport, preset, copilotConnected, baseUrl);
        var configuredModel = string.IsNullOrWhiteSpace(conversation.ModelOverride)
            ? _localProviderModel
            : conversation.ModelOverride!;
        var resolvedModel = ResolveChatRequestModelOverride(
            _localProviderTransport,
            _localProviderBaseUrl,
            configuredModel,
            _availableModels);
        var assistantModelLabel = string.IsNullOrWhiteSpace(resolvedModel) ? "(auto)" : resolvedModel.Trim();
        conversation.ModelLabel = assistantModelLabel;

        // Keep the turn startup path responsive; state durability is still preserved via debounced persistence.
        QueuePersistAppState();
        try {
            await ApplyUserProfileIntentAsync(text).ConfigureAwait(false);
        } catch (Exception ex) {
            if (VerboseServiceLogs || _debugMode) {
                AppendSystem("Profile intent update skipped for this turn: " + ex.Message);
            }
        }

        return new ChatTurnContext(
            conversation,
            conversationId,
            NextId(),
            text,
            BuildRequestTextForService(text),
            assistantModelLabel,
            authProbeMs);
    }

    private async Task AppendUserMessageAsync(ConversationRuntime conversation, string text) {
        var now = DateTime.Now;
        conversation.Messages.Add(("User", text, now, null));
        conversation.Title = ComputeConversationTitle(conversation.Title, conversation.Messages);
        conversation.UpdatedUtc = now.ToUniversalTime();
        if (string.Equals(conversation.Id, _activeConversationId, StringComparison.OrdinalIgnoreCase)) {
            // Avoid waiting on WebView render before request dispatch.
            _ = RenderTranscriptAsync();
        }

        // Avoid blocking request dispatch on storage I/O; debounce persistence instead.
        QueuePersistAppState();
    }

    private async Task ExecuteChatTurnWithReconnectAsync(ChatTurnContext turn, CancellationToken cancellationToken) {
        try {
            var initialClient = _client;
            if (initialClient is null) {
                if (await EnsureConnectedAsync(deferPostConnectMetadataSync: true).ConfigureAwait(false) && _client is { } reconnectClient) {
                    try {
                        await ExecuteChatTurnWithThreadRecoveryAsync(reconnectClient, turn, cancellationToken).ConfigureAwait(false);
                        return;
                    } catch (Exception reconnectEx) {
                        await ApplyTurnFailureAsync(turn, ResolveTurnOutcome(turn.RequestId, reconnectEx, disconnectedFallback: false, cancellationToken)).ConfigureAwait(false);
                        return;
                    }
                }

                await ApplyTurnFailureAsync(turn, AssistantTurnOutcome.Disconnected()).ConfigureAwait(false);
                return;
            }

            try {
                await ExecuteChatTurnWithThreadRecoveryAsync(initialClient, turn, cancellationToken).ConfigureAwait(false);
                return;
            } catch (Exception ex) when (IsDisconnectedError(ex)) {
                await DisposeClientAsync().ConfigureAwait(false);
                if (await EnsureConnectedAsync(deferPostConnectMetadataSync: true).ConfigureAwait(false) && _client is { } retryClient) {
                    try {
                        ResetStreamingTurnStateForRetry(turn);
                        await ExecuteChatTurnWithThreadRecoveryAsync(retryClient, turn, cancellationToken).ConfigureAwait(false);
                        return;
                    } catch (Exception retryEx) {
                        var resolvedRetryEx = retryEx;
                        if (await TryRecoverChatSlotByCancelingKickoffAsync(retryEx).ConfigureAwait(false) && _client is { } kickoffFreedClient) {
                            try {
                                ResetStreamingTurnStateForRetry(turn);
                                await ExecuteChatTurnWithThreadRecoveryAsync(kickoffFreedClient, turn, cancellationToken).ConfigureAwait(false);
                                return;
                            } catch (Exception kickoffRetryEx) {
                                resolvedRetryEx = kickoffRetryEx;
                            }
                        }

                        if (await TryHandleAuthenticationRequiredTurnFailureAsync(turn, resolvedRetryEx).ConfigureAwait(false)) {
                            return;
                        }

                        var promptQueued = false;
                        if (IsUsageLimitError(resolvedRetryEx)) {
                            var activeUsageLabel = ResolveActiveUsageLabelForDisplay();
                            MarkUsageLimitForActiveAccount(resolvedRetryEx.Message);
                            promptQueued = QueuePromptAfterSignIn(turn.UserText, turn.ConversationId);
                            await SetStatusAsync(SessionStatus.UsageLimitReached()).ConfigureAwait(false);
                            if (promptQueued) {
                                AppendSystem(turn.Conversation, SystemNotice.PromptQueuedAfterUsageLimit(activeUsageLabel));
                            }
                        }
                        await ApplyTurnFailureAsync(turn, ResolveTurnOutcome(turn.RequestId, resolvedRetryEx, disconnectedFallback: false, cancellationToken)).ConfigureAwait(false);
                        return;
                    }
                }

                await ApplyTurnFailureAsync(turn, ResolveTurnOutcome(turn.RequestId, ex, disconnectedFallback: true, cancellationToken)).ConfigureAwait(false);
                return;
            } catch (Exception ex) {
                var resolvedEx = ex;
                if (await TryRecoverChatSlotByCancelingKickoffAsync(ex).ConfigureAwait(false) && _client is { } kickoffFreedClient) {
                    try {
                        ResetStreamingTurnStateForRetry(turn);
                        await ExecuteChatTurnWithThreadRecoveryAsync(kickoffFreedClient, turn, cancellationToken).ConfigureAwait(false);
                        return;
                    } catch (Exception kickoffRetryEx) {
                        resolvedEx = kickoffRetryEx;
                    }
                }

                if (await TryHandleAuthenticationRequiredTurnFailureAsync(turn, resolvedEx).ConfigureAwait(false)) {
                    return;
                }

                var promptQueued = false;
                if (IsUsageLimitError(resolvedEx)) {
                    var activeUsageLabel = ResolveActiveUsageLabelForDisplay();
                    MarkUsageLimitForActiveAccount(resolvedEx.Message);
                    promptQueued = QueuePromptAfterSignIn(turn.UserText, turn.ConversationId);
                    await SetStatusAsync(SessionStatus.UsageLimitReached()).ConfigureAwait(false);
                    if (promptQueued) {
                        AppendSystem(turn.Conversation, SystemNotice.PromptQueuedAfterUsageLimit(activeUsageLabel));
                    }
                }
                await ApplyTurnFailureAsync(turn, ResolveTurnOutcome(turn.RequestId, resolvedEx, disconnectedFallback: false, cancellationToken)).ConfigureAwait(false);
                return;
            }
        } finally {
            await SetActivityAsync(null).ConfigureAwait(false);
        }
    }

    private async Task ExecuteChatTurnWithThreadRecoveryAsync(ChatServiceClient client, ChatTurnContext turn, CancellationToken cancellationToken) {
        try {
            await ExecuteChatTurnAsync(client, turn, cancellationToken).ConfigureAwait(false);
            return;
        } catch (Exception ex) {
            if (!await TryPrepareMissingThreadRecoveryAsync(turn, ex).ConfigureAwait(false)) {
                throw;
            }
        }

        await ExecuteChatTurnAsync(client, turn, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> TryPrepareMissingThreadRecoveryAsync(ChatTurnContext turn, Exception ex) {
        if (!IsMissingTransportThreadError(ex)) {
            return false;
        }

        turn.Conversation.ThreadId = null;
        if (string.Equals(turn.Conversation.Id, _activeConversationId, StringComparison.OrdinalIgnoreCase)) {
            _threadId = null;
        }

        await PersistAppStateAsync().ConfigureAwait(false);
        await SetStatusAsync("Recovered stale runtime thread. Retrying turn...").ConfigureAwait(false);
        return true;
    }

    private async Task<bool> TryRecoverChatSlotByCancelingKickoffAsync(Exception ex) {
        if (!IsChatInProgressError(ex)) {
            return false;
        }

        if (!_modelKickoffInProgress && string.IsNullOrWhiteSpace(_activeKickoffRequestId)) {
            return false;
        }

        var hasKickoffRequestToCancel = !string.IsNullOrWhiteSpace(_activeKickoffRequestId) && _client is not null;
        await CancelModelKickoffIfRunningAsync().ConfigureAwait(false);
        if (hasKickoffRequestToCancel && KickoffRecoverySettleDelay > TimeSpan.Zero) {
            await Task.Delay(KickoffRecoverySettleDelay).ConfigureAwait(false);
        }
        return true;
    }

    private void ResetStreamingTurnStateForRetry(ChatTurnContext turn) {
        if (!_assistantStreamingState.HasReceivedDelta() && !HasActiveTurnInterimResult()) {
            return;
        }

        _assistantStreamingState.Reset();
        ResetActiveTurnAssistantVisuals(turn.ConversationId);
    }

    private async Task ExecuteChatTurnAsync(ChatServiceClient client, ChatTurnContext turn, CancellationToken cancellationToken) {
        var req = new ChatRequest {
            RequestId = turn.RequestId,
            ThreadId = turn.Conversation.ThreadId,
            Text = turn.RequestText,
            Options = BuildChatRequestOptions(turn.Conversation)
        };

        var result = await client.RequestAsync<ChatResultMessage>(req, cancellationToken).ConfigureAwait(false);
        await ApplyChatResultAsync(turn, result).ConfigureAwait(false);
    }

    private async Task ApplyChatResultAsync(ChatTurnContext turn, ChatResultMessage result) {
        if (!TryFinalizeActiveTurnAssistantState(turn.Conversation, succeeded: true)) {
            return;
        }

        var completion = CompleteTurnLatencyTracking(turn.RequestId, DateTime.UtcNow);
        if (completion is not null) {
            RegisterTurnSuccessReliability(completion);
            AppendTurnLatencySystemNoticeIfNeeded(completion);
            lock (_turnDiagnosticsSync) {
                if (_lastTurnMetrics is null
                    || !string.Equals(_lastTurnMetrics.RequestId, completion.RequestId, StringComparison.OrdinalIgnoreCase)) {
                    _lastTurnMetrics = BuildTurnMetricsSnapshotFromCompletion(
                        completion,
                        outcome: "ok",
                        errorCode: null,
                        ttftMs: completion.DispatchToFirstDeltaMs,
                        promptTokens: null,
                        completionTokens: null,
                        totalTokens: null,
                        cachedPromptTokens: null,
                        reasoningTokens: null,
                        model: null,
                        requestedModel: null,
                        transport: null,
                        endpointHost: null);
                }
            }
        }

        var conversation = turn.Conversation;
        conversation.ThreadId = result.ThreadId;
        if (string.Equals(conversation.Id, _activeConversationId, StringComparison.OrdinalIgnoreCase)) {
            _threadId = result.ThreadId;
        }

        var normalizedAssistantTurn = await ApplyAssistantProfileUpdateAsync(result.Text).ConfigureAwait(false);
        var assistantText = normalizedAssistantTurn.VisibleText;
        conversation.PendingActions = normalizedAssistantTurn.PendingActions;
        conversation.PendingAssistantQuestionHint = normalizedAssistantTurn.PendingAssistantQuestionHint;
        assistantText = CollapseRepeatedExecutionContractBlockers(conversation, assistantText);
        _ = TryGetLastAssistantText(conversation, out var latestAssistantText);
        var activeTurnReceivedDelta = _assistantStreamingState.HasReceivedDelta();
        var activeTurnInterimResultSeen = HasActiveTurnInterimResult();
        var appendFinalAfterInterim = ShouldRenderFinalAssistantAsSeparateBubbleAfterInterim()
                                      && activeTurnInterimResultSeen
                                      && ShouldAppendFinalAssistantAfterInterim(assistantText, latestAssistantText);
        var appendFinalAfterStreamedDraft = ShouldAppendFinalAssistantAfterStreamedDraft(
            activeTurnReceivedDelta,
            activeTurnInterimResultSeen,
            assistantText,
            latestAssistantText);
        if (ShouldPreserveStreamedAssistantDraftOnNoTextWarning(
                activeTurnReceivedDelta,
                assistantText,
                latestAssistantText,
                out var runtimeWarningNotice)) {
            conversation.Messages.Add(("System", runtimeWarningNotice, DateTime.Now, null));
        } else if (appendFinalAfterInterim || appendFinalAfterStreamedDraft) {
            // Keep interim and final as separate assistant bubbles only when the final synthesis
            // materially differs from the existing draft/interim snapshot. This avoids duplicate bubble inflation.
            AppendAssistantText(conversation, assistantText);
        } else {
            ReplaceLastAssistantText(conversation, assistantText);
        }
        BindActiveTurnAssistantMessage(conversation);
        SetActiveTurnAssistantChannel(conversation, AssistantBubbleChannelKind.Final);
        SetActiveTurnAssistantProvisional(conversation, provisional: false, preferProvisionalEvents: false);
        PromoteAuthenticatedStateFromFinalAssistantTurn();
        ApplyFinalAssistantTurnTimeline(conversation, result.TurnTimelineEvents);
        _assistantStreamingState.ClearReceivedDelta();
        if (result.Tools is not null && (result.Tools.Calls.Count > 0 || result.Tools.Outputs.Count > 0)) {
            var toolMarkdown = BuildToolRunTranscriptMarkdown(result.Tools);
            if (!string.IsNullOrWhiteSpace(toolMarkdown)) {
                conversation.Messages.Add(("Tools", toolMarkdown, DateTime.Now, turn.AssistantModelLabel));
            }
        }

        conversation.UpdatedUtc = DateTime.UtcNow;
        conversation.Title = ComputeConversationTitle(conversation.Title, conversation.Messages);
        if (string.Equals(conversation.Id, _activeConversationId, StringComparison.OrdinalIgnoreCase)) {
            await RenderTranscriptAsync().ConfigureAwait(false);
        }

        await PersistAppStateAsync().ConfigureAwait(false);
    }

    private async Task ApplyTurnFailureAsync(ChatTurnContext turn, AssistantTurnOutcome outcome) {
        if (!TryFinalizeActiveTurnAssistantState(turn.Conversation, succeeded: false)) {
            return;
        }

        // Preserve the last successful continuation cues across transient failures so
        // compact retries can still answer the assistant's most recent pending question.

        var completion = CompleteTurnLatencyTracking(turn.RequestId, DateTime.UtcNow);
        if (completion is not null) {
            RegisterTurnFailureReliability(completion, outcome);
            AppendTurnLatencySystemNoticeIfNeeded(completion);
            lock (_turnDiagnosticsSync) {
                _lastTurnMetrics = BuildTurnMetricsSnapshotFromCompletion(
                    completion,
                    outcome: MapOutcomeToMetricsToken(outcome),
                    errorCode: MapErrorCodeToMetricsToken(outcome),
                    ttftMs: completion.DispatchToFirstDeltaMs,
                    promptTokens: null,
                    completionTokens: null,
                    totalTokens: null,
                    cachedPromptTokens: null,
                    reasoningTokens: null,
                    model: null,
                    requestedModel: null,
                    transport: null,
                    endpointHost: null);
            }
        }

        if (TryGetPartialTurnFailureNotice(turn.Conversation, outcome, out var notice)) {
            turn.Conversation.Messages.Add(("System", notice, DateTime.Now, null));
        } else {
            ReplaceLastAssistantText(turn.Conversation, AssistantTurnOutcomeFormatter.Format(outcome));
        }
        BindActiveTurnAssistantMessage(turn.Conversation);
        SetActiveTurnAssistantChannel(turn.Conversation, AssistantBubbleChannelKind.Final);
        SetActiveTurnAssistantProvisional(turn.Conversation, provisional: false, preferProvisionalEvents: false);

        turn.Conversation.UpdatedUtc = DateTime.UtcNow;
        if (string.Equals(turn.Conversation.Id, _activeConversationId, StringComparison.OrdinalIgnoreCase)) {
            await RenderTranscriptAsync().ConfigureAwait(false);
        }

        await PersistAppStateAsync().ConfigureAwait(false);
    }

    private bool QueuePromptAfterSignIn(string userText, string conversationId) {
        var text = (userText ?? string.Empty).Trim();
        if (text.Length == 0) {
            return false;
        }

        return TryEnqueuePromptAfterLogin(text, (conversationId ?? string.Empty).Trim(), out _);
    }

    private async Task<bool> TryHandleAuthenticationRequiredTurnFailureAsync(ChatTurnContext turn, Exception ex) {
        if (!RequiresInteractiveSignInForCurrentTransport() || !IsAuthenticationRequiredError(ex)) {
            return false;
        }

        SetInteractiveAuthenticationKnown(isAuthenticated: false);
        var promptQueued = TryEnqueuePromptAfterLogin(
            turn.UserText,
            turn.ConversationId,
            out var queuedCount,
            skipUserBubbleOnDispatch: true);
        var loginStarted = await StartLoginFlowIfNeededAsync(skipPreLoginAuthProbe: true).ConfigureAwait(false);
        if (loginStarted) {
            var waitingText = promptQueued
                ? $"Waiting for sign-in... ({queuedCount}/{MaxQueuedTurns} queued)"
                : "Waiting for sign-in... (queue full)";
            await SetStatusAsync(waitingText).ConfigureAwait(false);
        } else {
            await SetStatusAsync(SessionStatus.SignInRequired()).ConfigureAwait(false);
        }

        if (!loginStarted) {
            AppendSystem(turn.Conversation, SystemNotice.SignInRequiredBeforeSendingMessages());
            await SetActivityAsync("Sign-in required. Prompt will run after login.").ConfigureAwait(false);
        } else if (!promptQueued) {
            AppendSystem(turn.Conversation, "Sign-in queue is full. Complete sign-in or wait for queued prompts to run.");
            await SetActivityAsync("Sign-in queue is full. Waiting for available retry slot.").ConfigureAwait(false);
        } else {
            AppendSystem(turn.Conversation, SystemNotice.SignInRequiredBeforeSendingMessages());
            await SetActivityAsync("Prompt queued for retry after sign-in.").ConfigureAwait(false);
        }

        var failureMessage = promptQueued
            ? "Authentication required. Prompt queued for retry after sign-in."
            : "Authentication required. Sign-in queue is full; complete sign-in and resend.";
        await ApplyTurnFailureAsync(
                turn,
                AssistantTurnOutcome.Error(failureMessage))
            .ConfigureAwait(false);
        return true;
    }

    internal static bool IsActiveTurnCancellation(Exception ex, CancellationToken cancellationToken) {
        return ex is OperationCanceledException && cancellationToken.IsCancellationRequested;
    }

    private AssistantTurnOutcome ResolveTurnOutcome(
        string requestId,
        Exception ex,
        bool disconnectedFallback,
        CancellationToken cancellationToken) {
        if (IsCanceledTurn(requestId, ex) || IsActiveTurnCancellation(ex, cancellationToken)) {
            return AssistantTurnOutcome.Canceled();
        }

        if (IsUsageLimitError(ex)) {
            return AssistantTurnOutcome.UsageLimit(ex.Message, ResolveActiveUsageLabelForDisplay());
        }

        var message = ex.Message ?? string.Empty;
        if (message.Contains("Tool runner exceeded max rounds", StringComparison.OrdinalIgnoreCase)
            || message.Contains("max rounds", StringComparison.OrdinalIgnoreCase)) {
            return AssistantTurnOutcome.ToolRoundLimit(message);
        }

        return disconnectedFallback
            ? AssistantTurnOutcome.Disconnected()
            : AssistantTurnOutcome.Error(ex.Message);
    }
}
