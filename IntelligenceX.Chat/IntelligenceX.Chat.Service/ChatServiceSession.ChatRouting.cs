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
        var userRequest = ExtractPrimaryUserRequest(request.Text);
        var userIntent = ExtractIntentUserText(request.Text);
        RememberUserIntent(threadId, userIntent);
        var routedUserRequest = ExpandContinuationUserRequest(threadId, userRequest);
        if (TryAugmentRoutedUserRequestFromWorkingMemoryCheckpoint(threadId, userRequest, routedUserRequest, out var checkpointAugmentedRequest)) {
            routedUserRequest = checkpointAugmentedRequest;
            await TryWriteStatusAsync(
                    writer,
                    request.RequestId,
                    threadId,
                    status: ChatStatusCodes.Routing,
                    message: "Recovered compact follow-up context from working-memory checkpoint.")
                .ConfigureAwait(false);
        }
        var requestedMaxCandidateTools = request.Options?.MaxCandidateTools;
        var maxCandidateToolDiagnostics = ResolveMaxCandidateToolsDiagnosticsForTurn(requestedMaxCandidateTools, client.TransportKind, selectedModel);
        var maxCandidateTools = maxCandidateToolDiagnostics.EffectiveMaxCandidateTools;
        var executionContractApplies = ShouldEnforceExecuteOrExplainContract(routedUserRequest);
        var proactiveModeEnabled = TryReadProactiveModeFromRequestText(request.Text, out var proactiveMode) && proactiveMode;
        if (TryResolvePendingDomainIntentClarificationSelection(threadId, userRequest, out var selectedDomainIntentFamily)) {
            routedUserRequest = routedUserRequest + "\n\n" + BuildDomainIntentSelectionRoutingHint(selectedDomainIntentFamily);
            await TryWriteStatusAsync(
                    writer,
                    request.RequestId,
                    threadId,
                    status: ChatStatusCodes.Routing,
                    message: $"Applied pending domain scope selection: family={DescribeDomainIntentFamily(selectedDomainIntentFamily)}.")
                .ConfigureAwait(false);
        }
        var compactFollowUpTurn = LooksLikeContinuationFollowUp(userRequest);
        var continuationFollowUpTurn = compactFollowUpTurn
                                       && !string.Equals(routedUserRequest, userRequest, StringComparison.Ordinal);
        var usedContinuationSubset = false;
        if (weightedToolRouting && toolDefs.Count > 0) {
            if (!executionContractApplies) {
                if (compactFollowUpTurn) {
                    // Keep follow-up turns unconstrained so users don't see "subset retry" rewrites for
                    // short continuation requests (for example compact follow-up text or ordinal selections).
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
        var planExecuteReviewLoop = request.Options?.PlanExecuteReviewLoop ?? false;
        var maxReviewPasses = ResolveMaxReviewPasses(request.Options);
        var modelHeartbeatSeconds = ResolveModelHeartbeatSeconds(request.Options);
        var requestedReviewPasses = request.Options?.MaxReviewPasses;
        var requestedModelHeartbeatSeconds = request.Options?.ModelHeartbeatSeconds;

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
                plannerInsightsDetected,
                requestedMaxCandidateTools: requestedMaxCandidateTools,
                effectiveMaxCandidateTools: maxCandidateTools,
                effectiveContextLength: maxCandidateToolDiagnostics.EffectiveContextLength,
                contextAwareBudgetApplied: maxCandidateToolDiagnostics.ContextAwareBudgetApplied);
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

        var forceDomainIntentClarification = ShouldForceDomainIntentClarificationForConflictingSignals(
            routedUserRequest,
            fullToolDefs);
        if (forceDomainIntentClarification || ShouldRequestDomainIntentClarification(
                weightedToolRouting: weightedToolRouting,
                executionContractApplies: executionContractApplies,
                usedContinuationSubset: usedContinuationSubset,
                selectedToolCount: routingSelectedToolCount,
                totalToolCount: routingTotalToolCount,
                selectedTools: toolDefs)) {
            var conflictingDomainSignals = HasConflictingDomainIntentSignals(routedUserRequest);
            if (conflictingDomainSignals) {
                ClearPreferredDomainIntentFamily(threadId);
            }

            if (!conflictingDomainSignals && TryApplyDomainIntentSignalRoutingHint(
                    threadId,
                    routedUserRequest,
                    toolDefs,
                    out var signaledTools,
                    out var signaledFamily,
                    out var signaledRemovedCount)) {
                toolDefs = signaledTools;
                (routingSelectedToolCount, routingTotalToolCount) = NormalizeRoutingToolCounts(toolDefs.Count, originalToolCount);
                await TryWriteStatusAsync(
                        writer,
                        request.RequestId,
                        threadId,
                        status: ChatStatusCodes.Routing,
                        message:
                        $"Tool routing detected explicit domain-scope signals (family={DescribeDomainIntentFamily(signaledFamily)}) and removed {signaledRemovedCount} conflicting candidate tool(s).")
                    .ConfigureAwait(false);
                var signalRoutingMetaPayload = BuildRoutingMetaPayload(
                    strategy: "domain_signal_hint",
                    weightedToolRouting,
                    executionContractApplies,
                    usedContinuationSubset,
                    selectedToolCount: routingSelectedToolCount,
                    totalToolCount: routingTotalToolCount,
                    insightCount: routingInsights.Count,
                    plannerInsightsDetected: false,
                    requestedMaxCandidateTools: requestedMaxCandidateTools,
                    effectiveMaxCandidateTools: maxCandidateTools,
                    effectiveContextLength: maxCandidateToolDiagnostics.EffectiveContextLength,
                    contextAwareBudgetApplied: maxCandidateToolDiagnostics.ContextAwareBudgetApplied);
                await TryWriteStatusAsync(
                        writer,
                        request.RequestId,
                        threadId,
                        status: ChatStatusCodes.RoutingMeta,
                        message: signalRoutingMetaPayload)
                    .ConfigureAwait(false);
            } else if (!conflictingDomainSignals
                       && TryApplyDomainIntentAffinity(threadId, toolDefs, out var affinedTools, out var affinityFamily, out var affinityRemovedCount)) {
                toolDefs = affinedTools;
                (routingSelectedToolCount, routingTotalToolCount) = NormalizeRoutingToolCounts(toolDefs.Count, originalToolCount);
                await TryWriteStatusAsync(
                        writer,
                        request.RequestId,
                        threadId,
                        status: ChatStatusCodes.Routing,
                        message:
                        $"Tool routing reused previous domain-scope context (family={DescribeDomainIntentFamily(affinityFamily)}) and removed {affinityRemovedCount} conflicting candidate tool(s).")
                    .ConfigureAwait(false);
                var affinityRoutingMetaPayload = BuildRoutingMetaPayload(
                    strategy: "domain_family_affinity",
                    weightedToolRouting,
                    executionContractApplies,
                    usedContinuationSubset,
                    selectedToolCount: routingSelectedToolCount,
                    totalToolCount: routingTotalToolCount,
                    insightCount: routingInsights.Count,
                    plannerInsightsDetected: false,
                    requestedMaxCandidateTools: requestedMaxCandidateTools,
                    effectiveMaxCandidateTools: maxCandidateTools,
                    effectiveContextLength: maxCandidateToolDiagnostics.EffectiveContextLength,
                    contextAwareBudgetApplied: maxCandidateToolDiagnostics.ContextAwareBudgetApplied);
                await TryWriteStatusAsync(
                        writer,
                        request.RequestId,
                        threadId,
                        status: ChatStatusCodes.RoutingMeta,
                        message: affinityRoutingMetaPayload)
                    .ConfigureAwait(false);
            } else {
                await TryWriteStatusAsync(
                        writer,
                        request.RequestId,
                        threadId,
                        status: ChatStatusCodes.Routing,
                        message: conflictingDomainSignals
                            ? forceDomainIntentClarification
                                ? "Tool routing detected conflicting domain-scope signals and forced structured clarification before execution."
                                : "Tool routing detected conflicting domain-scope signals (multiple families); requesting scope clarification before execution."
                            : "Tool routing detected mixed cross-family domain candidates; requesting scope clarification before execution.")
                    .ConfigureAwait(false);

                var clarificationText = BuildDomainIntentClarificationText();
                RememberPendingDomainIntentClarificationRequest(threadId);
                RememberPendingActions(threadId, clarificationText);
                var clarificationResult = new ChatResultMessage {
                    Kind = ChatServiceMessageKind.Response,
                    RequestId = request.RequestId,
                    ThreadId = threadId,
                    Text = clarificationText,
                    Tools = null,
                    TurnTimelineEvents = SnapshotTurnTimelineEvents(request.RequestId)
                };
                return new ChatTurnRunResult(
                    Result: clarificationResult,
                    Usage: null,
                    ToolCallsCount: 0,
                    ToolRounds: 0,
                    ProjectionFallbackCount: 0,
                    ToolErrors: Array.Empty<ToolErrorMetricDto>(),
                    ResolvedModel: null);
            }
        }

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
        var structuredNextActionRetryUsed = false;
        var toolProgressRecoveryUsed = false;
        var hostStructuredNextActionReplayUsed = false;
        var packCapabilityFallbackReplayUsed = false;
        var noResultPhaseLoopWatchdogUsed = false;
        var interimResultSent = false;
        var isLocalCompatibleLoopback = _options.OpenAITransport == OpenAITransportKind.CompatibleHttp
                                        && IsLoopbackEndpoint(_options.OpenAIBaseUrl);
        var supportsSyntheticHostReplayItems = SupportsSyntheticHostReplayItems(_options.OpenAITransport);
        var replayOutputCompactionBudget = ResolveReplayOutputCompactionBudgetForTurn(resolvedModel);

        var mutatingToolHints = BuildMutatingToolHintsByName(toolDefs);
        for (var round = 0; round < maxRounds; round++) {
            var extracted = ToolCallParser.Extract(turn);
            if (extracted.Count == 0) {
                var text = EasyChatResult.FromTurn(turn).Text ?? string.Empty;
                var controlPayloadDetected = isLocalCompatibleLoopback && LooksLikeRuntimeControlPayloadArtifact(text);
                if (controlPayloadDetected) {
                    text = string.Empty;
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

                if (!hostStructuredNextActionReplayUsed
                    && ShouldAttemptCarryoverStructuredNextActionReplay(
                        continuationFollowUpTurn: continuationFollowUpTurn,
                        compactFollowUpTurn: compactFollowUpTurn,
                        userRequest: routedUserRequest,
                        assistantDraft: text)
                    && toolCalls.Count == 0
                    && toolOutputs.Count == 0
                    && TryBuildCarryoverStructuredNextActionToolCall(
                        threadId: threadId,
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
                    continue;
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
                        continuationFollowUpTurn: continuationFollowUpTurn,
                        compactFollowUpTurn: compactFollowUpTurn,
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
                    continuationFollowUpTurn: continuationFollowUpTurn,
                    compactFollowUpTurn: compactFollowUpTurn,
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
                    continue;
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
                    continue;
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
                    continue;
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
                    continue;
                }

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
                        : $"execution_contract_unmet_{watchdogReason}";
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
                    continue;
                }

                if (!noResultWatchdogTriggered
                    && ShouldAttemptProactiveFollowUpReview(
                        proactiveModeEnabled: proactiveModeEnabled,
                        hasToolActivity: hasToolActivity,
                        proactiveFollowUpUsed: proactiveFollowUpUsed,
                        continuationFollowUpTurn: continuationFollowUpTurn,
                        compactFollowUpTurn: compactFollowUpTurn,
                        assistantDraft: text)) {
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
                return new ChatTurnRunResult(
                    Result: result,
                    Usage: turn.Usage,
                    ToolCallsCount: toolCalls.Count,
                    ToolRounds: toolRounds,
                    ProjectionFallbackCount: projectionFallbackCount,
                    ToolErrors: BuildToolErrorMetrics(toolCalls, toolOutputs),
                    ResolvedModel: resolvedModel);
            }

            var priorOutputsByCallId = BuildLatestToolOutputsByCallId(toolOutputs);
            var priorToolCallsByCallId = BuildLatestToolCallContractsByCallId(toolCalls);
            var callsToExecute = new List<ToolCall>(extracted.Count);
            var replayRecoveredOutputs = new List<ToolOutputDto>(extracted.Count);
            for (var callIndex = 0; callIndex < extracted.Count; callIndex++) {
                var call = extracted[callIndex];
                if (TryGetReplayRecoveredOutputForCall(call, priorOutputsByCallId, priorToolCallsByCallId, out var replayRecoveredOutput)) {
                    replayRecoveredOutputs.Add(replayRecoveredOutput);
                    continue;
                }

                callsToExecute.Add(call);
            }

            var hasFreshCallsToExecute = callsToExecute.Count > 0;
            var roundNumber = 0;
            if (hasFreshCallsToExecute) {
                toolRounds++;
                roundNumber = toolRounds;
                await WriteToolRoundStartedStatusAsync(
                        writer,
                        request.RequestId,
                        threadId,
                        roundNumber,
                        maxRounds,
                        callsToExecute.Count,
                        parallelTools,
                        allowMutatingParallel)
                    .ConfigureAwait(false);
                if (planExecuteReviewLoop) {
                    await TryWriteStatusAsync(
                            writer,
                            request.RequestId,
                            threadId,
                            status: ChatStatusCodes.PhaseExecute,
                            message: $"Executing {callsToExecute.Count} planned tool call(s)...")
                        .ConfigureAwait(false);
                }

                foreach (var call in callsToExecute) {
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
            }

            IReadOnlyList<ToolOutputDto> executed;
            if (hasFreshCallsToExecute) {
                executed = await ExecuteToolsAsync(
                        writer,
                        request.RequestId,
                        threadId,
                        callsToExecute,
                        parallelTools,
                        allowMutatingParallel,
                        mutatingToolHints,
                        toolTimeoutSeconds,
                        routedUserRequest,
                        turnToken)
                    .ConfigureAwait(false);
            } else {
                executed = Array.Empty<ToolOutputDto>();
            }

            if (hasFreshCallsToExecute) {
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
            }

            UpdateToolRoutingStats(callsToExecute, executed);
            var executedCallsById = new Dictionary<string, ToolCall>(StringComparer.OrdinalIgnoreCase);
            foreach (var call in extracted) {
                var normalizedCallId = (call.CallId ?? string.Empty).Trim();
                if (normalizedCallId.Length == 0) {
                    continue;
                }

                executedCallsById[normalizedCallId] = call;
            }

            for (var outputIndex = 0; outputIndex < executed.Count; outputIndex++) {
                var output = executed[outputIndex];
                if (WasProjectionFallbackApplied(output)) {
                    projectionFallbackCount++;
                }

                var normalizedOutputCallId = ResolveToolOutputCallId(
                    extracted,
                    executedCallsById,
                    output.CallId,
                    outputIndex);
                toolOutputs.Add(new ToolOutputDto {
                    CallId = normalizedOutputCallId.Length == 0 ? output.CallId : normalizedOutputCallId,
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

            var replayInputOutputs = MergeToolRoundReplayOutputs(executed, replayRecoveredOutputs);
            var next = BuildToolRoundReplayInputWithBudget(
                extracted,
                executedCallsById,
                replayInputOutputs,
                replayOutputCompactionBudget,
                out var replayOutputCompactionStats);
            if (ShouldEmitReplayOutputCompactionStatus(replayOutputCompactionStats)) {
                await TryWriteStatusAsync(
                        writer,
                        request.RequestId,
                        threadId,
                        status: ChatStatusCodes.ToolReplayCompacted,
                        message: BuildReplayOutputCompactionStatusMessage(replayOutputCompactionBudget, replayOutputCompactionStats))
                    .ConfigureAwait(false);
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

        throw new InvalidOperationException($"Tool runner exceeded max rounds ({maxRounds}).");
    }

    private static bool SupportsSyntheticHostReplayItems(OpenAITransportKind transport) {
        // Synthetic host replay items (custom_tool_call/custom_tool_call_output) are
        // only reliable on compatible HTTP transports. Native/AppServer/Copilot
        // runtimes may reject host-generated call ids.
        return transport == OpenAITransportKind.CompatibleHttp;
    }

    private const int DefaultMaxReplayToolOutputCharsPerCall = 6_000;
    private const int DefaultMaxReplayToolOutputCharsTotal = 16_000;
    private const int SmallContextMaxReplayToolOutputCharsPerCall = 2_500;
    private const int SmallContextMaxReplayToolOutputCharsTotal = 7_000;
    private const int MediumContextMaxReplayToolOutputCharsPerCall = 4_000;
    private const int MediumContextMaxReplayToolOutputCharsTotal = 11_000;
    private const int LargeContextMaxReplayToolOutputCharsPerCall = 8_000;
    private const int LargeContextMaxReplayToolOutputCharsTotal = 22_000;
    private const string ReplayOutputCompactionMarker = "ix:replay-output-compacted:v1";
    private const string ReplayOutputBudgetStatusMarker = "ix:replay-output-budget:v1";
    private const string ReplayOutputBudgetStatusWhere = "tool_replay_input";
    private const string ReplayOutputBudgetStatusReason = "output_budget_compaction";

    private readonly record struct PriorToolCallContract(string Name, string ArgumentsJson);
    private readonly record struct ReplayToolOutputSelection(string Output, bool MatchedRawCallId);
    private readonly record struct ReplayOutputCompactionBudget(
        int MaxOutputCharsPerCall,
        int MaxOutputCharsTotal,
        long? EffectiveContextLength,
        bool ContextAwareBudgetApplied);
    private readonly record struct ReplayOutputCompactionStats(
        int ReplayedCallCount,
        int OriginalTotalChars,
        int CompactedTotalChars,
        int CompactedCallCount);

    private static readonly ReplayOutputCompactionBudget DefaultReplayOutputCompactionBudget = new(
        MaxOutputCharsPerCall: DefaultMaxReplayToolOutputCharsPerCall,
        MaxOutputCharsTotal: DefaultMaxReplayToolOutputCharsTotal,
        EffectiveContextLength: null,
        ContextAwareBudgetApplied: false);

    private ReplayOutputCompactionBudget ResolveReplayOutputCompactionBudgetForTurn(string? selectedModel) {
        var effectiveContextLength = ResolveEffectiveModelContextLength(selectedModel);
        if (!effectiveContextLength.HasValue) {
            return DefaultReplayOutputCompactionBudget;
        }

        var (maxOutputCharsPerCall, maxOutputCharsTotal) = ResolveContextAwareReplayOutputCharBudgets(effectiveContextLength.Value);
        return new ReplayOutputCompactionBudget(
            MaxOutputCharsPerCall: maxOutputCharsPerCall,
            MaxOutputCharsTotal: maxOutputCharsTotal,
            EffectiveContextLength: effectiveContextLength.Value,
            ContextAwareBudgetApplied: true);
    }

    private static (int MaxOutputCharsPerCall, int MaxOutputCharsTotal) ResolveContextAwareReplayOutputCharBudgets(
        long effectiveContextLength) {
        if (effectiveContextLength <= 0) {
            return (DefaultMaxReplayToolOutputCharsPerCall, DefaultMaxReplayToolOutputCharsTotal);
        }

        if (effectiveContextLength <= 8_192) {
            return (SmallContextMaxReplayToolOutputCharsPerCall, SmallContextMaxReplayToolOutputCharsTotal);
        }

        if (effectiveContextLength <= 16_384) {
            return (MediumContextMaxReplayToolOutputCharsPerCall, MediumContextMaxReplayToolOutputCharsTotal);
        }

        if (effectiveContextLength <= 32_768) {
            return (DefaultMaxReplayToolOutputCharsPerCall, DefaultMaxReplayToolOutputCharsTotal);
        }

        return (LargeContextMaxReplayToolOutputCharsPerCall, LargeContextMaxReplayToolOutputCharsTotal);
    }

    private static bool ShouldEmitReplayOutputCompactionStatus(ReplayOutputCompactionStats stats) {
        return stats.CompactedCallCount > 0 && stats.CompactedTotalChars < stats.OriginalTotalChars;
    }

    private static string BuildReplayOutputCompactionStatusMessage(
        ReplayOutputCompactionBudget budget,
        ReplayOutputCompactionStats stats) {
        var contextLength = budget.EffectiveContextLength.HasValue ? budget.EffectiveContextLength.Value.ToString() : "unknown";
        var contextAware = budget.ContextAwareBudgetApplied ? "true" : "false";
        var contextTier = ResolveReplayOutputCompactionContextTier(budget.EffectiveContextLength);
        return $"[{ReplayOutputBudgetStatusMarker} where={ReplayOutputBudgetStatusWhere} reason={ReplayOutputBudgetStatusReason} compacted_calls={stats.CompactedCallCount} replayed_calls={stats.ReplayedCallCount} original_chars={stats.OriginalTotalChars} kept_chars={stats.CompactedTotalChars} per_call_budget={budget.MaxOutputCharsPerCall} total_budget={budget.MaxOutputCharsTotal} context_aware={contextAware} context_tier={contextTier} context_length={contextLength}]";
    }

    private static string ResolveReplayOutputCompactionContextTier(long? effectiveContextLength) {
        if (!effectiveContextLength.HasValue || effectiveContextLength.Value <= 0) {
            return "unknown";
        }

        if (effectiveContextLength.Value <= 8_192) {
            return "small";
        }

        if (effectiveContextLength.Value <= 16_384) {
            return "medium";
        }

        if (effectiveContextLength.Value <= 32_768) {
            return "default";
        }

        return "large";
    }

    private static string ResolveToolOutputCallId(
        IReadOnlyList<ToolCall> extractedCalls,
        IReadOnlyDictionary<string, ToolCall> extractedCallsById,
        string? rawOutputCallId,
        int outputIndex) {
        _ = TryResolveToolOutputCallId(
            extractedCalls,
            extractedCallsById,
            rawOutputCallId,
            outputIndex,
            out var normalizedOutputCallId,
            out _);
        return normalizedOutputCallId;
    }

    private static bool TryResolveToolOutputCallId(
        IReadOnlyList<ToolCall> extractedCalls,
        IReadOnlyDictionary<string, ToolCall> extractedCallsById,
        string? rawOutputCallId,
        int outputIndex,
        out string normalizedOutputCallId,
        out bool matchedRawCallId) {
        normalizedOutputCallId = string.Empty;
        matchedRawCallId = false;
        var directOutputCallId = (rawOutputCallId ?? string.Empty).Trim();
        if (directOutputCallId.Length > 0 && extractedCallsById.ContainsKey(directOutputCallId)) {
            normalizedOutputCallId = directOutputCallId;
            matchedRawCallId = true;
            return true;
        }

        if (outputIndex >= 0 && outputIndex < extractedCalls.Count) {
            var fallbackCallId = (extractedCalls[outputIndex].CallId ?? string.Empty).Trim();
            if (fallbackCallId.Length > 0) {
                normalizedOutputCallId = fallbackCallId;
                return true;
            }
        }

        if (extractedCallsById.Count == 1) {
            foreach (var pair in extractedCallsById) {
                var singleCallId = (pair.Key ?? string.Empty).Trim();
                if (singleCallId.Length > 0) {
                    normalizedOutputCallId = singleCallId;
                    return true;
                }
            }
        }

        return false;
    }

    private static Dictionary<string, ToolOutputDto> BuildLatestToolOutputsByCallId(IReadOnlyList<ToolOutputDto> outputs) {
        var byCallId = new Dictionary<string, ToolOutputDto>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < outputs.Count; i++) {
            var callId = (outputs[i].CallId ?? string.Empty).Trim();
            if (callId.Length == 0) {
                continue;
            }

            byCallId[callId] = outputs[i];
        }

        return byCallId;
    }

    private static Dictionary<string, PriorToolCallContract> BuildLatestToolCallContractsByCallId(IReadOnlyList<ToolCallDto> calls) {
        var byCallId = new Dictionary<string, PriorToolCallContract>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < calls.Count; i++) {
            var callId = (calls[i].CallId ?? string.Empty).Trim();
            if (callId.Length == 0) {
                continue;
            }

            var callName = (calls[i].Name ?? string.Empty).Trim();
            var argumentsJson = NormalizeArgumentsJsonForReplayContract(calls[i].ArgumentsJson);
            byCallId[callId] = new PriorToolCallContract(callName, argumentsJson);
        }

        return byCallId;
    }

    private static string NormalizeArgumentsJsonForReplayContract(string? argumentsJson) {
        var value = (argumentsJson ?? string.Empty).Trim();
        if (value.Length == 0) {
            return "{}";
        }

        try {
            var parsed = JsonLite.Parse(value);
            if (parsed is null) {
                return "{}";
            }

            return JsonLite.Serialize(parsed);
        } catch {
            return value;
        }
    }

    private static bool CallMatchesReplayRecoveredContract(ToolCall call, PriorToolCallContract priorContract) {
        var currentName = (call.Name ?? string.Empty).Trim();
        if (priorContract.Name.Length > 0
            && currentName.Length > 0
            && !string.Equals(currentName, priorContract.Name, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        var currentArgumentsJson = call.Arguments is null
            ? "{}"
            : NormalizeArgumentsJsonForReplayContract(JsonLite.Serialize(call.Arguments));
        return string.Equals(currentArgumentsJson, priorContract.ArgumentsJson, StringComparison.Ordinal);
    }

    private static bool TryGetReplayRecoveredOutputForCall(ToolCall call, IReadOnlyDictionary<string, ToolOutputDto> outputsByCallId,
        IReadOnlyDictionary<string, PriorToolCallContract> priorCallsByCallId,
        out ToolOutputDto replayRecoveredOutput) {
        var callId = (call.CallId ?? string.Empty).Trim();
        if (callId.Length > 0
            && outputsByCallId.TryGetValue(callId, out var priorOutput)
            && (!priorCallsByCallId.TryGetValue(callId, out var priorCall) || CallMatchesReplayRecoveredContract(call, priorCall))) {
            replayRecoveredOutput = priorOutput with { CallId = callId };
            return true;
        }

        replayRecoveredOutput = default!;
        return false;
    }

    private static IReadOnlyList<ToolOutputDto> MergeToolRoundReplayOutputs(IReadOnlyList<ToolOutputDto> executed,
        IReadOnlyList<ToolOutputDto> replayRecoveredOutputs) {
        if (replayRecoveredOutputs.Count == 0) {
            return executed;
        }

        var merged = new List<ToolOutputDto>(executed.Count + replayRecoveredOutputs.Count);
        merged.AddRange(executed);
        merged.AddRange(replayRecoveredOutputs);
        return merged;
    }

    private static ChatInput BuildToolRoundReplayInput(
        IReadOnlyList<ToolCall> extractedCalls,
        IReadOnlyDictionary<string, ToolCall> extractedCallsById,
        IReadOnlyList<ToolOutputDto> outputs) {
        return BuildToolRoundReplayInputWithBudget(
            extractedCalls,
            extractedCallsById,
            outputs,
            DefaultReplayOutputCompactionBudget,
            out _);
    }

    private static ChatInput BuildToolRoundReplayInputWithBudget(
        IReadOnlyList<ToolCall> extractedCalls,
        IReadOnlyDictionary<string, ToolCall> extractedCallsById,
        IReadOnlyList<ToolOutputDto> outputs,
        ReplayOutputCompactionBudget compactionBudget,
        out ReplayOutputCompactionStats compactionStats) {
        var next = new ChatInput();
        var replayedCallIdsInOrder = new List<string>();
        var replayedCallsById = new Dictionary<string, ToolCall>(StringComparer.OrdinalIgnoreCase);
        var selectedOutputsByCallId = new Dictionary<string, ReplayToolOutputSelection>(StringComparer.OrdinalIgnoreCase);
        for (var outputIndex = 0; outputIndex < outputs.Count; outputIndex++) {
            var output = outputs[outputIndex];
            if (!TryResolveToolOutputCallId(
                    extractedCalls,
                    extractedCallsById,
                    output.CallId,
                    outputIndex,
                    out var normalizedOutputCallId,
                    out var matchedRawCallId)) {
                continue;
            }

            if (normalizedOutputCallId.Length == 0) {
                continue;
            }

            if (!extractedCallsById.TryGetValue(normalizedOutputCallId, out var executedCall)) {
                continue;
            }

            if (replayedCallsById.TryAdd(normalizedOutputCallId, executedCall)) {
                replayedCallIdsInOrder.Add(normalizedOutputCallId);
            }

            var candidateOutput = new ReplayToolOutputSelection(
                Output: output.Output ?? string.Empty,
                MatchedRawCallId: matchedRawCallId);
            if (!selectedOutputsByCallId.TryGetValue(normalizedOutputCallId, out var existingOutput)) {
                selectedOutputsByCallId[normalizedOutputCallId] = candidateOutput;
                continue;
            }

            if (candidateOutput.MatchedRawCallId || !existingOutput.MatchedRawCallId) {
                selectedOutputsByCallId[normalizedOutputCallId] = candidateOutput;
            }
        }

        selectedOutputsByCallId = CompactReplayOutputsByBudget(
            replayedCallIdsInOrder,
            selectedOutputsByCallId,
            compactionBudget,
            out compactionStats);

        for (var replayIndex = 0; replayIndex < replayedCallIdsInOrder.Count; replayIndex++) {
            var replayCallId = replayedCallIdsInOrder[replayIndex];
            if (!replayedCallsById.TryGetValue(replayCallId, out var replayCall)) {
                continue;
            }

            next.AddToolCall(
                replayCallId,
                replayCall.Name,
                replayCall.Input);
            if (selectedOutputsByCallId.TryGetValue(replayCallId, out var selectedOutput)) {
                next.AddToolOutput(replayCallId, selectedOutput.Output);
            }
        }

        return next;
    }

    private static Dictionary<string, ReplayToolOutputSelection> CompactReplayOutputsByBudget(
        IReadOnlyList<string> replayedCallIdsInOrder,
        IReadOnlyDictionary<string, ReplayToolOutputSelection> selectedOutputsByCallId,
        ReplayOutputCompactionBudget compactionBudget,
        out ReplayOutputCompactionStats compactionStats) {
        var remainingChars = Math.Max(0, compactionBudget.MaxOutputCharsTotal);
        var maxOutputCharsPerCall = Math.Max(0, compactionBudget.MaxOutputCharsPerCall);
        var replayedCallCount = 0;
        var originalTotalChars = 0;
        var compactedTotalChars = 0;
        var compactedCallCount = 0;
        var compactedByCallId = new Dictionary<string, ReplayToolOutputSelection>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < replayedCallIdsInOrder.Count; i++) {
            var callId = replayedCallIdsInOrder[i];
            if (string.IsNullOrWhiteSpace(callId) || !selectedOutputsByCallId.TryGetValue(callId, out var selectedOutput)) {
                continue;
            }

            replayedCallCount++;

            var originalOutput = selectedOutput.Output ?? string.Empty;
            originalTotalChars += originalOutput.Length;

            string compactedOutput;
            if (remainingChars <= 0 || maxOutputCharsPerCall <= 0) {
                compactedOutput = string.Empty;
            } else {
                var maxOutputChars = Math.Min(maxOutputCharsPerCall, remainingChars);
                compactedOutput = CompactReplayOutputText(originalOutput, maxOutputChars);
            }

            compactedByCallId[callId] = selectedOutput with { Output = compactedOutput };
            compactedTotalChars += compactedOutput.Length;
            if (compactedOutput.Length < originalOutput.Length) {
                compactedCallCount++;
            }

            remainingChars = Math.Max(0, remainingChars - compactedOutput.Length);
        }

        compactionStats = new ReplayOutputCompactionStats(
            ReplayedCallCount: replayedCallCount,
            OriginalTotalChars: originalTotalChars,
            CompactedTotalChars: compactedTotalChars,
            CompactedCallCount: compactedCallCount);
        return compactedByCallId;
    }

    private static string CompactReplayOutputText(string output, int maxOutputChars) {
        var source = output ?? string.Empty;
        if (maxOutputChars <= 0) {
            return string.Empty;
        }

        if (source.Length <= maxOutputChars) {
            return source;
        }

        var markerLine = $"[{ReplayOutputCompactionMarker} original_chars={source.Length} kept_chars={maxOutputChars}]";
        var budgetForContent = maxOutputChars - markerLine.Length - 2;
        if (budgetForContent < 16) {
            var headOnlyLength = Math.Max(0, maxOutputChars - markerLine.Length - 1);
            if (headOnlyLength <= 0) {
                return markerLine.Length > maxOutputChars ? markerLine[..maxOutputChars] : markerLine;
            }

            var headOnly = source[..Math.Min(source.Length, headOnlyLength)];
            return headOnly + "\n" + markerLine;
        }

        var prefixLength = budgetForContent / 2;
        var suffixLength = budgetForContent - prefixLength;
        var prefix = source[..prefixLength];
        var suffix = source[^suffixLength..];
        return BuildReplayCompactedOutputEnvelope(prefix + suffix, source.Length, maxOutputChars, prefix, suffix);
    }

    private static string BuildReplayCompactedOutputEnvelope(string output, int originalLength, int maxOutputChars, string? prefix = null,
        string? suffix = null) {
        var markerLine = $"[{ReplayOutputCompactionMarker} original_chars={originalLength} kept_chars={maxOutputChars}]";
        if (prefix is null || suffix is null) {
            return output.Length == 0 ? markerLine : output + "\n" + markerLine;
        }

        return prefix + "\n" + markerLine + "\n" + suffix;
    }

    private static ChatInput BuildHostReplayReviewInput(
        ToolCall executedCall,
        IReadOnlyList<ToolOutputDto> outputs,
        bool supportsSyntheticReplayItems) {
        if (supportsSyntheticReplayItems
            && TryBuildSyntheticHostReplayInput(executedCall, outputs, out var syntheticInput)) {
            return syntheticInput;
        }

        return ChatInput.FromText(BuildNativeHostReplayReviewPrompt(executedCall, outputs));
    }

    private static bool TryBuildSyntheticHostReplayInput(
        ToolCall executedCall,
        IReadOnlyList<ToolOutputDto> outputs,
        out ChatInput input) {
        input = null!;

        var executedCallId = (executedCall.CallId ?? string.Empty).Trim();
        if (executedCallId.Length == 0 || outputs.Count == 0) {
            return false;
        }

        for (var i = 0; i < outputs.Count; i++) {
            var outputCallId = (outputs[i].CallId ?? string.Empty).Trim();
            if (outputCallId.Length == 0) {
                continue;
            }

            if (!string.Equals(outputCallId, executedCallId, StringComparison.OrdinalIgnoreCase)) {
                return false;
            }
        }

        var nextInput = new ChatInput();
        nextInput.AddToolCall(executedCallId, executedCall.Name, executedCall.Input);
        for (var i = 0; i < outputs.Count; i++) {
            var output = outputs[i];
            var outputCallId = (output.CallId ?? string.Empty).Trim();
            nextInput.AddToolOutput(outputCallId.Length == 0 ? executedCallId : outputCallId, output.Output);
        }

        input = nextInput;
        return true;
    }

    private static string BuildNativeHostReplayReviewPrompt(ToolCall executedCall, IReadOnlyList<ToolOutputDto> outputs) {
        const int maxOutputsInPrompt = 3;
        const int maxOutputCharsPerItem = 3_000;
        const int maxOutputCharsTotal = 9_000;
        const int maxInputChars = 2_000;

        var sb = new StringBuilder();
        sb.AppendLine("ix:host-replay-review:v1");
        sb.AppendLine("A read-only follow-up action was already executed by the host runtime.");
        sb.AppendLine("Continue from the evidence below and provide the user-facing answer.");
        sb.AppendLine("Do not ask to rerun this same action and do not require synthetic tool call replay.");
        sb.AppendLine();

        var toolName = (executedCall.Name ?? string.Empty).Trim();
        var callId = (executedCall.CallId ?? string.Empty).Trim();
        sb.AppendLine("executed_tool: " + (toolName.Length == 0 ? "<unknown>" : toolName));
        sb.AppendLine("executed_call_id: " + (callId.Length == 0 ? "<unknown>" : callId));

        var inputJson = TruncateForHostReplayPrompt(executedCall.Input, maxInputChars);
        if (inputJson.Length > 0) {
            sb.AppendLine("executed_input_json:");
            sb.AppendLine("```json");
            sb.AppendLine(inputJson);
            sb.AppendLine("```");
        }

        if (outputs.Count == 0) {
            sb.AppendLine("tool_results: none");
            sb.AppendLine("If results are missing, explain the blocker briefly and request only the minimum next input.");
            return sb.ToString().TrimEnd();
        }

        sb.AppendLine("tool_results:");
        var emittedOutputChars = 0;
        var outputCount = Math.Min(outputs.Count, maxOutputsInPrompt);
        for (var i = 0; i < outputCount; i++) {
            var output = outputs[i];
            var outputCallId = (output.CallId ?? string.Empty).Trim();
            var errorCode = (output.ErrorCode ?? string.Empty).Trim();
            var error = (output.Error ?? string.Empty).Trim();

            sb.Append("result[").Append(i + 1).Append("] ");
            sb.Append("call_id=").Append(outputCallId.Length == 0 ? "<unknown>" : outputCallId);
            sb.Append(" ok=").Append(output.Ok == true ? "true" : "false");
            if (errorCode.Length > 0) {
                sb.Append(" error_code=").Append(errorCode);
            }
            if (error.Length > 0) {
                sb.Append(" error=").Append(TruncateForHostReplayPrompt(error, 240));
            }
            sb.AppendLine();

            var remainingBudget = maxOutputCharsTotal - emittedOutputChars;
            if (remainingBudget <= 0) {
                sb.AppendLine("output: <omitted due to prompt budget>");
                continue;
            }

            var itemBudget = Math.Min(maxOutputCharsPerItem, remainingBudget);
            var outputText = TruncateForHostReplayPrompt(output.Output, itemBudget);
            emittedOutputChars += outputText.Length;
            if (outputText.Length == 0) {
                sb.AppendLine("output: <empty>");
                continue;
            }

            sb.AppendLine("output:");
            sb.AppendLine("```");
            sb.AppendLine(outputText);
            sb.AppendLine("```");
        }

        if (outputs.Count > outputCount) {
            sb.AppendLine("additional_results_omitted: " + (outputs.Count - outputCount));
        }

        sb.AppendLine("Return the concise final answer with evidence.");
        return sb.ToString().TrimEnd();
    }

    private static string TruncateForHostReplayPrompt(string? value, int maxChars) {
        if (maxChars <= 0) {
            return string.Empty;
        }

        var text = (value ?? string.Empty).Trim();
        if (text.Length <= maxChars) {
            return text;
        }

        if (maxChars < 64) {
            return text.Substring(0, maxChars);
        }

        var omitted = text.Length - maxChars;
        return text.Substring(0, maxChars) + Environment.NewLine + $"...[truncated {omitted} chars]";
    }

}
