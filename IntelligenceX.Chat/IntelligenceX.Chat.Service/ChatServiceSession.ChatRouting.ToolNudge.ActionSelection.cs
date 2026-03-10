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
    private static string[] SplitScalarListValue(string value) {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return Array.Empty<string>();
        }

        var values = normalized.Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (values.Length == 0) {
            return Array.Empty<string>();
        }

        var result = new List<string>(values.Length);
        for (var i = 0; i < values.Length; i++) {
            var item = values[i].Trim();
            if (item.Length == 0) {
                continue;
            }

            result.Add(item);
        }

        return result.Count == 0 ? Array.Empty<string>() : result.ToArray();
    }

    private static bool TryReadNextActionToolName(JsonElement action, out string toolName) {
        toolName = string.Empty;

        if (action.ValueKind == JsonValueKind.String) {
            toolName = (action.GetString() ?? string.Empty).Trim();
            return toolName.Length > 0;
        }

        if (action.ValueKind != JsonValueKind.Object) {
            return false;
        }

        if (action.TryGetProperty("tool", out var toolNode) && toolNode.ValueKind == JsonValueKind.String) {
            toolName = (toolNode.GetString() ?? string.Empty).Trim();
            if (toolName.Length > 0) {
                return true;
            }
        }

        if (action.TryGetProperty("name", out var nameNode) && nameNode.ValueKind == JsonValueKind.String) {
            toolName = (nameNode.GetString() ?? string.Empty).Trim();
            if (toolName.Length > 0) {
                return true;
            }
        }

        if (action.TryGetProperty("tool_name", out var toolNameNode) && toolNameNode.ValueKind == JsonValueKind.String) {
            toolName = (toolNameNode.GetString() ?? string.Empty).Trim();
            if (toolName.Length > 0) {
                return true;
            }
        }

        if (action.TryGetProperty("toolName", out var toolNameCamelNode) && toolNameCamelNode.ValueKind == JsonValueKind.String) {
            toolName = (toolNameCamelNode.GetString() ?? string.Empty).Trim();
            if (toolName.Length > 0) {
                return true;
            }
        }

        return false;
    }

    private static string TryReadNextActionArgumentsJson(JsonElement action) {
        if (action.ValueKind != JsonValueKind.Object) {
            return "{}";
        }

        if (action.TryGetProperty("arguments", out var argsNode) && argsNode.ValueKind == JsonValueKind.Object) {
            return argsNode.GetRawText();
        }

        if (action.TryGetProperty("suggested_arguments", out var suggestedNode) && suggestedNode.ValueKind == JsonValueKind.Object) {
            return suggestedNode.GetRawText();
        }

        if (action.TryGetProperty("suggestedArguments", out suggestedNode) && suggestedNode.ValueKind == JsonValueKind.Object) {
            return suggestedNode.GetRawText();
        }

        if (action.TryGetProperty("args", out var argsAlias) && argsAlias.ValueKind == JsonValueKind.Object) {
            return argsAlias.GetRawText();
        }

        if (action.TryGetProperty("parameters", out var parametersNode) && parametersNode.ValueKind == JsonValueKind.Object) {
            return parametersNode.GetRawText();
        }

        return "{}";
    }

    private static string TryReadNextActionReason(JsonElement action) {
        if (action.ValueKind != JsonValueKind.Object) {
            return string.Empty;
        }

        if (action.TryGetProperty("reason", out var reasonNode) && reasonNode.ValueKind == JsonValueKind.String) {
            return (reasonNode.GetString() ?? string.Empty).Trim();
        }

        if (action.TryGetProperty("description", out var descriptionNode) && descriptionNode.ValueKind == JsonValueKind.String) {
            return (descriptionNode.GetString() ?? string.Empty).Trim();
        }

        return string.Empty;
    }

    private static double? TryReadNextActionConfidence(JsonElement action) {
        if (action.ValueKind != JsonValueKind.Object) {
            return null;
        }

        if (TryReadNormalizedConfidenceValue(action, "confidence", out var confidence)) {
            return confidence;
        }

        if (TryReadNormalizedConfidenceValue(action, "chain_confidence", out confidence)) {
            return confidence;
        }

        return null;
    }

    private static double? TryReadChainConfidence(JsonElement root) {
        if (TryFindNormalizedConfidenceValue(root, maxDepth: 3, out var confidence)) {
            return confidence;
        }

        return null;
    }

    private static bool TryReadNextActionsArray(JsonElement root, out JsonElement nextActions) {
        return TryFindNextActionsArray(root, maxDepth: 3, out nextActions);
    }

    private static bool TryFindNextActionsArray(JsonElement node, int maxDepth, out JsonElement nextActions) {
        if (TryReadNextActionsArrayDirect(node, out nextActions)) {
            return true;
        }

        if (maxDepth <= 0) {
            nextActions = default;
            return false;
        }

        if (node.ValueKind == JsonValueKind.Object) {
            foreach (var property in node.EnumerateObject()) {
                var value = property.Value;
                if (value.ValueKind != JsonValueKind.Object && value.ValueKind != JsonValueKind.Array) {
                    continue;
                }

                if (TryFindNextActionsArray(value, maxDepth - 1, out nextActions)) {
                    return true;
                }
            }
        } else if (node.ValueKind == JsonValueKind.Array) {
            var inspected = 0;
            foreach (var item in node.EnumerateArray()) {
                if (inspected >= 16) {
                    break;
                }

                inspected++;
                if (item.ValueKind != JsonValueKind.Object && item.ValueKind != JsonValueKind.Array) {
                    continue;
                }

                if (TryFindNextActionsArray(item, maxDepth - 1, out nextActions)) {
                    return true;
                }
            }
        }

        nextActions = default;
        return false;
    }

    private static bool TryReadNextActionsArrayDirect(JsonElement node, out JsonElement nextActions) {
        if (node.ValueKind == JsonValueKind.Object
            && node.TryGetProperty("next_actions", out nextActions)
            && nextActions.ValueKind == JsonValueKind.Array) {
            return true;
        }

        if (node.ValueKind == JsonValueKind.Object
            && node.TryGetProperty("nextActions", out nextActions)
            && nextActions.ValueKind == JsonValueKind.Array) {
            return true;
        }

        nextActions = default;
        return false;
    }

    private static bool TryFindNormalizedConfidenceValue(JsonElement node, int maxDepth, out double confidence) {
        if (TryReadNormalizedConfidenceValue(node, "chain_confidence", out confidence)) {
            return true;
        }

        if (maxDepth <= 0) {
            confidence = default;
            return false;
        }

        if (node.ValueKind == JsonValueKind.Object) {
            foreach (var property in node.EnumerateObject()) {
                var value = property.Value;
                if (value.ValueKind != JsonValueKind.Object && value.ValueKind != JsonValueKind.Array) {
                    continue;
                }

                if (TryFindNormalizedConfidenceValue(value, maxDepth - 1, out confidence)) {
                    return true;
                }
            }
        } else if (node.ValueKind == JsonValueKind.Array) {
            var inspected = 0;
            foreach (var item in node.EnumerateArray()) {
                if (inspected >= 16) {
                    break;
                }

                inspected++;
                if (item.ValueKind != JsonValueKind.Object && item.ValueKind != JsonValueKind.Array) {
                    continue;
                }

                if (TryFindNormalizedConfidenceValue(item, maxDepth - 1, out confidence)) {
                    return true;
                }
            }
        }

        confidence = default;
        return false;
    }

    private static bool TryReadNormalizedConfidenceValue(JsonElement node, string propertyName, out double confidence) {
        confidence = default;
        if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty(propertyName, out var value)) {
            return false;
        }

        double rawValue;
        switch (value.ValueKind) {
            case JsonValueKind.Number when value.TryGetDouble(out rawValue):
                break;
            case JsonValueKind.String when TryParseNamedConfidence((value.GetString() ?? string.Empty).Trim(), out rawValue):
                break;
            case JsonValueKind.String when double.TryParse(
                    (value.GetString() ?? string.Empty).Trim(),
                    NumberStyles.Float | NumberStyles.AllowThousands,
                    CultureInfo.InvariantCulture,
                    out rawValue):
                break;
            default:
                return false;
        }

        if (double.IsNaN(rawValue) || double.IsInfinity(rawValue)) {
            return false;
        }

        confidence = Math.Clamp(rawValue, 0d, 1d);
        return true;
    }

    private static bool TryParseNamedConfidence(string value, out double confidence) {
        confidence = default;
        if (string.IsNullOrWhiteSpace(value)) {
            return false;
        }

        switch (value.Trim().ToLowerInvariant()) {
            case "high":
                confidence = 0.85d;
                return true;
            case "medium":
                confidence = 0.60d;
                return true;
            case "low":
                confidence = 0.25d;
                return true;
            default:
                return false;
        }
    }

    private static string BuildStructuredNextActionRetryPrompt(
        string userRequest,
        string assistantDraft,
        string sourceTool,
        string nextTool,
        string argumentsJson,
        string nextReason) {
        var requestText = TrimForPrompt(userRequest, 320);
        var draftText = TrimForPrompt(assistantDraft, 800);
        var sourceToolText = string.IsNullOrWhiteSpace(sourceTool) ? "(unknown)" : sourceTool.Trim();
        var nextReasonText = string.IsNullOrWhiteSpace(nextReason) ? "(not provided)" : nextReason.Trim();

        return $$"""
            [Structured next action retry]
            {{StructuredNextActionRetryMarker}}
            Continuation request:
            {{requestText}}

            Previous assistant draft:
            {{draftText}}

            Previous tool guidance:
            source_tool: {{sourceToolText}}
            next_tool: {{nextTool}}
            reason: {{nextReasonText}}
            arguments_json: {{argumentsJson}}

            Call tool `{{nextTool}}` now using the provided arguments.
            Do not ask for another confirmation before attempting this read-only continuation.
            If this still cannot proceed, explain the exact blocker and the minimal missing input once.
            """;
    }

    private static string BuildToolProgressRecoveryPrompt(
        string userRequest,
        string assistantDraft,
        IReadOnlyList<ToolCallDto> toolCalls) {
        var requestText = TrimForPrompt(userRequest, 320);
        var draftText = TrimForPrompt(assistantDraft, 800);
        var executedTools = BuildExecutedToolsSummary(toolCalls);

        return $$"""
            [Tool progress recovery]
            {{ToolProgressRecoveryMarker}}
            Continuation request:
            {{requestText}}

            Previous assistant draft:
            {{draftText}}

            Tools already executed in this turn:
            {{executedTools}}

            Continue execution in the same turn.
            Do not ask for another short confirmation phrase when a safe read-only next step is available.
            Choose the best next tool from the available tool list and execute it now.
            If execution is truly blocked, return one concise blocker with only the minimal missing input.
            """;
    }

    private static string BuildExecutedToolsSummary(IReadOnlyList<ToolCallDto> toolCalls) {
        if (toolCalls.Count == 0) {
            return "(none)";
        }

        var distinct = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < toolCalls.Count; i++) {
            var name = (toolCalls[i].Name ?? string.Empty).Trim();
            if (name.Length == 0 || !seen.Add(name)) {
                continue;
            }

            distinct.Add(name);
            if (distinct.Count >= 8) {
                break;
            }
        }

        return distinct.Count == 0 ? "(none)" : string.Join(", ", distinct);
    }

    private static bool HasSingleReadOnlyPendingActionEnvelope(string assistantDraft) {
        return TryGetSinglePendingActionEnvelopeMutability(assistantDraft, out var mutability)
               && mutability == ActionMutability.ReadOnly;
    }

    private static bool TryGetSinglePendingActionEnvelopeMutability(string assistantDraft, out ActionMutability mutability) {
        mutability = ActionMutability.Unknown;
        var actions = ExtractPendingActions(assistantDraft);
        if (actions.Count != 1) {
            return false;
        }

        var action = actions[0];
        if (string.IsNullOrWhiteSpace(action.Id)) {
            return false;
        }

        mutability = action.Mutability;
        return true;
    }

    private static bool LooksLikeActionSelectionPayload(string text) {
        return TryReadActionSelectionIntent(
            text: text,
            actionId: out _,
            mutability: out _);
    }

    private static bool TryReadActionSelectionIntent(string text, out string actionId, out ActionMutability mutability) {
        actionId = string.Empty;
        mutability = ActionMutability.Unknown;

        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0 || normalized.Length > MaxActionSelectionPayloadChars) {
            return false;
        }

        if (!TryExtractActionSelectionPayloadJson(normalized, out var payload)) {
            return false;
        }

        // Cheap structural pre-check to avoid parsing arbitrary blobs on every request.
        var hasSupportedIdField = payload.IndexOf("\"id\"", StringComparison.OrdinalIgnoreCase) >= 0
                                  || payload.IndexOf("\"action_id\"", StringComparison.OrdinalIgnoreCase) >= 0
                                  || payload.IndexOf("\"actionid\"", StringComparison.OrdinalIgnoreCase) >= 0;
        if (!hasSupportedIdField) {
            return false;
        }

        try {
            using var doc = JsonDocument.Parse(payload, ActionSelectionJsonOptions);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) {
                return false;
            }

            if (!TryGetObjectPropertyCaseInsensitive(
                    doc.RootElement,
                    out var selection,
                    "ix_action_selection",
                    "ixActionSelection",
                    "action_selection",
                    "actionSelection")
                || selection.ValueKind != JsonValueKind.Object) {
                return false;
            }

            if (!TryGetObjectPropertyCaseInsensitive(selection, out var id, "id", "action_id", "actionId")) {
                return false;
            }

            if (id.ValueKind == JsonValueKind.String) {
                actionId = (id.GetString() ?? string.Empty).Trim();
                if (actionId.Length == 0) {
                    return false;
                }
            } else if (id.ValueKind == JsonValueKind.Number) {
                if (!id.TryGetInt64(out var numericId) || numericId <= 0) {
                    return false;
                }
                actionId = numericId.ToString();
            } else {
                return false;
            }

            mutability = ResolveActionSelectionMutability(selection);
            return true;
        } catch (JsonException) {
            return false;
        }
    }

}
