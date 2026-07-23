using System;
using System.Collections.Generic;
using IntelligenceX.Chat.Abstractions;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.App.Conversation;
using IntelligenceX.Chat.App.Markdown;

namespace IntelligenceX.Chat.App;

public sealed partial class MainWindow {
    private string BuildRequestTextForService(string userText) {
        var activeConversation = GetActiveConversation();
        var effectivePersona = GetEffectiveAssistantPersona();
        var effectiveName = GetEffectiveUserName();
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
        IReadOnlyList<string> missingFields = includeOnboardingContext ? BuildMissingOnboardingFields() : Array.Empty<string>();
        var localContextLines = BuildLocalContextFallbackLines(activeConversation, userText);
        var conversationStyleLines = ConversationStyleGuidanceBuilder.BuildRecentUserStyleLines(activeConversation.Messages);
        var capabilityAnswerStyleLines = assistantCapabilityQuestion
            ? ConversationStyleGuidanceBuilder.BuildCapabilityAnswerStyleLines(activeConversation.Messages)
            : null;
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

        return DesktopChatTurnProtocol.BuildRequestText(new DesktopChatTurnPromptContext {
            UserText = userText,
            EffectiveUserName = effectiveName,
            EffectiveAssistantPersona = effectivePersona,
            IncludeOnboardingContext = includeOnboardingContext,
            MissingOnboardingFields = missingFields,
            LocalContextLines = localContextLines,
            ConversationStyleLines = conversationStyleLines,
            CapabilityAnswerStyleLines = capabilityAnswerStyleLines,
            ContinuationStateLines = continuationStateLines,
            RecentAssistantAnswerWasSubstantive = recentAssistantAnswerWasSubstantive,
            RecentAssistantAskedQuestion = recentAssistantAskedQuestion,
            PersistentMemoryLines = memoryContextLines,
            PersistentMemoryEnabled = _persistentMemoryEnabled,
            CapabilitySelfKnowledgeLines = capabilitySelfKnowledgeLines,
            RuntimeCapabilityLines = runtimeCapabilityLines,
            ProactiveExecutionEnabled = proactiveExecutionEnabled,
            RuntimeSelfReportAnalysis = runtimeSelfReportAnalysis
        });
    }

    internal static bool? ResolveProactiveExecutionGuidanceMode(
        bool proactiveModeEnabled,
        string? userText,
        bool assistantCapabilityQuestion,
        bool assistantRuntimeIntrospectionQuestion,
        bool recentAssistantAskedQuestion) {
        return DesktopChatTurnProtocol.ResolveProactiveExecutionGuidanceMode(
            proactiveModeEnabled,
            userText,
            assistantCapabilityQuestion,
            assistantRuntimeIntrospectionQuestion,
            recentAssistantAskedQuestion);
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

    internal static bool ShouldUseThinServiceRequestEnvelope(
        bool includeOnboardingContext,
        bool includeLiveProfileUpdates) =>
        DesktopChatTurnProtocol.ShouldUseThinRequestEnvelope(includeOnboardingContext, includeLiveProfileUpdates);

    internal static bool ShouldIncludeAmbientOnboardingContext(
        string? userText,
        bool onboardingInProgress,
        bool assistantCapabilityQuestion,
        bool assistantRuntimeIntrospectionQuestion) {
        return DesktopChatTurnProtocol.ShouldIncludeAmbientOnboardingContext(
            userText,
            onboardingInProgress,
            assistantCapabilityQuestion,
            assistantRuntimeIntrospectionQuestion);
    }

    internal static bool ShouldIncludeProactiveExecutionMode(
        string? userText,
        bool assistantCapabilityQuestion,
        bool assistantRuntimeIntrospectionQuestion,
        bool recentAssistantAskedQuestion) {
        return DesktopChatTurnProtocol.ShouldIncludeProactiveExecutionMode(
            userText,
            assistantCapabilityQuestion,
            assistantRuntimeIntrospectionQuestion,
            recentAssistantAskedQuestion);
    }
}
