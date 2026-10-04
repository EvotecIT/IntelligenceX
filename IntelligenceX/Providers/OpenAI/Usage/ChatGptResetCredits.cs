using System;
using System.Collections.Generic;
using System.Globalization;
using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.Usage;

/// <summary>Provider-reported reset grants and retained history, separate from spendable usage credits.</summary>
public sealed class ChatGptResetCredits {
    private ChatGptResetCredits(JsonObject summary, JsonObject? details, JsonObject? history) {
        AvailableCount = details?.GetInt64("available_count") ?? summary.GetInt64("available_count");
        ApplicableAvailableCount = summary.GetInt64("applicable_available_count");
        DetailsAvailable = details?.GetArray("credits") is not null;
        var credits = new List<ChatGptResetCredit>();
        foreach (var item in details?.GetArray("credits") ?? new JsonArray()) {
            if (item.AsObject() is { } obj) credits.Add(new ChatGptResetCredit(obj));
        }
        Credits = credits;
        HistoryAvailable = history?.GetArray("events") is not null;
        var events = new List<ChatGptResetCreditEvent>();
        foreach (var item in history?.GetArray("events") ?? new JsonArray()) {
            if (item.AsObject() is { } obj) events.Add(new ChatGptResetCreditEvent(obj));
        }
        History = events;
        HistoryWindowStart = ReadTimestamp(history, "window_start");
        HistoryAsOf = ReadTimestamp(history, "as_of");
        HistoryNextCursor = history?.GetString("next_cursor");
    }
    /// <summary>Available grant count, or null when unreported.</summary>
    public long? AvailableCount { get; }
    /// <summary>Currently applicable grants; zero does not mean no banked grants.</summary>
    public long? ApplicableAvailableCount { get; }
    /// <summary>Whether an individual grant list was returned, including an empty list.</summary>
    public bool DetailsAvailable { get; }
    /// <summary>Individual grants with their original provider status.</summary>
    public IReadOnlyList<ChatGptResetCredit> Credits { get; }
    /// <summary>Whether a history event list was returned.</summary>
    public bool HistoryAvailable { get; }
    /// <summary>Events in the returned page, not a lifetime history.</summary>
    public IReadOnlyList<ChatGptResetCreditEvent> History { get; }
    /// <summary>Beginning of the provider's history retention window.</summary>
    public DateTimeOffset? HistoryWindowStart { get; }
    /// <summary>Time through which the history page is current.</summary>
    public DateTimeOffset? HistoryAsOf { get; }
    /// <summary>Cursor for another history page, when reported.</summary>
    public string? HistoryNextCursor { get; }

    /// <summary>Parses reset counts and separately retrieved details/history from a usage snapshot.</summary>
    public static ChatGptResetCredits? FromJson(JsonObject usage) {
        var summary = usage.GetObject("rate_limit_reset_credits");
        var details = usage.GetObject("rate_limit_reset_credit_details");
        var history = usage.GetObject("rate_limit_reset_credit_history");
        return summary is null && details is null && history is null ? null
            : new ChatGptResetCredits(summary ?? new JsonObject(), details, history);
    }
    internal static DateTimeOffset? ReadTimestamp(JsonObject? obj, string name) {
        if (obj is null) return null;
        var seconds = obj.GetInt64(name);
        if (seconds.HasValue) {
            try { return DateTimeOffset.FromUnixTimeSeconds(seconds.Value); }
            catch (ArgumentOutOfRangeException) { return null; }
        }
        return DateTimeOffset.TryParse(obj.GetString(name), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date) ? date : null;
    }
}

/// <summary>A provider reset grant. Reading or displaying it never consumes the grant.</summary>
public sealed class ChatGptResetCredit {
    internal ChatGptResetCredit(JsonObject obj) {
        Id = obj.GetString("id"); ResetType = obj.GetString("reset_type");
        Status = obj.GetString("status"); Title = obj.GetString("title");
        IsSupportedByPlan = obj.TryGetValue("is_supported_by_plan", out var supported) ? supported?.AsBoolean() : null;
        GrantedAt = ChatGptResetCredits.ReadTimestamp(obj, "granted_at");
        ExpiresAt = ChatGptResetCredits.ReadTimestamp(obj, "expires_at");
        RedeemStartedAt = ChatGptResetCredits.ReadTimestamp(obj, "redeem_started_at");
        RedeemedAt = ChatGptResetCredits.ReadTimestamp(obj, "redeemed_at");
    }
    /// <summary>Stable provider grant identity.</summary>
    public string? Id { get; }
    /// <summary>Provider reset type; unknown types are preserved.</summary>
    public string? ResetType { get; }
    /// <summary>Original provider status, without inferring redemption eligibility.</summary>
    public string? Status { get; }
    /// <summary>Provider-facing grant title.</summary>
    public string? Title { get; }
    /// <summary>Whether the account plan supports this grant, if reported.</summary>
    public bool? IsSupportedByPlan { get; }
    /// <summary>Exact grant instant, if valid.</summary>
    public DateTimeOffset? GrantedAt { get; }
    /// <summary>Exact expiry, retaining provider-reported fractional seconds.</summary>
    public DateTimeOffset? ExpiresAt { get; }
    /// <summary>Time redemption began, if reported.</summary>
    public DateTimeOffset? RedeemStartedAt { get; }
    /// <summary>Provider-confirmed redemption time, if reported.</summary>
    public DateTimeOffset? RedeemedAt { get; }
}

/// <summary>A provider grant/use event within the retained history window.</summary>
public sealed class ChatGptResetCreditEvent {
    internal ChatGptResetCreditEvent(JsonObject obj) {
        Id = obj.GetString("id"); Kind = obj.GetString("kind");
        OccurredAt = ChatGptResetCredits.ReadTimestamp(obj, "occurred_at");
    }
    /// <summary>Provider event identity.</summary>
    public string? Id { get; }
    /// <summary>Provider event kind, including unknown future kinds.</summary>
    public string? Kind { get; }
    /// <summary>Exact event time, if valid.</summary>
    public DateTimeOffset? OccurredAt { get; }
}
