using System;

namespace IntelligenceX.Chat.App;

/// <summary>
/// Classifies compact conversation turns using language-neutral shape signals.
/// </summary>
internal static class ConversationTurnShapeClassifier {
    private const int FollowUpHardLengthLimit = 96;
    private const int FollowUpCompactLengthLimit = 64;
    private const int FollowUpCompactTokenLimit = 6;
    private const int FollowUpQuestionTokenLimit = 8;
    private const int CapabilityQuestionLengthLimit = 96;
    private const int CapabilityQuestionTokenLimit = 12;
    private const int RuntimeQuestionLengthLimit = 120;
    private const int RuntimeQuestionTokenLimit = 18;
    private const int GenericQuestionLongLetterTokenLength = 10;
    private const int LowContextShortTurnTokenLimit = 3;
    private const int LowContextShortTurnLengthLimit = 24;
    private const int LowContextShortTurnMaxLetterTokenLength = 7;
    private const int TokenCountScanLimit = 8;
    private const int SubstantiveAssistantTokenFloor = 18;
    private const int SubstantiveAssistantLengthFloor = 120;
    private const int SubstantiveAssistantSentenceFloor = 2;
    private static readonly string[] AssistantRuntimeCueWords = {
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
    /// Returns <see langword="true"/> when the text looks like a compact follow-up that depends on prior context.
    /// </summary>
    /// <param name="userText">User text to classify.</param>
    internal static bool LooksLikeContextDependentFollowUp(string? userText) {
        var text = (userText ?? string.Empty).Trim();
        if (text.Length == 0) {
            return false;
        }

        if (text.Contains('\n', StringComparison.Ordinal) || text.Length > FollowUpHardLengthLimit) {
            return false;
        }

        var tokenCount = CountLetterDigitTokens(text, TokenCountScanLimit);
        if (tokenCount == 0) {
            return false;
        }

        if (tokenCount <= FollowUpCompactTokenLimit && text.Length <= FollowUpCompactLengthLimit) {
            return true;
        }

        return tokenCount <= FollowUpQuestionTokenLimit && ContainsQuestionSignal(text);
    }

    /// <summary>
    /// Returns <see langword="true"/> when the text looks like a light opener or low-information short turn.
    /// </summary>
    /// <param name="userText">User text to classify.</param>
    internal static bool LooksLikeLowContextShortTurn(string? userText) {
        var text = (userText ?? string.Empty).Trim();
        if (text.Length == 0) {
            return false;
        }

        var tokenCount = CountLetterDigitTokens(text, TokenCountScanLimit);
        return tokenCount > 0
               && tokenCount <= LowContextShortTurnTokenLimit
               && text.Length <= LowContextShortTurnLengthLimit
               && !ContainsQuestionSignal(text)
               && !ContainsDigit(text)
               && !ContainsLikelyTechnicalPunctuation(text)
               && GetMaxLetterTokenLength(text) <= LowContextShortTurnMaxLetterTokenLength;
    }

    /// <summary>
    /// Returns <see langword="true"/> when the text contains a question-style punctuation signal.
    /// </summary>
    /// <param name="text">Input text.</param>
    internal static bool ContainsQuestionSignal(string? text) {
        var normalized = (text ?? string.Empty).Trim();
        return normalized.IndexOf('?') >= 0
               || normalized.IndexOf('？') >= 0
               || normalized.IndexOf('¿') >= 0
               || normalized.IndexOf('؟') >= 0;
    }

    /// <summary>
    /// Returns <see langword="true"/> when an assistant message appears substantive enough that a tiny user reply may be an acknowledgement.
    /// </summary>
    /// <param name="text">Assistant text to classify.</param>
    internal static bool LooksLikeSubstantiveAssistantAnswer(string? text) {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        var tokenCount = CountLetterDigitTokens(normalized, maxTokens: 64);
        return tokenCount >= SubstantiveAssistantTokenFloor
               || normalized.Length >= SubstantiveAssistantLengthFloor
               || CountSentenceLikeBreaks(normalized) >= SubstantiveAssistantSentenceFloor;
    }

    /// <summary>
    /// Returns <see langword="true"/> when an assistant turn looks like it is asking the user a question.
    /// </summary>
    /// <param name="text">Assistant text to classify.</param>
    internal static bool LooksLikeAssistantQuestion(string? text) {
        return ContainsQuestionSignal(text);
    }

    /// <summary>
    /// Returns <see langword="true"/> when the user appears to be asking, in human terms, what the assistant can help with.
    /// This is used only for lightweight prompt shaping so the model answers naturally instead of running live demos.
    /// </summary>
    internal static bool LooksLikeAssistantCapabilityQuestion(string? userText) {
        var text = (userText ?? string.Empty).Trim();
        if (text.Length == 0
            || text.Length > CapabilityQuestionLengthLimit
            || !ContainsQuestionSignal(text)
            || ContainsDigit(text)
            || ContainsLikelyTechnicalPunctuation(text)
            || ContainsLikelyDomainLikeToken(text)) {
            return false;
        }

        var tokens = CollectLetterDigitTokens(text, CapabilityQuestionTokenLimit + 1);
        if (tokens.Count == 0 || tokens.Count > CapabilityQuestionTokenLimit) {
            return false;
        }

        if (ContainsUppercaseAcronymToken(text) || CountRuntimeCueMatches(tokens) > 0) {
            return false;
        }

        if (tokens.Count == 1) {
            return LooksLikeSingleNonSegmentedQuestionToken(tokens[0]);
        }

        if (tokens.Count <= 3) {
            return false;
        }

        return LooksLikeBroadGenericQuestionShape(text, tokens);
    }

    /// <summary>
    /// Returns <see langword="true"/> when the user is explicitly asking about the active runtime, model, or tool inventory.
    /// This keeps detailed runtime self-reporting opt-in instead of always-on.
    /// </summary>
    internal static bool LooksLikeAssistantRuntimeIntrospectionQuestion(string? userText) {
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

    private static bool ContainsDigit(string text) {
        var normalized = (text ?? string.Empty).Trim();
        for (var i = 0; i < normalized.Length; i++) {
            if (char.IsDigit(normalized[i])) {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsLikelyTechnicalPunctuation(string text) {
        var normalized = (text ?? string.Empty).Trim();
        for (var i = 0; i < normalized.Length; i++) {
            var ch = normalized[i];
            if (ch is ':' or '/' or '\\' or '@' or '_' or '`') {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsBlockedRuntimeMetaPunctuation(string text) {
        var normalized = (text ?? string.Empty).Trim();
        for (var i = 0; i < normalized.Length; i++) {
            var ch = normalized[i];
            if (ch is ':' or '\\' or '@' or '_' or '`') {
                return true;
            }
        }

        return false;
    }

    private static int CountLetterDigitTokens(string text, int maxTokens) {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0 || maxTokens <= 0) {
            return 0;
        }

        var count = 0;
        var inToken = false;
        for (var i = 0; i < normalized.Length; i++) {
            var ch = normalized[i];
            if (char.IsLetterOrDigit(ch)) {
                if (!inToken) {
                    count++;
                    if (count >= maxTokens) {
                        return count;
                    }

                    inToken = true;
                }
            } else {
                inToken = false;
            }
        }

        return count;
    }

    private static int CountSentenceLikeBreaks(string text) {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return 0;
        }

        var count = 0;
        for (var i = 0; i < normalized.Length; i++) {
            var ch = normalized[i];
            if (ch is '.' or '!' or '?' or '。' or '！' or '？' or '\n') {
                count++;
            }
        }

        return count;
    }

    private static int GetMaxLetterTokenLength(string text) {
        var normalized = (text ?? string.Empty).Trim();
        var longest = 0;
        var current = 0;
        for (var i = 0; i < normalized.Length; i++) {
            if (char.IsLetter(normalized[i])) {
                current++;
                if (current > longest) {
                    longest = current;
                }
            } else {
                current = 0;
            }
        }

        return longest;
    }

    /// <summary>
    /// Returns <see langword="true"/> when the question is broad/generic enough for conversational capability or runtime routing.
    /// </summary>
    /// <param name="text">Original user text.</param>
    /// <param name="tokens">Letter/digit tokens extracted from the text.</param>
    /// <param name="allowUppercaseAcronyms">
    /// Allows enterprise qualifiers like DNS/AD when the caller already knows the question is meta/self-report oriented.
    /// Do not enable this for general task routing.
    /// </param>
    internal static bool LooksLikeBroadGenericQuestionShape(string text, IReadOnlyList<string> tokens, bool allowUppercaseAcronyms = false) {
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

    private static int CountRuntimeCueMatches(IReadOnlyList<string> tokens) {
        ArgumentNullException.ThrowIfNull(tokens);

        var matches = 0;
        for (var i = 0; i < tokens.Count; i++) {
            var token = tokens[i];
            for (var j = 0; j < AssistantRuntimeCueWords.Length; j++) {
                if (string.Equals(token, AssistantRuntimeCueWords[j], StringComparison.OrdinalIgnoreCase)) {
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

    private static bool LooksLikeSingleNonSegmentedQuestionToken(string token) {
        var normalized = (token ?? string.Empty).Trim();
        if (normalized.Length < 2) {
            return false;
        }

        var hasNonAsciiLetter = false;
        for (var i = 0; i < normalized.Length; i++) {
            var ch = normalized[i];
            if (!char.IsLetter(ch)) {
                return false;
            }

            if (ch > 127) {
                hasNonAsciiLetter = true;
            }
        }

        return hasNonAsciiLetter;
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

    private static bool ContainsLikelyDomainLikeToken(string text) {
        var normalized = (text ?? string.Empty).Trim();
        for (var i = 1; i < normalized.Length - 1; i++) {
            if (normalized[i] != '.') {
                continue;
            }

            if (char.IsLetterOrDigit(normalized[i - 1]) && char.IsLetterOrDigit(normalized[i + 1])) {
                return true;
            }
        }

        return false;
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
