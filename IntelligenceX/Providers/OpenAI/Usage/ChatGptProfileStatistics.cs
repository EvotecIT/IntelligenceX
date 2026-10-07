using System;
using System.Collections.Generic;
using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.Usage;

/// <summary>Provider-calculated profile statistics and token activity, across the account's devices.</summary>
public sealed class ChatGptProfileStatistics {
    private ChatGptProfileStatistics(JsonObject obj) {
        var stats = obj.GetObject("stats") ?? new JsonObject();
        var profile = obj.GetObject("profile");
        DisplayName = profile?.GetString("display_name");
        Username = profile?.GetString("username");
        StatsAsOf = obj.GetObject("metadata")?.GetString("stats_as_of");
        GeneratedAt = ChatGptResetCredits.ReadTimestamp(obj.GetObject("metadata"), "generated_at");
        StatsError = obj.GetObject("metadata")?.GetString("stats_error");
        LifetimeTokens = stats.GetInt64("lifetime_tokens");
        PeakDailyTokens = stats.GetInt64("peak_daily_tokens");
        CurrentStreakDays = stats.GetInt64("current_streak_days");
        LongestStreakDays = stats.GetInt64("longest_streak_days");
        TotalThreads = stats.GetInt64("total_threads");
        LongestRunningTurnSeconds = stats.GetInt64("longest_running_turn_sec");
        FastModeUsagePercent = ChatGptAnalyticsJson.Number(stats, "fast_mode_usage_percentage");
        TotalSkillsUsed = stats.GetInt64("total_skills_used");
        UniqueSkillsUsed = stats.GetInt64("unique_skills_used");
        MostUsedReasoningEffortPercent = ChatGptAnalyticsJson.Number(stats, "most_used_reasoning_effort_percentage");
        MostUsedReasoningEffort = stats.GetString("most_used_reasoning_effort");
        DailyUsage = ChatGptAnalyticsJson.Rows(stats, "daily_usage_buckets", static row => new ChatGptTokenActivityBucket(row));
        WeeklyUsage = ChatGptAnalyticsJson.Rows(stats, "weekly_usage_buckets", static row => new ChatGptTokenActivityBucket(row));
        CumulativeDailyUsage = ChatGptAnalyticsJson.Rows(stats, "cumulative_daily_usage_buckets", static row => new ChatGptTokenActivityBucket(row));
        TopInvocations = ChatGptAnalyticsJson.Rows(stats, "top_invocations", static row => new ChatGptProfileInvocation(row));
    }
    /// <summary>Provider profile display name, when present.</summary>
    public string? DisplayName { get; }
    /// <summary>Provider profile username, when present.</summary>
    public string? Username { get; }
    /// <summary>Provider statistics date; does not imply real-time reporting.</summary>
    public string? StatsAsOf { get; }
    /// <summary>Provider snapshot generation timestamp.</summary>
    public DateTimeOffset? GeneratedAt { get; }
    /// <summary>Provider-declared statistics error, when reported.</summary>
    public string? StatsError { get; }
    /// <summary>Provider-reported lifetime tokens. Null means unreported.</summary>
    public long? LifetimeTokens { get; }
    /// <summary>Largest provider-reported daily token count. Null means unreported.</summary>
    public long? PeakDailyTokens { get; }
    /// <summary>Current activity streak in days. Null means unreported.</summary>
    public long? CurrentStreakDays { get; }
    /// <summary>Longest activity streak in days. Null means unreported.</summary>
    public long? LongestStreakDays { get; }
    /// <summary>Provider-reported thread count. Null means unreported.</summary>
    public long? TotalThreads { get; }
    /// <summary>Longest running turn in seconds. Null means unreported.</summary>
    public long? LongestRunningTurnSeconds { get; }
    /// <summary>Percentage of runs using fast mode. Null means unreported.</summary>
    public double? FastModeUsagePercent { get; }
    /// <summary>Total skill invocations. Null means unreported.</summary>
    public long? TotalSkillsUsed { get; }
    /// <summary>Number of distinct skills used. Null means unreported.</summary>
    public long? UniqueSkillsUsed { get; }
    /// <summary>Share of the most-used reasoning effort. Null means unreported.</summary>
    public double? MostUsedReasoningEffortPercent { get; }
    /// <summary>Most-used provider reasoning effort.</summary>
    public string? MostUsedReasoningEffort { get; }
    /// <summary>Provider daily token activity buckets.</summary>
    public IReadOnlyList<ChatGptTokenActivityBucket> DailyUsage { get; }
    /// <summary>Provider weekly token activity buckets.</summary>
    public IReadOnlyList<ChatGptTokenActivityBucket> WeeklyUsage { get; }
    /// <summary>Provider cumulative token activity buckets.</summary>
    public IReadOnlyList<ChatGptTokenActivityBucket> CumulativeDailyUsage { get; }
    /// <summary>Returned top skill or plugin invocations; not necessarily a complete ranking.</summary>
    public IReadOnlyList<ChatGptProfileInvocation> TopInvocations { get; }
    /// <summary>Parses the provider profile response.</summary>
    public static ChatGptProfileStatistics FromJson(JsonObject obj) => new(obj);
}

/// <summary>A dated provider token count, preserving integer precision.</summary>
public sealed class ChatGptTokenActivityBucket {
    internal ChatGptTokenActivityBucket(JsonObject obj) { StartDate = obj.GetString("start_date"); Tokens = obj.GetInt64("tokens"); }
    /// <summary>Provider bucket date, without conversion to a local timezone.</summary>
    public string? StartDate { get; }
    /// <summary>Provider token count, or null when unreported.</summary>
    public long? Tokens { get; }
}

/// <summary>A returned top skill or plugin invocation counter.</summary>
public sealed class ChatGptProfileInvocation {
    internal ChatGptProfileInvocation(JsonObject obj) {
        Type = obj.GetString("type"); PluginName = obj.GetString("plugin_name");
        SkillName = obj.GetString("skill_name"); UsageCount = obj.GetInt64("usage_count");
    }
    /// <summary>Provider invocation kind.</summary>
    public string? Type { get; }
    /// <summary>Provider plugin name.</summary>
    public string? PluginName { get; }
    /// <summary>Provider skill name.</summary>
    public string? SkillName { get; }
    /// <summary>Invocation count, when reported.</summary>
    public long? UsageCount { get; }
}
