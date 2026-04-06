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
        var normalized = (assistantText ?? string.Empty).Trim();
        var cleanedText = normalized;
        IReadOnlyList<AssistantPendingAction> pendingActions = Array.Empty<AssistantPendingAction>();
        var profileChanged = false;
        if (OnboardingModelProtocol.TryExtractLastProfileUpdate(cleanedText, out var profileUpdate, out var profileCleanedText)) {
            profileChanged = await ApplyProfileUpdateAsync(profileUpdate, autoCompleteOnboardingForProfileScope: false).ConfigureAwait(false);
            cleanedText = profileCleanedText;
        }

        var memoryChanged = false;
        if (MemoryModelProtocol.TryExtractLastMemoryUpdate(cleanedText, out var memoryUpdate, out var memoryCleanedText)) {
            memoryChanged = await ApplyMemoryUpdateAsync(memoryUpdate).ConfigureAwait(false);
            cleanedText = memoryCleanedText;
        }

        if (ActionModelProtocol.TryStripAndExtractPendingActions(cleanedText, out var extractedPendingActions, out var actionCleanedText)) {
            pendingActions = extractedPendingActions;
            cleanedText = ActionModelProtocol.MergeVisibleTextWithPendingActions(actionCleanedText, pendingActions);
        }

        var pendingAssistantQuestionHint = ConversationStyleGuidanceBuilder.BuildAssistantQuestionHint(cleanedText);

        if (!string.IsNullOrWhiteSpace(cleanedText)) {
            return new NormalizedAssistantTurn(cleanedText, pendingActions, pendingAssistantQuestionHint);
        }

        return new NormalizedAssistantTurn(profileChanged || memoryChanged ? "Got it." : normalized, pendingActions, pendingAssistantQuestionHint);
    }

    private static string? NormalizeProfileValue(string? value) {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length == 0 ? null : normalized;
    }

    private string? NormalizeAssistantPersonaValue(string? value) {
        var normalized = NormalizeProfileValue(value);
        if (string.IsNullOrWhiteSpace(normalized)) {
            return null;
        }

        var persona = normalized!;
        if (IsPersonaSkipValue(persona)) {
            return null;
        }

        if (persona.StartsWith("assistant persona:", StringComparison.OrdinalIgnoreCase)) {
            persona = persona["assistant persona:".Length..].Trim();
        } else if (persona.StartsWith("persona:", StringComparison.OrdinalIgnoreCase)) {
            persona = persona["persona:".Length..].Trim();
        }

        persona = persona.Replace('\r', ' ').Replace('\n', ' ').Trim();
        persona = TrimProfilePunctuation(persona);
        if (persona.Length == 0) {
            return null;
        }

        if (IsGenericPersonaValue(persona)) {
            var expanded = ExpandGenericPersonaFromHint(persona, FindRecentPersonaHintText());
            if (!string.IsNullOrWhiteSpace(expanded)) {
                persona = expanded;
            } else if (!string.IsNullOrWhiteSpace(_appState.AssistantPersona) && !IsGenericPersonaValue(_appState.AssistantPersona!)) {
                // Prevent downgrading a rich persona to a single generic token.
                persona = _appState.AssistantPersona!;
            } else {
                // Enforce descriptive persona values during onboarding.
                return null;
            }
        }

        if (persona.Length > 180) {
            persona = persona[..180].TrimEnd();
        }

        return persona.Length == 0 ? null : persona;
    }

    private string? FindRecentPersonaHintText() {
        var conversation = ResolveRequestConversation();
        for (var i = conversation.Messages.Count - 1; i >= 0; i--) {
            var message = conversation.Messages[i];
            if (!string.Equals(message.Role, "User", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var text = NormalizeProfileValue(message.Text);
            if (string.IsNullOrWhiteSpace(text)) {
                continue;
            }

            if (LooksLikePersonaPreferenceText(text!)) {
                return text;
            }
        }

        return null;
    }

    private static bool LooksLikePersonaPreferenceText(string text) {
        if (text.Contains("persona", StringComparison.OrdinalIgnoreCase)
            || text.Contains("style", StringComparison.OrdinalIgnoreCase)
            || text.Contains("mode", StringComparison.OrdinalIgnoreCase)
            || text.Contains("tone", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        return text.Contains("optimistic", StringComparison.OrdinalIgnoreCase)
               || text.Contains("helpful", StringComparison.OrdinalIgnoreCase)
               || text.Contains("friendly", StringComparison.OrdinalIgnoreCase)
               || text.Contains("funny", StringComparison.OrdinalIgnoreCase)
               || text.Contains("humor", StringComparison.OrdinalIgnoreCase)
               || text.Contains("concise", StringComparison.OrdinalIgnoreCase)
               || text.Contains("explan", StringComparison.OrdinalIgnoreCase)
               || text.Contains("pragmatic", StringComparison.OrdinalIgnoreCase)
               || text.Contains("analyst", StringComparison.OrdinalIgnoreCase)
               || text.Contains("engineer", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExpandGenericPersonaFromHint(string genericPersona, string? hintText) {
        if (string.IsNullOrWhiteSpace(hintText)) {
            return null;
        }

        var role = NormalizePersonaRole(genericPersona, hintText!);
        var traits = CollectPersonaTraits(hintText!);
        if (traits.Count == 0) {
            return null;
        }

        return role + " with " + JoinTraits(traits) + ".";
    }

    private static List<string> CollectPersonaTraits(string hintText) {
        var traits = new List<string>();
        AddTraitIfPresent(traits, hintText, "optimistic", "optimistic tone");
        AddTraitIfPresent(traits, hintText, "helpful", "helpful guidance");
        AddTraitIfPresent(traits, hintText, "friendly", "friendly tone");
        if (hintText.Contains("funny", StringComparison.OrdinalIgnoreCase)
            || hintText.Contains("humor", StringComparison.OrdinalIgnoreCase)
            || hintText.Contains("humour", StringComparison.OrdinalIgnoreCase)) {
            traits.Add("light humor");
        }
        AddTraitIfPresent(traits, hintText, "concise", "concise outputs");
        AddTraitIfPresent(traits, hintText, "pragmatic", "pragmatic guidance");
        if (hintText.Contains("explan", StringComparison.OrdinalIgnoreCase)
            || hintText.Contains("detailed", StringComparison.OrdinalIgnoreCase)) {
            traits.Add("clear explanations");
        }

        return traits;
    }

    private static void AddTraitIfPresent(List<string> traits, string text, string needle, string label) {
        if (!text.Contains(needle, StringComparison.OrdinalIgnoreCase)) {
            return;
        }

        if (!traits.Exists(t => string.Equals(t, label, StringComparison.OrdinalIgnoreCase))) {
            traits.Add(label);
        }
    }

    private static string JoinTraits(IReadOnlyList<string> traits) {
        if (traits.Count == 0) {
            return string.Empty;
        }

        if (traits.Count == 1) {
            return traits[0];
        }

        if (traits.Count == 2) {
            return traits[0] + " and " + traits[1];
        }

        var sb = new StringBuilder();
        for (var i = 0; i < traits.Count; i++) {
            if (i > 0) {
                sb.Append(i == traits.Count - 1 ? ", and " : ", ");
            }
            sb.Append(traits[i]);
        }

        return sb.ToString();
    }

    private static string NormalizePersonaRole(string genericPersona, string hintText) {
        var lowerHint = hintText.ToLowerInvariant();
        if (lowerHint.Contains("security analyst", StringComparison.Ordinal)
            || lowerHint.Contains("security", StringComparison.Ordinal)) {
            return "security analyst";
        }

        var token = genericPersona.Trim().ToLowerInvariant();
        return token switch {
            "analyst" or "analyst mode" => "analyst",
            "engineer" => "engineer",
            "admin" or "administrator" => "admin engineer",
            "operator" => "operations analyst",
            "security" => "security analyst",
            _ => genericPersona.Trim()
        };
    }

    private static bool IsGenericPersonaValue(string value) {
        var v = value.Trim().ToLowerInvariant();
        return v is "analyst"
            or "analyst mode"
            or "engineer"
            or "admin"
            or "administrator"
            or "operator"
            or "security"
            or "security analyst"
            or "support"
            or "helper"
            or "default";
    }

    private static bool IsPersonaSkipValue(string value) {
        var v = value.Trim();
        return v.Equals("skip", StringComparison.OrdinalIgnoreCase)
               || v.Equals("default", StringComparison.OrdinalIgnoreCase)
               || v.Equals("defaults", StringComparison.OrdinalIgnoreCase);
    }

    private static string TrimProfilePunctuation(string value) {
        var trimmed = value.Trim().Trim('.', '!', '?', ',', ';', ':', '"', '\'');
        return trimmed.Length == 0 ? value.Trim() : trimmed;
    }

    private static string? NormalizeUserNameValue(string? value) {
        var normalized = NormalizeProfileValue(value);
        if (string.IsNullOrWhiteSpace(normalized)) {
            return null;
        }

        var v = normalized!;
        if (v.Equals("skip", StringComparison.OrdinalIgnoreCase)
            || v.Equals("default", StringComparison.OrdinalIgnoreCase)
            || v.Equals("defaults", StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        var marker = v.IndexOf("call me", StringComparison.OrdinalIgnoreCase);
        if (marker >= 0) {
            var start = marker + "call me".Length;
            v = v[start..].Trim();
        }

        var trimmedPunctuation = v.Trim().Trim('.', '!', '?', ',', ';', ':', '"', '\'');
        if (trimmedPunctuation.Length > 0) {
            v = trimmedPunctuation;
        }

        // Keep profile names compact for consistent prompt-shaping and UI display.
        if (v.Length > 48) {
            v = v[..48].TrimEnd();
        }

        return v.Length == 0 ? null : v;
    }

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
