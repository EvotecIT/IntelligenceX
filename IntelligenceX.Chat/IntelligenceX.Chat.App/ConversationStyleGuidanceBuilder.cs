using System;
using System.Collections.Generic;

namespace IntelligenceX.Chat.App;

/// <summary>
/// Builds compact language-neutral guidance that helps the model mirror recent user style.
/// </summary>
internal static class ConversationStyleGuidanceBuilder {
    private const int MaxRecentUserTurns = 4;
    private const int MaxPendingActionsPreview = 3;
    private const int TerseTurnTokenLimit = 8;
    private const int TerseTurnLengthLimit = 60;
    private const int DetailedTurnTokenFloor = 18;
    private const int DetailedTurnLengthFloor = 120;
    private const int MultiSentenceFloor = 2;
    private const int AssistantExcerptLengthLimit = 180;

    /// <summary>
    /// Builds guidance lines from recent transcript messages.
    /// </summary>
    /// <param name="messages">Recent transcript messages.</param>
    /// <returns>Conversation-style guidance lines, or an empty list when no clear signal exists.</returns>
    internal static IReadOnlyList<string> BuildRecentUserStyleLines(IReadOnlyList<(string Role, string Text, DateTime Time, string? Model)> messages) {
        ArgumentNullException.ThrowIfNull(messages);

        var recentUserTurns = CollectRecentUserTurns(messages);
        if (recentUserTurns.Count == 0) {
            return Array.Empty<string>();
        }

        return BuildRecentUserStyleLinesFromTexts(recentUserTurns);
    }

    /// <summary>
    /// Builds compact answer-shape guidance specifically for explicit capability questions.
    /// </summary>
    /// <param name="messages">Recent transcript messages.</param>
    internal static IReadOnlyList<string> BuildCapabilityAnswerStyleLines(IReadOnlyList<(string Role, string Text, DateTime Time, string? Model)> messages) {
        ArgumentNullException.ThrowIfNull(messages);

        var recentUserTurns = CollectRecentUserTurns(messages);
        if (recentUserTurns.Count == 0) {
            return Array.Empty<string>();
        }

        return BuildCapabilityAnswerStyleLinesFromTexts(recentUserTurns);
    }

