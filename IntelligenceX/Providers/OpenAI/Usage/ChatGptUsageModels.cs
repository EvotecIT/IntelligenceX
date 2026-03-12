using System;
using System.Collections.Generic;
using System.Globalization;
using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.Usage;

/// <summary>
/// Represents a usage snapshot returned by ChatGPT usage endpoints.
/// </summary>
public sealed class ChatGptUsageSnapshot {
    /// <summary>
    /// Initializes a new usage snapshot.
    /// </summary>
    public ChatGptUsageSnapshot(string? userId, string? accountId, string? email, string? planType,
        ChatGptRateLimitStatus? rateLimit, ChatGptRateLimitStatus? codeReviewRateLimit,
        ChatGptCreditsSnapshot? credits, JsonObject raw, JsonObject? additional) {
        UserId = userId;
        AccountId = accountId;
        Email = email;
        PlanType = planType;
        RateLimit = rateLimit;
        CodeReviewRateLimit = codeReviewRateLimit;
        Credits = credits;
        Raw = raw;
        Additional = additional;
    }

    /// <summary>
    /// Gets the user id.
    /// </summary>
    public string? UserId { get; }
    /// <summary>
    /// Gets the account id.
    /// </summary>
    public string? AccountId { get; }
    /// <summary>
    /// Gets the account email.
    /// </summary>
    public string? Email { get; }
    /// <summary>
    /// Gets the plan type identifier.
    /// </summary>
    public string? PlanType { get; }
    /// <summary>
    /// Gets the main rate limit status.
    /// </summary>
    public ChatGptRateLimitStatus? RateLimit { get; }
    /// <summary>
    /// Gets the code review rate limit status.
    /// </summary>
    public ChatGptRateLimitStatus? CodeReviewRateLimit { get; }
    /// <summary>
    /// Gets the credits snapshot.
    /// </summary>
    public ChatGptCreditsSnapshot? Credits { get; }
    /// <summary>
    /// Gets the raw JSON object.
    /// </summary>
    public JsonObject Raw { get; }
    /// <summary>
    /// Gets unrecognized fields from the payload.
    /// </summary>
    public JsonObject? Additional { get; }

    /// <summary>
    /// Creates a snapshot from a JSON object.
    /// </summary>
    /// <param name="obj">Source JSON object.</param>
    /// <returns>The parsed snapshot.</returns>
    public static ChatGptUsageSnapshot FromJson(JsonObject obj) {
        var userId = obj.GetString("user_id") ?? obj.GetString("userId");
        var accountId = obj.GetString("account_id") ?? obj.GetString("accountId");
        var email = obj.GetString("email");
        var planType = obj.GetString("plan_type") ?? obj.GetString("planType");
        var rateLimit = ChatGptRateLimitStatus.FromJson(obj.GetObject("rate_limit") ?? obj.GetObject("rateLimit"));
        var codeReview = ChatGptRateLimitStatus.FromJson(obj.GetObject("code_review_rate_limit") ?? obj.GetObject("codeReviewRateLimit"));
        var credits = ChatGptCreditsSnapshot.FromJson(obj.GetObject("credits"));
        var additional = obj.ExtractAdditional(
            "user_id", "userId", "account_id", "accountId", "email", "plan_type", "planType",
            "rate_limit", "rateLimit", "code_review_rate_limit", "codeReviewRateLimit", "credits", "promo");
        return new ChatGptUsageSnapshot(userId, accountId, email, planType, rateLimit, codeReview, credits, obj, additional);
    }

    /// <summary>
    /// Serializes the snapshot to a JSON object.
    /// </summary>
    public JsonObject ToJson() {
        if (Raw is not null) {
            return Raw;
        }
        var obj = new JsonObject();
        if (!string.IsNullOrWhiteSpace(UserId)) {
            obj.Add("user_id", UserId);
        }
        if (!string.IsNullOrWhiteSpace(AccountId)) {
            obj.Add("account_id", AccountId);
        }
        if (!string.IsNullOrWhiteSpace(Email)) {
            obj.Add("email", Email);
        }
        if (!string.IsNullOrWhiteSpace(PlanType)) {
            obj.Add("plan_type", PlanType);
        }
        if (RateLimit is not null) {
            obj.Add("rate_limit", RateLimit.ToJson());
        }
        if (CodeReviewRateLimit is not null) {
            obj.Add("code_review_rate_limit", CodeReviewRateLimit.ToJson());
        }
        if (Credits is not null) {
            obj.Add("credits", Credits.ToJson());
        }
        return obj;
    }
}

