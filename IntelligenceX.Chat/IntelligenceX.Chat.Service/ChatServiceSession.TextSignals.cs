using System;

namespace IntelligenceX.Chat.Service;

internal sealed partial class ChatServiceSession {
    // Keep question detection script-agnostic for compact follow-ups and review loops.
    private static readonly char[] QuestionSignalPunctuation = new[] { '?', '？', '¿', '؟' };

    private static bool ContainsQuestionSignal(string text) {
        var value = text ?? string.Empty;
        return value.IndexOfAny(QuestionSignalPunctuation) >= 0;
    }
}
