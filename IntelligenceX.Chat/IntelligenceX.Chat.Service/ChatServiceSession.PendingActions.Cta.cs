using System;
using System.Collections.Generic;
using System.Text;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private static string NormalizeCompactCallToActionToken(string text) {
        // Assistant CTAs often appear in prose with trailing ':' / ';' (including fullwidth variants) that users
        // should not have to repeat, and that we explicitly disqualify for confirmation.
        var token = NormalizeCompactText(text);
        if (token.Length == 0) {
            return string.Empty;
        }

        token = token.TrimEnd(':', ';', '\uFF1A', '\uFF1B');
        return token.Trim();
    }
    private static string[] ExtractPendingActionCallToActionTokens(string assistantContext) {
        var draft = assistantContext ?? string.Empty;
        if (draft.Length == 0) {
            return Array.Empty<string>();
        }

        var phrases = ExtractQuotedPhrases(draft);
        if (phrases.Count == 0) {
            return Array.Empty<string>();
        }

        var tokens = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < phrases.Count; i++) {
            var phrase = phrases[i];
            if (!LooksLikeCallToActionContext(draft, phrase, onlyBulletContext: false)) {
                continue;
            }

            var token = NormalizeCompactCallToActionToken(phrase.Value);
            if (!LooksLikeCompactCallToActionToken(token)) {
                continue;
            }

            if (!seen.Add(token)) {
                continue;
            }

            tokens.Add(token);
            if (tokens.Count >= 6) {
                break;
            }
        }

        return tokens.Count == 0 ? Array.Empty<string>() : tokens.ToArray();
    }

    private static bool LooksLikeCompactCallToActionToken(string token) {
        var value = (token ?? string.Empty).Trim();
        if (value.Length == 0 || value.Length > 96) {
            return false;
        }

        if (value.Contains('\n', StringComparison.Ordinal) || value.Contains('\r', StringComparison.Ordinal)) {
            return false;
        }

        for (var i = 0; i < value.Length; i++) {
            if (char.IsControl(value[i])) {
                return false;
            }
        }

        // Keep it lean: only short, phrase-like tokens.
        var tokens = CountLetterDigitTokens(value, maxTokens: 12);
        return tokens is > 0 and <= 8;
    }

    private static bool UserMatchesPendingActionCallToActionTokens(string userText, IReadOnlyList<string> tokens) {
        if (tokens is null || tokens.Count == 0) {
            return false;
        }

        var raw = (userText ?? string.Empty).Trim();
        if (raw.Length == 0 || raw.Length > 96) {
            return false;
        }

        // Guardrails must run on raw input (pre-normalization) to avoid normalization widening matches.
        if (raw.IndexOfAny(PendingActionConfirmationQuestionPunctuation) >= 0) {
            return false;
        }
        if (raw.IndexOfAny(PendingActionConfirmationDisqualifierPunctuation) >= 0) {
            return false;
        }
        if (LooksLikeStructuredPendingActionConfirmationInput(raw)) {
            return false;
        }

        var request = NormalizeCompactText(raw);
        if (request.Length == 0 || request.Length > 96) {
            return false;
        }

        for (var i = 0; i < tokens.Count; i++) {
            var token = (tokens[i] ?? string.Empty).Trim();
            if (!LooksLikeCompactCallToActionToken(token)) {
                continue;
            }

            if (string.Equals(request, token, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

    private static List<string> SplitLines(string text) {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(text)) {
            return lines;
        }

        var sb = new StringBuilder();
        for (var i = 0; i < text.Length; i++) {
            var ch = text[i];
            if (ch == '\r') {
                continue;
            }
            if (ch == '\n') {
                lines.Add(sb.ToString());
                sb.Clear();
                continue;
            }

            sb.Append(ch);
        }

        lines.Add(sb.ToString());
        return lines;
    }
}
