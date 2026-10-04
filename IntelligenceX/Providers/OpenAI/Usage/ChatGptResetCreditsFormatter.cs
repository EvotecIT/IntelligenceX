using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace IntelligenceX.OpenAI.Usage;

/// <summary>Readable provider reset evidence for CLI and desktop account surfaces.</summary>
public static class ChatGptResetCreditsFormatter {
    /// <summary>Formats an exact instant in the supplied timezone and in UTC, with seconds and offsets.</summary>
    public static string FormatTimestamp(DateTimeOffset? value, TimeZoneInfo? timeZone = null) {
        if (!value.HasValue) return "Not reported";
        var local = TimeZoneInfo.ConvertTime(value.Value, timeZone ?? TimeZoneInfo.Local);
        return local.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF zzz", CultureInfo.InvariantCulture)
            + " (" + value.Value.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF 'UTC'", CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>Formats counts, grant lifecycle times and the returned retained-history page, preserving unknown data.</summary>
    public static string Format(ChatGptResetCredits? resets, string? error = null, TimeZoneInfo? timeZone = null) {
        var lines = new List<string>();
        if (resets is null) {
            lines.Add("Reset credits: not reported");
        } else {
            lines.Add("Provider reset credits: " + (resets.AvailableCount?.ToString(CultureInfo.InvariantCulture) ?? "unknown") + " available");
            if (resets.ApplicableAvailableCount.HasValue) lines.Add("Applicable now: " + resets.ApplicableAvailableCount.Value);
            if (!resets.DetailsAvailable) lines.Add("Grant expiry details: unavailable");
            foreach (var credit in resets.Credits.OrderBy(static c => c.ExpiresAt ?? DateTimeOffset.MaxValue)) {
                lines.Add((credit.Title ?? credit.ResetType ?? "Reset") + " · " + (credit.Status ?? "status unknown"));
                lines.Add("  Expires: " + FormatTimestamp(credit.ExpiresAt, timeZone));
                lines.Add("  Granted: " + FormatTimestamp(credit.GrantedAt, timeZone));
                if (credit.IsSupportedByPlan == false) lines.Add("  Not supported by the current plan");
                if (credit.RedeemStartedAt.HasValue) lines.Add("  Redemption started: " + FormatTimestamp(credit.RedeemStartedAt, timeZone));
                if (credit.RedeemedAt.HasValue) lines.Add("  Redeemed: " + FormatTimestamp(credit.RedeemedAt, timeZone));
            }
            if (resets.HistoryAvailable) {
                lines.Add("Reset history · " + FormatTimestamp(resets.HistoryWindowStart, timeZone) + " through " + FormatTimestamp(resets.HistoryAsOf, timeZone));
                if (resets.History.Count == 0) lines.Add("  No events in the returned history page");
                foreach (var item in resets.History) lines.Add("  " + (item.Kind ?? "Unknown event") + " · " + FormatTimestamp(item.OccurredAt, timeZone));
                if (!string.IsNullOrWhiteSpace(resets.HistoryNextCursor)) lines.Add("  More history is available from the provider (next_cursor).");
            } else {
                lines.Add("Reset history: unavailable");
            }
        }
        if (!string.IsNullOrWhiteSpace(error)) lines.Add(error!);
        return string.Join(Environment.NewLine, lines);
    }
}
