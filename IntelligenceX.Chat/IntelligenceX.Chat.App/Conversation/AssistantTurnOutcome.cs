using System;
using System.Text.RegularExpressions;

namespace IntelligenceX.Chat.App.Conversation;

/// <summary>
/// Structured assistant turn outcome contract for rendering non-success text.
/// </summary>
internal readonly record struct AssistantTurnOutcome(AssistantTurnOutcomeKind Kind, string? Detail = null, string? ContextLabel = null) {
    public static AssistantTurnOutcome Canceled() => new(AssistantTurnOutcomeKind.Canceled);

    public static AssistantTurnOutcome Disconnected() => new(AssistantTurnOutcomeKind.Disconnected);

    public static AssistantTurnOutcome UsageLimit(string? detail, string? accountLabel = null) =>
        new(AssistantTurnOutcomeKind.UsageLimit, detail, accountLabel);

    public static AssistantTurnOutcome ToolRoundLimit(string? detail, int? maxRounds = null) =>
        new(AssistantTurnOutcomeKind.ToolRoundLimit, BuildToolRoundLimitDetail(detail, maxRounds));

    public static AssistantTurnOutcome Error(string? detail) => new(AssistantTurnOutcomeKind.Error, detail);

    private static string BuildToolRoundLimitDetail(string? detail, int? maxRounds) {
        var normalized = (detail ?? string.Empty).Trim();
        if (maxRounds.HasValue && maxRounds.Value > 0 && normalized.Length == 0) {
            return maxRounds.Value.ToString();
        }

        if (!maxRounds.HasValue || maxRounds.Value <= 0 || normalized.Length > 0) {
            return normalized;
        }

        return maxRounds.Value.ToString();
    }
}

/// <summary>
/// Assistant turn outcome categories.
/// </summary>
internal enum AssistantTurnOutcomeKind {
    Canceled,
    Disconnected,
    UsageLimit,
    ToolRoundLimit,
    Error
}

/// <summary>
/// Renders assistant turn outcomes into transcript-safe text.
/// </summary>
internal static class AssistantTurnOutcomeFormatter {
    private static readonly Regex MaxRoundsRegex = new(@"\((?<value>\d+)\)", RegexOptions.Compiled);
    private static readonly Regex RetryAfterMinutesRegex = new(
        @"(?:in\s+about|about)\s+(?<minutes>\d+)\s+minute",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string Format(AssistantTurnOutcome outcome) {
        return outcome.Kind switch {
            AssistantTurnOutcomeKind.Canceled => "[canceled] Turn canceled.",
            AssistantTurnOutcomeKind.Disconnected => "[error] Disconnected.",
            AssistantTurnOutcomeKind.UsageLimit => "[limit] " + FormatUsageLimitDetail(outcome.Detail, outcome.ContextLabel),
            AssistantTurnOutcomeKind.ToolRoundLimit => FormatToolRoundLimit(outcome.Detail),
            AssistantTurnOutcomeKind.Error => "[error] " + FormatErrorDetail(outcome.Detail),
            _ => "[error] Unknown assistant failure."
        };
    }

    private static string FormatErrorDetail(string? detail) {
        var normalized = (detail ?? string.Empty).Trim();
        return normalized.Length == 0 ? "Unknown error." : normalized;
    }

    private static string FormatUsageLimitDetail(string? detail, string? accountLabel) {
        var normalized = (detail ?? string.Empty).Trim();
        var normalizedAccountLabel = (accountLabel ?? string.Empty).Trim();
        var accountText = normalizedAccountLabel.Length == 0
            ? "this account"
            : normalizedAccountLabel;
        if (normalized.Length == 0) {
            return
                "ChatGPT usage limit reached for " + accountText + ".\n\n"
                + "To continue now, open the top-right menu and choose **Switch Account**.";
        }

        var retryAfterMinutes = TryExtractRetryAfterMinutes(normalized);
        if (retryAfterMinutes.HasValue && retryAfterMinutes.Value > 0) {
            return
                "ChatGPT usage limit reached for " + accountText + ".\n\n"
                + "To continue now, open the top-right menu and choose **Switch Account**.\n"
                + "If you stay on " + accountText + ", retry in about "
                + FormatRetryDelay(retryAfterMinutes.Value)
                + ".";
        }

        return
            "ChatGPT usage limit reached for " + accountText + ".\n\n"
            + "To continue now, open the top-right menu and choose **Switch Account**.\n"
            + "Details: " + normalized;
    }

    private static string FormatToolRoundLimit(string? detail) {
        var maxRounds = ExtractMaxRounds(detail);
        var roundsText = maxRounds.HasValue ? maxRounds.Value.ToString() : "current";

        return
            "[warning] Tool safety limit reached.\n\n"
            + "I hit the in-session tool safety limit while chaining checks"
            + " (max rounds: " + roundsText + ").\n\n"
            + "I can continue right away if we narrow one step:\n"
            + "1. Ask for one target query first (for example one DC / one group / one OU).\n"
            + "2. Share your preferred next step and I’ll keep going from discovered context.\n"
            + "3. If needed, share explicit scope (domain controller or Base DN) to reduce retries.";
    }

    private static int? ExtractMaxRounds(string? detail) {
        var normalized = (detail ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return null;
        }

        if (int.TryParse(normalized, out var direct) && direct > 0) {
            return direct;
        }

        var match = MaxRoundsRegex.Match(normalized);
        if (!match.Success) {
            return null;
        }

        return int.TryParse(match.Groups["value"].Value, out var parsed) && parsed > 0
            ? parsed
            : null;
    }

    private static int? TryExtractRetryAfterMinutes(string detail) {
        if (string.IsNullOrWhiteSpace(detail)) {
            return null;
        }

        var match = RetryAfterMinutesRegex.Match(detail);
        if (!match.Success) {
            return null;
        }

        return int.TryParse(match.Groups["minutes"].Value, out var minutes) && minutes > 0
            ? minutes
            : null;
    }

    private static string FormatRetryDelay(int minutes) {
        if (minutes < 60) {
            return minutes.ToString() + " minute(s)";
        }

        var days = minutes / (24 * 60);
        var remainderAfterDays = minutes % (24 * 60);
        var hours = remainderAfterDays / 60;
        var mins = remainderAfterDays % 60;

        if (days <= 0) {
            return mins > 0
                ? hours.ToString() + "h " + mins.ToString() + "m"
                : hours.ToString() + " hour(s)";
        }

        if (hours == 0 && mins == 0) {
            return days.ToString() + " day(s)";
        }

        if (mins == 0) {
            return days.ToString() + "d " + hours.ToString() + "h";
        }

        return days.ToString() + "d " + hours.ToString() + "h " + mins.ToString() + "m";
    }
}
