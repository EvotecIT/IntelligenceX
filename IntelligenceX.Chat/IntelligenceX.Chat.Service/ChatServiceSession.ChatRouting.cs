using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using JsonValueKind = System.Text.Json.JsonValueKind;
using IntelligenceX.Chat.Abstractions.Policy;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Abstractions.Serialization;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Json;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.OpenAI.Auth;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private async Task<ChatTurnRunResult> RunChatOnCurrentThreadAsync(IntelligenceXClient client, StreamWriter writer, ChatRequest request, string threadId,
        CancellationToken cancellationToken) {
        var toolCalls = new List<ToolCallDto>();
        var toolOutputs = new List<ToolOutputDto>();
        var toolRounds = 0;
        var projectionFallbackCount = 0;

        IReadOnlyList<ToolDefinition> toolDefs = _registry.GetDefinitions();
        if (request.Options?.DisabledTools is { Length: > 0 } disabledTools && toolDefs.Count > 0) {
            var disabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < disabledTools.Length; i++) {
                if (!string.IsNullOrWhiteSpace(disabledTools[i])) {
                    disabled.Add(disabledTools[i].Trim());
                }
            }

            if (disabled.Count > 0) {
                var filtered = new List<ToolDefinition>(toolDefs.Count);
                for (var i = 0; i < toolDefs.Count; i++) {
                    if (!disabled.Contains(toolDefs[i].Name)) {
                        filtered.Add(toolDefs[i]);
                    }
                }
                toolDefs = filtered;
            }
        }
        toolDefs = SanitizeToolDefinitions(toolDefs);

        var selectedModel = request.Options?.Model ?? _options.Model;
        if (toolDefs.Count > 0 && ShouldDisableToolsForSelectedModel(client.TransportKind, selectedModel)) {
            toolDefs = Array.Empty<ToolDefinition>();
        }

        var fullToolDefs = toolDefs.Count == 0 ? Array.Empty<ToolDefinition>() : toolDefs.ToArray();
        var originalToolCount = toolDefs.Count;
        var routingInsights = new List<ToolRoutingInsight>();
        var weightedToolRouting = request.Options?.WeightedToolRouting ?? true;
        var maxCandidateTools = ResolveMaxCandidateToolsSetting(request.Options?.MaxCandidateTools, client.TransportKind);
        var userRequest = ExtractPrimaryUserRequest(request.Text);
        var userIntent = ExtractIntentUserText(request.Text);
        RememberUserIntent(threadId, userIntent);
        var routedUserRequest = ExpandContinuationUserRequest(threadId, userRequest);
        var executionContractApplies = ShouldEnforceExecuteOrExplainContract(routedUserRequest);
        var proactiveModeEnabled = TryReadProactiveModeFromRequestText(request.Text, out var proactiveMode) && proactiveMode;
        var compactFollowUpTurn = LooksLikeContinuationFollowUp(userRequest);
        var usedContinuationSubset = false;
        if (weightedToolRouting && toolDefs.Count > 0) {
            if (!executionContractApplies) {
                if (compactFollowUpTurn) {
                    // Keep follow-up turns unconstrained so users don't see "subset retry" rewrites for
                    // short continuation requests (for example "go ahead", "run it", "check replication").
                    routingInsights = new List<ToolRoutingInsight>();
                } else if (!TryGetContinuationToolSubset(threadId, userRequest, toolDefs, out var continuationSubset)) {
                    var routed = await SelectWeightedToolSubsetAsync(
                            client,
                            threadId,
                            toolDefs,
                            routedUserRequest,
                            maxCandidateTools,
                            cancellationToken)
                        .ConfigureAwait(false);
                    toolDefs = routed.Definitions;
                    routingInsights = routed.Insights;
                } else {
                    toolDefs = continuationSubset;
                    routingInsights = BuildContinuationRoutingInsights(toolDefs);
                    usedContinuationSubset = true;
                }
            } else {
                // Explicit action-selection turns should preserve the full tool set to maximize
                // first-pass execution reliability.
                routingInsights = new List<ToolRoutingInsight>();
            }
            RememberWeightedToolSubset(threadId, toolDefs, originalToolCount);
        }

        var (parallelTools, allowMutatingParallel, parallelToolMode) =
            ResolveParallelToolExecutionMode(request.Options, _options.ParallelTools, _options.AllowMutatingParallelToolCalls);
        var requestedMaxRounds = Math.Max(1, request.Options?.MaxToolRounds ?? _options.MaxToolRounds);
        var maxRounds = ResolveMaxToolRounds(request.Options, _options.MaxToolRounds);
        var turnTimeoutSeconds = request.Options?.TurnTimeoutSeconds ?? _options.TurnTimeoutSeconds;
        var toolTimeoutSeconds = request.Options?.ToolTimeoutSeconds ?? _options.ToolTimeoutSeconds;
        using var turnCts = CreateTimeoutCts(cancellationToken, turnTimeoutSeconds);
        var turnToken = turnCts?.Token ?? cancellationToken;
        var resolvedModel = await ResolveTurnModelAsync(client, request, turnToken).ConfigureAwait(false);

        var options = new ChatOptions {
            Model = resolvedModel,
            Instructions = BuildTurnInstructionsWithRuntimeIdentity(resolvedModel),
            ReasoningEffort = ResolveReasoningEffort(request.Options?.ReasoningEffort, _options.ReasoningEffort),
            ReasoningSummary = ResolveReasoningSummary(request.Options?.ReasoningSummary, _options.ReasoningSummary),
            TextVerbosity = ResolveTextVerbosity(request.Options?.TextVerbosity, _options.TextVerbosity),
            Temperature = request.Options?.Temperature ?? _options.Temperature,
            ParallelToolCalls = parallelTools,
            Tools = toolDefs.Count == 0 ? null : toolDefs,
            ToolChoice = toolDefs.Count == 0 ? null : ToolChoice.Auto
        };
        var planExecuteReviewLoop = request.Options?.PlanExecuteReviewLoop ?? true;
        var maxReviewPasses = ResolveMaxReviewPasses(request.Options);
        var modelHeartbeatSeconds = ResolveModelHeartbeatSeconds(request.Options);
        var requestedReviewPasses = request.Options?.MaxReviewPasses;
        var requestedModelHeartbeatSeconds = request.Options?.ModelHeartbeatSeconds;
        await TryWriteStatusAsync(
                writer,
                request.RequestId,
                threadId,
                status: ChatStatusCodes.ModelSelected,
                message: "Using model: " + resolvedModel)
            .ConfigureAwait(false);

        if (requestedReviewPasses.HasValue && requestedReviewPasses.Value != maxReviewPasses) {
            await TryWriteStatusAsync(
                    writer,
                    request.RequestId,
                    threadId,
                    status: ChatStatusCodes.ReviewPassesClamped,
                    message: BuildReviewPassClampMessage(requestedReviewPasses.Value, maxReviewPasses))
                .ConfigureAwait(false);
        }

        if (requestedModelHeartbeatSeconds.HasValue && requestedModelHeartbeatSeconds.Value != modelHeartbeatSeconds) {
            await TryWriteStatusAsync(
                    writer,
                    request.RequestId,
                    threadId,
                    status: ChatStatusCodes.ModelHeartbeatClamped,
                    message: BuildModelHeartbeatClampMessage(requestedModelHeartbeatSeconds.Value, modelHeartbeatSeconds))
                .ConfigureAwait(false);
        }

        if (requestedMaxRounds > maxRounds) {
            await TryWriteStatusAsync(
                    writer,
                    request.RequestId,
                    threadId,
                    status: ChatStatusCodes.ToolRoundCapApplied,
                    message: BuildToolRoundCapAppliedMessage(requestedMaxRounds, maxRounds))
                .ConfigureAwait(false);
        }

        if (!string.Equals(parallelToolMode, ParallelToolModeAuto, StringComparison.Ordinal)) {
            await TryWriteStatusAsync(
                    writer,
                    request.RequestId,
                    threadId,
                    status: ChatStatusCodes.ToolParallelMode,
                    message: $"Tool parallel mode: {parallelToolMode}.")
                .ConfigureAwait(false);
        }

        var (routingSelectedToolCount, routingTotalToolCount) = NormalizeRoutingToolCounts(toolDefs.Count, originalToolCount);
        if (ShouldEmitRoutingTransparency(routingSelectedToolCount, routingTotalToolCount)) {
            var plannerInsightsDetected = HasPlannerInsight(routingInsights);
            var routingStrategy = ResolveRoutingStrategy(
                weightedToolRouting,
                executionContractApplies,
                usedContinuationSubset,
                routingInsights,
                routingSelectedToolCount,
                routingTotalToolCount);

            await TryWriteStatusAsync(
                    writer,
                    request.RequestId,
                    threadId,
                    status: ChatStatusCodes.Routing,
                    message: BuildRoutingSelectionMessage(routingSelectedToolCount, routingTotalToolCount, routingStrategy))
                .ConfigureAwait(false);

            var routingMetaPayload = BuildRoutingMetaPayload(
                strategy: routingStrategy,
                weightedToolRouting,
                executionContractApplies,
                usedContinuationSubset,
                selectedToolCount: routingSelectedToolCount,
                totalToolCount: routingTotalToolCount,
                insightCount: routingInsights.Count,
                plannerInsightsDetected);
            await TryWriteStatusAsync(
                    writer,
                    request.RequestId,
                    threadId,
                    status: ChatStatusCodes.RoutingMeta,
                    message: routingMetaPayload)
                .ConfigureAwait(false);

            await EmitRoutingInsightsAsync(
                    writer,
                    request.RequestId,
                    threadId,
                    routingInsights,
                    routingStrategy,
                    routingSelectedToolCount,
                    routingTotalToolCount)
                .ConfigureAwait(false);
        }

        TurnInfo turn = await RunModelPhaseWithProgressAsync(
                client,
                writer,
                request.RequestId,
                threadId,
                ChatInput.FromText(request.Text),
                CopyChatOptions(options),
                turnToken,
                phaseStatus: planExecuteReviewLoop ? ChatStatusCodes.PhasePlan : ChatStatusCodes.Thinking,
                phaseMessage: planExecuteReviewLoop ? "Planning next steps with available tools..." : "Reasoning with available tools...",
                heartbeatLabel: planExecuteReviewLoop ? "Planning next steps" : "Reasoning",
                heartbeatSeconds: modelHeartbeatSeconds)
            .ConfigureAwait(false);
        var reviewPassesUsed = 0;
        var executionNudgeUsed = false;
        var toolReceiptCorrectionUsed = false;
        var noToolExecutionWatchdogUsed = false;
        var executionContractEscapeUsed = false;
        var continuationSubsetEscapeUsed = false;
        var autoPendingActionReplayUsed = false;
        var proactiveFollowUpUsed = false;
        var localNoTextDirectRetryUsed = false;
        var isLocalCompatibleLoopback = _options.OpenAITransport == OpenAITransportKind.CompatibleHttp
                                        && IsLoopbackEndpoint(_options.OpenAIBaseUrl);
        var lastNonEmptyAssistantDraft = string.Empty;
        var nudgeUnknownEnvelopeReplanCount = 0;
        var noTextRecoveryHitCount = 0;
        var noTextToolOutputRecoveryHitCount = 0;
        var proactiveSkipMutatingCount = 0;
        var proactiveSkipReadOnlyCount = 0;
        var proactiveSkipUnknownCount = 0;

        var mutatingToolHints = BuildMutatingToolHintsByName(toolDefs);
        for (var round = 0; round < maxRounds; round++) {
            var extracted = ToolCallParser.Extract(turn);
            if (extracted.Count == 0) {
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
                    continue;
                }

                var shouldAttemptExecutionNudge = false;
                var executionNudgeReason = executionNudgeUsed
                    ? "execution_nudge_already_used"
                    : "execution_nudge_not_evaluated";
                var suppressLocalToolRecoveryRetries = isLocalCompatibleLoopback
                                                       && !executionContractApplies
                                                       && toolCalls.Count == 0
                                                       && toolOutputs.Count == 0
                                                       && string.IsNullOrWhiteSpace(text);
                if (suppressLocalToolRecoveryRetries) {
                    executionNudgeReason = "local_runtime_recovery_disabled";
                } else if (!executionNudgeUsed) {
                    shouldAttemptExecutionNudge = EvaluateToolExecutionNudgeDecision(
                        userRequest: routedUserRequest,
                        assistantDraft: text,
                        toolsAvailable: toolDefs.Count > 0,
                        priorToolCalls: toolCalls.Count,
                        assistantDraftToolCalls: extracted.Count,
                        usedContinuationSubset: usedContinuationSubset,
                        out executionNudgeReason);
                }

                if (shouldAttemptExecutionNudge) {
                    if (string.Equals(executionNudgeReason, "single_unknown_pending_action_envelope", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(executionNudgeReason, "structured_draft_single_unknown_pending_action_envelope",
                            StringComparison.OrdinalIgnoreCase)) {
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
                    continue;
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
                    continue;
                }

                var shouldAttemptWatchdog = false;
                var watchdogReason = "not_evaluated";
                if (suppressLocalToolRecoveryRetries) {
                    watchdogReason = "local_runtime_recovery_disabled";
                } else {
                    shouldAttemptWatchdog = ShouldAttemptNoToolExecutionWatchdog(
                        userRequest: routedUserRequest,
                        assistantDraft: text,
                        toolsAvailable: toolDefs.Count > 0,
                        priorToolCalls: toolCalls.Count,
                        priorToolOutputs: toolOutputs.Count,
                        assistantDraftToolCalls: extracted.Count,
                        executionNudgeUsed: executionNudgeUsed,
                        toolReceiptCorrectionUsed: toolReceiptCorrectionUsed,
                        watchdogAlreadyUsed: noToolExecutionWatchdogUsed,
                        out watchdogReason);
                }
                TraceNoToolExecutionWatchdogDecision(
                    userRequest: routedUserRequest,
                    executionContractApplies: executionContractApplies,
                    toolsAvailable: toolDefs.Count > 0,
                    priorToolCalls: toolCalls.Count,
                    priorToolOutputs: toolOutputs.Count,
                    assistantDraftToolCalls: extracted.Count,
                    executionNudgeUsed: executionNudgeUsed,
                    toolReceiptCorrectionUsed: toolReceiptCorrectionUsed,
                    watchdogAlreadyUsed: noToolExecutionWatchdogUsed,
                    shouldRetry: shouldAttemptWatchdog,
                    reason: watchdogReason);
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
                    continue;
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
                    continue;
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
                    continue;
                }

                if (executionContractApplies && !hasToolActivity) {
                    var blockerReason = noToolExecutionWatchdogUsed
                        ? "no_tool_calls_after_watchdog_retry"
                        : $"execution_contract_unmet_{watchdogReason}";
                    text = BuildExecutionContractBlockerText(
                        userRequest: routedUserRequest,
                        assistantDraft: text,
                        reason: blockerReason);
                }

                if (planExecuteReviewLoop
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
                    continue;
                }

                var proactiveDecision = ResolveProactiveFollowUpReviewDecision(
                    proactiveModeEnabled: proactiveModeEnabled,
                    hasToolActivity: hasToolActivity,
                    proactiveFollowUpUsed: proactiveFollowUpUsed,
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

                if (proactiveDecision.ShouldAttempt) {
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
                    continue;
                }

                text = AppendTurnCompletionNotice(text, turn);
                if (!string.IsNullOrWhiteSpace(text)) {
                    lastNonEmptyAssistantDraft = text.Trim();
                }

                // Capture pending actions from the finalized assistant text so confirmation routing stays aligned
                // with what the user actually sees (including contract fallback substitutions).
                RememberPendingActions(threadId, text);

                if (_options.Redact) {
                    text = RedactText(text);
                }

                var textBeforeNoTextFallbackWasEmpty = string.IsNullOrWhiteSpace(text);
                text = ResolveAssistantTextBeforeNoTextFallback(
                    assistantDraft: text,
                    lastNonEmptyAssistantDraft: lastNonEmptyAssistantDraft,
                    hasToolActivity: hasToolActivity);
                if (hasToolActivity && textBeforeNoTextFallbackWasEmpty && !string.IsNullOrWhiteSpace(text)) {
                    noTextRecoveryHitCount++;
                }
                var textBeforeToolOutputFallbackWasEmpty = string.IsNullOrWhiteSpace(text);
                text = ResolveAssistantTextFromToolOutputsFallback(
                    assistantDraft: text,
                    toolCalls: toolCalls,
                    toolOutputs: toolOutputs);
                if (hasToolActivity && textBeforeToolOutputFallbackWasEmpty && !string.IsNullOrWhiteSpace(text)) {
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
                        continue;
                    }

                    text = BuildNoTextResponseFallbackText(
                        model: resolvedModel,
                        transport: _options.OpenAITransport,
                        baseUrl: _options.OpenAIBaseUrl);
                }

                TraceAutonomyTelemetryCounters(
                    requestId: request.RequestId,
                    threadId: threadId,
                    nudgeUnknownEnvelopeReplanCount: nudgeUnknownEnvelopeReplanCount,
                    noTextRecoveryHitCount: noTextRecoveryHitCount,
                    noTextToolOutputRecoveryHitCount: noTextToolOutputRecoveryHitCount,
                    proactiveSkipMutatingCount: proactiveSkipMutatingCount,
                    proactiveSkipReadOnlyCount: proactiveSkipReadOnlyCount,
                    proactiveSkipUnknownCount: proactiveSkipUnknownCount);
                var autonomyCounters = BuildAutonomyCounterMetrics(
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
                        : new ToolRunDto { Calls = toolCalls.ToArray(), Outputs = toolOutputs.ToArray() }
                };
                return new ChatTurnRunResult(
                    Result: result,
                    Usage: turn.Usage,
                    ToolCallsCount: toolCalls.Count,
                    ToolRounds: toolRounds,
                    ProjectionFallbackCount: projectionFallbackCount,
                    ToolErrors: BuildToolErrorMetrics(toolCalls, toolOutputs),
                    AutonomyCounters: autonomyCounters,
                    ResolvedModel: resolvedModel);
            }

            toolRounds++;
            var roundNumber = round + 1;
            await WriteToolRoundStartedStatusAsync(
                    writer,
                    request.RequestId,
                    threadId,
                    roundNumber,
                    maxRounds,
                    extracted.Count,
                    parallelTools,
                    allowMutatingParallel)
                .ConfigureAwait(false);
            if (planExecuteReviewLoop) {
                await TryWriteStatusAsync(
                        writer,
                        request.RequestId,
                        threadId,
                        status: ChatStatusCodes.PhaseExecute,
                        message: $"Executing {extracted.Count} planned tool call(s)...")
                    .ConfigureAwait(false);
            }

            foreach (var call in extracted) {
                await TryWriteStatusAsync(
                        writer,
                        request.RequestId,
                        threadId,
                        status: ChatStatusCodes.ToolCall,
                        toolName: call.Name,
                        toolCallId: call.CallId)
                    .ConfigureAwait(false);
                toolCalls.Add(new ToolCallDto {
                    CallId = call.CallId,
                    Name = call.Name,
                    ArgumentsJson = call.Arguments is null ? "{}" : JsonLite.Serialize(call.Arguments)
                });
            }

            var executed = await ExecuteToolsAsync(writer, request.RequestId, threadId, extracted, parallelTools, allowMutatingParallel,
                    mutatingToolHints, toolTimeoutSeconds, turnToken)
                .ConfigureAwait(false);
            var failedCallsThisRound = CountFailedToolOutputs(executed);
            await WriteToolRoundCompletedStatusAsync(
                    writer,
                    request.RequestId,
                    threadId,
                    roundNumber,
                    maxRounds,
                    executed.Count,
                    failedCallsThisRound)
                .ConfigureAwait(false);
            UpdateToolRoutingStats(extracted, executed);
            foreach (var output in executed) {
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

            var next = new ChatInput();
            foreach (var output in executed) {
                next.AddToolOutput(output.CallId, output.Output);
            }
            turn = await RunModelPhaseWithProgressAsync(
                    client,
                    writer,
                    request.RequestId,
                    threadId,
                    next,
                    CopyChatOptions(options, newThreadOverride: false),
                    turnToken,
                    phaseStatus: planExecuteReviewLoop ? ChatStatusCodes.PhaseReview : ChatStatusCodes.Thinking,
                    phaseMessage: planExecuteReviewLoop
                        ? $"Reviewing {executed.Count} tool result(s) and deciding next steps..."
                        : $"Analyzing {executed.Count} tool result(s)...",
                    heartbeatLabel: "Reviewing tool results",
                    heartbeatSeconds: modelHeartbeatSeconds)
                .ConfigureAwait(false);
        }

        await WriteToolRoundLimitReachedStatusAsync(
                writer,
                request.RequestId,
                threadId,
                maxRounds,
                toolCalls.Count,
                toolOutputs.Count)
            .ConfigureAwait(false);
        TraceAutonomyTelemetryCounters(
            requestId: request.RequestId,
            threadId: threadId,
            nudgeUnknownEnvelopeReplanCount: nudgeUnknownEnvelopeReplanCount,
            noTextRecoveryHitCount: noTextRecoveryHitCount,
            noTextToolOutputRecoveryHitCount: noTextToolOutputRecoveryHitCount,
            proactiveSkipMutatingCount: proactiveSkipMutatingCount,
            proactiveSkipReadOnlyCount: proactiveSkipReadOnlyCount,
            proactiveSkipUnknownCount: proactiveSkipUnknownCount);

        throw new InvalidOperationException($"Tool runner exceeded max rounds ({maxRounds}).");
    }

}
