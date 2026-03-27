using System;
using System.Collections.Generic;
using IntelligenceX.Chat.Abstractions;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.App.Markdown;

namespace IntelligenceX.Chat.App;

public sealed partial class MainWindow {
    private string BuildRequestTextForService(string userText) {
        var activeConversation = GetActiveConversation();
        var effectivePersona = GetEffectiveAssistantPersona();
        var effectiveName = GetEffectiveUserName();
        var profileIntent = ParseUserProfileIntent(userText);
        var includeLiveProfileUpdates = ShouldIncludeLiveProfileUpdates(
            profileIntent.HasUserName,
            profileIntent.HasAssistantPersona,
            profileIntent.HasThemePreset);
        var onboardingInProgress = !_appState.OnboardingCompleted;
        var runtimeSelfReportAnalysis = ConversationTurnShapeClassifier.AnalyzeAssistantRuntimeIntrospectionQuestion(userText);
        var assistantCapabilityQuestion = ConversationTurnShapeClassifier.LooksLikeAssistantCapabilityQuestion(userText);
        var compactAssistantRuntimeIntrospectionQuestion = runtimeSelfReportAnalysis.CompactReply;
        var assistantRuntimeIntrospectionQuestion = runtimeSelfReportAnalysis.IsRuntimeIntrospectionQuestion;
        var includeOnboardingContext = ShouldIncludeAmbientOnboardingContext(
            userText,
            onboardingInProgress,
            assistantCapabilityQuestion,
            assistantRuntimeIntrospectionQuestion);
        var memoryContextLines = BuildPersistentMemoryContextLines(userText);
        var shouldUseThinRequestEnvelope = ShouldUseThinServiceRequestEnvelope(
            includeOnboardingContext,
            includeLiveProfileUpdates);

        if (shouldUseThinRequestEnvelope) {
            var runtimeSelfReportDirectiveLines = PromptMarkdownBuilder.BuildRuntimeSelfReportDirectiveLines(runtimeSelfReportAnalysis);

            return PromptMarkdownBuilder.BuildThinServiceRequest(
                userText: userText,
                effectiveName: effectiveName,
                effectivePersona: effectivePersona,
                persistentMemoryLines: memoryContextLines,
                persistentMemoryPrompt: _persistentMemoryEnabled ? PromptAssets.GetPersistentMemoryPrompt() : string.Empty,
                runtimeSelfReportDirectiveLines: runtimeSelfReportDirectiveLines,
                runtimeSelfReportAnalysis: runtimeSelfReportAnalysis);
        }

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
        var capabilitySelfKnowledgeLines = SelectCapabilitySelfKnowledgeLines(
            _sessionPolicy,
            _toolCatalogPacks,
            _toolCatalogPlugins,
            _toolCatalogRoutingCatalog,
            _toolCatalogCapabilitySnapshot,
            _toolCatalogDefinitions.Count == 0 ? null : _toolCatalogDefinitions.Values,
            assistantCapabilityQuestion,
            assistantRuntimeIntrospectionQuestion,
            runtimeSelfReportAnalysis.DetectionSource);
        var runtimeCapabilityLines = assistantRuntimeIntrospectionQuestion
            ? BuildRuntimeCapabilityContextLines(
                compactSelfReport: ShouldUseCompactRuntimeCapabilityContext(
                    assistantRuntimeIntrospectionQuestion,
                    compactAssistantRuntimeIntrospectionQuestion,
                    runtimeSelfReportAnalysis.DetectionSource),
                runtimeSelfReportDetectionSource: runtimeSelfReportAnalysis.DetectionSource)
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
            includeLiveProfileUpdates: includeLiveProfileUpdates,
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
            proactiveExecutionEnabled: proactiveExecutionEnabled,
            runtimeSelfReportAnalysis: runtimeSelfReportAnalysis);
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

    internal static IReadOnlyList<string>? SelectCapabilitySelfKnowledgeLines(
        SessionPolicyDto? sessionPolicy,
        bool assistantCapabilityQuestion,
        bool assistantRuntimeIntrospectionQuestion,
        RuntimeSelfReportDetectionSource runtimeSelfReportDetectionSource = RuntimeSelfReportDetectionSource.None) {
        return SelectCapabilitySelfKnowledgeLines(
            sessionPolicy,
            toolCatalogPacks: null,
            toolCatalogPlugins: null,
            toolCatalogRoutingCatalog: null,
            toolCatalogCapabilitySnapshot: null,
            toolCatalogTools: null,
            assistantCapabilityQuestion,
            assistantRuntimeIntrospectionQuestion,
            runtimeSelfReportDetectionSource);
    }

