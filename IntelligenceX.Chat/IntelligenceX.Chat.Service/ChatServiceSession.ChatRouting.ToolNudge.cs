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
    private const string ContinuationContractMarker = "ix:continuation:v1";
    private const string StructuredNextActionRetryMarker = "ix:structured-next-action-retry:v1";
    private const string ToolProgressRecoveryMarker = "ix:tool-progress-recovery:v1";
    private const int MaxContinuationContractScanChars = 4096;
    private const int MaxContinuationContractFieldChars = 600;
    private const int MaxStructuredNextActionArgumentsChars = 32_768;
    private const int NoResultPhaseLoopThresholdWithToolActivity = 8;
    private const int NoResultPhaseLoopThresholdWithoutToolActivity = 6;
    private const int FollowUpShapeTokenScanLimit = 16;
    private const int FollowUpShapeShortTokenLimit = 6;
    private const int FollowUpShapeShortCharLimit = 64;
    private const int FollowUpQuestionMaxTokens = 12;
    private const int CompactFollowUpQuestionCharLimit = 80;
    private const int ContinuationFollowUpQuestionCharLimit = 96;
    private static readonly char[] CallToActionCommaPunctuation = new[] { ',', '\uFF0C', '\u3001', '\u060C' };
    private static readonly char[] CallToActionColonPunctuation = new[] { ':', '\uFF1A', '\uFE13' };
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
        var normalized = (userRequest ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        if (TryReadContinuationFocusLiveExecutionRequirementFromWorkingMemoryPrompt(
                normalized,
                out var requiresLiveExecution,
                out _,
                out _,
                out _)
            && requiresLiveExecution) {
            return true;
        }

        return TryReadActionSelectionIntent(
                   text: normalized,
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
        bool explicitToolQuestionTurn,
        bool toolActivityDetected,
        TurnAnswerPlan answerPlan,
        string assistantDraft) {
        if (toolActivityDetected) {
            return false;
        }

        // Explicit tool-capability questions should remain conversational even when an earlier
        // recovery path was active in this turn.
        if (explicitToolQuestionTurn) {
            return false;
        }

        // Follow-up question turns should not be rewritten into blocker/cached-evidence output.
        // Keep the model's direct answer path so tool-capability questions remain conversational.
        if ((continuationFollowUpTurn || compactFollowUpTurn)
            && ContainsQuestionSignal(userRequest)) {
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

        if ((continuationFollowUpTurn || compactFollowUpTurn)
            && answerPlan.HasPlan
            && answerPlan.CarryForwardUnresolvedFocus) {
            return true;
        }

        if (continuationFollowUpTurn || compactFollowUpTurn) {
            if (ContainsQuestionSignal(draft)) {
                return false;
            }

            return LooksLikeExecutionAcknowledgeDraft(draft);
        }

        return true;
    }

    private static bool LooksLikeExplicitToolQuestionTurn(string userRequest) {
        var request = (userRequest ?? string.Empty).Trim();
        if (request.Length == 0) {
            return false;
        }

        var requestedToolNames = ExtractExplicitRequestedToolNames(request);
        if (requestedToolNames.Length == 0) {
            return false;
        }

        if (ContainsQuestionSignal(request)) {
            return true;
        }

        return LooksLikeQuotedToolDescriptorReference(request);
    }

    private static bool LooksLikeQuotedToolDescriptorReference(string request) {
        if (string.IsNullOrWhiteSpace(request) || request.IndexOf('`') < 0) {
            return false;
        }

        if (request.IndexOf('\n') >= 0 || request.IndexOf('\r') >= 0) {
            return true;
        }

        if (request.IndexOf('·') >= 0 || request.IndexOf('•') >= 0 || request.IndexOf('・') >= 0) {
            return true;
        }

        var openParenIndex = request.IndexOf('(');
        var closeParenIndex = request.IndexOf(')');
        return openParenIndex >= 0 && closeParenIndex > openParenIndex;
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

        if (!executionContractApplies && IsArtifactOnlyFollowUpRequest(userRequest)) {
            reason = "artifact_only_follow_up_request";
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

        if (IsArtifactOnlyFollowUpRequest(request)) {
            reason = "artifact_only_follow_up_request";
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
        var draftReferencesFollowUp = AssistantDraftReferencesUserRequest(request, draft);
        var hasSinglePendingActionEnvelope = TryGetSinglePendingActionEnvelopeMutability(draft, out var singlePendingActionMutability);
        var hasSingleNonMutatingPendingActionEnvelope = hasSinglePendingActionEnvelope
                                                        && singlePendingActionMutability != ActionMutability.Mutating;
        var hasExecutionAckReference = LooksLikeExecutionAcknowledgeDraft(draft)
                                       && draft.IndexOf('"') < 0
                                       && draft.IndexOf('\'') < 0
                                       && CountLetterDigitTokens(request, maxTokens: 64) >= 6
                                       && draftReferencesFollowUp;
        var hasStructuredScopeChoiceDraft = LooksLikeStructuredScopeChoiceDraft(draft)
                                            && CountLetterDigitTokens(request, maxTokens: 64) >= 6;
        var hasLinkedFollowUpQuestionDraft = ContainsQuestionSignal(draft)
                                             && CountLetterDigitTokens(request, maxTokens: 64) >= 6
                                             && draftReferencesFollowUp;
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
            if (hasSingleNonMutatingPendingActionEnvelope && !ContainsQuestionSignal(draft)) {
                reason = singlePendingActionMutability == ActionMutability.Unknown
                    ? "single_unknown_pending_action_envelope"
                    : "single_readonly_pending_action_envelope";
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

            if (compactFollowUpHint
                && !ContainsQuestionSignal(draft)
                && draftReferencesFollowUp
                && LooksLikeStructuredExecutionDeferredDraft(draft)) {
                reason = "compact_follow_up_structured_execution_deferred_draft";
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
            if (hasSingleNonMutatingPendingActionEnvelope && !asksAnotherQuestion) {
                reason = singlePendingActionMutability == ActionMutability.Unknown
                    ? "structured_draft_single_unknown_pending_action_envelope"
                    : "structured_draft_single_readonly_pending_action_envelope";
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

            // Continuation turns can include concise promise/planning drafts in list form.
            // If the draft still anchors to the follow-up context and does not ask another
            // question, run one corrective nudge so execution happens in the same turn.
            if (!hasSinglePendingActionEnvelope && contextualFollowUp && draftReferencesFollowUp && !asksAnotherQuestion) {
                reason = "structured_draft_contextual_follow_up";
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
        if (echoedCallToAction || draftReferencesFollowUp) {
            reason = echoedCallToAction ? "cta_echo_linked_to_follow_up" : "assistant_draft_references_follow_up";
            return true;
        }

        reason = "assistant_draft_not_linked_to_follow_up";
        return false;
    }

    private static bool IsArtifactOnlyFollowUpRequest(string userRequest) {
        var request = (userRequest ?? string.Empty).Trim();
        if (request.Length == 0) {
            return false;
        }

        if (ExtractExplicitRequestedToolNames(request).Length > 0 || ShouldEnforceExecuteOrExplainContract(request)) {
            return false;
        }

        return ResolveRequestedArtifactIntent(request).RequiresArtifact;
    }

    private static bool LooksLikeStructuredExecutionDeferredDraft(string assistantDraft) {
        var draft = (assistantDraft ?? string.Empty).Trim();
        if (draft.Length < 120 || draft.Length > 3200) {
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
        var headingLikeCount = 0;
        var markdownFenceCount = 0;
        for (var i = 0; i < lines.Count && nonEmptyCount < 40; i++) {
            var trimmed = (lines[i] ?? string.Empty).Trim();
            if (trimmed.Length == 0) {
                continue;
            }

            nonEmptyCount++;
            if (IsBulletLikeLine(trimmed)) {
                bulletLikeCount++;
            }

            if (trimmed.StartsWith("#", StringComparison.Ordinal)
                || (trimmed.EndsWith(":", StringComparison.Ordinal) && CountLetterDigitTokens(trimmed, maxTokens: 24) >= 2)) {
                headingLikeCount++;
            }

            if (trimmed.StartsWith("```", StringComparison.Ordinal)) {
                markdownFenceCount++;
            }
        }

        if (markdownFenceCount >= 2) {
            return false;
        }

        return nonEmptyCount >= 6
               && bulletLikeCount >= 3
               && headingLikeCount >= 1;
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

}
