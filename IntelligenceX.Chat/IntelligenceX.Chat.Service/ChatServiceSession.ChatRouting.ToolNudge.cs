using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

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
            out _);
    }

    private static bool ShouldEnforceExecuteOrExplainContract(string userRequest) {
        return TryReadActionSelectionIntent(
                   text: (userRequest ?? string.Empty).Trim(),
                   actionId: out _,
                   mutability: out var mutability)
               && mutability == ActionMutability.Mutating;
    }

    private static bool ShouldAttemptNoToolExecutionWatchdog(
        string userRequest,
        string assistantDraft,
        bool toolsAvailable,
        int priorToolCalls,
        int priorToolOutputs,
        int assistantDraftToolCalls,
        bool executionNudgeUsed,
        bool toolReceiptCorrectionUsed,
        bool watchdogAlreadyUsed,
        out string reason) {
        reason = "not_eligible";

        if (watchdogAlreadyUsed) {
            reason = "watchdog_already_used";
            return false;
        }

        if (!ShouldEnforceExecuteOrExplainContract(userRequest)) {
            reason = "execution_contract_not_applicable";
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
            reason = "empty_assistant_draft";
            return false;
        }

        // Avoid correction/watchdog feedback loops if a previous retry prompt is echoed back into the draft.
        if (draft.Contains(ExecutionWatchdogMarker, StringComparison.OrdinalIgnoreCase)
            || draft.Contains(ExecutionContractMarker, StringComparison.OrdinalIgnoreCase)) {
            reason = "watchdog_or_contract_marker_present";
            return false;
        }

        reason = (!executionNudgeUsed && !toolReceiptCorrectionUsed)
            ? "strict_contract_watchdog_retry_no_prior_recovery"
            : "strict_contract_watchdog_retry";
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
            reason = draft.Length == 0 ? "empty_assistant_draft" : "assistant_draft_too_long";
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
        var compactFollowUp = LooksLikeCompactFollowUp(request);
        var contextualFollowUp = !compactFollowUp && LooksLikeContextualFollowUpForExecutionNudge(request, draft);
        var draftReferencesFollowUp = AssistantDraftReferencesUserRequest(request, draft);
        var hasSinglePendingActionEnvelope = TryGetSinglePendingActionEnvelopeMutability(draft, out var singlePendingActionMutability);
        var hasSingleNonMutatingPendingActionEnvelope = hasSinglePendingActionEnvelope
                                                        && singlePendingActionMutability != ActionMutability.Mutating;
        if (!usedContinuationSubset && !echoedCallToAction && !contextualFollowUp) {
            if (hasSingleNonMutatingPendingActionEnvelope && !ContainsQuestionSignal(draft)) {
                reason = singlePendingActionMutability == ActionMutability.Unknown
                    ? "single_unknown_pending_action_envelope"
                    : "single_readonly_pending_action_envelope";
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

        // Avoid overriding already-good short completions (for example "You're welcome.").
        // Only retry tool execution when the assistant draft still appears tied to the user's follow-up.
        if (echoedCallToAction || draftReferencesFollowUp) {
            reason = echoedCallToAction ? "cta_echo_linked_to_follow_up" : "assistant_draft_references_follow_up";
            return true;
        }

        reason = "assistant_draft_not_linked_to_follow_up";
        return false;
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

    private static bool TryReadSelectionBoolean(JsonElement element, string propertyName, out bool value) {
        value = false;
        if (!element.TryGetProperty(propertyName, out var node)) {
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
            // Most common CTA pattern: "... \"run now\", I'll execute ..."
            var after = closeIndexExclusive;
            if (after < assistantDraft.Length) {
                // Allow tiny whitespace, then comma.
                var scan = after;
                var consumedSpace = 0;
                while (scan < assistantDraft.Length && consumedSpace < 3 && char.IsWhiteSpace(assistantDraft[scan])) {
                    scan++;
                    consumedSpace++;
                }
                if (scan < assistantDraft.Length && assistantDraft[scan] == ',') {
                    return true;
                }
            }
        }

        // Bullet-like CTA: "- \"run now\"" or "1. \"run now\"" on its own line.
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
                return text[lineEnd] == ':';
            }

            // Empty line; move to previous.
            i = lineStart - 1;
            while (i >= 0 && (text[i] == '\n' || text[i] == '\r')) {
                i--;
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