/// <summary>
/// Represents rate limit status information.
/// </summary>
public sealed class ChatGptRateLimitStatus {
    /// <summary>
    /// Initializes a new rate limit status.
    /// </summary>
    public ChatGptRateLimitStatus(bool allowed, bool limitReached, ChatGptRateLimitWindow? primary, ChatGptRateLimitWindow? secondary,
        JsonObject raw, JsonObject? additional) {
        Allowed = allowed;
        LimitReached = limitReached;
        PrimaryWindow = primary;
        SecondaryWindow = secondary;
        Raw = raw;
        Additional = additional;
    }

    /// <summary>
    /// Gets a value indicating whether requests are allowed.
    /// </summary>
    public bool Allowed { get; }
    /// <summary>
    /// Gets a value indicating whether the limit has been reached.
    /// </summary>
    public bool LimitReached { get; }
    /// <summary>
    /// Gets the primary rate limit window.
    /// </summary>
    public ChatGptRateLimitWindow? PrimaryWindow { get; }
    /// <summary>
    /// Gets the secondary rate limit window.
    /// </summary>
    public ChatGptRateLimitWindow? SecondaryWindow { get; }
    /// <summary>
    /// Gets the raw JSON object.
    /// </summary>
    public JsonObject Raw { get; }
    /// <summary>
    /// Gets unrecognized fields from the payload.
    /// </summary>
    public JsonObject? Additional { get; }

    /// <summary>
    /// Parses a rate limit status from JSON.
    /// </summary>
    /// <param name="obj">Source JSON object.</param>
    /// <returns>The parsed status or null.</returns>
    public static ChatGptRateLimitStatus? FromJson(JsonObject? obj) {
        if (obj is null) {
            return null;
        }
        var allowed = obj.GetBoolean("allowed");
        var limitReached = obj.GetBoolean("limit_reached");
        var primary = ChatGptRateLimitWindow.FromJson(obj.GetObject("primary_window") ?? obj.GetObject("primaryWindow"));
        var secondary = ChatGptRateLimitWindow.FromJson(obj.GetObject("secondary_window") ?? obj.GetObject("secondaryWindow"));
        var additional = obj.ExtractAdditional(
            "allowed", "limit_reached", "primary_window", "primaryWindow", "secondary_window", "secondaryWindow");
        return new ChatGptRateLimitStatus(allowed, limitReached, primary, secondary, obj, additional);
    }

    /// <summary>
    /// Serializes the status to a JSON object.
    /// </summary>
    public JsonObject ToJson() {
        if (Raw is not null) {
            return Raw;
        }
        var obj = new JsonObject()
            .Add("allowed", Allowed)
            .Add("limit_reached", LimitReached);
        if (PrimaryWindow is not null) {
            obj.Add("primary_window", PrimaryWindow.ToJson());
        }
        if (SecondaryWindow is not null) {
            obj.Add("secondary_window", SecondaryWindow.ToJson());
        }
        return obj;
    }
}

/// <summary>
/// Represents a rate limit window.
/// </summary>
public sealed class ChatGptRateLimitWindow {
    /// <summary>
    /// Initializes a new rate limit window.
    /// </summary>
    public ChatGptRateLimitWindow(double? usedPercent, long? limitWindowSeconds, long? resetAfterSeconds, long? resetAt,
        JsonObject raw, JsonObject? additional) {
        UsedPercent = usedPercent;
        LimitWindowSeconds = limitWindowSeconds;
        ResetAfterSeconds = resetAfterSeconds;
        ResetAtUnixSeconds = resetAt;
        Raw = raw;
        Additional = additional;
    }

