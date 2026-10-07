using System;
using System.Collections.Generic;
using System.Globalization;
using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.Usage;

/// <summary>A root thread and explicitly known descendants for a provider lifetime-usage query.</summary>
public sealed class ChatGptThreadUsageRequest {
    /// <summary>Creates a query group. Supply all known descendants for complete task accounting.</summary>
    public ChatGptThreadUsageRequest(string threadId, DateTimeOffset? createdAt = null, IReadOnlyList<string>? descendantThreadIds = null) {
        ThreadId = threadId ?? throw new ArgumentNullException(nameof(threadId));
        CreatedAt = createdAt;
        DescendantThreadIds = descendantThreadIds ?? Array.Empty<string>();
    }
    /// <summary>Provider root thread identifier.</summary>
    public string ThreadId { get; }
    /// <summary>Root creation time, when known.</summary>
    public DateTimeOffset? CreatedAt { get; }
    /// <summary>Descendants belonging only to this root; groups must not overlap.</summary>
    public IReadOnlyList<string> DescendantThreadIds { get; }
    internal static JsonObject BuildQuery(IReadOnlyList<ChatGptThreadUsageRequest> groups) {
        if (groups is null) throw new ArgumentNullException(nameof(groups));
        if (groups.Count is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(groups), "Query between 1 and 100 root threads.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var rows = new JsonArray();
        foreach (var group in groups) {
            if (group is null) throw new ArgumentException("Thread groups cannot contain null.", nameof(groups));
            AddId(group.ThreadId);
            var descendants = new JsonArray();
            foreach (var id in group.DescendantThreadIds) { AddId(id); descendants.Add(JsonValue.From(id)); }
            rows.Add(JsonValue.From(new JsonObject().Add("thread_id", group.ThreadId)
                .Add("created_at", group.CreatedAt?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))
                .Add("descendant_thread_ids", descendants)));
        }
        return new JsonObject().Add("threads", rows);
        void AddId(string id) {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Thread identifiers cannot be empty.", nameof(groups));
            if (!seen.Add(id)) throw new ArgumentException("Thread usage groups overlap.", nameof(groups));
            if (seen.Count > 1000) throw new ArgumentOutOfRangeException(nameof(groups), "Query at most 1,000 thread identifiers.");
        }
    }
}

/// <summary>Provider lifetime consumption for explicit threads, independent of daily-chart date ranges.</summary>
public sealed class ChatGptThreadUsage {
    private readonly JsonObject _raw;
    private ChatGptThreadUsage(JsonObject obj) {
        _raw = obj;
        DataAsOf = ChatGptResetCredits.ReadTimestamp(obj, "data_as_of");
        Threads = ChatGptAnalyticsJson.Rows(obj, "threads", static row => new ChatGptThreadUsageEntry(row));
    }
    /// <summary>Provider data freshness timestamp.</summary>
    public DateTimeOffset? DataAsOf { get; }
    /// <summary>Measurements, including partial or unavailable entries.</summary>
    public IReadOnlyList<ChatGptThreadUsageEntry> Threads { get; }
    /// <summary>Parses a provider thread usage query response.</summary>
    public static ChatGptThreadUsage FromJson(JsonObject obj) => new(obj);
    /// <summary>Returns original provider fields, including comparison-plan metadata.</summary>
    public JsonObject ToJson() => _raw;
}

/// <summary>Allowance equivalents and actual credit debits, including unknown metrics.</summary>
public class ChatGptThreadUsageMetrics {
    internal ChatGptThreadUsageMetrics(JsonObject obj) {
        FiveHourLimitPercent = ChatGptAnalyticsJson.Number(obj, "five_hour_limit_percent");
        WeeklyLimitPercent = ChatGptAnalyticsJson.Number(obj, "weekly_limit_percent");
        BalanceUsageCredits = ChatGptAnalyticsJson.Number(obj, "balance_usage_credits");
        Model = obj.GetString("model"); ReasoningEffort = obj.GetString("reasoning_effort");
        Speed = obj.GetString("speed"); ProductExperience = obj.GetString("product_experience");
    }
    /// <summary>Lifetime consumption compared with the current full five-hour allowance.</summary>
    public double? FiveHourLimitPercent { get; }
    /// <summary>Lifetime consumption compared with the current full weekly allowance.</summary>
    public double? WeeklyLimitPercent { get; }
    /// <summary>Purchased or granted credits actually debited; null means unknown.</summary>
    public double? BalanceUsageCredits { get; }
    /// <summary>Provider model, when present in a breakdown group.</summary>
    public string? Model { get; }
    /// <summary>Provider reasoning effort, when present.</summary>
    public string? ReasoningEffort { get; }
    /// <summary>Provider speed setting, when present.</summary>
    public string? Speed { get; }
    /// <summary>Provider product experience, when present.</summary>
    public string? ProductExperience { get; }
}

/// <summary>One queried thread's lifetime measurements and provider coverage status.</summary>
public sealed class ChatGptThreadUsageEntry : ChatGptThreadUsageMetrics {
    internal ChatGptThreadUsageEntry(JsonObject obj) : base(obj) {
        ThreadId = obj.GetString("thread_id"); DataStatus = obj.GetString("data_status"); UsageSource = obj.GetString("usage_source");
        Groups = ChatGptAnalyticsJson.Rows(obj, "groups", static row => new ChatGptThreadUsageMetrics(row));
    }
    /// <summary>Queried provider thread identifier.</summary>
    public string? ThreadId { get; }
    /// <summary>Provider completeness status, such as partial or unavailable.</summary>
    public string? DataStatus { get; }
    /// <summary>Provider usage source, such as included_plan.</summary>
    public string? UsageSource { get; }
    /// <summary>Model, reasoning-effort and speed breakdowns.</summary>
    public IReadOnlyList<ChatGptThreadUsageMetrics> Groups { get; }
}
