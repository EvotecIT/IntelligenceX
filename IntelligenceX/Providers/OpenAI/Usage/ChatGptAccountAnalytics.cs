using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.Usage;

/// <summary>Account-wide provider measurements, independent of local Codex session logs.
/// Endpoints have independent availability and freshness; missing data never means zero usage.</summary>
[JsonConverter(typeof(ChatGptAccountAnalyticsConverter))]
public sealed class ChatGptAccountAnalytics {
    private readonly JsonObject _raw;
    private ChatGptAccountAnalytics(JsonObject obj) {
        _raw = obj;
        RetrievedAt = ChatGptResetCredits.ReadTimestamp(obj, "retrieved_at");
        Profile = obj.GetObject("profile") is { } profile ? ChatGptProfileStatistics.FromJson(profile) : null;
        DailyUsage = obj.GetObject("daily_usage") is { } daily ? ChatGptDailyTokenUsageBreakdown.FromJson(daily) : null;
        PlanHistory = obj.GetObject("plan_history") is { } history ? ChatGptPlanLimitHistory.FromJson(history) : null;
        Errors = ChatGptAnalyticsJson.Rows(obj, "errors", static error => new ChatGptAnalyticsError(error));
    }
    /// <summary>Collection time, distinct from each endpoint's provider data timestamp.</summary>
    public DateTimeOffset? RetrievedAt { get; }
    /// <summary>Provider profile totals, activity buckets and skill statistics.</summary>
    public ChatGptProfileStatistics? Profile { get; }
    /// <summary>Daily measurements and attribution, interpreted using the returned units.</summary>
    public ChatGptDailyTokenUsageBreakdown? DailyUsage { get; }
    /// <summary>Provider allowance accounting periods and coverage metadata.</summary>
    public ChatGptPlanLimitHistory? PlanHistory { get; }
    /// <summary>Safe endpoint availability errors; never includes response bodies or credentials.</summary>
    public IReadOnlyList<ChatGptAnalyticsError> Errors { get; }
    /// <summary>Whether at least one endpoint returned recognized measurement data.</summary>
    public bool IsAvailable => Profile is not null || DailyUsage is not null || PlanHistory is not null;
    /// <summary>Parses an IX analytics envelope while preserving original provider fields.</summary>
    public static ChatGptAccountAnalytics FromJson(JsonObject obj) => new(obj ?? throw new ArgumentNullException(nameof(obj)));
    /// <summary>Returns the complete measurement envelope for JSON export or account-scoped caching.</summary>
    public JsonObject ToJson() => _raw;
}

/// <summary>Availability of a single provider analytics endpoint.</summary>
public sealed class ChatGptAnalyticsError {
    internal ChatGptAnalyticsError(JsonObject obj) {
        Endpoint = obj.GetString("endpoint");
        StatusCode = obj.GetInt64("status_code");
        Message = obj.GetString("message");
    }
    /// <summary>Endpoint name, without account or authentication details.</summary>
    public string? Endpoint { get; }
    /// <summary>HTTP status when a response was received; null when no response status is available.</summary>
    public long? StatusCode { get; }
    /// <summary>Safe description of the unavailable measurement.</summary>
    public string? Message { get; }
}

/// <summary>Preserves provider field names and unknown measurements in JSON and source-generated protocol consumers.</summary>
public sealed class ChatGptAccountAnalyticsConverter : JsonConverter<ChatGptAccountAnalytics> {
    /// <summary>Initializes the converter for reflection or source-generated serialization.</summary>
    public ChatGptAccountAnalyticsConverter() { }
    /// <inheritdoc />
    public override ChatGptAccountAnalytics? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        if (reader.TokenType == JsonTokenType.Null) return null;
        using var document = JsonDocument.ParseValue(ref reader);
        var obj = JsonLite.Parse(document.RootElement.GetRawText())?.AsObject();
        return obj is null ? throw new JsonException("Invalid account analytics envelope.") : ChatGptAccountAnalytics.FromJson(obj);
    }
    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, ChatGptAccountAnalytics value, JsonSerializerOptions options) {
        using var document = JsonDocument.Parse(JsonLite.Serialize(JsonValue.From(value.ToJson())));
        document.RootElement.WriteTo(writer);
    }
}
