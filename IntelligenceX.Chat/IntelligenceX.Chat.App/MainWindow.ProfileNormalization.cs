using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.App.Conversation;
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
    private sealed record NormalizedAssistantTurn(string VisibleText, IReadOnlyList<AssistantPendingAction> PendingActions, string? PendingAssistantQuestionHint);

    private async Task<NormalizedAssistantTurn> ApplyAssistantProfileUpdateAsync(string? assistantText) {
        var protocolResult = DesktopChatTurnProtocol.NormalizeAssistantResponse(assistantText);
        var profileChanged = false;
        if (protocolResult.ProfileUpdate is not null) {
            profileChanged = await ApplyProfileUpdateAsync(
                    protocolResult.ProfileUpdate,
                    autoCompleteOnboardingForProfileScope: false)
                .ConfigureAwait(false);
        }

        var memoryChanged = false;
        if (protocolResult.MemoryUpdate is not null) {
            memoryChanged = await ApplyMemoryUpdateAsync(protocolResult.MemoryUpdate).ConfigureAwait(false);
        }

        if (!string.IsNullOrWhiteSpace(protocolResult.VisibleText)) {
            return new NormalizedAssistantTurn(
                protocolResult.VisibleText,
                protocolResult.PendingActions,
                protocolResult.PendingAssistantQuestionHint);
        }

        return new NormalizedAssistantTurn(
            profileChanged || memoryChanged ? "Got it." : (assistantText ?? string.Empty).Trim(),
            protocolResult.PendingActions,
            protocolResult.PendingAssistantQuestionHint);
    }

    private static string? NormalizeAssistantPersonaValue(string? value) =>
        DesktopChatProfileNormalizer.NormalizeAssistantPersona(value);

    private static string? NormalizeUserNameValue(string? value) =>
        DesktopChatProfileNormalizer.NormalizeUserName(value);

    private async Task SaveProfileAsync(string userName, string persona, string theme) {
        var update = new OnboardingProfileUpdate {
            Scope = ProfileUpdateScope.Profile,
            HasUserName = true,
            UserName = userName,
            HasAssistantPersona = true,
            AssistantPersona = persona,
            HasThemePreset = !string.IsNullOrWhiteSpace(theme),
            ThemePreset = theme
        };

        _ = await ApplyProfileUpdateAsync(update, autoCompleteOnboardingForProfileScope: true).ConfigureAwait(false);
    }

    private async Task RestartOnboardingAsync() {
        _appState.OnboardingCompleted = false;
        _appState.UserName = null;
        _appState.AssistantPersona = null;
        _sessionUserNameOverride = null;
        _sessionAssistantPersonaOverride = null;
        _sessionThemeOverride = null;
        _themePreset = "default";
        _appState.ThemePreset = "default";
        _conversations.Clear();
        var conversation = CreateConversationRuntime(DefaultConversationTitle);
        _conversations.Add(conversation);
        ActivateConversation(conversation.Id);
        _threadId = null;
        _assistantStreamingState.Reset();
        _activeRequestConversationId = null;
        ClearPendingTurns();
        ClearQueuedPromptsAfterLogin();
        _modelKickoffAttempted = false;
        _modelKickoffInProgress = false;
        _autoSignInAttempted = IsEffectivelyAuthenticatedForCurrentTransport();
        await ApplyThemeFromStateAsync().ConfigureAwait(false);
        await RenderTranscriptAsync().ConfigureAwait(false);
        await SetStatusAsync(ResolveConnectionStatusForCurrentTransport()).ConfigureAwait(false);
        if (_isConnected && RequiresInteractiveSignInForCurrentTransport() && !_isAuthenticated) {
            _ = await RefreshAuthenticationStateAsync(updateStatus: true).ConfigureAwait(false);
        }
        await PublishOptionsStateAsync().ConfigureAwait(false);
        await PersistAppStateAsync().ConfigureAwait(false);
        await EnsureFirstRunAuthenticatedAsync().ConfigureAwait(false);
        await EnsureOnboardingStartedAsync().ConfigureAwait(false);
    }

    private async Task AddAssistantMessageAsync(string text) {
        await AddAssistantMessageAsync(GetActiveConversation(), text, null).ConfigureAwait(false);
    }

    private async Task AddAssistantMessageAsync(ConversationRuntime conversation, string text, string? modelLabel) {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return;
        }

        if (conversation.Messages.Count > 0) {
            var last = conversation.Messages[^1];
            if (string.Equals(last.Role, "Assistant", StringComparison.OrdinalIgnoreCase)
                && ShouldSuppressConsecutiveAssistantDuplicate(
                    candidateAssistantText: normalized,
                    previousAssistantText: last.Text)) {
                return;
            }
        }

        var hasUserMessages = OnboardingPromptRules.HasAnyUserMessage(conversation.Messages);
        if (!hasUserMessages && OnboardingPromptRules.HasEquivalentAssistantMessage(conversation.Messages, normalized)) {
            return;
        }

        if (OnboardingPromptRules.IsLikelyOnboardingIntroPromptText(normalized)) {
            if (!hasUserMessages && OnboardingPromptRules.HasEquivalentOnboardingIntroPrompt(conversation.Messages, normalized)) {
                return;
            }
        }

        var normalizedModelLabel = string.IsNullOrWhiteSpace(modelLabel) ? null : modelLabel.Trim();
        conversation.Messages.Add(("Assistant", normalized, DateTime.Now, normalizedModelLabel));
        conversation.UpdatedUtc = DateTime.UtcNow;
        conversation.Title = ComputeConversationTitle(conversation.Title, conversation.Messages);
        if (string.Equals(conversation.Id, _activeConversationId, StringComparison.OrdinalIgnoreCase)) {
            await RenderTranscriptAsync().ConfigureAwait(false);
        }
    }

    private async Task ApplyThemeFromStateAsync() {
        await SetThemeAsync(GetEffectiveThemePreset()).ConfigureAwait(false);
    }

}
