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
        bool executionNudgeUsed,
        bool toolReceiptCorrectionUsed,
        bool watchdogAlreadyUsed,
        bool shouldRetry,
        string reason) {
        if (!executionContractApplies) {
            return;
        }

        var normalized = (userRequest ?? string.Empty).Trim();
        var tokenCount = CountLetterDigitTokens(normalized, maxTokens: 16);
        var outcome = shouldRetry ? "retry" : "skip";
        var watchdogState = watchdogAlreadyUsed ? "used" : "unused";
        var nudgeState = executionNudgeUsed ? "used" : "unused";
        var receiptState = toolReceiptCorrectionUsed ? "used" : "unused";
        Console.Error.WriteLine(
            $"[tool-watchdog] outcome={outcome} reason={reason} contract=true watchdog={watchdogState} nudge={nudgeState} receipt={receiptState} tools={toolsAvailable} prior_calls={Math.Max(0, priorToolCalls)} prior_outputs={Math.Max(0, priorToolOutputs)} draft_calls={Math.Max(0, assistantDraftToolCalls)} tokens={tokenCount}");
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
        if (!isFollowUp && !isActionPayload) {
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
        if (ShouldSkipWeightedRouting(userRequest)) {
            return (definitions, new List<ToolRoutingInsight>());
        }

        var limit = ResolveMaxCandidateToolsLimit(maxCandidateTools, definitions.Count);
        if (limit >= definitions.Count) {
            return (definitions, new List<ToolRoutingInsight>());
        }

        var planned = await TrySelectToolsViaModelPlannerAsync(client, threadId, userRequest, definitions, limit, cancellationToken).ConfigureAwait(false);
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

    private IReadOnlyList<ToolDefinition> SelectWeightedToolSubset(IReadOnlyList<ToolDefinition> definitions, string requestText, int? maxCandidateTools,
        out List<ToolRoutingInsight> insights) {
        insights = new List<ToolRoutingInsight>();
        if (definitions.Count <= 12) {
            return definitions;
        }

        var userRequest = ExtractPrimaryUserRequest(requestText);
        if (ShouldSkipWeightedRouting(userRequest)) {
            return definitions;
        }

        var limit = ResolveMaxCandidateToolsLimit(maxCandidateTools, definitions.Count);
        if (limit >= definitions.Count) {
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
            return definitions;
        }

        scored.Sort(static (a, b) => {
            var scoreCompare = b.Score.CompareTo(a.Score);
            if (scoreCompare != 0) {
                return scoreCompare;
            }

            return StringComparer.OrdinalIgnoreCase.Compare(a.Definition.Name, b.Definition.Name);
        });

        if (scored[0].Score < 1d) {
            return definitions;
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
            return definitions;
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

    private static List<ToolRoutingInsight> BuildRoutingInsights(IReadOnlyList<ToolScore> scored, IReadOnlyList<ToolDefinition> selectedDefs) {
        if (selectedDefs.Count == 0 || scored.Count == 0) {
            return new List<ToolRoutingInsight>();
        }

        var selectedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < selectedDefs.Count; i++) {
            selectedNames.Add(selectedDefs[i].Name);
        }

        var maxScore = scored[0].Score <= 0 ? 1d : scored[0].Score;
        var insights = new List<ToolRoutingInsight>();
        for (var i = 0; i < scored.Count; i++) {
            var toolScore = scored[i];
            if (!selectedNames.Contains(toolScore.Definition.Name)) {
                continue;
            }

            var confidenceValue = Math.Clamp(toolScore.Score / maxScore, 0d, 1d);
            var confidence = confidenceValue >= 0.72d ? "high" : confidenceValue >= 0.45d ? "medium" : "low";
            var reasons = new List<string>();
            if (toolScore.DirectNameMatch) {
                reasons.Add("direct name match");
            }
            if (toolScore.TokenHits > 0) {
                reasons.Add("token match");
            }
            if (toolScore.Adjustment > 0.2d) {
                reasons.Add("recent tool success");
            } else if (toolScore.Adjustment < -0.2d) {
                reasons.Add("recent tool failures");
            }

            if (reasons.Count == 0) {
                reasons.Add("general relevance");
            }

            insights.Add(new ToolRoutingInsight(
                ToolName: toolScore.Definition.Name,
                Confidence: confidence,
                Score: Math.Round(toolScore.Score, 3),
                Reason: string.Join(", ", reasons)));
        }

        insights.Sort(static (a, b) => b.Score.CompareTo(a.Score));
        if (insights.Count > 12) {
            insights.RemoveRange(12, insights.Count - 12);
        }

        return insights;
    }

    private static string[] TokenizeRoutingTokens(string text, int maxTokens) {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0 || maxTokens <= 0) {
            return Array.Empty<string>();
        }

        var tokens = new List<string>(Math.Min(12, maxTokens));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var inToken = false;
        var tokenStart = 0;
        for (var i = 0; i <= normalized.Length; i++) {
            var ch = i < normalized.Length ? normalized[i] : '\0';
            var isTokenChar = i < normalized.Length && char.IsLetterOrDigit(ch);
            if (isTokenChar) {
                if (!inToken) {
                    inToken = true;
                    tokenStart = i;
                }
                continue;
            }

            if (!inToken) {
                continue;
            }

            var token = normalized.Substring(tokenStart, i - tokenStart).Normalize(NormalizationForm.FormKC).Trim();
            inToken = false;
            if (token.Length == 0) {
                continue;
            }

            var lower = token.ToLowerInvariant();
            var hasNonAscii = false;
            for (var t = 0; t < lower.Length; t++) {
                if (lower[t] > 127) {
                    hasNonAscii = true;
                    break;
                }
            }

            var minLen = hasNonAscii ? 2 : 3;
            if (lower.Length < minLen) {
                continue;
            }

            if (seen.Add(lower)) {
                tokens.Add(lower);
                if (tokens.Count >= maxTokens) {
                    break;
                }
            }
        }

        return tokens.Count == 0 ? Array.Empty<string>() : tokens.ToArray();
    }

    private static string BuildToolRoutingSearchText(ToolDefinition definition) {
        if (definition is null) {
            return string.Empty;
        }

        var sb = new StringBuilder(256);
        sb.Append(definition.Name);
        if (!string.IsNullOrWhiteSpace(definition.Description)) {
            sb.Append(' ').Append(definition.Description!.Trim());
        }

        if (definition.Tags.Count > 0) {
            for (var i = 0; i < definition.Tags.Count; i++) {
                var tag = (definition.Tags[i] ?? string.Empty).Trim();
                if (tag.Length == 0) {
                    continue;
                }
                sb.Append(' ').Append(tag);
            }
        }

        if (definition.Aliases.Count > 0) {
            for (var i = 0; i < definition.Aliases.Count; i++) {
                var alias = definition.Aliases[i];
                if (alias is null || string.IsNullOrWhiteSpace(alias.Name)) {
                    continue;
                }
                sb.Append(' ').Append(alias.Name.Trim());
            }
        }

        var schemaArguments = ExtractToolSchemaPropertyNames(definition, maxCount: 12, out var hasTableViewProjection);
        for (var i = 0; i < schemaArguments.Length; i++) {
            sb.Append(' ').Append(schemaArguments[i]);
        }

        var requiredArguments = ExtractToolSchemaRequiredNames(definition, maxCount: 8);
        if (requiredArguments.Length > 0) {
            sb.Append(" required");
            for (var i = 0; i < requiredArguments.Length; i++) {
                sb.Append(' ').Append(requiredArguments[i]);
            }
        }

        if (hasTableViewProjection) {
            sb.Append(" table view projection columns sort_by sort_direction top");
        }

        return sb.ToString();
    }

    private static string[] ExtractToolSchemaPropertyNames(ToolDefinition definition, int maxCount, out bool hasTableViewProjection) {
        hasTableViewProjection = false;
        if (definition?.Parameters is null || maxCount <= 0) {
            return Array.Empty<string>();
        }

        var properties = definition.Parameters.GetObject("properties");
        if (properties is null || properties.Count == 0) {
            return Array.Empty<string>();
        }

        hasTableViewProjection = HasTableViewProjectionArguments(properties);

        var names = new List<string>(Math.Min(maxCount, properties.Count));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in properties) {
            var name = NormalizeToolSchemaToken(kv.Key);
            if (name.Length == 0 || !seen.Add(name)) {
                continue;
            }

            names.Add(name);
            if (names.Count >= maxCount) {
                break;
            }
        }

        return names.Count == 0 ? Array.Empty<string>() : names.ToArray();
    }

    private static string[] ExtractToolSchemaRequiredNames(ToolDefinition definition, int maxCount) {
        if (definition?.Parameters is null || maxCount <= 0) {
            return Array.Empty<string>();
        }

        var required = definition.Parameters.GetArray("required");
        if (required is null || required.Count == 0) {
            return Array.Empty<string>();
        }

        var names = new List<string>(Math.Min(maxCount, required.Count));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < required.Count && names.Count < maxCount; i++) {
            var value = NormalizeToolSchemaToken(required[i]?.AsString());
            if (value.Length == 0 || !seen.Add(value)) {
                continue;
            }

            names.Add(value);
        }

        return names.Count == 0 ? Array.Empty<string>() : names.ToArray();
    }

    private static bool HasTableViewProjectionArguments(JsonObject properties) {
        return properties.TryGetValue("columns", out _)
               || properties.TryGetValue("sort_by", out _)
               || properties.TryGetValue("sort_direction", out _)
               || properties.TryGetValue("top", out _);
    }

    private static string NormalizeToolSchemaToken(string? token) {
        var value = (token ?? string.Empty).Trim();
        if (value.Length == 0) {
            return string.Empty;
        }

        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++) {
            var c = value[i];
            if (char.IsLetterOrDigit(c) || c is '_' or '-') {
                sb.Append(c);
            } else if (char.IsWhiteSpace(c)) {
                sb.Append('_');
            }
        }

        return sb.ToString().Trim('_');
    }
}
