using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using IntelligenceX.Json;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private const string WeightedRoutingAmbiguityMarker = "ix:routing-ambiguity:v1";
    private const double WeightedRoutingAmbiguousSecondScoreRatioThreshold = 0.92d;
    private const double WeightedRoutingAmbiguousClusterScoreRatioThreshold = 0.82d;
    private const int WeightedRoutingAmbiguousClusterMinCount = 3;
    private const int WeightedRoutingAmbiguousSelectionFloor = 10;
    private const int WeightedRoutingAmbiguousSelectionCap = 12;
    private const int MaxWeightedRoutingFocusTokens = 12;
    private const double WeightedRoutingFocusTokenScore = 2d;

    private readonly record struct WeightedRoutingSelectionDiagnostics(
        bool AmbiguityWidened,
        int BaselineMinSelection,
        int EffectiveMinSelection,
        int AmbiguousClusterSize,
        double SecondScoreRatio);

    private static void TraceNoToolExecutionWatchdogDecision(
        string userRequest,
        bool executionContractApplies,
        bool toolsAvailable,
        int priorToolCalls,
        int priorToolOutputs,
        int assistantDraftToolCalls,
        bool continuationFollowUpTurn,
        bool compactFollowUpTurn,
        bool executionNudgeUsed,
        bool toolReceiptCorrectionUsed,
        bool watchdogAlreadyUsed,
        bool shouldRetry,
        string reason) {
        if (!ShouldTraceNoToolExecutionWatchdogDecision(
                executionContractApplies,
                continuationFollowUpTurn,
                compactFollowUpTurn)) {
            return;
        }

        var normalized = (userRequest ?? string.Empty).Trim();
        var tokenCount = CountLetterDigitTokens(normalized, maxTokens: 16);
        var outcome = shouldRetry ? "retry" : "skip";
        var mode = ResolveNoToolExecutionWatchdogMode(
            executionContractApplies,
            continuationFollowUpTurn,
            compactFollowUpTurn);
        var contractState = executionContractApplies ? "true" : "false";
        var watchdogState = watchdogAlreadyUsed ? "used" : "unused";
        var nudgeState = executionNudgeUsed ? "used" : "unused";
        var receiptState = toolReceiptCorrectionUsed ? "used" : "unused";
        Console.Error.WriteLine(
            $"[tool-watchdog] outcome={outcome} reason={reason} mode={mode} contract={contractState} watchdog={watchdogState} nudge={nudgeState} receipt={receiptState} tools={toolsAvailable} prior_calls={Math.Max(0, priorToolCalls)} prior_outputs={Math.Max(0, priorToolOutputs)} draft_calls={Math.Max(0, assistantDraftToolCalls)} tokens={tokenCount}");
    }

    internal static bool ShouldTraceNoToolExecutionWatchdogDecision(
        bool executionContractApplies,
        bool continuationFollowUpTurn,
        bool compactFollowUpTurn) {
        return executionContractApplies || continuationFollowUpTurn || compactFollowUpTurn;
    }

    internal static string ResolveNoToolExecutionWatchdogMode(
        bool executionContractApplies,
        bool continuationFollowUpTurn,
        bool compactFollowUpTurn) {
        if (executionContractApplies) {
            return "contract";
        }

        if (compactFollowUpTurn) {
            return "compact_follow_up";
        }

        if (continuationFollowUpTurn) {
            return "follow_up";
        }

        return "standard";
    }

    private static void TraceToolExecutionNudgeDecision(
        string userRequest,
        bool usedContinuationSubset,
        bool toolsAvailable,
        int priorToolCalls,
        int assistantDraftToolCalls,
        bool executionNudgeAlreadyUsed,
        bool shouldAttemptNudge,
        string reason) {
        var normalized = (userRequest ?? string.Empty).Trim();
        var isFollowUp = LooksLikeContinuationFollowUp(normalized);
        var isActionPayload = LooksLikeActionSelectionPayload(normalized);
        var forceTrace = string.Equals(
            Environment.GetEnvironmentVariable("IX_CHAT_TRACE_TOOL_NUDGE"),
            "1",
            StringComparison.Ordinal);
        if (!forceTrace && !isFollowUp && !isActionPayload) {
            return;
        }

        var tokenCount = CountLetterDigitTokens(normalized, maxTokens: 16);
        var kind = isActionPayload ? "action_payload" : "follow_up";
        var routing = usedContinuationSubset ? "subset" : "full";
        var outcome = shouldAttemptNudge ? "retry" : "skip";
        var nudgeState = executionNudgeAlreadyUsed ? "used" : "unused";

        Console.Error.WriteLine(
            $"[tool-nudge] outcome={outcome} reason={reason} kind={kind} routing={routing} nudge={nudgeState} tools={toolsAvailable} prior_calls={Math.Max(0, priorToolCalls)} draft_calls={Math.Max(0, assistantDraftToolCalls)} tokens={tokenCount}");
    }

    private static void TraceAutonomyTelemetryCounters(
        string requestId,
        string threadId,
        int nudgeUnknownEnvelopeReplanCount,
        int noTextRecoveryHitCount,
        int noTextToolOutputRecoveryHitCount,
        int proactiveSkipMutatingCount,
        int proactiveSkipReadOnlyCount,
        int proactiveSkipUnknownCount) {
        var normalizedRequestId = string.IsNullOrWhiteSpace(requestId) ? "-" : requestId.Trim();
        var normalizedThreadId = string.IsNullOrWhiteSpace(threadId) ? "-" : threadId.Trim();
        Console.Error.WriteLine(
            $"[autonomy-counters] request={normalizedRequestId} thread={normalizedThreadId} nudge_replan_unknown={Math.Max(0, nudgeUnknownEnvelopeReplanCount)} no_text_recovery_hits={Math.Max(0, noTextRecoveryHitCount)} no_text_tool_output_recovery_hits={Math.Max(0, noTextToolOutputRecoveryHitCount)} proactive_skip_mutating={Math.Max(0, proactiveSkipMutatingCount)} proactive_skip_readonly={Math.Max(0, proactiveSkipReadOnlyCount)} proactive_skip_unknown={Math.Max(0, proactiveSkipUnknownCount)}");
    }

    private static IReadOnlyList<TurnCounterMetricDto> BuildAutonomyCounterMetrics(
        int nudgeUnknownEnvelopeReplanCount,
        int noTextRecoveryHitCount,
        int noTextToolOutputRecoveryHitCount,
        int proactiveSkipMutatingCount,
        int proactiveSkipReadOnlyCount,
        int proactiveSkipUnknownCount) {
        var counters = new List<TurnCounterMetricDto>(capacity: 6);
        Add("nudge_replan_unknown_pending_action_envelope", nudgeUnknownEnvelopeReplanCount);
        Add("no_text_recovery_hits", noTextRecoveryHitCount);
        Add("no_text_tool_output_recovery_hits", noTextToolOutputRecoveryHitCount);
        Add("proactive_skip_mutating", proactiveSkipMutatingCount);
        Add("proactive_skip_readonly", proactiveSkipReadOnlyCount);
        Add("proactive_skip_unknown", proactiveSkipUnknownCount);
        return counters.Count == 0 ? Array.Empty<TurnCounterMetricDto>() : counters;

        void Add(string name, int count) {
            if (count <= 0) {
                return;
            }

            counters.Add(new TurnCounterMetricDto {
                Name = name,
                Count = count
            });
        }
    }

    private static AutonomyTelemetryDto BuildAutonomyTelemetrySummary(
        int toolRounds,
        int projectionFallbackCount,
        IReadOnlyList<ToolErrorMetricDto>? toolErrors,
        IReadOnlyList<TurnCounterMetricDto>? autonomyCounters,
        bool completed) {
        var recoveryEvents = Math.Max(0, projectionFallbackCount);

        if (toolErrors is { Count: > 0 }) {
            recoveryEvents += toolErrors.Sum(static metric => Math.Max(0, metric.Count));
        }

        if (autonomyCounters is { Count: > 0 }) {
            recoveryEvents += autonomyCounters.Sum(static counter => Math.Max(0, counter.Count));
        }

        return new AutonomyTelemetryDto {
            AutonomyDepth = Math.Max(0, toolRounds),
            RecoveryEvents = recoveryEvents,
            CompletionRate = completed ? 1.0d : 0.0d
        };
    }

    private static IReadOnlyList<ToolErrorMetricDto> BuildToolErrorMetrics(
        IReadOnlyList<ToolCallDto> calls,
        IReadOnlyList<ToolOutputDto> outputs) {
        if (calls.Count == 0 || outputs.Count == 0) {
            return Array.Empty<ToolErrorMetricDto>();
        }

        var nameByCallId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < calls.Count; i++) {
            var call = calls[i];
            var callId = (call.CallId ?? string.Empty).Trim();
            var toolName = (call.Name ?? string.Empty).Trim();
            if (callId.Length == 0 || toolName.Length == 0) {
                continue;
            }

            nameByCallId[callId] = toolName;
        }

        if (nameByCallId.Count == 0) {
            return Array.Empty<ToolErrorMetricDto>();
        }

        var counts = new Dictionary<(string ToolName, string ErrorCode), int>();
        for (var i = 0; i < outputs.Count; i++) {
            var output = outputs[i];
            var callId = (output.CallId ?? string.Empty).Trim();
            if (callId.Length == 0 || !nameByCallId.TryGetValue(callId, out var toolName)) {
                continue;
            }

            var errorCode = NormalizeToolErrorCode(output);
            if (errorCode.Length == 0) {
                continue;
            }

            var key = (toolName, errorCode);
            counts.TryGetValue(key, out var count);
            counts[key] = count + 1;
        }

        if (counts.Count == 0) {
            return Array.Empty<ToolErrorMetricDto>();
        }

        return counts
            .Select(pair => new ToolErrorMetricDto {
                ToolName = pair.Key.ToolName,
                ErrorCode = pair.Key.ErrorCode,
                Count = pair.Value
            })
            .OrderByDescending(metric => metric.Count)
            .ThenBy(metric => metric.ToolName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(metric => metric.ErrorCode, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizeToolErrorCode(ToolOutputDto output) {
        var errorCode = (output.ErrorCode ?? string.Empty).Trim();
        if (errorCode.Length > 0) {
            return errorCode;
        }

        if (output.Ok is false || !string.IsNullOrWhiteSpace(output.Error)) {
            return "tool_error";
        }

        return string.Empty;
    }

    private static string AppendTurnCompletionNotice(string text, TurnInfo turn) {
        var status = (turn.Status ?? string.Empty).Trim();
        if (status.Length == 0 || string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase)) {
            return text;
        }

        var reason = ResolveTurnCompletionReason(turn);
        var details = reason.Length == 0
            ? $"status '{status}'"
            : $"status '{status}' (reason: {reason})";
        var notice = $"Partial response: model returned {details}. Share your next step to resume.";

        var body = (text ?? string.Empty).TrimEnd();
        if (body.Length == 0) {
            return notice;
        }

        if (body.IndexOf("Partial response:", StringComparison.OrdinalIgnoreCase) >= 0) {
            return body;
        }

        return body + Environment.NewLine + Environment.NewLine + notice;
    }

    private static bool ShouldEmitInterimResultSnapshot(string assistantDraft) {
        var draft = (assistantDraft ?? string.Empty).Trim();
        if (draft.Length < 48 || draft.Length > 6_000) {
            return false;
        }

        if (draft.Contains(ExecutionContractMarker, StringComparison.OrdinalIgnoreCase)
            || draft.Contains(ExecutionWatchdogMarker, StringComparison.OrdinalIgnoreCase)
            || draft.Contains(ResponseReviewMarker, StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        return true;
    }

    private static string ResolveTurnCompletionReason(TurnInfo turn) {
        try {
            var response = turn.Raw.GetObject("response");
            if (response is null) {
                return string.Empty;
            }

            var incompleteDetails = response.GetObject("incomplete_details");
            var reason = (incompleteDetails?.GetString("reason") ?? string.Empty).Trim();
            if (reason.Length > 0) {
                return reason;
            }

            return (response.GetString("status_details") ?? string.Empty).Trim();
        } catch {
            return string.Empty;
        }
    }

    private static IReadOnlyList<ToolDefinition> SanitizeToolDefinitions(IReadOnlyList<ToolDefinition> definitions) {
        if (definitions.Count == 0) {
            return Array.Empty<ToolDefinition>();
        }

        var sanitized = new List<ToolDefinition>(definitions.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < definitions.Count; i++) {
            var definition = definitions[i];
            if (definition is null) {
                continue;
            }

            var normalizedName = (definition.Name ?? string.Empty).Trim();
            if (normalizedName.Length == 0 || !seen.Add(normalizedName)) {
                continue;
            }

            sanitized.Add(definition);
        }

        return sanitized.Count == 0 ? Array.Empty<ToolDefinition>() : sanitized;
    }

    private static IReadOnlyList<ToolDefinition> ApplyToolExposureOverrides(
        IReadOnlyList<ToolDefinition> definitions,
        string[]? enabledTools,
        string[]? disabledTools,
        string[]? enabledPackIds,
        string[]? disabledPackIds,
        ToolOrchestrationCatalog? toolOrchestrationCatalog) {
        if (definitions.Count == 0) {
            return Array.Empty<ToolDefinition>();
        }

        var enabledSpecified = enabledTools is not null;
        var enabledPacksSpecified = enabledPackIds is not null;
        var enabled = NormalizeToolNameSet(enabledTools);
        var disabled = NormalizeToolNameSet(disabledTools);
        var enabledPacks = NormalizePackIdSet(enabledPackIds);
        var disabledPacks = NormalizePackIdSet(disabledPackIds);
        if (!enabledSpecified
            && !enabledPacksSpecified
            && (disabled is null || disabled.Count == 0)
            && (disabledPacks is null || disabledPacks.Count == 0)) {
            return definitions;
        }
        if (enabledSpecified && (enabled is null || enabled.Count == 0)) {
            return Array.Empty<ToolDefinition>();
        }
        if (enabledPacksSpecified && (enabledPacks is null || enabledPacks.Count == 0)) {
            return Array.Empty<ToolDefinition>();
        }

        var filtered = new List<ToolDefinition>(definitions.Count);
        for (var i = 0; i < definitions.Count; i++) {
            var definition = definitions[i];
            if (definition is null) {
                continue;
            }

            var normalizedName = (definition.Name ?? string.Empty).Trim();
            if (normalizedName.Length == 0) {
                continue;
            }

            if (enabledSpecified && !enabled!.Contains(normalizedName)) {
                continue;
            }

            if (disabled is { Count: > 0 } && disabled.Contains(normalizedName)) {
                continue;
            }

            if (enabledPacksSpecified || disabledPacks is { Count: > 0 }) {
                var packId = ResolveToolPackId(definition, toolOrchestrationCatalog);
                if (enabledPacksSpecified && (packId.Length == 0 || !enabledPacks!.Contains(packId))) {
                    continue;
                }

                if (disabledPacks is { Count: > 0 } && packId.Length > 0 && disabledPacks.Contains(packId)) {
                    continue;
                }
            }

            filtered.Add(definition);
        }

        return filtered.Count == 0 ? Array.Empty<ToolDefinition>() : filtered;
    }

    private static HashSet<string>? NormalizeToolNameSet(string[]? toolNames) {
        if (toolNames is null) {
            return null;
        }

        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < toolNames.Length; i++) {
            var name = (toolNames[i] ?? string.Empty).Trim();
            if (name.Length > 0) {
                normalized.Add(name);
            }
        }

        return normalized;
    }

    private static HashSet<string>? NormalizePackIdSet(string[]? packIds) {
        if (packIds is null) {
            return null;
        }

        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < packIds.Length; i++) {
            var packId = ToolPackBootstrap.NormalizePackId(packIds[i]);
            if (packId.Length > 0) {
                normalized.Add(packId);
            }
        }

        return normalized;
    }

    private static string ResolveToolPackId(ToolDefinition definition, ToolOrchestrationCatalog? toolOrchestrationCatalog) {
        if (toolOrchestrationCatalog is not null
            && toolOrchestrationCatalog.TryGetPackId(definition.Name, out var catalogPackId)
            && catalogPackId.Length > 0) {
            return catalogPackId;
        }

        return ToolPackBootstrap.NormalizePackId(definition.Routing?.PackId);
    }

    private async Task<(IReadOnlyList<ToolDefinition> Definitions, List<ToolRoutingInsight> Insights)> SelectWeightedToolSubsetAsync(
        IntelligenceXClient client,
        string threadId,
        IReadOnlyList<ToolDefinition> definitions,
        string requestText,
        int? maxCandidateTools,
        CancellationToken cancellationToken) {
        if (definitions.Count <= 12) {
            return (definitions, new List<ToolRoutingInsight>());
        }

        var userRequest = ExtractPrimaryUserRequest(requestText);
        var limit = ResolveMaxCandidateToolsLimit(maxCandidateTools, definitions.Count);
        if (limit >= definitions.Count) {
            return (definitions, new List<ToolRoutingInsight>());
        }

        if (ShouldSkipWeightedRouting(userRequest)) {
            return (definitions, new List<ToolRoutingInsight>());
        }

        var plannerRequestText = BuildPlannerContextAugmentedRequest(threadId, requestText, definitions);
        var plannerCandidates = BuildModelPlannerCandidates(definitions, plannerRequestText, limit, _toolOrchestrationCatalog);
        var planned = await TrySelectToolsViaModelPlannerAsync(client, threadId, plannerRequestText, plannerCandidates, limit, cancellationToken)
            .ConfigureAwait(false);
        if (planned.Count > 0) {
            var selected = EnsureMinimumToolSelection(userRequest, definitions, planned, limit);
            if (selected.Count > 0 && selected.Count < definitions.Count) {
                var plannerInsights = BuildModelRoutingInsights(selected, plannedCount: planned.Count);
                return (selected, plannerInsights);
            }
        }

        var fallback = SelectWeightedToolSubset(definitions, userRequest, maxCandidateTools, out var fallbackInsights);
        return (fallback, fallbackInsights);
    }

    private static IReadOnlyList<ToolDefinition> BuildModelPlannerCandidates(
        IReadOnlyList<ToolDefinition> definitions,
        string requestText,
        int limit,
        ToolOrchestrationCatalog toolOrchestrationCatalog) {
        if (definitions.Count <= 64) {
            return definitions;
        }

        var minCandidateLimit = Math.Max(24, limit);
        var candidateLimit = Math.Clamp(Math.Max(limit * 3, minCandidateLimit), minCandidateLimit, Math.Min(definitions.Count, 96));
        var focusTokens = ResolveWeightedRoutingFocusTokens(requestText, Array.Empty<string>());
        _ = TryReadPlannerContextFromRequestText(requestText, out var plannerContext);
        var (derivedHandoffTargetPackIds, derivedHandoffTargetToolNames) =
            DerivePlannerHandoffTargetsFromContext(plannerContext, toolOrchestrationCatalog);
        var preferredToolNames = new HashSet<string>(plannerContext.PreferredToolNames, StringComparer.OrdinalIgnoreCase);
        var handoffTargetToolNames = new HashSet<string>(
            plannerContext.HandoffTargetToolNames.Concat(derivedHandoffTargetToolNames),
            StringComparer.OrdinalIgnoreCase);
        var preferredPackIds = new HashSet<string>(plannerContext.PreferredPackIds, StringComparer.OrdinalIgnoreCase);
        var handoffTargetPackIds = new HashSet<string>(
            plannerContext.HandoffTargetPackIds.Concat(derivedHandoffTargetPackIds),
            StringComparer.OrdinalIgnoreCase);
        var prefersRemoteCapableTools = HasRemoteHostRoutingHint(requestText, plannerContext);
        var prefersCrossPackContinuation =
            handoffTargetPackIds.Count > 0
            || handoffTargetToolNames.Count > 0
            || plannerContext.StructuredNextActionSourceToolNames.Length > 0
            || plannerContext.ContinuationSourceTool.Length > 0;
        var prefersEnvironmentBootstrap = ShouldPreferEnvironmentBootstrap(
            plannerContext,
            preferredPackIds,
            preferredToolNames,
            handoffTargetPackIds,
            handoffTargetToolNames);
        var prefersSetupAwareTools = ShouldPreferSetupAwareTools(
            plannerContext,
            preferredPackIds,
            preferredToolNames,
            handoffTargetPackIds,
            handoffTargetToolNames);
        if (focusTokens.Length == 0) {
            if (preferredToolNames.Count == 0
                && handoffTargetToolNames.Count == 0
                && preferredPackIds.Count == 0
                && handoffTargetPackIds.Count == 0) {
                return SelectDeterministicToolSubset(definitions, candidateLimit, toolOrchestrationCatalog);
            }
        }

        var scored = new List<(ToolDefinition Definition, int Priority, int FocusHits, string SearchText)>(definitions.Count);
        for (var i = 0; i < definitions.Count; i++) {
            var definition = definitions[i];
            var searchText = BuildToolRoutingSearchText(definition);
            var focusHits = 0;
            var priority = 0;
            var toolName = (definition.Name ?? string.Empty).Trim();
            var packId = ResolveToolPackId(definition, toolOrchestrationCatalog);
            if (preferredToolNames.Contains(toolName)) {
                priority += 1000;
            }
            if (handoffTargetToolNames.Contains(toolName)) {
                priority += 800;
            }
            if (preferredPackIds.Contains(packId)) {
                priority += 400;
            }
            if (handoffTargetPackIds.Contains(packId)) {
                priority += 200;
            }
            if (prefersRemoteCapableTools && ToolSupportsRemoteHostTargeting(definition, toolOrchestrationCatalog)) {
                priority += PlannerRemoteCapablePriorityBoost;
            }
            if (prefersCrossPackContinuation && ToolSupportsCrossPackHandoff(definition, toolOrchestrationCatalog)) {
                priority += PlannerCrossPackContinuationPriorityBoost;
            }
            if (prefersEnvironmentBootstrap
                && ToolSupportsEnvironmentDiscovery(definition, toolOrchestrationCatalog)
                && ToolMatchesPlannerContractTargets(toolName, packId, preferredToolNames, handoffTargetToolNames, preferredPackIds, handoffTargetPackIds)) {
                priority += PlannerEnvironmentDiscoverPriorityBoost;
            }
            if (prefersSetupAwareTools
                && ToolIsSetupAware(definition, toolOrchestrationCatalog)
                && ToolMatchesPlannerContractTargets(toolName, packId, preferredToolNames, handoffTargetToolNames, preferredPackIds, handoffTargetPackIds)) {
                priority += PlannerSetupAwarePriorityBoost;
            }
            for (var t = 0; t < focusTokens.Length; t++) {
                var token = focusTokens[t];
                if (token.Length == 0) {
                    continue;
                }

                if (searchText.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0) {
                    focusHits++;
                }
            }

            scored.Add((definition, priority, focusHits, searchText));
        }

        scored.Sort(static (left, right) => {
            var priorityCompare = right.Priority.CompareTo(left.Priority);
            if (priorityCompare != 0) {
                return priorityCompare;
            }

            var hitCompare = right.FocusHits.CompareTo(left.FocusHits);
            if (hitCompare != 0) {
                return hitCompare;
            }

            return StringComparer.OrdinalIgnoreCase.Compare(left.Definition.Name, right.Definition.Name);
        });

        var selected = new List<ToolDefinition>(candidateLimit);
        var selectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < scored.Count && selected.Count < candidateLimit; i++) {
            if (scored[i].Priority <= 0 && scored[i].FocusHits <= 0) {
                break;
            }

            var definition = scored[i].Definition;
            var name = (definition.Name ?? string.Empty).Trim();
            if (name.Length == 0 || !selectedNames.Add(name)) {
                continue;
            }

            selected.Add(definition);
        }

        if (selected.Count >= candidateLimit) {
            return selected;
        }

        var deterministicBackfill = SelectDeterministicToolSubset(definitions, candidateLimit, toolOrchestrationCatalog);
        for (var i = 0; i < deterministicBackfill.Count && selected.Count < candidateLimit; i++) {
            var definition = deterministicBackfill[i];
            var name = (definition.Name ?? string.Empty).Trim();
            if (name.Length == 0 || !selectedNames.Add(name)) {
                continue;
            }

            selected.Add(definition);
        }

        return selected.Count == 0 ? Array.Empty<ToolDefinition>() : selected;
    }

    private IReadOnlyList<ToolDefinition> SelectWeightedToolSubset(IReadOnlyList<ToolDefinition> definitions, string requestText, int? maxCandidateTools,
        out List<ToolRoutingInsight> insights) {
        insights = new List<ToolRoutingInsight>();
        if (definitions.Count <= 12) {
            return definitions;
        }

        var userRequest = ExtractPrimaryUserRequest(requestText);
        var limit = ResolveMaxCandidateToolsLimit(maxCandidateTools, definitions.Count);
        if (limit >= definitions.Count) {
            return definitions;
        }

        if (ShouldSkipWeightedRouting(userRequest)) {
            return definitions;
        }

        var explicitRequestedToolNames = BuildExplicitRequestedToolNameSet(userRequest);
        var routingTokens = TokenizeRoutingTokens(userRequest, maxTokens: 16);
        var focusTokens = ResolveWeightedRoutingFocusTokens(requestText, routingTokens);
        _ = TryReadPlannerContextFromRequestText(requestText, out var plannerContext);
        var (derivedHandoffTargetPackIds, derivedHandoffTargetToolNames) =
            DerivePlannerHandoffTargetsFromContext(plannerContext, _toolOrchestrationCatalog);
        var preferredToolNames = new HashSet<string>(plannerContext.PreferredToolNames, StringComparer.OrdinalIgnoreCase);
        var handoffTargetToolNames = new HashSet<string>(
            plannerContext.HandoffTargetToolNames.Concat(derivedHandoffTargetToolNames),
            StringComparer.OrdinalIgnoreCase);
        var preferredPackIds = new HashSet<string>(plannerContext.PreferredPackIds, StringComparer.OrdinalIgnoreCase);
        var handoffTargetPackIds = new HashSet<string>(
            plannerContext.HandoffTargetPackIds.Concat(derivedHandoffTargetPackIds),
            StringComparer.OrdinalIgnoreCase);
        var prefersRemoteCapableTools = HasRemoteHostRoutingHint(requestText, plannerContext);
        var prefersCrossPackContinuation =
            handoffTargetPackIds.Count > 0
            || handoffTargetToolNames.Count > 0
            || plannerContext.StructuredNextActionSourceToolNames.Length > 0
            || plannerContext.ContinuationSourceTool.Length > 0;
        var prefersEnvironmentBootstrap = ShouldPreferEnvironmentBootstrap(
            plannerContext,
            preferredPackIds,
            preferredToolNames,
            handoffTargetPackIds,
            handoffTargetToolNames);
        var prefersSetupAwareTools = ShouldPreferSetupAwareTools(
            plannerContext,
            preferredPackIds,
            preferredToolNames,
            handoffTargetPackIds,
            handoffTargetToolNames);
        var routingTokenSupport = routingTokens.Length == 0 ? Array.Empty<int>() : new int[routingTokens.Length];
        var focusTokenSupport = focusTokens.Length == 0 ? Array.Empty<int>() : new int[focusTokens.Length];
        string[]? toolSearchTexts = null;
        if (routingTokens.Length > 0 || focusTokens.Length > 0) {
            toolSearchTexts = new string[definitions.Count];
            for (var i = 0; i < definitions.Count; i++) {
                toolSearchTexts[i] = BuildToolRoutingSearchText(definitions[i]);
            }

            for (var t = 0; t < routingTokens.Length; t++) {
                var token = routingTokens[t];
                if (token.Length == 0) {
                    continue;
                }

                var support = 0;
                for (var i = 0; i < toolSearchTexts.Length; i++) {
                    if (toolSearchTexts[i].IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0) {
                        support++;
                    }
                }

                routingTokenSupport[t] = support;
            }

            for (var t = 0; t < focusTokens.Length; t++) {
                var token = focusTokens[t];
                if (token.Length == 0) {
                    continue;
                }

                var support = 0;
                for (var i = 0; i < toolSearchTexts.Length; i++) {
                    if (toolSearchTexts[i].IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0) {
                        support++;
                    }
                }

                focusTokenSupport[t] = support;
            }
        }

        // Tokens that show up in most tools are noise (ex: "get", "list"). Filter them out per-turn.
        var maxTokenSupport = Math.Max(1, (int)Math.Ceiling(definitions.Count * 0.55d));

        var scored = new List<ToolScore>(definitions.Count);
        var hasSignal = false;
        for (var i = 0; i < definitions.Count; i++) {
            var definition = definitions[i];
            var score = 0d;
            var tokenHits = 0;
            var focusTokenHits = 0;
            var explicitToolMatch = IsExplicitRequestedToolMatch(definition.Name, explicitRequestedToolNames);
            var directNameMatch = userRequest.IndexOf(definition.Name, StringComparison.OrdinalIgnoreCase) >= 0;
            var packId = ResolveToolPackId(definition, _toolOrchestrationCatalog);
            if (explicitToolMatch) {
                score += 9d;
            }
            if (directNameMatch) {
                score += 6d;
            }
            if (preferredToolNames.Contains(definition.Name)) {
                score += 10d;
            }
            if (handoffTargetToolNames.Contains(definition.Name)) {
                score += 8d;
            }
            if (preferredPackIds.Contains(packId)) {
                score += 4d;
            }
            if (handoffTargetPackIds.Contains(packId)) {
                score += 3d;
            }
            var remoteCapableBoost = prefersRemoteCapableTools && ToolSupportsRemoteHostTargeting(definition, _toolOrchestrationCatalog)
                ? WeightedRoutingRemoteCapableScoreBoost
                : 0d;
            score += remoteCapableBoost;
            var crossPackContinuationBoost = prefersCrossPackContinuation && ToolSupportsCrossPackHandoff(definition, _toolOrchestrationCatalog)
                ? WeightedRoutingCrossPackContinuationScoreBoost
                : 0d;
            score += crossPackContinuationBoost;
            var environmentDiscoverBoost =
                prefersEnvironmentBootstrap
                && ToolSupportsEnvironmentDiscovery(definition, _toolOrchestrationCatalog)
                && ToolMatchesPlannerContractTargets(definition.Name, packId, preferredToolNames, handoffTargetToolNames, preferredPackIds, handoffTargetPackIds)
                    ? WeightedRoutingEnvironmentDiscoverScoreBoost
                    : 0d;
            score += environmentDiscoverBoost;
            var setupAwareBoost =
                prefersSetupAwareTools
                && ToolIsSetupAware(definition, _toolOrchestrationCatalog)
                && ToolMatchesPlannerContractTargets(definition.Name, packId, preferredToolNames, handoffTargetToolNames, preferredPackIds, handoffTargetPackIds)
                    ? WeightedRoutingSetupAwareScoreBoost
                    : 0d;
            score += setupAwareBoost;

            if (routingTokens.Length > 0) {
                var searchText = toolSearchTexts?[i] ?? BuildToolRoutingSearchText(definition);
                for (var t = 0; t < routingTokens.Length; t++) {
                    if (routingTokenSupport[t] > maxTokenSupport) {
                        continue;
                    }

                    var token = routingTokens[t];
                    if (token.Length == 0) {
                        continue;
                    }

                    if (searchText.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0) {
                        tokenHits++;
                    }
                }

                if (tokenHits > 0) {
                    score += tokenHits * 1.25d;
                }
            }

            if (focusTokens.Length > 0) {
                var searchText = toolSearchTexts?[i] ?? BuildToolRoutingSearchText(definition);
                for (var t = 0; t < focusTokens.Length; t++) {
                    if (focusTokenSupport[t] > maxTokenSupport) {
                        continue;
                    }

                    var token = focusTokens[t];
                    if (token.Length == 0) {
                        continue;
                    }

                    if (searchText.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0) {
                        focusTokenHits++;
                    }
                }

                if (focusTokenHits > 0) {
                    score += focusTokenHits * WeightedRoutingFocusTokenScore;
                }
            }

            var adjustment = ReadToolRoutingAdjustment(definition.Name);
            score += adjustment;
            if (score > 0.01d) {
                hasSignal = true;
            }

            scored.Add(new ToolScore(
                Definition: definition,
                Score: score,
                DirectNameMatch: directNameMatch,
                ExplicitToolMatch: explicitToolMatch,
                TokenHits: tokenHits,
                FocusTokenHits: focusTokenHits,
                Adjustment: adjustment,
                RemoteCapableBoost: remoteCapableBoost,
                CrossPackContinuationBoost: crossPackContinuationBoost,
                EnvironmentDiscoverBoost: environmentDiscoverBoost,
                SetupAwareBoost: setupAwareBoost));
        }

        if (!hasSignal) {
            return SelectDeterministicToolSubset(definitions, limit, _toolOrchestrationCatalog);
        }

        scored.Sort(static (a, b) => {
            var scoreCompare = b.Score.CompareTo(a.Score);
            if (scoreCompare != 0) {
                return scoreCompare;
            }

            return StringComparer.OrdinalIgnoreCase.Compare(a.Definition.Name, b.Definition.Name);
        });

        if (scored[0].Score < 1d) {
            return SelectDeterministicToolSubset(definitions, limit, _toolOrchestrationCatalog);
        }

        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var selectedDefs = new List<ToolDefinition>(Math.Min(limit, definitions.Count));
        for (var i = 0; i < scored.Count && selectedDefs.Count < limit; i++) {
            var definition = scored[i].Definition;
            if (!selected.Add(definition.Name)) {
                continue;
            }
            selectedDefs.Add(definition);
        }

        if (selectedDefs.Count == 0) {
            return SelectDeterministicToolSubset(definitions, limit, _toolOrchestrationCatalog);
        }

        var selectionDiagnostics = ResolveWeightedRoutingSelectionDiagnostics(scored, limit, definitions.Count);
        var minSelection = selectionDiagnostics.EffectiveMinSelection;
        if (selectedDefs.Count < minSelection) {
            for (var i = selectedDefs.Count; i < scored.Count && selectedDefs.Count < minSelection; i++) {
                var definition = scored[i].Definition;
                if (!selected.Add(definition.Name)) {
                    continue;
                }
                selectedDefs.Add(definition);
            }
        }

        if (selectedDefs.Count >= definitions.Count) {
            return definitions;
        }

        insights = BuildRoutingInsights(scored, selectedDefs, selectionDiagnostics);
        return selectedDefs;
    }

    private static string[] ResolveWeightedRoutingFocusTokens(string requestText, IReadOnlyList<string> routingTokens) {
        var focusTokens = new List<string>();
        if (TryReadContinuationFocusUnresolvedAskFromWorkingMemoryPrompt(requestText, out var unresolvedAsk)) {
            focusTokens.AddRange(TokenizeRoutingTokens(unresolvedAsk, maxTokens: MaxWeightedRoutingFocusTokens));
        }

        if (TryReadPlannerContextFromRequestText(requestText, out var plannerContext)) {
            if (plannerContext.MissingLiveEvidence.Length > 0) {
                focusTokens.AddRange(TokenizeRoutingTokens(plannerContext.MissingLiveEvidence, maxTokens: MaxWeightedRoutingFocusTokens));
            }

            AddPlannerContextFocusTokens(focusTokens, plannerContext.PreferredPackIds);
            AddPlannerContextFocusTokens(focusTokens, plannerContext.PreferredToolNames);
            AddPlannerContextFocusTokens(focusTokens, plannerContext.StructuredNextActionSourceToolNames);
            AddPlannerContextFocusTokens(focusTokens, plannerContext.HandoffTargetPackIds);
            AddPlannerContextFocusTokens(focusTokens, plannerContext.HandoffTargetToolNames);
            if (plannerContext.StructuredNextActionReason.Length > 0) {
                focusTokens.AddRange(TokenizeRoutingTokens(plannerContext.StructuredNextActionReason, maxTokens: 8));
            }
        }

        var normalizedFocusTokens = NormalizeDistinctStrings(focusTokens, MaxWeightedRoutingFocusTokens);
        if (normalizedFocusTokens.Length == 0) {
            return Array.Empty<string>();
        }

        if (routingTokens.Count == 0) {
            return normalizedFocusTokens;
        }

        var existing = new HashSet<string>(routingTokens, StringComparer.OrdinalIgnoreCase);
        return normalizedFocusTokens
            .Where(existing.Add)
            .ToArray();
    }

    private static void AddPlannerContextFocusTokens(ICollection<string> target, IReadOnlyList<string> values) {
        if (target is null || values is null || values.Count == 0) {
            return;
        }

        for (var i = 0; i < values.Count; i++) {
            var value = (values[i] ?? string.Empty).Trim();
            if (value.Length == 0) {
                continue;
            }

            target.Add(value);
            var tokens = TokenizeRoutingTokens(value, maxTokens: 6);
            for (var t = 0; t < tokens.Length; t++) {
                target.Add(tokens[t]);
            }
        }
    }

    private static bool HasRemoteHostRoutingHint(string requestText, PlannerContextMetadata plannerContext) {
        return !string.IsNullOrWhiteSpace(TryExtractHostHintFromUserRequest(ExtractPrimaryUserRequest(requestText)))
               || !string.IsNullOrWhiteSpace(TryExtractHostHintFromUserRequest(plannerContext.MissingLiveEvidence))
               || !string.IsNullOrWhiteSpace(TryExtractHostHintFromUserRequest(plannerContext.StructuredNextActionReason))
               || !string.IsNullOrWhiteSpace(TryExtractHostHintFromUserRequest(requestText));
    }

    private static (string[] TargetPackIds, string[] TargetToolNames) DerivePlannerHandoffTargetsFromContext(
        PlannerContextMetadata plannerContext,
        ToolOrchestrationCatalog? toolOrchestrationCatalog) {
        if (toolOrchestrationCatalog is null || toolOrchestrationCatalog.Count == 0) {
            return (Array.Empty<string>(), Array.Empty<string>());
        }

        var sourceToolNames = NormalizeDistinctStrings(
            plannerContext.StructuredNextActionSourceToolNames
                .Concat(new[] { plannerContext.ContinuationSourceTool }),
            MaxPlannerContextHandoffTargets);
        if (sourceToolNames.Length == 0) {
            return (Array.Empty<string>(), Array.Empty<string>());
        }

        var targetPackIds = new List<string>();
        var targetToolNames = new List<string>();
        for (var i = 0; i < sourceToolNames.Length; i++) {
            var sourceToolName = (sourceToolNames[i] ?? string.Empty).Trim();
            if (sourceToolName.Length == 0 || !toolOrchestrationCatalog.TryGetEntry(sourceToolName, out var entry)) {
                continue;
            }

            for (var e = 0; e < entry.HandoffEdges.Count; e++) {
                var edge = entry.HandoffEdges[e];
                var targetPackId = NormalizePackId(edge.TargetPackId);
                if (targetPackId.Length > 0) {
                    targetPackIds.Add(targetPackId);
                }

                var targetToolName = NormalizeToolNameForAnswerPlan(edge.TargetToolName);
                if (targetToolName.Length > 0) {
                    targetToolNames.Add(targetToolName);
                }
            }
        }

        return (
            NormalizeDistinctStrings(targetPackIds, MaxPlannerContextHandoffTargets),
            NormalizeDistinctStrings(targetToolNames, MaxPlannerContextHandoffTargets));
    }

    private static bool ToolSupportsRemoteHostTargeting(ToolDefinition definition, ToolOrchestrationCatalog? toolOrchestrationCatalog) {
        if (definition is null) {
            return false;
        }

        if (toolOrchestrationCatalog is not null
            && toolOrchestrationCatalog.TryGetEntry(definition.Name, out var entry)) {
            return entry.SupportsRemoteHostTargeting
                   || entry.RemoteHostArguments.Count > 0
                   || ToolExecutionScopes.IsRemoteCapable(entry.ExecutionScope);
        }

        var schemaTraits = ToolSchemaTraitProjection.Project(definition);
        return schemaTraits.SupportsRemoteHostTargeting
               || ToolExecutionScopes.IsRemoteCapable(schemaTraits.ExecutionScope);
    }

    private static bool ToolSupportsEnvironmentDiscovery(ToolDefinition definition, ToolOrchestrationCatalog? toolOrchestrationCatalog) {
        if (definition is null) {
            return false;
        }

        if (toolOrchestrationCatalog is not null
            && toolOrchestrationCatalog.TryGetEntry(definition.Name, out var entry)) {
            return entry.IsEnvironmentDiscoverTool
                   || string.Equals(entry.Role, ToolRoutingTaxonomy.RoleEnvironmentDiscover, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(definition.Routing?.Role, ToolRoutingTaxonomy.RoleEnvironmentDiscover, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ToolIsSetupAware(ToolDefinition definition, ToolOrchestrationCatalog? toolOrchestrationCatalog) {
        if (definition is null) {
            return false;
        }

        if (toolOrchestrationCatalog is not null
            && toolOrchestrationCatalog.TryGetEntry(definition.Name, out var entry)) {
            return entry.IsSetupAware
                   || entry.SetupToolName.Length > 0
                   || entry.SetupRequirementCount > 0
                   || entry.SetupHintKeys.Count > 0;
        }

        return definition.Setup?.IsSetupAware == true
               || !string.IsNullOrWhiteSpace(definition.Setup?.SetupToolName)
               || definition.Setup?.Requirements.Count > 0
               || definition.Setup?.SetupHintKeys.Count > 0;
    }

    private static bool ToolSupportsCrossPackHandoff(ToolDefinition definition, ToolOrchestrationCatalog? toolOrchestrationCatalog) {
        if (definition is null) {
            return false;
        }

        if (toolOrchestrationCatalog is not null
            && toolOrchestrationCatalog.TryGetEntry(definition.Name, out var entry)) {
            if (!entry.IsHandoffAware || entry.HandoffEdges.Count == 0) {
                return false;
            }

            for (var i = 0; i < entry.HandoffEdges.Count; i++) {
                var targetPackId = NormalizePackId(entry.HandoffEdges[i].TargetPackId);
                if (targetPackId.Length > 0 && !string.Equals(targetPackId, entry.PackId, StringComparison.OrdinalIgnoreCase)) {
                    return true;
                }
            }

            return false;
        }

        var sourcePackId = NormalizePackId(definition.Routing?.PackId);
        var routes = definition.Handoff?.OutboundRoutes;
        if (definition.Handoff?.IsHandoffAware != true || routes is not { Count: > 0 }) {
            return false;
        }

        for (var i = 0; i < routes.Count; i++) {
            var targetPackId = NormalizePackId(routes[i]?.TargetPackId);
            if (targetPackId.Length > 0 && !string.Equals(targetPackId, sourcePackId, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

    private static bool ShouldPreferEnvironmentBootstrap(
        PlannerContextMetadata plannerContext,
        IReadOnlyCollection<string> preferredPackIds,
        IReadOnlyCollection<string> preferredToolNames,
        IReadOnlyCollection<string> handoffTargetPackIds,
        IReadOnlyCollection<string> handoffTargetToolNames) {
        if ((preferredPackIds?.Count ?? 0) == 0 && (handoffTargetPackIds?.Count ?? 0) == 0) {
            return false;
        }

        if ((preferredToolNames?.Count ?? 0) > 0 || (handoffTargetToolNames?.Count ?? 0) > 0) {
            return false;
        }

        return plannerContext.RequiresLiveExecution
               || plannerContext.MissingLiveEvidence.Length > 0
               || plannerContext.StructuredNextActionSourceToolNames.Length > 0
               || plannerContext.ContinuationSourceTool.Length > 0;
    }

    private static bool ShouldPreferSetupAwareTools(
        PlannerContextMetadata plannerContext,
        IReadOnlyCollection<string> preferredPackIds,
        IReadOnlyCollection<string> preferredToolNames,
        IReadOnlyCollection<string> handoffTargetPackIds,
        IReadOnlyCollection<string> handoffTargetToolNames) {
        if ((preferredPackIds?.Count ?? 0) == 0
            && (preferredToolNames?.Count ?? 0) == 0
            && (handoffTargetPackIds?.Count ?? 0) == 0
            && (handoffTargetToolNames?.Count ?? 0) == 0) {
            return false;
        }

        return plannerContext.RequiresLiveExecution
               || plannerContext.MissingLiveEvidence.Length > 0
               || plannerContext.StructuredNextActionReason.Length > 0
               || plannerContext.ContinuationReason.Length > 0;
    }

    private static bool ToolMatchesPlannerContractTargets(
        string? toolName,
        string? packId,
        IReadOnlyCollection<string> preferredToolNames,
        IReadOnlyCollection<string> handoffTargetToolNames,
        IReadOnlyCollection<string> preferredPackIds,
        IReadOnlyCollection<string> handoffTargetPackIds) {
        var normalizedToolName = (toolName ?? string.Empty).Trim();
        if (normalizedToolName.Length > 0
            && ((preferredToolNames?.Contains(normalizedToolName, StringComparer.OrdinalIgnoreCase) ?? false)
                || (handoffTargetToolNames?.Contains(normalizedToolName, StringComparer.OrdinalIgnoreCase) ?? false))) {
            return true;
        }

        var normalizedPackId = NormalizePackId(packId);
        if (normalizedPackId.Length == 0) {
            return false;
        }

        return (preferredPackIds?.Contains(normalizedPackId, StringComparer.OrdinalIgnoreCase) ?? false)
               || (handoffTargetPackIds?.Contains(normalizedPackId, StringComparer.OrdinalIgnoreCase) ?? false);
    }

    private static WeightedRoutingSelectionDiagnostics ResolveWeightedRoutingSelectionDiagnostics(
        IReadOnlyList<ToolScore> scored,
        int limit,
        int definitionCount) {
        var baseline = Math.Min(definitionCount, Math.Max(8, Math.Min(limit, 12)));
        if (scored.Count < 2 || definitionCount <= baseline) {
            return new WeightedRoutingSelectionDiagnostics(
                AmbiguityWidened: false,
                BaselineMinSelection: baseline,
                EffectiveMinSelection: baseline,
                AmbiguousClusterSize: 0,
                SecondScoreRatio: 0d);
        }

        var topScore = scored[0].Score;
        if (topScore <= 0d) {
            return new WeightedRoutingSelectionDiagnostics(
                AmbiguityWidened: false,
                BaselineMinSelection: baseline,
                EffectiveMinSelection: baseline,
                AmbiguousClusterSize: 0,
                SecondScoreRatio: 0d);
        }

        var secondScoreRatio = scored[1].Score / topScore;
        if (secondScoreRatio < WeightedRoutingAmbiguousSecondScoreRatioThreshold) {
            return new WeightedRoutingSelectionDiagnostics(
                AmbiguityWidened: false,
                BaselineMinSelection: baseline,
                EffectiveMinSelection: baseline,
                AmbiguousClusterSize: 0,
                SecondScoreRatio: Math.Round(secondScoreRatio, 3));
        }

        var ambiguousClusterSize = 0;
        for (var i = 0; i < scored.Count; i++) {
            var score = scored[i].Score;
            if (score <= 0.01d) {
                break;
            }

            var scoreRatio = score / topScore;
            if (scoreRatio < WeightedRoutingAmbiguousClusterScoreRatioThreshold) {
                break;
            }

            ambiguousClusterSize++;
            if (ambiguousClusterSize >= WeightedRoutingAmbiguousSelectionCap) {
                break;
            }
        }

        if (ambiguousClusterSize < WeightedRoutingAmbiguousClusterMinCount) {
            return new WeightedRoutingSelectionDiagnostics(
                AmbiguityWidened: false,
                BaselineMinSelection: baseline,
                EffectiveMinSelection: baseline,
                AmbiguousClusterSize: ambiguousClusterSize,
                SecondScoreRatio: Math.Round(secondScoreRatio, 3));
        }

        var widened = Math.Max(
            baseline,
            Math.Min(
                WeightedRoutingAmbiguousSelectionCap,
                Math.Max(WeightedRoutingAmbiguousSelectionFloor, ambiguousClusterSize)));
        var effective = Math.Min(definitionCount, widened);
        return new WeightedRoutingSelectionDiagnostics(
            AmbiguityWidened: effective > baseline,
            BaselineMinSelection: baseline,
            EffectiveMinSelection: effective,
            AmbiguousClusterSize: ambiguousClusterSize,
            SecondScoreRatio: Math.Round(secondScoreRatio, 3));
    }

}
