using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.OpenAI;
using IntelligenceX.OpenAI.AppServer.Models;
using IntelligenceX.OpenAI.Chat;
using IntelligenceX.OpenAI.ToolCalling;
using IntelligenceX.Tools;
using IntelligenceX.Tools.Common;
using IntelligenceX.Json;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
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

        var plannerCandidates = BuildModelPlannerCandidates(definitions, limit);
        var planned = await TrySelectToolsViaModelPlannerAsync(client, threadId, userRequest, plannerCandidates, limit, cancellationToken)
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

    private static IReadOnlyList<ToolDefinition> BuildModelPlannerCandidates(IReadOnlyList<ToolDefinition> definitions, int limit) {
        if (definitions.Count <= 64) {
            return definitions;
        }

        var minCandidateLimit = Math.Max(24, limit);
        var candidateLimit = Math.Clamp(Math.Max(limit * 3, minCandidateLimit), minCandidateLimit, Math.Min(definitions.Count, 96));
        return SelectDeterministicToolSubset(definitions, candidateLimit);
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

        var routingTokens = TokenizeRoutingTokens(userRequest, maxTokens: 16);
        var routingTokenSupport = routingTokens.Length == 0 ? Array.Empty<int>() : new int[routingTokens.Length];
        string[]? toolSearchTexts = null;
        if (routingTokens.Length > 0) {
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
        }

        // Tokens that show up in most tools are noise (ex: "get", "list"). Filter them out per-turn.
        var maxTokenSupport = Math.Max(1, (int)Math.Ceiling(definitions.Count * 0.55d));

        var scored = new List<ToolScore>(definitions.Count);
        var hasSignal = false;
        for (var i = 0; i < definitions.Count; i++) {
            var definition = definitions[i];
            var score = 0d;
            var tokenHits = 0;
            var directNameMatch = userRequest.IndexOf(definition.Name, StringComparison.OrdinalIgnoreCase) >= 0;
            if (directNameMatch) {
                score += 6d;
            }

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

            var adjustment = ReadToolRoutingAdjustment(definition.Name);
            score += adjustment;
            if (score > 0.01d) {
                hasSignal = true;
            }

            scored.Add(new ToolScore(
                Definition: definition,
                Score: score,
                DirectNameMatch: directNameMatch,
                TokenHits: tokenHits,
                Adjustment: adjustment));
        }

        if (!hasSignal) {
            return SelectDeterministicToolSubset(definitions, limit);
        }

        scored.Sort(static (a, b) => {
            var scoreCompare = b.Score.CompareTo(a.Score);
            if (scoreCompare != 0) {
                return scoreCompare;
            }

            return StringComparer.OrdinalIgnoreCase.Compare(a.Definition.Name, b.Definition.Name);
        });

        if (scored[0].Score < 1d) {
            return SelectDeterministicToolSubset(definitions, limit);
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
            return SelectDeterministicToolSubset(definitions, limit);
        }

        var minSelection = Math.Min(definitions.Count, Math.Max(8, Math.Min(limit, 12)));
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

        insights = BuildRoutingInsights(scored, selectedDefs);
        return selectedDefs;
    }

}
