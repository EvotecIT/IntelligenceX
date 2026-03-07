using System;
using System.Collections.Generic;

namespace IntelligenceX.Chat.Abstractions;

/// <summary>
/// Shared classifier for compact runtime self-report turns so app and host stay behaviorally aligned.
/// </summary>
public static class RuntimeSelfReportTurnClassifier {
    private const int RuntimeQuestionLengthLimit = 120;
    private const int RuntimeQuestionTokenLimit = 18;
    private const int CompactRuntimeQuestionLengthLimit = 72;
    private const int CompactRuntimeQuestionTokenLimit = 7;
    private const int GenericQuestionLongLetterTokenLength = 10;
    private static readonly string[] RuntimeCueWords = {
        "model",
        "runtime",
        "tool",
        "tools",
        "pack",
        "packs",
        "plugin",
        "plugins",
        "transport"
    };

    /// <summary>
    /// Returns <see langword="true"/> when the text looks like a runtime/model/tool self-report question.
    /// </summary>
    public static bool LooksLikeRuntimeIntrospectionQuestion(string? userText) {
        var text = (userText ?? string.Empty).Trim();
        if (text.Length == 0
            || text.Length > RuntimeQuestionLengthLimit
            || !ContainsQuestionSignal(text)
            || ContainsBlockedRuntimeMetaPunctuation(text)) {
            return false;
        }

        var tokens = CollectLetterDigitTokens(text, RuntimeQuestionTokenLimit + 1);
        if (tokens.Count == 0 || tokens.Count > RuntimeQuestionTokenLimit) {
            return false;
        }

        var runtimeCueMatches = CountRuntimeCueMatches(tokens);
        if (runtimeCueMatches == 0) {
            return false;
        }

        if (tokens.Count <= 3) {
            return !LooksLikeConcreteQuestionLead(tokens);
        }

        // Runtime self-report asks often carry enterprise qualifiers like DNS/AD.
        // Allow uppercase acronyms here only so those meta-questions are not
        // misclassified as concrete operational tasks.
        return LooksLikeBroadGenericQuestionShape(text, tokens, allowUppercaseAcronyms: true);
    }

    /// <summary>
    /// Returns <see langword="true"/> when a runtime self-report ask is compact enough for a short meta reply.
    /// </summary>
    public static bool LooksLikeCompactRuntimeIntrospectionQuestion(string? userText) {
        var text = (userText ?? string.Empty).Trim();
        if (!LooksLikeRuntimeIntrospectionQuestion(text)) {
            return false;
        }

        if (text.Length > CompactRuntimeQuestionLengthLimit) {
            return false;
        }

        var tokens = CollectLetterDigitTokens(text, CompactRuntimeQuestionTokenLimit + 1);
        return tokens.Count > 0 && tokens.Count <= CompactRuntimeQuestionTokenLimit;
    }

    /// <summary>
    /// Returns <see langword="true"/> when the text contains a question-style punctuation signal.
    /// </summary>
    public static bool ContainsQuestionSignal(string? text) {
        var normalized = (text ?? string.Empty).Trim();
        return normalized.IndexOf('?') >= 0
               || normalized.IndexOf('？') >= 0
               || normalized.IndexOf('¿') >= 0
               || normalized.IndexOf('؟') >= 0;
    }

    private static bool ContainsBlockedRuntimeMetaPunctuation(string text) {
        var normalized = (text ?? string.Empty).Trim();
        for (var i = 0; i < normalized.Length; i++) {
            var ch = normalized[i];
            if (ch is '\\' or '@') {
                return true;
            }
        }

        return false;
    }

    private static int CountRuntimeCueMatches(IReadOnlyList<string> tokens) {
        ArgumentNullException.ThrowIfNull(tokens);

        var matches = 0;
        for (var i = 0; i < tokens.Count; i++) {
            var token = tokens[i];
            for (var j = 0; j < RuntimeCueWords.Length; j++) {
                if (string.Equals(token, RuntimeCueWords[j], StringComparison.OrdinalIgnoreCase)) {
                    matches++;
                    break;
                }
            }
        }

        return matches;
    }

