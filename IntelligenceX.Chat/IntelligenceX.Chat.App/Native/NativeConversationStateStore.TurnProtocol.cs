using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.App.Conversation;
using IntelligenceX.Chat.App.Theming;

namespace IntelligenceX.Chat.App.Native;

internal sealed partial class NativeConversationStateStore {
    /// <summary>
    /// Builds the same service request envelope used by the legacy desktop shell.
    /// </summary>
    internal string BuildRequestText(NativeConversation conversation, string userText, SessionPolicyDto? sessionPolicy = null) {
        ArgumentNullException.ThrowIfNull(conversation);
        if (_state is null) {
            throw new InvalidOperationException("Native profile state must be loaded before chat requests are created.");
        }

        var messages = conversation.Messages
            .Select(static message => (
                Role: message.Role,
                Text: message.Text,
                Time: message.CreatedAt.LocalDateTime,
                Model: message.Model))
            .ToList();
        var runtimeSelfReportAnalysis = ConversationTurnShapeClassifier.AnalyzeAssistantRuntimeIntrospectionQuestion(userText);
        var assistantCapabilityQuestion = ConversationTurnShapeClassifier.LooksLikeAssistantCapabilityQuestion(userText);
        var assistantRuntimeIntrospectionQuestion = runtimeSelfReportAnalysis.IsRuntimeIntrospectionQuestion;
        var onboardingInProgress = !_state.OnboardingCompleted;
        var includeOnboardingContext = DesktopChatTurnProtocol.ShouldIncludeAmbientOnboardingContext(
            userText,
            onboardingInProgress,
            assistantCapabilityQuestion,
            assistantRuntimeIntrospectionQuestion);
        var recentAssistantAskedQuestion = ConversationStyleGuidanceBuilder.HasRecentAssistantQuestion(messages);
        var effectiveUserName = _sessionUserName ?? _state.UserName;
        var effectivePersona = _sessionAssistantPersona ?? _state.AssistantPersona;
        var effectiveTheme = _sessionThemePreset ?? _state.ThemePreset;
        IReadOnlyList<string> missingFields = includeOnboardingContext
            ? MainWindow.BuildMissingOnboardingFields(
                effectiveUserName,
                effectivePersona,
                effectiveTheme,
                _state.OnboardingCompleted)
            : Array.Empty<string>();
        var capabilitySelfKnowledgeLines = MainWindow.SelectCapabilitySelfKnowledgeLines(
            sessionPolicy,
            assistantCapabilityQuestion,
            assistantRuntimeIntrospectionQuestion,
            runtimeSelfReportAnalysis.DetectionSource);

        return DesktopChatTurnProtocol.BuildRequestText(new DesktopChatTurnPromptContext {
            UserText = userText,
            EffectiveUserName = effectiveUserName,
            EffectiveAssistantPersona = effectivePersona,
            IncludeOnboardingContext = includeOnboardingContext,
            MissingOnboardingFields = missingFields,
            // Keep the structured profile protocol available in native chat without duplicating lexical intent rules.
            IncludeLiveProfileUpdates = true,
            LocalContextLines = DesktopChatTurnProtocol.BuildLocalContextFallbackLines(
                conversation.ThreadId,
                messages,
                userText),
            ConversationStyleLines = ConversationStyleGuidanceBuilder.BuildRecentUserStyleLines(messages),
            CapabilityAnswerStyleLines = assistantCapabilityQuestion
                ? ConversationStyleGuidanceBuilder.BuildCapabilityAnswerStyleLines(messages)
                : null,
            PersonaGuidanceLines = MainWindow.BuildPersonaGuidanceLines(effectivePersona),
            ContinuationStateLines = ConversationStyleGuidanceBuilder.BuildContinuationStateLines(
                messages,
                conversation.PendingActions,
                conversation.PendingAssistantQuestionHint),
            RecentAssistantAnswerWasSubstantive = ConversationStyleGuidanceBuilder.HasRecentSubstantiveAssistantAnswer(messages),
            RecentAssistantAskedQuestion = recentAssistantAskedQuestion,
            PersistentMemoryLines = BuildNativeMemoryContextLines(_state),
            PersistentMemoryEnabled = _state.PersistentMemoryEnabled,
            CapabilitySelfKnowledgeLines = capabilitySelfKnowledgeLines,
            ProactiveExecutionEnabled = DesktopChatTurnProtocol.ResolveProactiveExecutionGuidanceMode(
                _state.ProactiveModeEnabled,
                userText,
                assistantCapabilityQuestion,
                assistantRuntimeIntrospectionQuestion,
                recentAssistantAskedQuestion),
            RuntimeSelfReportAnalysis = runtimeSelfReportAnalysis
        });
    }