    internal static IReadOnlyList<string>? SelectCapabilitySelfKnowledgeLines(
        SessionPolicyDto? sessionPolicy,
        IReadOnlyList<ToolPackInfoDto>? toolCatalogPacks,
        IReadOnlyList<PluginInfoDto>? toolCatalogPlugins,
        SessionRoutingCatalogDiagnosticsDto? toolCatalogRoutingCatalog,
        SessionCapabilitySnapshotDto? toolCatalogCapabilitySnapshot,
        IReadOnlyCollection<ToolDefinitionDto>? toolCatalogTools,
        bool assistantCapabilityQuestion,
        bool assistantRuntimeIntrospectionQuestion,
        RuntimeSelfReportDetectionSource runtimeSelfReportDetectionSource = RuntimeSelfReportDetectionSource.None) {
        if (assistantCapabilityQuestion) {
            return BuildCapabilitySelfKnowledgeLines(
                sessionPolicy,
                toolCatalogPacks,
                toolCatalogPlugins,
                toolCatalogRoutingCatalog,
                toolCatalogCapabilitySnapshot,
                toolCatalogExecutionSummary: null,
                toolCatalogTools: toolCatalogTools,
                runtimeIntrospectionMode: false,
                runtimeSelfReportDetectionSource: runtimeSelfReportDetectionSource);
        }

        if (assistantRuntimeIntrospectionQuestion) {
            return BuildCapabilitySelfKnowledgeLines(
                sessionPolicy,
                toolCatalogPacks,
                toolCatalogPlugins,
                toolCatalogRoutingCatalog,
                toolCatalogCapabilitySnapshot,
                toolCatalogExecutionSummary: null,
                toolCatalogTools: toolCatalogTools,
                runtimeIntrospectionMode: true,
                runtimeSelfReportDetectionSource: runtimeSelfReportDetectionSource);
        }

        return null;
    }

    internal static IReadOnlyList<string>? SelectCapabilitySelfKnowledgeLines(
        SessionPolicyDto? sessionPolicy,
        IReadOnlyList<ToolPackInfoDto>? toolCatalogPacks,
        SessionRoutingCatalogDiagnosticsDto? toolCatalogRoutingCatalog,
        SessionCapabilitySnapshotDto? toolCatalogCapabilitySnapshot,
        IReadOnlyCollection<ToolDefinitionDto>? toolCatalogTools,
        bool assistantCapabilityQuestion,
        bool assistantRuntimeIntrospectionQuestion,
        RuntimeSelfReportDetectionSource runtimeSelfReportDetectionSource = RuntimeSelfReportDetectionSource.None) {
        return SelectCapabilitySelfKnowledgeLines(
            sessionPolicy,
            toolCatalogPacks,
            toolCatalogPlugins: null,
            toolCatalogRoutingCatalog,
            toolCatalogCapabilitySnapshot,
            toolCatalogTools,
            assistantCapabilityQuestion,
            assistantRuntimeIntrospectionQuestion,
            runtimeSelfReportDetectionSource);
    }

    internal static bool ShouldUseCompactRuntimeCapabilityContext(
        bool assistantRuntimeIntrospectionQuestion,
        bool compactAssistantRuntimeIntrospectionQuestion,
        RuntimeSelfReportDetectionSource runtimeSelfReportDetectionSource = RuntimeSelfReportDetectionSource.None) {
        if (!assistantRuntimeIntrospectionQuestion) {
            return false;
        }

        if (compactAssistantRuntimeIntrospectionQuestion) {
            return true;
        }

        return runtimeSelfReportDetectionSource == RuntimeSelfReportDetectionSource.LexicalFallback;
    }

    internal static bool ShouldIncludeLiveProfileUpdates(
        bool hasUserNameUpdate,
        bool hasAssistantPersonaUpdate,
        bool hasThemePresetUpdate) {
        return hasUserNameUpdate || hasAssistantPersonaUpdate || hasThemePresetUpdate;
    }

    internal static bool ShouldUseThinServiceRequestEnvelope(
        bool includeOnboardingContext,
        bool includeLiveProfileUpdates) {
        return !includeOnboardingContext && !includeLiveProfileUpdates;
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
