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
        toolDefs = ApplyToolExposureOverrides(
            toolDefs,
            request.Options?.EnabledTools,
            request.Options?.DisabledTools,
            request.Options?.EnabledPackIds,
            request.Options?.DisabledPackIds,
            _toolOrchestrationCatalog);
        toolDefs = SanitizeToolDefinitions(toolDefs);

        var selectedModel = request.Options?.Model ?? _options.Model;
        if (toolDefs.Count > 0 && ShouldDisableToolsForSelectedModel(client.TransportKind, selectedModel)) {
            toolDefs = Array.Empty<ToolDefinition>();
        }

        var fullToolDefs = toolDefs.Count == 0 ? Array.Empty<ToolDefinition>() : toolDefs.ToArray();
        var domainIntentFamilyAvailability = ResolveDomainIntentFamilyAvailability(fullToolDefs);
        var originalToolCount = toolDefs.Count;
        var routingInsights = new List<ToolRoutingInsight>();
        long? weightedSubsetSelectionMs = null;
        long? resolveModelMs = null;
        var weightedToolRouting = request.Options?.WeightedToolRouting ?? true;
        var userRequest = ExtractPrimaryUserRequest(request.Text);
        var userIntent = ExtractIntentUserText(request.Text);
        var continuationContractDetected = TryReadContinuationContractFromRequestText(request.Text, out _, out _);
        var hasFreshPendingActionContext = HasFreshPendingActionsContext(threadId);
        RememberUserIntent(threadId, userIntent);
        var routedUserRequest = ExpandContinuationUserRequestWithOptions(threadId, userRequest, forceContinuationFollowUp: continuationContractDetected);
        var continuationExpandedFromContext = !string.Equals(routedUserRequest, userRequest, StringComparison.Ordinal);
        if (TryAugmentRoutedUserRequestFromWorkingMemoryCheckpoint(threadId, userRequest, routedUserRequest, out var checkpointAugmentedRequest)) {
            routedUserRequest = checkpointAugmentedRequest;
            continuationExpandedFromContext = !string.Equals(routedUserRequest, userRequest, StringComparison.Ordinal);
            await TryWriteStatusAsync(
                    writer,
                    request.RequestId,
                    threadId,
                    status: ChatStatusCodes.Routing,
                    message: "Recovered compact follow-up context from working-memory checkpoint.")
                .ConfigureAwait(false);
        }
        var hasStructuredContinuationContext = continuationContractDetected
                                              || hasFreshPendingActionContext
                                              || continuationExpandedFromContext;
        var requestedMaxCandidateTools = request.Options?.MaxCandidateTools;
        var maxCandidateToolDiagnostics = ResolveMaxCandidateToolsDiagnosticsForTurn(requestedMaxCandidateTools, client.TransportKind, selectedModel);
        var maxCandidateTools = maxCandidateToolDiagnostics.EffectiveMaxCandidateTools;
        var executionContractApplies = ShouldEnforceExecuteOrExplainContract(routedUserRequest);
        var proactiveModeEnabled = TryReadProactiveModeFromRequestText(request.Text, out var proactiveMode) && proactiveMode;
        if (TryResolvePendingDomainIntentClarificationSelection(
                threadId,
                userRequest,
                fullToolDefs,
                out var selectedDomainIntentFamily)) {
            routedUserRequest = routedUserRequest + "\n\n" + BuildDomainIntentSelectionRoutingHint(selectedDomainIntentFamily);
            await TryWriteStatusAsync(
                    writer,
                    request.RequestId,
                    threadId,
                    status: ChatStatusCodes.Routing,
                    message: $"Applied pending domain scope selection: family={DescribeDomainIntentFamily(selectedDomainIntentFamily)}.")
                .ConfigureAwait(false);
        }
        var (continuationFollowUpTurn, compactFollowUpTurn) = ResolveFollowUpTurnClassification(
            continuationContractDetected,
            hasStructuredContinuationContext,
            userRequest,
            routedUserRequest);
        var structuredCompactFollowUpTurn = continuationContractDetected && compactFollowUpTurn;
        var usedContinuationSubset = false;
        if (weightedToolRouting && toolDefs.Count > 0) {
            if (!executionContractApplies) {
                if (structuredCompactFollowUpTurn) {
                    // Keep follow-up turns unconstrained so users don't see "subset retry" rewrites for
                    // explicit structured continuation requests.
                    routingInsights = new List<ToolRoutingInsight>();
                } else if (!TryGetContinuationToolSubset(threadId, userRequest, toolDefs, out var continuationSubset)) {
                    await TryWriteStatusAsync(
                            writer,
                            request.RequestId,
                            threadId,
                            status: ChatStatusCodes.Routing,
                            message: $"Selecting relevant tools from {toolDefs.Count} available candidate(s)...")
                        .ConfigureAwait(false);
                    var weightedSubsetStopwatch = Stopwatch.StartNew();
                    var routed = await SelectWeightedToolSubsetAsync(
                            client,
                            threadId,
                            toolDefs,
                            routedUserRequest,
                            maxCandidateTools,
                            cancellationToken)
                        .ConfigureAwait(false);
                    weightedSubsetSelectionMs = Math.Max(0L, weightedSubsetStopwatch.ElapsedMilliseconds);
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
        var weightedAmbiguityWidened = TryResolveWeightedRoutingAmbiguityTelemetry(
            routingInsights,
            out var weightedAmbiguityBaselineSelection,
            out var weightedAmbiguityEffectiveSelection,
            out var weightedAmbiguityClusterSize,
            out var weightedAmbiguitySecondScoreRatio);
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
                contextAwareBudgetApplied: maxCandidateToolDiagnostics.ContextAwareBudgetApplied,
                domainIntentSource: null,
                domainIntentFamily: null,
                weightedAmbiguityWidened,
                weightedAmbiguityBaselineSelection,
                weightedAmbiguityEffectiveSelection,
                weightedAmbiguityClusterSize,
                weightedAmbiguitySecondScoreRatio);
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

        var domainIntentSignalRequest = userRequest;
        var conflictingDomainSignals = domainIntentFamilyAvailability.HasMixedFamilies
                                       && HasConflictingDomainIntentSignals(domainIntentSignalRequest, fullToolDefs);
        if (conflictingDomainSignals) {
            ClearPreferredDomainIntentFamily(threadId);
        }

        var domainIntentRoutingResolved = false;
        if (!conflictingDomainSignals
            && TryApplyDomainIntentSignalRoutingHint(
                threadId,
                domainIntentSignalRequest,
                toolDefs,
                fullToolDefs,
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
                contextAwareBudgetApplied: maxCandidateToolDiagnostics.ContextAwareBudgetApplied,
                domainIntentSource: "signal_hint",
                domainIntentFamily: signaledFamily,
                weightedAmbiguityWidened,
                weightedAmbiguityBaselineSelection,
                weightedAmbiguityEffectiveSelection,
                weightedAmbiguityClusterSize,
                weightedAmbiguitySecondScoreRatio);
            await TryWriteStatusAsync(
                    writer,
                    request.RequestId,
                    threadId,
                    status: ChatStatusCodes.RoutingMeta,
                    message: signalRoutingMetaPayload)
                .ConfigureAwait(false);
            domainIntentRoutingResolved = true;
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
                contextAwareBudgetApplied: maxCandidateToolDiagnostics.ContextAwareBudgetApplied,
                domainIntentSource: "affinity",
                domainIntentFamily: affinityFamily,
                weightedAmbiguityWidened,
                weightedAmbiguityBaselineSelection,
                weightedAmbiguityEffectiveSelection,
                weightedAmbiguityClusterSize,
                weightedAmbiguitySecondScoreRatio);
            await TryWriteStatusAsync(
                    writer,
                    request.RequestId,
                    threadId,
                    status: ChatStatusCodes.RoutingMeta,
                    message: affinityRoutingMetaPayload)
                .ConfigureAwait(false);
            domainIntentRoutingResolved = true;
        }

        var forceDomainIntentClarification = ShouldForceDomainIntentClarificationForConflictingSignals(
            domainIntentSignalRequest,
            fullToolDefs);
        var shouldRequestDomainIntentClarification = ShouldRequestDomainIntentClarification(
            weightedToolRouting: weightedToolRouting,
            executionContractApplies: executionContractApplies,
            usedContinuationSubset: usedContinuationSubset,
            selectedToolCount: routingSelectedToolCount,
            totalToolCount: routingTotalToolCount,
            selectedTools: toolDefs);
        var hasPreferredDomainIntentFamily = TryGetCurrentDomainIntentFamily(threadId, out var preferredDomainIntentFamily);
        if (hasPreferredDomainIntentFamily
            && !IsDomainIntentFamilyAvailable(domainIntentFamilyAvailability, preferredDomainIntentFamily)) {
            ClearPreferredDomainIntentFamily(threadId);
            hasPreferredDomainIntentFamily = false;
        }
        if (ShouldSuppressDomainIntentClarificationForCompactFollowUp(
                structuredCompactFollowUpTurn,
                hasPreferredDomainIntentFamily,
                hasFreshPendingActionContext,
                conflictingDomainSignals)) {
            shouldRequestDomainIntentClarification = false;
        }

        if (!domainIntentRoutingResolved
            && (forceDomainIntentClarification || shouldRequestDomainIntentClarification)) {
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

            var actionCatalog = ResolveDomainIntentActionCatalog(fullToolDefs);
            var clarificationText = BuildDomainIntentClarificationText(domainIntentFamilyAvailability, actionCatalog);
            var clarificationVisibleText = BuildDomainIntentClarificationVisibleText(domainIntentFamilyAvailability, actionCatalog);
            if (clarificationText.Length > 0 && clarificationVisibleText.Length > 0) {
                RememberPendingDomainIntentClarificationRequest(threadId);
                RememberPendingActions(threadId, clarificationText);
                var clarificationResult = new ChatResultMessage {
                    Kind = ChatServiceMessageKind.Response,
                    RequestId = request.RequestId,
                    ThreadId = threadId,
                    Text = clarificationVisibleText,
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
                    AutonomyCounters: Array.Empty<TurnCounterMetricDto>(),
                    ResolvedModel: null,
                    WeightedSubsetSelectionMs: weightedSubsetSelectionMs,
                    ResolveModelMs: resolveModelMs);
            }
        }

        await TryWriteStatusAsync(
                writer,
                request.RequestId,
                threadId,
                status: ChatStatusCodes.Thinking,
                message: "Resolving runtime model and preparing first response...")
            .ConfigureAwait(false);

        var resolveModelStopwatch = Stopwatch.StartNew();
        var resolvedModel = await ResolveTurnModelAsync(client, request, turnToken).ConfigureAwait(false);
        resolveModelMs = Math.Max(0L, resolveModelStopwatch.ElapsedMilliseconds);

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

        var firstTurnInputText = request.Text;
        if (continuationFollowUpTurn) {
            var continuationContractEnvelope = BuildContinuationContractEnvelope(routedUserRequest, userRequest);
            if (continuationContractEnvelope.Length > 0) {
                firstTurnInputText = continuationContractEnvelope;
            }
        }

        TurnInfo turn = await RunModelPhaseWithProgressAsync(
                client,
                writer,
                request.RequestId,
                threadId,
                ChatInput.FromText(firstTurnInputText),
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
        var noToolExecutionWatchdogReason = "not_evaluated";
        var executionContractEscapeUsed = false;
        var continuationSubsetEscapeUsed = false;
        var autoPendingActionReplayUsed = false;
        var proactiveFollowUpUsed = false;
        var localNoTextDirectRetryUsed = false;
        var noTextToolOutputDirectRetryUsed = false;
        var structuredNextActionRetryUsed = false;
        var toolProgressRecoveryUsed = false;
        var hostStructuredNextActionReplayUsed = false;
        var hostDomainIntentBootstrapReplayUsed = false;
        var noResultPhaseLoopWatchdogUsed = false;
        var interimResultSent = false;
        var lastNonEmptyAssistantDraft = string.Empty;
        var nudgeUnknownEnvelopeReplanCount = 0;
        var noTextRecoveryHitCount = 0;
        var noTextToolOutputRecoveryHitCount = 0;
        var proactiveSkipMutatingCount = 0;
        var proactiveSkipReadOnlyCount = 0;
        var proactiveSkipUnknownCount = 0;
        var isLocalCompatibleLoopback = _options.OpenAITransport == OpenAITransportKind.CompatibleHttp
                                        && IsLoopbackEndpoint(_options.OpenAIBaseUrl);
        var supportsSyntheticHostReplayItems = SupportsSyntheticHostReplayItems(_options.OpenAITransport);
        var replayOutputCompactionBudget = ResolveReplayOutputCompactionBudgetForTurn(resolvedModel);

        var mutatingToolHints = BuildMutatingToolHintsByName(toolDefs);
        for (var round = 0; round < maxRounds; round++) {
            var extracted = ToolCallParser.Extract(turn);
            if (extracted.Count == 0) {
                var assistantDraft = EasyChatResult.FromTurn(turn).Text ?? string.Empty;
                var controlPayloadDetected = isLocalCompatibleLoopback && LooksLikeRuntimeControlPayloadArtifact(assistantDraft);
                if (!controlPayloadDetected && !string.IsNullOrWhiteSpace(assistantDraft)) {
                    lastNonEmptyAssistantDraft = assistantDraft.Trim();
                }

                var noExtractedRoundState = new NoExtractedToolRoundState(
                    turn: turn,
                    assistantDraft: assistantDraft,
                    controlPayloadDetected: controlPayloadDetected,
                    routedUserRequest: routedUserRequest,
                    executionContractApplies: executionContractApplies,
                    toolDefs: toolDefs,
                    options: options,
                    usedContinuationSubset: usedContinuationSubset,
                    toolRounds: toolRounds,
                    projectionFallbackCount: projectionFallbackCount,
                    reviewPassesUsed: reviewPassesUsed,
                    executionNudgeUsed: executionNudgeUsed,
                    toolReceiptCorrectionUsed: toolReceiptCorrectionUsed,
                    noToolExecutionWatchdogUsed: noToolExecutionWatchdogUsed,
                    noToolExecutionWatchdogReason: noToolExecutionWatchdogReason,
                    executionContractEscapeUsed: executionContractEscapeUsed,
                    continuationSubsetEscapeUsed: continuationSubsetEscapeUsed,
                    autoPendingActionReplayUsed: autoPendingActionReplayUsed,
                    proactiveFollowUpUsed: proactiveFollowUpUsed,
                    localNoTextDirectRetryUsed: localNoTextDirectRetryUsed,
                    noTextToolOutputDirectRetryUsed: noTextToolOutputDirectRetryUsed,
                    structuredNextActionRetryUsed: structuredNextActionRetryUsed,
                    toolProgressRecoveryUsed: toolProgressRecoveryUsed,
                    hostStructuredNextActionReplayUsed: hostStructuredNextActionReplayUsed,
                    hostDomainIntentBootstrapReplayUsed: hostDomainIntentBootstrapReplayUsed,
                    noResultPhaseLoopWatchdogUsed: noResultPhaseLoopWatchdogUsed,
                    lastNonEmptyAssistantDraft: lastNonEmptyAssistantDraft,
                    nudgeUnknownEnvelopeReplanCount: nudgeUnknownEnvelopeReplanCount,
                    noTextRecoveryHitCount: noTextRecoveryHitCount,
                    noTextToolOutputRecoveryHitCount: noTextToolOutputRecoveryHitCount,
                    proactiveSkipMutatingCount: proactiveSkipMutatingCount,
                    proactiveSkipReadOnlyCount: proactiveSkipReadOnlyCount,
                    proactiveSkipUnknownCount: proactiveSkipUnknownCount,
                    interimResultSent: interimResultSent);

                var noExtractedRecoveryOutcome = await HandleNoExtractedToolCallsRecoveryAsync(
                        client: client,
                        writer: writer,
                        request: request,
                        threadId: threadId,
                        round: round,
                        maxRounds: maxRounds,
                        parallelTools: parallelTools,
                        allowMutatingParallel: allowMutatingParallel,
                        planExecuteReviewLoop: planExecuteReviewLoop,
                        modelHeartbeatSeconds: modelHeartbeatSeconds,
                        toolTimeoutSeconds: toolTimeoutSeconds,
                        continuationFollowUpTurn: continuationFollowUpTurn,
                        compactFollowUpTurn: compactFollowUpTurn,
                        isLocalCompatibleLoopback: isLocalCompatibleLoopback,
                        supportsSyntheticHostReplayItems: supportsSyntheticHostReplayItems,
                        userRequest: userRequest,
                        fullToolDefs: fullToolDefs,
                        mutatingToolHints: mutatingToolHints,
                        originalToolCount: originalToolCount,
                        toolCalls: toolCalls,
                        toolOutputs: toolOutputs,
                        extracted: extracted,
                        turnToken: turnToken,
                        state: noExtractedRoundState)
                    .ConfigureAwait(false);
                if (noExtractedRecoveryOutcome.Flow == NoExtractedToolRoundFlow.ReturnFinal
                    && noExtractedRecoveryOutcome.FinalResult is not null) {
                    return noExtractedRecoveryOutcome.FinalResult;
                }

                if (noExtractedRecoveryOutcome.Flow == NoExtractedToolRoundFlow.Continue) {
                    RestoreNoExtractedToolRoundState(noExtractedRoundState, ref turn, ref routedUserRequest, ref executionContractApplies, ref toolDefs, ref options,
                        ref usedContinuationSubset, ref toolRounds, ref projectionFallbackCount, ref reviewPassesUsed, ref executionNudgeUsed,
                        ref toolReceiptCorrectionUsed, ref noToolExecutionWatchdogUsed, ref noToolExecutionWatchdogReason, ref executionContractEscapeUsed, ref continuationSubsetEscapeUsed,
                        ref autoPendingActionReplayUsed, ref proactiveFollowUpUsed, ref localNoTextDirectRetryUsed, ref noTextToolOutputDirectRetryUsed, ref structuredNextActionRetryUsed,
                        ref toolProgressRecoveryUsed, ref hostStructuredNextActionReplayUsed, ref hostDomainIntentBootstrapReplayUsed, ref noResultPhaseLoopWatchdogUsed,
                        ref lastNonEmptyAssistantDraft, ref nudgeUnknownEnvelopeReplanCount, ref noTextRecoveryHitCount, ref noTextToolOutputRecoveryHitCount,
                        ref proactiveSkipMutatingCount, ref proactiveSkipReadOnlyCount, ref proactiveSkipUnknownCount,
                        ref interimResultSent);
                    continue;
                }

                var noExtractedFinalizeOutcome = await HandleNoExtractedToolCallsFinalizeAsync(
                        client: client,
                        writer: writer,
                        request: request,
                        threadId: threadId,
                        round: round,
                        maxRounds: maxRounds,
                        parallelTools: parallelTools,
                        allowMutatingParallel: allowMutatingParallel,
                        planExecuteReviewLoop: planExecuteReviewLoop,
                        maxReviewPasses: maxReviewPasses,
                        modelHeartbeatSeconds: modelHeartbeatSeconds,
                        toolTimeoutSeconds: toolTimeoutSeconds,
                        continuationFollowUpTurn: continuationFollowUpTurn,
                        compactFollowUpTurn: compactFollowUpTurn,
                        proactiveModeEnabled: proactiveModeEnabled,
                        isLocalCompatibleLoopback: isLocalCompatibleLoopback,
                        supportsSyntheticHostReplayItems: supportsSyntheticHostReplayItems,
                        resolvedModel: resolvedModel,
                        weightedSubsetSelectionMs: weightedSubsetSelectionMs,
                        resolveModelMs: resolveModelMs,
                        userIntent: userIntent,
                        fullToolDefs: fullToolDefs,
                        mutatingToolHints: mutatingToolHints,
                        originalToolCount: originalToolCount,
                        toolCalls: toolCalls,
                        toolOutputs: toolOutputs,
                        extracted: extracted,
                        turnToken: turnToken,
                        state: noExtractedRoundState)
                    .ConfigureAwait(false);
                if (noExtractedFinalizeOutcome.Flow == NoExtractedToolRoundFlow.ReturnFinal
                    && noExtractedFinalizeOutcome.FinalResult is not null) {
                    return noExtractedFinalizeOutcome.FinalResult;
                }

                RestoreNoExtractedToolRoundState(noExtractedRoundState, ref turn, ref routedUserRequest, ref executionContractApplies, ref toolDefs, ref options,
                    ref usedContinuationSubset, ref toolRounds, ref projectionFallbackCount, ref reviewPassesUsed, ref executionNudgeUsed,
                    ref toolReceiptCorrectionUsed, ref noToolExecutionWatchdogUsed, ref noToolExecutionWatchdogReason, ref executionContractEscapeUsed, ref continuationSubsetEscapeUsed,
                    ref autoPendingActionReplayUsed, ref proactiveFollowUpUsed, ref localNoTextDirectRetryUsed, ref noTextToolOutputDirectRetryUsed, ref structuredNextActionRetryUsed,
                    ref toolProgressRecoveryUsed, ref hostStructuredNextActionReplayUsed, ref hostDomainIntentBootstrapReplayUsed, ref noResultPhaseLoopWatchdogUsed,
                    ref lastNonEmptyAssistantDraft, ref nudgeUnknownEnvelopeReplanCount, ref noTextRecoveryHitCount, ref noTextToolOutputRecoveryHitCount,
                    ref proactiveSkipMutatingCount, ref proactiveSkipReadOnlyCount, ref proactiveSkipUnknownCount,
                    ref interimResultSent);
                continue;
            }

            var hostPackPreflightCalls = BuildHostPackPreflightCalls(threadId, fullToolDefs, extracted);
            IReadOnlyList<ToolCall> roundCalls;
            if (hostPackPreflightCalls.Count == 0) {
                roundCalls = extracted;
            } else {
                var mergedRoundCalls = new List<ToolCall>(hostPackPreflightCalls.Count + extracted.Count);
                mergedRoundCalls.AddRange(hostPackPreflightCalls);
                mergedRoundCalls.AddRange(extracted);
                roundCalls = mergedRoundCalls;
            }

            var priorOutputsByCallId = BuildLatestToolOutputsByCallId(toolOutputs);
            var priorToolCallsByCallId = BuildLatestToolCallContractsByCallId(toolCalls);
            var callsToExecute = new List<ToolCall>(roundCalls.Count);
            var replayRecoveredOutputs = new List<ToolOutputDto>(roundCalls.Count);
            for (var callIndex = 0; callIndex < roundCalls.Count; callIndex++) {
                var call = roundCalls[callIndex];
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
                var executeTask = ExecuteToolsAsync(
                    writer,
                    request.RequestId,
                    threadId,
                    callsToExecute,
                    parallelTools,
                    allowMutatingParallel,
                    mutatingToolHints,
                    toolTimeoutSeconds,
                    routedUserRequest,
                    turnToken);
                if (planExecuteReviewLoop) {
                    await RunPhaseProgressLoopAsync(
                            writer,
                            request.RequestId,
                            threadId,
                            phaseStatus: ChatStatusCodes.PhaseExecute,
                            phaseMessage: $"Executing {callsToExecute.Count} planned tool call(s)...",
                            heartbeatLabel: "Executing tool plan",
                            heartbeatSeconds: modelHeartbeatSeconds,
                            cancellationToken: turnToken,
                            phaseTask: executeTask)
                        .ConfigureAwait(false);
                }

                executed = await executeTask.ConfigureAwait(false);
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
            RememberSuccessfulPackPreflightCalls(threadId, callsToExecute, executed);
            var executedCallsById = new Dictionary<string, ToolCall>(StringComparer.OrdinalIgnoreCase);
            foreach (var call in roundCalls) {
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
                    roundCalls,
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
                roundCalls,
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

}
