using System;

namespace IntelligenceX.OpenAI.Chat;

public enum ReasoningEffort {
    Minimal,
    Low,
    Medium,
    High,
    XHigh
}

public enum ReasoningSummary {
    Auto,
    Concise,
    Detailed,
    Off
}

public enum TextVerbosity {
    Low,
    Medium,
    High
}

public static class ChatEnumParser {
    public static ReasoningEffort? ParseReasoningEffort(string? value) {
        return TryParseEnum(value, out ReasoningEffort result) ? result : null;
    }

    public static ReasoningSummary? ParseReasoningSummary(string? value) {
        return TryParseEnum(value, out ReasoningSummary result) ? result : null;
    }

    public static TextVerbosity? ParseTextVerbosity(string? value) {
        return TryParseEnum(value, out TextVerbosity result) ? result : null;
    }

    private static bool TryParseEnum<T>(string? value, out T result) where T : struct {
        result = default;
        if (string.IsNullOrWhiteSpace(value)) {
            return false;
        }
        var normalized = value.Trim();
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
