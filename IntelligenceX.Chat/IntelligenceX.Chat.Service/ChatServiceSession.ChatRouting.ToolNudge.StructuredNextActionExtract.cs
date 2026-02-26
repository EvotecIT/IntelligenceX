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
        parsed = false;
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        if (bool.TryParse(normalized, out parsed)) {
            return true;
        }

        if (string.Equals(normalized, "1", StringComparison.Ordinal)) {
            parsed = true;
            return true;
        }

        if (string.Equals(normalized, "0", StringComparison.Ordinal)) {
            parsed = false;
            return true;
        }

        if (string.Equals(normalized, "yes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "on", StringComparison.OrdinalIgnoreCase)) {
            parsed = true;
            return true;
        }

        if (string.Equals(normalized, "no", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "off", StringComparison.OrdinalIgnoreCase)) {
            parsed = false;
            return true;
        }

        return false;
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

}
