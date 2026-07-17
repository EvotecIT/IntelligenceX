using System;

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
