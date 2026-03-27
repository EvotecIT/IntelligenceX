using System;
using System.Collections.Generic;

namespace IntelligenceX.Chat.Abstractions;

/// <summary>
/// Indicates how runtime self-report mode was detected for a turn.
/// </summary>
public enum RuntimeSelfReportDetectionSource {
    /// <summary>
    /// The turn was not classified as a runtime self-report question.
    /// </summary>
    None,

    /// <summary>
    /// Detection relied on the minimal free-text lexical fallback.
    /// </summary>
    LexicalFallback,

    /// <summary>
    /// Detection came from a structured runtime self-report directive.
    /// </summary>
    StructuredDirective
}

/// <summary>
/// Shared classifier for compact runtime self-report turns so app and host stay behaviorally aligned.
/// </summary>
public static class RuntimeSelfReportTurnClassifier {
    private const int RuntimeQuestionLengthLimit = 120;
    private const int RuntimeQuestionTokenLimit = 18;
    private const int CompactRuntimeQuestionLengthLimit = 72;
    private const int CompactRuntimeQuestionTokenLimit = 7;
    private const int ShortRuntimeQuestionTokenLimit = 5;
    private const int GenericQuestionLongLetterTokenLength = 10;
    private const int ConcretePlanningQuestionMinimumTokens = 5;
    private const int ConcretePlanningPostCueVerbLikeTokenLength = 6;
    private const int ConcretePlanningAdditionalConcreteTokenLength = 6;
    private const int ConcretePlanningAdditionalConcreteTokenCount = 1;
    private const int ConcretePlanningShortBridgeTokenLength = 3;
    private const int ShortSingleCueQuestionTokenLimit = 4;

    /// <summary>
    /// Structured runtime self-report analysis shared across app and host consumers.
    /// </summary>
    /// <param name="IsRuntimeIntrospectionQuestion">Whether the turn is an explicit runtime self-report question.</param>
    /// <param name="CompactReply">Whether the reply should stay in the compact runtime-answer shape.</param>
    /// <param name="ModelRequested">Whether model/runtime identity details were requested.</param>
    /// <param name="ToolingRequested">Whether tooling details were requested.</param>
    /// <param name="UserRequestLiteral">Normalized user request literal carried forward for prompt building.</param>
    /// <param name="FromStructuredDirective">Whether the analysis was sourced from a structured directive rather than lexical fallback.</param>
    public readonly record struct RuntimeSelfReportTurnAnalysis(
        bool IsRuntimeIntrospectionQuestion,
        bool CompactReply,
        bool ModelRequested,
        bool ToolingRequested,
        string UserRequestLiteral,
        bool FromStructuredDirective) {
        /// <summary>
        /// Gets the normalized detection source for the analysis result.
        /// </summary>
        public RuntimeSelfReportDetectionSource DetectionSource => !IsRuntimeIntrospectionQuestion
            ? RuntimeSelfReportDetectionSource.None
            : FromStructuredDirective
                ? RuntimeSelfReportDetectionSource.StructuredDirective
                : RuntimeSelfReportDetectionSource.LexicalFallback;
    }
    private const int RuntimeCueAffixLengthLimit = 2;
    private static readonly string[] RuntimeCueBlockedAffixes = {
        "s",
        "es"
    };
    /// <summary>
    /// Returns <see langword="true"/> when the text looks like a runtime/model/tool self-report question.
    /// </summary>
    public static bool LooksLikeRuntimeIntrospectionQuestion(string? userText) {
        return Analyze(userText).IsRuntimeIntrospectionQuestion;
    }

    /// <summary>
    /// Returns <see langword="true"/> when a runtime self-report ask is compact enough for a short meta reply.
    /// </summary>
    public static bool LooksLikeCompactRuntimeIntrospectionQuestion(string? userText) {
        return Analyze(userText).CompactReply;
    }

    /// <summary>
    /// Returns <see langword="true"/> when the runtime self-report ask explicitly includes tooling scope
    /// or a structured directive has already declared that tooling details are desired.
    /// </summary>
    public static bool LooksLikeToolingScopedRuntimeIntrospectionQuestion(string? userText) {
        return Analyze(userText).ToolingRequested;
    }

    /// <summary>
    /// Returns <see langword="true"/> when the runtime self-report ask explicitly includes model/runtime identity
    /// or the structured directive has already declared that model details are desired.
    /// </summary>
    public static bool LooksLikeModelScopedRuntimeIntrospectionQuestion(string? userText) {
        return Analyze(userText).ModelRequested;
    }

