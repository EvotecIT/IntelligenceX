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
    private async Task<NoExtractedToolRoundOutcome> HandleNoExtractedToolCallsFinalizeAsync(
        IntelligenceXClient client,
        StreamWriter writer,
        ChatRequest request,
        string threadId,
        int round,
        int maxRounds,
        bool parallelTools,
        bool allowMutatingParallel,
        bool planExecuteReviewLoop,
        int maxReviewPasses,
        int modelHeartbeatSeconds,
        int toolTimeoutSeconds,
        bool continuationFollowUpTurn,
        bool compactFollowUpTurn,
        bool proactiveModeEnabled,
        bool isLocalCompatibleLoopback,
        bool supportsSyntheticHostReplayItems,
        string? resolvedModel,
        string userIntent,
        ToolDefinition[] fullToolDefs,
        IReadOnlyDictionary<string, bool> mutatingToolHints,
        int originalToolCount,
        List<ToolCallDto> toolCalls,
        List<ToolOutputDto> toolOutputs,
        IReadOnlyList<ToolCall> extracted,
        CancellationToken turnToken,
        NoExtractedToolRoundState state) {
        var turn = state.Turn;
        var text = state.AssistantDraft;
        var controlPayloadDetected = state.ControlPayloadDetected;
        var routedUserRequest = state.RoutedUserRequest;
        var executionContractApplies = state.ExecutionContractApplies;
        var toolDefs = state.ToolDefs;
        var options = state.Options;
        var usedContinuationSubset = state.UsedContinuationSubset;
        var toolRounds = state.ToolRounds;
        var projectionFallbackCount = state.ProjectionFallbackCount;
        var reviewPassesUsed = state.ReviewPassesUsed;
        var executionNudgeUsed = state.ExecutionNudgeUsed;
        var noToolExecutionWatchdogUsed = state.NoToolExecutionWatchdogUsed;
        var noToolExecutionWatchdogReason = state.NoToolExecutionWatchdogReason;
        var autoPendingActionReplayUsed = state.AutoPendingActionReplayUsed;
        var proactiveFollowUpUsed = state.ProactiveFollowUpUsed;
        var localNoTextDirectRetryUsed = state.LocalNoTextDirectRetryUsed;
        var structuredNextActionRetryUsed = state.StructuredNextActionRetryUsed;
        var toolProgressRecoveryUsed = state.ToolProgressRecoveryUsed;
        var hostStructuredNextActionReplayUsed = state.HostStructuredNextActionReplayUsed;
        var packCapabilityFallbackReplayUsed = state.PackCapabilityFallbackReplayUsed;
        var noResultPhaseLoopWatchdogUsed = state.NoResultPhaseLoopWatchdogUsed;
        var lastNonEmptyAssistantDraft = state.LastNonEmptyAssistantDraft;
        var nudgeUnknownEnvelopeReplanCount = state.NudgeUnknownEnvelopeReplanCount;
        var noTextRecoveryHitCount = state.NoTextRecoveryHitCount;
        var noTextToolOutputRecoveryHitCount = state.NoTextToolOutputRecoveryHitCount;
        var proactiveSkipMutatingCount = state.ProactiveSkipMutatingCount;
        var proactiveSkipReadOnlyCount = state.ProactiveSkipReadOnlyCount;
        var proactiveSkipUnknownCount = state.ProactiveSkipUnknownCount;
        var interimResultSent = state.InterimResultSent;

                var structuredNextActionToolDefs = fullToolDefs.Length > 0 ? fullToolDefs : toolDefs;
                var hasStructuredNextAction = TryExtractStructuredNextAction(
                    toolDefinitions: structuredNextActionToolDefs,
                    toolCalls: toolCalls,
                    toolOutputs: toolOutputs,
                    out _,
                    out var structuredNextToolName,
                    out _,
                    out _,
                    out _);
                var allowHostStructuredReplay = ShouldAllowHostStructuredNextActionReplay(text);
                if (!hostStructuredNextActionReplayUsed
                    && allowHostStructuredReplay
                    && toolCalls.Count > 0
                    && toolOutputs.Count > 0
                    && TryBuildHostStructuredNextActionToolCall(
                        toolDefinitions: structuredNextActionToolDefs,
                        toolCalls: toolCalls,
                        toolOutputs: toolOutputs,
                        mutatingToolHintsByName: mutatingToolHints,
                        out var hostStructuredNextActionCall,
                        out var hostStructuredNextActionReason)) {
                    hostStructuredNextActionReplayUsed = true;
                    if (fullToolDefs.Length > 0 && toolDefs.Count != fullToolDefs.Length) {
                        toolDefs = fullToolDefs;
                        options.Tools = fullToolDefs;
                        options.ToolChoice = ToolChoice.Auto;
                        usedContinuationSubset = false;
                        RememberWeightedToolSubset(threadId, toolDefs, originalToolCount);
                    }

                    Trace.WriteLine(
                        $"[host-structured-next-action] outcome=execute reason={hostStructuredNextActionReason} continuation={continuationFollowUpTurn} tool={hostStructuredNextActionCall.Name} prior_calls={toolCalls.Count} prior_outputs={toolOutputs.Count}");

                    toolRounds++;
                    var hostRoundNumber = round + 1;
                    await WriteToolRoundStartedStatusAsync(
                            writer,
                            request.RequestId,
                            threadId,
                            hostRoundNumber,
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
                                message: $"Executing tool-recommended next action ({hostStructuredNextActionCall.Name})...")
                            .ConfigureAwait(false);
                    }

                    await TryWriteStatusAsync(
                            writer,
                            request.RequestId,
                            threadId,
                            status: ChatStatusCodes.ToolCall,
                            toolName: hostStructuredNextActionCall.Name,
                            toolCallId: hostStructuredNextActionCall.CallId)
                        .ConfigureAwait(false);
                    toolCalls.Add(new ToolCallDto {
                        CallId = hostStructuredNextActionCall.CallId,
                        Name = hostStructuredNextActionCall.Name,
                        ArgumentsJson = hostStructuredNextActionCall.Arguments is null
                            ? "{}"
                            : JsonLite.Serialize(hostStructuredNextActionCall.Arguments)
                    });

                    var hostStructuredCalls = new[] { hostStructuredNextActionCall };
                    var hostStructuredOutputs = await ExecuteToolsAsync(
                            writer,
                            request.RequestId,
                            threadId,
                            hostStructuredCalls,
                            parallel: false,
                            allowMutatingParallel: allowMutatingParallel,
                            mutatingToolHintsByName: mutatingToolHints,
                            toolTimeoutSeconds: toolTimeoutSeconds,
                            userRequest: routedUserRequest,
                            cancellationToken: turnToken)
                        .ConfigureAwait(false);
                    var hostStructuredFailedCalls = CountFailedToolOutputs(hostStructuredOutputs);
                    await WriteToolRoundCompletedStatusAsync(
                            writer,
                            request.RequestId,
                            threadId,
                            hostRoundNumber,
                            maxRounds,
                            hostStructuredOutputs.Count,
                            hostStructuredFailedCalls)
                        .ConfigureAwait(false);
                    UpdateToolRoutingStats(hostStructuredCalls, hostStructuredOutputs);
                    foreach (var output in hostStructuredOutputs) {
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

                    var hostStructuredNextInput = BuildHostReplayReviewInput(
                        hostStructuredNextActionCall,
                        hostStructuredOutputs,
                        supportsSyntheticHostReplayItems);
                    turn = await RunModelPhaseWithProgressAsync(
                            client,
                            writer,
                            request.RequestId,
                            threadId,
                            hostStructuredNextInput,
                            CopyChatOptions(options, newThreadOverride: false),
                            turnToken,
                            phaseStatus: planExecuteReviewLoop ? ChatStatusCodes.PhaseReview : ChatStatusCodes.Thinking,
                            phaseMessage: "Reviewing tool-recommended next action results...",
                            heartbeatLabel: "Reviewing next action",
                            heartbeatSeconds: modelHeartbeatSeconds)
                        .ConfigureAwait(false);
                    return ContinueRound();
                }

                if (!packCapabilityFallbackReplayUsed
                    && allowHostStructuredReplay
                    && toolCalls.Count > 0
                    && toolOutputs.Count > 0
                    && TryBuildPackCapabilityFallbackToolCall(
                        toolDefinitions: structuredNextActionToolDefs,
                        toolCalls: toolCalls,
                        toolOutputs: toolOutputs,
                        userRequest: routedUserRequest,
                        mutatingToolHintsByName: mutatingToolHints,
                        out var packFallbackCall,
                        out var packFallbackReason)) {
                    packCapabilityFallbackReplayUsed = true;
                    if (fullToolDefs.Length > 0 && toolDefs.Count != fullToolDefs.Length) {
                        toolDefs = fullToolDefs;
                        options.Tools = fullToolDefs;
                        options.ToolChoice = ToolChoice.Auto;
                        usedContinuationSubset = false;
                        RememberWeightedToolSubset(threadId, toolDefs, originalToolCount);
                    }

                    Trace.WriteLine(
                        $"[pack-capability-fallback] outcome=execute reason={packFallbackReason} continuation={continuationFollowUpTurn} tool={packFallbackCall.Name} prior_calls={toolCalls.Count} prior_outputs={toolOutputs.Count}");

                    toolRounds++;
                    var packFallbackRoundNumber = round + 1;
                    await WriteToolRoundStartedStatusAsync(
                            writer,
                            request.RequestId,
                            threadId,
                            packFallbackRoundNumber,
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
                                message: $"Executing pack fallback discovery action ({packFallbackCall.Name})...")
                            .ConfigureAwait(false);
                    }

                    await TryWriteStatusAsync(
                            writer,
                            request.RequestId,
                            threadId,
                            status: ChatStatusCodes.ToolCall,
                            toolName: packFallbackCall.Name,
                            toolCallId: packFallbackCall.CallId)
                        .ConfigureAwait(false);
                    toolCalls.Add(new ToolCallDto {
                        CallId = packFallbackCall.CallId,
                        Name = packFallbackCall.Name,
                        ArgumentsJson = packFallbackCall.Arguments is null
                            ? "{}"
                            : JsonLite.Serialize(packFallbackCall.Arguments)
                    });

                    var packFallbackCalls = new[] { packFallbackCall };
                    var packFallbackOutputs = await ExecuteToolsAsync(
                            writer,
                            request.RequestId,
                            threadId,
                            packFallbackCalls,
                            parallel: false,
                            allowMutatingParallel: allowMutatingParallel,
                            mutatingToolHintsByName: mutatingToolHints,
                            toolTimeoutSeconds: toolTimeoutSeconds,
                            userRequest: routedUserRequest,
                            cancellationToken: turnToken)
                        .ConfigureAwait(false);
                    var packFallbackFailedCalls = CountFailedToolOutputs(packFallbackOutputs);
                    await WriteToolRoundCompletedStatusAsync(
                            writer,
                            request.RequestId,
                            threadId,
                            packFallbackRoundNumber,
                            maxRounds,
                            packFallbackOutputs.Count,
                            packFallbackFailedCalls)
                        .ConfigureAwait(false);
                    UpdateToolRoutingStats(packFallbackCalls, packFallbackOutputs);
                    foreach (var output in packFallbackOutputs) {
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

                    var packFallbackNextInput = BuildHostReplayReviewInput(
                        packFallbackCall,
                        packFallbackOutputs,
                        supportsSyntheticHostReplayItems);
                    turn = await RunModelPhaseWithProgressAsync(
                            client,
                            writer,
                            request.RequestId,
                            threadId,
                            packFallbackNextInput,
                            CopyChatOptions(options, newThreadOverride: false),
                            turnToken,
                            phaseStatus: planExecuteReviewLoop ? ChatStatusCodes.PhaseReview : ChatStatusCodes.Thinking,
                            phaseMessage: "Reviewing fallback discovery results...",
                            heartbeatLabel: "Reviewing fallback results",
                            heartbeatSeconds: modelHeartbeatSeconds)
                        .ConfigureAwait(false);
                    return ContinueRound();
                }

                if (!structuredNextActionRetryUsed
                    && TryBuildStructuredNextActionRetryPrompt(
                        toolDefinitions: structuredNextActionToolDefs,
                        toolCalls: toolCalls,
                        toolOutputs: toolOutputs,
                        continuationFollowUpTurn: continuationFollowUpTurn,
                        userRequest: routedUserRequest,
                        assistantDraft: text,
                        out var structuredNextActionPrompt,
                        out var structuredNextActionReason)) {
                    structuredNextActionRetryUsed = true;
                    if (fullToolDefs.Length > 0 && toolDefs.Count != fullToolDefs.Length) {
                        toolDefs = fullToolDefs;
                        options.Tools = fullToolDefs;
                        options.ToolChoice = ToolChoice.Auto;
                        usedContinuationSubset = false;
                        RememberWeightedToolSubset(threadId, toolDefs, originalToolCount);
                    }
                    Trace.WriteLine(
                        $"[structured-next-action] outcome=retry reason={structuredNextActionReason} continuation={continuationFollowUpTurn} tools={toolDefs.Count} prior_calls={toolCalls.Count} prior_outputs={toolOutputs.Count}");
                    var structuredRetryOptions = CopyChatOptions(options, newThreadOverride: false);
                    if (hasStructuredNextAction
                        && !string.IsNullOrWhiteSpace(structuredNextToolName)
                        && toolDefs.Any(def => string.Equals(def.Name, structuredNextToolName, StringComparison.OrdinalIgnoreCase))) {
                        structuredRetryOptions.ToolChoice = ToolChoice.Custom(structuredNextToolName);
                    }
                    turn = await RunModelPhaseWithProgressAsync(
                            client,
                            writer,
                            request.RequestId,
                            threadId,
                            ChatInput.FromText(structuredNextActionPrompt),
                            structuredRetryOptions,
                            turnToken,
                            phaseStatus: planExecuteReviewLoop ? ChatStatusCodes.PhasePlan : ChatStatusCodes.Thinking,
                            phaseMessage: "Continuing with tool-recommended next action.",
                            heartbeatLabel: "Executing next action",
                            heartbeatSeconds: modelHeartbeatSeconds)
                        .ConfigureAwait(false);
                    return ContinueRound();
                }

                var shouldAttemptToolProgressRecovery = ShouldAttemptToolProgressRecovery(
                    continuationFollowUpTurn: continuationFollowUpTurn,
                    assistantDraft: text,
                    toolsAvailable: fullToolDefs.Length > 0 || toolDefs.Count > 0,
                    priorToolCalls: toolCalls.Count,
                    priorToolOutputs: toolOutputs.Count,
                    assistantDraftToolCalls: extracted.Count,
                    progressRecoveryAlreadyUsed: toolProgressRecoveryUsed,
                    out var toolProgressRecoveryReason);
                if (shouldAttemptToolProgressRecovery) {
                    toolProgressRecoveryUsed = true;
                    if (fullToolDefs.Length > 0 && toolDefs.Count != fullToolDefs.Length) {
                        toolDefs = fullToolDefs;
                        options.Tools = fullToolDefs;
                        options.ToolChoice = ToolChoice.Auto;
                        usedContinuationSubset = false;
                        RememberWeightedToolSubset(threadId, toolDefs, originalToolCount);
                    }
                    Trace.WriteLine(
                        $"[tool-progress-recovery] outcome=retry reason={toolProgressRecoveryReason} continuation={continuationFollowUpTurn} tools={toolDefs.Count} prior_calls={toolCalls.Count} prior_outputs={toolOutputs.Count}");
                    var progressRecoveryPrompt = BuildToolProgressRecoveryPrompt(
                        userRequest: routedUserRequest,
                        assistantDraft: text,
                        toolCalls: toolCalls);
                    turn = await RunModelPhaseWithProgressAsync(
                            client,
                            writer,
                            request.RequestId,
                            threadId,
                            ChatInput.FromText(progressRecoveryPrompt),
                            CopyChatOptions(options, newThreadOverride: false),
                            turnToken,
                            phaseStatus: planExecuteReviewLoop ? ChatStatusCodes.PhasePlan : ChatStatusCodes.Thinking,
                            phaseMessage: "Continuing execution after blocker-style draft.",
                            heartbeatLabel: "Recovering execution progress",
                            heartbeatSeconds: modelHeartbeatSeconds)
                        .ConfigureAwait(false);
                    return ContinueRound();
                }

                var hasToolActivity = toolCalls.Count > 0 || toolOutputs.Count > 0;
                var noResultWatchdogTriggered = false;
                var trailingPhaseLoopEvents = CountTrailingPhaseLoopEvents(request.RequestId);
                if (ShouldTriggerNoResultPhaseLoopWatchdog(
                        trailingPhaseLoopEvents: trailingPhaseLoopEvents,
                        hasToolActivity: hasToolActivity,
                        watchdogAlreadyUsed: noResultPhaseLoopWatchdogUsed,
                        executionContractApplies: executionContractApplies,
                        continuationFollowUpTurn: continuationFollowUpTurn,
                        compactFollowUpTurn: compactFollowUpTurn,
                        assistantDraft: text,
                        out var noResultWatchdogReason)) {
                    noResultPhaseLoopWatchdogUsed = true;
                    noResultWatchdogTriggered = true;
                    await TryWriteStatusAsync(
                            writer,
                            request.RequestId,
                            threadId,
                            status: ChatStatusCodes.NoResultWatchdogTriggered,
                            message: $"No-result watchdog triggered after repeated plan/review loops ({trailingPhaseLoopEvents} phase events).")
                        .ConfigureAwait(false);
                    text = BuildExecutionContractBlockerText(
                        userRequest: routedUserRequest,
                        assistantDraft: text,
                        reason: "no_result_watchdog_" + noResultWatchdogReason);
                }

                if (executionContractApplies && !hasToolActivity) {
                    var blockerReason = noToolExecutionWatchdogUsed
                        ? "no_tool_calls_after_watchdog_retry"
                        : $"execution_contract_unmet_{noToolExecutionWatchdogReason}";
                    text = BuildExecutionContractBlockerText(
                        userRequest: routedUserRequest,
                        assistantDraft: text,
                        reason: blockerReason);
                }

                if (!noResultWatchdogTriggered
                    && !interimResultSent
                    && planExecuteReviewLoop
                    && maxReviewPasses > 0
                    && hasToolActivity
                    && ShouldEmitInterimResultSnapshot(text)) {
                    interimResultSent = true;
                    await TryWriteInterimResultAsync(
                            writer,
                            request.RequestId,
                            threadId,
                            text,
                            stage: "interim_review_draft",
                            toolCallsCount: toolCalls.Count,
                            toolOutputsCount: toolOutputs.Count)
                        .ConfigureAwait(false);
                }

                if (!noResultWatchdogTriggered
                    && planExecuteReviewLoop
                    && ShouldAttemptResponseQualityReview(
                        userRequest: routedUserRequest,
                        assistantDraft: text,
                        executionContractApplies: executionContractApplies,
                        hasToolActivity: hasToolActivity,
                        reviewPassesUsed: reviewPassesUsed,
                        maxReviewPasses: maxReviewPasses)) {
                    reviewPassesUsed++;
                    var reviewPrompt = BuildResponseQualityReviewPrompt(
                        userRequest: routedUserRequest,
                        assistantDraft: text,
                        hasToolActivity: hasToolActivity,
                        reviewPassNumber: reviewPassesUsed,
                        maxReviewPasses: maxReviewPasses);
                    turn = await RunReviewOnlyModelPhaseWithProgressAsync(
                            client,
                            writer,
                            request.RequestId,
                            threadId,
                            ChatInput.FromText(reviewPrompt),
                            options,
                            turnToken,
                            phaseStatus: ChatStatusCodes.PhaseReview,
                            phaseMessage: $"Reviewing response quality ({reviewPassesUsed}/{maxReviewPasses})...",
                            heartbeatLabel: "Reviewing response",
                            heartbeatSeconds: modelHeartbeatSeconds)
                        .ConfigureAwait(false);
                    return ContinueRound();
                }

                var proactiveDecision = ResolveProactiveFollowUpReviewDecision(
                    proactiveModeEnabled: proactiveModeEnabled,
                    hasToolActivity: hasToolActivity,
                    proactiveFollowUpUsed: proactiveFollowUpUsed,
                    continuationFollowUpTurn: continuationFollowUpTurn,
                    compactFollowUpTurn: compactFollowUpTurn,
                    assistantDraft: text);
                if (!proactiveDecision.ShouldAttempt
                    && string.Equals(proactiveDecision.Reason, "skip_pending_mutating_actions", StringComparison.OrdinalIgnoreCase)) {
                    proactiveSkipMutatingCount++;
                    if (proactiveDecision.PendingReadOnlyCount > 0) {
                        proactiveSkipReadOnlyCount++;
                    }
                    if (proactiveDecision.PendingUnknownCount > 0) {
                        proactiveSkipUnknownCount++;
                    }
                }

                if (!noResultWatchdogTriggered && proactiveDecision.ShouldAttempt) {
                    proactiveFollowUpUsed = true;
                    var proactivePrompt = BuildProactiveFollowUpReviewPrompt(routedUserRequest, text);
                    turn = await RunReviewOnlyModelPhaseWithProgressAsync(
                            client,
                            writer,
                            request.RequestId,
                            threadId,
                            ChatInput.FromText(proactivePrompt),
                            options,
                            turnToken,
                            phaseStatus: ChatStatusCodes.PhaseReview,
                            phaseMessage: "Generating proactive next checks and fixes...",
                            heartbeatLabel: "Preparing proactive follow-up",
                            heartbeatSeconds: modelHeartbeatSeconds)
                        .ConfigureAwait(false);
                    return ContinueRound();
                }

                if (ShouldForceExecutionContractBlockerAtFinalize(
                        userRequest: routedUserRequest,
                        executionContractApplies: executionContractApplies,
                        autoPendingActionReplayUsed: autoPendingActionReplayUsed,
                        executionNudgeUsed: executionNudgeUsed,
                        noToolExecutionWatchdogUsed: noToolExecutionWatchdogUsed,
                        continuationFollowUpTurn: continuationFollowUpTurn,
                        compactFollowUpTurn: compactFollowUpTurn,
                        toolActivityDetected: hasToolActivity,
                        assistantDraft: text)) {
                    var blockerReason = noToolExecutionWatchdogUsed
                        ? "no_tool_calls_after_watchdog_retry"
                        : "no_tool_evidence_at_finalize";
                    if (!TryBuildToolEvidenceFallbackText(threadId, routedUserRequest, out var cachedEvidenceFallbackText)) {
                        text = BuildExecutionContractBlockerText(
                            userRequest: routedUserRequest,
                            assistantDraft: text,
                            reason: blockerReason);
                    } else {
                        text = cachedEvidenceFallbackText;
                    }
                }

                text = AppendTurnCompletionNotice(text, turn);

                var finalizedStructuredNextActionToolDefs = fullToolDefs.Length > 0 ? fullToolDefs : toolDefs;
                RememberStructuredNextActionCarryover(
                    threadId,
                    finalizedStructuredNextActionToolDefs,
                    toolCalls,
                    toolOutputs,
                    mutatingToolHints);

                // Capture pending actions from the finalized assistant text so confirmation routing stays aligned
                // with what the user actually sees (including contract fallback substitutions).
                RememberPreferredDomainIntentFamily(threadId, toolCalls, toolOutputs, mutatingToolHints);
                RememberThreadToolEvidence(threadId, toolCalls, toolOutputs, mutatingToolHints);
                RememberWorkingMemoryCheckpoint(threadId, userIntent, routedUserRequest, toolCalls, toolOutputs, mutatingToolHints);
                RememberPendingActions(threadId, text);

                if (_options.Redact) {
                    text = RedactText(text);
                }

                var textBeforeNoTextFallback = text;
                text = ResolveAssistantTextBeforeNoTextFallback(
                    assistantDraft: text,
                    lastNonEmptyAssistantDraft: lastNonEmptyAssistantDraft,
                    hasToolActivity: hasToolActivity);
                if (string.IsNullOrWhiteSpace(textBeforeNoTextFallback) && !string.IsNullOrWhiteSpace(text)) {
                    noTextRecoveryHitCount++;
                }
                var textBeforeToolOutputFallback = text;
                text = ResolveAssistantTextFromToolOutputsFallback(
                    assistantDraft: text,
                    toolCalls: toolCalls,
                    toolOutputs: toolOutputs);
                if (string.IsNullOrWhiteSpace(textBeforeToolOutputFallback) && !string.IsNullOrWhiteSpace(text)) {
                    noTextToolOutputRecoveryHitCount++;
                }

                if (string.IsNullOrWhiteSpace(text)) {
                    var shouldAttemptLocalNoTextDirectRetry = !localNoTextDirectRetryUsed
                                                             && isLocalCompatibleLoopback
                                                             && toolDefs.Count > 0
                                                             && toolCalls.Count == 0
                                                             && toolOutputs.Count == 0;
                    if (shouldAttemptLocalNoTextDirectRetry) {
                        localNoTextDirectRetryUsed = true;
                        var directRetryPrompt = BuildCompatibleRuntimeNoTextDirectRetryPrompt(routedUserRequest);
                        turn = await RunModelPhaseWithProgressAsync(
                                client,
                                writer,
                                request.RequestId,
                                threadId,
                                ChatInput.FromText(directRetryPrompt),
                                CopyChatOptionsWithoutTools(options, newThreadOverride: false),
                                turnToken,
                                phaseStatus: ChatStatusCodes.PhaseReview,
                                phaseMessage: controlPayloadDetected
                                    ? "Retrying direct response after runtime control-payload artifact..."
                                    : "Retrying response in direct mode (without tools)...",
                                heartbeatLabel: "Retrying direct response",
                                heartbeatSeconds: modelHeartbeatSeconds)
                            .ConfigureAwait(false);
                        return ContinueRound();
                    }

                    text = BuildNoTextResponseFallbackText(
                        model: resolvedModel,
                        transport: _options.OpenAITransport,
                        baseUrl: _options.OpenAIBaseUrl);
                }

                if (!string.IsNullOrWhiteSpace(text) && !LooksLikeRuntimeControlPayloadArtifact(text)) {
                    lastNonEmptyAssistantDraft = text.Trim();
                }

                var autonomyCounters = BuildAutonomyCounterMetrics(
                    nudgeUnknownEnvelopeReplanCount: nudgeUnknownEnvelopeReplanCount,
                    noTextRecoveryHitCount: noTextRecoveryHitCount,
                    noTextToolOutputRecoveryHitCount: noTextToolOutputRecoveryHitCount,
                    proactiveSkipMutatingCount: proactiveSkipMutatingCount,
                    proactiveSkipReadOnlyCount: proactiveSkipReadOnlyCount,
                    proactiveSkipUnknownCount: proactiveSkipUnknownCount);
                TraceAutonomyTelemetryCounters(
                    requestId: request.RequestId,
                    threadId: threadId,
                    nudgeUnknownEnvelopeReplanCount: nudgeUnknownEnvelopeReplanCount,
                    noTextRecoveryHitCount: noTextRecoveryHitCount,
                    noTextToolOutputRecoveryHitCount: noTextToolOutputRecoveryHitCount,
                    proactiveSkipMutatingCount: proactiveSkipMutatingCount,
                    proactiveSkipReadOnlyCount: proactiveSkipReadOnlyCount,
                    proactiveSkipUnknownCount: proactiveSkipUnknownCount);

                var result = new ChatResultMessage {
                    Kind = ChatServiceMessageKind.Response,
                    RequestId = request.RequestId,
                    ThreadId = threadId,
                    Text = text,
                    Tools = toolCalls.Count == 0 && toolOutputs.Count == 0
                        ? null
                        : new ToolRunDto { Calls = toolCalls.ToArray(), Outputs = toolOutputs.ToArray() },
                    TurnTimelineEvents = SnapshotTurnTimelineEvents(request.RequestId)
                };
                return NoExtractedToolRoundOutcome.ReturnFinal(new ChatTurnRunResult(
                    Result: result,
                    Usage: turn.Usage,
                    ToolCallsCount: toolCalls.Count,
                    ToolRounds: toolRounds,
                    ProjectionFallbackCount: projectionFallbackCount,
                    ToolErrors: BuildToolErrorMetrics(toolCalls, toolOutputs),
                    AutonomyCounters: autonomyCounters,
                    ResolvedModel: resolvedModel));
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
            state.ReviewPassesUsed = reviewPassesUsed;
            state.ExecutionNudgeUsed = executionNudgeUsed;
            state.NoToolExecutionWatchdogUsed = noToolExecutionWatchdogUsed;
            state.NoToolExecutionWatchdogReason = noToolExecutionWatchdogReason;
            state.AutoPendingActionReplayUsed = autoPendingActionReplayUsed;
            state.ProactiveFollowUpUsed = proactiveFollowUpUsed;
            state.LocalNoTextDirectRetryUsed = localNoTextDirectRetryUsed;
            state.StructuredNextActionRetryUsed = structuredNextActionRetryUsed;
            state.ToolProgressRecoveryUsed = toolProgressRecoveryUsed;
            state.HostStructuredNextActionReplayUsed = hostStructuredNextActionReplayUsed;
            state.PackCapabilityFallbackReplayUsed = packCapabilityFallbackReplayUsed;
            state.NoResultPhaseLoopWatchdogUsed = noResultPhaseLoopWatchdogUsed;
            state.LastNonEmptyAssistantDraft = lastNonEmptyAssistantDraft;
            state.NudgeUnknownEnvelopeReplanCount = nudgeUnknownEnvelopeReplanCount;
            state.NoTextRecoveryHitCount = noTextRecoveryHitCount;
            state.NoTextToolOutputRecoveryHitCount = noTextToolOutputRecoveryHitCount;
            state.ProactiveSkipMutatingCount = proactiveSkipMutatingCount;
            state.ProactiveSkipReadOnlyCount = proactiveSkipReadOnlyCount;
            state.ProactiveSkipUnknownCount = proactiveSkipUnknownCount;
            state.InterimResultSent = interimResultSent;
        }
    }
}
