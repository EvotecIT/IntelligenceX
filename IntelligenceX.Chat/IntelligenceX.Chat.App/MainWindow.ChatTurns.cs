using System;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.App.Conversation;
using IntelligenceX.Chat.App.Launch;
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
            ? (_appState.LocalProviderRuntimeOverrideActive ? _localProviderModel : _sessionPolicy?.RuntimeIdentity?.Model)
            : conversation.ModelOverride!;
        var resolvedModel = _appState.LocalProviderRuntimeOverrideActive && string.IsNullOrWhiteSpace(conversation.ModelOverride)
            ? ResolveChatRequestModelOverride(
                _localProviderTransport,
                _localProviderBaseUrl,
                configuredModel,
                _availableModels)
            : configuredModel;
        var assistantModelLabel = string.IsNullOrWhiteSpace(resolvedModel) ? "(auto)" : resolvedModel.Trim();
        conversation.ModelLabel = assistantModelLabel;

        // Keep the turn startup path responsive; state durability is still preserved via debounced persistence.
        QueuePersistAppState();
        return new ChatTurnContext(
            conversation,
            conversationId,
            NextId(),
            text,
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
        await EnsureSelectedServiceProfileForTurnAsync(client, cancellationToken).ConfigureAwait(false);
        var options = BuildChatRequestOptions(turn.Conversation);
        var modelLabel = options?.Model ?? _sessionPolicy?.RuntimeIdentity?.Model;
        var resolvedModelLabel = string.IsNullOrWhiteSpace(modelLabel) ? "(auto)" : modelLabel.Trim();
        turn.Conversation.ModelLabel = resolvedModelLabel;
        turn = turn with { AssistantModelLabel = resolvedModelLabel };
        var req = new ChatRequest {
            RequestId = turn.RequestId,
            ThreadId = turn.Conversation.ThreadId,
            Text = BuildRequestTextForService(turn.UserText, turn.Conversation),
            Options = options
        };

        var runner = ResolveTurnRunner(client);
        var result = await runner
            .RunAsync(req, ApplyChatTurnUpdateAsync, cancellationToken)
            .ConfigureAwait(false);
        await ApplyChatResultAsync(turn, result.Response).ConfigureAwait(false);
    }

    private async Task EnsureSelectedServiceProfileForTurnAsync(ChatServiceClient client, CancellationToken cancellationToken) {
        if (_appState.LocalProviderRuntimeOverrideActive) {
            return;
        }

        var desiredProfile = ChatServiceLaunchProfileMapper.NormalizeProfileName(_appProfileName);
        var profiles = await client.ListProfilesAsync(cancellationToken).ConfigureAwait(false);
        var profileChanged = !string.Equals(profiles.ActiveProfile, desiredProfile, StringComparison.OrdinalIgnoreCase);
        if (profileChanged) {
            if (!ContainsProfileName(profiles.Profiles ?? Array.Empty<string>(), desiredProfile)) {
                throw new InvalidOperationException(
                    $"The selected chat profile '{desiredProfile}' is not available in the connected service. Select an existing profile or save this profile before sending.");
            }

            var selected = await client.SetProfileAsync(desiredProfile, newThread: false, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (!selected.Ok) {
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(selected.Message)
                        ? $"The chat service could not select profile '{desiredProfile}'."
                        : selected.Message);
            }
            _serviceActiveProfileName = desiredProfile;
        }

        if (profileChanged || _sessionPolicy is null
            || !string.Equals(_sessionPolicy.RuntimeIdentity?.ProfileName, desiredProfile, StringComparison.OrdinalIgnoreCase)) {
            var hello = await client.RequestAsync<HelloMessage>(
                    new HelloRequest { RequestId = NextId() }, cancellationToken)
                .ConfigureAwait(false);
            var tools = await client.RequestAsync<ToolListMessage>(
                    new ListToolsRequest { RequestId = NextId() }, cancellationToken)
                .ConfigureAwait(false);
            _sessionPolicy = hello.Policy;
            UpdateToolCatalog(tools.Tools, tools.RoutingCatalog, tools.Packs, tools.Plugins, tools.CapabilitySnapshot);
            SeedBackgroundSchedulerSnapshot(tools.CapabilitySnapshot?.BackgroundScheduler);
            RecordStartupBootstrapCacheMode(_sessionPolicy);
        }
    }

    private ChatServiceTurnRunner ResolveTurnRunner(ChatServiceClient client) {
        if (!ReferenceEquals(client, _client)) {
            return new ChatServiceTurnRunner(client);
        }

        return _turnRunner ??= new ChatServiceTurnRunner(client);
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
