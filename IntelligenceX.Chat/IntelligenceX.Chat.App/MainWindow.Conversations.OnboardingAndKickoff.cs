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
    private async Task EnsureOnboardingStartedAsync() {
        await _onboardingGate.WaitAsync().ConfigureAwait(false);
        try {
            var active = GetActiveConversation();
            var pruned = PruneDuplicateOnboardingPrompts();
            if (pruned) {
                await RenderTranscriptAsync().ConfigureAwait(false);
                await PersistAppStateAsync().ConfigureAwait(false);
            }

            if (active.Messages.Count > 0) {
                return;
            }

            if (_appState.OnboardingCompleted) {
                return;
            }

            if (_modelKickoffAttempted) {
                return;
            }

            await MaybeStartModelKickoffAsync().ConfigureAwait(false);
        } finally {
            _onboardingGate.Release();
        }
    }

    private bool PruneDuplicateOnboardingPrompts() {
        var changed = false;
        for (var i = 0; i < _conversations.Count; i++) {
            var conversation = _conversations[i];
            if (conversation.Messages.Count == 0) {
                continue;
            }

            var conversationChanged = OnboardingPromptRules.PruneDuplicateAskNamePrompts(conversation.Messages);
            if (OnboardingPromptRules.PruneDuplicateAssistantLeadPrompts(conversation.Messages)) {
                conversationChanged = true;
            }

            if (!conversationChanged) {
                continue;
            }

            conversation.UpdatedUtc = DateTime.UtcNow;
            conversation.Title = ComputeConversationTitle(conversation.Title, conversation.Messages);
            changed = true;
        }

        return changed;
    }

    private async Task MaybeStartModelKickoffAsync() {
        if (_modelKickoffAttempted || _modelKickoffInProgress) {
            return;
        }

        if (_appState.OnboardingCompleted) {
            return;
        }

        // Local/compatible runtimes should stay focused on explicit user turns and avoid background kickoff work.
        if (!RequiresInteractiveSignInForCurrentTransport()) {
            return;
        }

        var conversation = GetActiveConversation();
        if (conversation.Messages.Count > 0 || !IsEffectivelyAuthenticatedForCurrentTransport()) {
            return;
        }

        var missingFields = BuildMissingOnboardingFields();
        if (missingFields.Count == 0) {
            _appState.OnboardingCompleted = true;
            await PublishOptionsStateAsync().ConfigureAwait(false);
            await PersistAppStateAsync().ConfigureAwait(false);
            return;
        }

        var client = _client;
        if (client is null) {
            return;
        }

        _modelKickoffAttempted = true;
        _modelKickoffInProgress = true;
        _activeRequestConversationId = conversation.Id;
        try {
            var requestOptions = BuildKickoffChatRequestOptions(BuildChatRequestOptions(conversation));
            var kickoffModelLabel = string.IsNullOrWhiteSpace(requestOptions.Model) ? "(auto)" : requestOptions.Model!.Trim();
            conversation.ModelLabel = kickoffModelLabel;
            var request = new ChatRequest {
                RequestId = NextId(),
                ThreadId = conversation.ThreadId,
                Text = BuildKickoffRequestText(missingFields),
                Options = requestOptions
            };
            _activeKickoffRequestId = request.RequestId;

            var result = await client.RequestAsync<ChatResultMessage>(request, CancellationToken.None).ConfigureAwait(false);
            conversation.ThreadId = result.ThreadId;
            if (string.Equals(conversation.Id, _activeConversationId, StringComparison.OrdinalIgnoreCase)) {
                _threadId = result.ThreadId;
            }
            var normalizedAssistantTurn = await ApplyAssistantProfileUpdateAsync(result.Text).ConfigureAwait(false);
            conversation.PendingActions = normalizedAssistantTurn.PendingActions;
            conversation.PendingAssistantQuestionHint = normalizedAssistantTurn.PendingAssistantQuestionHint;
            await AddAssistantMessageAsync(conversation, normalizedAssistantTurn.VisibleText, kickoffModelLabel).ConfigureAwait(false);
            conversation.UpdatedUtc = DateTime.UtcNow;
            conversation.Title = ComputeConversationTitle(conversation.Title, conversation.Messages);
            await PersistAppStateAsync().ConfigureAwait(false);
        } catch (Exception ex) {
            if (VerboseServiceLogs || _debugMode) {
                AppendSystem(SystemNotice.ModelKickoffFailed(ex.Message));
            }

            if (IsUsageLimitError(ex)) {
                MarkUsageLimitForActiveAccount(ex.Message);
                await SetStatusAsync(SessionStatus.UsageLimitReached()).ConfigureAwait(false);
                _modelKickoffAttempted = true;
            } else {
                _modelKickoffAttempted = false;
            }
        } finally {
            _modelKickoffInProgress = false;
            _activeKickoffRequestId = null;
            _activeRequestConversationId = null;
            await SetActivityAsync(null).ConfigureAwait(false);
        }
    }

    internal static ChatRequestOptions BuildKickoffChatRequestOptions(ChatRequestOptions? options) {
        var baseOptions = options ?? new ChatRequestOptions();
        return baseOptions with {
            // Keep onboarding kickoff short and non-blocking so user turns always take priority.
            MaxToolRounds = 1,
            ParallelTools = false,
            PlanExecuteReviewLoop = false,
            MaxReviewPasses = 0,
            TurnTimeoutSeconds = KickoffTurnTimeoutSeconds,
            ToolTimeoutSeconds = KickoffToolTimeoutSeconds,
            ModelHeartbeatSeconds = KickoffHeartbeatSeconds
        };
    }

    private async Task CancelModelKickoffIfRunningAsync() {
        if (!_modelKickoffInProgress) {
            return;
        }

        var kickoffRequestId = (_activeKickoffRequestId ?? string.Empty).Trim();
        _modelKickoffInProgress = false;
        _activeKickoffRequestId = null;
        _activeRequestConversationId = null;

        var client = _client;
        if (kickoffRequestId.Length == 0 || client is null) {
            return;
        }

        try {
            using var cts = new CancellationTokenSource(KickoffCancelAckTimeout);
            var cancelRequest = new CancelChatRequest {
                RequestId = NextId(),
                ChatRequestId = kickoffRequestId
            };
            _ = await client.RequestAsync<AckMessage>(cancelRequest, cts.Token).ConfigureAwait(false);
        } catch (Exception ex) {
            if (VerboseServiceLogs || _debugMode) {
                AppendSystem("Kickoff cancel best-effort: " + ex.Message);
            }
        }
    }

}