    /// <summary>
    /// Gets the percentage of usage consumed.
    /// </summary>
    public double? UsedPercent { get; }
    /// <summary>
    /// Gets the window length in seconds.
    /// </summary>
    public long? LimitWindowSeconds { get; }
    /// <summary>
    /// Gets the seconds remaining until reset.
    /// </summary>
    public long? ResetAfterSeconds { get; }
    /// <summary>
    /// Gets the reset time in Unix seconds.
    /// </summary>
    public long? ResetAtUnixSeconds { get; }
    /// <summary>
    /// Gets the raw JSON object.
    /// </summary>
    public JsonObject Raw { get; }
    /// <summary>
    /// Gets unrecognized fields from the payload.
    /// </summary>
    public JsonObject? Additional { get; }

    /// <summary>
    /// Parses a rate limit window from JSON.
    /// </summary>
    /// <param name="obj">Source JSON object.</param>
    /// <returns>The parsed window or null.</returns>
    public static ChatGptRateLimitWindow? FromJson(JsonObject? obj) {
        if (obj is null) {
            return null;
        }
        var usedPercent = obj.GetDouble("used_percent");
        var limitWindowSeconds = obj.GetInt64("limit_window_seconds");
        var resetAfterSeconds = obj.GetInt64("reset_after_seconds");
        var resetAt = obj.GetInt64("reset_at");
        var additional = obj.ExtractAdditional(
            "used_percent", "limit_window_seconds", "reset_after_seconds", "reset_at");
        return new ChatGptRateLimitWindow(usedPercent, limitWindowSeconds, resetAfterSeconds, resetAt, obj, additional);
    }

    /// <summary>
    /// Serializes the window to a JSON object.
    /// </summary>
    public JsonObject ToJson() {
        if (Raw is not null) {
            return Raw;
        }
        var obj = new JsonObject();
        if (UsedPercent.HasValue) {
            obj.Add("used_percent", UsedPercent.Value);
        }
        if (LimitWindowSeconds.HasValue) {
            obj.Add("limit_window_seconds", LimitWindowSeconds.Value);
        }
        if (ResetAfterSeconds.HasValue) {
            obj.Add("reset_after_seconds", ResetAfterSeconds.Value);
        }
        if (ResetAtUnixSeconds.HasValue) {
            obj.Add("reset_at", ResetAtUnixSeconds.Value);
        }
        return obj;
    }
}

/// <summary>
/// Represents a credits snapshot for a ChatGPT account.
/// </summary>
public sealed class ChatGptCreditsSnapshot {
    /// <summary>
    /// Initializes a new credits snapshot.
    /// </summary>
    public ChatGptCreditsSnapshot(bool hasCredits, bool unlimited, double? balance, int[]? approxLocalMessages,
        int[]? approxCloudMessages, JsonObject raw, JsonObject? additional) {
        HasCredits = hasCredits;
        Unlimited = unlimited;
        Balance = balance;
        ApproxLocalMessages = approxLocalMessages;
        ApproxCloudMessages = approxCloudMessages;
        Raw = raw;
        Additional = additional;
    }

    /// <summary>
    /// Gets a value indicating whether the account has credits.
    /// </summary>
    public bool HasCredits { get; }
    /// <summary>
    /// Gets a value indicating whether credits are unlimited.
    /// </summary>
    public bool Unlimited { get; }
    /// <summary>
    /// Gets the credits balance, when available.
    /// </summary>
    public double? Balance { get; }
    /// <summary>
    /// Gets approximate local message counts.
    /// </summary>
    public int[]? ApproxLocalMessages { get; }
    /// <summary>
    /// Gets approximate cloud message counts.
    /// </summary>
    public int[]? ApproxCloudMessages { get; }
    /// <summary>
    /// Gets the raw JSON object.
    /// </summary>
    public JsonObject Raw { get; }
    /// <summary>
    /// Gets unrecognized fields from the payload.
    /// </summary>
    public JsonObject? Additional { get; }

