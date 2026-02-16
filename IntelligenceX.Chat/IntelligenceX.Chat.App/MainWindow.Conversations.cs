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
    private bool LoadConversationsFromState(ChatAppState state) {
        var repaired = false;
        _conversations.Clear();
        if (state.Conversations is { Count: > 0 }) {
            foreach (var stored in state.Conversations) {
                if (string.IsNullOrWhiteSpace(stored.Id)) {
                    continue;
                }

                var conversation = new ConversationRuntime {
                    Id = stored.Id.Trim(),
                    Title = string.IsNullOrWhiteSpace(stored.Title) ? DefaultConversationTitle : stored.Title.Trim(),
                    ThreadId = string.IsNullOrWhiteSpace(stored.ThreadId) ? null : stored.ThreadId.Trim(),
                    UpdatedUtc = EnsureUtc(stored.UpdatedUtc)
                };

                if (stored.Messages is { Count: > 0 }) {
                    foreach (var message in stored.Messages) {
                        if (string.IsNullOrWhiteSpace(message.Text)) {
                            continue;
                        }

                        var repairedText = RepairLegacyTranscriptText(message.Text, out var messageWasRepaired);
                        if (messageWasRepaired) {
                            message.Text = repairedText;
                            repaired = true;
                        }

                        var local = EnsureUtc(message.TimeUtc).ToLocalTime();
                        conversation.Messages.Add((message.Role ?? "System", repairedText, local));
                    }
                }

                if (conversation.UpdatedUtc == default && conversation.Messages.Count > 0) {
                    conversation.UpdatedUtc = conversation.Messages[^1].Time.ToUniversalTime();
                }

                if (conversation.Messages.Count > MaxMessagesPerConversation) {
                    conversation.Messages.RemoveRange(0, conversation.Messages.Count - MaxMessagesPerConversation);
                }

                conversation.Title = ComputeConversationTitle(conversation.Title, conversation.Messages);

                _conversations.Add(conversation);
            }
        } else {
            var legacy = new ConversationRuntime {
                Id = BuildConversationId(),
                Title = DefaultConversationTitle,
                ThreadId = string.IsNullOrWhiteSpace(state.ThreadId) ? null : state.ThreadId
            };
            if (state.Messages is { Count: > 0 }) {
                foreach (var message in state.Messages) {
                    if (string.IsNullOrWhiteSpace(message.Text)) {
                        continue;
                    }

                    var repairedText = RepairLegacyTranscriptText(message.Text, out var messageWasRepaired);
                    if (messageWasRepaired) {
                        message.Text = repairedText;
                        repaired = true;
                    }

                    var local = EnsureUtc(message.TimeUtc).ToLocalTime();
                    legacy.Messages.Add((message.Role ?? "System", repairedText, local));
                }
            }
            legacy.Title = ComputeConversationTitle(legacy.Title, legacy.Messages);
            legacy.UpdatedUtc = legacy.Messages.Count > 0
                ? legacy.Messages[^1].Time.ToUniversalTime()
                : DateTime.UtcNow;
            _conversations.Add(legacy);
        }

        if (_conversations.Count == 0) {
            _conversations.Add(CreateConversationRuntime(DefaultConversationTitle));
        }

        _conversations.Sort(static (a, b) => b.UpdatedUtc.CompareTo(a.UpdatedUtc));
        if (_conversations.Count > MaxConversations) {
            _conversations.RemoveRange(MaxConversations, _conversations.Count - MaxConversations);
        }

        return repaired;
    }

    private static string RepairLegacyTranscriptText(string text, out bool repaired) {
        if (!TranscriptMarkdownNormalizer.TryRepairLegacyTranscript(text, out var normalized)) {
            repaired = false;
            return text;
        }

        repaired = true;
        return normalized;
    }

    private string ResolveInitialConversationId(ChatAppState state) {
        var requested = (state.ActiveConversationId ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(requested) && FindConversationById(requested) is not null) {
            return requested;
        }

        return _conversations[0].Id;
    }

    private ConversationRuntime CreateConversationRuntime(string? title = null) {
        return new ConversationRuntime {
            Id = BuildConversationId(),
            Title = string.IsNullOrWhiteSpace(title) ? DefaultConversationTitle : title.Trim(),
            ThreadId = null,
            UpdatedUtc = DateTime.UtcNow
        };
    }

    private static string BuildConversationId() {
        return "chat-" + Guid.NewGuid().ToString("N");
    }

    private ConversationRuntime? FindConversationById(string? conversationId) {
        if (string.IsNullOrWhiteSpace(conversationId)) {
            return null;
        }

        var id = conversationId.Trim();
        for (var i = 0; i < _conversations.Count; i++) {
            if (string.Equals(_conversations[i].Id, id, StringComparison.OrdinalIgnoreCase)) {
                return _conversations[i];
            }
        }

        return null;
    }

    private void ActivateConversation(string conversationId) {
        var conversation = FindConversationById(conversationId);
        if (conversation is null) {
            conversation = _conversations.Count > 0 ? _conversations[0] : CreateConversationRuntime(DefaultConversationTitle);
            if (_conversations.Count == 0) {
                _conversations.Add(conversation);
            }
        }

        _activeConversationId = conversation.Id;
        _messages = conversation.Messages;
        _threadId = conversation.ThreadId;
    }

    private ConversationRuntime GetActiveConversation() {
        var conversation = FindConversationById(_activeConversationId);
        if (conversation is not null) {
            return conversation;
        }

        var created = CreateConversationRuntime(DefaultConversationTitle);
        _conversations.Add(created);
        ActivateConversation(created.Id);
        return created;
    }

    private ConversationRuntime ResolveRequestConversation() {
        if (!string.IsNullOrWhiteSpace(_activeRequestConversationId)) {
            var target = FindConversationById(_activeRequestConversationId);
            if (target is not null) {
                return target;
            }
        }

        return GetActiveConversation();
    }

    private async Task NewConversationAsync() {
        var conversation = CreateConversationRuntime(DefaultConversationTitle);
        _conversations.Add(conversation);
        TrimConversationsToLimit();
        ActivateConversation(conversation.Id);
        _assistantStreaming.Clear();
        _activeRequestConversationId = null;
        _modelKickoffAttempted = false;
        _modelKickoffInProgress = false;
        _autoSignInAttempted = _appState.OnboardingCompleted || AnyConversationHasMessages();
        await RenderTranscriptAsync().ConfigureAwait(false);
        await PublishOptionsStateAsync().ConfigureAwait(false);
        await PersistAppStateAsync().ConfigureAwait(false);
    }

    private async Task SwitchConversationAsync(string conversationId) {
        var target = FindConversationById(conversationId);
        if (target is null) {
            return;
        }

        ActivateConversation(target.Id);
        _modelKickoffAttempted = _messages.Count > 0;
        _autoSignInAttempted = _appState.OnboardingCompleted || AnyConversationHasMessages();
        await RenderTranscriptAsync().ConfigureAwait(false);
        await PublishOptionsStateAsync().ConfigureAwait(false);
        await PersistAppStateAsync().ConfigureAwait(false);
    }

    private async Task RenameConversationAsync(string conversationId, string title) {
        var conversation = FindConversationById(conversationId);
        if (conversation is null) {
            return;
        }

        var normalized = BuildConversationTitleFromText(title);
        if (string.IsNullOrWhiteSpace(normalized)) {
            normalized = DefaultConversationTitle;
        }

        conversation.Title = normalized;
        conversation.UpdatedUtc = DateTime.UtcNow;
        await PublishOptionsStateAsync().ConfigureAwait(false);
        await PersistAppStateAsync().ConfigureAwait(false);
    }

    private async Task DeleteConversationAsync(string conversationId) {
        var conversation = FindConversationById(conversationId);
        if (conversation is null) {
            return;
        }

        if (_isSending && string.Equals(_activeRequestConversationId, conversation.Id, StringComparison.OrdinalIgnoreCase)) {
            await SetStatusAsync(SessionStatus.CannotDeleteActiveConversationDuringTurn()).ConfigureAwait(false);
            return;
        }

        if (_conversations.Count <= 1) {
            ClearConversation();
            return;
        }

        _conversations.Remove(conversation);
        if (string.Equals(_activeConversationId, conversation.Id, StringComparison.OrdinalIgnoreCase)) {
            _conversations.Sort(static (a, b) => b.UpdatedUtc.CompareTo(a.UpdatedUtc));
            var next = _conversations[0];
            ActivateConversation(next.Id);
            await RenderTranscriptAsync().ConfigureAwait(false);
        }

        if (string.Equals(_activeRequestConversationId, conversation.Id, StringComparison.OrdinalIgnoreCase)) {
            _activeRequestConversationId = null;
        }

        await PublishOptionsStateAsync().ConfigureAwait(false);
        await PersistAppStateAsync().ConfigureAwait(false);
    }

    private void TrimConversationsToLimit() {
        if (_conversations.Count <= MaxConversations) {
            return;
        }

        _conversations.Sort(static (a, b) => b.UpdatedUtc.CompareTo(a.UpdatedUtc));
        while (_conversations.Count > MaxConversations) {
            var idx = _conversations.Count - 1;
            if (string.Equals(_conversations[idx].Id, _activeConversationId, StringComparison.OrdinalIgnoreCase) && idx > 0) {
                idx--;
            }
            if (!string.IsNullOrWhiteSpace(_activeRequestConversationId)
                && string.Equals(_conversations[idx].Id, _activeRequestConversationId, StringComparison.OrdinalIgnoreCase)
                && idx > 0) {
                idx--;
            }
            _conversations.RemoveAt(idx);
        }
    }

    private static string ComputeConversationTitle(string currentTitle, List<(string Role, string Text, DateTime Time)> messages) {
        if (!string.IsNullOrWhiteSpace(currentTitle) && !string.Equals(currentTitle, DefaultConversationTitle, StringComparison.OrdinalIgnoreCase)) {
            return currentTitle;
        }

        for (var i = 0; i < messages.Count; i++) {
            var message = messages[i];
            if (!string.Equals(message.Role, "User", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var title = BuildConversationTitleFromText(message.Text);
            if (!string.IsNullOrWhiteSpace(title)) {
                return title;
            }
        }

        return DefaultConversationTitle;
    }

    private static string BuildConversationTitleFromText(string? text) {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return DefaultConversationTitle;
        }

        normalized = normalized.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (normalized.Length > 56) {
            normalized = normalized[..56].TrimEnd() + "...";
        }

        return normalized;
    }

    private async Task SwitchProfileAsync(string profileName) {
        var normalized = ResolveAppProfileName(profileName);
        if (string.Equals(normalized, _appProfileName, StringComparison.OrdinalIgnoreCase)) {
            await PublishOptionsStateAsync().ConfigureAwait(false);
            return;
        }

        ClearPendingTurns();
        ClearQueuedPromptsAfterLogin();
        await PersistAppStateAsync().ConfigureAwait(false);
        await LoadProfileStateAsync(normalized, render: true).ConfigureAwait(false);
        ClearConversationThreadIds();
        await PersistAppStateAsync().ConfigureAwait(false);

        if (_client is not null) {
            try {
                var profileApplied = await SyncConnectedServiceProfileAndModelsAsync(
                    forceModelRefresh: false,
                    setProfileNewThread: true,
                    appendWarnings: false).ConfigureAwait(false);
                if (!profileApplied) {
                    await ReconnectServiceSessionAsync().ConfigureAwait(false);
                }
            } catch (Exception ex) {
                if (VerboseServiceLogs || _debugMode) {
                    AppendSystem("Profile switch runtime sync failed: " + ex.Message);
                }
            }
        }

        await EnsureFirstRunAuthenticatedAsync().ConfigureAwait(false);
        await EnsureOnboardingStartedAsync().ConfigureAwait(false);
        await PersistAppStateAsync().ConfigureAwait(false);
    }

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

        var conversation = GetActiveConversation();
        if (conversation.Messages.Count > 0 || !_isAuthenticated) {
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
            var request = new ChatRequest {
                RequestId = NextId(),
                ThreadId = conversation.ThreadId,
                Text = BuildKickoffRequestText(missingFields),
                Options = BuildChatRequestOptions()
            };
            _activeKickoffRequestId = request.RequestId;

            var result = await client.RequestAsync<ChatResultMessage>(request, CancellationToken.None).ConfigureAwait(false);
            conversation.ThreadId = result.ThreadId;
            if (string.Equals(conversation.Id, _activeConversationId, StringComparison.OrdinalIgnoreCase)) {
                _threadId = result.ThreadId;
            }
            var assistantText = await ApplyAssistantProfileUpdateAsync(result.Text).ConfigureAwait(false);
            await AddAssistantMessageAsync(conversation, assistantText).ConfigureAwait(false);
            conversation.UpdatedUtc = DateTime.UtcNow;
            conversation.Title = ComputeConversationTitle(conversation.Title, conversation.Messages);
            await PersistAppStateAsync().ConfigureAwait(false);
        } catch (Exception ex) {
            if (VerboseServiceLogs || _debugMode) {
                AppendSystem(SystemNotice.ModelKickoffFailed(ex.Message));
            }

            if (IsUsageLimitError(ex)) {
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

}
