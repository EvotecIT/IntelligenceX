using System;
using System.Text.RegularExpressions;

namespace IntelligenceX.Chat.App.Conversation;

/// <summary>
/// Structured assistant turn outcome contract for rendering non-success text.
/// </summary>
internal readonly record struct AssistantTurnOutcome(AssistantTurnOutcomeKind Kind, string? Detail = null) {
    public static AssistantTurnOutcome Canceled() => new(AssistantTurnOutcomeKind.Canceled);

    public static AssistantTurnOutcome Disconnected() => new(AssistantTurnOutcomeKind.Disconnected);

    public static AssistantTurnOutcome UsageLimit(string? detail) => new(AssistantTurnOutcomeKind.UsageLimit, detail);

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

    public static string Format(AssistantTurnOutcome outcome) {
        return outcome.Kind switch {
            AssistantTurnOutcomeKind.Canceled => "[canceled] Turn canceled.",
            AssistantTurnOutcomeKind.Disconnected => "[error] Disconnected.",
            AssistantTurnOutcomeKind.UsageLimit => "[limit] " + FormatUsageLimitDetail(outcome.Detail),
            AssistantTurnOutcomeKind.ToolRoundLimit => FormatToolRoundLimit(outcome.Detail),
            AssistantTurnOutcomeKind.Error => "[error] " + FormatErrorDetail(outcome.Detail),
            _ => "[error] Unknown assistant failure."
        };
    }

    private static string FormatErrorDetail(string? detail) {
        var normalized = (detail ?? string.Empty).Trim();
        return normalized.Length == 0 ? "Unknown error." : normalized;
    }

    private static string FormatUsageLimitDetail(string? detail) {
        var normalized = (detail ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return "Usage limit reached. Try again later or switch account.";
        }

        return normalized;
    }

    private static string FormatToolRoundLimit(string? detail) {
        var maxRounds = ExtractMaxRounds(detail);
        var roundsText = maxRounds.HasValue ? maxRounds.Value.ToString() : "current";

        return
            "I hit the in-session tool safety limit while chaining checks"
            + " (max rounds: " + roundsText + ").\n\n"
            + "I can continue right away if we narrow one step:\n"
            + "1. Ask for one target query first (for example one DC / one group / one OU).\n"
            + "2. Say \"continue\" and I’ll keep going from discovered context.\n"
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
}
