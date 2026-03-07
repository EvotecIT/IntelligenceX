using System;
using System.Collections.Generic;
using IntelligenceX.Chat.App.Markdown;

namespace IntelligenceX.Chat.App;

public sealed partial class MainWindow {
    private string BuildRequestTextForService(string userText) {
        var activeConversation = GetActiveConversation();
        var effectivePersona = GetEffectiveAssistantPersona();
        var effectiveName = GetEffectiveUserName();
        var onboardingInProgress = !_appState.OnboardingCompleted;
        var assistantCapabilityQuestion = ConversationTurnShapeClassifier.LooksLikeAssistantCapabilityQuestion(userText);
        var assistantRuntimeIntrospectionQuestion = ConversationTurnShapeClassifier.LooksLikeAssistantRuntimeIntrospectionQuestion(userText);
        var includeOnboardingContext = ShouldIncludeAmbientOnboardingContext(
            userText,
            onboardingInProgress,
            assistantCapabilityQuestion,
            assistantRuntimeIntrospectionQuestion);
        IReadOnlyList<string> missingFields = includeOnboardingContext ? BuildMissingOnboardingFields() : Array.Empty<string>();
        var localContextLines = BuildLocalContextFallbackLines(activeConversation, userText);
        var conversationStyleLines = ConversationStyleGuidanceBuilder.BuildRecentUserStyleLines(activeConversation.Messages);
        var capabilityAnswerStyleLines = assistantCapabilityQuestion
            ? ConversationStyleGuidanceBuilder.BuildCapabilityAnswerStyleLines(activeConversation.Messages)
            : null;
        var personaGuidanceLines = BuildPersonaGuidanceLines(effectivePersona);
        var continuationStateLines = ConversationStyleGuidanceBuilder.BuildContinuationStateLines(
            activeConversation.Messages,
            activeConversation.PendingActions,
            activeConversation.PendingAssistantQuestionHint);
        var recentAssistantAnswerWasSubstantive = ConversationStyleGuidanceBuilder.HasRecentSubstantiveAssistantAnswer(activeConversation.Messages);
        var recentAssistantAskedQuestion = ConversationStyleGuidanceBuilder.HasRecentAssistantQuestion(activeConversation.Messages);
        var memoryContextLines = BuildPersistentMemoryContextLines(userText);
        var capabilitySelfKnowledgeLines = assistantCapabilityQuestion || assistantRuntimeIntrospectionQuestion
            ? BuildCapabilitySelfKnowledgeLines(runtimeIntrospectionMode: assistantRuntimeIntrospectionQuestion)
            : null;
        var runtimeCapabilityLines = assistantRuntimeIntrospectionQuestion
            ? BuildRuntimeCapabilityContextLines()
            : null;
        var proactiveExecutionEnabled = ResolveProactiveExecutionGuidanceMode(
            _proactiveModeEnabled,
            userText,
            assistantCapabilityQuestion,
            assistantRuntimeIntrospectionQuestion,
            recentAssistantAskedQuestion);

        return PromptMarkdownBuilder.BuildServiceRequest(
            userText: userText,
            effectiveName: effectiveName,
            effectivePersona: effectivePersona,
            onboardingInProgress: includeOnboardingContext,
            missingOnboardingFields: missingFields,
            includeLiveProfileUpdates: MightContainProfileUpdateCue(userText),
            executionBehaviorPrompt: PromptAssets.GetExecutionBehaviorPrompt(),
            localContextLines: localContextLines,
            conversationStyleLines: conversationStyleLines,
            capabilityAnswerStyleLines: capabilityAnswerStyleLines,
            personaGuidanceLines: personaGuidanceLines,
            continuationStateLines: continuationStateLines,
            recentAssistantAnswerWasSubstantive: recentAssistantAnswerWasSubstantive,
            recentAssistantAskedQuestion: recentAssistantAskedQuestion,
            persistentMemoryLines: memoryContextLines,
            persistentMemoryPrompt: _persistentMemoryEnabled ? PromptAssets.GetPersistentMemoryPrompt() : string.Empty,
            capabilitySelfKnowledgeLines: capabilitySelfKnowledgeLines,
            runtimeCapabilityLines: runtimeCapabilityLines,
            proactiveExecutionEnabled: proactiveExecutionEnabled);
    }

    internal static bool? ResolveProactiveExecutionGuidanceMode(
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

    internal static bool ShouldIncludeAmbientOnboardingContext(
        string? userText,
        bool onboardingInProgress,
        bool assistantCapabilityQuestion,
        bool assistantRuntimeIntrospectionQuestion) {
        if (!onboardingInProgress) {
            return false;
        }

        var normalized = (userText ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        if (assistantCapabilityQuestion || assistantRuntimeIntrospectionQuestion) {
            return false;
        }

        return ConversationTurnShapeClassifier.LooksLikeLowContextShortTurn(normalized);
    }

    internal static bool ShouldIncludeProactiveExecutionMode(
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
}
