using System;

namespace IntelligenceX.Tools.Common;

/// <summary>
/// Shared text helpers for tool implementations.
/// </summary>
public static class ToolText {
    /// <summary>
    /// Truncates a string to <paramref name="maxChars"/> characters.
    /// </summary>
    /// <param name="value">Input string.</param>
    /// <param name="maxChars">Maximum characters to keep. When 0 or less, no truncation is applied.</param>
    /// <returns>Truncated string; or the original value when already within the limit.</returns>
    public static string? Truncate(string? value, int maxChars) {
        if (string.IsNullOrEmpty(value)) {
            return value;
        }
        if (maxChars <= 0 || value.Length <= maxChars) {
            return value;
        }
        return value.Substring(0, maxChars);
    }
}
