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
                        if (IsUsageLimitError(retryEx)) {
                            await SetStatusAsync(SessionStatus.UsageLimitReached()).ConfigureAwait(false);
                        }
                        await ApplyTurnFailureAsync(turn, ResolveTurnOutcome(turn.RequestId, retryEx, disconnectedFallback: false)).ConfigureAwait(false);
                        return;
                    }
                }

                await ApplyTurnFailureAsync(turn, ResolveTurnOutcome(turn.RequestId, ex, disconnectedFallback: true)).ConfigureAwait(false);
                return;
            } catch (Exception ex) {
                if (IsUsageLimitError(ex)) {
                    await SetStatusAsync(SessionStatus.UsageLimitReached()).ConfigureAwait(false);
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
        ReplaceLastAssistantText(turn.Conversation, AssistantTurnOutcomeFormatter.Format(outcome));
        turn.Conversation.UpdatedUtc = DateTime.UtcNow;
        if (string.Equals(turn.Conversation.Id, _activeConversationId, StringComparison.OrdinalIgnoreCase)) {
            await RenderTranscriptAsync().ConfigureAwait(false);
        }

        await PersistAppStateAsync().ConfigureAwait(false);
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