    /// <summary>
    /// Parses a credits snapshot from JSON.
    /// </summary>
    /// <param name="obj">Source JSON object.</param>
    /// <returns>The parsed snapshot or null.</returns>
    public static ChatGptCreditsSnapshot? FromJson(JsonObject? obj) {
        if (obj is null) {
            return null;
        }
        var hasCredits = obj.GetBoolean("has_credits");
        var unlimited = obj.GetBoolean("unlimited");
        var balance = ParseBalance(obj);
        var approxLocal = ReadIntArray(obj.GetArray("approx_local_messages"));
        var approxCloud = ReadIntArray(obj.GetArray("approx_cloud_messages"));
        var additional = obj.ExtractAdditional(
            "has_credits", "unlimited", "balance", "approx_local_messages", "approx_cloud_messages");
        return new ChatGptCreditsSnapshot(hasCredits, unlimited, balance, approxLocal, approxCloud, obj, additional);
    }

    /// <summary>
    /// Serializes the credits snapshot to a JSON object.
    /// </summary>
    public JsonObject ToJson() {
        if (Raw is not null) {
            return Raw;
        }
        var obj = new JsonObject()
            .Add("has_credits", HasCredits)
            .Add("unlimited", Unlimited);
        if (Balance.HasValue) {
            obj.Add("balance", Balance.Value);
        }
        if (ApproxLocalMessages is not null) {
            var array = new JsonArray();
            foreach (var value in ApproxLocalMessages) {
                array.Add(JsonValue.From(value));
            }
            obj.Add("approx_local_messages", array);
        }
        if (ApproxCloudMessages is not null) {
            var array = new JsonArray();
            foreach (var value in ApproxCloudMessages) {
                array.Add(JsonValue.From(value));
            }
            obj.Add("approx_cloud_messages", array);
        }
        return obj;
    }

    private static double? ParseBalance(JsonObject obj) {
        var balance = obj.GetDouble("balance");
        if (balance.HasValue) {
            return balance.Value;
        }
        var text = obj.GetString("balance");
        if (string.IsNullOrWhiteSpace(text)) {
            return null;
        }
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)) {
            return parsed;
        }
        return null;
    }

    private static int[]? ReadIntArray(JsonArray? array) {
        if (array is null) {
            return null;
        }
        var list = new List<int>();
        foreach (var item in array) {
            var value = item.AsInt64();
            if (value.HasValue) {
                list.Add((int)value.Value);
                continue;
            }
            var dbl = item.AsDouble();
            if (dbl.HasValue) {
                list.Add((int)Math.Round(dbl.Value));
            }
        }
        return list.ToArray();
    }
}

/// <summary>
/// Represents a single credit usage event.
/// </summary>
public sealed class ChatGptCreditUsageEvent {
    /// <summary>
    /// Initializes a new credit usage event.
    /// </summary>
    public ChatGptCreditUsageEvent(string? date, string? productSurface, double? creditAmount, string? usageId, JsonObject raw,
        JsonObject? additional, string? processingTier = null, string? model = null) {
        Date = date;
        ProductSurface = productSurface;
        CreditAmount = creditAmount;
        UsageId = usageId;
        ProcessingTier = processingTier;
        Model = model;
        Raw = raw;
        Additional = additional;
    }

    /// <summary>
    /// Gets the event date.
    /// </summary>
    public string? Date { get; }
    /// <summary>
    /// Gets the product surface identifier.
    /// </summary>
    public string? ProductSurface { get; }
    /// <summary>
    /// Gets the credit amount for the event.
    /// </summary>
    public double? CreditAmount { get; }
    /// <summary>
    /// Gets the usage id when present.
    /// </summary>
    public string? UsageId { get; }
    /// <summary>
    /// Gets the processing tier when present (for example priority/fast).
    /// </summary>
    public string? ProcessingTier { get; }
    /// <summary>
    /// Gets the model identifier when present.
    /// </summary>
    public string? Model { get; }
    /// <summary>
    /// Gets the raw JSON object.
    /// </summary>
    public JsonObject Raw { get; }
    /// <summary>
    /// Gets unrecognized fields from the payload.
    /// </summary>
    public JsonObject? Additional { get; }

