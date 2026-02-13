using System;
using System.Collections.Generic;
using IntelligenceX.Chat.App.Theming;

namespace IntelligenceX.Chat.App;

internal static class OnboardingPromptRules {
    public static bool IsLikelyOnboardingIntroPromptText(string? text) {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        return normalized.Contains("what should i call you", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("let's set this up", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("lets set this up", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("preferred name", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("assistant persona", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("pick a theme", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsAskNamePromptText(string? text) {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        return normalized.Contains("what should i call you", StringComparison.OrdinalIgnoreCase)
               && normalized.Contains("skip", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsAskThemePromptText(string? text) {
        var normalized = (text ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return false;
        }

        return normalized.Contains("theme", StringComparison.OrdinalIgnoreCase)
               && ThemeContract.ContainsKnownToken(normalized);
    }

    public static bool PruneDuplicateAskNamePrompts(List<(string Role, string Text, DateTime Time)> messages) {
        var changed = false;
        var seenAskName = false;

        for (var i = messages.Count - 1; i >= 0; i--) {
            var message = messages[i];
            if (!string.Equals(message.Role, "Assistant", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            if (!IsAskNamePromptText(message.Text)) {
                continue;
            }

            if (!seenAskName) {
                seenAskName = true;
                continue;
            }

            messages.RemoveAt(i);
            changed = true;
        }

        return changed;
    }

    public static bool PruneDuplicateAssistantLeadPrompts(List<(string Role, string Text, DateTime Time)> messages) {
        if (messages.Count <= 1) {
            return false;
        }

        var changed = false;
        var firstUserIndex = -1;
        for (var i = 0; i < messages.Count; i++) {
            if (string.Equals(messages[i].Role, "User", StringComparison.OrdinalIgnoreCase)) {
                firstUserIndex = i;
                break;
            }
        }

        var endExclusive = firstUserIndex >= 0 ? firstUserIndex : messages.Count;
        if (endExclusive <= 1) {
            return false;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = endExclusive - 1; i >= 0; i--) {
            var message = messages[i];
            if (!string.Equals(message.Role, "Assistant", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            if (!IsLikelyOnboardingIntroPromptText(message.Text)) {
                continue;
            }

            var key = NormalizePromptKey(message.Text);
            if (key.Length == 0) {
                continue;
            }

            if (seen.Contains(key)) {
                messages.RemoveAt(i);
                changed = true;
                continue;
            }

            seen.Add(key);
        }

        return changed;
    }

    public static bool HasEquivalentOnboardingIntroPrompt(IReadOnlyList<(string Role, string Text, DateTime Time)> messages, string? candidate) {
        if (messages is null || messages.Count == 0 || !IsLikelyOnboardingIntroPromptText(candidate)) {
            return false;
        }

        var candidateKey = NormalizePromptKey(candidate);
        if (candidateKey.Length == 0) {
            return false;
        }

        for (var i = 0; i < messages.Count; i++) {
            var message = messages[i];
            if (!string.Equals(message.Role, "Assistant", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            if (!IsLikelyOnboardingIntroPromptText(message.Text)) {
                continue;
            }

            if (string.Equals(NormalizePromptKey(message.Text), candidateKey, StringComparison.Ordinal)) {
                return true;
            }
        }

        return false;
    }

    public static bool HasAnyUserMessage(IReadOnlyList<(string Role, string Text, DateTime Time)> messages) {
        if (messages is null || messages.Count == 0) {
            return false;
        }

        for (var i = 0; i < messages.Count; i++) {
            if (string.Equals(messages[i].Role, "User", StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }

        return false;
    }

    public static bool HasEquivalentAssistantMessage(IReadOnlyList<(string Role, string Text, DateTime Time)> messages, string? candidate) {
        if (messages is null || messages.Count == 0) {
            return false;
        }

        var candidateKey = NormalizePromptKey(candidate);
        if (candidateKey.Length == 0) {
            return false;
        }

        for (var i = 0; i < messages.Count; i++) {
            var message = messages[i];
            if (!string.Equals(message.Role, "Assistant", StringComparison.OrdinalIgnoreCase)) {
                continue;
            }

            if (string.Equals(NormalizePromptKey(message.Text), candidateKey, StringComparison.Ordinal)) {
                return true;
            }
        }

        return false;
    }

    private static string NormalizePromptKey(string? text) {
        var normalized = (text ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0) {
            return string.Empty;
        }

        var sb = new System.Text.StringBuilder(normalized.Length);
        var previousWasSpace = false;
        for (var i = 0; i < normalized.Length; i++) {
            var ch = normalized[i];
            if (char.IsLetterOrDigit(ch)) {
                sb.Append(ch);
                previousWasSpace = false;
                continue;
            }

            if (char.IsWhiteSpace(ch)) {
                if (!previousWasSpace) {
                    sb.Append(' ');
                    previousWasSpace = true;
                }
            }
        }

        return sb.ToString().Trim();
    }
}
