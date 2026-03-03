using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using JsonValueKind = System.Text.Json.JsonValueKind;
using IntelligenceX.Chat.Abstractions.Protocol;
using IntelligenceX.Json;
using IntelligenceX.Tools;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private static bool TryBuildStructuredNextActionRetryPrompt(
        IReadOnlyList<ToolDefinition> toolDefinitions,
        IReadOnlyList<ToolCallDto> toolCalls,
        IReadOnlyList<ToolOutputDto> toolOutputs,
        bool continuationFollowUpTurn,
        string userRequest,
        string assistantDraft,
        out string prompt,
        out string reason) {
        prompt = string.Empty;
        reason = "not_eligible";

        if (!continuationFollowUpTurn) {
            reason = "not_continuation_follow_up";
            return false;
        }

        if (toolDefinitions.Count == 0 || toolCalls.Count == 0 || toolOutputs.Count == 0) {
            reason = "missing_tool_context";
            return false;
        }

        var draft = (assistantDraft ?? string.Empty).Trim();
        if (!ContainsQuestionSignal(draft)
            && !LooksLikeMultilineFollowUpBlockerDraft(draft)
            && !LooksLikeExecutionAcknowledgeDraft(draft)) {
            reason = "assistant_draft_not_blocker_like";
            return false;
        }

        if (!TryExtractStructuredNextAction(
                toolDefinitions,
                toolCalls,
                toolOutputs,
                out var sourceTool,
                out var nextTool,
                out var argumentsJson,
                out var nextReason,
                out _)) {
            reason = "no_structured_next_action";
            return false;
        }

        prompt = BuildStructuredNextActionRetryPrompt(
            userRequest: userRequest,
            assistantDraft: draft,
            sourceTool: sourceTool,
            nextTool: nextTool,
            argumentsJson: argumentsJson,
            nextReason: nextReason);
        reason = "structured_next_action_found";
        return true;
    }

    private static bool ShouldAttemptToolProgressRecovery(
        bool continuationFollowUpTurn,
        string assistantDraft,
        bool toolsAvailable,
        int priorToolCalls,
        int priorToolOutputs,
        int assistantDraftToolCalls,
        bool progressRecoveryAlreadyUsed,
        out string reason) {
        reason = "not_eligible";

        if (progressRecoveryAlreadyUsed) {
            reason = "tool_progress_recovery_already_used";
            return false;
        }

        if (!toolsAvailable) {
            reason = "tools_unavailable";
            return false;
        }

        if (!continuationFollowUpTurn) {
            reason = "not_continuation_follow_up";
            return false;
        }

        if (priorToolCalls == 0 || priorToolOutputs == 0) {
            reason = "missing_prior_tool_activity";
            return false;
        }

        if (assistantDraftToolCalls > 0) {
            reason = "assistant_draft_has_tool_calls";
            return false;
        }

        var draft = (assistantDraft ?? string.Empty).Trim();
        if (draft.Length == 0 || draft.Length > 2800) {
            reason = draft.Length == 0 ? "empty_assistant_draft" : "assistant_draft_too_long";
            return false;
        }

        if (draft.Contains(ToolProgressRecoveryMarker, StringComparison.OrdinalIgnoreCase)
            || draft.Contains(StructuredNextActionRetryMarker, StringComparison.OrdinalIgnoreCase)) {
            reason = "recovery_marker_present";
            return false;
        }

        if (!ContainsQuestionSignal(draft)
            && !LooksLikeMultilineFollowUpBlockerDraft(draft)
            && !LooksLikeExecutionAcknowledgeDraft(draft)) {
            reason = "assistant_draft_not_blocker_like";
            return false;
        }

        reason = "blocker_like_draft_after_tool_activity";
        return true;
    }

    private static bool ShouldAllowHostStructuredNextActionReplay(string assistantDraft) {
        var draft = (assistantDraft ?? string.Empty).Trim();
        if (draft.Length == 0 || ContainsQuestionSignal(draft)) {
            return false;
        }

        return LooksLikeMultilineFollowUpBlockerDraft(draft)
               || LooksLikeExecutionAcknowledgeDraft(draft);
    }

    internal static bool ShouldAttemptCarryoverStructuredNextActionReplay(
        bool continuationFollowUpTurn,
        bool compactFollowUpTurn,
        string userRequest,
        string assistantDraft) {
        if (!compactFollowUpTurn) {
            return false;
        }

        var request = (userRequest ?? string.Empty).Trim();
        if (request.Length == 0 || ContainsQuestionSignal(request)) {
            return false;
        }

        // If this turn is already anchored to new contextual request content, avoid replaying stale carryover
        // actions from previous turns and let normal tool planning proceed.
        if (LooksLikeContextualFollowUpForExecutionNudge(request, assistantDraft)) {
            return false;
        }

        // Non-expanded compact follow-ups (e.g. "go ahead") should still be allowed to replay carryover.
        if (!continuationFollowUpTurn) {
            if (LooksLikeActionSelectionPayload(request)) {
                return false;
            }
        }

        return true;
    }

    private static bool ShouldTriggerNoResultPhaseLoopWatchdog(
        int trailingPhaseLoopEvents,
        bool hasToolActivity,
        bool watchdogAlreadyUsed,
        bool executionContractApplies,
        bool continuationFollowUpTurn,
        bool compactFollowUpTurn,
        string assistantDraft,
        out string reason) {
        reason = "not_eligible";
        if (watchdogAlreadyUsed) {
            reason = "watchdog_already_used";
            return false;
        }

        var threshold = hasToolActivity
            ? NoResultPhaseLoopThresholdWithToolActivity
            : NoResultPhaseLoopThresholdWithoutToolActivity;
        if (trailingPhaseLoopEvents < threshold) {
            reason = "phase_loop_threshold_not_met";
            return false;
        }

        var draft = (assistantDraft ?? string.Empty).Trim();
        if (draft.Length == 0) {
            reason = "empty_assistant_draft_watchdog_retry";
            return true;
        }

        if (draft.Contains(ExecutionContractMarker, StringComparison.OrdinalIgnoreCase)
            || draft.Contains(ExecutionWatchdogMarker, StringComparison.OrdinalIgnoreCase)
            || draft.Contains(ResponseReviewMarker, StringComparison.OrdinalIgnoreCase)) {
            reason = "watchdog_marker_present";
            return false;
        }

        var blockerLikeDraft = LooksLikeMultilineFollowUpBlockerDraft(draft)
                               || LooksLikeExecutionAcknowledgeDraft(draft);
        if (!executionContractApplies
            && !continuationFollowUpTurn
            && !compactFollowUpTurn
            && !blockerLikeDraft) {
            reason = "turn_not_execution_shaped";
            return false;
        }

        if (!executionContractApplies
            && ContainsQuestionSignal(draft)
            && !blockerLikeDraft) {
            reason = "assistant_question_without_execution_contract";
            return false;
        }

        reason = hasToolActivity
            ? "phase_loop_with_tool_activity"
            : "phase_loop_without_tool_activity";
        return true;
    }

    private static bool TryExtractStructuredNextAction(
        IReadOnlyList<ToolDefinition> toolDefinitions,
        IReadOnlyList<ToolCallDto> toolCalls,
        IReadOnlyList<ToolOutputDto> toolOutputs,
        out string sourceTool,
        out string nextTool,
        out string argumentsJson,
        out string nextReason,
        out ActionMutability nextActionMutability) {
        sourceTool = string.Empty;
        nextTool = string.Empty;
        argumentsJson = "{}";
        nextReason = string.Empty;
        nextActionMutability = ActionMutability.Unknown;

        var availableTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < toolDefinitions.Count; i++) {
            var name = (toolDefinitions[i].Name ?? string.Empty).Trim();
            if (name.Length > 0) {
                availableTools.Add(name);
            }
        }

        if (availableTools.Count == 0) {
            return false;
        }

        var callNamesById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < toolCalls.Count; i++) {
            var callId = (toolCalls[i].CallId ?? string.Empty).Trim();
            var callName = (toolCalls[i].Name ?? string.Empty).Trim();
            if (callId.Length == 0 || callName.Length == 0) {
                continue;
            }
            callNamesById[callId] = callName;
        }

        for (var outputIndex = toolOutputs.Count - 1; outputIndex >= 0; outputIndex--) {
            var output = toolOutputs[outputIndex];
            var payload = (output.Output ?? string.Empty).Trim();
            if (payload.Length == 0 || payload[0] != '{') {
                continue;
            }

            try {
                using var doc = JsonDocument.Parse(payload, ActionSelectionJsonOptions);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) {
                    continue;
                }

                if (!TryReadNextActionsArray(doc.RootElement, out var nextActions)) {
                    continue;
                }

                for (var actionIndex = 0; actionIndex < nextActions.GetArrayLength(); actionIndex++) {
                    var action = nextActions[actionIndex];
                    if (!TryReadNextActionToolName(action, out var candidateTool)) {
                        continue;
                    }

                    if (candidateTool.Length == 0 || !availableTools.Contains(candidateTool)) {
                        continue;
                    }

                    var candidateArgumentsJson = TryReadNextActionArgumentsJson(action);
                    var candidateReason = TryReadNextActionReason(action);
                    var candidateMutability = TryReadNextActionMutability(action);

                    var outputCallId = (output.CallId ?? string.Empty).Trim();
                    if (outputCallId.Length > 0 && callNamesById.TryGetValue(outputCallId, out var sourceName)) {
                        sourceTool = sourceName;
                    }

                    nextTool = candidateTool;
                    argumentsJson = candidateArgumentsJson;
                    nextReason = candidateReason;
                    nextActionMutability = candidateMutability;
                    return true;
                }
            } catch (JsonException) {
                continue;
            }
        }

        return false;
    }

    private static bool TryBuildHostStructuredNextActionToolCall(
        IReadOnlyList<ToolDefinition> toolDefinitions,
        IReadOnlyList<ToolCallDto> toolCalls,
        IReadOnlyList<ToolOutputDto> toolOutputs,
        IReadOnlyDictionary<string, bool>? mutatingToolHintsByName,
        out ToolCall toolCall,
        out string reason) {
        toolCall = null!;
        reason = "not_eligible";

        if (!TryExtractStructuredNextAction(
                toolDefinitions,
                toolCalls,
                toolOutputs,
                out _,
                out var nextTool,
                out var argumentsJson,
                out _,
                out var nextActionMutability)) {
            reason = "no_structured_next_action";
            return false;
        }

        if (!TryGetToolDefinitionByName(toolDefinitions, nextTool, out var toolDefinition)) {
            reason = "next_tool_not_available";
            return false;
        }

        var mutability = ResolveStructuredNextActionMutability(
            declaredMutability: nextActionMutability,
            toolName: nextTool,
            toolDefinition: toolDefinition,
            mutatingToolHintsByName: mutatingToolHintsByName);

        if (mutability == ActionMutability.Unknown) {
            reason = "next_action_mutability_unknown";
            return false;
        }

        if (mutability == ActionMutability.Mutating) {
            reason = "next_action_mutating_not_autorun";
            return false;
        }

        if (!TryParseStructuredNextActionArguments(argumentsJson, toolDefinition, out var normalizedArguments, out var argumentReason)) {
            reason = argumentReason;
            return false;
        }

        // Prevent host-driven structured next-action loops from replaying the exact same
        // tool + normalized arguments that already ran in this turn.
        if (HasEquivalentToolCallArguments(toolCalls, nextTool, toolDefinition, normalizedArguments)) {
            reason = "next_action_self_loop";
            return false;
        }

        var serializedArguments = JsonLite.Serialize(normalizedArguments);
        var callId = "host_next_action_" + Guid.NewGuid().ToString("N");
        var raw = new JsonObject()
            .Add("type", "tool_call")
            .Add("call_id", callId)
            .Add("name", nextTool)
            .Add("arguments", serializedArguments);
        toolCall = new ToolCall(
            callId: callId,
            name: nextTool,
            input: serializedArguments,
            arguments: normalizedArguments,
            raw: raw);
        reason = "structured_next_action_readonly_autorun";
        return true;
    }

    private static ActionMutability TryReadNextActionMutability(JsonElement action) {
        if (action.ValueKind != JsonValueKind.Object) {
            return ActionMutability.Unknown;
        }

        if (TryReadNextActionBoolean(action, "mutating", out var mutating)) {
            return mutating ? ActionMutability.Mutating : ActionMutability.ReadOnly;
        }

        if (TryReadNextActionBoolean(action, "is_mutating", out mutating)) {
            return mutating ? ActionMutability.Mutating : ActionMutability.ReadOnly;
        }

        if (TryReadNextActionBoolean(action, "readonly", out var readOnly)) {
            return readOnly ? ActionMutability.ReadOnly : ActionMutability.Mutating;
        }

        if (TryReadNextActionBoolean(action, "read_only", out readOnly)) {
            return readOnly ? ActionMutability.ReadOnly : ActionMutability.Mutating;
        }

        return ActionMutability.Unknown;
    }

    private static bool TryReadNextActionBoolean(JsonElement action, string propertyName, out bool value) {
        value = false;
        if (!action.TryGetProperty(propertyName, out var node)) {
            return false;
        }

        switch (node.ValueKind) {
            case JsonValueKind.True:
                value = true;
                return true;
            case JsonValueKind.False:
                value = false;
                return true;
            case JsonValueKind.Number:
                if (node.TryGetInt64(out var number)) {
                    if (number == 0) {
                        value = false;
                        return true;
                    }
                    if (number == 1) {
                        value = true;
                        return true;
                    }
                }
                return false;
            case JsonValueKind.String:
                return TryParseProtocolBoolean((node.GetString() ?? string.Empty).Trim(), out value);
            default:
                return false;
        }
    }

    private static bool TryGetToolDefinitionByName(
        IReadOnlyList<ToolDefinition> toolDefinitions,
        string toolName,
        out ToolDefinition definition) {
        var normalizedToolName = (toolName ?? string.Empty).Trim();
        for (var i = 0; i < toolDefinitions.Count; i++) {
            var candidate = toolDefinitions[i];
            if (candidate is null) {
                continue;
            }

            var candidateName = (candidate.Name ?? string.Empty).Trim();
            if (!string.Equals(candidateName, normalizedToolName, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            definition = candidate;
            return true;
        }

        definition = null!;
        return false;
    }

    private static bool TryParseStructuredNextActionArguments(
        string argumentsJson,
        ToolDefinition toolDefinition,
        out JsonObject normalizedArguments,
        out string reason) {
        normalizedArguments = new JsonObject();
        reason = "not_eligible";

        var rawArguments = (argumentsJson ?? string.Empty).Trim();
        if (rawArguments.Length == 0 || rawArguments == "{}") {
            reason = "no_arguments";
            return true;
        }

        if (rawArguments.Length > MaxStructuredNextActionArgumentsChars) {
            reason = "arguments_payload_too_large";
            return false;
        }

        JsonObject? parsed;
        try {
            parsed = JsonLite.Parse(rawArguments)?.AsObject();
        } catch {
            reason = "arguments_parse_failed";
            return false;
        }

        if (parsed is null) {
            reason = "arguments_not_object";
            return false;
        }

        normalizedArguments = CoerceStructuredNextActionArgumentsForTool(parsed, toolDefinition);
        reason = "arguments_normalized";
        return true;
    }

    private static JsonObject CoerceStructuredNextActionArgumentsForTool(JsonObject arguments, ToolDefinition toolDefinition) {
        var normalized = new JsonObject(StringComparer.Ordinal);
        var properties = toolDefinition.Parameters?.GetObject("properties");
        foreach (var pair in arguments) {
            var key = pair.Key ?? string.Empty;
            var value = pair.Value ?? JsonValue.Null;
            if (key.Length == 0 || value.Kind != IntelligenceX.Json.JsonValueKind.String || properties is null) {
                normalized.Add(key, value);
                continue;
            }

            if (!TryGetToolSchemaProperty(properties, key, out var propertySchema)) {
                normalized.Add(key, value);
                continue;
            }

            var type = (propertySchema.GetString("type") ?? string.Empty).Trim();
            var stringValue = (value.AsString() ?? string.Empty).Trim();
            if (type.Length == 0 || stringValue.Length == 0) {
                normalized.Add(key, value);
                continue;
            }

            if (type.Equals("boolean", StringComparison.OrdinalIgnoreCase)
                && TryParseFlexibleBoolean(stringValue, out var boolValue)) {
                normalized.Add(key, boolValue);
                continue;
            }

            if (type.Equals("integer", StringComparison.OrdinalIgnoreCase)
                && long.TryParse(stringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue)) {
                normalized.Add(key, intValue);
                continue;
            }

            if (type.Equals("number", StringComparison.OrdinalIgnoreCase)
                && double.TryParse(stringValue, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var doubleValue)) {
                normalized.Add(key, doubleValue);
                continue;
            }

            if (type.Equals("array", StringComparison.OrdinalIgnoreCase)) {
                if (TryParseJsonArrayString(stringValue, out var parsedArray)) {
                    normalized.Add(key, parsedArray);
                    continue;
                }

                var splitValues = SplitScalarListValue(stringValue);
                if (splitValues.Length > 0) {
                    var array = new JsonArray();
                    for (var i = 0; i < splitValues.Length; i++) {
                        array.Add(splitValues[i]);
                    }
                    normalized.Add(key, array);
                    continue;
                }
            }

            normalized.Add(key, value);
        }

        return normalized;
    }

    private static bool TryGetToolSchemaProperty(JsonObject properties, string argumentName, out JsonObject propertySchema) {
        propertySchema = null!;

        var exact = properties.GetObject(argumentName);
        if (exact is not null) {
            propertySchema = exact;
            return true;
        }

        foreach (var pair in properties) {
            var candidateName = pair.Key ?? string.Empty;
            if (!string.Equals(candidateName, argumentName, StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var asObject = pair.Value?.AsObject();
            if (asObject is null) {
                continue;
            }

            propertySchema = asObject;
            return true;
        }

        return false;
    }

    private static bool TryParseFlexibleBoolean(string value, out bool parsed) {
        return TryParseProtocolBoolean((value ?? string.Empty).Trim(), out parsed);
    }

    private static bool TryParseJsonArrayString(string value, out JsonArray parsedArray) {
        parsedArray = null!;
        if (string.IsNullOrWhiteSpace(value)) {
            return false;
        }

        var normalized = value.Trim();
        if (normalized.Length < 2 || normalized[0] != '[' || normalized[^1] != ']') {
            return false;
        }

        try {
            parsedArray = JsonLite.Parse(normalized)?.AsArray() ?? null!;
        } catch {
            parsedArray = null!;
        }

        return parsedArray is not null;
    }

}
