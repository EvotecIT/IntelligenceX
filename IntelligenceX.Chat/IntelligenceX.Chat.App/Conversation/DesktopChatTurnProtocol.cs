using System;
using System.Collections.Generic;
using IntelligenceX.Chat.Abstractions;
using IntelligenceX.Chat.App.Markdown;

namespace IntelligenceX.Chat.App.Conversation;

/// <summary>
/// Captures the shell-neutral context used to build one desktop chat request.
/// </summary>
internal sealed record DesktopChatTurnPromptContext {
    public required string UserText { get; init; }
    public string? EffectiveUserName { get; init; }
    public string? EffectiveAssistantPersona { get; init; }
    public bool IncludeOnboardingContext { get; init; }
    public IReadOnlyList<string> MissingOnboardingFields { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> LocalContextLines { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ConversationStyleLines { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string>? CapabilityAnswerStyleLines { get; init; }
    public IReadOnlyList<string> PersonaGuidanceLines { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ContinuationStateLines { get; init; } = Array.Empty<string>();
    public bool RecentAssistantAnswerWasSubstantive { get; init; }
    public bool RecentAssistantAskedQuestion { get; init; }
    public IReadOnlyList<string> PersistentMemoryLines { get; init; } = Array.Empty<string>();
    public bool PersistentMemoryEnabled { get; init; }
    public IReadOnlyList<string>? CapabilitySelfKnowledgeLines { get; init; }
    public IReadOnlyList<string>? RuntimeCapabilityLines { get; init; }
    public bool? ProactiveExecutionEnabled { get; init; }
    public required RuntimeSelfReportTurnClassifier.RuntimeSelfReportTurnAnalysis RuntimeSelfReportAnalysis { get; init; }
}

/// <summary>
/// Represents the visible and structured parts of one assistant response.
/// </summary>
internal sealed record DesktopAssistantTurnProtocolResult(
    string VisibleText,
    IReadOnlyList<AssistantPendingAction> PendingActions,
    string? PendingAssistantQuestionHint,
    OnboardingProfileUpdate? ProfileUpdate,
    AssistantMemoryUpdate? MemoryUpdate);

/// <summary>
/// Owns request-envelope construction and assistant protocol extraction for every desktop shell.
/// </summary>
internal static class DesktopChatTurnProtocol {
    /// <summary>
    /// Builds the service request text from shell-neutral turn context.
    /// </summary>
    public static string BuildRequestText(DesktopChatTurnPromptContext context) {
        ArgumentNullException.ThrowIfNull(context);
        var userText = (context.UserText ?? string.Empty).Trim();
        var includeStructuredProfileUpdates = ShouldIncludeStructuredProfileUpdates(context.RuntimeSelfReportAnalysis);
        if (ShouldUseThinRequestEnvelope(context.IncludeOnboardingContext, includeStructuredProfileUpdates)) {
            var runtimeSelfReportDirectiveLines = PromptMarkdownBuilder.BuildRuntimeSelfReportDirectiveLines(
                context.RuntimeSelfReportAnalysis);
            return PromptMarkdownBuilder.BuildThinServiceRequest(
                userText: userText,
                effectiveName: context.EffectiveUserName,
                effectivePersona: context.EffectiveAssistantPersona,
                persistentMemoryLines: context.PersistentMemoryLines,
                persistentMemoryPrompt: context.PersistentMemoryEnabled ? PromptAssets.GetPersistentMemoryPrompt() : string.Empty,
                runtimeSelfReportDirectiveLines: runtimeSelfReportDirectiveLines,
                runtimeSelfReportAnalysis: context.RuntimeSelfReportAnalysis);
        }

        return PromptMarkdownBuilder.BuildServiceRequest(
            userText: userText,
            effectiveName: context.EffectiveUserName,
            effectivePersona: context.EffectiveAssistantPersona,
            onboardingInProgress: context.IncludeOnboardingContext,
            missingOnboardingFields: context.MissingOnboardingFields,
            includeLiveProfileUpdates: includeStructuredProfileUpdates,
            executionBehaviorPrompt: PromptAssets.GetExecutionBehaviorPrompt(),
            localContextLines: context.LocalContextLines,
            conversationStyleLines: context.ConversationStyleLines,
            capabilityAnswerStyleLines: context.CapabilityAnswerStyleLines,
            personaGuidanceLines: context.PersonaGuidanceLines,
            continuationStateLines: context.ContinuationStateLines,
            recentAssistantAnswerWasSubstantive: context.RecentAssistantAnswerWasSubstantive,
            recentAssistantAskedQuestion: context.RecentAssistantAskedQuestion,
            persistentMemoryLines: context.PersistentMemoryLines,
            persistentMemoryPrompt: context.PersistentMemoryEnabled ? PromptAssets.GetPersistentMemoryPrompt() : string.Empty,
            capabilitySelfKnowledgeLines: context.CapabilitySelfKnowledgeLines,
            runtimeCapabilityLines: context.RuntimeCapabilityLines,
            proactiveExecutionEnabled: context.ProactiveExecutionEnabled,
            runtimeSelfReportAnalysis: context.RuntimeSelfReportAnalysis);
    }

    /// <summary>
    /// Extracts private desktop protocol blocks while preserving the assistant text intended for the transcript.
    /// </summary>
    public static DesktopAssistantTurnProtocolResult NormalizeAssistantResponse(string? assistantText) {
        var normalized = (assistantText ?? string.Empty).Trim();
        var cleanedText = normalized;
        OnboardingProfileUpdate? profileUpdate = null;
        AssistantMemoryUpdate? memoryUpdate = null;
        IReadOnlyList<AssistantPendingAction> pendingActions = Array.Empty<AssistantPendingAction>();

        if (OnboardingModelProtocol.TryExtractLastProfileUpdate(cleanedText, out var extractedProfile, out var profileCleanedText)) {
            profileUpdate = extractedProfile;
            cleanedText = profileCleanedText;
        }

        if (MemoryModelProtocol.TryExtractLastMemoryUpdate(cleanedText, out var extractedMemory, out var memoryCleanedText)) {
            memoryUpdate = extractedMemory;
            cleanedText = memoryCleanedText;
        }

        if (ActionModelProtocol.TryStripAndExtractPendingActions(cleanedText, out var extractedActions, out var actionCleanedText)) {
            pendingActions = extractedActions;
            cleanedText = ActionModelProtocol.MergeVisibleTextWithPendingActions(actionCleanedText, pendingActions);
        }

        if (string.IsNullOrWhiteSpace(cleanedText) && (profileUpdate is not null || memoryUpdate is not null)) {
            cleanedText = "Got it.";
        }

        return new DesktopAssistantTurnProtocolResult(
            cleanedText,
            pendingActions,
            ConversationStyleGuidanceBuilder.BuildAssistantQuestionHint(cleanedText),
            profileUpdate,
            memoryUpdate);
    }

    public static bool ShouldUseThinRequestEnvelope(bool includeOnboardingContext, bool includeLiveProfileUpdates) =>
        !includeOnboardingContext && !includeLiveProfileUpdates;

    /// <summary>
    /// Keeps profile-update protocol routing language-neutral and identical across desktop shells.
    /// Runtime self-report turns are the only structural exception because they suppress profile context.
    /// </summary>
    public static bool ShouldIncludeStructuredProfileUpdates(
        RuntimeSelfReportTurnClassifier.RuntimeSelfReportTurnAnalysis runtimeSelfReportAnalysis) =>
        !runtimeSelfReportAnalysis.IsRuntimeIntrospectionQuestion;

    public static bool ShouldIncludeAmbientOnboardingContext(
        string? userText,
        bool onboardingInProgress,
        bool assistantCapabilityQuestion,
        bool assistantRuntimeIntrospectionQuestion) {
        if (!onboardingInProgress) {
            return false;
        }

        var normalized = (userText ?? string.Empty).Trim();
        return normalized.Length > 0
               && !assistantCapabilityQuestion
               && !assistantRuntimeIntrospectionQuestion
               && ConversationTurnShapeClassifier.LooksLikeLowContextShortTurn(normalized);
    }

    public static bool? ResolveProactiveExecutionGuidanceMode(
        bool proactiveModeEnabled,
        string? userText,
        bool assistantCapabilityQuestion,
        bool assistantRuntimeIntrospectionQuestion,
        bool recentAssistantAskedQuestion) {
        if (!proactiveModeEnabled) {
            return false;
        }

        return ShouldIncludeProactiveExecutionMode(
            userText,
            assistantCapabilityQuestion,
            assistantRuntimeIntrospectionQuestion,
            recentAssistantAskedQuestion)
            ? true
            : null;
    }

    public static bool ShouldIncludeProactiveExecutionMode(
        string? userText,
        bool assistantCapabilityQuestion,
        bool assistantRuntimeIntrospectionQuestion,
        bool recentAssistantAskedQuestion) {
        var normalized = (userText ?? string.Empty).Trim();
        if (normalized.Length == 0
            || assistantCapabilityQuestion
            || assistantRuntimeIntrospectionQuestion
            || ConversationTurnShapeClassifier.LooksLikeLowContextShortTurn(normalized)) {
            return false;
        }

        if (recentAssistantAskedQuestion && ConversationTurnShapeClassifier.LooksLikeContextDependentFollowUp(normalized)) {
            return true;
        }

        return !ConversationTurnShapeClassifier.ContainsQuestionSignal(normalized);
    }

    /// <summary>
    /// Builds a compact local-history fallback when the service thread cannot carry the context.
    /// </summary>
    public static IReadOnlyList<string> BuildLocalContextFallbackLines(
        string? threadId,
        IReadOnlyList<(string Role, string Text, DateTime Time, string? Model)> messages,
        string? userText) {
        ArgumentNullException.ThrowIfNull(messages);
        var needsFallback = string.IsNullOrWhiteSpace(threadId)
                            || ConversationTurnShapeClassifier.LooksLikeContextDependentFollowUp(userText);
        if (!needsFallback) {
            return Array.Empty<string>();
        }

        var lines = new List<string>();
        var normalizedUserText = (userText ?? string.Empty).Trim();
        for (var i = messages.Count - 1; i >= 0 && lines.Count < 6; i--) {
            var message = messages[i];
            if (string.IsNullOrWhiteSpace(message.Text)
                || string.Equals(message.Role, "Tools", StringComparison.OrdinalIgnoreCase)
                || string.Equals(message.Role, "System", StringComparison.OrdinalIgnoreCase)
                || string.Equals(message.Role, "User", StringComparison.OrdinalIgnoreCase)
                   && string.Equals(message.Text.Trim(), normalizedUserText, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var compact = (message.Text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (compact.Length > 220) {
                compact = compact[..220].TrimEnd() + "...";
            }
            if (compact.Length == 0) {
                continue;
            }

            lines.Add((string.Equals(message.Role, "Assistant", StringComparison.OrdinalIgnoreCase) ? "Assistant" : "User")
                      + ": " + compact);
        }

        lines.Reverse();
        return lines;
    }
}
