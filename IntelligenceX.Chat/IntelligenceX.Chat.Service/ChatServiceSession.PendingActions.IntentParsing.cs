using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private static List<string> ExtractPendingActionIntentTokens(string text, int maxTokens) {
        var normalized = NormalizeCompactText(text);
        if (normalized.Length == 0 || maxTokens <= 0) {
            return new List<string>();
        }

        var tokens = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var token = new StringBuilder();

        for (var i = 0; i <= normalized.Length; i++) {
            var ch = i < normalized.Length ? normalized[i] : '\0';
            var isTokenChar = i < normalized.Length && char.IsLetterOrDigit(ch);
            if (isTokenChar) {
                token.Append(char.ToLowerInvariant(ch));
                continue;
            }

            if (token.Length == 0) {
                continue;
            }

            var value = token.ToString();
            token.Clear();

            if (!LooksLikePendingActionIntentToken(value)) {
                continue;
            }

            if (!seen.Add(value)) {
                continue;
            }

            tokens.Add(value);
            if (tokens.Count >= maxTokens) {
                break;
            }
        }

        return tokens;
    }

    private static bool LooksLikePendingActionIntentToken(string token) {
        var value = (token ?? string.Empty).Trim();
        if (value.Length == 0) {
            return false;
        }

        var hasDigit = false;
        var hasLetter = false;
        var hasNonAscii = false;
        for (var i = 0; i < value.Length; i++) {
            var ch = value[i];
            if (!char.IsLetterOrDigit(ch)) {
                return false;
            }
            if (char.IsDigit(ch)) {
                hasDigit = true;
            }
            if (char.IsLetter(ch)) {
                hasLetter = true;
            }
            // Heuristic only: this is not full script detection; it keeps short non-Latin intent tokens eligible.
            if (ch > 127) {
                hasNonAscii = true;
            }
        }

        if (hasDigit) {
            return true;
        }

        if (!hasLetter) {
            return false;
        }

        var minLength = hasNonAscii ? 2 : 3;
        return value.Length >= minLength;
    }

    private static bool TokenOverlapsPendingActionIntent(string token, IReadOnlyList<string> actionTokens) {
        var value = (token ?? string.Empty).Trim();
        if (value.Length == 0 || actionTokens.Count == 0) {
            return false;
        }

        for (var i = 0; i < actionTokens.Count; i++) {
            var actionToken = (actionTokens[i] ?? string.Empty).Trim();
            if (actionToken.Length == 0) {
                continue;
            }

            if (string.Equals(value, actionToken, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }

            var minSharedLength = Math.Min(value.Length, actionToken.Length);
            if (minSharedLength < 5) {
                continue;
            }

            if (value.StartsWith(actionToken, StringComparison.OrdinalIgnoreCase)
                || actionToken.StartsWith(value, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

    private static string ReadFirstToken(string text) {
        var value = (text ?? string.Empty).Trim();
        if (value.Length == 0) {
            return string.Empty;
        }
        var end = 0;
        while (end < value.Length && !char.IsWhiteSpace(value[end])) {
            end++;
        }
        return end <= 0 ? string.Empty : value.Substring(0, end).Trim();
    }

    private static bool TryParseExplicitActSelection(string userText, out string actionId, out string reason) {
        actionId = string.Empty;
        reason = "not_explicit_act";
        var trimmed = (userText ?? string.Empty).Trim();
        if (trimmed.Length == 0) {
            return false;
        }

        var candidate = TrimExplicitActCommandWrappers(trimmed);
        if (!candidate.StartsWith("/act", StringComparison.OrdinalIgnoreCase)) {
            return false;
        }

        // Require `/act` as a standalone token; avoid accidentally treating `/actuator` etc. as an action selection.
        if (candidate.Length > 4 && !char.IsWhiteSpace(candidate[4])) {
            reason = "invalid_act_command_token";
            return true;
        }

        var rest = candidate[4..].Trim();
        if (rest.Length == 0) {
            reason = "act_id_missing";
            return true;
        }

        var token = ReadFirstToken(rest);
        if (token.Length == 0) {
            reason = "act_id_missing";
            return true;
        }

        var normalizedId = NormalizeExplicitActSelectionIdToken(token);
        if (normalizedId.Length == 0) {
            reason = "act_id_invalid";
            return true;
        }

        var trailing = rest[token.Length..].Trim();
        if (trailing.Length > 0 && !AllCharsAllowedInExplicitActTrailingSuffix(trailing)) {
            reason = "act_command_has_trailing_tokens";
            return true;
        }

        actionId = normalizedId;
        reason = "explicit_act_id";
        return true;
    }

    private static string TrimExplicitActCommandWrappers(string text) {
        var value = (text ?? string.Empty).Trim();
        if (value.Length < 2) {
            return value;
        }

        for (var depth = 0; depth < 2; depth++) {
            if (value.Length < 2) {
                break;
            }

            if (!TryGetExplicitActWrapperEnd(value[0], out var expectedClose) || value[^1] != expectedClose) {
                break;
            }

            value = value.Substring(1, value.Length - 2).Trim();
        }

        return value;
    }

    private static bool TryGetExplicitActWrapperEnd(char open, out char close) {
        close = '\0';
        switch (open) {
            case '"':
                close = '"';
                return true;
            case '\'':
                close = '\'';
                return true;
            case '`':
                close = '`';
                return true;
            case '(':
                close = ')';
                return true;
            case '[':
                close = ']';
                return true;
            case '\u201C': // left double smart quote
                close = '\u201D'; // right double smart quote
                return true;
            case '\u2018': // left single smart quote
                close = '\u2019'; // right single smart quote
                return true;
            case '\uFF02': // fullwidth double quote
                close = '\uFF02';
                return true;
            case '\uFF07': // fullwidth apostrophe
                close = '\uFF07';
                return true;
            default:
                return false;
        }
    }

    private static string NormalizeExplicitActSelectionIdToken(string token) {
        var value = (token ?? string.Empty).Trim();
        if (value.Length == 0) {
            return string.Empty;
        }

        value = value.TrimStart('(', '[', '"', '\'', '`', '\u201C', '\u2018', '\uFF02', '\uFF07');
        value = value.TrimEnd(')', ']', '"', '\'', '`', '.', ',', ';', ':', '!', '?', '\u201D', '\u2019', '\uFF02', '\uFF07');
        if (value.Length == 0 || value.Length > 64) {
            return string.Empty;
        }

        for (var i = 0; i < value.Length; i++) {
            var ch = value[i];
            if (char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.') {
                continue;
            }

            return string.Empty;
        }

        return value;
    }

    private static bool AllCharsAllowedInExplicitActTrailingSuffix(string text) {
        for (var i = 0; i < text.Length; i++) {
            var ch = text[i];
            if (char.IsWhiteSpace(ch)) {
                continue;
            }
            if (ch is ')' or ']' or '"' or '\'' or '`' or ',' or '.' or ';' or ':' or '!' or '?') {
                continue;
            }
            if (ch is '\u201D' or '\u2019' or '\uFF02' or '\uFF07') {
                continue;
            }

            return false;
        }

        return true;
    }

    private static bool TryParseOrdinalSelection(string text, out int value) {
        value = 0;
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        var i = 0;
        while (i < normalized.Length && char.IsDigit(normalized[i])) {
            i++;
        }
        if (i == 0) {
            return false;
        }

        var digits = normalized.Substring(0, i);
        if (!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out value)) {
            return false;
        }

        var rest = normalized[i..].Trim();
        if (rest.Length == 0) {
            return true;
        }

        // Allow simple punctuation variants like "2." or "2)".
        return rest is "." or ")" or "]" or ":";
    }

}
