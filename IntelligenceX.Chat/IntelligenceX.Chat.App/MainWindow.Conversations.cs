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
using IntelligenceX.Chat.App.Markdown;
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
    private const int KickoffTurnTimeoutSeconds = 25;
    private const int KickoffToolTimeoutSeconds = 20;
    private const int KickoffHeartbeatSeconds = 4;

    private bool LoadConversationsFromState(ChatAppState state) {
        var repaired = false;
        _conversations.Clear();
        lock (_turnDiagnosticsSync) {
            _assistantTurnVisualStateByConversationId.Clear();
            _activeTurnAssistantConversationId = null;
            _activeTurnAssistantMessageIndex = -1;
            _activeTurnAssistantPendingTimeline.Clear();
            _activeTurnAssistantProvisional = false;
            _activeTurnUsesProvisionalEvents = false;
            _activeTurnInterimResultSeen = false;
            _activeTurnInterimFingerprint = null;
        }
        if (state.Conversations is { Count: > 0 }) {
            foreach (var stored in state.Conversations) {
                if (string.IsNullOrWhiteSpace(stored.Id)) {
                    continue;
                }

                var conversation = new ConversationRuntime {
                    Id = stored.Id.Trim(),
                    Title = string.IsNullOrWhiteSpace(stored.Title) ? DefaultConversationTitle : stored.Title.Trim(),
                    ThreadId = string.IsNullOrWhiteSpace(stored.ThreadId) ? null : stored.ThreadId.Trim(),
                    RuntimeLabel = string.IsNullOrWhiteSpace(stored.RuntimeLabel) ? null : stored.RuntimeLabel.Trim(),
                    ModelLabel = string.IsNullOrWhiteSpace(stored.ModelLabel) ? null : stored.ModelLabel.Trim(),
                    ModelOverride = string.IsNullOrWhiteSpace(stored.ModelOverride) ? null : stored.ModelOverride.Trim(),
                    PendingAssistantQuestionHint = string.IsNullOrWhiteSpace(stored.PendingAssistantQuestionHint) ? null : stored.PendingAssistantQuestionHint.Trim(),
                    UpdatedUtc = EnsureUtc(stored.UpdatedUtc)
                };
                if (IsSystemConversation(conversation)) {
                    conversation.Title = SystemConversationTitle;
                    conversation.ModelOverride = null;
                }

                if (stored.Messages is { Count: > 0 }) {
                    foreach (var message in stored.Messages) {
                        if (string.IsNullOrWhiteSpace(message.Text)) {
                            continue;
                        }

                        var repairedText = TranscriptMarkdownPreparation.NormalizePersistedTranscriptText(message.Role, message.Text, out var messageWasRepaired);
                        if (messageWasRepaired) {
                            message.Text = repairedText;
                            repaired = true;
                        }

                        var local = EnsureUtc(message.TimeUtc).ToLocalTime();
                        var messageModel = string.IsNullOrWhiteSpace(message.Model) ? null : message.Model.Trim();
                        conversation.Messages.Add((message.Role ?? "System", repairedText, local, messageModel));
                    }
                }

                if (stored.PendingActions is { Count: > 0 }) {
                    var restoredPendingActions = new List<AssistantPendingAction>(stored.PendingActions.Count);
                    for (var i = 0; i < stored.PendingActions.Count; i++) {
                        var pendingAction = stored.PendingActions[i];
                        var id = (pendingAction.Id ?? string.Empty).Trim();
                        var reply = (pendingAction.Reply ?? string.Empty).Trim();
                        if (id.Length == 0 || reply.Length == 0) {
                            continue;
                        }

                        restoredPendingActions.Add(new AssistantPendingAction(
                            id,
                            (pendingAction.Title ?? string.Empty).Trim(),
                            (pendingAction.Request ?? string.Empty).Trim(),
                            reply));
                    }

                    conversation.PendingActions = restoredPendingActions;
                }

                if (conversation.UpdatedUtc == default && conversation.Messages.Count > 0) {
                    conversation.UpdatedUtc = conversation.Messages[^1].Time.ToUniversalTime();
                }

                if (conversation.Messages.Count > MaxMessagesPerConversation) {
                    conversation.Messages.RemoveRange(0, conversation.Messages.Count - MaxMessagesPerConversation);
                }

                conversation.Title = ComputeConversationTitle(conversation.Title, conversation.Messages);
                if (IsSystemConversation(conversation)) {
                    conversation.Title = SystemConversationTitle;
                    conversation.ModelOverride = null;
                }

                _conversations.Add(conversation);
            }
        } else {
            var legacy = new ConversationRuntime {
                Id = BuildConversationId(),
                Title = DefaultConversationTitle,
                ModelOverride = null,
                ThreadId = string.IsNullOrWhiteSpace(state.ThreadId) ? null : state.ThreadId
            };
            if (state.Messages is { Count: > 0 }) {
                foreach (var message in state.Messages) {
                    if (string.IsNullOrWhiteSpace(message.Text)) {
                        continue;
                    }

                    var repairedText = TranscriptMarkdownPreparation.NormalizePersistedTranscriptText(message.Role, message.Text, out var messageWasRepaired);
                    if (messageWasRepaired) {
                        message.Text = repairedText;
                        repaired = true;
                    }

                    var local = EnsureUtc(message.TimeUtc).ToLocalTime();
                    var messageModel = string.IsNullOrWhiteSpace(message.Model) ? null : message.Model.Trim();
                    legacy.Messages.Add((message.Role ?? "System", repairedText, local, messageModel));
                }
            }
            legacy.Title = ComputeConversationTitle(legacy.Title, legacy.Messages);
            legacy.UpdatedUtc = legacy.Messages.Count > 0
                ? legacy.Messages[^1].Time.ToUniversalTime()
                : DateTime.UtcNow;
            _conversations.Add(legacy);
        }

        EnsureSystemConversation();
        if (_conversations.Count == 0) {
            _conversations.Add(CreateConversationRuntime(DefaultConversationTitle));
            EnsureSystemConversation();
        }

        _conversations.Sort(CompareConversationsForDisplay);
        TrimConversationsToLimit();

        return repaired;
    }

    private string ResolveInitialConversationId(ChatAppState state) {
        var requested = (state.ActiveConversationId ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(requested) && FindConversationById(requested) is not null) {
            return requested;
        }

        for (var i = 0; i < _conversations.Count; i++) {
            var conversation = _conversations[i];
            if (!IsSystemConversation(conversation)) {
                return conversation.Id;
            }
        }

        return EnsureSystemConversation().Id;
    }

    private ConversationRuntime CreateConversationRuntime(string? title = null) {
        return new ConversationRuntime {
            Id = BuildConversationId(),
            Title = string.IsNullOrWhiteSpace(title) ? DefaultConversationTitle : title.Trim(),
            ModelOverride = null,
            ThreadId = null,
            UpdatedUtc = DateTime.UtcNow
        };
    }

    private static string BuildConversationId() {
        return "chat-" + Guid.NewGuid().ToString("N");
    }

    private static bool IsSystemConversationId(string? conversationId) {
        return string.Equals(
            (conversationId ?? string.Empty).Trim(),
            SystemConversationId,
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSystemConversation(ConversationRuntime? conversation) {
        return conversation is not null && IsSystemConversationId(conversation.Id);
    }

    private static int CompareConversationsForDisplay(ConversationRuntime? left, ConversationRuntime? right) {
        var leftIsSystem = IsSystemConversation(left);
        var rightIsSystem = IsSystemConversation(right);
        if (leftIsSystem != rightIsSystem) {
            return leftIsSystem ? 1 : -1;
        }

        if (left is null && right is null) {
            return 0;
        }

        if (left is null) {
            return 1;
        }

        if (right is null) {
            return -1;
        }

        return right.UpdatedUtc.CompareTo(left.UpdatedUtc);
    }

    private ConversationRuntime EnsureSystemConversation() {
        for (var i = 0; i < _conversations.Count; i++) {
            var existing = _conversations[i];
            if (!IsSystemConversation(existing)) {
                continue;
            }

            existing.Title = SystemConversationTitle;
            if (existing.UpdatedUtc == default) {
                existing.UpdatedUtc = DateTime.UtcNow;
            }
            return existing;
        }

        var conversation = new ConversationRuntime {
            Id = SystemConversationId,
            Title = SystemConversationTitle,
            ModelOverride = null,
            ThreadId = null,
            UpdatedUtc = DateTime.UtcNow
        };
        _conversations.Add(conversation);
        return conversation;
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
                EnsureSystemConversation();
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
        EnsureSystemConversation();
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
        _assistantStreamingState.Reset();
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
        if (ShouldRefreshAuthenticationStateAfterConversationSwitch(
                requiresInteractiveSignIn: RequiresInteractiveSignInForCurrentTransport(),
                isConnected: _isConnected,
                isAuthenticated: _isAuthenticated,
                loginInProgress: _loginInProgress,
                hasExplicitUnauthenticatedProbeSnapshot: HasExplicitUnauthenticatedEnsureLoginProbeSnapshot())) {
            _ = await RefreshAuthenticationStateAsync(updateStatus: true).ConfigureAwait(false);
        }
        await PublishOptionsStateAsync().ConfigureAwait(false);
        await PersistAppStateAsync().ConfigureAwait(false);
    }

    private async Task RenameConversationAsync(string conversationId, string title) {
        var conversation = FindConversationById(conversationId);
        if (conversation is null) {
            return;
        }
        if (IsSystemConversation(conversation)) {
            await SetStatusAsync("System conversation title is fixed.", SessionStatusTone.Warn).ConfigureAwait(false);
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
            InvalidatePublishedOptionsState();
            await PublishOptionsStateAsync().ConfigureAwait(false);
            return;
        }
        if (IsSystemConversation(conversation)) {
            await SetStatusAsync("System conversation cannot be deleted.", SessionStatusTone.Warn).ConfigureAwait(false);
            InvalidatePublishedOptionsState();
            await PublishOptionsStateAsync().ConfigureAwait(false);
            return;
        }

        if (IsTurnDispatchInProgress() && string.Equals(_activeRequestConversationId, conversation.Id, StringComparison.OrdinalIgnoreCase)) {
            await SetStatusAsync(SessionStatus.CannotDeleteActiveConversationDuringTurn()).ConfigureAwait(false);
            InvalidatePublishedOptionsState();
            await PublishOptionsStateAsync().ConfigureAwait(false);
            return;
        }

        var nonSystemConversationCount = 0;
        for (var i = 0; i < _conversations.Count; i++) {
            if (!IsSystemConversation(_conversations[i])) {
                nonSystemConversationCount++;
            }
        }

        if (nonSystemConversationCount <= 1) {
            var isActiveConversation = string.Equals(_activeConversationId, conversation.Id, StringComparison.OrdinalIgnoreCase);
            conversation.Messages.Clear();
            conversation.PendingActions = Array.Empty<AssistantPendingAction>();
            conversation.PendingAssistantQuestionHint = null;
            ClearConversationAssistantVisualState(conversation.Id);
            conversation.Title = DefaultConversationTitle;
            conversation.ThreadId = null;
            conversation.RuntimeLabel = null;
            conversation.ModelLabel = null;
            conversation.ModelOverride = null;
            conversation.UpdatedUtc = DateTime.UtcNow;
            if (string.Equals(_activeRequestConversationId, conversation.Id, StringComparison.OrdinalIgnoreCase)) {
                _activeRequestConversationId = null;
            }

            if (isActiveConversation) {
                _messages = conversation.Messages;
                _assistantStreamingState.Reset();
                _threadId = null;
                ClearToolRoutingInsights();
                _modelKickoffAttempted = false;
                _modelKickoffInProgress = false;
                _pendingLoginPrompt = null;
                await RenderTranscriptAsync().ConfigureAwait(false);
            }

            await PublishOptionsStateAsync().ConfigureAwait(false);
            await PersistAppStateAsync().ConfigureAwait(false);
            return;
        }

        _conversations.Remove(conversation);
        ClearConversationAssistantVisualState(conversation.Id);
        if (string.Equals(_activeConversationId, conversation.Id, StringComparison.OrdinalIgnoreCase)) {
            _conversations.Sort(CompareConversationsForDisplay);
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

    private async Task SetConversationModelAsync(string conversationId, string? model) {
        var conversation = FindConversationById(conversationId);
        if (conversation is null) {
            return;
        }

        if (IsSystemConversation(conversation)) {
            await SetStatusAsync("System conversation cannot override model.", SessionStatusTone.Warn).ConfigureAwait(false);
            return;
        }

        var normalized = (model ?? string.Empty).Trim();
        if (string.Equals(normalized, "(auto)", StringComparison.OrdinalIgnoreCase)) {
            normalized = string.Empty;
        }

        conversation.ModelOverride = normalized.Length == 0 ? null : normalized;

        var transport = NormalizeLocalProviderTransport(_localProviderTransport);
        var baseUrl = (_localProviderBaseUrl ?? string.Empty).Trim();
        var preset = DetectCompatibleProviderPreset(baseUrl);
        var copilotConnected = string.Equals(transport, TransportCompatibleHttp, StringComparison.OrdinalIgnoreCase)
                               && baseUrl.Contains("api.githubcopilot.com", StringComparison.OrdinalIgnoreCase);
        conversation.RuntimeLabel = ResolveRuntimeProviderLabelForState(transport, preset, copilotConnected, baseUrl);

        var configuredModel = conversation.ModelOverride ?? _localProviderModel;
        var resolvedModel = ResolveChatRequestModelOverride(
            _localProviderTransport,
            _localProviderBaseUrl,
            configuredModel,
            _availableModels);
        conversation.ModelLabel = string.IsNullOrWhiteSpace(resolvedModel) ? "(auto)" : resolvedModel.Trim();
        conversation.UpdatedUtc = DateTime.UtcNow;

        await SetStatusAsync(
                conversation.ModelOverride is null
                    ? "Conversation model override cleared."
                    : "Conversation model override set to '" + conversation.ModelOverride + "'.")
            .ConfigureAwait(false);
        await PublishOptionsStateAsync().ConfigureAwait(false);
        await PersistAppStateAsync().ConfigureAwait(false);
    }

    private void TrimConversationsToLimit() {
        _conversations.Sort(CompareConversationsForDisplay);
        EnsureSystemConversation();

        var userConversationLimit = MaxConversations - 1;
        if (userConversationLimit < 1) {
            userConversationLimit = 1;
        }

        var userConversations = new List<ConversationRuntime>(_conversations.Count);
        for (var i = 0; i < _conversations.Count; i++) {
            var conversation = _conversations[i];
            if (!IsSystemConversation(conversation)) {
                userConversations.Add(conversation);
            }
        }

        if (userConversations.Count <= userConversationLimit) {
            return;
        }

        userConversations.Sort(CompareConversationsForDisplay);
        ConversationRuntime? activeConversation = null;
        if (!string.IsNullOrWhiteSpace(_activeConversationId)) {
            var resolvedActive = FindConversationById(_activeConversationId);
            if (resolvedActive is not null && !IsSystemConversation(resolvedActive)) {
                activeConversation = resolvedActive;
            }
        }

        ConversationRuntime? activeRequestConversation = null;
        if (!string.IsNullOrWhiteSpace(_activeRequestConversationId)) {
            var resolvedRequest = FindConversationById(_activeRequestConversationId);
            if (resolvedRequest is not null && !IsSystemConversation(resolvedRequest)) {
                activeRequestConversation = resolvedRequest;
            }
        }

        var protectedConversations = new HashSet<ConversationRuntime>();
        if (activeConversation is not null) {
            protectedConversations.Add(activeConversation);
        }
        if (activeRequestConversation is not null) {
            protectedConversations.Add(activeRequestConversation);
        }

        while (userConversations.Count > userConversationLimit) {
            var removalIndex = userConversations.Count - 1;
            while (removalIndex >= 0 && protectedConversations.Contains(userConversations[removalIndex])) {
                removalIndex--;
            }

            if (removalIndex < 0) {
                // Keep cap enforcement deterministic even if all remaining entries are currently protected.
                if (activeRequestConversation is not null && !ReferenceEquals(activeRequestConversation, activeConversation)) {
                    removalIndex = userConversations.FindLastIndex(item => ReferenceEquals(item, activeRequestConversation));
                } else {
                    removalIndex = userConversations.FindLastIndex(item => !ReferenceEquals(item, activeConversation));
                }
                if (removalIndex < 0) {
                    removalIndex = userConversations.Count - 1;
                }
            }

            var toRemove = userConversations[removalIndex];
            userConversations.RemoveAt(removalIndex);
            _conversations.Remove(toRemove);
            ClearConversationAssistantVisualState(toRemove.Id);
            protectedConversations.Remove(toRemove);

            if (ReferenceEquals(toRemove, activeRequestConversation)) {
                _activeRequestConversationId = null;
                activeRequestConversation = null;
            }

            if (ReferenceEquals(toRemove, activeConversation)) {
                _activeConversationId = string.Empty;
                activeConversation = null;
            }
        }

        if (activeConversation is null && userConversations.Count > 0) {
            _activeConversationId = userConversations[0].Id;
        }

        _conversations.Sort(CompareConversationsForDisplay);
    }

    private static string ComputeConversationTitle(string currentTitle, List<(string Role, string Text, DateTime Time, string? Model)> messages) {
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

    private static string? NormalizeConversationModelOverride(string? value) {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length == 0 ? null : normalized;
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
                    BuildRuntimePackToggleLists(out var enablePackIds, out var disablePackIds);
                    var liveSynced = await TryApplyRuntimeSettingsLiveAsync(
                            profileSaved: true,
                            model: _localProviderModel,
                            openAITransport: _localProviderTransport,
                            openAIBaseUrl: _localProviderBaseUrl,
                            openAIAuthMode: _localProviderOpenAIAuthMode,
                            openAIApiKey: null,
                            openAIBasicUsername: _localProviderOpenAIBasicUsername,
                            openAIBasicPassword: null,
                            openAIAccountId: _localProviderOpenAIAccountId,
                            clearOpenAIBasicAuth: false,
                            clearOpenAIApiKey: false,
                            openAIStreaming: true,
                            openAIAllowInsecureHttp: ShouldAllowInsecureHttp(_localProviderTransport, _localProviderBaseUrl),
                            reasoningEffort: _localProviderReasoningEffort,
                            reasoningSummary: _localProviderReasoningSummary,
                            textVerbosity: _localProviderTextVerbosity,
                            temperature: _localProviderTemperature,
                            enablePackIds: enablePackIds,
                            disablePackIds: disablePackIds).ConfigureAwait(false);
                    if (liveSynced) {
                        await SyncConnectedServiceProfileAndModelsAsync(
                            forceModelRefresh: false,
                            setProfileNewThread: false,
                            appendWarnings: false).ConfigureAwait(false);
                    } else if (VerboseServiceLogs || _debugMode) {
                        AppendSystem("Profile switch runtime sync failed: profile wasn't found and live apply didn't complete.");
                    }
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

}
