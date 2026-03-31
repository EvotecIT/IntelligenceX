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
    /// Legacy lexical-fallback detection retained only for compatibility with explicit directives
    /// or precomputed analyses. Raw user text no longer upgrades into this mode automatically.
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
    private const int GenericQuestionLongLetterTokenLength = 10;

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
            var literal = (directive.UserRequestLiteral ?? string.Empty).Trim();
            if (IsTrustedStructuredDirective(directive, literal)) {
                return new RuntimeSelfReportTurnAnalysis(
                    IsRuntimeIntrospectionQuestion: true,
                    CompactReply: directive.CompactReply,
                    ModelRequested: directive.ModelRequested ?? false,
                    ToolingRequested: directive.ToolingRequested ?? false,
                    UserRequestLiteral: literal,
                    FromStructuredDirective: true);
            }

            var untrustedLiteral = literal.Length > 0 ? literal : userText;
            return AnalyzeWithoutTrustedDirective(untrustedLiteral);
        }

        return AnalyzeWithoutTrustedDirective(userText);
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

    private static RuntimeSelfReportTurnAnalysis AnalyzeWithoutTrustedDirective(string? userText) {
        var text = (userText ?? string.Empty).Trim();
        return new RuntimeSelfReportTurnAnalysis(false, false, false, false, text, false);
    }

    private static bool IsTrustedStructuredDirective(
        RuntimeSelfReportDirective.ParsedDirective directive,
        string literal) {
        return directive.DetectionSource == RuntimeSelfReportDetectionSource.StructuredDirective
               && literal.Length > 0;
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
