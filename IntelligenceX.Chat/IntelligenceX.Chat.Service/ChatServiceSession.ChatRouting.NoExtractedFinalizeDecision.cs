using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.Tools;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private enum NoExtractedFinalizeContinuationDecisionKind {
        None = 0,
        StructuredNextActionRetry,
        ToolProgressRecovery
    }

    private readonly record struct NoExtractedFinalizeContinuationDecision(
        NoExtractedFinalizeContinuationDecisionKind Kind,
        string Reason,
        string Prompt,
        bool ExpandToFullToolAvailability,
        string? PreferredToolName) {
        public static NoExtractedFinalizeContinuationDecision None(string reason) =>
            new(NoExtractedFinalizeContinuationDecisionKind.None, reason, string.Empty, ExpandToFullToolAvailability: false, PreferredToolName: null);

        public static NoExtractedFinalizeContinuationDecision StructuredNextActionRetry(string reason, string prompt, string? preferredToolName) =>
            new(NoExtractedFinalizeContinuationDecisionKind.StructuredNextActionRetry, reason, prompt, ExpandToFullToolAvailability: true, preferredToolName);

        public static NoExtractedFinalizeContinuationDecision ToolProgressRecovery(string reason, string prompt) =>
            new(NoExtractedFinalizeContinuationDecisionKind.ToolProgressRecovery, reason, prompt, ExpandToFullToolAvailability: true, PreferredToolName: null);
    }

    private enum NoExtractedFinalizeReviewDecisionKind {
        None = 0,
        ResponseQualityReview,
        ProactiveFollowUpReview
    }

    private readonly record struct NoExtractedFinalizeReviewDecision(
        NoExtractedFinalizeReviewDecisionKind Kind,
        string Reason,
        string Prompt,
        int ReviewPassNumber) {
        public static NoExtractedFinalizeReviewDecision None(string reason) =>
            new(NoExtractedFinalizeReviewDecisionKind.None, reason, string.Empty, ReviewPassNumber: 0);

        public static NoExtractedFinalizeReviewDecision ResponseQualityReview(string reason, string prompt, int reviewPassNumber) =>
            new(NoExtractedFinalizeReviewDecisionKind.ResponseQualityReview, reason, prompt, reviewPassNumber);

        public static NoExtractedFinalizeReviewDecision ProactiveFollowUpReview(string reason, string prompt) =>
            new(NoExtractedFinalizeReviewDecisionKind.ProactiveFollowUpReview, reason, prompt, ReviewPassNumber: 0);
    }

    private enum NoExtractedFinalizeNoTextDecisionKind {
        None = 0,
        ToolOutputSynthesisRetry,
        LocalDirectRetry
    }

    private readonly record struct NoExtractedFinalizeNoTextDecision(
        NoExtractedFinalizeNoTextDecisionKind Kind,
        string Reason,
        string Prompt) {
        public static NoExtractedFinalizeNoTextDecision None(string reason) =>
            new(NoExtractedFinalizeNoTextDecisionKind.None, reason, string.Empty);

        public static NoExtractedFinalizeNoTextDecision ToolOutputSynthesisRetry(string reason, string prompt) =>
            new(NoExtractedFinalizeNoTextDecisionKind.ToolOutputSynthesisRetry, reason, prompt);

        public static NoExtractedFinalizeNoTextDecision LocalDirectRetry(string reason, string prompt) =>
            new(NoExtractedFinalizeNoTextDecisionKind.LocalDirectRetry, reason, prompt);
    }

    private static NoExtractedFinalizeContinuationDecision ResolveNoExtractedFinalizeContinuationDecision(
        bool structuredNextActionRetryUsed,
        IReadOnlyList<ToolDefinition> structuredNextActionToolDefs,
        IReadOnlyList<ToolCallDto> toolCalls,
        IReadOnlyList<ToolOutputDto> toolOutputs,
        bool continuationFollowUpTurn,
        string userRequest,
        string assistantDraft,
        bool hasStructuredNextAction,
        string? structuredNextToolName,
        IReadOnlyList<ToolDefinition> activeToolDefinitions,
        bool toolsAvailable,
        int assistantDraftToolCalls,
        bool toolProgressRecoveryUsed) {
        if (!structuredNextActionRetryUsed
            && TryBuildStructuredNextActionRetryPrompt(
                toolDefinitions: structuredNextActionToolDefs,
                toolCalls: toolCalls,
                toolOutputs: toolOutputs,
                continuationFollowUpTurn: continuationFollowUpTurn,
                userRequest: userRequest,
                assistantDraft: assistantDraft,
                out var structuredNextActionPrompt,
                out var structuredNextActionReason)) {
            var preferredToolName = hasStructuredNextAction
                                    && !string.IsNullOrWhiteSpace(structuredNextToolName)
                                    && activeToolDefinitions.Count > 0
                                    && ContainsToolDefinition(activeToolDefinitions, structuredNextToolName)
                ? structuredNextToolName
                : null;
            return NoExtractedFinalizeContinuationDecision.StructuredNextActionRetry(
                structuredNextActionReason,
                structuredNextActionPrompt,
                preferredToolName);
        }

        if (ShouldAttemptToolProgressRecovery(
                continuationFollowUpTurn: continuationFollowUpTurn,
                assistantDraft: assistantDraft,
                toolsAvailable: toolsAvailable,
                priorToolCalls: toolCalls.Count,
                priorToolOutputs: toolOutputs.Count,
                assistantDraftToolCalls: assistantDraftToolCalls,
                progressRecoveryAlreadyUsed: toolProgressRecoveryUsed,
                out var toolProgressRecoveryReason)) {
            return NoExtractedFinalizeContinuationDecision.ToolProgressRecovery(
                toolProgressRecoveryReason,
                BuildToolProgressRecoveryPrompt(
                    userRequest: userRequest,
                    assistantDraft: assistantDraft,
                    toolCalls: toolCalls));
        }

        return NoExtractedFinalizeContinuationDecision.None("no_finalize_continuation_selected");
    }

    private static bool ContainsToolDefinition(IReadOnlyList<ToolDefinition> toolDefinitions, string toolName) {
        for (var i = 0; i < toolDefinitions.Count; i++) {
            if (string.Equals(toolDefinitions[i].Name, toolName, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

    private async Task<TurnInfo> ApplyNoExtractedFinalizeContinuationDecisionAsync(
        IntelligenceXClient client,
        StreamWriter writer,
        ChatRequest request,
        string threadId,
        ChatOptions options,
        CancellationToken turnToken,
        bool planExecuteReviewLoop,
        int modelHeartbeatSeconds,
        NoExtractedFinalizeContinuationDecision decision) {
        var phaseStatus = ChatStatusCodes.Thinking;
        var phaseMessage = string.Empty;
        var heartbeatLabel = string.Empty;
        var retryOptions = CopyChatOptions(options, newThreadOverride: false);

        switch (decision.Kind) {
            case NoExtractedFinalizeContinuationDecisionKind.StructuredNextActionRetry:
                phaseStatus = planExecuteReviewLoop ? ChatStatusCodes.PhasePlan : ChatStatusCodes.Thinking;
                phaseMessage = "Continuing with tool-recommended next action.";
                heartbeatLabel = "Executing next action";
                if (!string.IsNullOrWhiteSpace(decision.PreferredToolName)) {
                    retryOptions.ToolChoice = ToolChoice.Custom(decision.PreferredToolName);
                }
                break;
            case NoExtractedFinalizeContinuationDecisionKind.ToolProgressRecovery:
                phaseStatus = planExecuteReviewLoop ? ChatStatusCodes.PhasePlan : ChatStatusCodes.Thinking;
                phaseMessage = "Continuing execution after blocker-style draft.";
                heartbeatLabel = "Recovering execution progress";
                break;
            default:
                throw new InvalidOperationException("Finalize continuation decision must be non-empty before applying.");
        }

        var promptOptions = await CopyChatOptionsWithPromptAwareToolOrderingAndEmitStatusAsync(
                writer,
                request.RequestId,
                threadId,
                retryOptions,
                decision.Prompt,
                strategy: "prompt_continuation",
                newThreadOverride: false)
            .ConfigureAwait(false);
        return await RunModelPhaseWithProgressAsync(
                client,
                writer,
                request.RequestId,
                threadId,
                ChatInput.FromText(decision.Prompt),
                promptOptions,
                turnToken,
                phaseStatus: phaseStatus,
                phaseMessage: phaseMessage,
                heartbeatLabel: heartbeatLabel,
                heartbeatSeconds: modelHeartbeatSeconds)
            .ConfigureAwait(false);
    }

    private static NoExtractedFinalizeReviewDecision ResolveNoExtractedFinalizeReviewDecision(
        bool noResultWatchdogTriggered,
        bool planExecuteReviewLoop,
        int maxReviewPasses,
        int reviewPassesUsed,
        TurnExecutionIntent turnExecutionIntent,
        string userRequest,
        string assistantDraft,
        TurnAnswerPlan answerPlan,
        bool executionContractApplies,
        bool hasToolActivity,
        ProactiveFollowUpReviewDecision proactiveDecision,
        IReadOnlyList<ToolOutputDto> toolOutputs) {
        if (string.IsNullOrWhiteSpace(assistantDraft)) {
            return NoExtractedFinalizeReviewDecision.None("empty_assistant_draft");
        }

        if (turnExecutionIntent.RequestedArtifact.RequiresArtifact
            && !IsRequestedArtifactRequirementSatisfied(turnExecutionIntent.RequestedArtifact, assistantDraft, answerPlan)) {
            return NoExtractedFinalizeReviewDecision.ProactiveFollowUpReview(
                "allow_requested_artifact_missing",
                BuildProactiveFollowUpReviewPrompt(userRequest, assistantDraft, toolOutputs));
        }

        if (!noResultWatchdogTriggered
            && planExecuteReviewLoop
            && ShouldAttemptResponseQualityReview(
                userRequest: userRequest,
                assistantDraft: assistantDraft,
                executionContractApplies: executionContractApplies,
                hasToolActivity: hasToolActivity,
                reviewPassesUsed: reviewPassesUsed,
                maxReviewPasses: maxReviewPasses)) {
            var nextReviewPass = reviewPassesUsed + 1;
            var rememberedExecutionBackends = ReadRememberedToolExecutionBackendHintsFromRequestText(userRequest);
            return NoExtractedFinalizeReviewDecision.ResponseQualityReview(
                reason: "response_quality_review",
                prompt: BuildResponseQualityReviewPrompt(
                    userRequest: userRequest,
                    assistantDraft: assistantDraft,
                    hasToolActivity: hasToolActivity,
                    reviewPassNumber: nextReviewPass,
                    maxReviewPasses: maxReviewPasses,
                    rememberedExecutionBackends: rememberedExecutionBackends),
                reviewPassNumber: nextReviewPass);
        }

        if (!noResultWatchdogTriggered && proactiveDecision.ShouldAttempt) {
            return NoExtractedFinalizeReviewDecision.ProactiveFollowUpReview(
                proactiveDecision.Reason,
                BuildProactiveFollowUpReviewPrompt(userRequest, assistantDraft, toolOutputs));
        }

        return NoExtractedFinalizeReviewDecision.None(
            noResultWatchdogTriggered
                ? "no_result_watchdog_triggered"
                : planExecuteReviewLoop
                    ? "no_finalize_review_selected"
                    : "review_loop_disabled");
    }

    private async Task<TurnInfo> ApplyNoExtractedFinalizeReviewDecisionAsync(
        IntelligenceXClient client,
        StreamWriter writer,
        ChatRequest request,
        string threadId,
        ChatOptions options,
        CancellationToken turnToken,
        int maxReviewPasses,
        int modelHeartbeatSeconds,
        NoExtractedFinalizeReviewDecision decision) {
        return decision.Kind switch {
            NoExtractedFinalizeReviewDecisionKind.ResponseQualityReview => await RunReviewOnlyModelPhaseWithProgressAsync(
                    client,
                    writer,
                    request.RequestId,
                    threadId,
                    ChatInput.FromText(decision.Prompt),
                    await CopyChatOptionsWithPromptAwareToolOrderingAndEmitStatusAsync(
                            writer,
                            request.RequestId,
                            threadId,
                            options,
                            decision.Prompt,
                            strategy: "prompt_review",
                            newThreadOverride: false)
                        .ConfigureAwait(false),
                    turnToken,
                    phaseStatus: ChatStatusCodes.PhaseReview,
                    phaseMessage: $"Reviewing response quality ({decision.ReviewPassNumber}/{maxReviewPasses})...",
                    heartbeatLabel: "Reviewing response",
                    heartbeatSeconds: modelHeartbeatSeconds)
                .ConfigureAwait(false),
            NoExtractedFinalizeReviewDecisionKind.ProactiveFollowUpReview => await RunReviewOnlyModelPhaseWithProgressAsync(
                    client,
                    writer,
                    request.RequestId,
                    threadId,
                    ChatInput.FromText(decision.Prompt),
                    await CopyChatOptionsWithPromptAwareToolOrderingAndEmitStatusAsync(
                            writer,
                            request.RequestId,
                            threadId,
                            options,
                            decision.Prompt,
                            strategy: "prompt_review",
                            newThreadOverride: false)
                        .ConfigureAwait(false),
                    turnToken,
                    phaseStatus: ChatStatusCodes.PhaseReview,
                    phaseMessage: "Generating proactive next checks and fixes...",
                    heartbeatLabel: "Preparing proactive follow-up",
                    heartbeatSeconds: modelHeartbeatSeconds)
                .ConfigureAwait(false),
            _ => throw new InvalidOperationException("Finalize review decision must be non-empty before applying.")
        };
    }

    private static NoExtractedFinalizeNoTextDecision ResolveNoExtractedFinalizeNoTextDecision(
        bool noTextToolOutputDirectRetryUsed,
        bool planExecuteReviewLoop,
        bool redactEnabled,
        bool hasSuccessfulToolOutput,
        IReadOnlyList<ToolCallDto> toolCalls,
        IReadOnlyList<ToolOutputDto> toolOutputs,
        string assistantDraft,
        bool localNoTextDirectRetryUsed,
        bool isLocalCompatibleLoopback,
        int availableToolCount,
        int priorToolCalls,
        string userRequest) {
        if (!noTextToolOutputDirectRetryUsed
            && planExecuteReviewLoop
            && !redactEnabled
            && hasSuccessfulToolOutput
            && toolOutputs.Count > 0
            && string.IsNullOrWhiteSpace(assistantDraft)) {
            return NoExtractedFinalizeNoTextDecision.ToolOutputSynthesisRetry(
                "tool_output_synthesis_retry",
                BuildNoTextToolOutputSynthesisPrompt(
                    userRequest: userRequest,
                    toolCalls: toolCalls,
                    toolOutputs: toolOutputs));
        }

        if (string.IsNullOrWhiteSpace(assistantDraft)
            && !localNoTextDirectRetryUsed
            && isLocalCompatibleLoopback
            && availableToolCount > 0
            && priorToolCalls == 0
            && toolOutputs.Count == 0) {
            return NoExtractedFinalizeNoTextDecision.LocalDirectRetry(
                "local_no_text_direct_retry",
                BuildCompatibleRuntimeNoTextDirectRetryPrompt(userRequest));
        }

        return NoExtractedFinalizeNoTextDecision.None("no_no_text_recovery_selected");
    }

    private static (string AssistantDraft, NoExtractedFinalizeNoTextDecision Decision) ResolveNoExtractedFinalizeNoTextOutcome(
        bool noTextToolOutputDirectRetryUsed,
        bool planExecuteReviewLoop,
        bool redactEnabled,
        bool hasSuccessfulToolOutput,
        IReadOnlyList<ToolCallDto> toolCalls,
        IReadOnlyList<ToolOutputDto> toolOutputs,
        string assistantDraft,
        bool localNoTextDirectRetryUsed,
        bool isLocalCompatibleLoopback,
        int availableToolCount,
        int priorToolCalls,
        string userRequest) {
        var recoveredAssistantDraft = ResolveAssistantTextFromToolOutputsFallback(
            assistantDraft: assistantDraft,
            toolCalls: toolCalls,
            toolOutputs: toolOutputs);
        var decision = ResolveNoExtractedFinalizeNoTextDecision(
            noTextToolOutputDirectRetryUsed: noTextToolOutputDirectRetryUsed,
            planExecuteReviewLoop: planExecuteReviewLoop,
            redactEnabled: redactEnabled,
            hasSuccessfulToolOutput: hasSuccessfulToolOutput,
            toolCalls: toolCalls,
            toolOutputs: toolOutputs,
            assistantDraft: assistantDraft,
            localNoTextDirectRetryUsed: localNoTextDirectRetryUsed,
            isLocalCompatibleLoopback: isLocalCompatibleLoopback,
            availableToolCount: availableToolCount,
            priorToolCalls: priorToolCalls,
            userRequest: userRequest);
        return (recoveredAssistantDraft, decision);
    }

    private async Task<TurnInfo> ApplyNoExtractedFinalizeNoTextDecisionAsync(
        IntelligenceXClient client,
        StreamWriter writer,
        ChatRequest request,
        string threadId,
        ChatOptions options,
        CancellationToken turnToken,
        bool planExecuteReviewLoop,
        int modelHeartbeatSeconds,
        bool controlPayloadDetected,
        NoExtractedFinalizeNoTextDecision decision) {
        return decision.Kind switch {
            NoExtractedFinalizeNoTextDecisionKind.ToolOutputSynthesisRetry => await RunReviewOnlyModelPhaseWithProgressAsync(
                    client,
                    writer,
                    request.RequestId,
                    threadId,
                    ChatInput.FromText(decision.Prompt),
                    CopyChatOptionsWithoutTools(options, newThreadOverride: false),
                    turnToken,
                    phaseStatus: planExecuteReviewLoop ? ChatStatusCodes.PhaseReview : ChatStatusCodes.Thinking,
                    phaseMessage: "Tool execution completed without narrative. Synthesizing findings...",
                    heartbeatLabel: "Synthesizing findings",
                    heartbeatSeconds: modelHeartbeatSeconds)
                .ConfigureAwait(false),
            NoExtractedFinalizeNoTextDecisionKind.LocalDirectRetry => await RunModelPhaseWithProgressAsync(
                    client,
                    writer,
                    request.RequestId,
                    threadId,
                    ChatInput.FromText(decision.Prompt),
                    CopyChatOptionsWithoutTools(options, newThreadOverride: false),
                    turnToken,
                    phaseStatus: ChatStatusCodes.PhaseReview,
                    phaseMessage: controlPayloadDetected
                        ? "Retrying direct response after runtime control-payload artifact..."
                        : "Retrying response in direct mode (without tools)...",
                    heartbeatLabel: "Retrying direct response",
                    heartbeatSeconds: modelHeartbeatSeconds)
                .ConfigureAwait(false),
            _ => throw new InvalidOperationException("Finalize no-text decision must be non-empty before applying.")
        };
    }

    internal static (string Kind, string Reason, bool ExpandToFullToolAvailability, string? PreferredToolName) ResolveNoExtractedFinalizeContinuationDecisionForTesting(
        IReadOnlyList<ToolDefinition> toolDefinitions,
        IReadOnlyList<ToolCallDto> toolCalls,
        IReadOnlyList<ToolOutputDto> toolOutputs,
        bool continuationFollowUpTurn,
        string userRequest,
        string assistantDraft,
        bool structuredNextActionRetryUsed,
        bool toolProgressRecoveryUsed,
        int assistantDraftToolCalls) {
        var hasStructuredNextAction = TryExtractStructuredNextAction(
            toolDefinitions,
            toolCalls,
            toolOutputs,
            out _,
            out var structuredNextToolName,
            out _,
            out _,
            out _,
            out _);
        var decision = ResolveNoExtractedFinalizeContinuationDecision(
            structuredNextActionRetryUsed: structuredNextActionRetryUsed,
            structuredNextActionToolDefs: toolDefinitions,
            toolCalls: toolCalls,
            toolOutputs: toolOutputs,
            continuationFollowUpTurn: continuationFollowUpTurn,
            userRequest: userRequest,
            assistantDraft: assistantDraft,
            hasStructuredNextAction: hasStructuredNextAction,
            structuredNextToolName: structuredNextToolName,
            activeToolDefinitions: toolDefinitions,
            toolsAvailable: toolDefinitions.Count > 0,
            assistantDraftToolCalls: assistantDraftToolCalls,
            toolProgressRecoveryUsed: toolProgressRecoveryUsed);
        return (decision.Kind.ToString(), decision.Reason, decision.ExpandToFullToolAvailability, decision.PreferredToolName);
    }

    internal static (string Kind, string Reason, int ReviewPassNumber) ResolveNoExtractedFinalizeReviewDecisionForTesting(
        bool noResultWatchdogTriggered,
        bool planExecuteReviewLoop,
        int maxReviewPasses,
        int reviewPassesUsed,
        string userRequest,
        string assistantDraft,
        bool executionContractApplies,
        bool hasToolActivity,
        bool proactiveModeEnabled,
        bool proactiveFollowUpUsed,
        bool continuationFollowUpTurn,
        bool compactFollowUpTurn) {
        var turnExecutionIntent = ResolveTurnExecutionIntent(
            userRequest: userRequest,
            continuationFollowUpTurn: continuationFollowUpTurn,
            compactFollowUpTurn: compactFollowUpTurn,
            hasPendingActionContext: false,
            hasToolActivity: hasToolActivity,
            startupBootstrapCompleted: true,
            startupBootstrapCompletedSuccessfully: true,
            hasCachedToolCatalog: false,
            servingPersistedPreview: false);
        var reviewedDraft = ResolveReviewedAssistantDraft(assistantDraft);
        var proactiveDecision = ResolveProactiveFollowUpReviewDecision(
            proactiveModeEnabled: proactiveModeEnabled,
            hasToolActivity: hasToolActivity,
            proactiveFollowUpUsed: proactiveFollowUpUsed,
            continuationFollowUpTurn: continuationFollowUpTurn,
            compactFollowUpTurn: compactFollowUpTurn,
            userRequest: userRequest,
            assistantDraft: assistantDraft,
            answerPlanOverride: reviewedDraft.AnswerPlan);
        var decision = ResolveNoExtractedFinalizeReviewDecision(
            noResultWatchdogTriggered: noResultWatchdogTriggered,
            planExecuteReviewLoop: planExecuteReviewLoop,
            maxReviewPasses: maxReviewPasses,
            reviewPassesUsed: reviewPassesUsed,
            turnExecutionIntent: turnExecutionIntent,
            userRequest: userRequest,
            assistantDraft: reviewedDraft.VisibleText,
            answerPlan: reviewedDraft.AnswerPlan,
            executionContractApplies: executionContractApplies,
            hasToolActivity: hasToolActivity,
            proactiveDecision: proactiveDecision,
            toolOutputs: Array.Empty<ToolOutputDto>());
        return (decision.Kind.ToString(), decision.Reason, decision.ReviewPassNumber);
    }

    internal static (string Kind, string Reason) ResolveNoExtractedFinalizeNoTextDecisionForTesting(
        bool noTextToolOutputDirectRetryUsed,
        bool planExecuteReviewLoop,
        bool redactEnabled,
        bool hasSuccessfulToolOutput,
        int toolOutputsCount,
        string assistantDraft,
        bool localNoTextDirectRetryUsed,
        bool isLocalCompatibleLoopback,
        int availableToolCount,
        int priorToolCalls,
        string userRequest) {
        var toolOutputs = new List<ToolOutputDto>(toolOutputsCount);
        for (var i = 0; i < toolOutputsCount; i++) {
            toolOutputs.Add(new ToolOutputDto {
                CallId = $"call_{i + 1}",
                Ok = hasSuccessfulToolOutput,
                Output = $"output_{i + 1}"
            });
        }

        var decision = ResolveNoExtractedFinalizeNoTextDecision(
            noTextToolOutputDirectRetryUsed: noTextToolOutputDirectRetryUsed,
            planExecuteReviewLoop: planExecuteReviewLoop,
            redactEnabled: redactEnabled,
            hasSuccessfulToolOutput: hasSuccessfulToolOutput,
            toolCalls: Array.Empty<ToolCallDto>(),
            toolOutputs: toolOutputs,
            assistantDraft: assistantDraft,
            localNoTextDirectRetryUsed: localNoTextDirectRetryUsed,
            isLocalCompatibleLoopback: isLocalCompatibleLoopback,
            availableToolCount: availableToolCount,
            priorToolCalls: priorToolCalls,
            userRequest: userRequest);
        return (decision.Kind.ToString(), decision.Reason);
    }

    internal static (string AssistantDraft, string Kind, string Reason) ResolveNoExtractedFinalizeNoTextOutcomeForTesting(
        bool noTextToolOutputDirectRetryUsed,
        bool planExecuteReviewLoop,
        bool redactEnabled,
        bool hasSuccessfulToolOutput,
        string assistantDraft,
        bool localNoTextDirectRetryUsed,
        bool isLocalCompatibleLoopback,
        int availableToolCount,
        int priorToolCalls,
        string userRequest) {
        var toolCalls = new List<ToolCallDto> {
            new() {
                CallId = "call_1",
                Name = "search_query"
            }
        };
        var toolOutputs = new List<ToolOutputDto> {
            new() {
                CallId = "call_1",
                Ok = hasSuccessfulToolOutput,
                Output = "Found 3 relevant results."
            }
        };

        var outcome = ResolveNoExtractedFinalizeNoTextOutcome(
            noTextToolOutputDirectRetryUsed: noTextToolOutputDirectRetryUsed,
            planExecuteReviewLoop: planExecuteReviewLoop,
            redactEnabled: redactEnabled,
            hasSuccessfulToolOutput: hasSuccessfulToolOutput,
            toolCalls: toolCalls,
            toolOutputs: toolOutputs,
            assistantDraft: assistantDraft,
            localNoTextDirectRetryUsed: localNoTextDirectRetryUsed,
            isLocalCompatibleLoopback: isLocalCompatibleLoopback,
            availableToolCount: availableToolCount,
            priorToolCalls: priorToolCalls,
            userRequest: userRequest);
        return (outcome.AssistantDraft, outcome.Decision.Kind.ToString(), outcome.Decision.Reason);
    }
}