    private static List<string> CollectLetterDigitTokens(string text, int maxTokens) {
        var normalized = (text ?? string.Empty).Trim();
        var tokens = new List<string>(Math.Max(0, Math.Min(maxTokens, 8)));
        if (normalized.Length == 0 || maxTokens <= 0) {
            return tokens;
        }

        var start = -1;
        for (var i = 0; i < normalized.Length; i++) {
            if (char.IsLetterOrDigit(normalized[i])) {
                if (start < 0) {
                    start = i;
                }
            } else if (start >= 0) {
                tokens.Add(normalized[start..i]);
                if (tokens.Count >= maxTokens) {
                    return tokens;
                }

                start = -1;
            }
        }

        if (start >= 0 && tokens.Count < maxTokens) {
            tokens.Add(normalized[start..]);
        }

        return tokens;
    }

    /// <summary>
    /// Shared broad-question shape helper used by both runtime and app-side conversational routing.
    /// </summary>
    public static bool LooksLikeBroadGenericQuestionShape(string text, IReadOnlyList<string> tokens, bool allowUppercaseAcronyms = false) {
        ArgumentNullException.ThrowIfNull(tokens);

        if (tokens.Count == 0 || (!allowUppercaseAcronyms && ContainsUppercaseAcronymToken(text))) {
            return false;
        }

        var longLetterTokens = 0;
        for (var i = 0; i < tokens.Count; i++) {
            var token = tokens[i];
            if (token.Length >= GenericQuestionLongLetterTokenLength && IsAllLetters(token)) {
                longLetterTokens++;
                if (longLetterTokens >= 2) {
                    return false;
                }
            }
        }

        if (LooksLikeConcreteQuestionLead(tokens)) {
            return false;
        }

        if (tokens.Count <= 3) {
            return false;
        }

        if (tokens.Count <= 5) {
            return HasTrailingShortToken(tokens, trailingTokenWindow: 2, maxTokenLength: 3);
        }

        return HasTrailingShortToken(tokens, trailingTokenWindow: 2, maxTokenLength: 3)
               || HasTrailingShortToken(tokens, trailingTokenWindow: 3, maxTokenLength: 3);
    }

    private static bool HasTrailingShortToken(IReadOnlyList<string> tokens, int trailingTokenWindow, int maxTokenLength) {
        ArgumentNullException.ThrowIfNull(tokens);

        if (tokens.Count == 0 || trailingTokenWindow <= 0 || maxTokenLength <= 0) {
            return false;
        }

        for (var i = Math.Max(0, tokens.Count - trailingTokenWindow); i < tokens.Count; i++) {
            if (tokens[i].Length > 0 && tokens[i].Length <= maxTokenLength) {
                return true;
            }
        }

        return false;
    }

    private static bool LooksLikeConcreteQuestionLead(IReadOnlyList<string> tokens) {
        ArgumentNullException.ThrowIfNull(tokens);

        if (tokens.Count < 4
            || tokens[0].Length == 0
            || tokens[0].Length > 3
            || tokens[1].Length == 0
            || tokens[1].Length > 3) {
            return false;
        }

        var concreteTailTokens = 0;
        for (var i = 2; i < tokens.Count; i++) {
            if (tokens[i].Length >= 4) {
                concreteTailTokens++;
                if (concreteTailTokens >= 2) {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool ContainsUppercaseAcronymToken(string text) {
        var normalized = (text ?? string.Empty).Trim();
        var currentLength = 0;
        var allUppercase = true;
        for (var i = 0; i < normalized.Length; i++) {
            var ch = normalized[i];
            if (char.IsLetter(ch)) {
                currentLength++;
                allUppercase &= char.IsUpper(ch);
            } else {
                if (currentLength is >= 2 and <= 5 && allUppercase) {
                    return true;
                }

                currentLength = 0;
                allUppercase = true;
            }
        }

        return currentLength is >= 2 and <= 5 && allUppercase;
    }

    private static bool IsAllLetters(string text) {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        for (var i = 0; i < normalized.Length; i++) {
            if (!char.IsLetter(normalized[i])) {
                return false;
            }
        }

        return true;
    }
}
