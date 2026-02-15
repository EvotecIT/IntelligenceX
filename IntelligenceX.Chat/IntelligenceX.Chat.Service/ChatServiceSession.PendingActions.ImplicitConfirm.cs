using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    private static readonly char[] ImplicitConfirmationQuestionPunctuation = new[] { '?', '？', '¿', '؟' };
    // A very small disqualifying set for "this is clearly structured payload" (JSON/XML).
    // Avoid overly broad disqualifiers like quotes/backticks which are common in plain chat.
    private static readonly char[] ImplicitConfirmationStructuredChars = new[] { '{', '}', '[', ']', '<', '>' };
    private static readonly HashSet<string> ImplicitSingleActionRejectPhrases = new(
        new[] {
            "no",
            "nope",
            "nah",
            "nie",
            "no thanks",
            "no thank you",
            "not now",
            "dont",
            "don't",
            "do not"
        }
            .Select(CanonicalizeImplicitPendingActionConfirmationPhrase)
            .Where(static phrase => phrase.Length > 0),
        StringComparer.Ordinal);
    private static readonly HashSet<string> ImplicitSingleActionConfirmPhrases = new(
        new[] {
            // Keep this intentionally small and "high precision": when we have a single pending action, we only treat
            // very common acknowledgements as confirmation. Everything else should fall back to explicit `/act <id>`
            // or ordinal selection to avoid accidental execution.
            "ok",
            "okay",
            "okej",
            "sure",
            "yes",
            "yep",
            "yup",
            "do it",
            "run it",
            "tak",
            "dzialaj",
            "uruchom",
            "uruchom to",
            "dalej",
            "继续",
            "继续执行",
            "好",
            "好的",
            "行"
        }
            .Select(CanonicalizeImplicitPendingActionConfirmationPhrase)
            .Where(static phrase => phrase.Length > 0),
        StringComparer.Ordinal);

    private static bool LooksLikeImplicitPendingActionConfirmation(string userText) {
        // The caller is responsible for trimming/primary normalization; keep this predicate stable and avoid
        // re-trimming which can subtly change the decision boundary for wrapper/punctuation cases.
        var raw = userText ?? string.Empty;
        if (raw.Length == 0 || raw.Length > 32) {
            return false;
        }

        // Never treat multi-line inputs as confirmations. This prevents smuggling commands or extra context
        // while still looking like a short acknowledgement.
        if (raw.Contains('\n', StringComparison.Ordinal) || raw.Contains('\r', StringComparison.Ordinal)) {
            return false;
        }

        // Avoid treating follow-up questions as confirmations ("why?", "dalej?", "¿por qué?", "لماذا؟").
        if (raw.IndexOfAny(ImplicitConfirmationQuestionPunctuation) >= 0) {
            return false;
        }

        // Avoid accidentally consuming explicit commands or paths.
        if (raw.StartsWith("/", StringComparison.Ordinal)
            || raw.StartsWith("-", StringComparison.Ordinal)
            || raw.Contains('\\', StringComparison.Ordinal)
            || raw.Contains("://", StringComparison.Ordinal)
            || LooksLikeWindowsDrivePath(raw)) {
            return false;
        }

        // Reject obvious structured payload fragments early. This prevents punctuation trimming from turning
        // a paste like `{"go":false}` into an allowlisted confirmation token.
        if (raw.IndexOfAny(ImplicitConfirmationStructuredChars) >= 0) {
            return false;
        }

        // Treat assignment-ish fragments as "new context", not confirmation (e.g. "x=y", "FOO=bar").
        // This also closes a class of "structured fragment -> accidental token" risks if the allowlist expands.
        if (raw.Contains('=', StringComparison.Ordinal)) {
            return false;
        }

        // Support chatty wrapper confirmations like `ok` without turning arbitrary code-ish snippets into tokens.
        // We only unwrap when the inner text is already an allowlisted confirmation.
        if (raw is { Length: >= 2 } && raw[0] == '`' && raw[^1] == '`') {
            var inner = raw.Substring(1, raw.Length - 2);
            var innerNormalized = CanonicalizeImplicitPendingActionConfirmationPhrase(inner);
            if (innerNormalized.Length != 0
                && !LooksLikeImplicitSingleActionReject(innerNormalized)
                && ImplicitSingleActionConfirmPhrases.Contains(innerNormalized)) {
                return true;
            }
            return false;
        }

        var normalized = CanonicalizeImplicitPendingActionConfirmationPhrase(raw);
        if (normalized.Length == 0) {
            return false;
        }

        // Extra safety: never treat explicit negative acknowledgements (including common "no <something>" variants)
        // as confirmation.
        if (LooksLikeImplicitSingleActionReject(normalized)) {
            return false;
        }

        // High-precision allowlist to avoid running tools from benign short messages ("tomorrow", "wait", "thanks").
        return ImplicitSingleActionConfirmPhrases.Contains(normalized);
    }

    private static bool LooksLikeImplicitSingleActionReject(string normalized) {
        if (ImplicitSingleActionRejectPhrases.Contains(normalized)) {
            return true;
        }

        // Conservative "decorated reject" handling (safety-first): treat short refusal leads as rejections even when
        // they're followed by punctuation and additional text (e.g. "no, thanks", "nah. later", "nie: jutro").
        // False positives are acceptable here because this predicate is only used to *prevent* implicit execution.
        if (LooksLikeDecoratedRejectLead(normalized, "no")
            || LooksLikeDecoratedRejectLead(normalized, "nope")
            || LooksLikeDecoratedRejectLead(normalized, "nah")
            || LooksLikeDecoratedRejectLead(normalized, "nie")) {
            return true;
        }

        // Conservative prefix rejects (safety-first): avoid implicit confirmation when the message starts
        // like a refusal, even if it includes extra words.
        return normalized.StartsWith("dont", StringComparison.Ordinal)
            || normalized.StartsWith("don't", StringComparison.Ordinal)
            || normalized.StartsWith("do not", StringComparison.Ordinal);
    }

    private static bool LooksLikeDecoratedRejectLead(string normalized, string lead) {
        if (!normalized.StartsWith(lead, StringComparison.Ordinal)) {
            return false;
        }

        if (normalized.Length == lead.Length) {
            return true;
        }

        var next = normalized[lead.Length];
        if (char.IsWhiteSpace(next)) {
            return true;
        }

        // Treat common delimiters after a refusal lead as rejection even if additional text follows.
        return IsRejectLeadPunctuation(next);
    }

    private static bool IsRejectLeadPunctuation(char ch) {
        return IsDecorationPunctuation(ch)
            || ch is ':'
            or ';'
            or '\uFF1A' // ：
            or '\uFF1B' // ；
            or '?'
            or '？'
            or '¿'
            or '؟';
    }

    private static bool LooksLikeWindowsDrivePath(string text) {
        // Common case: "C:\\Windows\\..." / "D:/logs/..."
        return text is { Length: >= 3 }
            && char.IsLetter(text[0])
            && text[1] == ':'
            && (text[2] == '\\' || text[2] == '/');
    }

    private static string CanonicalizeImplicitPendingActionConfirmationPhrase(string text) {
        var normalized = (text ?? string.Empty)
            .Trim()
            .Normalize(NormalizationForm.FormKC);

        // Trim leading/trailing *decoration* punctuation only (including common CJK/fullwidth punctuation) so "ok!"
        // and "ok！" match. Avoid trimming arbitrary punctuation (like ':' or ';') because it can turn incomplete
        // prefixes ("ok:" / "go:") into allowlisted confirmations.
        var span = normalized.AsSpan();
        var start = 0;
        var end = span.Length;
        while (start < end
            && (char.IsWhiteSpace(span[start])
                || span[start] == '`'
                || IsDecorationPunctuation(span[start]))) {
            start++;
        }
        while (end > start
            && (char.IsWhiteSpace(span[end - 1])
                || span[end - 1] == '`'
                || IsDecorationPunctuation(span[end - 1]))) {
            end--;
        }
        normalized = (start == 0 && end == span.Length) ? normalized : span.Slice(start, end - start).ToString();
        if (normalized.Length == 0) {
            return string.Empty;
        }

        normalized = CollapseWhitespace(normalized).Trim();
        if (normalized.Length == 0) {
            return string.Empty;
        }

        normalized = NormalizeApostrophes(normalized);
        return normalized.ToLowerInvariant();
    }

    private static bool IsDecorationPunctuation(char ch) {
        // Conservative set of punctuation that is commonly used as "decoration" around short acknowledgements.
        // Intentionally excludes ":" and ";" so prefixes like "ok:" remain non-confirming.
        return ch is '.'
            or '!'
            or ','
            or '\u2026' // …
            or '\u3002' // 。
            or '\uFF01' // ！
            or '\uFF0C' // ，
            or '\uFF0E' // ．
            or '\uFF61'; // ｡
    }

    private static string NormalizeApostrophes(string value) {
        if (string.IsNullOrEmpty(value)) {
            return value;
        }

        // Avoid IndexOfAny with a shared char[] so the normalization set is unambiguous (and easy to audit).
        var idx = value.IndexOf('\u2018');
        if (idx < 0) {
            idx = value.IndexOf('\u2019');
        }
        if (idx < 0) {
            idx = value.IndexOf('\uFF07');
        }
        if (idx < 0) {
            return value;
        }

        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++) {
            var ch = value[i];
            if (ch == '\u2018' || ch == '\u2019' || ch == '\uFF07') {
                sb.Append('\'');
            } else {
                sb.Append(ch);
            }
        }
        return sb.ToString();
    }

}

