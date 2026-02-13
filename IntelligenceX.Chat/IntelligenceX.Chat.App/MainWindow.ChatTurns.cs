using System;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.App.Conversation;
using IntelligenceX.Chat.Client;
using Microsoft.UI.Xaml;

namespace IntelligenceX.Chat.App;

public sealed partial class MainWindow : Window {
    private sealed record ChatTurnContext(
        ConversationRuntime Conversation,
        string ConversationId,
        string RequestId,
        string RequestText);

    private async Task<ChatTurnContext?> PrepareChatTurnAsync(string text) {
        if (!_isAuthenticated) {
            var authenticatedNow = await RefreshAuthenticationStateAsync(updateStatus: true).ConfigureAwait(false);
            if (authenticatedNow) {
                _isAuthenticated = true;
            }
        }

        var conversation = GetActiveConversation();
        var conversationId = conversation.Id;

        if (!_isAuthenticated) {
            _queuedPromptAfterLogin = text;
            _queuedPromptAfterLoginConversationId = conversationId;
            var loginStarted = await StartLoginFlowIfNeededAsync().ConfigureAwait(false);
            await SetStatusAsync(loginStarted ? SessionStatus.WaitingForSignIn() : SessionStatus.SignInRequired()).ConfigureAwait(false);
            if (!loginStarted) {
                AppendSystem(SystemNotice.SignInRequiredBeforeSendingMessages());
            }

            return null;
        }

        await ApplyUserProfileIntentAsync(text).ConfigureAwait(false);

        _assistantStreaming.Clear();
        _activeTurnReceivedDelta = false;
        var now = DateTime.Now;
        conversation.Messages.Add(("User", text, now));
        conversation.Messages.Add(("Assistant", string.Empty, now));
        conversation.Title = ComputeConversationTitle(conversation.Title, conversation.Messages);
        conversation.UpdatedUtc = now.ToUniversalTime();
        if (string.Equals(conversationId, _activeConversationId, StringComparison.OrdinalIgnoreCase)) {
            await RenderTranscriptAsync().ConfigureAwait(false);
        }

        await PersistAppStateAsync().ConfigureAwait(false);
        return new ChatTurnContext(
            conversation,
            conversationId,
            NextId(),
            BuildRequestTextForService(text));
    }

    private async Task ExecuteChatTurnWithReconnectAsync(ChatTurnContext turn) {
        try {
            var initialClient = _client;
            if (initialClient is null) {
                await ApplyTurnFailureAsync(turn, AssistantTurnOutcome.Disconnected()).ConfigureAwait(false);
                return;
            }

            try {
                await ExecuteChatTurnAsync(initialClient, turn).ConfigureAwait(false);
                return;
            } catch (Exception ex) when (IsDisconnectedError(ex)) {
                await DisposeClientAsync().ConfigureAwait(false);
                if (await EnsureConnectedAsync().ConfigureAwait(false) && _client is { } retryClient) {
                    try {
                        await ExecuteChatTurnAsync(retryClient, turn).ConfigureAwait(false);
                        return;
                    } catch (Exception retryEx) {
                        var promptQueued = false;
                        if (IsUsageLimitError(retryEx)) {
                            promptQueued = QueuePromptAfterSignIn(turn.RequestText, turn.ConversationId);
                            await SetStatusAsync(SessionStatus.UsageLimitReached()).ConfigureAwait(false);
                            if (promptQueued) {
                                AppendSystem(turn.Conversation, SystemNotice.PromptQueuedAfterUsageLimit());
                            }
                        }
                        await ApplyTurnFailureAsync(turn, ResolveTurnOutcome(turn.RequestId, retryEx, disconnectedFallback: false)).ConfigureAwait(false);
                        return;
                    }
                }

                await ApplyTurnFailureAsync(turn, ResolveTurnOutcome(turn.RequestId, ex, disconnectedFallback: true)).ConfigureAwait(false);
                return;
            } catch (Exception ex) {
                var promptQueued = false;
                if (IsUsageLimitError(ex)) {
                    promptQueued = QueuePromptAfterSignIn(turn.RequestText, turn.ConversationId);
                    await SetStatusAsync(SessionStatus.UsageLimitReached()).ConfigureAwait(false);
                    if (promptQueued) {
                        AppendSystem(turn.Conversation, SystemNotice.PromptQueuedAfterUsageLimit());
                    }
                }
                await ApplyTurnFailureAsync(turn, ResolveTurnOutcome(turn.RequestId, ex, disconnectedFallback: false)).ConfigureAwait(false);
                return;
            }
        } finally {
            await SetActivityAsync(null).ConfigureAwait(false);
        }
    }

    private async Task ExecuteChatTurnAsync(ChatServiceClient client, ChatTurnContext turn) {
        var req = new ChatRequest {
            RequestId = turn.RequestId,
            ThreadId = turn.Conversation.ThreadId,
            Text = turn.RequestText,
            Options = BuildChatRequestOptions()
        };

        var result = await client.RequestAsync<ChatResultMessage>(req, CancellationToken.None).ConfigureAwait(false);
        await ApplyChatResultAsync(turn.Conversation, result).ConfigureAwait(false);
    }

    private async Task ApplyChatResultAsync(ConversationRuntime conversation, ChatResultMessage result) {
        conversation.ThreadId = result.ThreadId;
        if (string.Equals(conversation.Id, _activeConversationId, StringComparison.OrdinalIgnoreCase)) {
            _threadId = result.ThreadId;
        }

        var assistantText = await ApplyAssistantProfileUpdateAsync(result.Text).ConfigureAwait(false);
        ReplaceLastAssistantText(conversation, assistantText);
        _activeTurnReceivedDelta = false;
        if (_debugMode && result.Tools is not null && (result.Tools.Calls.Count > 0 || result.Tools.Outputs.Count > 0)) {
            conversation.Messages.Add(("Tools", BuildToolRunMarkdown(result.Tools), DateTime.Now));
        }

        conversation.UpdatedUtc = DateTime.UtcNow;
        conversation.Title = ComputeConversationTitle(conversation.Title, conversation.Messages);
        if (string.Equals(conversation.Id, _activeConversationId, StringComparison.OrdinalIgnoreCase)) {
            await RenderTranscriptAsync().ConfigureAwait(false);
        }

        await PersistAppStateAsync().ConfigureAwait(false);
    }

    private async Task ApplyTurnFailureAsync(ChatTurnContext turn, AssistantTurnOutcome outcome) {
        if (TryGetPartialTurnFailureNotice(turn.Conversation, outcome, out var notice)) {
            turn.Conversation.Messages.Add(("System", notice, DateTime.Now));
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
                + "Say \"continue\" to keep going, or narrow scope (one DC / one OU).",
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

    private static bool StartsWithOutcomeMarker(string text) {
        return text.StartsWith("[error]", StringComparison.OrdinalIgnoreCase)
               || text.StartsWith("[warning]", StringComparison.OrdinalIgnoreCase)
               || text.StartsWith("[limit]", StringComparison.OrdinalIgnoreCase)
               || text.StartsWith("[canceled]", StringComparison.OrdinalIgnoreCase);
    }

    private bool QueuePromptAfterSignIn(string requestText, string conversationId) {
        var text = (requestText ?? string.Empty).Trim();
        if (text.Length == 0) {
            return false;
        }

        _queuedPromptAfterLogin = text;
        _queuedPromptAfterLoginConversationId = (conversationId ?? string.Empty).Trim();
        return true;
    }

    private AssistantTurnOutcome ResolveTurnOutcome(string requestId, Exception ex, bool disconnectedFallback) {
        if (IsCanceledTurn(requestId, ex)) {
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
