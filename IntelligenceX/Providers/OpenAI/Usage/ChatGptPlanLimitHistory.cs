using System;
using System.Collections.Generic;
using IntelligenceX.Json;

namespace IntelligenceX.OpenAI.Usage;

/// <summary>Allowance accounting history, including provider coverage and approximation metadata.</summary>
public sealed class ChatGptPlanLimitHistory {
    private ChatGptPlanLimitHistory(JsonObject obj) {
        DataAsOf = ChatGptResetCredits.ReadTimestamp(obj, "data_as_of");
        CoverageStart = ChatGptResetCredits.ReadTimestamp(obj, "coverage_start");
        CoverageComplete = ChatGptAnalyticsJson.Boolean(obj, "coverage_complete");
        Approximate = ChatGptAnalyticsJson.Boolean(obj, "approximate");
        BoundaryToleranceSeconds = ChatGptAnalyticsJson.Number(obj, "boundary_tolerance_seconds");
        Periods = ChatGptAnalyticsJson.Rows(obj, "periods", static row => new ChatGptPlanLimitPeriod(row));
    }
    /// <summary>Time through which the provider's accounting is current.</summary>
    public DateTimeOffset? DataAsOf { get; }
    /// <summary>Beginning of available provider coverage.</summary>
    public DateTimeOffset? CoverageStart { get; }
    /// <summary>Whether coverage is complete; null means unknown.</summary>
    public bool? CoverageComplete { get; }
    /// <summary>Whether accounting is approximate; null means unknown.</summary>
    public bool? Approximate { get; }
    /// <summary>Provider accounting boundary tolerance in seconds.</summary>
    public double? BoundaryToleranceSeconds { get; }
    /// <summary>Returned accounting periods, which may be shortened by resets.</summary>
    public IReadOnlyList<ChatGptPlanLimitPeriod> Periods { get; }
    /// <summary>Parses the provider history response.</summary>
    public static ChatGptPlanLimitHistory FromJson(JsonObject obj) => new(obj);
}

/// <summary>One provider allowance accounting period.</summary>
public sealed class ChatGptPlanLimitPeriod {
    internal ChatGptPlanLimitPeriod(JsonObject obj) {
        Id = obj.GetString("id"); PlanType = obj.GetString("plan_type"); WindowMinutes = obj.GetInt64("window_minutes");
        StartsAt = ChatGptResetCredits.ReadTimestamp(obj, "starts_at"); EndsAt = ChatGptResetCredits.ReadTimestamp(obj, "ends_at");
        AccountingComplete = ChatGptAnalyticsJson.Boolean(obj, "accounting_complete");
        UsedBasisPoints = ChatGptAnalyticsJson.Number(obj, "used_basis_points");
        Breakdowns = ChatGptAnalyticsJson.Rows(obj, "breakdowns", static row => new ChatGptPlanLimitBreakdown(row));
    }
    /// <summary>Provider period identifier.</summary>
    public string? Id { get; }
    /// <summary>Provider plan identifier.</summary>
    public string? PlanType { get; }
    /// <summary>Allowance duration, distinct from actual accounting period length.</summary>
    public long? WindowMinutes { get; }
    /// <summary>Actual provider accounting period start.</summary>
    public DateTimeOffset? StartsAt { get; }
    /// <summary>Actual provider accounting period end.</summary>
    public DateTimeOffset? EndsAt { get; }
    /// <summary>Whether this period's accounting is complete.</summary>
    public bool? AccountingComplete { get; }
    /// <summary>Consumption in basis points; 100 basis points is one percentage point.</summary>
    public double? UsedBasisPoints { get; }
    /// <summary>Consumption as a percentage of the full allowance, without clamping.</summary>
    public double? UsedPercent => UsedBasisPoints / 100d;
    /// <summary>Feature, trigger, model or surface breakdowns.</summary>
    public IReadOnlyList<ChatGptPlanLimitBreakdown> Breakdowns { get; }
}

/// <summary>One dimension of provider allowance accounting.</summary>
public sealed class ChatGptPlanLimitBreakdown {
    internal ChatGptPlanLimitBreakdown(JsonObject obj) {
        Dimension = obj.GetString("dimension");
        Rows = ChatGptAnalyticsJson.Rows(obj, "rows", static row => new ChatGptPlanLimitBreakdownRow(row));
    }
    /// <summary>Provider breakdown dimension.</summary>
    public string? Dimension { get; }
    /// <summary>Measurements within this dimension.</summary>
    public IReadOnlyList<ChatGptPlanLimitBreakdownRow> Rows { get; }
}

/// <summary>A category's allowance consumption.</summary>
public sealed class ChatGptPlanLimitBreakdownRow {
    internal ChatGptPlanLimitBreakdownRow(JsonObject obj) { Key = obj.GetString("key"); BasisPoints = ChatGptAnalyticsJson.Number(obj, "basis_points"); }
    /// <summary>Provider category key.</summary>
    public string? Key { get; }
    /// <summary>Consumption in basis points, or null when unknown.</summary>
    public double? BasisPoints { get; }
    /// <summary>Consumption in percentage points of the full allowance.</summary>
    public double? UsedPercent => BasisPoints / 100d;
}
