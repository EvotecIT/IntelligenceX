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