    /// <summary>
    /// Strips private assistant protocol blocks and applies their structured state updates.
    /// </summary>
    internal async Task<DesktopAssistantTurnProtocolResult> NormalizeAssistantTurnAsync(
        NativeConversation conversation,
        string? assistantText,
        CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(conversation);
        var result = DesktopChatTurnProtocol.NormalizeAssistantResponse(assistantText);
        var persistentProfileUpdate = result.ProfileUpdate is not null
                                      && ResolveProfileScope(result.ProfileUpdate) == ProfileUpdateScope.Profile;
        var hasPersistentUpdate = persistentProfileUpdate || result.MemoryUpdate is not null;
        var stateChanged = false;

        if (result.ProfileUpdate is not null && !persistentProfileUpdate) {
            ApplySessionProfileUpdate(result.ProfileUpdate);
            stateChanged = true;
        }

        if (hasPersistentUpdate) {
            _state = await _stateStore.UpdateAsync(
                    _profileName,
                    current => {
                        var state = current ?? _state ?? new ChatAppState { ProfileName = _profileName };
                        if (persistentProfileUpdate) {
                            stateChanged |= ApplyPersistentProfileUpdate(state, result.ProfileUpdate!);
                        }
                        if (result.MemoryUpdate is not null) {
                            stateChanged |= ApplyMemoryUpdate(state, result.MemoryUpdate);
                        }
                        return state;
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }

        conversation.PendingActions.Clear();
        conversation.PendingActions.AddRange(result.PendingActions);
        conversation.PendingAssistantQuestionHint = result.PendingAssistantQuestionHint;
        if (result.PendingActions.Count > 0 || result.PendingAssistantQuestionHint is not null) {
            conversation.UpdatedUtc = DateTime.UtcNow;
        }

        return string.IsNullOrWhiteSpace(result.VisibleText) && stateChanged
            ? result with { VisibleText = "Got it." }
            : result;
    }

    private static IReadOnlyList<string> BuildNativeMemoryContextLines(ChatAppState state) {
        if (!state.PersistentMemoryEnabled || state.MemoryFacts is not { Count: > 0 }) {
            return Array.Empty<string>();
        }

        return state.MemoryFacts
            .Where(static fact => !string.IsNullOrWhiteSpace(fact.Fact))
            .OrderByDescending(static fact => fact.Weight)
            .ThenByDescending(static fact => fact.UpdatedUtc)
            .Select(static fact => fact.Fact.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToArray();
    }

    private static ProfileUpdateScope ResolveProfileScope(OnboardingProfileUpdate update) {
        if (update.Scope != ProfileUpdateScope.Unspecified) {
            return update.Scope;
        }

        return update.HasOnboardingCompleted
               && update.OnboardingCompleted
               && (update.HasUserName || update.HasAssistantPersona || update.HasThemePreset)
            ? ProfileUpdateScope.Profile
            : ProfileUpdateScope.Session;
    }

    private void ApplySessionProfileUpdate(OnboardingProfileUpdate update) {
        if (update.HasUserName) {
            _sessionUserName = NormalizeOptionalProfileValue(update.UserName);
        }
        if (update.HasAssistantPersona) {
            _sessionAssistantPersona = NormalizeOptionalProfileValue(update.AssistantPersona);
        }
        if (update.HasThemePreset) {
            _sessionThemePreset = ThemeContract.Normalize(update.ThemePreset);
        }
    }

    private static bool ApplyPersistentProfileUpdate(ChatAppState state, OnboardingProfileUpdate update) {
        var changed = false;
        if (update.HasUserName) {
            var next = NormalizeOptionalProfileValue(update.UserName);
            if (!string.Equals(state.UserName, next, StringComparison.Ordinal)) {
                state.UserName = next;
                changed = true;
            }
        }
        if (update.HasAssistantPersona) {
            var next = NormalizeOptionalProfileValue(update.AssistantPersona);
            if (!string.Equals(state.AssistantPersona, next, StringComparison.Ordinal)) {
                state.AssistantPersona = next;
                changed = true;
            }
        }
        if (update.HasThemePreset) {
            var next = ThemeContract.Normalize(update.ThemePreset);
            if (next is not null && !string.Equals(state.ThemePreset, next, StringComparison.OrdinalIgnoreCase)) {
                state.ThemePreset = next;
                changed = true;
            }
        }
        if (update.HasOnboardingCompleted) {
            var missingFields = MainWindow.BuildMissingOnboardingFields(
                state.UserName,
                state.AssistantPersona,
                state.ThemePreset,
                onboardingCompleted: false);
            var next = update.OnboardingCompleted && missingFields.Count == 0;
            if (state.OnboardingCompleted != next) {
                state.OnboardingCompleted = next;
                changed = true;
            }
        }

        return changed;
    }

    private static bool ApplyMemoryUpdate(ChatAppState state, AssistantMemoryUpdate update) {
        if (!state.PersistentMemoryEnabled) {
            return false;
        }

        state.MemoryFacts ??= new List<ChatMemoryFactState>();
        var changed = false;
        foreach (var candidate in update.DeleteFacts) {
            var normalized = (candidate ?? string.Empty).Trim();
            if (normalized.Length == 0) {
                continue;
            }
            changed |= state.MemoryFacts.RemoveAll(fact =>
                string.Equals(fact.Id, normalized, StringComparison.OrdinalIgnoreCase)
                || string.Equals(fact.Fact, normalized, StringComparison.OrdinalIgnoreCase)) > 0;
        }

        foreach (var upsert in update.Upserts) {
            var factText = (upsert?.Fact ?? string.Empty).Trim();
            if (factText.Length == 0) {
                continue;
            }
            var tags = (upsert!.Tags ?? Array.Empty<string>())
                .Where(static tag => !string.IsNullOrWhiteSpace(tag))
                .Select(static tag => tag.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var existing = state.MemoryFacts.FirstOrDefault(fact =>
                string.Equals(fact.Fact, factText, StringComparison.OrdinalIgnoreCase));
            if (existing is null) {
                state.MemoryFacts.Add(new ChatMemoryFactState {
                    Id = Guid.NewGuid().ToString("N"),
                    Fact = factText,
                    Weight = Math.Clamp(upsert.Weight, 1, 5),
                    Tags = tags,
                    UpdatedUtc = DateTime.UtcNow
                });
            } else {
                existing.Weight = Math.Clamp(upsert.Weight, 1, 5);
                existing.Tags = tags;
                existing.UpdatedUtc = DateTime.UtcNow;
            }
            changed = true;
        }

        if (state.MemoryFacts.Count > 120) {
            state.MemoryFacts = state.MemoryFacts
                .OrderByDescending(static fact => fact.UpdatedUtc)
                .Take(120)
                .ToList();
        }
        return changed;
    }

    private static string? NormalizeOptionalProfileValue(string? value) {
        var normalized = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (normalized.Length > 180) {
            normalized = normalized[..180].TrimEnd();
        }
        return normalized.Length == 0 ? null : normalized;
    }
}
