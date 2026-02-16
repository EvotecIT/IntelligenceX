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
    private static readonly HashSet<string> MutatingActionTokens = new(StringComparer.OrdinalIgnoreCase) {
        "add",
        "apply",
        "approve",
        "block",
        "clear",
        "create",
        "delete",
        "disable",
        "enable",
        "fix",
        "grant",
        "install",
        "join",
        "kill",
        "lock",
        "modify",
        "move",
        "patch",
        "promote",
        "purge",
        "quarantine",
        "remove",
        "rename",
        "revoke",
        "reset",
        "restart",
        "reboot",
        "rollback",
        "set",
        "shutdown",
        "start",
        "stop",
        "terminate",
        "uninstall",
        "unlock",
        "update",
        "write"
    };
    private static readonly HashSet<string> ReadOnlyActionTokens = new(StringComparer.OrdinalIgnoreCase) {
        "analyze",
        "analyse",
        "audit",
        "check",
        "collect",
        "count",
        "discover",
        "enumerate",
        "explain",
        "fetch",
        "find",
        "get",
        "inspect",
        "list",
        "map",
        "query",
        "read",
        "report",
        "resolve",
        "review",
        "scan",
        "search",
        "show",
        "summarize",
        "summarise",
        "top",
        "trace",
        "verify"
    };
    private static readonly HashSet<string> ActionIntentSkipTokens = new(StringComparer.OrdinalIgnoreCase) {
        "a",
        "an",
        "and",
        "can",
        "could",
        "for",
        "it",
        "just",
        "kindly",
        "let",
        "lets",
        "me",
        "now",
        "please",
        "the",
        "this",
        "to",
        "we",
        "would",
        "you"
    };
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
                   actionIntentText: out var actionIntentText)
               && IsLikelyMutatingActionIntent(actionIntentText);
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
        if (!usedContinuationSubset && !echoedCallToAction) {
            reason = "no_continuation_subset_and_no_cta_echo";
            return false;
        }

        if (!echoedCallToAction && !LooksLikeCompactFollowUp(request)) {
            reason = "request_not_compact_follow_up";
            return false;
        }

        var asksAnotherQuestion = draft.Contains('?', StringComparison.Ordinal);
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

        // Avoid overriding already-good short completions (for example "You're welcome.").
        // Only retry tool execution when the assistant draft still appears tied to the user's follow-up.
        if (echoedCallToAction || AssistantDraftReferencesUserRequest(request, draft)) {
            reason = echoedCallToAction ? "cta_echo_linked_to_follow_up" : "assistant_draft_references_follow_up";
            return true;
        }

        reason = "assistant_draft_not_linked_to_follow_up";
        return false;
    }

    private static bool LooksLikeActionSelectionPayload(string text) {
        return TryReadActionSelectionIntent(
            text: text,
            actionId: out _,
            actionIntentText: out _);
    }

    private static bool TryReadActionSelectionIntent(string text, out string actionId, out string actionIntentText) {
        actionId = string.Empty;
        actionIntentText = string.Empty;

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

            var title = selection.TryGetProperty("title", out var titleNode) && titleNode.ValueKind == JsonValueKind.String
                ? (titleNode.GetString() ?? string.Empty).Trim()
                : string.Empty;
            var request = selection.TryGetProperty("request", out var requestNode) && requestNode.ValueKind == JsonValueKind.String
                ? (requestNode.GetString() ?? string.Empty).Trim()
                : string.Empty;

            actionIntentText = CollapseWhitespace(string.Join(' ', new[] { title, request }).Trim());
            return true;
        } catch (JsonException) {
            return false;
        }
    }

    private static bool IsLikelyMutatingActionIntent(string text) {
        var normalized = CollapseWhitespace((text ?? string.Empty).Trim());
        if (normalized.Length == 0) {
            return false;
        }

        var tokens = TokenizeActionIntent(normalized, maxTokens: 32);
        if (tokens.Count == 0) {
            return false;
        }

        var firstToken = FindFirstActionIntentVerb(tokens);
        // Mutating verbs anywhere in the action intent must win over leading read-only verbs
        // (for example: "check and disable user").
        for (var i = 0; i < tokens.Count; i++) {
            if (MutatingActionTokens.Contains(tokens[i])) {
                return true;
            }
        }

        if (firstToken.Length > 0 && ReadOnlyActionTokens.Contains(firstToken)) {
            return false;
        }

        return false;
    }

    private static string FindFirstActionIntentVerb(IReadOnlyList<string> tokens) {
        for (var i = 0; i < tokens.Count; i++) {
            var token = tokens[i];
            if (ActionIntentSkipTokens.Contains(token)) {
                continue;
            }
            return token;
        }

        return string.Empty;
    }

    private static List<string> TokenizeActionIntent(string text, int maxTokens) {
        var tokens = new List<string>();
        if (string.IsNullOrWhiteSpace(text) || maxTokens <= 0) {
            return tokens;
        }

        Span<char> tokenBuffer = stackalloc char[48];
        var tokenLength = 0;
        for (var i = 0; i < text.Length; i++) {
            var ch = text[i];
            if (char.IsLetterOrDigit(ch) || ch == '_') {
                if (tokenLength < tokenBuffer.Length) {
                    tokenBuffer[tokenLength] = char.ToLowerInvariant(ch);
                    tokenLength++;
                }
                continue;
            }

            if (tokenLength > 0) {
                tokens.Add(new string(tokenBuffer[..tokenLength]));
                tokenLength = 0;
                if (tokens.Count >= maxTokens) {
                    return tokens;
                }
            }
        }

        if (tokenLength > 0 && tokens.Count < maxTokens) {
            tokens.Add(new string(tokenBuffer[..tokenLength]));
        }

        return tokens;
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

    private static bool TryGetQuotePair(char openQuote, out char closeQuote, out bool apostropheLike) {
        apostropheLike = false;
        closeQuote = '\0';
        switch (openQuote) {
            case '"':
                closeQuote = '"';
                return true;
            case '\'':
                closeQuote = '\'';
                apostropheLike = true;
                return true;
            case '\u201C': // “
                closeQuote = '\u201D'; // ”
                return true;
            case '\u2018': // ‘
                closeQuote = '\u2019'; // ’
                apostropheLike = true;
                return true;
            case '\uFF02': // ＂
                closeQuote = '\uFF02';
                return true;
            case '\uFF07': // ＇
                closeQuote = '\uFF07';
                apostropheLike = true;
                return true;
            default:
                return false;
        }
    }

    private static string NormalizeCompactText(string text) {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return string.Empty;
        }

        // Normalize can throw on invalid Unicode (e.g., lone surrogates). This function runs on raw user/assistant input,
        // so it must be exception-safe.
        try {
            normalized = normalized.Normalize(NormalizationForm.FormKC);
        } catch (ArgumentException) {
            // Leave as-is.
        }

        // Strip inline-code wrappers (`run now`) without trying to parse markdown fully.
        if (normalized.Length >= 2 && normalized[0] == '`' && normalized[^1] == '`') {
            normalized = normalized.Substring(1, normalized.Length - 2).Trim();
        }

        // Trim light punctuation wrappers so "run now?" and "\"run now\"" normalize.
        normalized = normalized.Trim().Trim(
            '"', '\'', '.', '!', '?', ',', '(', ')', 
            '\u201C', '\u201D', // “ ”
            '\u2018', '\u2019', // ‘ ’
            '\uFF02', '\uFF07'  // ＂ ＇
        );
        if (normalized.Length == 0) {
            return string.Empty;
        }

        // Collapse whitespace to stabilize matching across minor formatting differences.
        var sb = new StringBuilder(normalized.Length);
        var inSpace = false;
        for (var i = 0; i < normalized.Length; i++) {
            var ch = normalized[i];
            if (char.IsWhiteSpace(ch)) {
                if (!inSpace) {
                    sb.Append(' ');
                    inSpace = true;
                }
                continue;
            }

            inSpace = false;
            sb.Append(ch);
        }

        return sb.ToString().Trim();
    }

    private static bool ContainsPhraseWithBoundaries(string haystack, string needle) {
        if (haystack.Length == 0 || needle.Length == 0 || needle.Length > haystack.Length) {
            return false;
        }

        var startIndex = 0;
        while (true) {
            var idx = haystack.IndexOf(needle, startIndex, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) {
                return false;
            }

            var beforeOk = idx == 0 || !char.IsLetterOrDigit(haystack[idx - 1]);
            var afterIndex = idx + needle.Length;
            var afterOk = afterIndex >= haystack.Length || !char.IsLetterOrDigit(haystack[afterIndex]);
            if (beforeOk && afterOk) {
                return true;
            }

            startIndex = idx + 1;
            if (startIndex >= haystack.Length) {
                return false;
            }
        }
    }

    private static bool LooksLikeCompactFollowUp(string userRequest) {
        var normalized = (userRequest ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        if (normalized.Contains('\n', StringComparison.Ordinal)) {
            return false;
        }

        if (normalized.Length > 80) {
            return false;
        }

        var tokenCount = CountLetterDigitTokens(normalized, maxTokens: 12);
        if (tokenCount == 0) {
            return false;
        }

        if (tokenCount <= 6 && normalized.Length <= 64) {
            return true;
        }

        return tokenCount <= 8 && normalized.Length <= 80 && normalized.Contains('?', StringComparison.Ordinal);
    }

    private static bool AssistantDraftReferencesUserRequest(string userRequest, string assistantDraft) {
        var request = (userRequest ?? string.Empty).Trim();
        var draft = (assistantDraft ?? string.Empty).Trim();
        if (request.Length == 0 || draft.Length == 0) {
            return false;
        }

        // Direct substring match is the strongest signal.
        if (request.Length >= 3 && draft.IndexOf(request, StringComparison.OrdinalIgnoreCase) >= 0) {
            return true;
        }

        // Fall back to token containment (language-agnostic): if any meaningful user token appears in the draft,
        // it is likely the assistant intended to act on that follow-up but failed to call tools.
        var inToken = false;
        var tokenStart = 0;
        var checkedTokens = 0;
        for (var i = 0; i <= request.Length; i++) {
            var ch = i < request.Length ? request[i] : '\0';
            var isTokenChar = i < request.Length && char.IsLetterOrDigit(ch);
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

            var token = request.Substring(tokenStart, i - tokenStart);
            inToken = false;
            if (token.Length == 0) {
                continue;
            }

            var hasNonAscii = false;
            for (var t = 0; t < token.Length; t++) {
                if (token[t] > 127) {
                    hasNonAscii = true;
                    break;
                }
            }

            var minLen = hasNonAscii ? 2 : 3;
            if (token.Length < minLen) {
                continue;
            }

            checkedTokens++;
            if (draft.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0) {
                return true;
            }

            if (checkedTokens >= 12) {
                break;
            }
        }

        return false;
    }

    private static int CountLetterDigitTokens(string text, int maxTokens) {
        var tokenCount = 0;
        var inToken = false;
        for (var i = 0; i < text.Length; i++) {
            var ch = text[i];
            if (char.IsLetterOrDigit(ch)) {
                if (!inToken) {
                    tokenCount++;
                    if (tokenCount >= maxTokens) {
                        return tokenCount;
                    }
                    inToken = true;
                }
            } else {
                inToken = false;
            }
        }

        return tokenCount;
    }

    private static string BuildToolExecutionNudgePrompt(string userRequest, string assistantDraft) {
        var requestText = string.IsNullOrWhiteSpace(userRequest) ? "(empty)" : userRequest.Trim();
        var draftText = string.IsNullOrWhiteSpace(assistantDraft) ? "(empty)" : assistantDraft.Trim();
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
            If tools truly cannot satisfy the request, explain the exact blocker and the minimal missing input.
            """;
    }

    private static string BuildNoToolExecutionWatchdogPrompt(string userRequest, string assistantDraft) {
        var requestText = TrimForPrompt(userRequest, ToolReceiptCorrectionMaxUserRequestChars);
        var draftText = TrimForPrompt(assistantDraft, ToolReceiptCorrectionMaxDraftChars);
        return $$"""
            [Execution watchdog]
            {{ExecutionWatchdogMarker}}
            The previous retries still produced zero tool calls in this turn.

            User request:
            {{requestText}}

            Previous assistant draft:
            {{draftText}}

            If tools can satisfy this request, call them now in this turn.
            If tools cannot satisfy this request, do not imply execution. State the exact blocker and the minimal missing input.
            """;
    }

    private static string BuildExecutionContractBlockerText(string userRequest, string assistantDraft, string reason) {
        var requestText = TrimForPrompt(userRequest, 280);
        var reasonCode = string.IsNullOrWhiteSpace(reason) ? "no_tool_calls_after_retries" : reason.Trim();
        var replayActionBlock = BuildExecutionContractReplayActionBlock(userRequest, assistantDraft);
        return $$"""
            [Execution blocked]
            {{ExecutionContractMarker}}
            I do not have confirmed tool output for this selected action yet.

            Selected action request:
            {{requestText}}

            Reason code: {{reasonCode}}

            Please retry this action. Reply `continue` to retry in this context, or use the action command below.
            {{replayActionBlock}}
            """;
    }

    private static string BuildExecutionContractEscapePrompt(string userRequest, string assistantDraft) {
        var requestText = TrimForPrompt(userRequest, ToolReceiptCorrectionMaxUserRequestChars);
        var draftText = TrimForPrompt(assistantDraft, ToolReceiptCorrectionMaxDraftChars);
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
            - If no tool can satisfy the request, do not claim execution. Explain the exact blocker and the minimal missing input.
            - Keep the response concise and execution-focused.
            """;
    }

    private static string BuildExecutionContractReplayActionBlock(string userRequest, string assistantDraft) {
        if (!TryResolveReplayActionForExecutionContract(userRequest, assistantDraft, out var actionId, out var actionTitle, out var actionRequest)) {
            return string.Empty;
        }

        actionTitle = NormalizeReplayActionText(actionTitle, maxChars: 120);
        actionRequest = NormalizeReplayActionText(actionRequest, maxChars: 220);

        return $$"""

            [Action]
            ix:action:v1
            id: {{actionId}}
            title: {{actionTitle}}
            request: {{actionRequest}}
            reply: /act {{actionId}}
            """;
    }

    private static bool TryResolveReplayActionForExecutionContract(string userRequest, string assistantDraft, out string actionId, out string actionTitle, out string actionRequest) {
        if (TryParseActionSelectionForReplay(userRequest, out actionId, out actionTitle, out actionRequest)) {
            return true;
        }

        return TryParseSinglePendingActionForReplay(assistantDraft, out actionId, out actionTitle, out actionRequest);
    }

    private static bool TryParseSinglePendingActionForReplay(string assistantDraft, out string actionId, out string actionTitle, out string actionRequest) {
        actionId = string.Empty;
        actionTitle = string.Empty;
        actionRequest = string.Empty;

        var actions = ExtractPendingActions(assistantDraft);
        if (actions.Count == 0) {
            actions = ExtractFallbackChoicePendingActions(assistantDraft);
        }
        if (actions.Count != 1) {
            return false;
        }

        var action = actions[0];
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

    private static bool TryParseActionSelectionForReplay(string userRequest, out string actionId, out string actionTitle, out string actionRequest) {
        actionId = string.Empty;
        actionTitle = string.Empty;
        actionRequest = string.Empty;

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