    /// <summary>
    /// Parses a credit usage event from JSON.
    /// </summary>
    /// <param name="obj">Source JSON object.</param>
    /// <returns>The parsed credit usage event.</returns>
    public static ChatGptCreditUsageEvent FromJson(JsonObject obj) {
        var date = obj.GetString("date");
        var surface = obj.GetString("product_surface") ?? obj.GetString("productSurface");
        var creditAmount = obj.GetDouble("credit_amount") ?? obj.GetDouble("creditAmount");
        var usageId = obj.GetString("usage_id") ?? obj.GetString("usageId");
        var processingTier = obj.GetString("processing_tier")
                             ?? obj.GetString("processingTier")
                             ?? obj.GetString("service_tier")
                             ?? obj.GetString("serviceTier");
        var model = obj.GetString("model");
        var additional = obj.ExtractAdditional(
            "date", "product_surface", "productSurface", "credit_amount", "creditAmount", "usage_id", "usageId",
            "processing_tier", "processingTier", "service_tier", "serviceTier", "model");
        return new ChatGptCreditUsageEvent(date, surface, creditAmount, usageId, obj, additional, processingTier, model);
    }

    /// <summary>
    /// Serializes the event to a JSON object.
    /// </summary>
    public JsonObject ToJson() {
        if (Raw is not null) {
            return Raw;
        }
        var obj = new JsonObject();
        if (!string.IsNullOrWhiteSpace(Date)) {
            obj.Add("date", Date);
        }
        if (!string.IsNullOrWhiteSpace(ProductSurface)) {
            obj.Add("product_surface", ProductSurface);
        }
        if (CreditAmount.HasValue) {
            obj.Add("credit_amount", CreditAmount.Value);
        }
        if (!string.IsNullOrWhiteSpace(UsageId)) {
            obj.Add("usage_id", UsageId);
        }
        if (!string.IsNullOrWhiteSpace(ProcessingTier)) {
            obj.Add("processing_tier", ProcessingTier);
        }
        if (!string.IsNullOrWhiteSpace(Model)) {
            obj.Add("model", Model);
        }
        return obj;
    }
}

/// <summary>
/// Represents daily token usage breakdown grouped by product surface.
/// </summary>
public sealed class ChatGptDailyTokenUsageBreakdown {
    /// <summary>
    /// Initializes a new daily token usage breakdown payload.
    /// </summary>
    public ChatGptDailyTokenUsageBreakdown(IReadOnlyList<ChatGptDailyTokenUsageDay> data, string? units, JsonObject raw,
        JsonObject? additional) {
        Data = data ?? Array.Empty<ChatGptDailyTokenUsageDay>();
        Units = units;
        Raw = raw;
        Additional = additional;
    }

    /// <summary>
    /// Gets the daily data points.
    /// </summary>
    public IReadOnlyList<ChatGptDailyTokenUsageDay> Data { get; }
    /// <summary>
    /// Gets the unit label reported by the API.
    /// </summary>
    public string? Units { get; }
    /// <summary>
    /// Gets the raw JSON object.
    /// </summary>
    public JsonObject Raw { get; }
    /// <summary>
    /// Gets unrecognized fields from the payload.
    /// </summary>
    public JsonObject? Additional { get; }

    /// <summary>
    /// Parses a daily token usage breakdown payload from JSON.
    /// </summary>
    public static ChatGptDailyTokenUsageBreakdown FromJson(JsonObject obj) {
        var data = new List<ChatGptDailyTokenUsageDay>();
        var array = obj.GetArray("data");
        if (array is not null) {
            foreach (var item in array) {
                var day = item.AsObject();
                if (day is null) {
                    continue;
                }
                data.Add(ChatGptDailyTokenUsageDay.FromJson(day));
            }
        }
        var units = obj.GetString("units");
        var additional = obj.ExtractAdditional("data", "units");
        return new ChatGptDailyTokenUsageBreakdown(data, units, obj, additional);
    }

    /// <summary>
    /// Serializes the breakdown to a JSON object.
    /// </summary>
    public JsonObject ToJson() {
        if (Raw is not null) {
            return Raw;
        }
        var obj = new JsonObject();
        var data = new JsonArray();
        foreach (var day in Data) {
            data.Add(JsonValue.From(day.ToJson()));
        }
        obj.Add("data", data);
        if (!string.IsNullOrWhiteSpace(Units)) {
            obj.Add("units", Units);
        }
        return obj;
    }
}

