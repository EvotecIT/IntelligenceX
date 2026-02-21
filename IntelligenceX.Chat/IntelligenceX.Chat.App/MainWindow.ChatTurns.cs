using System;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.App.Conversation;
using IntelligenceX.Chat.Client;
using Microsoft.UI.Xaml;

namespace IntelligenceX.Chat.App;

public sealed partial class MainWindow : Window {
    private const string ExecutionContractMarker = "ix:execution-contract:v1";
    private const int MaxExecutionContractHistoryScan = 12;
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
            if (dispatchAuthProbeOutcome == DispatchAuthenticationProbeOutcome.Authenticated) {
                _isAuthenticated = true;
            }
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

        _assistantStreaming.Clear();
        _activeTurnReceivedDelta = false;
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
        var now = DateTime.Now;
        conversation.Messages.Add(("Assistant", string.Empty, now, assistantModelLabel));
        conversation.Title = ComputeConversationTitle(conversation.Title, conversation.Messages);
        conversation.UpdatedUtc = now.ToUniversalTime();
        if (string.Equals(conversationId, _activeConversationId, StringComparison.OrdinalIgnoreCase)) {
            await RenderTranscriptAsync().ConfigureAwait(false);
        }

        await PersistAppStateAsync().ConfigureAwait(false);
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
            await RenderTranscriptAsync().ConfigureAwait(false);
        }

        await PersistAppStateAsync().ConfigureAwait(false);
    }

    private async Task ExecuteChatTurnWithReconnectAsync(ChatTurnContext turn, CancellationToken cancellationToken) {
        try {
            var initialClient = _client;
            if (initialClient is null) {
                if (await EnsureConnectedAsync().ConfigureAwait(false) && _client is { } reconnectClient) {
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
                if (await EnsureConnectedAsync().ConfigureAwait(false) && _client is { } retryClient) {
                    try {
                        await ExecuteChatTurnWithThreadRecoveryAsync(retryClient, turn, cancellationToken).ConfigureAwait(false);
                        return;
                    } catch (Exception retryEx) {
                        var resolvedRetryEx = retryEx;
                        if (await TryRecoverChatSlotByCancelingKickoffAsync(retryEx).ConfigureAwait(false) && _client is { } kickoffFreedClient) {
                            try {
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
                            MarkUsageLimitForActiveAccount(resolvedRetryEx.Message);
                            promptQueued = QueuePromptAfterSignIn(turn.UserText, turn.ConversationId);
                            await SetStatusAsync(SessionStatus.UsageLimitReached()).ConfigureAwait(false);
                            if (promptQueued) {
                                AppendSystem(turn.Conversation, SystemNotice.PromptQueuedAfterUsageLimit());
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
                    MarkUsageLimitForActiveAccount(resolvedEx.Message);
                    promptQueued = QueuePromptAfterSignIn(turn.UserText, turn.ConversationId);
                    await SetStatusAsync(SessionStatus.UsageLimitReached()).ConfigureAwait(false);
                    if (promptQueued) {
                        AppendSystem(turn.Conversation, SystemNotice.PromptQueuedAfterUsageLimit());
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
        var completion = CompleteTurnLatencyTracking(turn.RequestId, DateTime.UtcNow);
        if (completion is not null) {
            RegisterTurnSuccessReliability(completion);
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

        var assistantText = await ApplyAssistantProfileUpdateAsync(result.Text).ConfigureAwait(false);
        assistantText = CollapseRepeatedExecutionContractBlockers(conversation, assistantText);
        if (ShouldPreserveStreamedAssistantDraftOnNoTextWarning(
                _activeTurnReceivedDelta,
                assistantText,
                TryGetLastAssistantText(conversation, out var streamedAssistantText) ? streamedAssistantText : string.Empty,
                out var runtimeWarningNotice)) {
            conversation.Messages.Add(("System", runtimeWarningNotice, DateTime.Now, null));
        } else {
            ReplaceLastAssistantText(conversation, assistantText);
        }
        _activeTurnReceivedDelta = false;
        if (_debugMode && result.Tools is not null && (result.Tools.Calls.Count > 0 || result.Tools.Outputs.Count > 0)) {
            conversation.Messages.Add(("Tools", BuildToolRunMarkdown(result.Tools), DateTime.Now, turn.AssistantModelLabel));
        }

        conversation.UpdatedUtc = DateTime.UtcNow;
        conversation.Title = ComputeConversationTitle(conversation.Title, conversation.Messages);
        if (string.Equals(conversation.Id, _activeConversationId, StringComparison.OrdinalIgnoreCase)) {
            await RenderTranscriptAsync().ConfigureAwait(false);
        }

        await PersistAppStateAsync().ConfigureAwait(false);
    }

    private async Task ApplyTurnFailureAsync(ChatTurnContext turn, AssistantTurnOutcome outcome) {
        var completion = CompleteTurnLatencyTracking(turn.RequestId, DateTime.UtcNow);
        if (completion is not null) {
            RegisterTurnFailureReliability(completion, outcome);
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

        turn.Conversation.UpdatedUtc = DateTime.UtcNow;
        if (string.Equals(turn.Conversation.Id, _activeConversationId, StringComparison.OrdinalIgnoreCase)) {
            await RenderTranscriptAsync().ConfigureAwait(false);
        }

        await PersistAppStateAsync().ConfigureAwait(false);
    }

    private bool TryGetPartialTurnFailureNotice(ConversationRuntime conversation, AssistantTurnOutcome outcome, out string notice) {
        notice = string.Empty;
        if (!_activeTurnReceivedDelta) {
            return false;
        }

        if (!TryGetLastAssistantText(conversation, out var assistantText)) {
            return false;
        }

        var normalizedAssistant = (assistantText ?? string.Empty).Trim();
        if (normalizedAssistant.Length == 0 || StartsWithOutcomeMarker(normalizedAssistant)) {
            return false;
        }

        notice = outcome.Kind switch {
            AssistantTurnOutcomeKind.ToolRoundLimit =>
                "Partial response shown above. The turn hit the tool safety limit before completion. "
                + "Reply naturally to proceed, or narrow scope (one DC / one OU).",
            AssistantTurnOutcomeKind.UsageLimit =>
                "Partial response shown above. The turn then hit your account usage limit. "
                + "Switch account or try again later.",
            AssistantTurnOutcomeKind.Canceled =>
                "Partial response shown above. Turn was canceled before completion.",
            AssistantTurnOutcomeKind.Disconnected =>
                "Partial response shown above. Connection dropped before the turn could finish.",
            _ =>
                "Partial response shown above. The turn ended before completion."
        };
        _activeTurnReceivedDelta = false;
        return true;
    }

    private static bool TryGetLastAssistantText(ConversationRuntime conversation, out string text) {
        for (var i = conversation.Messages.Count - 1; i >= 0; i--) {
            var entry = conversation.Messages[i];
            if (!string.Equals(entry.Role, "Assistant", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            text = entry.Text;
            return true;
        }

        text = string.Empty;
        return false;
    }

    internal static bool ShouldPreserveStreamedAssistantDraftOnNoTextWarning(
        bool activeTurnReceivedDelta,
        string? finalAssistantText,
        string? streamedAssistantText,
        out string notice) {
        notice = string.Empty;
        if (!activeTurnReceivedDelta) {
            return false;
        }

        var finalText = (finalAssistantText ?? string.Empty).Trim();
        if (!IsNoTextWarningText(finalText)) {
            return false;
        }

        var streamed = (streamedAssistantText ?? string.Empty).Trim();
        if (streamed.Length == 0 || StartsWithOutcomeMarker(streamed) || IsNoTextWarningText(streamed)) {
            return false;
        }

        notice = "Runtime warning: no final response envelope was produced. Kept the partial streamed response shown above.";
        return true;
    }

    internal static bool IsNoTextWarningText(string? text) {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        return normalized.StartsWith("[warning] No response text was produced", StringComparison.OrdinalIgnoreCase);
    }

    private static string CollapseRepeatedExecutionContractBlockers(ConversationRuntime conversation, string assistantText) {
        if (!TryParseExecutionContractBlocker(assistantText, out var reasonCode, out var actionId)) {
            return assistantText;
        }

        if (!TryFindRecentExecutionContractBlocker(
                conversation,
                currentAssistantText: assistantText,
                actionIdHint: actionId,
                reasonHint: reasonCode,
                previousReasonCode: out var previousReasonCode,
                previousActionId: out var previousActionId)) {
            return assistantText;
        }

        if (actionId.Length == 0) {
            actionId = previousActionId;
        }

        if (reasonCode.Length == 0) {
            reasonCode = previousReasonCode;
        }
        if (reasonCode.Length == 0) {
            reasonCode = "no_tool_calls_after_retries";
        }

        var actionHint = actionId.Length > 0 ? $"Action: /act {actionId}" + Environment.NewLine + Environment.NewLine : string.Empty;
        return $$"""
            [Execution blocked]
            {{ExecutionContractMarker}}
            Still blocked; no new tool output was produced in this retry.

            {{actionHint}}Reason code: {{reasonCode}}

            Retry with narrower scope (single DC/domain), or use Stop if this turn is looping without new tool output.
            """;
    }

    private static bool TryFindRecentExecutionContractBlocker(ConversationRuntime conversation, string? currentAssistantText, string? actionIdHint, string? reasonHint,
        out string previousReasonCode, out string previousActionId) {
        previousReasonCode = string.Empty;
        previousActionId = string.Empty;

        var normalizedCurrentAssistantText = NormalizeExecutionContractBlockerText(currentAssistantText);
        var normalizedActionHint = (actionIdHint ?? string.Empty).Trim();
        var normalizedReasonHint = (reasonHint ?? string.Empty).Trim();

        var scanned = 0;
        var skippedCurrentBlocker = false;
        for (var i = conversation.Messages.Count - 1; i >= 0; i--) {
            var entry = conversation.Messages[i];
            if (!string.Equals(entry.Role, "Assistant", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            scanned++;
            if (scanned > MaxExecutionContractHistoryScan) {
                break;
            }

            if (!TryParseExecutionContractBlocker(entry.Text, out var candidateReason, out var candidateAction)) {
                continue;
            }

            var normalizedCandidateAction = (candidateAction ?? string.Empty).Trim();
            var normalizedCandidateReason = (candidateReason ?? string.Empty).Trim();
            if (!skippedCurrentBlocker && normalizedCurrentAssistantText.Length > 0) {
                var normalizedEntryText = NormalizeExecutionContractBlockerText(entry.Text);
                if (normalizedEntryText.Length > 0
                    && string.Equals(normalizedEntryText, normalizedCurrentAssistantText, StringComparison.OrdinalIgnoreCase)) {
                    skippedCurrentBlocker = true;
                    continue;
                }
            }

            if (normalizedActionHint.Length > 0) {
                if (!string.Equals(normalizedActionHint, normalizedCandidateAction, StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }
            } else if (normalizedReasonHint.Length > 0 && normalizedCandidateAction.Length == 0) {
                if (!string.Equals(normalizedReasonHint, normalizedCandidateReason, StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }
            }

            previousReasonCode = normalizedCandidateReason;
            previousActionId = normalizedCandidateAction;
            return true;
        }

        return false;
    }

    private static string NormalizeExecutionContractBlockerText(string? text) {
        return (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
    }

    private static bool TryParseExecutionContractBlocker(string text, out string reasonCode, out string actionId) {
        reasonCode = string.Empty;
        actionId = string.Empty;
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0 || normalized.IndexOf(ExecutionContractMarker, StringComparison.OrdinalIgnoreCase) < 0) {
            return false;
        }

        var lines = normalized.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < lines.Length; i++) {
            var line = lines[i].Trim();
            if (line.StartsWith("Reason code:", StringComparison.OrdinalIgnoreCase)) {
                reasonCode = line["Reason code:".Length..].Trim();
                continue;
            }

            if (line.StartsWith("id:", StringComparison.OrdinalIgnoreCase)) {
                actionId = line["id:".Length..].Trim();
            }
        }

        return true;
    }

    private static bool StartsWithOutcomeMarker(string text) {
        return text.StartsWith("[error]", StringComparison.OrdinalIgnoreCase)
               || text.StartsWith("[warning]", StringComparison.OrdinalIgnoreCase)
               || text.StartsWith("[limit]", StringComparison.OrdinalIgnoreCase)
               || text.StartsWith("[canceled]", StringComparison.OrdinalIgnoreCase);
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

        _isAuthenticated = false;
        _authenticatedAccountId = null;
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
            return AssistantTurnOutcome.UsageLimit(ex.Message);
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
