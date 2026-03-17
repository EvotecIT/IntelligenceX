using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using IntelligenceX.Chat.Tooling;
using IntelligenceX.Tools;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {

    private static string BuildToolExecutionNudgePrompt(string userRequest, string assistantDraft) {
        return BuildToolExecutionNudgePrompt(userRequest, assistantDraft, toolDefinitions: null);
    }

    private static string BuildToolExecutionNudgePrompt(
        string userRequest,
        string assistantDraft,
        IReadOnlyList<ToolDefinition>? toolDefinitions) {
        var requestText = string.IsNullOrWhiteSpace(userRequest) ? "(empty)" : userRequest.Trim();
        var draftText = string.IsNullOrWhiteSpace(assistantDraft) ? "(empty)" : assistantDraft.Trim();
        var executionHintBlock = FormatExecutionAvailabilityHintBlock(toolDefinitions);
        return $$"""
            [Execution correction]
            {{ExecutionCorrectionMarker}}
            The previous assistant draft did not execute tools.

            User request:
            {{requestText}}

            Previous assistant draft:
            {{draftText}}

            Execute available tools now when they can satisfy this request.
            Do not ask for another confirmation unless a required input cannot be inferred or discovered.
            {{executionHintBlock}}
            If tools truly cannot satisfy the request, explain the exact blocker and the minimal missing input.
            """;
    }

    private static string BuildNoToolExecutionWatchdogPrompt(string userRequest, string assistantDraft) {
        return BuildNoToolExecutionWatchdogPrompt(userRequest, assistantDraft, toolDefinitions: null);
    }

    private static string BuildNoToolExecutionWatchdogPrompt(
        string userRequest,
        string assistantDraft,
        IReadOnlyList<ToolDefinition>? toolDefinitions) {
        var requestText = TrimForPrompt(userRequest, ToolReceiptCorrectionMaxUserRequestChars);
        var draftText = TrimForPrompt(assistantDraft, ToolReceiptCorrectionMaxDraftChars);
        var executionHintBlock = FormatExecutionAvailabilityHintBlock(toolDefinitions);
        return $$"""
            [Execution watchdog]
            {{ExecutionWatchdogMarker}}
            The previous retries still produced zero tool calls in this turn.

            User request:
            {{requestText}}

            Previous assistant draft:
            {{draftText}}

            If tools can satisfy this request, call them now in this turn.
            {{executionHintBlock}}
            If tools cannot satisfy this request, do not imply execution. State the exact blocker and the minimal missing input.
            """;
    }

    private static string BuildExecutionContractBlockerText(string userRequest, string assistantDraft, string reason) {
        var requestText = TrimForPrompt(userRequest, 280);
        var reasonCode = string.IsNullOrWhiteSpace(reason) ? "no_tool_calls_after_retries" : reason.Trim();
        var detailLine = string.Equals(reasonCode, "background_work_waiting_on_prerequisites", StringComparison.OrdinalIgnoreCase)
            ? "Prepared follow-up work is waiting on prerequisite helper steps before a safe replay can continue."
            : string.Empty;
        var replayActionBlock = BuildExecutionContractReplayActionBlock(userRequest, assistantDraft);
        return $$"""
            [Execution blocked]
            {{ExecutionContractMarker}}
            I do not have confirmed tool output for this selected action yet.

            Selected action request:
            {{requestText}}

            Reason code: {{reasonCode}}
            {{detailLine}}

            Please retry this action in this context, or use the action command below.
            {{replayActionBlock}}
            """;
    }

    private static string BuildBackgroundWorkDependencyRecoveryPrompt(
        string userRequest,
        string assistantDraft,
        BackgroundWorkDependencyRecoverySummary summary,
        string reason) {
        var requestText = TrimForPrompt(userRequest, ToolReceiptCorrectionMaxUserRequestChars);
        var draftText = TrimForPrompt(assistantDraft, ToolReceiptCorrectionMaxDraftChars);
        var helperToolsLine = summary.HelperToolNames.Length == 0
            ? "helper_tools: (unknown)"
            : "helper_tools: " + string.Join(", ", summary.HelperToolNames);
        var authArgsLine = summary.AuthenticationArgumentNames.Length == 0
            ? string.Empty
            : "runtime_auth_args: " + string.Join(", ", summary.AuthenticationArgumentNames);
        var setupHelpersLine = summary.SetupHelperToolNames.Length == 0
            ? string.Empty
            : "setup_helpers: " + string.Join(", ", summary.SetupHelperToolNames);
        var cooldownHelpersLine = summary.RetryCooldownHelperToolNames.Length == 0
            ? string.Empty
            : "cooldown_helpers: " + string.Join(", ", summary.RetryCooldownHelperToolNames);
        return $$"""
            [Background prerequisite recovery]
            ix:background-prerequisite-recovery:v1
            Selected action request:
            {{requestText}}

            Previous assistant draft:
            {{draftText}}

            Prepared follow-up work is blocked on prerequisite helpers.
            blocked_items: {{summary.BlockedItemCount}}
            {{helperToolsLine}}
            recovery_reason: {{reason}}
            {{authArgsLine}}
            {{setupHelpersLine}}
            {{cooldownHelpersLine}}

            Requirements:
            - Do not claim execution completed.
            - If runtime auth context is missing, ask only for the minimal runtime auth input needed to continue.
            - If runtime setup context is missing, ask only for the minimal setup or runtime profile details needed to continue.
            - If helper retry is cooling down or still pending, explain that prepared follow-up work is waiting on prerequisite helpers and that execution can continue after they succeed.
            - Do not ask for broad confirmation.
            - Keep the response concise and execution-focused.
            """;
    }

    private static string BuildBackgroundWorkDependencyRecoveryBlockerText(
        string userRequest,
        string assistantDraft,
        BackgroundWorkDependencyRecoverySummary summary,
        string reason) {
        var requestText = TrimForPrompt(userRequest, 280);
        var helperToolsLine = summary.HelperToolNames.Length == 0
            ? "helper_tools: (unknown)"
            : "helper_tools: " + string.Join(", ", summary.HelperToolNames);
        var replayActionBlock = BuildExecutionContractReplayActionBlock(userRequest, assistantDraft);

        if (string.Equals(reason, "background_prerequisite_auth_context_required", StringComparison.OrdinalIgnoreCase)) {
            var authArgs = summary.AuthenticationArgumentNames.Length == 0
                ? "runtime authentication context"
                : string.Join(", ", summary.AuthenticationArgumentNames);
            return $$"""
                [Execution blocked]
                {{ExecutionContractMarker}}
                I do not have confirmed tool output for this selected action yet.

                Selected action request:
                {{requestText}}

                Reason code: {{reason}}
                Prepared follow-up work is blocked on prerequisite helpers: {{string.Join(", ", summary.HelperToolNames)}}.
                To continue, provide the minimal runtime authentication input required for this session: {{authArgs}}.

                Please retry this action in this context, or use the action command below.
                {{replayActionBlock}}
                """;
        }

        if (string.Equals(reason, "background_prerequisite_setup_context_required", StringComparison.OrdinalIgnoreCase)) {
            var setupHelpers = summary.SetupHelperToolNames.Length == 0
                ? helperToolsLine
                : "setup_helpers: " + string.Join(", ", summary.SetupHelperToolNames);
            return $$"""
                [Execution blocked]
                {{ExecutionContractMarker}}
                I do not have confirmed tool output for this selected action yet.

                Selected action request:
                {{requestText}}

                Reason code: {{reason}}
                Prepared follow-up work is blocked on prerequisite setup helpers.
                {{setupHelpers}}
                Provide the minimal setup or runtime-profile details needed for those helpers, then retry.

                Please retry this action in this context, or use the action command below.
                {{replayActionBlock}}
                """;
        }

        return $$"""
            [Execution blocked]
            {{ExecutionContractMarker}}
            I do not have confirmed tool output for this selected action yet.

            Selected action request:
            {{requestText}}

            Reason code: {{reason}}
            Prepared follow-up work is still waiting on prerequisite helpers.
            {{helperToolsLine}}
            The helper retry is still pending, so execution can continue after the prerequisite step succeeds.

            Please retry this action in this context, or use the action command below.
            {{replayActionBlock}}
            """;
    }

    private static string BuildExecutionContractEscapePrompt(string userRequest, string assistantDraft) {
        return BuildExecutionContractEscapePrompt(userRequest, assistantDraft, toolDefinitions: null);
    }

    private static string BuildExecutionContractEscapePrompt(
        string userRequest,
        string assistantDraft,
        IReadOnlyList<ToolDefinition>? toolDefinitions) {
        var requestText = TrimForPrompt(userRequest, ToolReceiptCorrectionMaxUserRequestChars);
        var draftText = TrimForPrompt(assistantDraft, ToolReceiptCorrectionMaxDraftChars);
        var executionHintBlock = FormatExecutionAvailabilityHintBlock(toolDefinitions, bulletPrefix: "- ");
        return $$"""
            [Execution contract escape]
            {{ExecutionContractEscapeMarker}}
            This action-selection turn still has zero tool calls.

            Selected action request:
            {{requestText}}

            Previous assistant draft:
            {{draftText}}

            Retry now with full tool availability for this turn.
            Requirements:
            - Call at least one relevant tool in this turn if any registered tool can satisfy the request.
            {{executionHintBlock}}
            - If no tool can satisfy the request, do not claim execution. Explain the exact blocker and the minimal missing input.
            - Keep the response concise and execution-focused.
            """;
    }

    private static string BuildContinuationSubsetEscapePrompt(string userRequest, string assistantDraft) {
        return BuildContinuationSubsetEscapePrompt(userRequest, assistantDraft, toolDefinitions: null);
    }

    private static string BuildContinuationSubsetEscapePrompt(
        string userRequest,
        string assistantDraft,
        IReadOnlyList<ToolDefinition>? toolDefinitions) {
        var requestText = TrimForPrompt(userRequest, ToolReceiptCorrectionMaxUserRequestChars);
        var draftText = TrimForPrompt(assistantDraft, ToolReceiptCorrectionMaxDraftChars);
        var executionHintBlock = FormatExecutionAvailabilityHintBlock(toolDefinitions, bulletPrefix: "- ");
        return $$"""
            [Continuation subset escape]
            {{ContinuationSubsetEscapeMarker}}
            This follow-up turn reused a narrowed tool subset and still produced zero tool activity.

            User request:
            {{requestText}}

            Previous assistant draft:
            {{draftText}}

            Retry now with full tool availability for this turn.
            Requirements:
            - Call at least one relevant tool in this turn when any registered tool can satisfy the request.
            {{executionHintBlock}}
            - If no tool can satisfy the request, state the exact blocker and the minimal missing input.
            - Keep the response concise and execution-focused.
            """;
    }

    private static string FormatExecutionAvailabilityHintBlock(
        IReadOnlyList<ToolDefinition>? toolDefinitions,
        string bulletPrefix = "") {
        var lines = ToolExecutionAvailabilityHints.BuildPromptHintLines(toolDefinitions);
        if (lines.Count == 0) {
            return string.Empty;
        }

        var builder = new StringBuilder();
        for (var i = 0; i < lines.Count; i++) {
            var line = (lines[i] ?? string.Empty).Trim();
            if (line.Length == 0) {
                continue;
            }

            if (builder.Length > 0) {
                builder.AppendLine();
            }

            if (bulletPrefix.Length > 0 && !line.StartsWith(bulletPrefix, StringComparison.Ordinal)) {
                var normalized = line.StartsWith("- ", StringComparison.Ordinal) ? line.Substring(2) : line;
                builder.Append(bulletPrefix);
                builder.Append(normalized);
                continue;
            }

            builder.Append(line);
        }

        return builder.ToString();
    }

    private static string BuildExecutionContractReplayActionBlock(string userRequest, string assistantDraft) {
        if (!TryResolveReplayActionForExecutionContract(
                userRequest,
                assistantDraft,
                out var actionId,
                out var actionTitle,
                out var actionRequest,
                out var mutability)) {
            return string.Empty;
        }

        actionTitle = NormalizeReplayActionText(actionTitle, maxChars: 120);
        actionRequest = NormalizeReplayActionText(actionRequest, maxChars: 220);
        var mutabilityLine = mutability switch {
            ActionMutability.Mutating => "mutating: true",
            ActionMutability.ReadOnly => "mutating: false",
            _ => string.Empty
        };
        var mutabilityBlock = mutabilityLine.Length == 0 ? string.Empty : mutabilityLine + Environment.NewLine;

        return $$"""

            [Action]
            ix:action:v1
            id: {{actionId}}
            title: {{actionTitle}}
            request: {{actionRequest}}
            {{mutabilityBlock}}reply: /act {{actionId}}
            """;
    }

    private static bool TryResolveReplayActionForExecutionContract(
        string userRequest,
        string assistantDraft,
        out string actionId,
        out string actionTitle,
        out string actionRequest,
        out ActionMutability mutability) {
        if (TryParseActionSelectionForReplay(userRequest, out actionId, out actionTitle, out actionRequest, out mutability)) {
            return true;
        }

        return TryParseSinglePendingActionForReplay(assistantDraft, out actionId, out actionTitle, out actionRequest, out mutability);
    }

    private static bool TryParseSinglePendingActionForReplay(
        string assistantDraft,
        out string actionId,
        out string actionTitle,
        out string actionRequest,
        out ActionMutability mutability) {
        actionId = string.Empty;
        actionTitle = string.Empty;
        actionRequest = string.Empty;
        mutability = ActionMutability.Unknown;

        var actions = ExtractPendingActions(assistantDraft);
        if (actions.Count == 0) {
            actions = ExtractFallbackChoicePendingActions(assistantDraft);
        }
        if (actions.Count != 1) {
            return false;
        }

        var action = actions[0];
        mutability = action.Mutability;
        actionId = NormalizeReplayActionIdToken(action.Id);
        if (actionId.Length == 0) {
            return false;
        }

        actionTitle = NormalizeReplayActionText(action.Title, maxChars: 200);
        actionRequest = NormalizeReplayActionText(action.Request, maxChars: 600);
        if (actionRequest.Length == 0) {
            actionRequest = actionTitle;
        }
        if (actionRequest.Length == 0) {
            actionRequest = "Retry selected action.";
        }
        if (actionTitle.Length == 0) {
            actionTitle = actionRequest;
        }

        return true;
    }

    private static bool TryParseActionSelectionForReplay(
        string userRequest,
        out string actionId,
        out string actionTitle,
        out string actionRequest,
        out ActionMutability mutability) {
        actionId = string.Empty;
        actionTitle = string.Empty;
        actionRequest = string.Empty;
        mutability = ActionMutability.Unknown;

        var normalized = (userRequest ?? string.Empty).Trim();
        if (normalized.Length == 0 || normalized.Length > MaxActionSelectionPayloadChars || normalized[0] != '{') {
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

            actionId = NormalizeReplayActionId(id);
            if (actionId.Length == 0) {
                return false;
            }

            actionTitle = NormalizeReplayActionText(TryReadReplayActionSelectionText(selection, "title"), maxChars: 200);
            actionRequest = NormalizeReplayActionText(TryReadReplayActionSelectionText(selection, "request"), maxChars: 600);
            mutability = ResolveActionSelectionMutability(selection);
            if (actionRequest.Length == 0) {
                actionRequest = actionTitle;
            }
            if (actionRequest.Length == 0) {
                actionRequest = "Retry selected action.";
            }
            if (actionTitle.Length == 0) {
                actionTitle = actionRequest;
            }

            return true;
        } catch (JsonException) {
            return false;
        }
    }

    private static string NormalizeReplayActionId(JsonElement id) {
        switch (id.ValueKind) {
            case JsonValueKind.String: {
                    return NormalizeReplayActionIdToken(id.GetString() ?? string.Empty);
                }
            case JsonValueKind.Number:
                if (!id.TryGetInt64(out var numericId) || numericId <= 0) {
                    return string.Empty;
                }

                return numericId.ToString();
            default:
                return string.Empty;
        }
    }

    private static string NormalizeReplayActionIdToken(string idToken) {
        var token = ReadFirstToken((idToken ?? string.Empty).Trim());
        if (token.Length == 0) {
            return string.Empty;
        }

        var sb = new StringBuilder(Math.Min(token.Length, 64));
        for (var i = 0; i < token.Length && sb.Length < 64; i++) {
            var ch = token[i];
            if (char.IsWhiteSpace(ch) || char.IsControl(ch)) {
                continue;
            }
            if (ch == ':' || ch == ';') {
                continue;
            }

            sb.Append(ch);
        }

        return sb.ToString().Trim();
    }

    private static string TryReadReplayActionSelectionText(JsonElement selection, string propertyName) {
        if (!selection.TryGetProperty(propertyName, out var value)) {
            return string.Empty;
        }

        if (value.ValueKind == JsonValueKind.String) {
            return value.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static string NormalizeReplayActionText(string text, int maxChars) {
        var normalized = CollapseWhitespace(text ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return string.Empty;
        }

        if (normalized.Length > maxChars) {
            normalized = normalized.Substring(0, maxChars);
        }

        return normalized.Trim();
    }

}
