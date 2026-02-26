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

    private const int MaxQuotedPhraseSpan = 140;
    // Security/perf: hard-cap the amount of untrusted input we will consider for action-selection detection.
    // This keeps any attempted parsing bounded even under adversarial input.
    private const int MaxActionSelectionPayloadChars = 4096;
    private const string ExecutionCorrectionMarker = "ix:execution-correction:v1";
    private const string ExecutionWatchdogMarker = "ix:execution-watchdog:v1";
    private const string ExecutionContractMarker = "ix:execution-contract:v1";
    private const string ExecutionContractEscapeMarker = "ix:execution-contract-escape:v1";
    private const string ContinuationSubsetEscapeMarker = "ix:continuation-subset-escape:v1";
    private const string StructuredNextActionRetryMarker = "ix:structured-next-action-retry:v1";
    private const string ToolProgressRecoveryMarker = "ix:tool-progress-recovery:v1";
    private const int MaxStructuredNextActionArgumentsChars = 32_768;
    private const int NoResultPhaseLoopThresholdWithToolActivity = 8;
    private const int NoResultPhaseLoopThresholdWithoutToolActivity = 6;
    private const int FollowUpQuestionMaxTokens = 12;
    private static readonly char[] CallToActionCommaPunctuation = new[] { ',', '\uFF0C', '\u3001', '\u060C' };
    private static readonly char[] CallToActionColonPunctuation = new[] { ':', '\uFF1A', '\uFE13' };
    // Keep this token set language-inclusive so boolean schema coercion does not depend on English-only replies.
    private static readonly HashSet<string> FlexibleBooleanTrueTokens = new(StringComparer.OrdinalIgnoreCase) {
        "yes", "on",
        "si", "sí", "sim", "oui", "ja", "tak",
        "да", "نعم", "是", "はい", "예",
        "evet"
    };
    private static readonly HashSet<string> FlexibleBooleanFalseTokens = new(StringComparer.OrdinalIgnoreCase) {
        "no", "off",
        "non", "nein", "nie", "não", "nao",
        "нет", "لا", "否", "いいえ", "아니요",
        "hayir", "hayır"
    };
    private enum ActionMutability {
        Unknown = 0,
        ReadOnly = 1,
        Mutating = 2
    }

    private static readonly JsonDocumentOptions ActionSelectionJsonOptions = new() {
        MaxDepth = 16,
        CommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false
    };

    private static bool ShouldAttemptToolExecutionNudge(string userRequest, string assistantDraft, bool toolsAvailable, int priorToolCalls,
        int assistantDraftToolCalls, bool usedContinuationSubset) {
        return EvaluateToolExecutionNudgeDecision(
            userRequest,
            assistantDraft,
            toolsAvailable,
            priorToolCalls,
            assistantDraftToolCalls,
            usedContinuationSubset,
            compactFollowUpHint: false,
            out _);
    }

    private static bool ShouldEnforceExecuteOrExplainContract(string userRequest) {
        return TryReadActionSelectionIntent(
                   text: (userRequest ?? string.Empty).Trim(),
                   actionId: out _,
                   mutability: out var mutability)
               && mutability == ActionMutability.Mutating;
    }

    private static bool ShouldForceExecutionContractBlockerAtFinalize(
        string userRequest,
        bool executionContractApplies,
        bool autoPendingActionReplayUsed,
        bool executionNudgeUsed,
        bool noToolExecutionWatchdogUsed,
        bool continuationFollowUpTurn,
        bool compactFollowUpTurn,
        bool toolActivityDetected,
        string assistantDraft) {
        if (toolActivityDetected) {
            return false;
        }

        var draft = (assistantDraft ?? string.Empty).Trim();
        var looksLikeExecutionIntentPlaceholder = LooksLikeExecutionIntentPlaceholderDraft(userRequest, draft);

        // Only force the blocker when this turn entered an execution-required path.
        if (!executionContractApplies
            && !autoPendingActionReplayUsed
            && !executionNudgeUsed
            && !noToolExecutionWatchdogUsed
            && !continuationFollowUpTurn
            && !compactFollowUpTurn
            && !looksLikeExecutionIntentPlaceholder) {
            return false;
        }

        if (draft.Length == 0) {
            return true;
        }

        // If the draft is already a structured blocker/action envelope, keep it.
        if (draft.Contains(ExecutionContractMarker, StringComparison.OrdinalIgnoreCase)
            || draft.Contains("ix:action:v1", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        if (continuationFollowUpTurn || compactFollowUpTurn) {
            if (ContainsQuestionSignal(draft)) {
                return false;
            }

            return LooksLikeExecutionAcknowledgeDraft(draft);
        }

        return true;
    }

    private static bool ShouldAttemptNoToolExecutionWatchdog(
        string userRequest,
        string assistantDraft,
        bool toolsAvailable,
        int priorToolCalls,
        int priorToolOutputs,
        int assistantDraftToolCalls,
        bool continuationFollowUpTurn,
        bool compactFollowUpTurn,
        bool executionNudgeUsed,
        bool toolReceiptCorrectionUsed,
        bool watchdogAlreadyUsed,
        out string reason) {
        reason = "not_eligible";

        if (watchdogAlreadyUsed) {
            reason = "watchdog_already_used";
            return false;
        }

        var executionContractApplies = ShouldEnforceExecuteOrExplainContract(userRequest);
        var contextualFollowUp = LooksLikeContextualFollowUpForExecutionNudge(userRequest, assistantDraft);
        var postRecoveryWatchdogEligible = executionNudgeUsed || toolReceiptCorrectionUsed || contextualFollowUp;
        if (!executionContractApplies
            && !continuationFollowUpTurn
            && !compactFollowUpTurn
            && !postRecoveryWatchdogEligible) {
            reason = "execution_contract_or_follow_up_not_applicable";
            return false;
        }

        if (!toolsAvailable) {
            reason = "tools_unavailable";
            return false;
        }

        if (priorToolCalls > 0 || priorToolOutputs > 0) {
            reason = "tool_activity_present";
            return false;
        }

        if (assistantDraftToolCalls > 0) {
            reason = "assistant_draft_has_tool_calls";
            return false;
        }

        var draft = (assistantDraft ?? string.Empty).Trim();
        if (draft.Length == 0) {
            reason = "empty_assistant_draft_watchdog_retry";
            return true;
        }

        // Avoid correction/watchdog feedback loops if a previous retry prompt is echoed back into the draft.
        if (draft.Contains(ExecutionWatchdogMarker, StringComparison.OrdinalIgnoreCase)
            || draft.Contains(ExecutionContractMarker, StringComparison.OrdinalIgnoreCase)) {
            reason = "watchdog_or_contract_marker_present";
            return false;
        }

        if (!executionContractApplies) {
            if (!LooksLikeMultilineFollowUpBlockerDraft(draft) && !LooksLikeExecutionAcknowledgeDraft(draft)) {
                reason = "follow_up_draft_not_blocker_like";
                return false;
            }

            if (contextualFollowUp && !compactFollowUpTurn && !continuationFollowUpTurn) {
                reason = "contextual_follow_up_watchdog_retry";
                return true;
            }

            reason = "compact_follow_up_watchdog_retry";
            return true;
        }

        reason = (!executionNudgeUsed && !toolReceiptCorrectionUsed)
            ? "strict_contract_watchdog_retry_no_prior_recovery"
            : "strict_contract_watchdog_retry";
        return true;
    }

    private static bool ShouldSuppressLocalToolRecoveryRetries(
        bool isLocalCompatibleLoopback,
        bool executionContractApplies,
        bool compactFollowUpTurn,
        bool toolsAvailable,
        int priorToolCalls,
        int priorToolOutputs,
        string userRequest,
        string assistantDraft) {
        if (!isLocalCompatibleLoopback) {
            return false;
        }

        if (executionContractApplies) {
            return false;
        }

        if (compactFollowUpTurn) {
            return false;
        }

        // If tools are available, allow retry logic to decide. Local suppression should only guard
        // truly toolless paths in compatible loopback mode.
        if (toolsAvailable) {
            return false;
        }

        if (priorToolCalls != 0 || priorToolOutputs != 0) {
            return false;
        }

        var draft = (assistantDraft ?? string.Empty).Trim();
        if (draft.Length == 0) {
            return true;
        }

        // In local loopback runs we still want recovery retries for execution-intent drafts so scenario validation
        // can converge instead of silently stopping with zero tool calls.
        if (LooksLikeExecutionAcknowledgeDraft(draft)
            || LooksLikeMultilineFollowUpBlockerDraft(draft)
            || LooksLikeStructuredScopeChoiceDraft(draft)
            || LooksLikeExecutionIntentPlaceholderDraft(userRequest, draft)) {
            return false;
        }

        if (ContainsQuestionSignal(draft)
            && AssistantDraftReferencesUserRequest(userRequest, draft)) {
            return false;
        }

        return true;
    }

    private static bool ShouldAttemptContinuationSubsetEscape(
        bool executionContractApplies,
        bool usedContinuationSubset,
        bool continuationSubsetEscapeUsed,
        bool toolsAvailable,
        int priorToolCalls,
        int priorToolOutputs,
        out string reason) {
        reason = "not_eligible";

        if (executionContractApplies) {
            reason = "execution_contract_turn";
            return false;
        }

        if (!usedContinuationSubset) {
            reason = "full_tools_already_available";
            return false;
        }

        if (continuationSubsetEscapeUsed) {
            reason = "subset_escape_already_used";
            return false;
        }

        if (!toolsAvailable) {
            reason = "tools_unavailable";
            return false;
        }

        if (priorToolCalls > 0 || priorToolOutputs > 0) {
            reason = "tool_activity_present";
            return false;
        }

        reason = "continuation_subset_no_tool_activity";
        return true;
    }

    private static bool EvaluateToolExecutionNudgeDecision(
        string userRequest,
        string assistantDraft,
        bool toolsAvailable,
        int priorToolCalls,
        int assistantDraftToolCalls,
        bool usedContinuationSubset,
        bool compactFollowUpHint,
        out string reason) {
        reason = "not_eligible";

        // Keep the eligibility checks inside this method (not only at the call site) so future callers can't
        // accidentally force a retry when tools can't run or when tool execution is already happening.
        if (!toolsAvailable) {
            reason = "tools_unavailable";
            return false;
        }

        if (priorToolCalls > 0) {
            reason = "prior_tool_calls_present";
            return false;
        }

        if (assistantDraftToolCalls > 0) {
            reason = "assistant_draft_has_tool_calls";
            return false;
        }

        var request = (userRequest ?? string.Empty).Trim();
        if (request.Length == 0) {
            reason = "empty_user_request";
            return false;
        }

        var draft = (assistantDraft ?? string.Empty).Trim();
        if (draft.Length == 0 || draft.Length > 2400) {
            if (draft.Length == 0) {
                var requestTokenCount = CountLetterDigitTokens(request, maxTokens: 64);
                if (requestTokenCount >= 4) {
                    reason = "empty_assistant_draft_retry";
                    return true;
                }

                reason = "empty_assistant_draft";
                return false;
            }

            reason = "assistant_draft_too_long";
            return false;
        }

        // Guard against accidental feedback loops where the assistant echoes the correction prompt itself.
        if (draft.Contains(ExecutionCorrectionMarker, StringComparison.OrdinalIgnoreCase)) {
            reason = "already_contains_execution_correction_marker";
            return false;
        }

        // If the user selected an explicit pending action (/act or ordinal selection), we should strongly prefer
        // tool execution over a "talky" draft. This is language-agnostic and works after app restarts.
        if (LooksLikeActionSelectionPayload(request)) {
            reason = "explicit_action_selection_payload";
            return true;
        }

        // If the assistant explicitly told the user to "say/type/etc." a quoted phrase, accept echoing that phrase even when
        // weighted continuation routing wasn't used (for example after a restart or when tool routing kept full tool lists).
        var echoedCallToAction = UserMatchesAssistantCallToAction(request, draft);
        var compactFollowUp = compactFollowUpHint || LooksLikeCompactFollowUp(request);
        var contextualFollowUp = !compactFollowUp && LooksLikeContextualFollowUpForExecutionNudge(request, draft);
        var hasSingleReadOnlyPendingActionEnvelope = HasSingleReadOnlyPendingActionEnvelope(draft);
        var hasExecutionAckReference = LooksLikeExecutionAcknowledgeDraft(draft)
                                       && draft.IndexOf('"') < 0
                                       && draft.IndexOf('\'') < 0
                                       && CountLetterDigitTokens(request, maxTokens: 64) >= 6
                                       && AssistantDraftReferencesUserRequest(request, draft);
        var hasStructuredScopeChoiceDraft = LooksLikeStructuredScopeChoiceDraft(draft)
                                            && CountLetterDigitTokens(request, maxTokens: 64) >= 6;
        var hasLinkedFollowUpQuestionDraft = ContainsQuestionSignal(draft)
                                             && CountLetterDigitTokens(request, maxTokens: 64) >= 6
                                             && AssistantDraftReferencesUserRequest(request, draft);
        if (hasExecutionAckReference) {
            reason = "execution_ack_draft_references_request";
            return true;
        }

        if (LooksLikeExecutionIntentPlaceholderDraft(request, draft)) {
            reason = "execution_intent_placeholder_draft";
            return true;
        }

        if (hasStructuredScopeChoiceDraft) {
            reason = "structured_scope_choice_draft";
            return true;
        }

        if (hasLinkedFollowUpQuestionDraft) {
            reason = "assistant_question_linked_to_follow_up";
            return true;
        }

        if (!usedContinuationSubset && !echoedCallToAction && !contextualFollowUp) {
            if (hasSingleReadOnlyPendingActionEnvelope && !ContainsQuestionSignal(draft)) {
                reason = "single_readonly_pending_action_envelope";
                return true;
            }

            if (compactFollowUpHint && LooksLikeMultilineFollowUpBlockerDraft(draft)) {
                reason = "compact_follow_up_multiline_blocker_draft";
                return true;
            }

            if (compactFollowUpHint && LooksLikeExecutionAcknowledgeDraft(draft)) {
                reason = "compact_follow_up_execution_ack_draft";
                return true;
            }

            reason = "no_continuation_subset_and_no_cta_or_contextual_follow_up";
            return false;
        }

        if (!echoedCallToAction && !compactFollowUp && !contextualFollowUp) {
            reason = "request_not_compact_or_contextual_follow_up";
            return false;
        }

        var asksAnotherQuestion = ContainsQuestionSignal(draft);
        if (asksAnotherQuestion) {
            if (echoedCallToAction || AssistantDraftReferencesUserRequest(request, draft)) {
                reason = "assistant_question_linked_to_follow_up";
                return true;
            }

            reason = "assistant_question_not_linked";
            return false;
        }

        // Language-agnostic "acknowledgement-like" draft: short, no structured output, no numeric evidence.
        var isMultiLine = draft.Contains('\n', StringComparison.Ordinal) || draft.Contains('\r', StringComparison.Ordinal);
        var hasStructuredOutput = isMultiLine
                                  || draft.Contains('|', StringComparison.Ordinal)
                                  || draft.Contains('{', StringComparison.Ordinal)
                                  || draft.Contains('[', StringComparison.Ordinal);
        if (hasStructuredOutput) {
            if (hasSingleReadOnlyPendingActionEnvelope && !asksAnotherQuestion) {
                reason = "structured_draft_single_readonly_pending_action_envelope";
                return true;
            }

            if (contextualFollowUp && LooksLikeMultilineFollowUpBlockerDraft(draft)) {
                reason = "structured_contextual_blocker_draft";
                return true;
            }

            // Multi-line drafts are usually results/explanations; avoid retrying tools based on incidental quoted text
            // inside structured output (for example JSON like `"run now",`). Only allow the nudge when the CTA quote
            // is formatted as an explicit option/bullet on its own line.
            if (echoedCallToAction && UserMatchesAssistantCallToAction(request, draft, onlyBulletContext: true)) {
                reason = "structured_draft_with_explicit_bullet_cta";
                return true;
            }

            reason = "structured_draft_not_eligible";
            return false;
        }

        var hasNumericSignal = false;
        for (var i = 0; i < draft.Length; i++) {
            if (char.IsDigit(draft[i])) {
                hasNumericSignal = true;
                break;
            }
        }

        if (hasNumericSignal || draft.Length > 220) {
            reason = hasNumericSignal ? "assistant_draft_has_numeric_signal" : "assistant_draft_too_long_for_nudge";
            return false;
        }

        if (compactFollowUpHint && LooksLikeExecutionAcknowledgeDraft(draft)) {
            reason = "compact_follow_up_execution_ack_draft";
            return true;
        }

        // Avoid overriding already-good short completions (for example "You're welcome.").
        // Only retry tool execution when the assistant draft still appears tied to the user's follow-up.
        if (echoedCallToAction || AssistantDraftReferencesUserRequest(request, draft)) {
            reason = echoedCallToAction ? "cta_echo_linked_to_follow_up" : "assistant_draft_references_follow_up";
            return true;
        }

        reason = "assistant_draft_not_linked_to_follow_up";
        return false;
    }

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

        if (FlexibleBooleanTrueTokens.Contains(normalized)) {
            parsed = true;
            return true;
        }

        if (FlexibleBooleanFalseTokens.Contains(normalized)) {
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

    private static bool TryExtractActionSelectionPayloadJson(string text, out string payload) {
        payload = string.Empty;
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        if (normalized[0] == '{') {
            payload = normalized;
            return true;
        }

        if (!normalized.StartsWith("```", StringComparison.Ordinal) || !normalized.EndsWith("```", StringComparison.Ordinal)) {
            return false;
        }

        var firstNewline = normalized.IndexOf('\n');
        if (firstNewline < 0 || firstNewline + 1 >= normalized.Length) {
            return false;
        }

        var closingFenceStart = normalized.LastIndexOf("```", StringComparison.Ordinal);
        if (closingFenceStart <= firstNewline) {
            return false;
        }

        var extracted = normalized[(firstNewline + 1)..closingFenceStart].Trim();
        if (extracted.Length == 0 || extracted[0] != '{') {
            return false;
        }

        payload = extracted;
        return true;
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

    private static bool TryReadSelectionBoolean(JsonElement element, string propertyName, out bool value) {
        value = false;
        if (!TryGetObjectPropertyCaseInsensitive(element, out var node, propertyName)) {
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
            case JsonValueKind.String: {
                    var text = (node.GetString() ?? string.Empty).Trim();
                    return TryParseProtocolBoolean(text, out value);
                }
            default:
                return false;
        }
    }

    private static bool TryGetObjectPropertyCaseInsensitive(JsonElement element, out JsonElement value, params string[] names) {
        value = default;
        if (element.ValueKind != JsonValueKind.Object || names is null || names.Length == 0) {
            return false;
        }

        for (var i = 0; i < names.Length; i++) {
            var name = (names[i] ?? string.Empty).Trim();
            if (name.Length == 0) {
                continue;
            }

            foreach (var property in element.EnumerateObject()) {
                if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) {
                    continue;
                }

                value = property.Value;
                return true;
            }
        }

        return false;
    }

    private static bool TryParseProtocolBoolean(string value, out bool parsed) {
        parsed = false;
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        if (string.Equals(normalized, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "1", StringComparison.Ordinal)) {
            parsed = true;
            return true;
        }

        if (string.Equals(normalized, "false", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "0", StringComparison.Ordinal)) {
            parsed = false;
            return true;
        }

        return false;
    }

    private static bool UserMatchesAssistantCallToAction(string userRequest, string assistantDraft, bool onlyBulletContext = false) {
        var request = NormalizeCompactText(userRequest);
        if (request.Length == 0 || request.Length > 120) {
            return false;
        }

        var phrases = ExtractQuotedPhrases(assistantDraft);
        if (phrases.Count == 0) {
            return false;
        }

        for (var i = 0; i < phrases.Count; i++) {
            var phrase = phrases[i];
            if (!LooksLikeCallToActionContext(assistantDraft, phrase, onlyBulletContext)) {
                continue;
            }

            var normalizedPhrase = NormalizeCompactText(phrase.Value);
            if (normalizedPhrase.Length == 0 || normalizedPhrase.Length > 96) {
                continue;
            }

            // Strong signal: exact echo.
            if (string.Equals(request, normalizedPhrase, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }

            // Common pattern: "yes - <phrase>" or "<phrase>?".
            if (ContainsPhraseWithBoundaries(request, normalizedPhrase)) {
                return true;
            }
        }

        return false;
    }

    // Keep this language-agnostic: treat a quote as a "say/type this exact phrase" CTA only when local punctuation
    // makes it look like an instruction snippet, not an incidental quoted error message.
    private static bool LooksLikeCallToActionContext(string assistantDraft, QuotedPhrase phrase, bool onlyBulletContext) {
        if (string.IsNullOrEmpty(assistantDraft)) {
            return false;
        }

        var openIndex = phrase.OpenIndex;
        var closeIndexExclusive = phrase.CloseIndexExclusive;
        if (openIndex < 0 || closeIndexExclusive <= openIndex + 1 || closeIndexExclusive > assistantDraft.Length) {
            return false;
        }

        var closeQuoteIndex = closeIndexExclusive - 1;

        if (!onlyBulletContext) {
            // Common CTA pattern: "... \"<token>\", I'll execute ..."
            var after = closeIndexExclusive;
            if (after < assistantDraft.Length) {
                // Allow tiny whitespace, then comma.
                var scan = after;
                var consumedSpace = 0;
                while (scan < assistantDraft.Length && consumedSpace < 3 && char.IsWhiteSpace(assistantDraft[scan])) {
                    scan++;
                    consumedSpace++;
                }
                if (scan < assistantDraft.Length && IsCallToActionComma(assistantDraft[scan])) {
                    return true;
                }
            }
        }

        // Bullet-like CTA: "- \"<token>\"" or "1. \"<token>\"" on its own line.
        var lineStart = 0;
        for (var i = openIndex - 1; i >= 0; i--) {
            var ch = assistantDraft[i];
            if (ch == '\n' || ch == '\r') {
                lineStart = i + 1;
                break;
            }
        }

        var lineEnd = assistantDraft.Length;
        for (var i = closeQuoteIndex + 1; i < assistantDraft.Length; i++) {
            var ch = assistantDraft[i];
            if (ch == '\n' || ch == '\r') {
                lineEnd = i;
                break;
            }
        }

        // Scan trimmed prefix without allocating (no Substring/Trim).
        var left = lineStart;
        var right = openIndex - 1;
        while (left <= right && char.IsWhiteSpace(assistantDraft[left])) {
            left++;
        }
        while (right >= left && char.IsWhiteSpace(assistantDraft[right])) {
            right--;
        }
        if (left > right) {
            // Quote is the only meaningful content on its line (explicit instruction snippet).
            var suffixLeft = closeIndexExclusive;
            if (suffixLeft >= assistantDraft.Length) {
                // Only accept quote-only lines when preceded by an explicit label line (for example "To proceed:").
                return PreviousNonEmptyLineEndsWithColon(assistantDraft, lineStart);
            }

            var suffixRight = lineEnd - 1;
            if (suffixRight >= assistantDraft.Length) {
                suffixRight = assistantDraft.Length - 1;
            }
            while (suffixLeft <= suffixRight && char.IsWhiteSpace(assistantDraft[suffixLeft])) {
                suffixLeft++;
            }
            while (suffixRight >= suffixLeft && char.IsWhiteSpace(assistantDraft[suffixRight])) {
                suffixRight--;
            }

            if (suffixLeft > suffixRight) {
                // Avoid treating incidental quoted log/error lines as CTAs unless the assistant explicitly introduced them.
                return PreviousNonEmptyLineEndsWithColon(assistantDraft, lineStart);
            }

            return false;
        }

        // "-", "*", "•"
        if (right == left) {
            var bullet = assistantDraft[left];
            if (bullet == '-' || bullet == '*' || bullet == '•') {
                return true;
            }
        }

        // "1." / "1)" / "1:" (accept common markers without requiring '.')
        var marker = assistantDraft[right];
        if (marker == '.' || marker == ')' || marker == ':') {
            // Multi-digit markers ("12)") are common; accept any non-empty run of digits before the marker.
            var digitCount = 0;
            for (var i = left; i < right; i++) {
                if (!char.IsDigit(assistantDraft[i])) {
                    digitCount = 0;
                    break;
                }
                digitCount++;
            }
            if (digitCount > 0) {
                return true;
            }
        }

        return false;
    }

    private static bool PreviousNonEmptyLineEndsWithColon(string text, int currentLineStart) {
        if (string.IsNullOrEmpty(text) || currentLineStart <= 0) {
            return false;
        }

        // Walk backwards over line breaks and empty lines until we find the previous non-empty line.
        var i = currentLineStart - 1;
        while (i >= 0 && (text[i] == '\n' || text[i] == '\r')) {
            i--;
        }

        while (i >= 0) {
            var lineEnd = i;
            var lineStart = i;
            while (lineStart >= 0 && text[lineStart] != '\n' && text[lineStart] != '\r') {
                lineStart--;
            }
            var start = lineStart + 1;

            while (start <= lineEnd && char.IsWhiteSpace(text[start])) {
                start++;
            }
            while (lineEnd >= start && char.IsWhiteSpace(text[lineEnd])) {
                lineEnd--;
            }

            if (start <= lineEnd) {
                return IsCallToActionColon(text[lineEnd]);
            }

            // Empty line; move to previous.
            i = lineStart - 1;
            while (i >= 0 && (text[i] == '\n' || text[i] == '\r')) {
                i--;
            }
        }

        return false;
    }

    private static bool IsCallToActionComma(char value) {
        for (var i = 0; i < CallToActionCommaPunctuation.Length; i++) {
            if (CallToActionCommaPunctuation[i] == value) {
                return true;
            }
        }

        return false;
    }

    private static bool IsCallToActionColon(char value) {
        for (var i = 0; i < CallToActionColonPunctuation.Length; i++) {
            if (CallToActionColonPunctuation[i] == value) {
                return true;
            }
        }

        return false;
    }

    private readonly record struct QuotedPhrase(int OpenIndex, int CloseIndexExclusive, string Value);

    private static List<QuotedPhrase> ExtractQuotedPhrases(string text) {
        var value = text ?? string.Empty;
        if (value.Length == 0) {
            return new List<QuotedPhrase>();
        }

        var phrases = new List<QuotedPhrase>();
        for (var i = 0; i < value.Length; i++) {
            var openQuote = value[i];
            if (!TryGetQuotePair(openQuote, out var closeQuote, out var apostropheLike)) {
                continue;
            }

            // Treat apostrophes inside words as apostrophes, not as quoting. This avoids accidentally pairing "don't"
            // with a later single-quote and extracting a huge bogus "phrase".
            if (apostropheLike
                && i > 0
                && i + 1 < value.Length
                && char.IsLetterOrDigit(value[i - 1])
                && char.IsLetterOrDigit(value[i + 1])) {
                continue;
            }

            // Find a closing quote without scanning unboundedly far (prevents large accidental spans and reduces allocations).
            var maxEnd = Math.Min(value.Length - 1, i + 1 + MaxQuotedPhraseSpan);
            var end = -1;
            for (var j = i + 1; j <= maxEnd; j++) {
                var ch = value[j];
                if (ch == '\n' || ch == '\r') {
                    break;
                }
                if (ch == closeQuote) {
                    end = j;
                    break;
                }
            }

            if (end <= i + 1) {
                continue;
            }

            var inner = value.Substring(i + 1, end - i - 1).Trim();
            var openIndex = i;
            i = end;
            if (inner.Length == 0 || inner.Length > 96) {
                continue;
            }

            if (inner.Contains('\n', StringComparison.Ordinal)) {
                continue;
            }

            // Keep it lean: only short, "say this" kind of phrases (avoid quoting entire paragraphs).
            var tokens = CountLetterDigitTokens(inner, maxTokens: 12);
            if (tokens == 0 || tokens > 8) {
                continue;
            }

            phrases.Add(new QuotedPhrase(openIndex, end + 1, inner));
            if (phrases.Count >= 6) {
                break;
            }
        }

        return phrases;
    }

}
