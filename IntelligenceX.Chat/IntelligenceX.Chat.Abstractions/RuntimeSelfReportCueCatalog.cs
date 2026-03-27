namespace IntelligenceX.Chat.Abstractions;

/// <summary>
/// Shared cue catalog for runtime self-report fallback and app-side capability blocking.
/// Keep the lexical fallback set intentionally minimal, while allowing the capability
/// gate to block a broader set of internal runtime-meta nouns from generic "what can you do" mode.
/// </summary>
public static class RuntimeSelfReportCueCatalog {
    private const int RuntimeCueAffixLengthLimit = 2;
    private static readonly string[] RuntimeCueBlockedAffixes = {
        "s",
        "es"
    };

    /// <summary>
    /// Minimal free-text fallback cues that still allow plain runtime self-report asks when no
    /// structured directive is present. Keep this list narrow and language-agnostic tests around it.
    /// </summary>
    public static readonly string[] LexicalFallbackCueWords = {
        "model",
        "tools"
    };

    /// <summary>
    /// Broader internal runtime-meta nouns that should stay out of generic capability-question mode
    /// even when they are no longer considered valid self-report triggers on their own.
    /// </summary>
    public static readonly string[] CapabilityBlockedMetaCueWords = {
        "model",
        "runtime",
        "tool",
        "tools",
        "transport",
        "pack",
        "packs",
        "plugin",
        "plugins"
    };

    /// <summary>
    /// Counts minimal free-text self-report fallback cues, using the shared cue-token rules
    /// that allow inflected borrowed forms like <c>modelu</c> while still blocking simple plurals like <c>models</c>.
    /// </summary>
    public static int CountLexicalFallbackCueMatches(IReadOnlyList<string> tokens) {
        ArgumentNullException.ThrowIfNull(tokens);

        var matches = 0;
        for (var i = 0; i < tokens.Count; i++) {
            var token = tokens[i];
            for (var j = 0; j < LexicalFallbackCueWords.Length; j++) {
                if (MatchesLexicalFallbackCueToken(token, LexicalFallbackCueWords[j])) {
                    matches++;
                    break;
                }
            }
        }

        return matches;
    }

    /// <summary>
    /// Counts broader internal runtime-meta nouns that should keep a question out of generic capability mode.
    /// These are exact-token checks because they are only used for app-side meta blocking, not self-report triggering.
    /// </summary>
    public static int CountCapabilityBlockedMetaCueMatches(IReadOnlyList<string> tokens) {
        ArgumentNullException.ThrowIfNull(tokens);

        var matches = 0;
        for (var i = 0; i < tokens.Count; i++) {
            var token = (tokens[i] ?? string.Empty).Trim();
            if (token.Length == 0) {
                continue;
            }

            for (var j = 0; j < CapabilityBlockedMetaCueWords.Length; j++) {
                if (string.Equals(token, CapabilityBlockedMetaCueWords[j], StringComparison.OrdinalIgnoreCase)) {
                    matches++;
                    break;
                }
            }
        }

        return matches;
    }

    private static bool MatchesLexicalFallbackCueToken(string? token, string cueWord) {
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

        var affix = normalized[cueWord.Length..];
        for (var i = 0; i < RuntimeCueBlockedAffixes.Length; i++) {
            if (string.Equals(affix, RuntimeCueBlockedAffixes[i], StringComparison.OrdinalIgnoreCase)) {
                return false;
            }
        }

        return true;
    }
}