    /// <summary>
    /// Returns <see langword="true"/> when the most recent assistant turn appears substantive enough to support acknowledgement-style replies.
    /// </summary>
    /// <param name="messages">Recent transcript messages.</param>
    internal static bool HasRecentSubstantiveAssistantAnswer(IReadOnlyList<(string Role, string Text, DateTime Time, string? Model)> messages) {
        ArgumentNullException.ThrowIfNull(messages);

        for (var i = messages.Count - 1; i >= 0; i--) {
            var message = messages[i];
            if (!string.Equals(message.Role, "Assistant", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            return ConversationTurnShapeClassifier.LooksLikeSubstantiveAssistantAnswer(message.Text);
        }

        return false;
    }

    /// <summary>
    /// Returns <see langword="true"/> when the most recent assistant turn appears to ask the user a question.
    /// </summary>
    /// <param name="messages">Recent transcript messages.</param>
    internal static bool HasRecentAssistantQuestion(IReadOnlyList<(string Role, string Text, DateTime Time, string? Model)> messages) {
        ArgumentNullException.ThrowIfNull(messages);

        for (var i = messages.Count - 1; i >= 0; i--) {
            var message = messages[i];
            if (!string.Equals(message.Role, "Assistant", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            return ConversationTurnShapeClassifier.LooksLikeAssistantQuestion(message.Text);
        }

        return false;
    }

    /// <summary>
    /// Builds a compact persisted hint for assistant clarification questions.
    /// </summary>
    /// <param name="assistantText">Assistant text to summarize.</param>
    internal static string? BuildAssistantQuestionHint(string? assistantText) {
        var normalized = (assistantText ?? string.Empty).Trim();
        if (!ConversationTurnShapeClassifier.LooksLikeAssistantQuestion(normalized)) {
            return null;
        }

        var excerpt = CompactAssistantExcerpt(normalized);
        return excerpt.Length == 0 ? null : excerpt;
    }

    /// <summary>
    /// Builds continuation-state guidance from the latest assistant turn and any pending actions.
    /// </summary>
    /// <param name="messages">Recent transcript messages.</param>
    /// <param name="pendingActions">Pending actions extracted from the latest assistant turn.</param>
    /// <param name="persistedAssistantQuestionHint">Optional persisted assistant-question hint from conversation state.</param>
    internal static IReadOnlyList<string> BuildContinuationStateLines(
        IReadOnlyList<(string Role, string Text, DateTime Time, string? Model)> messages,
        IReadOnlyList<AssistantPendingAction>? pendingActions,
        string? persistedAssistantQuestionHint = null) {
        ArgumentNullException.ThrowIfNull(messages);

        var lines = new List<string>(4);
        var latestAssistantText = GetLatestAssistantText(messages);
        var questionHint = BuildAssistantQuestionHint(latestAssistantText)
                           ?? CompactAssistantExcerpt(persistedAssistantQuestionHint);
        if (!string.IsNullOrWhiteSpace(questionHint)) {
            lines.Add("Latest assistant turn left a pending question or clarification. Treat the current user message as likely answering that question if it fits naturally.");
            lines.Add("Pending assistant question: " + questionHint);
        }

        if (pendingActions is { Count: > 0 }) {
            lines.Add("Latest assistant turn also exposed structured follow-up actions. Short user replies may be selecting or confirming one of them.");

            for (var i = 0; i < pendingActions.Count && i < MaxPendingActionsPreview; i++) {
                var action = pendingActions[i];
                var label = ResolvePendingActionLabel(action);
                if (label.Length == 0) {
                    continue;
                }

                lines.Add("Pending action option: " + label + " (`" + action.Reply.Trim() + "`)");
            }
        }

        return lines.Count == 0 ? Array.Empty<string>() : lines;
    }

    private static IReadOnlyList<string> BuildRecentUserStyleLinesFromTexts(IReadOnlyList<string> recentUserTurns) {
        var totalLength = 0;
        var totalTokens = 0;
        var terseTurns = 0;
        var detailedTurns = 0;
        var questionTurns = 0;
        var emphaticTurns = 0;

        for (var i = 0; i < recentUserTurns.Count; i++) {
            var text = recentUserTurns[i];
            totalLength += text.Length;

            var tokenCount = CountLetterDigitTokens(text, maxTokens: 64);
            totalTokens += tokenCount;

            if (tokenCount <= TerseTurnTokenLimit && text.Length <= TerseTurnLengthLimit) {
                terseTurns++;
            }

            if (tokenCount >= DetailedTurnTokenFloor
                || text.Length >= DetailedTurnLengthFloor
                || CountSentenceLikeBreaks(text) >= MultiSentenceFloor) {
                detailedTurns++;
            }

            if (ConversationTurnShapeClassifier.ContainsQuestionSignal(text)) {
                questionTurns++;
            }

            if (ContainsEmphasisSignal(text)) {
                emphaticTurns++;
            }
        }

        var averageLength = totalLength / recentUserTurns.Count;
        var averageTokens = totalTokens / recentUserTurns.Count;
        var lines = new List<string>(3);

        if (terseTurns >= Math.Max(2, recentUserTurns.Count - 1)
            || (averageTokens <= TerseTurnTokenLimit && averageLength <= TerseTurnLengthLimit)) {
            lines.Add("Recent user style is terse and direct. Match that with short, confident phrasing instead of padded preambles.");
            lines.Add("Keep the response shape compact: lead with the result or action, use short paragraphs, and avoid unnecessary recap.");
            lines.Add("Skip optional follow-up suggestions unless they unlock a clearly useful next action.");
            lines.Add("If the user's request already seems complete, it is fine to end cleanly after the answer.");
            lines.Add("Avoid filler closers such as generic 'let me know' offers unless a concrete next action is genuinely useful.");
        } else if (detailedTurns >= Math.Max(1, recentUserTurns.Count / 2)
                   || averageTokens >= DetailedTurnTokenFloor
                   || averageLength >= DetailedTurnLengthFloor) {
            lines.Add("Recent user style is more detailed and exploratory. Match that with slightly richer context while keeping momentum.");
            lines.Add("Allow a bit more explanation and rationale than usual, but keep the structure easy to scan.");
            lines.Add("When helpful, you may close with one or two concrete next-step suggestions.");
        }

        if (questionTurns >= Math.Max(1, recentUserTurns.Count / 2)) {
            lines.Add("Answer the user's core question or request first, then offer optional next steps.");
        }

        if (emphaticTurns > 0) {
            lines.Add("Match the user's energy and directness without becoming robotic, defensive, or confrontational.");
        }

        return lines.Count == 0 ? Array.Empty<string>() : lines;
    }

    private static IReadOnlyList<string> BuildCapabilityAnswerStyleLinesFromTexts(IReadOnlyList<string> recentUserTurns) {
        var totalLength = 0;
        var totalTokens = 0;
        var terseTurns = 0;
        var detailedTurns = 0;

        for (var i = 0; i < recentUserTurns.Count; i++) {
            var text = recentUserTurns[i];
            totalLength += text.Length;

            var tokenCount = CountLetterDigitTokens(text, maxTokens: 64);
            totalTokens += tokenCount;

            if (tokenCount <= TerseTurnTokenLimit && text.Length <= TerseTurnLengthLimit) {
                terseTurns++;
            }

            if (tokenCount >= DetailedTurnTokenFloor
                || text.Length >= DetailedTurnLengthFloor
                || CountSentenceLikeBreaks(text) >= MultiSentenceFloor) {
                detailedTurns++;
            }
        }

        var averageLength = totalLength / recentUserTurns.Count;
        var averageTokens = totalTokens / recentUserTurns.Count;
        var lines = new List<string>(3);

        if (terseTurns >= Math.Max(2, recentUserTurns.Count - 1)
            || (averageTokens <= TerseTurnTokenLimit && averageLength <= TerseTurnLengthLimit)) {
            lines.Add("For capability questions, answer with 2-3 concrete examples and one short invitation.");
            lines.Add("Keep it to one short paragraph or a tight bullet list.");
            lines.Add("Do not turn capability answers into environment inventories, exhaustive tool lists, or self-validation demos.");
        } else if (detailedTurns >= Math.Max(1, recentUserTurns.Count / 2)
                   || averageTokens >= DetailedTurnTokenFloor
                   || averageLength >= DetailedTurnLengthFloor) {
            lines.Add("For capability questions, answer with 3-5 concrete examples and a little rationale about how you would help.");
            lines.Add("Keep it practical and grounded in live session capability, but avoid exhaustive inventories.");
            lines.Add("You may spend a bit more space showing how you would approach the work when that helps the user choose.");
        } else {
            lines.Add("For capability questions, answer with a few concrete examples and one natural invitation to continue.");
            lines.Add("Keep it practical, grounded, and non-exhaustive.");
        }

        return lines;
    }

    private static List<string> CollectRecentUserTurns(IReadOnlyList<(string Role, string Text, DateTime Time, string? Model)> messages) {
        var recentUserTurns = new List<string>(MaxRecentUserTurns);
        for (var i = messages.Count - 1; i >= 0 && recentUserTurns.Count < MaxRecentUserTurns; i--) {
            var message = messages[i];
            if (!string.Equals(message.Role, "User", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            var text = (message.Text ?? string.Empty).Trim();
            if (text.Length == 0) {
                continue;
            }

            recentUserTurns.Add(text);
        }

        recentUserTurns.Reverse();
        return recentUserTurns;
    }

    private static bool ContainsEmphasisSignal(string text) {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        var uppercaseLetters = 0;
        var totalLetters = 0;
        for (var i = 0; i < normalized.Length; i++) {
            var ch = normalized[i];
            if (char.IsLetter(ch)) {
                totalLetters++;
                if (char.IsUpper(ch)) {
                    uppercaseLetters++;
                }
            }
        }

        if (normalized.Contains("!!", StringComparison.Ordinal)
            || normalized.Contains("！", StringComparison.Ordinal)
            || normalized.Contains("?!", StringComparison.Ordinal)
            || normalized.Contains("!?", StringComparison.Ordinal)) {
            return true;
        }

        return totalLetters >= 6 && uppercaseLetters * 3 >= totalLetters;
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

    private static string GetLatestAssistantText(IReadOnlyList<(string Role, string Text, DateTime Time, string? Model)> messages) {
        for (var i = messages.Count - 1; i >= 0; i--) {
            var message = messages[i];
            if (string.Equals(message.Role, "Assistant", StringComparison.OrdinalIgnoreCase)) {
                return (message.Text ?? string.Empty).Trim();
            }
        }

        return string.Empty;
    }

    private static string CompactAssistantExcerpt(string? text) {
        var normalized = (text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (normalized.Length == 0) {
            return string.Empty;
        }

        if (normalized.Length > AssistantExcerptLengthLimit) {
            normalized = normalized[..AssistantExcerptLengthLimit].TrimEnd() + "...";
        }

        return normalized;
    }

    private static string ResolvePendingActionLabel(AssistantPendingAction action) {
        var title = (action.Title ?? string.Empty).Trim();
        if (title.Length > 0) {
            return title;
        }

        var request = (action.Request ?? string.Empty).Trim();
        if (request.Length == 0) {
            return string.Empty;
        }

        return request.Length <= AssistantExcerptLengthLimit
            ? request
            : request[..AssistantExcerptLengthLimit].TrimEnd() + "...";
    }
}
