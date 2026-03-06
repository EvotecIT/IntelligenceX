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
    private const int LowContextShortTurnTokenLimit = 3;
    private const int LowContextShortTurnLengthLimit = 24;
    private const int LowContextShortTurnMaxLetterTokenLength = 7;
    private const int TokenCountScanLimit = 8;
    private const int SubstantiveAssistantTokenFloor = 18;
    private const int SubstantiveAssistantLengthFloor = 120;
    private const int SubstantiveAssistantSentenceFloor = 2;

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
}
