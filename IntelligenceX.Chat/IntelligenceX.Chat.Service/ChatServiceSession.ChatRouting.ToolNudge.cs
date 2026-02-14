using System;
using System.Collections.Generic;
using System.Text;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {

    private const int MaxQuotedPhraseSpan = 140;

    // Narrow, safety-oriented hints: this is not a "phrase list" of confirmations. It's a guard to ensure we only
    // treat quoted phrases as call-to-action targets when the assistant explicitly instructs the user to say/type/etc.
    private static readonly string[] ToolNudgeCallToActionHints = new[] {
        "say",
        "type",
        "reply",
        "respond",
        "send",
        "enter",
        "paste",
        "write"
    };

    private static bool ShouldAttemptToolExecutionNudge(string userRequest, string assistantDraft, bool toolsAvailable, int priorToolCalls,
        bool usedContinuationSubset) {
        if (!toolsAvailable || priorToolCalls > 0) {
            return false;
        }

        var request = (userRequest ?? string.Empty).Trim();
        if (request.Length == 0) {
            return false;
        }

        var draft = (assistantDraft ?? string.Empty).Trim();
        if (draft.Length == 0 || draft.Length > 2400) {
            return false;
        }

        // If the assistant explicitly told the user to "say/type/etc." a quoted phrase, accept echoing that phrase even when
        // weighted continuation routing wasn't used (for example after a restart or when tool routing kept full tool lists).
        var echoedCallToAction = UserMatchesAssistantCallToAction(request, draft);
        if (!usedContinuationSubset && !echoedCallToAction) {
            return false;
        }

        if (!echoedCallToAction && !LooksLikeCompactFollowUp(request)) {
            return false;
        }

        var asksAnotherQuestion = draft.Contains('?', StringComparison.Ordinal);
        if (asksAnotherQuestion) {
            return echoedCallToAction || AssistantDraftReferencesUserRequest(request, draft);
        }

        // Language-agnostic "acknowledgement-like" draft: short, no structured output, no numeric evidence.
        var hasStructuredOutput = draft.Contains('\n', StringComparison.Ordinal)
                                  || draft.Contains('|', StringComparison.Ordinal)
                                  || draft.Contains('{', StringComparison.Ordinal)
                                  || draft.Contains('[', StringComparison.Ordinal);
        if (hasStructuredOutput) {
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
            return false;
        }

        // Avoid overriding already-good short completions (for example "You're welcome.").
        // Only retry tool execution when the assistant draft still appears tied to the user's follow-up.
        return echoedCallToAction || AssistantDraftReferencesUserRequest(request, draft);
    }

    private static bool UserMatchesAssistantCallToAction(string userRequest, string assistantDraft) {
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
            if (!LooksLikeCallToActionContext(assistantDraft, phrase.OpenIndex)) {
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

    private static bool LooksLikeCallToActionContext(string assistantDraft, int quoteIndex) {
        if (quoteIndex <= 0) {
            return false;
        }

        // Constrain to the "local" sentence so earlier CTA phrases don't bleed into later incidental quotes.
        var windowStart = Math.Max(0, quoteIndex - 72);
        for (var i = quoteIndex - 1; i >= 0 && (quoteIndex - i) <= 220; i--) {
            var ch = assistantDraft[i];
            if (ch == '.' || ch == '!' || ch == '?' || ch == '\n' || ch == '\r') {
                windowStart = Math.Max(windowStart, i + 1);
                break;
            }
        }

        var window = assistantDraft.Substring(windowStart, quoteIndex - windowStart);
        for (var i = 0; i < ToolNudgeCallToActionHints.Length; i++) {
            if (ContainsWord(window, ToolNudgeCallToActionHints[i])) {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsWord(string text, string word) {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(word) || word.Length > text.Length) {
            return false;
        }

        var startIndex = 0;
        while (true) {
            var idx = text.IndexOf(word, startIndex, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) {
                return false;
            }

            var beforeOk = idx == 0 || !char.IsLetterOrDigit(text[idx - 1]);
            var afterIndex = idx + word.Length;
            var afterOk = afterIndex >= text.Length || !char.IsLetterOrDigit(text[afterIndex]);
            if (beforeOk && afterOk) {
                return true;
            }

            startIndex = idx + 1;
            if (startIndex >= text.Length) {
                return false;
            }
        }
    }

    private readonly record struct QuotedPhrase(int OpenIndex, int CloseIndex, string Value);

    private static List<QuotedPhrase> ExtractQuotedPhrases(string text) {
        var value = text ?? string.Empty;
        if (value.Length == 0) {
            return new List<QuotedPhrase>();
        }

        var phrases = new List<QuotedPhrase>();
        for (var i = 0; i < value.Length; i++) {
            var quote = value[i];
            if (quote != '"' && quote != '\'') {
                continue;
            }

            // Treat apostrophes inside words as apostrophes, not as quoting. This avoids accidentally pairing "don't"
            // with a later single-quote and extracting a huge bogus "phrase".
            if (quote == '\'' && i > 0 && i + 1 < value.Length && char.IsLetterOrDigit(value[i - 1]) && char.IsLetterOrDigit(value[i + 1])) {
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
                if (ch == quote) {
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

            phrases.Add(new QuotedPhrase(openIndex, end, inner));
            if (phrases.Count >= 6) {
                break;
            }
        }

        return phrases;
    }

    private static string NormalizeCompactText(string text) {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return string.Empty;
        }

        // Strip inline-code wrappers (`run now`) without trying to parse markdown fully.
        if (normalized.Length >= 2 && normalized[0] == '`' && normalized[^1] == '`') {
            normalized = normalized.Substring(1, normalized.Length - 2).Trim();
        }

        // Trim light punctuation wrappers so "run now?" and "\"run now\"" normalize.
        normalized = normalized.Trim().Trim('"', '\'', '.', '!', '?', ':', ';', ',', '(', ')', '[', ']', '{', '}');
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
}
