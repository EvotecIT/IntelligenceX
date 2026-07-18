using System;
using System.Collections.Generic;

namespace IntelligenceX.Chat.App.Conversation;

/// <summary>
/// Normalizes structured desktop profile values identically for every shell.
/// </summary>
internal static class DesktopChatProfileNormalizer {
    private const int MaximumUserNameLength = 48;
    private const int MaximumPersonaLength = 180;

    internal static string? NormalizeUserName(string? value) {
        var normalized = NormalizeSingleLine(value);
        if (IsEmptyProfileMarker(normalized)) {
            return null;
        }

        normalized = TrimProfilePunctuation(normalized!);
        return Clamp(normalized, MaximumUserNameLength);
    }

    internal static string? NormalizeAssistantPersona(string? value) {
        var normalized = NormalizeSingleLine(value);
        if (IsEmptyProfileMarker(normalized)) {
            return null;
        }

        normalized = TrimProfilePunctuation(normalized!);
        return Clamp(normalized, MaximumPersonaLength);
    }

    /// <summary>
    /// Identifies profile fields that still need to be collected before onboarding can complete.
    /// </summary>
    internal static List<string> GetMissingOnboardingFields(
        string? effectiveUserName,
        string? effectiveAssistantPersona,
        string? effectiveThemePreset,
        bool onboardingCompleted) {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(effectiveUserName)) {
            missing.Add("userName");
        }
        if (string.IsNullOrWhiteSpace(effectiveAssistantPersona)) {
            missing.Add("assistantPersona");
        }
        if (string.IsNullOrWhiteSpace(effectiveThemePreset)
            || (!onboardingCompleted && string.Equals(effectiveThemePreset, "default", StringComparison.OrdinalIgnoreCase))) {
            missing.Add("themePreset");
        }
        return missing;
    }

    /// <summary>
    /// Resolves legacy unspecified profile updates consistently for every desktop shell.
    /// </summary>
    internal static ProfileUpdateScope ResolveEffectiveUpdateScope(OnboardingProfileUpdate update) {
        ArgumentNullException.ThrowIfNull(update);

        if (update.Scope != ProfileUpdateScope.Unspecified) {
            return update.Scope;
        }

        var completesOnboardingWithProfileFields = update.HasOnboardingCompleted
                                                  && update.OnboardingCompleted
                                                  && (update.HasUserName
                                                      || update.HasAssistantPersona
                                                      || update.HasThemePreset);
        return completesOnboardingWithProfileFields
            ? ProfileUpdateScope.Profile
            : ProfileUpdateScope.Session;
    }

    private static string? NormalizeSingleLine(string? value) {
        var normalized = (value ?? string.Empty)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        return normalized.Length == 0 ? null : normalized;
    }

    private static bool IsEmptyProfileMarker(string? value) {
        return string.IsNullOrWhiteSpace(value)
               || value.Equals("skip", StringComparison.OrdinalIgnoreCase)
               || value.Equals("default", StringComparison.OrdinalIgnoreCase)
               || value.Equals("defaults", StringComparison.OrdinalIgnoreCase);
    }

    private static string TrimProfilePunctuation(string value) {
        var trimmed = value.Trim().Trim('.', '!', '?', ',', ';', ':', '"', '\'');
        return trimmed.Length == 0 ? value.Trim() : trimmed;
    }

    private static string? Clamp(string value, int maximumLength) {
        var normalized = value.Length > maximumLength
            ? value[..maximumLength].TrimEnd()
            : value;
        return normalized.Length == 0 ? null : normalized;
    }
}
