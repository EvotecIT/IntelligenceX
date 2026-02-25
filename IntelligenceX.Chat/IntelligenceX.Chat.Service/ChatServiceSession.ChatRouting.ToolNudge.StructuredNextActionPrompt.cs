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
            Do not ask for another "go ahead" when a safe read-only next step is available.
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
        var actions = ExtractPendingActions(assistantDraft);
        if (actions.Count != 1) {
            return false;
        }

        var action = actions[0];
        return action.Mutability == ActionMutability.ReadOnly && !string.IsNullOrWhiteSpace(action.Id);
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

        if (normalized[0] != '{') {
            return false;
        }

        // Cheap pre-check to avoid parsing arbitrary small JSON blobs on every request.
        // We intentionally keep this case-sensitive: System.Text.Json property matching is case-sensitive by default.
        if (normalized.IndexOf("\"ix_action_selection\"", StringComparison.Ordinal) < 0 || normalized.IndexOf("\"id\"", StringComparison.Ordinal) < 0) {
            return false;
        }

        try {
            using var doc = JsonDocument.Parse(normalized, ActionSelectionJsonOptions);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) {
                return false;
            }

            if (!doc.RootElement.TryGetProperty("ix_action_selection", out var selection) || selection.ValueKind != JsonValueKind.Object) {
                return false;
            }

            if (!selection.TryGetProperty("id", out var id)) {
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

    private static ActionMutability ResolveActionSelectionMutability(JsonElement selection) {
        bool? mutating = null;
        bool? readOnly = null;

        if (TryReadSelectionBoolean(selection, "mutating", out var parsedMutating)) {
            mutating = parsedMutating;
        }

        if (TryReadSelectionBoolean(selection, "readonly", out var parsedReadOnly)) {
            readOnly = parsedReadOnly;
        }

        return ResolveActionMutability(mutating, readOnly);
    }

    private static ActionMutability ResolveActionMutability(bool? mutating, bool? readOnly) {
        if (mutating.HasValue) {
            return mutating.Value ? ActionMutability.Mutating : ActionMutability.ReadOnly;
        }

        if (readOnly.HasValue) {
            return readOnly.Value ? ActionMutability.ReadOnly : ActionMutability.Mutating;
        }

        return ActionMutability.Unknown;
    }

    private static ActionMutability ResolveActionMutabilityFromNullableBoolean(bool? mutating) {
        return mutating.HasValue
            ? (mutating.Value ? ActionMutability.Mutating : ActionMutability.ReadOnly)
            : ActionMutability.Unknown;
    }

}