    /// <summary>
    /// Produces a structured runtime self-report analysis so app and host can share one normalized view
    /// instead of re-running multiple cue/scope checks independently.
    /// </summary>
    public static RuntimeSelfReportTurnAnalysis Analyze(string? userText) {
        if (RuntimeSelfReportDirective.TryParse(userText, out var directive)) {
            var literal = string.IsNullOrWhiteSpace(directive.UserRequestLiteral)
                ? (userText ?? string.Empty).Trim()
                : directive.UserRequestLiteral!.Trim();
            RuntimeSelfReportTurnAnalysis lexical = default;
            if (!directive.ModelRequested.HasValue || !directive.ToolingRequested.HasValue) {
                lexical = AnalyzeWithoutDirective(literal);
            }

            return new RuntimeSelfReportTurnAnalysis(
                IsRuntimeIntrospectionQuestion: true,
                CompactReply: directive.CompactReply,
                ModelRequested: directive.ModelRequested ?? lexical.ModelRequested,
                ToolingRequested: directive.ToolingRequested ?? lexical.ToolingRequested,
                UserRequestLiteral: literal,
                FromStructuredDirective: true);
        }

        return AnalyzeWithoutDirective(userText);
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

    private static RuntimeSelfReportTurnAnalysis AnalyzeWithoutDirective(string? userText) {
        var text = (userText ?? string.Empty).Trim();
        if (text.Length == 0
            || text.Length > RuntimeQuestionLengthLimit
            || !ContainsQuestionSignal(text)
            || ContainsBlockedRuntimeMetaPunctuation(text)) {
            return new RuntimeSelfReportTurnAnalysis(false, false, false, false, text, false);
        }

        var tokens = CollectLetterDigitTokens(text, RuntimeQuestionTokenLimit + 1);
        if (tokens.Count == 0 || tokens.Count > RuntimeQuestionTokenLimit) {
            return new RuntimeSelfReportTurnAnalysis(false, false, false, false, text, false);
        }

        var runtimeCueMatches = CountRuntimeCueMatches(tokens);
        if (runtimeCueMatches == 0) {
            return new RuntimeSelfReportTurnAnalysis(false, false, false, false, text, false);
        }

        if (LooksLikeBareLexicalCueQuestion(tokens, runtimeCueMatches)) {
            return new RuntimeSelfReportTurnAnalysis(false, false, false, false, text, false);
        }

        if (LooksLikeShortSingleCueFallbackQuestion(text, tokens, runtimeCueMatches)) {
            return new RuntimeSelfReportTurnAnalysis(false, false, false, false, text, false);
        }

        if (LooksLikeShortSingleCueScopedQualifierQuestion(text, tokens, runtimeCueMatches)) {
            return new RuntimeSelfReportTurnAnalysis(false, false, false, false, text, false);
        }

        if (LooksLikeSingleCueWeakBridgeQuestion(text, tokens, runtimeCueMatches)) {
            return new RuntimeSelfReportTurnAnalysis(false, false, false, false, text, false);
        }

        if (LooksLikeConcreteSingleModelPlanningQuestion(tokens)
            || LooksLikeConcreteToolingPlanningQuestion(tokens)) {
            return new RuntimeSelfReportTurnAnalysis(false, false, false, false, text, false);
        }

        var isRuntimeQuestion = tokens.Count <= ShortRuntimeQuestionTokenLimit
            ? !LooksLikeConcreteQuestionLead(tokens)
            : LooksLikeBroadGenericQuestionShape(text, tokens, allowUppercaseAcronyms: true);
        if (!isRuntimeQuestion) {
            return new RuntimeSelfReportTurnAnalysis(false, false, false, false, text, false);
        }

        var toolingRequested = CountToolingCueMatches(tokens) > 0;
        var modelRequested = CountModelCueMatches(tokens) > 0 || !toolingRequested;
        var compactReply = text.Length <= CompactRuntimeQuestionLengthLimit
                           && tokens.Count <= CompactRuntimeQuestionTokenLimit;
        return new RuntimeSelfReportTurnAnalysis(true, compactReply, modelRequested, toolingRequested, text, false);
    }

    private static int CountRuntimeCueMatches(IReadOnlyList<string> tokens) => RuntimeSelfReportCueCatalog.CountLexicalFallbackCueMatches(tokens);

    private static int CountToolingCueMatches(IReadOnlyList<string> tokens) {
        ArgumentNullException.ThrowIfNull(tokens);

        var matches = 0;
        for (var i = 0; i < tokens.Count; i++) {
            var token = tokens[i];
            if (IsRuntimeCueToken(token, "tool")
                || IsRuntimeCueToken(token, "tools")
                || IsRuntimeCueToken(token, "pack")
                || IsRuntimeCueToken(token, "packs")
                || IsRuntimeCueToken(token, "plugin")) {
                matches++;
                continue;
            }

            if (IsRuntimeCueToken(token, "plugins")) {
                matches++;
            }
        }

        return matches;
    }

    private static int CountModelCueMatches(IReadOnlyList<string> tokens) {
        ArgumentNullException.ThrowIfNull(tokens);

        var matches = 0;
        for (var i = 0; i < tokens.Count; i++) {
            var token = tokens[i];
            if (IsRuntimeCueToken(token, "model")
                || IsRuntimeCueToken(token, "runtime")) {
                matches++;
            }
        }

        return matches;
    }

    private static bool IsRuntimeCueToken(string? token, string cueWord) {
        var normalized = (token ?? string.Empty).Trim();
        if (normalized.Length == 0 || cueWord.Length == 0) {
            return false;
        }

        if (string.Equals(normalized, cueWord, StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        if (!normalized.StartsWith(cueWord, StringComparison.OrdinalIgnoreCase)
            || normalized.Length <= cueWord.Length
            || normalized.Length > cueWord.Length + RuntimeCueAffixLengthLimit) {
            return false;
        }

        for (var i = cueWord.Length; i < normalized.Length; i++) {
            if (!char.IsLetter(normalized[i])) {
                return false;
            }
        }

        var affix = normalized[cueWord.Length..];
        for (var i = 0; i < RuntimeCueBlockedAffixes.Length; i++) {
            if (string.Equals(affix, RuntimeCueBlockedAffixes[i], StringComparison.OrdinalIgnoreCase)) {
                return false;
            }
        }

        return true;
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

    private static bool LooksLikeConcreteSingleModelPlanningQuestion(IReadOnlyList<string> tokens) {
        ArgumentNullException.ThrowIfNull(tokens);

        if (tokens.Count < ConcretePlanningQuestionMinimumTokens
            || CountToolingCueMatches(tokens) > 0
            || CountModelCueMatches(tokens) != 1) {
            return false;
        }

        var modelCueIndex = -1;
        for (var i = 0; i < tokens.Count; i++) {
            if (IsRuntimeCueToken(tokens[i], "model")) {
                modelCueIndex = i;
                break;
            }
        }

        if (modelCueIndex < 0 || modelCueIndex >= tokens.Count - 3) {
            return false;
        }

        return HasConcretePlanningTailAfterCue(tokens, modelCueIndex);
    }

    private static bool LooksLikeConcreteToolingPlanningQuestion(IReadOnlyList<string> tokens) {
        ArgumentNullException.ThrowIfNull(tokens);

        if (tokens.Count < ConcretePlanningQuestionMinimumTokens
            || CountToolingCueMatches(tokens) != 1
            || CountModelCueMatches(tokens) > 0) {
            return false;
        }

        var toolingCueIndex = -1;
        for (var i = 0; i < tokens.Count; i++) {
            if (IsRuntimeCueToken(tokens[i], "tool")
                || IsRuntimeCueToken(tokens[i], "tools")) {
                toolingCueIndex = i;
                break;
            }
        }

        if (toolingCueIndex < 0 || toolingCueIndex >= tokens.Count - 3) {
            return false;
        }

        return HasConcretePlanningTailAfterCue(tokens, toolingCueIndex);
    }

    private static bool LooksLikeBareLexicalCueQuestion(IReadOnlyList<string> tokens, int runtimeCueMatches) {
        ArgumentNullException.ThrowIfNull(tokens);

        return runtimeCueMatches == 1
               && tokens.Count <= 2;
    }

    private static bool LooksLikeShortSingleCueFallbackQuestion(string text, IReadOnlyList<string> tokens, int runtimeCueMatches) {
        ArgumentNullException.ThrowIfNull(tokens);

        if (runtimeCueMatches != 1 || tokens.Count > ShortSingleCueQuestionTokenLimit) {
            return false;
        }

        if (ContainsShortSingleCuePreservingPunctuation(text) || ContainsInflectedLexicalFallbackCue(tokens)) {
            return false;
        }

        return true;
    }

    private static bool LooksLikeShortSingleCueScopedQualifierQuestion(string text, IReadOnlyList<string> tokens, int runtimeCueMatches) {
        ArgumentNullException.ThrowIfNull(tokens);

        if (runtimeCueMatches != 1) {
            return false;
        }

        var normalized = (text ?? string.Empty).Trim();
        return normalized.IndexOf('/') >= 0
               || normalized.IndexOf('.') >= 0
               || ContainsUppercaseAcronymToken(normalized);
    }

    private static bool LooksLikeSingleCueWeakBridgeQuestion(string text, IReadOnlyList<string> tokens, int runtimeCueMatches) {
        ArgumentNullException.ThrowIfNull(tokens);

        if (runtimeCueMatches != 1
            || tokens.Count < ConcretePlanningQuestionMinimumTokens
            || ContainsShortSingleCuePreservingPunctuation(text)
            || ContainsInflectedLexicalFallbackCue(tokens)) {
            return false;
        }

        var cueIndex = FindFirstLexicalFallbackCueIndex(tokens);
        if (cueIndex < 0 || cueIndex >= tokens.Count - 1) {
            return false;
        }

        return GetMaxTokenLength(tokens, cueIndex + 1) <= 4;
    }

    private static bool ContainsShortSingleCuePreservingPunctuation(string text) {
        var normalized = (text ?? string.Empty).Trim();
        for (var i = 0; i < normalized.Length; i++) {
            var ch = normalized[i];
            if (ch is ':' or '_' or '`') {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsInflectedLexicalFallbackCue(IReadOnlyList<string> tokens) {
        ArgumentNullException.ThrowIfNull(tokens);

        for (var i = 0; i < tokens.Count; i++) {
            var token = (tokens[i] ?? string.Empty).Trim();
            if (token.Length == 0) {
                continue;
            }

            for (var j = 0; j < RuntimeSelfReportCueCatalog.LexicalFallbackCueWords.Length; j++) {
                var cueWord = RuntimeSelfReportCueCatalog.LexicalFallbackCueWords[j];
                if (IsRuntimeCueToken(token, cueWord)
                    && !string.Equals(token, cueWord, StringComparison.OrdinalIgnoreCase)) {
                    return true;
                }
            }
        }

        return false;
    }

    private static int FindFirstLexicalFallbackCueIndex(IReadOnlyList<string> tokens) {
        ArgumentNullException.ThrowIfNull(tokens);

        for (var i = 0; i < tokens.Count; i++) {
            var token = (tokens[i] ?? string.Empty).Trim();
            if (token.Length == 0) {
                continue;
            }

            for (var j = 0; j < RuntimeSelfReportCueCatalog.LexicalFallbackCueWords.Length; j++) {
                if (IsRuntimeCueToken(token, RuntimeSelfReportCueCatalog.LexicalFallbackCueWords[j])) {
                    return i;
                }
            }
        }

        return -1;
    }

    private static int GetMaxTokenLength(IReadOnlyList<string> tokens, int startIndex) {
        ArgumentNullException.ThrowIfNull(tokens);

        var maxLength = 0;
        for (var i = Math.Max(0, startIndex); i < tokens.Count; i++) {
            if (tokens[i].Length > maxLength) {
                maxLength = tokens[i].Length;
            }
        }

        return maxLength;
    }

    private static bool HasConcretePlanningTailAfterCue(IReadOnlyList<string> tokens, int cueIndex) {
        ArgumentNullException.ThrowIfNull(tokens);

        if (cueIndex < 0 || cueIndex >= tokens.Count - 2) {
            return false;
        }

        if (tokens[cueIndex + 1].Length >= ConcretePlanningPostCueVerbLikeTokenLength) {
            return CountConcreteTailTokens(tokens, cueIndex + 2) >= ConcretePlanningAdditionalConcreteTokenCount;
        }

        if (cueIndex < tokens.Count - 3
            && tokens[cueIndex + 1].Length <= ConcretePlanningShortBridgeTokenLength
            && tokens[cueIndex + 2].Length <= ConcretePlanningShortBridgeTokenLength) {
            return CountConcreteTailTokens(tokens, cueIndex + 3) >= ConcretePlanningAdditionalConcreteTokenCount;
        }

        return false;
    }

    private static int CountConcreteTailTokens(IReadOnlyList<string> tokens, int startIndex) {
        ArgumentNullException.ThrowIfNull(tokens);

        var count = 0;
        for (var i = Math.Max(0, startIndex); i < tokens.Count; i++) {
            if (tokens[i].Length >= ConcretePlanningAdditionalConcreteTokenLength) {
                count++;
            }
        }

        return count;
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
