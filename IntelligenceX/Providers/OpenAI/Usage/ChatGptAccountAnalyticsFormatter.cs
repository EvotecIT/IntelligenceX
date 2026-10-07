using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace IntelligenceX.OpenAI.Usage;

/// <summary>Shared text presentation of provider account measurements and their coverage.</summary>
public static class ChatGptAccountAnalyticsFormatter {
    /// <summary>Formats account-wide data without combining it with machine-local token estimates.</summary>
    public static string Format(ChatGptAccountAnalytics? analytics) {
        if (analytics is null) return "Codex account analytics: not reported";
        var lines = new List<string> { "Codex account analytics · provider reported" };
        if (analytics.Profile is { } profile) {
            lines.Add("Profile as of: " + (profile.StatsAsOf ?? "unknown")
                + " · generated " + ChatGptResetCreditsFormatter.FormatTimestamp(profile.GeneratedAt));
            AddCount("Lifetime tokens", profile.LifetimeTokens);
            AddCount("Peak daily tokens", profile.PeakDailyTokens);
            AddCount("Longest turn (seconds)", profile.LongestRunningTurnSeconds);
            AddCount("Current streak (days)", profile.CurrentStreakDays);
            AddCount("Longest streak (days)", profile.LongestStreakDays);
            AddCount("Unique skills", profile.UniqueSkillsUsed);
            AddCount("Skill uses", profile.TotalSkillsUsed);
            if (profile.FastModeUsagePercent.HasValue) lines.Add("Fast mode: " + Percent(profile.FastModeUsagePercent) + "% of runs");
            if (profile.MostUsedReasoningEffort is not null)
                lines.Add("Most used reasoning: " + profile.MostUsedReasoningEffort + " · " + Percent(profile.MostUsedReasoningEffortPercent) + "%");
        }
        if (analytics.DailyUsage is { } daily) {
            lines.Add("Daily account usage: " + daily.Data.Count.ToString(CultureInfo.InvariantCulture)
                + " buckets · units " + (daily.Units ?? "unknown"));
            lines.Add("Daily data as of: " + ChatGptResetCreditsFormatter.FormatTimestamp(daily.DataFreshness));
            var features = daily.Data.SelectMany(static day => day.Attribution)
                .Where(static row => row.Value.HasValue).GroupBy(static row => row.ThreadSource ?? "unknown")
                .Select(static group => new { Key = group.Key, Value = group.Sum(static row => row.Value!.Value) })
                .OrderByDescending(static group => group.Value).ToArray();
            var total = features.Sum(static group => group.Value);
            if (total > 0) foreach (var feature in features)
                lines.Add(feature.Key + ": " + (100 * feature.Value / total).ToString("0.####", CultureInfo.InvariantCulture)
                    + "% of reported usage in this range");
        }
        if (analytics.PlanHistory is { } history) {
            lines.Add("Plan accounting as of: " + ChatGptResetCreditsFormatter.FormatTimestamp(history.DataAsOf));
            if (history.CoverageComplete != true || history.Approximate != false)
                lines.Add("Plan accounting coverage is partial, approximate or unconfirmed.");
            foreach (var period in history.Periods) {
                lines.Add(ChatGptResetCreditsFormatter.FormatTimestamp(period.StartsAt) + " – "
                    + ChatGptResetCreditsFormatter.FormatTimestamp(period.EndsAt) + ": " + Percent(period.UsedPercent)
                    + "% of " + (period.WindowMinutes?.ToString(CultureInfo.InvariantCulture) ?? "unknown") + "-minute allowance"
                    + (period.AccountingComplete == true ? "" : " · incomplete accounting"));
            }
        }
        foreach (var error in analytics.Errors) lines.Add(error.Message ?? "Some provider analytics are unavailable.");
        return string.Join(Environment.NewLine, lines);
        void AddCount(string label, long? value) {
            if (value.HasValue) lines.Add(label + ": " + value.Value.ToString("N0", CultureInfo.InvariantCulture));
        }
    }
    private static string Percent(double? value) => value?.ToString("0.####", CultureInfo.InvariantCulture) ?? "unknown";
}
