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

                var text = EasyChatResult.FromTurn(turn).Text ?? string.Empty;
                var controlPayloadDetected = isLocalCompatibleLoopback && LooksLikeRuntimeControlPayloadArtifact(text);
                if (controlPayloadDetected) {
                    text = string.Empty;
                } else if (!string.IsNullOrWhiteSpace(text)) {
                    lastNonEmptyAssistantDraft = text.Trim();
                }

                if (!autoPendingActionReplayUsed
                    && toolCalls.Count == 0
                    && toolOutputs.Count == 0
                    && LooksLikeContinuationFollowUp(userRequest)
                    && TryBuildSinglePendingActionSelectionPayload(text, out var autoSelectionPayload, out var autoActionId)) {
                    autoPendingActionReplayUsed = true;
                    routedUserRequest = autoSelectionPayload;
                    executionContractApplies = ShouldEnforceExecuteOrExplainContract(routedUserRequest);

                    turn = await RunModelPhaseWithProgressAsync(
                            client,
                            writer,
                            request.RequestId,
                            threadId,
                            ChatInput.FromText(autoSelectionPayload),
                            CopyChatOptions(options, newThreadOverride: false),
                            turnToken,
                            phaseStatus: planExecuteReviewLoop ? ChatStatusCodes.PhaseExecute : ChatStatusCodes.Thinking,
                            phaseMessage: $"Executing follow-up action {autoActionId} directly.",
                            heartbeatLabel: "Executing selected action",
                            heartbeatSeconds: modelHeartbeatSeconds)
                        .ConfigureAwait(false);
                    return ContinueRound();
                }

                if (!hostStructuredNextActionReplayUsed
                    && ShouldAttemptCarryoverStructuredNextActionReplay(
                        continuationFollowUpTurn: continuationFollowUpTurn,
                        compactFollowUpTurn: compactFollowUpTurn,
                        userRequest: routedUserRequest,
                        assistantDraft: text)
                    && toolCalls.Count == 0
                    && toolOutputs.Count == 0) {
                    var carryoverHostHintInput = BuildCarryoverHostHintInput(routedUserRequest, text);
                    if (TryBuildCarryoverStructuredNextActionToolCall(
                        threadId: threadId,
                        userRequest: carryoverHostHintInput,
                        toolDefinitions: fullToolDefs.Length > 0 ? fullToolDefs : toolDefs,
                        mutatingToolHintsByName: mutatingToolHints,
                        out var carryoverStructuredNextActionCall,
                        out var carryoverStructuredNextActionReason)) {
                        hostStructuredNextActionReplayUsed = true;
                        RemoveStructuredNextActionCarryover(threadId);
                        if (fullToolDefs.Length > 0 && toolDefs.Count != fullToolDefs.Length) {
                            toolDefs = fullToolDefs;
                            options.Tools = fullToolDefs;
                            options.ToolChoice = ToolChoice.Auto;
                            usedContinuationSubset = false;
                            RememberWeightedToolSubset(threadId, toolDefs, originalToolCount);
                        }

                        Trace.WriteLine(
                            $"[host-structured-next-action] outcome=execute reason={carryoverStructuredNextActionReason} continuation={continuationFollowUpTurn} tool={carryoverStructuredNextActionCall.Name} prior_calls={toolCalls.Count} prior_outputs={toolOutputs.Count}");

                        toolRounds++;
                        var carryoverHostRoundNumber = round + 1;
                        await WriteToolRoundStartedStatusAsync(
                                writer,
                                request.RequestId,
                                threadId,
                                carryoverHostRoundNumber,
                                maxRounds,
                                1,
                                parallelTools,
                                allowMutatingParallel)
                            .ConfigureAwait(false);
                        if (planExecuteReviewLoop) {
                            await TryWriteStatusAsync(
                                    writer,
                                    request.RequestId,
                                    threadId,
                                    status: ChatStatusCodes.PhaseExecute,
                                    message: $"Executing queued read-only follow-up action ({carryoverStructuredNextActionCall.Name})...")
                                .ConfigureAwait(false);
                        }

                        await TryWriteStatusAsync(
                                writer,
                                request.RequestId,
                                threadId,
                                status: ChatStatusCodes.ToolCall,
                                toolName: carryoverStructuredNextActionCall.Name,
                                toolCallId: carryoverStructuredNextActionCall.CallId)
                            .ConfigureAwait(false);
                        toolCalls.Add(new ToolCallDto {
                            CallId = carryoverStructuredNextActionCall.CallId,
                            Name = carryoverStructuredNextActionCall.Name,
                            ArgumentsJson = carryoverStructuredNextActionCall.Arguments is null
                                ? "{}"
                                : JsonLite.Serialize(carryoverStructuredNextActionCall.Arguments)
                        });

                        var carryoverHostCalls = new[] { carryoverStructuredNextActionCall };
                        var carryoverHostOutputs = await ExecuteToolsAsync(
                                writer,
                                request.RequestId,
                                threadId,
                                carryoverHostCalls,
                                parallel: false,
                                allowMutatingParallel: allowMutatingParallel,
                                mutatingToolHintsByName: mutatingToolHints,
                                toolTimeoutSeconds: toolTimeoutSeconds,
                                userRequest: routedUserRequest,
                                cancellationToken: turnToken)
                            .ConfigureAwait(false);
                        var carryoverHostFailedCalls = CountFailedToolOutputs(carryoverHostOutputs);
                        await WriteToolRoundCompletedStatusAsync(
                                writer,
                                request.RequestId,
                                threadId,
                                carryoverHostRoundNumber,
                                maxRounds,
                                carryoverHostOutputs.Count,
                                carryoverHostFailedCalls)
                            .ConfigureAwait(false);
                        UpdateToolRoutingStats(carryoverHostCalls, carryoverHostOutputs);
                        foreach (var output in carryoverHostOutputs) {
                            if (WasProjectionFallbackApplied(output)) {
                                projectionFallbackCount++;
                            }

                            toolOutputs.Add(new ToolOutputDto {
                                CallId = output.CallId,
                                Output = output.Output,
                                Ok = output.Ok,
                                ErrorCode = output.ErrorCode,
                                Error = output.Error,
                                Hints = output.Hints,
                                IsTransient = output.IsTransient,
                                SummaryMarkdown = output.SummaryMarkdown,
                                MetaJson = output.MetaJson,
                                RenderJson = output.RenderJson,
                                FailureJson = output.FailureJson
                            });
                        }

                        var carryoverHostNextInput = BuildHostReplayReviewInput(
                            carryoverStructuredNextActionCall,
                            carryoverHostOutputs,
                            supportsSyntheticHostReplayItems);
                        turn = await RunModelPhaseWithProgressAsync(
                                client,
                                writer,
                                request.RequestId,
                                threadId,
                                carryoverHostNextInput,
                                CopyChatOptions(options, newThreadOverride: false),
                                turnToken,
                                phaseStatus: planExecuteReviewLoop ? ChatStatusCodes.PhaseReview : ChatStatusCodes.Thinking,
                                phaseMessage: "Reviewing queued follow-up action results...",
                                heartbeatLabel: "Reviewing queued action",
                                heartbeatSeconds: modelHeartbeatSeconds)
                            .ConfigureAwait(false);
                        return ContinueRound();
                    }
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
                    priorToolCalls: toolCalls.Count,
                    priorToolOutputs: toolOutputs.Count,
                    userRequest: routedUserRequest,
                    assistantDraft: text);
                if (suppressLocalToolRecoveryRetries) {
                    executionNudgeReason = "local_runtime_recovery_disabled";
                } else if (!executionNudgeUsed) {
                    if (LooksLikeExecutionAcknowledgeDraft(text)
                        && AssistantDraftReferencesUserRequest(routedUserRequest, text)) {
                        shouldAttemptExecutionNudge = true;
                        executionNudgeReason = "execution_ack_draft_direct_retry";
                    } else {
                        shouldAttemptExecutionNudge = EvaluateToolExecutionNudgeDecision(
                            userRequest: routedUserRequest,
                            assistantDraft: text,
                            toolsAvailable: toolDefs.Count > 0,
                            priorToolCalls: toolCalls.Count,
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
                        userRequest: routedUserRequest,
                        usedContinuationSubset: usedContinuationSubset,
                        toolsAvailable: toolDefs.Count > 0,
                        priorToolCalls: toolCalls.Count,
                        assistantDraftToolCalls: extracted.Count,
                        executionNudgeAlreadyUsed: executionNudgeUsed,
                        shouldAttemptNudge: shouldAttemptExecutionNudge,
                        reason: executionNudgeReason);
                    executionNudgeUsed = true;
                    var nudgePrompt = BuildToolExecutionNudgePrompt(routedUserRequest, text);
                    turn = await RunModelPhaseWithProgressAsync(
                            client,
                            writer,
                            request.RequestId,
                            threadId,
                            ChatInput.FromText(nudgePrompt),
                            CopyChatOptions(options, newThreadOverride: false),
                            turnToken,
                            phaseStatus: planExecuteReviewLoop ? ChatStatusCodes.PhasePlan : ChatStatusCodes.Thinking,
                            phaseMessage: "Re-planning to execute available tools in this turn.",
                            heartbeatLabel: "Re-planning execution",
                            heartbeatSeconds: modelHeartbeatSeconds)
                        .ConfigureAwait(false);
                    return ContinueRound();
                }

                TraceToolExecutionNudgeDecision(
                    userRequest: routedUserRequest,
                    usedContinuationSubset: usedContinuationSubset,
                    toolsAvailable: toolDefs.Count > 0,
                    priorToolCalls: toolCalls.Count,
                    assistantDraftToolCalls: extracted.Count,
                    executionNudgeAlreadyUsed: executionNudgeUsed,
                    shouldAttemptNudge: false,
                    reason: executionNudgeReason);

                if (!suppressLocalToolRecoveryRetries
                    && !toolReceiptCorrectionUsed
                    && ShouldAttemptToolReceiptCorrection(
                        userRequest: routedUserRequest,
                        assistantDraft: text,
                        tools: toolDefs,
                        priorToolCalls: toolCalls.Count,
                        priorToolOutputs: toolOutputs.Count,
                        assistantDraftToolCalls: extracted.Count)) {
                    toolReceiptCorrectionUsed = true;
                    var correctionPrompt = BuildToolReceiptCorrectionPrompt(routedUserRequest, text);
                    turn = await RunModelPhaseWithProgressAsync(
                            client,
                            writer,
                            request.RequestId,
                            threadId,
                            ChatInput.FromText(correctionPrompt),
                            CopyChatOptions(options, newThreadOverride: false),
                            turnToken,
                            phaseStatus: planExecuteReviewLoop ? ChatStatusCodes.PhasePlan : ChatStatusCodes.Thinking,
                            phaseMessage: "Re-planning to correct an inconsistent tool receipt in this turn.",
                            heartbeatLabel: "Re-planning tool receipt",
                            heartbeatSeconds: modelHeartbeatSeconds)
                        .ConfigureAwait(false);
                    return ContinueRound();
                }

                var shouldAttemptWatchdog = false;
                noToolExecutionWatchdogReason = "not_evaluated";
                if (suppressLocalToolRecoveryRetries) {
                    noToolExecutionWatchdogReason = "local_runtime_recovery_disabled";
                } else {
                    shouldAttemptWatchdog = ShouldAttemptNoToolExecutionWatchdog(
                        userRequest: routedUserRequest,
                        assistantDraft: text,
                        toolsAvailable: toolDefs.Count > 0,
                        priorToolCalls: toolCalls.Count,
                        priorToolOutputs: toolOutputs.Count,
                        assistantDraftToolCalls: extracted.Count,
                        continuationFollowUpTurn: continuationFollowUpTurn,
                        compactFollowUpTurn: compactFollowUpTurn,
                        executionNudgeUsed: executionNudgeUsed,
                        toolReceiptCorrectionUsed: toolReceiptCorrectionUsed,
                        watchdogAlreadyUsed: noToolExecutionWatchdogUsed,
                        out noToolExecutionWatchdogReason);
                }
                TraceNoToolExecutionWatchdogDecision(
                    userRequest: routedUserRequest,
                    executionContractApplies: executionContractApplies,
                    toolsAvailable: toolDefs.Count > 0,
                    priorToolCalls: toolCalls.Count,
                    priorToolOutputs: toolOutputs.Count,
                    assistantDraftToolCalls: extracted.Count,
                    continuationFollowUpTurn: continuationFollowUpTurn,
                    compactFollowUpTurn: compactFollowUpTurn,
                    executionNudgeUsed: executionNudgeUsed,
                    toolReceiptCorrectionUsed: toolReceiptCorrectionUsed,
                    watchdogAlreadyUsed: noToolExecutionWatchdogUsed,
                    shouldRetry: shouldAttemptWatchdog,
                    reason: noToolExecutionWatchdogReason);
                if (shouldAttemptWatchdog) {
                    noToolExecutionWatchdogUsed = true;
                    var watchdogPrompt = BuildNoToolExecutionWatchdogPrompt(routedUserRequest, text);
                    turn = await RunModelPhaseWithProgressAsync(
                            client,
                            writer,
                            request.RequestId,
                            threadId,
                            ChatInput.FromText(watchdogPrompt),
                            CopyChatOptions(options, newThreadOverride: false),
                            turnToken,
                            phaseStatus: planExecuteReviewLoop ? ChatStatusCodes.PhaseReview : ChatStatusCodes.Thinking,
                            phaseMessage: "Re-validating tool execution for this turn.",
                            heartbeatLabel: "Re-validating execution",
                            heartbeatSeconds: modelHeartbeatSeconds)
                        .ConfigureAwait(false);
                    return ContinueRound();
                }

                var hasToolActivity = toolCalls.Count > 0 || toolOutputs.Count > 0;
                if (executionContractApplies
                    && !hasToolActivity
                    && !executionContractEscapeUsed
                    && fullToolDefs.Length > 0) {
                    executionContractEscapeUsed = true;
                    toolDefs = fullToolDefs;
                    options.Tools = fullToolDefs;
                    options.ToolChoice = ToolChoice.Auto;
                    usedContinuationSubset = false;
                    RememberWeightedToolSubset(threadId, toolDefs, originalToolCount);

                    var escapePrompt = BuildExecutionContractEscapePrompt(routedUserRequest, text);
                    turn = await RunModelPhaseWithProgressAsync(
                            client,
                            writer,
                            request.RequestId,
                            threadId,
                            ChatInput.FromText(escapePrompt),
                            CopyChatOptions(options, newThreadOverride: false),
                            turnToken,
                            phaseStatus: planExecuteReviewLoop ? ChatStatusCodes.PhasePlan : ChatStatusCodes.Thinking,
                            phaseMessage: "Selected action had no tool activity; retrying with full tool availability.",
                            heartbeatLabel: "Re-planning with full tools",
                            heartbeatSeconds: modelHeartbeatSeconds)
                        .ConfigureAwait(false);
                    return ContinueRound();
                }

                var shouldAttemptContinuationSubsetEscape = ShouldAttemptContinuationSubsetEscape(
                    executionContractApplies: executionContractApplies,
                    usedContinuationSubset: usedContinuationSubset,
                    continuationSubsetEscapeUsed: continuationSubsetEscapeUsed,
                    toolsAvailable: fullToolDefs.Length > 0,
                    priorToolCalls: toolCalls.Count,
                    priorToolOutputs: toolOutputs.Count,
                    out _);
                if (!suppressLocalToolRecoveryRetries && shouldAttemptContinuationSubsetEscape) {
                    continuationSubsetEscapeUsed = true;
                    toolDefs = fullToolDefs;
                    options.Tools = fullToolDefs;
                    options.ToolChoice = ToolChoice.Auto;
                    usedContinuationSubset = false;
                    RememberWeightedToolSubset(threadId, toolDefs, originalToolCount);

                    var subsetEscapePrompt = BuildContinuationSubsetEscapePrompt(routedUserRequest, text);
                    turn = await RunModelPhaseWithProgressAsync(
                            client,
                            writer,
                            request.RequestId,
                            threadId,
                            ChatInput.FromText(subsetEscapePrompt),
                            CopyChatOptions(options, newThreadOverride: false),
                            turnToken,
                            phaseStatus: planExecuteReviewLoop ? ChatStatusCodes.PhasePlan : ChatStatusCodes.Thinking,
                            phaseMessage: "Follow-up subset had no tool activity; retrying with full tool availability.",
                            heartbeatLabel: "Expanding follow-up tools",
                            heartbeatSeconds: modelHeartbeatSeconds)
                        .ConfigureAwait(false);
                    return ContinueRound();
                }

                if (!hostDomainIntentBootstrapReplayUsed
                    && !executionContractApplies
                    && toolCalls.Count == 0
                    && toolOutputs.Count == 0
                    && TryBuildHostDomainIntentEnvironmentBootstrapCall(
                        threadId: threadId,
                        userRequest: routedUserRequest,
                        toolDefinitions: fullToolDefs.Length > 0 ? fullToolDefs : toolDefs,
                        out var hostDomainBootstrapCall,
                        out var hostDomainBootstrapReason)) {
                    hostDomainIntentBootstrapReplayUsed = true;
                    if (fullToolDefs.Length > 0 && toolDefs.Count != fullToolDefs.Length) {
                        toolDefs = fullToolDefs;
                        options.Tools = fullToolDefs;
                        options.ToolChoice = ToolChoice.Auto;
                        usedContinuationSubset = false;
                        RememberWeightedToolSubset(threadId, toolDefs, originalToolCount);
                    }

                    Trace.WriteLine(
                        $"[host-domain-bootstrap] outcome=execute reason={hostDomainBootstrapReason} continuation={continuationFollowUpTurn} tool={hostDomainBootstrapCall.Name} prior_calls={toolCalls.Count} prior_outputs={toolOutputs.Count}");

                    toolRounds++;
                    var hostDomainBootstrapRoundNumber = round + 1;
                    await WriteToolRoundStartedStatusAsync(
                            writer,
                            request.RequestId,
                            threadId,
                            hostDomainBootstrapRoundNumber,
                            maxRounds,
                            1,
                            parallelTools,
                            allowMutatingParallel)
                        .ConfigureAwait(false);
                    if (planExecuteReviewLoop) {
                        await TryWriteStatusAsync(
                                writer,
                                request.RequestId,
                                threadId,
                                status: ChatStatusCodes.PhaseExecute,
                                message: $"Executing domain-scope bootstrap ({hostDomainBootstrapCall.Name})...")
                            .ConfigureAwait(false);
                    }

                    await TryWriteStatusAsync(
                            writer,
                            request.RequestId,
                            threadId,
                            status: ChatStatusCodes.ToolCall,
                            toolName: hostDomainBootstrapCall.Name,
                            toolCallId: hostDomainBootstrapCall.CallId)
                        .ConfigureAwait(false);
                    toolCalls.Add(new ToolCallDto {
                        CallId = hostDomainBootstrapCall.CallId,
                        Name = hostDomainBootstrapCall.Name,
                        ArgumentsJson = hostDomainBootstrapCall.Arguments is null
                            ? "{}"
                            : JsonLite.Serialize(hostDomainBootstrapCall.Arguments)
                    });

                    var hostDomainBootstrapCalls = new[] { hostDomainBootstrapCall };
                    var hostDomainBootstrapOutputs = await ExecuteToolsAsync(
                            writer,
                            request.RequestId,
                            threadId,
                            hostDomainBootstrapCalls,
                            parallel: false,
                            allowMutatingParallel: allowMutatingParallel,
                            mutatingToolHintsByName: mutatingToolHints,
                            toolTimeoutSeconds: toolTimeoutSeconds,
                            userRequest: routedUserRequest,
                            cancellationToken: turnToken)
                        .ConfigureAwait(false);
                    var hostDomainBootstrapFailedCalls = CountFailedToolOutputs(hostDomainBootstrapOutputs);
                    await WriteToolRoundCompletedStatusAsync(
                            writer,
                            request.RequestId,
                            threadId,
                            hostDomainBootstrapRoundNumber,
                            maxRounds,
                            hostDomainBootstrapOutputs.Count,
                            hostDomainBootstrapFailedCalls)
                        .ConfigureAwait(false);
                    UpdateToolRoutingStats(hostDomainBootstrapCalls, hostDomainBootstrapOutputs);
                    RememberSuccessfulPackPreflightCalls(threadId, hostDomainBootstrapCalls, hostDomainBootstrapOutputs);
                    foreach (var output in hostDomainBootstrapOutputs) {
                        if (WasProjectionFallbackApplied(output)) {
                            projectionFallbackCount++;
                        }

                        toolOutputs.Add(new ToolOutputDto {
                            CallId = output.CallId,
                            Output = output.Output,
                            Ok = output.Ok,
                            ErrorCode = output.ErrorCode,
                            Error = output.Error,
                            Hints = output.Hints,
                            IsTransient = output.IsTransient,
                            SummaryMarkdown = output.SummaryMarkdown,
                            MetaJson = output.MetaJson,
                            RenderJson = output.RenderJson,
                            FailureJson = output.FailureJson
                        });
                    }

                    var hostDomainBootstrapNextInput = BuildHostReplayReviewInput(
                        hostDomainBootstrapCall,
                        hostDomainBootstrapOutputs,
                        supportsSyntheticHostReplayItems);
                    turn = await RunModelPhaseWithProgressAsync(
                            client,
                            writer,
                            request.RequestId,
                            threadId,
                            hostDomainBootstrapNextInput,
                            CopyChatOptions(options, newThreadOverride: false),
                            turnToken,
                            phaseStatus: planExecuteReviewLoop ? ChatStatusCodes.PhaseReview : ChatStatusCodes.Thinking,
                            phaseMessage: "Reviewing domain-scope bootstrap results...",
                            heartbeatLabel: "Reviewing domain bootstrap",
                            heartbeatSeconds: modelHeartbeatSeconds)
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
}
