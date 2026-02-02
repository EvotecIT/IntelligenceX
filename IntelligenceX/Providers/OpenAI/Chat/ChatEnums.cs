using System;

namespace IntelligenceX.OpenAI.Chat;

/// <summary>
/// Indicates the desired reasoning effort.
/// </summary>
public enum ReasoningEffort {
    /// <summary>
    /// Minimal reasoning effort.
    /// </summary>
    Minimal,
    /// <summary>
    /// Low reasoning effort.
    /// </summary>
    Low,
    /// <summary>
    /// Medium reasoning effort.
    /// </summary>
    Medium,
    /// <summary>
    /// High reasoning effort.
    /// </summary>
    High,
    /// <summary>
    /// Extra-high reasoning effort.
    /// </summary>
    XHigh
}

/// <summary>
/// Indicates the desired reasoning summary level.
/// </summary>
public enum ReasoningSummary {
    /// <summary>
    /// Automatically choose summary level.
    /// </summary>
    Auto,
    /// <summary>
    /// Concise summary.
    /// </summary>
    Concise,
    /// <summary>
    /// Detailed summary.
    /// </summary>
    Detailed,
    /// <summary>
    /// Disable summaries.
    /// </summary>
    Off
}

/// <summary>
/// Indicates the desired text verbosity level.
/// </summary>
public enum TextVerbosity {
    /// <summary>
    /// Low verbosity.
    /// </summary>
    Low,
    /// <summary>
    /// Medium verbosity.
    /// </summary>
    Medium,
    /// <summary>
    /// High verbosity.
    /// </summary>
    High
}

/// <summary>
/// Helpers for parsing enum values from strings.
/// </summary>
public static class ChatEnumParser {
    /// <summary>
    /// Parses a reasoning effort from a string.
    /// </summary>
    /// <param name="value">Input string.</param>
    public static ReasoningEffort? ParseReasoningEffort(string? value) {
        return TryParseEnum(value, out ReasoningEffort result) ? result : null;
    }

    /// <summary>
    /// Parses a reasoning summary from a string.
    /// </summary>
    /// <param name="value">Input string.</param>
    public static ReasoningSummary? ParseReasoningSummary(string? value) {
        return TryParseEnum(value, out ReasoningSummary result) ? result : null;
    }

    /// <summary>
    /// Parses a text verbosity from a string.
    /// </summary>
    /// <param name="value">Input string.</param>
    public static TextVerbosity? ParseTextVerbosity(string? value) {
        return TryParseEnum(value, out TextVerbosity result) ? result : null;
    }

    private static bool TryParseEnum<T>(string? value, out T result) where T : struct {
        result = default;
        if (string.IsNullOrWhiteSpace(value)) {
            return false;
        }
        var normalized = value!.Trim();
        normalized = normalized.Replace("-", string.Empty).Replace("_", string.Empty);
        return Enum.TryParse(normalized, true, out result);
    }
}

internal static class ChatEnumExtensions {
    public static string ToApiString(this ReasoningEffort effort) {
        return effort switch {
            ReasoningEffort.Minimal => "minimal",
            ReasoningEffort.Low => "low",
            ReasoningEffort.Medium => "medium",
            ReasoningEffort.High => "high",
            ReasoningEffort.XHigh => "xhigh",
            _ => "medium"
        };
    }

    public static string ToApiString(this ReasoningSummary summary) {
        return summary switch {
            ReasoningSummary.Auto => "auto",
            ReasoningSummary.Concise => "concise",
            ReasoningSummary.Detailed => "detailed",
            ReasoningSummary.Off => "off",
            _ => "auto"
        };
    }

    public static string ToApiString(this TextVerbosity verbosity) {
        return verbosity switch {
            TextVerbosity.Low => "low",
            TextVerbosity.Medium => "medium",
            TextVerbosity.High => "high",
            _ => "medium"
        };
    }
}
