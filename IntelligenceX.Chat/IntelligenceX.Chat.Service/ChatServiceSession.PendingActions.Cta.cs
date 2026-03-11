using System;
using System.Collections.Generic;
using System.Text;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private const int MaxCompactCallToActionLength = 96;
    private const int MaxCompactCallToActionTokenCount = 8;
    private const int MaxCompactCallToActionTokenScan = 12;
    private const int MaxPendingActionCallToActionTokens = 6;
    private const int MaxInlinePendingActionCallToActionSuffixTokens = 3;
    private const int MinInlinePendingActionCallToActionSuffixTokens = 2;

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

        if (ContainsInvalidUnicodeSequence(draft)) {
            return Array.Empty<string>();
        }

        var phrases = ExtractQuotedPhrases(draft);
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
            if (tokens.Count >= MaxPendingActionCallToActionTokens) {
                break;
            }
        }

        if (tokens.Count < MaxPendingActionCallToActionTokens) {
            ExtractUnquotedPendingActionCallToActionTokens(draft, tokens, seen);
        }

        return tokens.Count == 0 ? Array.Empty<string>() : tokens.ToArray();
    }

    private static bool LooksLikeCompactCallToActionToken(string token) {
        var value = (token ?? string.Empty).Trim();
        if (value.Length == 0 || value.Length > MaxCompactCallToActionLength) {
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
        var tokens = CountLetterDigitTokens(value, maxTokens: MaxCompactCallToActionTokenScan);
        return tokens is > 0 and <= MaxCompactCallToActionTokenCount;
    }

    private static bool UserMatchesPendingActionCallToActionTokens(string userText, IReadOnlyList<string> tokens) {
        if (tokens is null || tokens.Count == 0) {
            return false;
        }

        var raw = (userText ?? string.Empty).Trim();
        if (raw.Length == 0 || raw.Length > MaxCompactCallToActionLength) {
            return false;
        }

        if (ContainsInvalidUnicodeSequence(raw)) {
            return false;
        }

        // Guardrails must run on raw input (pre-normalization) to avoid normalization widening matches.
        if (ContainsQuestionSignal(raw)) {
            return false;
        }
        if (raw.IndexOfAny(PendingActionConfirmationDisqualifierPunctuation) >= 0) {
            return false;
        }
        if (LooksLikeStructuredPendingActionConfirmationInput(raw)) {
            return false;
        }

        var request = NormalizeCompactText(raw);
        if (request.Length == 0 || request.Length > MaxCompactCallToActionLength) {
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

            if (ContainsPhraseWithBoundaries(request, token)) {
                return true;
            }
        }

        return false;
    }

    private static void ExtractUnquotedPendingActionCallToActionTokens(
        string assistantContext,
        List<string> tokens,
        HashSet<string> seen) {
        var lines = SplitLines(assistantContext);
        for (var i = 0; i < lines.Count && tokens.Count < MaxPendingActionCallToActionTokens; i++) {
            var line = (lines[i] ?? string.Empty).Trim();
            if (line.Length == 0) {
                continue;
            }

            if (line.StartsWith("[Action]", StringComparison.Ordinal)
                || line.StartsWith("ix:action:v1", StringComparison.OrdinalIgnoreCase)) {
                break;
            }

            if (TryAddStandalonePendingActionCallToActionToken(line, i, lines, tokens, seen)) {
                continue;
            }

            TryAddInlinePendingActionCallToActionTokens(line, tokens, seen);
        }
    }

    private static bool TryAddStandalonePendingActionCallToActionToken(
        string line,
        int lineIndex,
        IReadOnlyList<string> lines,
        List<string> tokens,
        HashSet<string> seen) {
        if (lineIndex <= 0 || !PreviousNonEmptyPendingActionLineEndsWithColon(lines, lineIndex)) {
            return false;
        }

        return TryAddPendingActionCallToActionToken(line, tokens, seen);
    }

    private static void TryAddInlinePendingActionCallToActionTokens(
        string line,
        List<string> tokens,
        HashSet<string> seen) {
        for (var i = 0; i < line.Length && tokens.Count < MaxPendingActionCallToActionTokens; i++) {
            if (!IsCallToActionComma(line[i])) {
                continue;
            }

            var clause = NormalizeCompactCallToActionToken(line[..i]);
            if (!LooksLikeCompactCallToActionToken(clause)) {
                continue;
            }

            var parts = clause.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < MinInlinePendingActionCallToActionSuffixTokens) {
                continue;
            }

            var maxSuffixTokens = Math.Min(MaxInlinePendingActionCallToActionSuffixTokens, parts.Length);
            for (var suffixTokenCount = MinInlinePendingActionCallToActionSuffixTokens;
                 suffixTokenCount <= maxSuffixTokens && tokens.Count < MaxPendingActionCallToActionTokens;
                 suffixTokenCount++) {
                var start = parts.Length - suffixTokenCount;
                var candidate = string.Join(" ", parts, start, suffixTokenCount);
                TryAddPendingActionCallToActionToken(candidate, tokens, seen);
            }
        }
    }

    private static bool TryAddPendingActionCallToActionToken(
        string candidate,
        List<string> tokens,
        HashSet<string> seen) {
        var token = NormalizeCompactCallToActionToken(candidate);
        if (!LooksLikeCompactCallToActionToken(token) || !seen.Add(token)) {
            return false;
        }

        tokens.Add(token);
        return true;
    }

    private static bool PreviousNonEmptyPendingActionLineEndsWithColon(IReadOnlyList<string> lines, int currentLineIndex) {
        for (var i = currentLineIndex - 1; i >= 0; i--) {
            var line = (lines[i] ?? string.Empty).Trim();
            if (line.Length == 0) {
                continue;
            }

            return IsCallToActionColon(line[^1]);
        }

        return false;
    }

    private static bool ContainsInvalidUnicodeSequence(string text) {
        var value = text ?? string.Empty;
        for (var i = 0; i < value.Length; i++) {
            var current = value[i];
            if (!char.IsSurrogate(current)) {
                continue;
            }

            if (char.IsHighSurrogate(current)
                && i + 1 < value.Length
                && char.IsLowSurrogate(value[i + 1])) {
                i++;
                continue;
            }

            return true;
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
