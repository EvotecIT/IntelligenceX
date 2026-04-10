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
        long? weightedSubsetSelectionMs,
        long? resolveModelMs,
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
        var noTextToolOutputDirectRetryUsed = state.NoTextToolOutputDirectRetryUsed;
        var structuredNextActionRetryUsed = state.StructuredNextActionRetryUsed;
        var toolProgressRecoveryUsed = state.ToolProgressRecoveryUsed;
        var backgroundDependencyRecoveryUsed = state.BackgroundDependencyRecoveryUsed;
        var hostStructuredNextActionReplayUsed = state.HostStructuredNextActionReplayUsed;
        var noResultPhaseLoopWatchdogUsed = state.NoResultPhaseLoopWatchdogUsed;
        var lastNonEmptyAssistantDraft = state.LastNonEmptyAssistantDraft;
        var nudgeUnknownEnvelopeReplanCount = state.NudgeUnknownEnvelopeReplanCount;
        var noTextRecoveryHitCount = state.NoTextRecoveryHitCount;
        var noTextToolOutputRecoveryHitCount = state.NoTextToolOutputRecoveryHitCount;
        var proactiveSkipMutatingCount = state.ProactiveSkipMutatingCount;
        var proactiveSkipReadOnlyCount = state.ProactiveSkipReadOnlyCount;
        var proactiveSkipUnknownCount = state.ProactiveSkipUnknownCount;
        var interimResultSent = state.InterimResultSent;
        var primaryUserRequest = ExtractPrimaryUserRequest(request.Text);
        var visibleUserRequest = string.IsNullOrWhiteSpace(primaryUserRequest)
            ? routedUserRequest
            : primaryUserRequest;
        if (continuationFollowUpTurn || compactFollowUpTurn) {
            text = ResolveAssistantTextFromRequestedArtifactToolOutputsFallback(
                userRequest: visibleUserRequest,
                assistantDraft: text,
                toolOutputs: toolOutputs);
        }

                var structuredNextActionToolDefs = fullToolDefs.Length > 0 ? fullToolDefs : toolDefs;
                var hasStructuredNextAction = TryExtractStructuredNextAction(
                    toolDefinitions: structuredNextActionToolDefs,
                    toolCalls: toolCalls,
                    toolOutputs: toolOutputs,
                    out _,
                    out var structuredNextToolName,
                    out _,
                    out _,
                    out _,
                    out _);
                var allowHostStructuredReplay = ShouldAllowHostStructuredNextActionReplay(text);
                var hostStructuredReplayHintInput = BuildCarryoverHostHintInput(userIntent, text);
                if (!hostStructuredNextActionReplayUsed
                    && allowHostStructuredReplay
                    && toolCalls.Count > 0
                    && toolOutputs.Count > 0
                    && TryBuildHostStructuredNextActionToolCall(
                        toolDefinitions: structuredNextActionToolDefs,
                        toolCalls: toolCalls,
                        toolOutputs: toolOutputs,
                        userRequest: hostStructuredReplayHintInput,
                        mutatingToolHintsByName: mutatingToolHints,
                        out var hostStructuredNextActionCall,
                        out var hostStructuredNextActionReason)) {
                    if (ShouldBlockSingleHostStructuredReplayForScopeShift(
                            threadId,
                            ResolveFinalizeHostScopeShiftUserRequest(userIntent, routedUserRequest),
                            hostStructuredNextActionCall.Arguments ?? new JsonObject())) {
                        hostStructuredNextActionReplayUsed = true;
                        Trace.WriteLine(
                            $"[host-structured-next-action] outcome=skip reason=scope_shift_requires_fresh_plan continuation={continuationFollowUpTurn} tool={hostStructuredNextActionCall.Name} prior_calls={toolCalls.Count} prior_outputs={toolOutputs.Count}");
                    } else {
                    hostStructuredNextActionReplayUsed = true;
                    if (fullToolDefs.Length > 0 && toolDefs.Count != fullToolDefs.Length) {
                        toolDefs = ExpandToFullToolAvailabilityForPromptExposure(routedUserRequest, fullToolDefs, options);
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
                        supportsSyntheticHostReplayItems,
                        out var hostStructuredReviewPromptText);
                    var hostStructuredReviewOptions = await CopyChatOptionsWithPromptAwareToolOrderingAndEmitStatusAsync(
                            writer,
                            request.RequestId,
                            threadId,
                            options,
                            hostStructuredReviewPromptText,
                            strategy: "prompt_review",
                            newThreadOverride: false)
                        .ConfigureAwait(false);
                    turn = await RunModelPhaseWithProgressAsync(
                            client,
                            writer,
                            request.RequestId,
                            threadId,
                            hostStructuredNextInput,
                            hostStructuredReviewOptions,
                            turnToken,
                            phaseStatus: planExecuteReviewLoop ? ChatStatusCodes.PhaseReview : ChatStatusCodes.Thinking,
                            phaseMessage: "Reviewing tool-recommended next action results...",
                            heartbeatLabel: "Reviewing next action",
                                heartbeatSeconds: modelHeartbeatSeconds)
                        .ConfigureAwait(false);
                    return ContinueRound();
                    }
                }

                var finalizeContinuationDecision = ResolveNoExtractedFinalizeContinuationDecision(
                    structuredNextActionRetryUsed: structuredNextActionRetryUsed,
                    structuredNextActionToolDefs: structuredNextActionToolDefs,
                    toolCalls: toolCalls,
                    toolOutputs: toolOutputs,
                    continuationFollowUpTurn: continuationFollowUpTurn,
                    userRequest: routedUserRequest,
                    assistantDraft: text,
                    hasStructuredNextAction: hasStructuredNextAction,
                    structuredNextToolName: structuredNextToolName,
                    activeToolDefinitions: toolDefs,
                    toolsAvailable: fullToolDefs.Length > 0 || toolDefs.Count > 0,
                    assistantDraftToolCalls: extracted.Count,
                    toolProgressRecoveryUsed: toolProgressRecoveryUsed);
                if (finalizeContinuationDecision.Kind == NoExtractedFinalizeContinuationDecisionKind.StructuredNextActionRetry) {
                    structuredNextActionRetryUsed = true;

                    if (finalizeContinuationDecision.ExpandToFullToolAvailability
                        && fullToolDefs.Length > 0
                        && toolDefs.Count != fullToolDefs.Length) {
                        toolDefs = ExpandToFullToolAvailabilityForPromptExposure(routedUserRequest, fullToolDefs, options);
                        usedContinuationSubset = false;
                        RememberWeightedToolSubset(threadId, toolDefs, originalToolCount);
                    }

                    Trace.WriteLine(
                        $"[structured-next-action] outcome=retry reason={finalizeContinuationDecision.Reason} continuation={continuationFollowUpTurn} tools={toolDefs.Count} prior_calls={toolCalls.Count} prior_outputs={toolOutputs.Count}");

                    turn = await ApplyNoExtractedFinalizeContinuationDecisionAsync(
                            client,
                            writer,
                            request,
                            threadId,
                            options,
                            turnToken,
                            planExecuteReviewLoop,
                            modelHeartbeatSeconds,
                            finalizeContinuationDecision)
                        .ConfigureAwait(false);
                    return ContinueRound();
                }

                if (!backgroundDependencyRecoveryUsed
                    && TryBuildBackgroundWorkDependencyRecoveryBlockerText(
                        threadId: threadId,
                        userRequest: routedUserRequest,
                        assistantDraft: text,
                        toolDefinitions: structuredNextActionToolDefs,
                        text: out var backgroundDependencyRecoveryText,
                        reason: out var backgroundDependencyRecoveryTextReason)) {
                    backgroundDependencyRecoveryUsed = true;
                    Trace.WriteLine(
                        $"[background-prerequisite-recovery] outcome=deterministic_blocker reason={backgroundDependencyRecoveryTextReason} continuation={continuationFollowUpTurn} prior_calls={toolCalls.Count} prior_outputs={toolOutputs.Count}");
                    text = backgroundDependencyRecoveryText;
                } else if (!backgroundDependencyRecoveryUsed
                    && TryBuildBackgroundWorkDependencyRecoveryPrompt(
                        threadId: threadId,
                        userRequest: routedUserRequest,
                        assistantDraft: text,
                        toolDefinitions: structuredNextActionToolDefs,
                        prompt: out var backgroundDependencyRecoveryPrompt,
                        reason: out var backgroundDependencyRecoveryReason)) {
                    backgroundDependencyRecoveryUsed = true;
                    Trace.WriteLine(
                        $"[background-prerequisite-recovery] outcome=prompt reason={backgroundDependencyRecoveryReason} continuation={continuationFollowUpTurn} prior_calls={toolCalls.Count} prior_outputs={toolOutputs.Count}");
                    var backgroundDependencyRecoveryOptions = await CopyChatOptionsWithPromptAwareToolOrderingAndEmitStatusAsync(
                            writer,
                            request.RequestId,
                            threadId,
                            options,
                            backgroundDependencyRecoveryPrompt,
                            strategy: "prompt_recovery",
                            newThreadOverride: false)
                        .ConfigureAwait(false);
                    turn = await RunModelPhaseWithProgressAsync(
                            client,
                            writer,
                            request.RequestId,
                            threadId,
                            ChatInput.FromText(backgroundDependencyRecoveryPrompt),
                            backgroundDependencyRecoveryOptions,
                            turnToken,
                            phaseStatus: planExecuteReviewLoop ? ChatStatusCodes.PhasePlan : ChatStatusCodes.Thinking,
                            phaseMessage: "Resolving prepared follow-up prerequisites.",
                            heartbeatLabel: "Resolving prerequisites",
                            heartbeatSeconds: modelHeartbeatSeconds)
                        .ConfigureAwait(false);
                    return ContinueRound();
                }

                if (finalizeContinuationDecision.Kind == NoExtractedFinalizeContinuationDecisionKind.ToolProgressRecovery) {
                    toolProgressRecoveryUsed = true;

                    if (finalizeContinuationDecision.ExpandToFullToolAvailability
                        && fullToolDefs.Length > 0
                        && toolDefs.Count != fullToolDefs.Length) {
                        toolDefs = ExpandToFullToolAvailabilityForPromptExposure(routedUserRequest, fullToolDefs, options);
                        usedContinuationSubset = false;
                        RememberWeightedToolSubset(threadId, toolDefs, originalToolCount);
                    }

                    Trace.WriteLine(
                        $"[tool-progress-recovery] outcome=retry reason={finalizeContinuationDecision.Reason} continuation={continuationFollowUpTurn} tools={toolDefs.Count} prior_calls={toolCalls.Count} prior_outputs={toolOutputs.Count}");

                    turn = await ApplyNoExtractedFinalizeContinuationDecisionAsync(
                            client,
                            writer,
                            request,
                            threadId,
                            options,
                            turnToken,
                            planExecuteReviewLoop,
                            modelHeartbeatSeconds,
                            finalizeContinuationDecision)
                        .ConfigureAwait(false);
                    return ContinueRound();
                }

                var hasToolActivity = toolCalls.Count > 0 || toolOutputs.Count > 0;
                var noResultWatchdogTriggered = false;
                var suppressForcedExecutionBlockerForLocalDirectRetry = false;
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
                    if (ShouldAttemptLocalDirectRetryAfterNoResultWatchdog(
                            threadId: threadId,
                            userRequest: visibleUserRequest,
                            executionContractApplies: executionContractApplies,
                            continuationFollowUpTurn: continuationFollowUpTurn,
                            compactFollowUpTurn: compactFollowUpTurn,
                            localNoTextDirectRetryUsed: localNoTextDirectRetryUsed,
                            isLocalCompatibleLoopback: isLocalCompatibleLoopback,
                            availableToolCount: toolDefs.Count,
                            hasToolActivity: hasToolActivity)) {
                        // Give the compatible local runtime one final direct-response retry on short
                        // follow-up turns before we surface an execution-blocked card. This preserves
                        // conversational capability/tool-summary turns that do not actually require tools.
                        suppressForcedExecutionBlockerForLocalDirectRetry = true;
                        text = string.Empty;
                    } else {
                        text = BuildExecutionContractBlockerText(
                            userRequest: visibleUserRequest,
                            assistantDraft: text,
                            reason: "no_result_watchdog_" + noResultWatchdogReason);
                    }
                }

                if (executionContractApplies && !hasToolActivity) {
                    var blockerReason = noToolExecutionWatchdogUsed
                        ? "no_tool_calls_after_watchdog_retry"
                        : $"execution_contract_unmet_{noToolExecutionWatchdogReason}";
                    text = BuildExecutionContractBlockerText(
                        userRequest: visibleUserRequest,
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

                var proactiveDecision = ResolveProactiveFollowUpReviewDecision(
                    proactiveModeEnabled: proactiveModeEnabled,
                    hasToolActivity: hasToolActivity,
                    proactiveFollowUpUsed: proactiveFollowUpUsed,
                    continuationFollowUpTurn: continuationFollowUpTurn,
                    compactFollowUpTurn: compactFollowUpTurn,
                    userRequest: visibleUserRequest,
                    assistantDraft: text,
                    answerPlanOverride: state.AnswerPlan);
                var startupToolingBootstrapTask = Volatile.Read(ref _startupToolingBootstrapTask);
                var turnExecutionIntent = ResolveTurnExecutionIntent(
                    userRequest: visibleUserRequest,
                    continuationFollowUpTurn: continuationFollowUpTurn,
                    compactFollowUpTurn: compactFollowUpTurn,
                    hasPendingActionContext: false,
                    hasToolActivity: hasToolActivity,
                    startupBootstrapCompleted: startupToolingBootstrapTask?.IsCompleted ?? false,
                    startupBootstrapCompletedSuccessfully: startupToolingBootstrapTask?.IsCompletedSuccessfully ?? false,
                    hasCachedToolCatalog: TryGetCachedToolCatalogForListTools(out _),
                    servingPersistedPreview: _servingPersistedToolingBootstrapPreview);
                if (!proactiveDecision.ShouldAttempt
                    && string.Equals(proactiveDecision.Reason, "skip_pending_mutating_actions", StringComparison.OrdinalIgnoreCase)) {
                    proactiveSkipMutatingCount++;
                    if (proactiveDecision.PendingReadOnlyCount > 0) {
                        proactiveSkipReadOnlyCount += proactiveDecision.PendingReadOnlyCount;
                    }
                    if (proactiveDecision.PendingUnknownCount > 0) {
                        proactiveSkipUnknownCount += proactiveDecision.PendingUnknownCount;
                    }
                }

                var finalizeReviewDecision = ResolveNoExtractedFinalizeReviewDecision(
                    noResultWatchdogTriggered: noResultWatchdogTriggered,
                    planExecuteReviewLoop: planExecuteReviewLoop,
                    maxReviewPasses: maxReviewPasses,
                    reviewPassesUsed: reviewPassesUsed,
                    turnExecutionIntent: turnExecutionIntent,
                    userRequest: visibleUserRequest,
                    assistantDraft: text,
                    answerPlan: state.AnswerPlan,
                    executionContractApplies: executionContractApplies,
                    hasToolActivity: hasToolActivity,
                    proactiveDecision: proactiveDecision,
                    toolOutputs: toolOutputs);
                if (finalizeReviewDecision.Kind != NoExtractedFinalizeReviewDecisionKind.None) {
                    if (finalizeReviewDecision.Kind == NoExtractedFinalizeReviewDecisionKind.ResponseQualityReview) {
                        reviewPassesUsed = finalizeReviewDecision.ReviewPassNumber;
                    } else if (finalizeReviewDecision.Kind == NoExtractedFinalizeReviewDecisionKind.ProactiveFollowUpReview) {
                        proactiveFollowUpUsed = true;
                    }

                    turn = await ApplyNoExtractedFinalizeReviewDecisionAsync(
                            client,
                            writer,
                            request,
                            threadId,
                            options,
                            turnToken,
                            maxReviewPasses,
                            modelHeartbeatSeconds,
                            finalizeReviewDecision)
                        .ConfigureAwait(false);
                    return ContinueRound();
                }

                var explicitToolQuestionTurn = LooksLikeExplicitToolQuestionTurn(visibleUserRequest);
                if (!hasToolActivity
                    && TryBuildInformationalToolCapabilityFallbackText(
                        userRequest: visibleUserRequest,
                        routedUserRequest: routedUserRequest,
                        lastAssistantDraft: lastNonEmptyAssistantDraft,
                        toolDefinitions: fullToolDefs.Length > 0 ? fullToolDefs : toolDefs,
                        out var informationalToolCapabilityText)) {
                    text = informationalToolCapabilityText;
                } else if (TryPreferCachedEvidenceForResolvedCompactContinuation(
                        threadId: threadId,
                        userRequest: primaryUserRequest,
                        answerPlan: state.AnswerPlan,
                        toolActivityDetected: hasToolActivity,
                        out var resolvedContinuationCachedEvidenceText)) {
                    text = resolvedContinuationCachedEvidenceText;
                } else if (!suppressForcedExecutionBlockerForLocalDirectRetry
                           && ShouldForceExecutionContractBlockerAtFinalize(
                        userRequest: visibleUserRequest,
                        executionContractApplies: executionContractApplies,
                        autoPendingActionReplayUsed: autoPendingActionReplayUsed,
                        executionNudgeUsed: executionNudgeUsed,
                        noToolExecutionWatchdogUsed: noToolExecutionWatchdogUsed,
                        continuationFollowUpTurn: continuationFollowUpTurn,
                        compactFollowUpTurn: compactFollowUpTurn,
                        explicitToolQuestionTurn: explicitToolQuestionTurn,
                        toolActivityDetected: hasToolActivity,
                        answerPlan: state.AnswerPlan,
                        assistantDraft: text)) {
                    var backgroundWorkBlockedOnPrerequisites = TryBuildBackgroundWorkDependencyBlockedGuidance(
                        threadId,
                        out _);
                    var blockerReason = noToolExecutionWatchdogUsed
                        ? "no_tool_calls_after_watchdog_retry"
                        : backgroundWorkBlockedOnPrerequisites
                            ? "background_work_waiting_on_prerequisites"
                            : "no_tool_evidence_at_finalize";
                    var builtCachedEvidenceFallback =
                        TryBuildToolEvidenceFallbackText(threadId, primaryUserRequest, out var cachedEvidenceFallbackText)
                        || TryBuildToolEvidenceFallbackText(threadId, routedUserRequest, out cachedEvidenceFallbackText);
                    if (!builtCachedEvidenceFallback) {
                        text = BuildExecutionContractBlockerText(
                            userRequest: visibleUserRequest,
                            assistantDraft: text,
                            reason: blockerReason);
                    } else {
                        text = cachedEvidenceFallbackText;
                    }
                }

                text = AppendTurnCompletionNotice(text, turn);
                text = AppendNoToolExecutionDisclosureIfNeeded(
                    assistantDraft: text,
                    tools: fullToolDefs.Length > 0 ? fullToolDefs : toolDefs,
                    priorToolCalls: toolCalls.Count,
                    priorToolOutputs: toolOutputs.Count);

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
                RememberToolHandoffBackgroundWork(threadId, finalizedStructuredNextActionToolDefs, toolCalls, toolOutputs);
                RememberWorkingMemoryCheckpoint(threadId, userIntent, routedUserRequest, state.AnswerPlan, toolCalls, toolOutputs, mutatingToolHints);

                var textBeforeNoTextFallback = text;
                text = ResolveAssistantTextBeforeNoTextFallback(
                    assistantDraft: text,
                    lastNonEmptyAssistantDraft: lastNonEmptyAssistantDraft,
                    hasToolActivity: hasToolActivity);
                if (string.IsNullOrWhiteSpace(textBeforeNoTextFallback) && !string.IsNullOrWhiteSpace(text)) {
                    noTextRecoveryHitCount++;
                }

                var hasSuccessfulToolOutput = false;
                for (var outputIndex = 0; outputIndex < toolOutputs.Count; outputIndex++) {
                    if (toolOutputs[outputIndex]?.Ok is true) {
                        hasSuccessfulToolOutput = true;
                        break;
                    }
                }

                var textBeforeToolOutputFallback = text;
                var finalizeNoTextOutcome = ResolveNoExtractedFinalizeNoTextOutcome(
                    noTextToolOutputDirectRetryUsed: noTextToolOutputDirectRetryUsed,
                    planExecuteReviewLoop: planExecuteReviewLoop,
                    redactEnabled: _options.Redact,
                    hasSuccessfulToolOutput: hasSuccessfulToolOutput,
                    toolCalls: toolCalls,
                    toolOutputs: toolOutputs,
                    assistantDraft: text,
                    localNoTextDirectRetryUsed: localNoTextDirectRetryUsed,
                    isLocalCompatibleLoopback: isLocalCompatibleLoopback,
                    availableToolCount: toolDefs.Count,
                    priorToolCalls: toolCalls.Count,
                    userRequest: visibleUserRequest);
                text = finalizeNoTextOutcome.AssistantDraft;
                if (string.IsNullOrWhiteSpace(textBeforeToolOutputFallback) && !string.IsNullOrWhiteSpace(text)) {
                    noTextToolOutputRecoveryHitCount++;
                }
                var finalizeNoTextDecision = finalizeNoTextOutcome.Decision;
                if (finalizeNoTextDecision.Kind != NoExtractedFinalizeNoTextDecisionKind.None) {
                    if (finalizeNoTextDecision.Kind == NoExtractedFinalizeNoTextDecisionKind.ToolOutputSynthesisRetry) {
                        noTextToolOutputDirectRetryUsed = true;
                    } else if (finalizeNoTextDecision.Kind == NoExtractedFinalizeNoTextDecisionKind.LocalDirectRetry) {
                        localNoTextDirectRetryUsed = true;
                    }

                    turn = await ApplyNoExtractedFinalizeNoTextDecisionAsync(
                            client,
                            writer,
                            request,
                            threadId,
                            options,
                            turnToken,
                            planExecuteReviewLoop,
                            modelHeartbeatSeconds,
                            controlPayloadDetected,
                            finalizeNoTextDecision)
                        .ConfigureAwait(false);
                    return ContinueRound();
                }

                if (string.IsNullOrWhiteSpace(text)) {

                    text = BuildNoTextResponseFallbackText(
                        model: resolvedModel,
                        transport: _options.OpenAITransport,
                        baseUrl: _options.OpenAIBaseUrl);
                }

                // Hidden answer-plan metadata is runtime-only and should never leak into the final
                // user-visible ChatResultMessage, even on paths that already parsed it earlier.
                text = ResolveReviewedAssistantDraft(text).VisibleText;
                text = NormalizeFinalResultTextForProtocol(text);

                if (_options.Redact) {
                    text = RedactText(text);
                }
                await RememberPendingActionsAndEmitBackgroundWorkStatusAsync(writer, request.RequestId, threadId, text)
                    .ConfigureAwait(false);

                if (!string.IsNullOrWhiteSpace(text) && !LooksLikeRuntimeControlPayloadArtifact(text)) {
                    lastNonEmptyAssistantDraft = text.Trim();
                }

                var backgroundWorkSnapshot = ResolveThreadBackgroundWorkSnapshot(threadId);
                var autonomyCounters = BuildAutonomyCounterMetrics(
                    nudgeUnknownEnvelopeReplanCount: nudgeUnknownEnvelopeReplanCount,
                    noTextRecoveryHitCount: noTextRecoveryHitCount,
                    noTextToolOutputRecoveryHitCount: noTextToolOutputRecoveryHitCount,
                    proactiveSkipMutatingCount: proactiveSkipMutatingCount,
                    proactiveSkipReadOnlyCount: proactiveSkipReadOnlyCount,
                    proactiveSkipUnknownCount: proactiveSkipUnknownCount,
                    backgroundWorkSnapshot: backgroundWorkSnapshot);
                TraceAutonomyTelemetryCounters(
                    requestId: request.RequestId,
                    threadId: threadId,
                    nudgeUnknownEnvelopeReplanCount: nudgeUnknownEnvelopeReplanCount,
                    noTextRecoveryHitCount: noTextRecoveryHitCount,
                    noTextToolOutputRecoveryHitCount: noTextToolOutputRecoveryHitCount,
                    proactiveSkipMutatingCount: proactiveSkipMutatingCount,
                    proactiveSkipReadOnlyCount: proactiveSkipReadOnlyCount,
                    proactiveSkipUnknownCount: proactiveSkipUnknownCount);
                var toolErrorMetrics = BuildToolErrorMetrics(toolCalls, toolOutputs);
                var autonomyTelemetry = BuildAutonomyTelemetrySummary(
                    toolRounds: toolRounds,
                    projectionFallbackCount: projectionFallbackCount,
                    toolErrors: toolErrorMetrics,
                    autonomyCounters: autonomyCounters,
                    completed: true);

                var result = new ChatResultMessage {
                    Kind = ChatServiceMessageKind.Response,
                    RequestId = request.RequestId,
                    ThreadId = threadId,
                    Text = text,
                    Tools = toolCalls.Count == 0 && toolOutputs.Count == 0
                        ? null
                        : new ToolRunDto { Calls = toolCalls.ToArray(), Outputs = toolOutputs.ToArray() },
                    TurnTimelineEvents = SnapshotTurnTimelineEvents(request.RequestId),
                    AutonomyTelemetry = autonomyTelemetry
                };
                return NoExtractedToolRoundOutcome.ReturnFinal(new ChatTurnRunResult(
                    Result: result,
                    Usage: turn.Usage,
                    ToolCallsCount: toolCalls.Count,
                    ToolRounds: toolRounds,
                    ProjectionFallbackCount: projectionFallbackCount,
                    ToolErrors: toolErrorMetrics,
                    AutonomyCounters: autonomyCounters,
                    ResolvedModel: resolvedModel,
                    WeightedSubsetSelectionMs: weightedSubsetSelectionMs,
                    ResolveModelMs: resolveModelMs));
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
            state.NoTextToolOutputDirectRetryUsed = noTextToolOutputDirectRetryUsed;
            state.StructuredNextActionRetryUsed = structuredNextActionRetryUsed;
            state.ToolProgressRecoveryUsed = toolProgressRecoveryUsed;
            state.BackgroundDependencyRecoveryUsed = backgroundDependencyRecoveryUsed;
            state.HostStructuredNextActionReplayUsed = hostStructuredNextActionReplayUsed;
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

    private static string ResolveFinalizeHostScopeShiftUserRequest(string userIntent, string routedUserRequest) {
        var normalizedUserIntent = NormalizeContextualFollowUpRequest(userIntent);
        if (normalizedUserIntent.Length > 0) {
            return normalizedUserIntent;
        }

        return NormalizeContextualFollowUpRequest(routedUserRequest);
    }

    internal static string ResolveFinalizeHostScopeShiftUserRequestForTesting(string userIntent, string routedUserRequest) {
        return ResolveFinalizeHostScopeShiftUserRequest(userIntent, routedUserRequest);
    }

    private bool ShouldAttemptLocalDirectRetryAfterNoResultWatchdog(
        string threadId,
        string userRequest,
        bool executionContractApplies,
        bool continuationFollowUpTurn,
        bool compactFollowUpTurn,
        bool localNoTextDirectRetryUsed,
        bool isLocalCompatibleLoopback,
        int availableToolCount,
        bool hasToolActivity) {
        if (executionContractApplies
            || localNoTextDirectRetryUsed
            || !isLocalCompatibleLoopback
            || availableToolCount <= 0
            || hasToolActivity
            || (!continuationFollowUpTurn && !compactFollowUpTurn)) {
            return false;
        }

        var normalizedRequest = (userRequest ?? string.Empty).Trim();
        var hasConversationContinuationContext = continuationFollowUpTurn
                                                || compactFollowUpTurn
                                                || HasFreshThreadToolEvidence(threadId)
                                                || TryGetWorkingMemoryCheckpoint(threadId, out _);
        if (normalizedRequest.Length == 0
            || !hasConversationContinuationContext
            || ContainsQuestionSignal(normalizedRequest)
            || LooksLikeExplicitToolQuestionTurn(normalizedRequest)) {
            return false;
        }

        return true;
    }

    internal static bool ShouldAttemptLocalDirectRetryAfterNoResultWatchdogForTesting(
        string userRequest,
        bool hasConversationContinuationContext,
        bool executionContractApplies,
        bool continuationFollowUpTurn,
        bool compactFollowUpTurn,
        bool localNoTextDirectRetryUsed,
        bool isLocalCompatibleLoopback,
        int availableToolCount,
        bool hasToolActivity) {
        if (executionContractApplies
            || localNoTextDirectRetryUsed
            || !isLocalCompatibleLoopback
            || availableToolCount <= 0
            || hasToolActivity
            || (!continuationFollowUpTurn && !compactFollowUpTurn && !hasConversationContinuationContext)) {
            return false;
        }

        var normalizedRequest = (userRequest ?? string.Empty).Trim();
        if (normalizedRequest.Length == 0
            || !hasConversationContinuationContext
            || ContainsQuestionSignal(normalizedRequest)
            || LooksLikeExplicitToolQuestionTurn(normalizedRequest)) {
            return false;
        }

        return true;
    }
}
