using System;
using System.Globalization;

namespace IntelligenceX.Visualization.Heatmaps;

/// <summary>
/// Provides shared formatting helpers for heatmap/report copy.
/// </summary>
public static class HeatmapDisplayText {
    /// <summary>
    /// Formats a caller-provided count label with the correct singular or plural noun.
    /// </summary>
    /// <param name="countText">Already formatted numeric count text, such as <c>1.23K</c>.</param>
    /// <param name="count">Raw numeric count used to choose singular or plural.</param>
    /// <param name="singular">Singular noun used when the count is one.</param>
    /// <param name="plural">Plural noun used for all other counts.</param>
    /// <returns>A count label such as <c>1 repository</c> or <c>1.23K repositories</c>.</returns>
    public static string FormatCount(string countText, long count, string singular, string plural) {
        if (string.IsNullOrWhiteSpace(countText)) {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(countText));
        }

        return countText.Trim() + " " + ResolveNoun(count, singular, plural);
    }

    /// <summary>
    /// Formats a numeric count with the correct singular or plural noun.
    /// </summary>
    /// <param name="count">Numeric count to render.</param>
    /// <param name="singular">Singular noun used when the count is one.</param>
    /// <param name="plural">Plural noun used for all other counts.</param>
    /// <returns>A count label such as <c>1 day</c> or <c>2 days</c>.</returns>
    public static string FormatCount(long count, string singular, string plural) {
        return count.ToString(CultureInfo.InvariantCulture) + " " + ResolveNoun(count, singular, plural);
    }

    /// <summary>
    /// Formats a streak-style day count label.
    /// </summary>
    /// <param name="count">Day count to render.</param>
    /// <returns>A label such as <c>1 day</c> or <c>85 days</c>.</returns>
    public static string FormatDays(int count) {
        return FormatCount(count, "day", "days");
    }

    /// <summary>
    /// Formats an active-day count label.
    /// </summary>
    /// <param name="count">Active day count to render.</param>
    /// <returns>A label such as <c>1 active day</c> or <c>30 active days</c>.</returns>
    public static string FormatActiveDays(int count) {
        return FormatCount(count, "active day", "active days");
    }

    /// <summary>
    /// Formats a compact elapsed-duration label using hour, minute, and second units.
    /// </summary>
    /// <param name="duration">Elapsed duration to render.</param>
    /// <returns>A label such as <c>45s</c>, <c>12m 30s</c>, or <c>2h 15m</c>.</returns>
    public static string FormatDuration(TimeSpan duration) {
        if (duration < TimeSpan.Zero) {
            duration = TimeSpan.Zero;
        }

        if (duration.TotalHours >= 1d) {
            return ((int)duration.TotalHours).ToString(CultureInfo.InvariantCulture)
                   + "h "
                   + duration.Minutes.ToString(CultureInfo.InvariantCulture)
                   + "m";
        }

        if (duration.TotalMinutes >= 1d) {
            return ((int)duration.TotalMinutes).ToString(CultureInfo.InvariantCulture)
                   + "m "
                   + duration.Seconds.ToString(CultureInfo.InvariantCulture)
                   + "s";
        }

        return Math.Max(0, (int)Math.Round(duration.TotalSeconds, MidpointRounding.AwayFromZero))
               .ToString(CultureInfo.InvariantCulture)
               + "s";
    }

    /// <summary>
    /// Formats a date range used in report subtitles and section headers.
    /// </summary>
    /// <param name="startDayUtc">Inclusive range start.</param>
    /// <param name="endDayUtc">Inclusive range end.</param>
    /// <param name="emptyLabel">Fallback label when the range is missing.</param>
    /// <returns>A label such as <c>2026-01-01 to 2026-01-31</c>.</returns>
    public static string FormatDateRange(DateTime? startDayUtc, DateTime? endDayUtc, string emptyLabel = "No range") {
        if (!startDayUtc.HasValue || !endDayUtc.HasValue) {
            return string.IsNullOrWhiteSpace(emptyLabel) ? "No range" : emptyLabel.Trim();
        }

        return startDayUtc.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
               + " to "
               + endDayUtc.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Formats a trailing month-window label for monthly charts.
    /// </summary>
    /// <param name="months">Number of months in the window.</param>
    /// <returns>A label such as <c>Trailing 13 months</c>.</returns>
    public static string FormatTrailingMonthWindow(int months) {
        return "Trailing " + FormatCount(months, "month", "months");
    }

    /// <summary>
    /// Joins short summary segments with a presentation-friendly separator.
    /// </summary>
    /// <param name="parts">Summary segments to join.</param>
    /// <returns>A display string such as <c>2000 tokens · 2 active days · peak 2026-03-10 (1200)</c>.</returns>
    public static string JoinSummaryParts(params string?[] parts) {
        if (parts is null || parts.Length == 0) {
            return string.Empty;
        }

        return string.Join(" · ", parts.Where(static part => !string.IsNullOrWhiteSpace(part)).Select(static part => part!.Trim()));
    }

    private static string ResolveNoun(long count, string singular, string plural) {
        if (string.IsNullOrWhiteSpace(singular)) {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(singular));
        }

        if (string.IsNullOrWhiteSpace(plural)) {
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(plural));
        }

        return Math.Abs(count) == 1L ? singular.Trim() : plural.Trim();
    }
}
