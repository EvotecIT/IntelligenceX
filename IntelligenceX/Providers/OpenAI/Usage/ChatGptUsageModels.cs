using System;
using System.Collections.Generic;
using System.Globalization;
using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.Usage;

/// <summary>
/// Snapshot of ChatGPT usage, limits, and credits for the authenticated account.
/// </summary>
public sealed class ChatGptUsageSnapshot {
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
    /// User identifier returned by the service (if provided).
    /// </summary>
    public string? UserId { get; }
    /// <summary>
    /// Account identifier returned by the service (if provided).
    /// </summary>
    public string? AccountId { get; }
    /// <summary>
    /// Account email (if returned by the service).
    /// </summary>
    public string? Email { get; }
    /// <summary>
    /// Plan type such as "pro".
    /// </summary>
    public string? PlanType { get; }
    /// <summary>
    /// Primary usage rate limit information.
    /// </summary>
    public ChatGptRateLimitStatus? RateLimit { get; }
    /// <summary>
    /// Code review specific rate limit information (when available).
    /// </summary>
    public ChatGptRateLimitStatus? CodeReviewRateLimit { get; }
    /// <summary>
    /// Credits and balance details (if available).
    /// </summary>
    public ChatGptCreditsSnapshot? Credits { get; }
    /// <summary>
    /// Raw JSON payload from the service.
    /// </summary>
    public JsonObject Raw { get; }
    /// <summary>
    /// Additional unmapped fields from the payload.
    /// </summary>
    public JsonObject? Additional { get; }

    /// <summary>
    /// Parses a usage snapshot from a JSON object.
    /// </summary>
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
    /// Serializes the snapshot back to JSON.
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
/// Rate limit status for a usage category.
/// </summary>
public sealed class ChatGptRateLimitStatus {
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
    /// Whether requests are currently allowed.
    /// </summary>
    public bool Allowed { get; }
    /// <summary>
    /// Whether a limit has been reached.
    /// </summary>
    public bool LimitReached { get; }
    /// <summary>
    /// Primary rate limit window details.
    /// </summary>
    public ChatGptRateLimitWindow? PrimaryWindow { get; }
    /// <summary>
    /// Secondary rate limit window details.
    /// </summary>
    public ChatGptRateLimitWindow? SecondaryWindow { get; }
    /// <summary>
    /// Raw JSON payload from the service.
    /// </summary>
    public JsonObject Raw { get; }
    /// <summary>
    /// Additional unmapped fields from the payload.
    /// </summary>
    public JsonObject? Additional { get; }

    /// <summary>
    /// Parses a rate limit status from JSON.
    /// </summary>
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
    /// Serializes the rate limit status back to JSON.
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
/// Rate limit window details (usage percentage, window size, and reset timings).
/// </summary>
public sealed class ChatGptRateLimitWindow {
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
    /// Percentage of the window used (0-100).
    /// </summary>
    public double? UsedPercent { get; }
    /// <summary>
    /// Window size in seconds.
    /// </summary>
    public long? LimitWindowSeconds { get; }
    /// <summary>
    /// Seconds until the window resets.
    /// </summary>
    public long? ResetAfterSeconds { get; }
    /// <summary>
    /// Reset time as a Unix timestamp (seconds).
    /// </summary>
    public long? ResetAtUnixSeconds { get; }
    /// <summary>
    /// Raw JSON payload from the service.
    /// </summary>
    public JsonObject Raw { get; }
    /// <summary>
    /// Additional unmapped fields from the payload.
    /// </summary>
    public JsonObject? Additional { get; }

    /// <summary>
    /// Parses a rate limit window from JSON.
    /// </summary>
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
    /// Serializes the rate limit window back to JSON.
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
/// Credits snapshot for the account, including balance and message estimates.
/// </summary>
public sealed class ChatGptCreditsSnapshot {
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
    /// Whether credits are available.
    /// </summary>
    public bool HasCredits { get; }
    /// <summary>
    /// Whether credits are unlimited for this account.
    /// </summary>
    public bool Unlimited { get; }
    /// <summary>
    /// Current credit balance (if provided).
    /// </summary>
    public double? Balance { get; }
    /// <summary>
    /// Approximate local message counts (when provided).
    /// </summary>
    public int[]? ApproxLocalMessages { get; }
    /// <summary>
    /// Approximate cloud message counts (when provided).
    /// </summary>
    public int[]? ApproxCloudMessages { get; }
    /// <summary>
    /// Raw JSON payload from the service.
    /// </summary>
    public JsonObject Raw { get; }
    /// <summary>
    /// Additional unmapped fields from the payload.
    /// </summary>
    public JsonObject? Additional { get; }

    /// <summary>
    /// Parses a credits snapshot from JSON.
    /// </summary>
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
    /// Serializes the credits snapshot back to JSON.
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
/// A single credit usage event.
/// </summary>
public sealed class ChatGptCreditUsageEvent {
    public ChatGptCreditUsageEvent(string? date, string? productSurface, double? creditAmount, string? usageId, JsonObject raw,
        JsonObject? additional) {
        Date = date;
        ProductSurface = productSurface;
        CreditAmount = creditAmount;
        UsageId = usageId;
        Raw = raw;
        Additional = additional;
    }

    /// <summary>
    /// Event date (as returned by the service).
    /// </summary>
    public string? Date { get; }
    /// <summary>
    /// Product surface name (e.g., "cli").
    /// </summary>
    public string? ProductSurface { get; }
    /// <summary>
    /// Credit amount for the event.
    /// </summary>
    public double? CreditAmount { get; }
    /// <summary>
    /// Usage identifier (if provided).
    /// </summary>
    public string? UsageId { get; }
    /// <summary>
    /// Raw JSON payload from the service.
    /// </summary>
    public JsonObject Raw { get; }
    /// <summary>
    /// Additional unmapped fields from the payload.
    /// </summary>
    public JsonObject? Additional { get; }

    /// <summary>
    /// Parses a credit usage event from JSON.
    /// </summary>
    public static ChatGptCreditUsageEvent FromJson(JsonObject obj) {
        var date = obj.GetString("date");
        var surface = obj.GetString("product_surface") ?? obj.GetString("productSurface");
        var creditAmount = obj.GetDouble("credit_amount") ?? obj.GetDouble("creditAmount");
        var usageId = obj.GetString("usage_id") ?? obj.GetString("usageId");
        var additional = obj.ExtractAdditional("date", "product_surface", "productSurface", "credit_amount", "creditAmount", "usage_id", "usageId");
        return new ChatGptCreditUsageEvent(date, surface, creditAmount, usageId, obj, additional);
    }

    /// <summary>
    /// Serializes the credit usage event back to JSON.
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
        return obj;
    }
}

/// <summary>
/// Combined usage report with the latest snapshot and optional credit events.
/// </summary>
public sealed class ChatGptUsageReport {
    public ChatGptUsageReport(ChatGptUsageSnapshot snapshot, IReadOnlyList<ChatGptCreditUsageEvent> events) {
        Snapshot = snapshot;
        Events = events;
    }

    /// <summary>
    /// Current usage snapshot.
    /// </summary>
    public ChatGptUsageSnapshot Snapshot { get; }
    /// <summary>
    /// Recent credit usage events (if requested).
    /// </summary>
    public IReadOnlyList<ChatGptCreditUsageEvent> Events { get; }

    /// <summary>
    /// Serializes the report to JSON.
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
        return obj;
    }
}