/// <summary>
/// Represents one day's token usage breakdown by product surface.
/// </summary>
public sealed class ChatGptDailyTokenUsageDay {
    /// <summary>
    /// Initializes a new daily token usage row.
    /// </summary>
    public ChatGptDailyTokenUsageDay(string? date, IReadOnlyDictionary<string, double> productSurfaceUsageValues, JsonObject raw,
        JsonObject? additional) {
        Date = date;
        ProductSurfaceUsageValues = productSurfaceUsageValues ?? new Dictionary<string, double>(StringComparer.Ordinal);
        Raw = raw;
        Additional = additional;
    }

    /// <summary>
    /// Gets the bucket date.
    /// </summary>
    public string? Date { get; }
    /// <summary>
    /// Gets usage values keyed by product surface.
    /// </summary>
    public IReadOnlyDictionary<string, double> ProductSurfaceUsageValues { get; }
    /// <summary>
    /// Gets the total across all surfaces for the day.
    /// </summary>
    public double Total => ProductSurfaceUsageValues.Values.Sum();
    /// <summary>
    /// Gets the raw JSON object.
    /// </summary>
    public JsonObject Raw { get; }
    /// <summary>
    /// Gets unrecognized fields from the payload.
    /// </summary>
    public JsonObject? Additional { get; }

    /// <summary>
    /// Parses a daily token usage row from JSON.
    /// </summary>
    public static ChatGptDailyTokenUsageDay FromJson(JsonObject obj) {
        var date = obj.GetString("date");
        var usageValues = new Dictionary<string, double>(StringComparer.Ordinal);
        var usageObj = obj.GetObject("product_surface_usage_values") ?? obj.GetObject("productSurfaceUsageValues");
        if (usageObj is not null) {
            foreach (var pair in usageObj) {
                var value = pair.Value?.AsDouble();
                if (value.HasValue) {
                    usageValues[pair.Key] = value.Value;
                }
            }
        }
        var additional = obj.ExtractAdditional("date", "product_surface_usage_values", "productSurfaceUsageValues");
        return new ChatGptDailyTokenUsageDay(date, usageValues, obj, additional);
    }

    /// <summary>
    /// Serializes the day to a JSON object.
    /// </summary>
    public JsonObject ToJson() {
        if (Raw is not null) {
            return Raw;
        }
        var obj = new JsonObject();
        if (!string.IsNullOrWhiteSpace(Date)) {
            obj.Add("date", Date);
        }
        var usage = new JsonObject();
        foreach (var pair in ProductSurfaceUsageValues) {
            usage.Add(pair.Key, pair.Value);
        }
        obj.Add("product_surface_usage_values", usage);
        return obj;
    }
}

/// <summary>
/// Represents a combined usage snapshot and usage events report.
/// </summary>
public sealed class ChatGptUsageReport {
    /// <summary>
    /// Initializes a new usage report.
    /// </summary>
    public ChatGptUsageReport(ChatGptUsageSnapshot snapshot, IReadOnlyList<ChatGptCreditUsageEvent> events,
        ChatGptDailyTokenUsageBreakdown? dailyBreakdown = null) {
        Snapshot = snapshot;
        Events = events;
        DailyBreakdown = dailyBreakdown;
    }

    /// <summary>
    /// Gets the usage snapshot.
    /// </summary>
    public ChatGptUsageSnapshot Snapshot { get; }
    /// <summary>
    /// Gets the credit usage events.
    /// </summary>
    public IReadOnlyList<ChatGptCreditUsageEvent> Events { get; }
    /// <summary>
    /// Gets the daily token usage breakdown when requested.
    /// </summary>
    public ChatGptDailyTokenUsageBreakdown? DailyBreakdown { get; }

    /// <summary>
    /// Serializes the report to a JSON object.
    /// </summary>
    public JsonObject ToJson() {
        var obj = new JsonObject()
            .Add("usage", Snapshot.ToJson());
        if (Events is not null) {
            var array = new JsonArray();
            foreach (var evt in Events) {
                array.Add(JsonValue.From(evt.ToJson()));
            }
            obj.Add("events", array);
        }
        if (DailyBreakdown is not null) {
            obj.Add("dailyBreakdown", DailyBreakdown.ToJson());
        }
        return obj;
    }
}
