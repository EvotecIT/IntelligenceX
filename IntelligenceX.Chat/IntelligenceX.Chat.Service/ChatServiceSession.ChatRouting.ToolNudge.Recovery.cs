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

    private static bool LooksLikeMultilineFollowUpBlockerDraft(string assistantDraft) {
        var draft = (assistantDraft ?? string.Empty).Trim();
        if (draft.Length < 48 || draft.Length > 2400) {
            return false;
        }

        if (!draft.Contains('\n', StringComparison.Ordinal) && !draft.Contains('\r', StringComparison.Ordinal)) {
            return false;
        }

        if (draft.Contains("ix:action:v1", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        var lines = SplitLines(draft);
        var nonEmptyCount = 0;
        var bulletLikeCount = 0;
        for (var i = 0; i < lines.Count && nonEmptyCount < 24; i++) {
            var trimmed = (lines[i] ?? string.Empty).Trim();
            if (trimmed.Length == 0) {
                continue;
            }

            nonEmptyCount++;
            if (IsBulletLikeLine(trimmed)) {
                bulletLikeCount++;
            }
        }

        // Treat only compact "blocked + minimal input" style drafts as replay blockers.
        // Longer evidence-heavy summaries often contain bullets but should be returned as final output.
        return nonEmptyCount >= 3
               && nonEmptyCount <= 10
               && bulletLikeCount >= 2;
    }

    private static bool IsBulletLikeLine(string value) {
        if (value.StartsWith("- ", StringComparison.Ordinal)
            || value.StartsWith("* ", StringComparison.Ordinal)
            || value.StartsWith("• ", StringComparison.Ordinal)
            || value.StartsWith("– ", StringComparison.Ordinal)
            || value.StartsWith("— ", StringComparison.Ordinal)) {
            return true;
        }

        var idx = 0;
        while (idx < value.Length && char.IsDigit(value[idx])) {
            idx++;
        }

        if (idx > 0 && idx + 1 < value.Length) {
            var marker = value[idx];
            if ((marker == '.' || marker == ')' || marker == ':' || marker == ']')
                && char.IsWhiteSpace(value[idx + 1])) {
                return true;
            }
        }

        return false;
    }

    private static bool LooksLikeExecutionAcknowledgeDraft(string assistantDraft) {
        var draft = (assistantDraft ?? string.Empty).Trim();
        if (draft.Length < 24 || draft.Length > 280) {
            return false;
        }

        if (draft.Contains('\n', StringComparison.Ordinal) || draft.Contains('\r', StringComparison.Ordinal)) {
            return false;
        }

        if (ContainsQuestionSignal(draft)) {
            return false;
        }

        if (draft.Contains('|', StringComparison.Ordinal)
            || draft.Contains('{', StringComparison.Ordinal)
            || draft.Contains('}', StringComparison.Ordinal)
            || draft.Contains('[', StringComparison.Ordinal)
            || draft.Contains(']', StringComparison.Ordinal)
            || draft.Contains('<', StringComparison.Ordinal)
            || draft.Contains('>', StringComparison.Ordinal)
            || draft.Contains('=', StringComparison.Ordinal)) {
            return false;
        }

        var tokenCount = CountLetterDigitTokens(draft, maxTokens: 64);
        return tokenCount >= 5 && tokenCount <= 48;
    }

    private static bool LooksLikeStructuredScopeChoiceDraft(string assistantDraft) {
        var draft = (assistantDraft ?? string.Empty).Trim();
        if (draft.Length < 48 || draft.Length > 900) {
            return false;
        }

        if (!draft.Contains('\n', StringComparison.Ordinal) && !draft.Contains('\r', StringComparison.Ordinal)) {
            return false;
        }

        if (draft.Contains("ix:action:v1", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        if (LooksLikeMultilineFollowUpBlockerDraft(draft)) {
            return false;
        }

        if (draft.Contains('|', StringComparison.Ordinal)
            || draft.Contains('{', StringComparison.Ordinal)
            || draft.Contains('}', StringComparison.Ordinal)
            || draft.Contains('[', StringComparison.Ordinal)
            || draft.Contains(']', StringComparison.Ordinal)) {
            return false;
        }

        var lines = SplitLines(draft);
        var nonEmptyCount = 0;
        for (var i = 0; i < lines.Count; i++) {
            if ((lines[i] ?? string.Empty).Trim().Length > 0) {
                nonEmptyCount++;
            }
        }

        if (nonEmptyCount < 2 || nonEmptyCount > 6) {
            return false;
        }

        // Scope-choice drafts usually present at least two inline options (for example two domain/DC anchors)
        // but do not include final evidence rows/tables.
        var backtickCount = 0;
        for (var i = 0; i < draft.Length; i++) {
            if (draft[i] == '`') {
                backtickCount++;
            }
        }

        return backtickCount >= 4;
    }

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
        if (!continuationFollowUpTurn || !compactFollowUpTurn) {
            return false;
        }

        // If this turn is already anchored to new contextual request content, avoid replaying stale carryover
        // actions from previous turns and let normal tool planning proceed.
        if (LooksLikeContextualFollowUpForExecutionNudge(userRequest, assistantDraft)) {
            return false;
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

}
