using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Json;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.Tools;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private async Task<NoExtractedToolRoundOutcome> HandleNoExtractedToolCallsRecoveryAsync(
        IntelligenceXClient client,
        StreamWriter writer,
        ChatRequest request,
        string threadId,
        int round,
        int maxRounds,
        bool parallelTools,
        bool allowMutatingParallel,
        bool planExecuteReviewLoop,
        int modelHeartbeatSeconds,
        int toolTimeoutSeconds,
        bool continuationFollowUpTurn,
        bool compactFollowUpTurn,
        bool isLocalCompatibleLoopback,
        bool supportsSyntheticHostReplayItems,
        string userRequest,
        ToolDefinition[] fullToolDefs,
        IReadOnlyDictionary<string, bool> mutatingToolHints,
        int originalToolCount,
        List<ToolCallDto> toolCalls,
        List<ToolOutputDto> toolOutputs,
        IReadOnlyList<ToolCall> extracted,
        CancellationToken turnToken,
        NoExtractedToolRoundState state) {
        var turn = state.Turn;
        var routedUserRequest = state.RoutedUserRequest;
        var executionContractApplies = state.ExecutionContractApplies;
        var toolDefs = state.ToolDefs;
        var options = state.Options;
        var usedContinuationSubset = state.UsedContinuationSubset;
        var toolRounds = state.ToolRounds;
        var projectionFallbackCount = state.ProjectionFallbackCount;
        var executionNudgeUsed = state.ExecutionNudgeUsed;
        var toolReceiptCorrectionUsed = state.ToolReceiptCorrectionUsed;
        var noToolExecutionWatchdogUsed = state.NoToolExecutionWatchdogUsed;
        var noToolExecutionWatchdogReason = state.NoToolExecutionWatchdogReason;
        var executionContractEscapeUsed = state.ExecutionContractEscapeUsed;
        var continuationSubsetEscapeUsed = state.ContinuationSubsetEscapeUsed;
        var autoPendingActionReplayUsed = state.AutoPendingActionReplayUsed;
        var hostStructuredNextActionReplayUsed = state.HostStructuredNextActionReplayUsed;
        var hostDomainIntentBootstrapReplayUsed = state.HostDomainIntentBootstrapReplayUsed;
        var lastNonEmptyAssistantDraft = state.LastNonEmptyAssistantDraft;
        var nudgeUnknownEnvelopeReplanCount = state.NudgeUnknownEnvelopeReplanCount;
        var noTextRecoveryHitCount = state.NoTextRecoveryHitCount;
        var noTextToolOutputRecoveryHitCount = state.NoTextToolOutputRecoveryHitCount;
        var proactiveSkipMutatingCount = state.ProactiveSkipMutatingCount;
        var proactiveSkipReadOnlyCount = state.ProactiveSkipReadOnlyCount;
        var proactiveSkipUnknownCount = state.ProactiveSkipUnknownCount;
        var visibleUserRequest = string.IsNullOrWhiteSpace(userRequest) ? routedUserRequest : userRequest;
        var bootstrapOnlyToolActivity = hostDomainIntentBootstrapReplayUsed
            && HasOnlyHostGeneratedPackPreflightToolActivity(toolCalls, toolOutputs);
        var priorToolCallsForRecoveryRetry = bootstrapOnlyToolActivity ? 0 : toolCalls.Count;
        var priorToolOutputsForRecoveryRetry = bootstrapOnlyToolActivity ? 0 : toolOutputs.Count;

                var text = EasyChatResult.FromTurn(turn).Text ?? string.Empty;
                var controlPayloadDetected = isLocalCompatibleLoopback && LooksLikeRuntimeControlPayloadArtifact(text);
                if (controlPayloadDetected) {
                    text = string.Empty;
                } else if (!string.IsNullOrWhiteSpace(text)) {
                    lastNonEmptyAssistantDraft = text.Trim();
                }

                var explicitToolReferenceInUserRequest = ExtractExplicitRequestedToolNames(userRequest).Length > 0;
                var prePromptExecutionDecision = ResolveNoExtractedRecoveryPrePromptExecutionDecision(
                    threadId: threadId,
                    userRequest: userRequest,
                    assistantDraft: text,
                    toolDefinitions: fullToolDefs.Length > 0 ? fullToolDefs : toolDefs,
                    mutatingToolHintsByName: mutatingToolHints,
                    continuationFollowUpTurn: continuationFollowUpTurn,
                    compactFollowUpTurn: compactFollowUpTurn,
                    autoPendingActionReplayUsed: autoPendingActionReplayUsed,
                    hostStructuredNextActionReplayUsed: hostStructuredNextActionReplayUsed,
                    explicitToolReferenceInUserRequest: explicitToolReferenceInUserRequest,
                    priorToolCalls: toolCalls.Count,
                    priorToolOutputs: toolOutputs.Count);
                if (prePromptExecutionDecision.Kind != NoExtractedRecoveryExecutionDecisionKind.None) {
                    if (prePromptExecutionDecision.Kind == NoExtractedRecoveryExecutionDecisionKind.AutoPendingActionReplay) {
                        autoPendingActionReplayUsed = true;
                        routedUserRequest = prePromptExecutionDecision.ReplayPayload;
                        executionContractApplies = ShouldEnforceExecuteOrExplainContract(routedUserRequest);
                    } else if (prePromptExecutionDecision.Kind == NoExtractedRecoveryExecutionDecisionKind.CarryoverStructuredNextActionReplay) {
                        hostStructuredNextActionReplayUsed = true;
                        RemoveStructuredNextActionCarryover(threadId);
                    }

                    if (prePromptExecutionDecision.ExpandToFullToolAvailability
                        && fullToolDefs.Length > 0
                        && toolDefs.Count != fullToolDefs.Length) {
                        toolDefs = ExpandToFullToolAvailabilityForPromptExposure(routedUserRequest, fullToolDefs, options);
                        usedContinuationSubset = false;
                        RememberWeightedToolSubset(threadId, toolDefs, originalToolCount);
                    }

                    (turn, toolRounds, projectionFallbackCount) = await ApplyNoExtractedRecoveryExecutionDecisionAsync(
                            client,
                            writer,
                            request,
                            threadId,
                            continuationFollowUpTurn,
                            round,
                            maxRounds,
                            parallelTools,
                            allowMutatingParallel,
                            planExecuteReviewLoop,
                            modelHeartbeatSeconds,
                            toolTimeoutSeconds,
                            supportsSyntheticHostReplayItems,
                            routedUserRequest,
                            options,
                            mutatingToolHints,
                            toolCalls,
                            toolOutputs,
                            toolRounds,
                            projectionFallbackCount,
                            turnToken,
                            prePromptExecutionDecision)
                        .ConfigureAwait(false);
                    return ContinueRound();
                }

                var shouldAttemptExecutionNudge = false;
                var executionNudgeReason = executionNudgeUsed
                    ? "execution_nudge_already_used"
                    : "execution_nudge_not_evaluated";
                var suppressLocalToolRecoveryRetries = ShouldSuppressLocalToolRecoveryRetries(
                    isLocalCompatibleLoopback: isLocalCompatibleLoopback,
                    executionContractApplies: executionContractApplies,
                    compactFollowUpTurn: compactFollowUpTurn,
                    toolsAvailable: toolDefs.Count > 0 || fullToolDefs.Length > 0,
                    priorToolCalls: priorToolCallsForRecoveryRetry,
                    priorToolOutputs: priorToolOutputsForRecoveryRetry,
                    userRequest: visibleUserRequest,
                    assistantDraft: text);
                if (suppressLocalToolRecoveryRetries) {
                    executionNudgeReason = "local_runtime_recovery_disabled";
                } else if (!executionNudgeUsed) {
                    if (LooksLikeExecutionAcknowledgeDraft(text)
                        && AssistantDraftReferencesUserRequest(visibleUserRequest, text)) {
                        shouldAttemptExecutionNudge = true;
                        executionNudgeReason = "execution_ack_draft_direct_retry";
                    } else {
                        shouldAttemptExecutionNudge = EvaluateToolExecutionNudgeDecision(
                            userRequest: visibleUserRequest,
                            assistantDraft: text,
                            toolsAvailable: toolDefs.Count > 0,
                            priorToolCalls: priorToolCallsForRecoveryRetry,
                            assistantDraftToolCalls: extracted.Count,
                            usedContinuationSubset: usedContinuationSubset,
                            compactFollowUpHint: compactFollowUpTurn,
                            out executionNudgeReason);
                    }
                }
                if (string.Equals(Environment.GetEnvironmentVariable("IX_CHAT_TRACE_TOOL_NUDGE"), "1", StringComparison.Ordinal)) {
                    Console.Error.WriteLine(
                        $"[tool-nudge-eval] suppress={suppressLocalToolRecoveryRetries} should={shouldAttemptExecutionNudge} reason={executionNudgeReason} prior_calls={toolCalls.Count} draft_calls={extracted.Count} tools={toolDefs.Count} subset={usedContinuationSubset}");
                }

                if (shouldAttemptExecutionNudge) {
                    if (string.Equals(executionNudgeReason, "single_unknown_pending_action_envelope", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(executionNudgeReason, "structured_draft_single_unknown_pending_action_envelope", StringComparison.OrdinalIgnoreCase)) {
                        nudgeUnknownEnvelopeReplanCount++;
                    }

                    TraceToolExecutionNudgeDecision(
                        userRequest: visibleUserRequest,
                        usedContinuationSubset: usedContinuationSubset,
                        toolsAvailable: toolDefs.Count > 0,
                        priorToolCalls: priorToolCallsForRecoveryRetry,
                        assistantDraftToolCalls: extracted.Count,
                        executionNudgeAlreadyUsed: executionNudgeUsed,
                        shouldAttemptNudge: shouldAttemptExecutionNudge,
                        reason: executionNudgeReason);
                    executionNudgeUsed = true;
                    var nudgePrompt = BuildToolExecutionNudgePrompt(visibleUserRequest, text, toolDefs);
                    var nudgeOptions = await CopyChatOptionsWithPromptAwareToolOrderingAndEmitStatusAsync(
                            writer,
                            request.RequestId,
                            threadId,
                            options,
                            nudgePrompt,
                            strategy: "prompt_recovery",
                            newThreadOverride: false)
                        .ConfigureAwait(false);
                    turn = await RunModelPhaseWithProgressAsync(
                            client,
                            writer,
                            request.RequestId,
                            threadId,
                            ChatInput.FromText(nudgePrompt),
                            nudgeOptions,
                            turnToken,
                            phaseStatus: planExecuteReviewLoop ? ChatStatusCodes.PhasePlan : ChatStatusCodes.Thinking,
                            phaseMessage: "Re-planning to execute available tools in this turn.",
                            heartbeatLabel: "Re-planning execution",
                            heartbeatSeconds: modelHeartbeatSeconds)
                        .ConfigureAwait(false);
                    return ContinueRound();
                }

                TraceToolExecutionNudgeDecision(
                    userRequest: visibleUserRequest,
                    usedContinuationSubset: usedContinuationSubset,
                    toolsAvailable: toolDefs.Count > 0,
                    priorToolCalls: priorToolCallsForRecoveryRetry,
                    assistantDraftToolCalls: extracted.Count,
                    executionNudgeAlreadyUsed: executionNudgeUsed,
                    shouldAttemptNudge: false,
                    reason: executionNudgeReason);

                var shouldAttemptToolReceiptCorrection = !suppressLocalToolRecoveryRetries
                                                        && !toolReceiptCorrectionUsed
                                                        && ShouldAttemptToolReceiptCorrection(
                                                            userRequest: visibleUserRequest,
                                                            assistantDraft: text,
                                                            tools: toolDefs,
                                                            priorToolCalls: priorToolCallsForRecoveryRetry,
                                                            priorToolOutputs: priorToolOutputsForRecoveryRetry,
                                                            assistantDraftToolCalls: extracted.Count);

                var shouldAttemptWatchdog = false;
                noToolExecutionWatchdogReason = "not_evaluated";
                if (suppressLocalToolRecoveryRetries) {
                    noToolExecutionWatchdogReason = "local_runtime_recovery_disabled";
                } else {
                    shouldAttemptWatchdog = ShouldAttemptNoToolExecutionWatchdog(
                        userRequest: visibleUserRequest,
                        assistantDraft: text,
                        toolsAvailable: toolDefs.Count > 0,
                        priorToolCalls: priorToolCallsForRecoveryRetry,
                        priorToolOutputs: priorToolOutputsForRecoveryRetry,
                        assistantDraftToolCalls: extracted.Count,
                        continuationFollowUpTurn: continuationFollowUpTurn,
                        compactFollowUpTurn: compactFollowUpTurn,
                        executionNudgeUsed: executionNudgeUsed,
                        toolReceiptCorrectionUsed: toolReceiptCorrectionUsed,
                        watchdogAlreadyUsed: noToolExecutionWatchdogUsed,
                        out noToolExecutionWatchdogReason);
                }
                TraceNoToolExecutionWatchdogDecision(
                    userRequest: visibleUserRequest,
                    executionContractApplies: executionContractApplies,
                    toolsAvailable: toolDefs.Count > 0,
                    priorToolCalls: priorToolCallsForRecoveryRetry,
                    priorToolOutputs: priorToolOutputsForRecoveryRetry,
                    assistantDraftToolCalls: extracted.Count,
                    continuationFollowUpTurn: continuationFollowUpTurn,
                    compactFollowUpTurn: compactFollowUpTurn,
                    executionNudgeUsed: executionNudgeUsed,
                    toolReceiptCorrectionUsed: toolReceiptCorrectionUsed,
                    watchdogAlreadyUsed: noToolExecutionWatchdogUsed,
                    shouldRetry: shouldAttemptWatchdog,
                    reason: noToolExecutionWatchdogReason);
                var hasToolActivity = toolCalls.Count > 0 || toolOutputs.Count > 0;
                var shouldAttemptContinuationSubsetEscape = ShouldAttemptContinuationSubsetEscape(
                    executionContractApplies: executionContractApplies,
                    usedContinuationSubset: usedContinuationSubset,
                    continuationSubsetEscapeUsed: continuationSubsetEscapeUsed,
                    toolsAvailable: fullToolDefs.Length > 0,
                    priorToolCalls: toolCalls.Count,
                    priorToolOutputs: toolOutputs.Count,
                    out var continuationSubsetEscapeReason);

                var promptRecoveryDecision = ResolveNoExtractedPromptRecoveryDecision(
                    suppressLocalToolRecoveryRetries: suppressLocalToolRecoveryRetries,
                    shouldAttemptExecutionNudge: shouldAttemptExecutionNudge,
                    executionNudgeReason: executionNudgeReason,
                    shouldAttemptToolReceiptCorrection: shouldAttemptToolReceiptCorrection,
                    shouldAttemptWatchdog: shouldAttemptWatchdog,
                    noToolExecutionWatchdogReason: noToolExecutionWatchdogReason,
                    executionContractApplies: executionContractApplies,
                    hasToolActivity: hasToolActivity,
                    executionContractEscapeUsed: executionContractEscapeUsed,
                    fullToolsAvailable: fullToolDefs.Length > 0,
                    shouldAttemptContinuationSubsetEscape: shouldAttemptContinuationSubsetEscape,
                    continuationSubsetEscapeReason: continuationSubsetEscapeReason);
                if (promptRecoveryDecision.Kind != NoExtractedPromptRecoveryDecisionKind.None) {
                    switch (promptRecoveryDecision.Kind) {
                        case NoExtractedPromptRecoveryDecisionKind.ExecutionNudge:
                            if (ShouldCountUnknownPendingActionEnvelopeNudge(promptRecoveryDecision.Reason)) {
                                nudgeUnknownEnvelopeReplanCount++;
                            }

                            executionNudgeUsed = true;
                            break;
                        case NoExtractedPromptRecoveryDecisionKind.ToolReceiptCorrection:
                            toolReceiptCorrectionUsed = true;
                            break;
                        case NoExtractedPromptRecoveryDecisionKind.ExecutionWatchdog:
                            noToolExecutionWatchdogUsed = true;
                            break;
                        case NoExtractedPromptRecoveryDecisionKind.ExecutionContractEscape:
                            executionContractEscapeUsed = true;
                            break;
                        case NoExtractedPromptRecoveryDecisionKind.ContinuationSubsetEscape:
                            continuationSubsetEscapeUsed = true;
                            break;
                    }

                    if (promptRecoveryDecision.ExpandToFullToolAvailability) {
                        toolDefs = ExpandToFullToolAvailabilityForPromptExposure(routedUserRequest, fullToolDefs, options);
                        usedContinuationSubset = false;
                        RememberWeightedToolSubset(threadId, toolDefs, originalToolCount);
                    }

                    turn = await ApplyNoExtractedPromptRecoveryDecisionAsync(
                            client,
                            writer,
                            request,
                            threadId,
                            options,
                            turnToken,
                            planExecuteReviewLoop,
                            modelHeartbeatSeconds,
                            visibleUserRequest,
                            text,
                            toolDefs,
                            promptRecoveryDecision)
                        .ConfigureAwait(false);
                    return ContinueRound();
                }

                var postPromptExecutionDecision = ResolveNoExtractedRecoveryPostPromptExecutionDecision(
                    threadId: threadId,
                    visibleUserRequest: visibleUserRequest,
                    routedUserRequest: routedUserRequest,
                    toolDefinitions: fullToolDefs.Length > 0 ? fullToolDefs : toolDefs,
                    executionContractApplies: executionContractApplies,
                    hostDomainIntentBootstrapReplayUsed: hostDomainIntentBootstrapReplayUsed,
                    bootstrapOnlyToolActivity: bootstrapOnlyToolActivity,
                    priorToolCalls: toolCalls.Count,
                    priorToolOutputs: toolOutputs.Count);
                if (postPromptExecutionDecision.Kind != NoExtractedRecoveryExecutionDecisionKind.None) {
                    hostDomainIntentBootstrapReplayUsed = true;
                    if (postPromptExecutionDecision.Kind == NoExtractedRecoveryExecutionDecisionKind.HostDomainIntentBootstrapReplay) {
                        // Host bootstrap establishes scope but should not consume the pre-bootstrap
                        // empty-draft recovery budget for the now-scoped operational follow-up round.
                        executionNudgeUsed = false;
                        noToolExecutionWatchdogUsed = false;
                        noToolExecutionWatchdogReason = "not_evaluated";
                    }

                    if (postPromptExecutionDecision.ExpandToFullToolAvailability
                        && fullToolDefs.Length > 0
                        && toolDefs.Count != fullToolDefs.Length) {
                        toolDefs = ExpandToFullToolAvailabilityForPromptExposure(routedUserRequest, fullToolDefs, options);
                        usedContinuationSubset = false;
                        RememberWeightedToolSubset(threadId, toolDefs, originalToolCount);
                    }

                    (turn, toolRounds, projectionFallbackCount) = await ApplyNoExtractedRecoveryExecutionDecisionAsync(
                            client,
                            writer,
                            request,
                            threadId,
                            continuationFollowUpTurn,
                            round,
                            maxRounds,
                            parallelTools,
                            allowMutatingParallel,
                            planExecuteReviewLoop,
                            modelHeartbeatSeconds,
                            toolTimeoutSeconds,
                            supportsSyntheticHostReplayItems,
                            routedUserRequest,
                            options,
                            mutatingToolHints,
                            toolCalls,
                            toolOutputs,
                            toolRounds,
                            projectionFallbackCount,
                            turnToken,
                            postPromptExecutionDecision)
                        .ConfigureAwait(false);
                    return ContinueRound();
                }


        PersistState();
        return NoExtractedToolRoundOutcome.ProceedToFinalize();

        NoExtractedToolRoundOutcome ContinueRound() {
            PersistState();
            return NoExtractedToolRoundOutcome.ContinueRound();
        }

        void PersistState() {
            state.Turn = turn;
            state.AssistantDraft = text;
            state.ControlPayloadDetected = controlPayloadDetected;
            state.RoutedUserRequest = routedUserRequest;
            state.ExecutionContractApplies = executionContractApplies;
            state.ToolDefs = toolDefs;
            state.Options = options;
            state.UsedContinuationSubset = usedContinuationSubset;
            state.ToolRounds = toolRounds;
            state.ProjectionFallbackCount = projectionFallbackCount;
            state.ExecutionNudgeUsed = executionNudgeUsed;
            state.ToolReceiptCorrectionUsed = toolReceiptCorrectionUsed;
            state.NoToolExecutionWatchdogUsed = noToolExecutionWatchdogUsed;
            state.NoToolExecutionWatchdogReason = noToolExecutionWatchdogReason;
            state.ExecutionContractEscapeUsed = executionContractEscapeUsed;
            state.ContinuationSubsetEscapeUsed = continuationSubsetEscapeUsed;
            state.AutoPendingActionReplayUsed = autoPendingActionReplayUsed;
            state.HostStructuredNextActionReplayUsed = hostStructuredNextActionReplayUsed;
            state.HostDomainIntentBootstrapReplayUsed = hostDomainIntentBootstrapReplayUsed;
            state.LastNonEmptyAssistantDraft = lastNonEmptyAssistantDraft;
            state.NudgeUnknownEnvelopeReplanCount = nudgeUnknownEnvelopeReplanCount;
            state.NoTextRecoveryHitCount = noTextRecoveryHitCount;
            state.NoTextToolOutputRecoveryHitCount = noTextToolOutputRecoveryHitCount;
            state.ProactiveSkipMutatingCount = proactiveSkipMutatingCount;
            state.ProactiveSkipReadOnlyCount = proactiveSkipReadOnlyCount;
            state.ProactiveSkipUnknownCount = proactiveSkipUnknownCount;
        }
    }

    private static bool HasOnlyHostGeneratedPackPreflightToolActivity(
        IReadOnlyList<ToolCallDto> toolCalls,
        IReadOnlyList<ToolOutputDto> toolOutputs) {
        var sawActivity = false;

        for (var i = 0; i < toolCalls.Count; i++) {
            if (!IsHostGeneratedPackPreflightCallId(toolCalls[i]?.CallId ?? string.Empty)) {
                return false;
            }

            sawActivity = true;
        }

        for (var i = 0; i < toolOutputs.Count; i++) {
            if (!IsHostGeneratedPackPreflightCallId(toolOutputs[i]?.CallId ?? string.Empty)) {
                return false;
            }

            sawActivity = true;
        }

        return sawActivity;
    }
}
