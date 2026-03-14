using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using IntelligenceX.Json;
using IntelligenceX.OpenAI;
using IntelligenceX.Telemetry.Usage;

namespace IntelligenceX.Visualization.Heatmaps;

#pragma warning disable CS1591

/// <summary>
/// Configures provider-neutral usage overview generation from canonical telemetry events.
/// </summary>
public sealed class UsageTelemetryOverviewOptions {
    /// <summary>
    /// Gets or sets the metric summarized by the overview.
    /// </summary>
    public UsageSummaryMetric Metric { get; set; } = UsageSummaryMetric.TotalTokens;

    /// <summary>
    /// Gets or sets the overview title.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets an optional overview subtitle prefix.
    /// </summary>
    public string? Subtitle { get; set; }

    /// <summary>
    /// Gets or sets the breakdowns rendered as embedded heatmaps.
    /// </summary>
    public IReadOnlyList<UsageHeatmapBreakdownDimension>? HeatmapBreakdowns { get; set; } = new[] {
        UsageHeatmapBreakdownDimension.Surface,
        UsageHeatmapBreakdownDimension.Provider,
        UsageHeatmapBreakdownDimension.Model,
        UsageHeatmapBreakdownDimension.SourceRoot,
        UsageHeatmapBreakdownDimension.Account,
        UsageHeatmapBreakdownDimension.Person
    };

    /// <summary>
    /// Gets or sets optional display-label overrides for source roots.
    /// </summary>
    public IReadOnlyDictionary<string, string>? SourceRootLabels { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of legend entries emitted per heatmap.
    /// </summary>
    public int LegendLimit { get; set; } = 5;

    /// <summary>
    /// Gets or sets the maximum number of breakdown entries retained in the summary.
    /// </summary>
    public int BreakdownLimit { get; set; } = 5;

    /// <summary>
    /// Gets or sets the rolling windows retained in the summary.
    /// </summary>
    public IReadOnlyList<int>? RollingWindowDays { get; set; } = new[] { 7, 30 };
}

/// <summary>
/// Represents one metric card in a usage overview.
/// </summary>
public sealed class UsageTelemetryOverviewCard {
    public UsageTelemetryOverviewCard(string key, string label, string value, string? subtitle = null) {
        Key = string.IsNullOrWhiteSpace(key) ? "card" : key.Trim();
        Label = string.IsNullOrWhiteSpace(label) ? "Card" : label.Trim();
        Value = string.IsNullOrWhiteSpace(value) ? "0" : value.Trim();
        Subtitle = HeatmapText.NormalizeOptionalText(subtitle);
    }

    public string Key { get; }
    public string Label { get; }
    public string Value { get; }
    public string? Subtitle { get; }

    public JsonObject ToJson() {
        var obj = new JsonObject()
            .Add("key", Key)
            .Add("label", Label)
            .Add("value", Value);
        if (!string.IsNullOrWhiteSpace(Subtitle)) {
            obj.Add("subtitle", Subtitle);
        }
        return obj;
    }
}

/// <summary>
/// Represents one named heatmap in a usage overview document.
/// </summary>
public sealed class UsageTelemetryOverviewHeatmap {
    public UsageTelemetryOverviewHeatmap(string key, string label, HeatmapDocument document) {
        Key = string.IsNullOrWhiteSpace(key) ? "heatmap" : key.Trim();
        Label = string.IsNullOrWhiteSpace(label) ? "Heatmap" : label.Trim();
        Document = document ?? throw new ArgumentNullException(nameof(document));
    }

    public string Key { get; }
    public string Label { get; }
    public HeatmapDocument Document { get; }

    public JsonObject ToJson() {
        return new JsonObject()
            .Add("key", Key)
            .Add("label", Label)
            .Add("document", Document.ToJson());
    }
}

/// <summary>
/// Represents one ranked model usage callout in a provider overview.
/// </summary>
public sealed class UsageTelemetryOverviewModelHighlight {
    public UsageTelemetryOverviewModelHighlight(string model, long totalTokens) {
        Model = string.IsNullOrWhiteSpace(model) ? "unknown-model" : model.Trim();
        TotalTokens = Math.Max(0L, totalTokens);
    }

    public string Model { get; }
    public long TotalTokens { get; }

    public JsonObject ToJson() {
        return new JsonObject()
            .Add("model", Model)
            .Add("totalTokens", TotalTokens);
    }
}

/// <summary>
/// Represents one monthly provider usage point in the overview.
/// </summary>
public sealed class UsageTelemetryOverviewMonthlyUsage {
    public UsageTelemetryOverviewMonthlyUsage(DateTime monthUtc, long totalValue, int activeDays) {
        MonthUtc = new DateTime(monthUtc.Year, monthUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        TotalValue = Math.Max(0L, totalValue);
        ActiveDays = Math.Max(0, activeDays);
    }

    public DateTime MonthUtc { get; }
    public long TotalValue { get; }
    public int ActiveDays { get; }
    public string Label => MonthUtc.ToString("MMM", CultureInfo.InvariantCulture);
    public string Key => MonthUtc.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    public JsonObject ToJson() {
        return new JsonObject()
            .Add("monthUtc", MonthUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            .Add("key", Key)
            .Add("label", Label)
            .Add("totalValue", TotalValue)
            .Add("activeDays", ActiveDays);
    }
}

/// <summary>
/// Represents one primary provider metric shown in the provider header.
/// </summary>
public sealed class UsageTelemetryOverviewSectionMetric {
    public UsageTelemetryOverviewSectionMetric(
        string key,
        string label,
        string value,
        string? subtitle,
        double? ratio,
        string color) {
        Key = string.IsNullOrWhiteSpace(key) ? "metric" : key.Trim();
        Label = string.IsNullOrWhiteSpace(label) ? "Metric" : label.Trim();
        Value = string.IsNullOrWhiteSpace(value) ? "0" : value.Trim();
        Subtitle = HeatmapText.NormalizeOptionalText(subtitle);
        Ratio = ratio.HasValue ? Math.Max(0d, Math.Min(1d, ratio.Value)) : null;
        Color = string.IsNullOrWhiteSpace(color) ? "#888888" : color.Trim();
    }

    public string Key { get; }
    public string Label { get; }
    public string Value { get; }
    public string? Subtitle { get; }
    public double? Ratio { get; }
    public string Color { get; }

    public JsonObject ToJson() {
        var obj = new JsonObject()
            .Add("key", Key)
            .Add("label", Label)
            .Add("value", Value)
            .Add("color", Color);
        if (!string.IsNullOrWhiteSpace(Subtitle)) {
            obj.Add("subtitle", Subtitle);
        }
        if (Ratio.HasValue) {
            obj.Add("ratio", Ratio.Value);
        }
        return obj;
    }
}

/// <summary>
/// Represents one composition slice inside a provider summary.
/// </summary>
public sealed class UsageTelemetryOverviewCompositionItem {
    public UsageTelemetryOverviewCompositionItem(
        string key,
        string label,
        string value,
        string? subtitle,
        double? ratio,
        string color) {
        Key = string.IsNullOrWhiteSpace(key) ? "item" : key.Trim();
        Label = string.IsNullOrWhiteSpace(label) ? "Item" : label.Trim();
        Value = string.IsNullOrWhiteSpace(value) ? "0" : value.Trim();
        Subtitle = HeatmapText.NormalizeOptionalText(subtitle);
        Ratio = ratio.HasValue ? Math.Max(0d, Math.Min(1d, ratio.Value)) : null;
        Color = string.IsNullOrWhiteSpace(color) ? "#888888" : color.Trim();
    }

    public string Key { get; }
    public string Label { get; }
    public string Value { get; }
    public string? Subtitle { get; }
    public double? Ratio { get; }
    public string Color { get; }

    public JsonObject ToJson() {
        var obj = new JsonObject()
            .Add("key", Key)
            .Add("label", Label)
            .Add("value", Value)
            .Add("color", Color);
        if (!string.IsNullOrWhiteSpace(Subtitle)) {
            obj.Add("subtitle", Subtitle);
        }
        if (Ratio.HasValue) {
            obj.Add("ratio", Ratio.Value);
        }
        return obj;
    }
}

/// <summary>
/// Represents one composition summary card inside a provider section.
/// </summary>
public sealed class UsageTelemetryOverviewComposition {
    public UsageTelemetryOverviewComposition(
        string title,
        string copy,
        IReadOnlyList<UsageTelemetryOverviewCompositionItem> items) {
        Title = string.IsNullOrWhiteSpace(title) ? "Breakdown" : title.Trim();
        Copy = string.IsNullOrWhiteSpace(copy) ? string.Empty : copy.Trim();
        Items = items ?? Array.Empty<UsageTelemetryOverviewCompositionItem>();
    }

    public string Title { get; }
    public string Copy { get; }
    public IReadOnlyList<UsageTelemetryOverviewCompositionItem> Items { get; }

    public JsonObject ToJson() {
        var obj = new JsonObject()
            .Add("title", Title)
            .Add("copy", Copy);
        var items = new JsonArray();
        foreach (var item in Items) {
            items.Add(JsonValue.From(item.ToJson()));
        }
        obj.Add("items", items);
        return obj;
    }
}

/// <summary>
/// Represents one ranked model entry in a provider overview.
/// </summary>
public sealed class UsageTelemetryOverviewTopModel {
    public UsageTelemetryOverviewTopModel(string model, long totalTokens, double sharePercent) {
        Model = string.IsNullOrWhiteSpace(model) ? "unknown-model" : model.Trim();
        TotalTokens = Math.Max(0L, totalTokens);
        SharePercent = Math.Max(0d, sharePercent);
    }

    public string Model { get; }
    public long TotalTokens { get; }
    public double SharePercent { get; }

    public JsonObject ToJson() {
        return new JsonObject()
            .Add("model", Model)
            .Add("totalTokens", TotalTokens)
            .Add("sharePercent", SharePercent);
    }
}

/// <summary>
/// Represents one API pricing cost driver in a provider overview.
/// </summary>
public sealed class UsageTelemetryOverviewCostDriver {
    public UsageTelemetryOverviewCostDriver(string model, decimal estimatedCostUsd, double sharePercent) {
        Model = string.IsNullOrWhiteSpace(model) ? "unknown-model" : model.Trim();
        EstimatedCostUsd = estimatedCostUsd < 0m ? 0m : estimatedCostUsd;
        SharePercent = Math.Max(0d, sharePercent);
    }

    public string Model { get; }
    public decimal EstimatedCostUsd { get; }
    public double SharePercent { get; }

    public JsonObject ToJson() {
        return new JsonObject()
            .Add("model", Model)
            .Add("estimatedCostUsd", JsonValue.From((double)EstimatedCostUsd))
            .Add("sharePercent", SharePercent);
    }
}

/// <summary>
/// Represents an estimated API-route pricing summary for one provider section.
/// </summary>
public sealed class UsageTelemetryOverviewApiCostEstimate {
    public UsageTelemetryOverviewApiCostEstimate(
        decimal totalEstimatedCostUsd,
        long coveredTokens,
        long uncoveredTokens,
        IReadOnlyList<UsageTelemetryOverviewCostDriver> topDrivers) {
        TotalEstimatedCostUsd = totalEstimatedCostUsd < 0m ? 0m : totalEstimatedCostUsd;
        CoveredTokens = Math.Max(0L, coveredTokens);
        UncoveredTokens = Math.Max(0L, uncoveredTokens);
        TopDrivers = topDrivers ?? Array.Empty<UsageTelemetryOverviewCostDriver>();
    }

    public decimal TotalEstimatedCostUsd { get; }
    public long CoveredTokens { get; }
    public long UncoveredTokens { get; }
    public IReadOnlyList<UsageTelemetryOverviewCostDriver> TopDrivers { get; }

    public JsonObject ToJson() {
        var obj = new JsonObject()
            .Add("totalEstimatedCostUsd", JsonValue.From((double)TotalEstimatedCostUsd))
            .Add("coveredTokens", CoveredTokens)
            .Add("uncoveredTokens", UncoveredTokens);

        var drivers = new JsonArray();
        foreach (var driver in TopDrivers) {
            drivers.Add(JsonValue.From(driver.ToJson()));
        }
        obj.Add("topDrivers", drivers);
        return obj;
    }
}

/// <summary>
/// Represents one row inside a provider-specific insight section.
/// </summary>
public sealed class UsageTelemetryOverviewInsightRow {
    public UsageTelemetryOverviewInsightRow(
        string label,
        string value,
        string? subtitle = null,
        double? ratio = null,
        string? href = null) {
        Label = string.IsNullOrWhiteSpace(label) ? "Item" : label.Trim();
        Value = string.IsNullOrWhiteSpace(value) ? "0" : value.Trim();
        Subtitle = HeatmapText.NormalizeOptionalText(subtitle);
        Ratio = ratio.HasValue ? Math.Max(0d, Math.Min(1d, ratio.Value)) : null;
        Href = HeatmapText.NormalizeOptionalText(href);
    }

    public string Label { get; }
    public string Value { get; }
    public string? Subtitle { get; }
    public double? Ratio { get; }
    public string? Href { get; }

    public JsonObject ToJson() {
        var obj = new JsonObject()
            .Add("label", Label)
            .Add("value", Value);
        if (!string.IsNullOrWhiteSpace(Subtitle)) {
            obj.Add("subtitle", Subtitle);
        }
        if (Ratio.HasValue) {
            obj.Add("ratio", Ratio.Value);
        }
        if (!string.IsNullOrWhiteSpace(Href)) {
            obj.Add("href", Href);
        }
        return obj;
    }
}

/// <summary>
/// Represents one provider-specific insight section.
/// </summary>
public sealed class UsageTelemetryOverviewInsightSection {
    public UsageTelemetryOverviewInsightSection(
        string key,
        string title,
        string? headline,
        string? note,
        IReadOnlyList<UsageTelemetryOverviewInsightRow> rows) {
        Key = string.IsNullOrWhiteSpace(key) ? "insight" : key.Trim();
        Title = string.IsNullOrWhiteSpace(title) ? "Insight" : title.Trim();
        Headline = HeatmapText.NormalizeOptionalText(headline);
        Note = HeatmapText.NormalizeOptionalText(note);
        Rows = rows ?? Array.Empty<UsageTelemetryOverviewInsightRow>();
    }

    public string Key { get; }
    public string Title { get; }
    public string? Headline { get; }
    public string? Note { get; }
    public IReadOnlyList<UsageTelemetryOverviewInsightRow> Rows { get; }

    public JsonObject ToJson() {
        var obj = new JsonObject()
            .Add("key", Key)
            .Add("title", Title)
            .Add("rows", ToJsonArray(Rows, static row => row.ToJson()));
        if (!string.IsNullOrWhiteSpace(Headline)) {
            obj.Add("headline", Headline);
        }
        if (!string.IsNullOrWhiteSpace(Note)) {
            obj.Add("note", Note);
        }
        return obj;
    }

    private static JsonArray ToJsonArray<T>(IReadOnlyList<T> values, Func<T, JsonObject> projector) {
        var array = new JsonArray();
        foreach (var value in values) {
            array.Add(JsonValue.From(projector(value)));
        }
        return array;
    }
}

/// <summary>
/// Represents one provider-specific usage section in the overview.
/// </summary>
public sealed class UsageTelemetryOverviewProviderSection {
    public UsageTelemetryOverviewProviderSection(
        string key,
        string providerId,
        string title,
        string subtitle,
        HeatmapDocument heatmap,
        IReadOnlyList<UsageTelemetryOverviewSectionMetric> metrics,
        UsageTelemetryOverviewComposition? composition,
        IReadOnlyList<UsageTelemetryOverviewCard> spotlightCards,
        long inputTokens,
        long outputTokens,
        long totalTokens,
        string monthlyUsageTitle,
        string monthlyUsageUnitsLabel,
        IReadOnlyList<UsageTelemetryOverviewMonthlyUsage> monthlyUsage,
        IReadOnlyList<UsageTelemetryOverviewInsightSection>? additionalInsights,
        IReadOnlyList<UsageTelemetryOverviewTopModel> topModels,
        UsageTelemetryOverviewApiCostEstimate? apiCostEstimate,
        UsageTelemetryOverviewModelHighlight? mostUsedModel,
        UsageTelemetryOverviewModelHighlight? recentModel,
        int longestStreakDays,
        int currentStreakDays,
        string? note) {
        Key = string.IsNullOrWhiteSpace(key) ? "provider" : key.Trim();
        ProviderId = string.IsNullOrWhiteSpace(providerId) ? "unknown-provider" : providerId.Trim();
        Title = string.IsNullOrWhiteSpace(title) ? ProviderId : title.Trim();
        Subtitle = string.IsNullOrWhiteSpace(subtitle) ? "No range" : subtitle.Trim();
        Heatmap = heatmap ?? throw new ArgumentNullException(nameof(heatmap));
        Metrics = metrics ?? Array.Empty<UsageTelemetryOverviewSectionMetric>();
        Composition = composition;
        SpotlightCards = spotlightCards ?? Array.Empty<UsageTelemetryOverviewCard>();
        InputTokens = Math.Max(0L, inputTokens);
        OutputTokens = Math.Max(0L, outputTokens);
        TotalTokens = Math.Max(0L, totalTokens);
        MonthlyUsageTitle = string.IsNullOrWhiteSpace(monthlyUsageTitle) ? "Monthly usage" : monthlyUsageTitle.Trim();
        MonthlyUsageUnitsLabel = string.IsNullOrWhiteSpace(monthlyUsageUnitsLabel) ? "units" : monthlyUsageUnitsLabel.Trim();
        MonthlyUsage = monthlyUsage ?? Array.Empty<UsageTelemetryOverviewMonthlyUsage>();
        AdditionalInsights = additionalInsights ?? Array.Empty<UsageTelemetryOverviewInsightSection>();
        TopModels = topModels ?? Array.Empty<UsageTelemetryOverviewTopModel>();
        ApiCostEstimate = apiCostEstimate;
        MostUsedModel = mostUsedModel;
        RecentModel = recentModel;
        LongestStreakDays = Math.Max(0, longestStreakDays);
        CurrentStreakDays = Math.Max(0, currentStreakDays);
        Note = HeatmapText.NormalizeOptionalText(note);
    }

    public string Key { get; }
    public string ProviderId { get; }
    public string Title { get; }
    public string Subtitle { get; }
    public HeatmapDocument Heatmap { get; }
    public IReadOnlyList<UsageTelemetryOverviewSectionMetric> Metrics { get; }
    public UsageTelemetryOverviewComposition? Composition { get; }
    public IReadOnlyList<UsageTelemetryOverviewCard> SpotlightCards { get; }
    public long InputTokens { get; }
    public long OutputTokens { get; }
    public long TotalTokens { get; }
    public string MonthlyUsageTitle { get; }
    public string MonthlyUsageUnitsLabel { get; }
    public IReadOnlyList<UsageTelemetryOverviewMonthlyUsage> MonthlyUsage { get; }
    public IReadOnlyList<UsageTelemetryOverviewInsightSection> AdditionalInsights { get; }
    public IReadOnlyList<UsageTelemetryOverviewTopModel> TopModels { get; }
    public UsageTelemetryOverviewApiCostEstimate? ApiCostEstimate { get; }
    public UsageTelemetryOverviewModelHighlight? MostUsedModel { get; }
    public UsageTelemetryOverviewModelHighlight? RecentModel { get; }
    public int LongestStreakDays { get; }
    public int CurrentStreakDays { get; }
    public string? Note { get; }

    public JsonObject ToJson() {
        var obj = new JsonObject()
            .Add("key", Key)
            .Add("providerId", ProviderId)
            .Add("title", Title)
            .Add("subtitle", Subtitle)
            .Add("metrics", ToJsonArray(Metrics, static metric => metric.ToJson()))
            .Add("spotlightCards", ToJsonArray(SpotlightCards, static card => card.ToJson()))
            .Add("inputTokens", InputTokens)
            .Add("outputTokens", OutputTokens)
            .Add("totalTokens", TotalTokens)
            .Add("monthlyUsageTitle", MonthlyUsageTitle)
            .Add("monthlyUsageUnitsLabel", MonthlyUsageUnitsLabel)
            .Add("longestStreakDays", LongestStreakDays)
            .Add("currentStreakDays", CurrentStreakDays)
            .Add("heatmap", Heatmap.ToJson());

        obj.Add("monthlyUsage", ToJsonArray(MonthlyUsage, static month => month.ToJson()));
        obj.Add("additionalInsights", ToJsonArray(AdditionalInsights, static insight => insight.ToJson()));
        obj.Add("topModels", ToJsonArray(TopModels, static model => model.ToJson()));

        if (MostUsedModel is not null) {
            obj.Add("mostUsedModel", MostUsedModel.ToJson());
        }
        if (RecentModel is not null) {
            obj.Add("recentModel", RecentModel.ToJson());
        }
        if (Composition is not null) {
            obj.Add("composition", Composition.ToJson());
        }
        if (ApiCostEstimate is not null) {
            obj.Add("apiCostEstimate", ApiCostEstimate.ToJson());
        }
        if (!string.IsNullOrWhiteSpace(Note)) {
            obj.Add("note", Note);
        }

        return obj;
    }

    private static JsonArray ToJsonArray<T>(IReadOnlyList<T> values, Func<T, JsonObject> projector) {
        var array = new JsonArray();
        foreach (var value in values) {
            array.Add(JsonValue.From(projector(value)));
        }
        return array;
    }
}

/// <summary>
/// Represents a reusable overview snapshot backed by canonical telemetry usage data.
/// </summary>
public sealed class UsageTelemetryOverviewDocument {
    public UsageTelemetryOverviewDocument(
        string title,
        string? subtitle,
        UsageSummaryMetric metric,
        string units,
        UsageSummarySnapshot summary,
        IReadOnlyList<UsageTelemetryOverviewCard> cards,
        IReadOnlyList<UsageTelemetryOverviewHeatmap> heatmaps,
        IReadOnlyList<UsageTelemetryOverviewProviderSection>? providerSections = null) {
        Title = string.IsNullOrWhiteSpace(title) ? "Usage Overview" : title.Trim();
        Subtitle = HeatmapText.NormalizeOptionalText(subtitle);
        Metric = metric;
        Units = string.IsNullOrWhiteSpace(units) ? "tokens" : units.Trim();
        Summary = summary ?? throw new ArgumentNullException(nameof(summary));
        Cards = cards ?? Array.Empty<UsageTelemetryOverviewCard>();
        Heatmaps = heatmaps ?? Array.Empty<UsageTelemetryOverviewHeatmap>();
        ProviderSections = providerSections ?? Array.Empty<UsageTelemetryOverviewProviderSection>();
    }

    public string Title { get; }
    public string? Subtitle { get; }
    public UsageSummaryMetric Metric { get; }
    public string Units { get; }
    public UsageSummarySnapshot Summary { get; }
    public IReadOnlyList<UsageTelemetryOverviewCard> Cards { get; }
    public IReadOnlyList<UsageTelemetryOverviewHeatmap> Heatmaps { get; }
    public IReadOnlyList<UsageTelemetryOverviewProviderSection> ProviderSections { get; }

    public JsonObject ToJson() {
        var obj = new JsonObject()
            .Add("title", Title)
            .Add("metric", Metric.ToString())
            .Add("units", Units)
            .Add("summary", ToJson(Summary));

        if (!string.IsNullOrWhiteSpace(Subtitle)) {
            obj.Add("subtitle", Subtitle);
        }

        var cards = new JsonArray();
        foreach (var card in Cards) {
            cards.Add(JsonValue.From(card.ToJson()));
        }
        obj.Add("cards", cards);

        var heatmaps = new JsonArray();
        foreach (var heatmap in Heatmaps) {
            heatmaps.Add(JsonValue.From(heatmap.ToJson()));
        }
        obj.Add("heatmaps", heatmaps);

        var providerSections = new JsonArray();
        foreach (var providerSection in ProviderSections) {
            providerSections.Add(JsonValue.From(providerSection.ToJson()));
        }
        obj.Add("providerSections", providerSections);

        return obj;
    }

    private static JsonObject ToJson(UsageSummarySnapshot summary) {
        var obj = new JsonObject()
            .Add("metric", summary.Metric.ToString())
            .Add("startDayUtc", summary.StartDayUtc?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            .Add("endDayUtc", summary.EndDayUtc?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            .Add("totalValue", (double)summary.TotalValue)
            .Add("totalDays", summary.TotalDays)
            .Add("activeDays", summary.ActiveDays)
            .Add("averagePerCalendarDay", (double)summary.AveragePerCalendarDay)
            .Add("averagePerActiveDay", (double)summary.AveragePerActiveDay)
            .Add("peakDayUtc", summary.PeakDayUtc?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            .Add("peakValue", (double)summary.PeakValue);

        obj.Add("rollingWindows", ToJson(summary.RollingWindows));
        obj.Add("providerBreakdown", ToJson(summary.ProviderBreakdown));
        obj.Add("accountBreakdown", ToJson(summary.AccountBreakdown));
        obj.Add("personBreakdown", ToJson(summary.PersonBreakdown));
        obj.Add("modelBreakdown", ToJson(summary.ModelBreakdown));
        obj.Add("sourceRootBreakdown", ToJson(summary.SourceRootBreakdown));
        obj.Add("surfaceBreakdown", ToJson(summary.SurfaceBreakdown));
        return obj;
    }

    private static JsonArray ToJson(IEnumerable<UsageRollingWindowSummary> values) {
        var array = new JsonArray();
        foreach (var value in values) {
            array.Add(new JsonObject()
                .Add("windowDays", value.WindowDays)
                .Add("startDayUtc", value.StartDayUtc?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                .Add("endDayUtc", value.EndDayUtc?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                .Add("daysCovered", value.DaysCovered)
                .Add("totalValue", (double)value.TotalValue)
                .Add("averagePerCalendarDay", (double)value.AveragePerCalendarDay));
        }
        return array;
    }

    private static JsonArray ToJson(IEnumerable<UsageSummaryBreakdownEntry> values) {
        var array = new JsonArray();
        foreach (var value in values) {
            array.Add(new JsonObject()
                .Add("key", value.Key)
                .Add("value", (double)value.Value));
        }
        return array;
    }
}

/// <summary>
/// Builds reusable overview snapshots with summary cards plus heatmaps from canonical telemetry events.
/// </summary>
public sealed class UsageTelemetryOverviewBuilder {
    private const int ProviderTrailingWindowDays = 364;
    private static readonly IReadOnlyDictionary<string, UsageTelemetryApiPrice> ApiPricingByModel =
        new Dictionary<string, UsageTelemetryApiPrice>(StringComparer.OrdinalIgnoreCase) {
            ["gpt-5.4"] = new(2.50m, 0.25m, 15m),
            ["gpt-5.4-codex"] = new(2.50m, 0.25m, 15m),
            ["gpt-5.3"] = new(1.75m, 0.175m, 14m),
            ["gpt-5.3-codex"] = new(1.75m, 0.175m, 14m),
            ["gpt-5.2"] = new(1.75m, 0.175m, 14m),
            ["gpt-5.2-codex"] = new(1.75m, 0.175m, 14m),
            ["gpt-5.1"] = new(1.25m, 0.125m, 10m),
            ["gpt-5.1-codex"] = new(1.25m, 0.125m, 10m),
            ["gpt-5-codex"] = new(1.25m, 0.125m, 10m),
            ["gpt-5.1-codex-max"] = new(1.25m, 0.125m, 10m),
            ["gpt-5.1-codex-mini"] = new(0.25m, 0.025m, 2m)
        };

    /// <summary>
    /// Builds an overview document from canonical telemetry events.
    /// </summary>
    public UsageTelemetryOverviewDocument Build(
        IEnumerable<UsageEventRecord> events,
        UsageTelemetryOverviewOptions? options = null) {
        if (events is null) {
            throw new ArgumentNullException(nameof(events));
        }

        var eventList = events
            .Where(static record => record is not null)
            .OrderBy(static record => record.TimestampUtc)
            .ToArray();
        if (eventList.Length == 0) {
            throw new InvalidOperationException("No telemetry usage events were available for overview generation.");
        }

        var effectiveOptions = options ?? new UsageTelemetryOverviewOptions();
        var aggregates = new UsageDailyAggregateBuilder().Build(
            eventList,
            new UsageDailyAggregateOptions {
                Dimensions = UsageAggregateDimensions.Provider
                             | UsageAggregateDimensions.Account
                             | UsageAggregateDimensions.Person
                             | UsageAggregateDimensions.Model
                             | UsageAggregateDimensions.Surface
            });
        var summary = new UsageSummaryBuilder().Build(
            aggregates,
            new UsageSummaryOptions {
                Metric = effectiveOptions.Metric,
                BreakdownLimit = Math.Max(1, effectiveOptions.BreakdownLimit),
                RollingWindowDays = effectiveOptions.RollingWindowDays
            });

        var title = NormalizeOptional(effectiveOptions.Title) ?? "Usage Overview";
        var subtitle = BuildOverviewSubtitle(summary, effectiveOptions);
        var units = ResolveUnitsLabel(effectiveOptions.Metric);
        var cards = BuildCards(summary, effectiveOptions.Metric).ToArray();
        var heatmaps = BuildHeatmaps(eventList, summary, title, effectiveOptions).ToArray();
        var providerSections = effectiveOptions.Metric == UsageSummaryMetric.TotalTokens
            ? BuildProviderSections(eventList)
            : Array.Empty<UsageTelemetryOverviewProviderSection>();

        return new UsageTelemetryOverviewDocument(
            title,
            subtitle,
            effectiveOptions.Metric,
            units,
            summary,
            cards,
            heatmaps,
            providerSections);
    }

    private static IReadOnlyList<UsageTelemetryOverviewProviderSection> BuildProviderSections(
        IReadOnlyList<UsageEventRecord> events) {
        return events
            .GroupBy(static record => NormalizeOptional(record.ProviderId) ?? "unknown-provider", StringComparer.OrdinalIgnoreCase)
            .Select(BuildProviderSection)
            .OrderByDescending(static section => section.TotalTokens)
            .ThenBy(static section => section.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static UsageTelemetryOverviewProviderSection BuildProviderSection(
        IGrouping<string, UsageEventRecord> providerGroup) {
        var providerId = providerGroup.Key;
        var allEvents = providerGroup
            .Where(static record => record is not null)
            .OrderBy(static record => record.TimestampUtc)
            .ToArray();
        var latestDayUtc = allEvents.Length == 0
            ? DateTime.UtcNow.Date
            : allEvents[allEvents.Length - 1].TimestampUtc.UtcDateTime.Date;
        var rangeEndUtc = latestDayUtc;
        var rangeStartUtc = latestDayUtc.AddDays(-(ProviderTrailingWindowDays - 1));
        var events = allEvents
            .Where(record => record.TimestampUtc.UtcDateTime.Date >= rangeStartUtc
                             && record.TimestampUtc.UtcDateTime.Date <= rangeEndUtc)
            .ToArray();

        var title = ResolveProviderTitle(providerId);
        var subtitle = BuildRangeLabel(rangeStartUtc, rangeEndUtc);
        var inputTokens = events.Sum(static record => record.InputTokens ?? 0L);
        var outputTokens = events.Sum(static record => record.OutputTokens ?? 0L);
        var totalTokens = events.Sum(static record => record.TotalTokens ?? 0L);
        var metrics = BuildTokenMetrics(inputTokens, outputTokens, totalTokens, providerId);
        var composition = BuildTokenComposition(inputTokens, outputTokens, totalTokens, providerId);
        var monthlyUsage = BuildMonthlyUsage(events, rangeStartUtc, rangeEndUtc);
        var topModels = BuildTopModels(events, 5);
        var apiCostEstimate = BuildApiCostEstimate(events, 5);
        var mostUsedModel = BuildModelHighlight(events);
        var recentModel = BuildModelHighlight(FilterToRecentWindow(events, 30));
        var (longestStreakDays, currentStreakDays) = ComputeStreaks(events);
        var note = BuildCoverageNote(events);
        var spotlightCards = BuildTelemetrySpotlightCards(mostUsedModel, recentModel, longestStreakDays, currentStreakDays);

        var heatmap = BuildProviderHeatmap(title, providerId, events, rangeStartUtc, rangeEndUtc);

        return new UsageTelemetryOverviewProviderSection(
            key: "provider-" + NormalizeKey(providerId),
            providerId: providerId,
            title: title,
            subtitle: subtitle,
            heatmap: heatmap,
            metrics: metrics,
            composition: composition,
            spotlightCards: spotlightCards,
            inputTokens: inputTokens,
            outputTokens: outputTokens,
            totalTokens: totalTokens,
            monthlyUsageTitle: "Monthly usage",
            monthlyUsageUnitsLabel: "tokens",
            monthlyUsage: monthlyUsage,
            additionalInsights: Array.Empty<UsageTelemetryOverviewInsightSection>(),
            topModels: topModels,
            apiCostEstimate: apiCostEstimate,
            mostUsedModel: mostUsedModel,
            recentModel: recentModel,
            longestStreakDays: longestStreakDays,
            currentStreakDays: currentStreakDays,
            note: note);
    }

    private static HeatmapDocument BuildProviderHeatmap(
        string title,
        string providerId,
        IReadOnlyList<UsageEventRecord> events,
        DateTime rangeStartUtc,
        DateTime rangeEndUtc) {
        var aggregates = new UsageDailyAggregateBuilder().Build(
            events,
            new UsageDailyAggregateOptions {
                Dimensions = UsageAggregateDimensions.None
            });

        return new UsageHeatmapDocumentBuilder().Build(
            aggregates,
            new UsageHeatmapDocumentOptions {
                Title = title + " activity",
                Subtitle = null,
                Units = "tokens",
                Metric = UsageHeatmapMetric.TotalTokens,
                BreakdownDimension = UsageHeatmapBreakdownDimension.None,
                Palette = ResolveProviderPalette(providerId),
                WeekStart = DayOfWeek.Monday,
                LegendLowLabel = "Less",
                LegendHighLabel = "More",
                ShowIntensityLegend = true,
                LegendEntries = Array.Empty<UsageHeatmapLegendEntry>(),
                ShowDocumentHeader = false,
                ShowSectionHeaders = false,
                CompactWeekdayLabels = true,
                GroupSectionsByYear = false,
                RangeStartUtc = rangeStartUtc,
                RangeEndUtc = rangeEndUtc
            });
    }

    private static IReadOnlyList<UsageTelemetryOverviewMonthlyUsage> BuildMonthlyUsage(
        IReadOnlyList<UsageEventRecord> events,
        DateTime rangeStartUtc,
        DateTime rangeEndUtc) {
        var monthLookup = events
            .GroupBy(record => new DateTime(
                record.TimestampUtc.UtcDateTime.Year,
                record.TimestampUtc.UtcDateTime.Month,
                1,
                0,
                0,
                0,
                DateTimeKind.Utc))
            .ToDictionary(
                static group => group.Key,
                group => new UsageTelemetryOverviewMonthlyUsage(
                    group.Key,
                    group.Sum(static record => record.TotalTokens ?? 0L),
                    group.Select(static record => record.TimestampUtc.UtcDateTime.Date).Distinct().Count()));

        var values = new List<UsageTelemetryOverviewMonthlyUsage>();
        var cursor = new DateTime(rangeStartUtc.Year, rangeStartUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var endMonth = new DateTime(rangeEndUtc.Year, rangeEndUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        while (cursor <= endMonth) {
            if (monthLookup.TryGetValue(cursor, out var month)) {
                values.Add(month);
            } else {
                values.Add(new UsageTelemetryOverviewMonthlyUsage(cursor, 0L, 0));
            }

            cursor = cursor.AddMonths(1);
        }

        return values;
    }

    private static IReadOnlyList<UsageTelemetryOverviewTopModel> BuildTopModels(
        IReadOnlyList<UsageEventRecord> events,
        int limit) {
        var totalTokens = events.Sum(static record => record.TotalTokens ?? 0L);
        return events
            .GroupBy(static record => NormalizeOptional(record.Model) ?? "unknown-model", StringComparer.OrdinalIgnoreCase)
            .Select(group => {
                var modelTotal = group.Sum(static record => record.TotalTokens ?? 0L);
                var share = totalTokens <= 0L ? 0d : modelTotal / (double)totalTokens * 100d;
                return new UsageTelemetryOverviewTopModel(group.Key, modelTotal, share);
            })
            .Where(static entry => entry.TotalTokens > 0L)
            .OrderByDescending(static entry => entry.TotalTokens)
            .ThenBy(static entry => entry.Model, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, limit))
            .ToArray();
    }

    private static UsageTelemetryOverviewApiCostEstimate? BuildApiCostEstimate(
        IReadOnlyList<UsageEventRecord> events,
        int driverLimit) {
        if (events.Count == 0) {
            return null;
        }

        var estimates = events
            .Select(EstimateEventApiCost)
            .ToArray();
        var totalEstimatedCost = estimates.Sum(static estimate => estimate.EstimatedCostUsd);
        var coveredTokens = estimates
            .Where(static estimate => estimate.HasKnownPricing)
            .Sum(static estimate => estimate.TotalTokens);
        var uncoveredTokens = estimates
            .Where(static estimate => !estimate.HasKnownPricing)
            .Sum(static estimate => estimate.TotalTokens);

        var topDrivers = estimates
            .Where(static estimate => estimate.HasKnownPricing)
            .GroupBy(static estimate => estimate.Model, StringComparer.OrdinalIgnoreCase)
            .Select(group => {
                var cost = group.Sum(static estimate => estimate.EstimatedCostUsd);
                var share = totalEstimatedCost <= 0m ? 0d : (double)(cost / totalEstimatedCost * 100m);
                return new UsageTelemetryOverviewCostDriver(group.Key, cost, share);
            })
            .Where(static driver => driver.EstimatedCostUsd > 0m)
            .OrderByDescending(static driver => driver.EstimatedCostUsd)
            .ThenBy(static driver => driver.Model, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, driverLimit))
            .ToArray();

        if (totalEstimatedCost <= 0m && coveredTokens <= 0L && uncoveredTokens <= 0L) {
            return null;
        }

        return new UsageTelemetryOverviewApiCostEstimate(
            totalEstimatedCost,
            coveredTokens,
            uncoveredTokens,
            topDrivers);
    }

    private static IReadOnlyList<UsageTelemetryOverviewSectionMetric> BuildTokenMetrics(
        long inputTokens,
        long outputTokens,
        long totalTokens,
        string providerId) {
        var accent = ResolveProviderAccentColors(providerId);
        return new[] {
            new UsageTelemetryOverviewSectionMetric(
                "input",
                "Input tokens",
                FormatCompact(inputTokens),
                FormatPercent(inputTokens, totalTokens) + " of section total",
                ComputeRatio(inputTokens, totalTokens),
                accent.Input),
            new UsageTelemetryOverviewSectionMetric(
                "output",
                "Output tokens",
                FormatCompact(outputTokens),
                FormatPercent(outputTokens, totalTokens) + " of section total",
                ComputeRatio(outputTokens, totalTokens),
                accent.Output),
            new UsageTelemetryOverviewSectionMetric(
                "total",
                "Total tokens",
                FormatCompact(totalTokens),
                "100% of section total",
                totalTokens > 0 ? 1d : 0d,
                accent.Total)
        };
    }

    private static UsageTelemetryOverviewComposition? BuildTokenComposition(
        long inputTokens,
        long outputTokens,
        long totalTokens,
        string providerId) {
        if (totalTokens <= 0L) {
            return null;
        }

        var otherTokens = Math.Max(0L, totalTokens - inputTokens - outputTokens);
        var accent = ResolveProviderAccentColors(providerId);
        var items = new List<UsageTelemetryOverviewCompositionItem> {
            new(
                "input",
                "Input",
                FormatCompact(inputTokens),
                FormatPercent(inputTokens, totalTokens),
                ComputeRatio(inputTokens, totalTokens),
                accent.Input),
            new(
                "output",
                "Output",
                FormatCompact(outputTokens),
                FormatPercent(outputTokens, totalTokens),
                ComputeRatio(outputTokens, totalTokens),
                accent.Output)
        };

        if (otherTokens > 0L) {
            items.Add(new UsageTelemetryOverviewCompositionItem(
                "other",
                "Other",
                FormatCompact(otherTokens),
                FormatPercent(otherTokens, totalTokens),
                ComputeRatio(otherTokens, totalTokens),
                accent.Other));
        }

        return new UsageTelemetryOverviewComposition(
            "Token mix",
            FormatCompact(totalTokens) + " total tokens across this provider section",
            items);
    }

    private static IReadOnlyList<UsageTelemetryOverviewCard> BuildTelemetrySpotlightCards(
        UsageTelemetryOverviewModelHighlight? mostUsedModel,
        UsageTelemetryOverviewModelHighlight? recentModel,
        int longestStreakDays,
        int currentStreakDays) {
        return new[] {
            new UsageTelemetryOverviewCard(
                "most-used-model",
                "Most Used Model",
                mostUsedModel is null ? "n/a" : mostUsedModel.Model,
                mostUsedModel is null ? null : FormatCompact(mostUsedModel.TotalTokens)),
            new UsageTelemetryOverviewCard(
                "recent-model",
                "Recent Use (Last 30 Days)",
                recentModel is null ? "n/a" : recentModel.Model,
                recentModel is null ? null : FormatCompact(recentModel.TotalTokens)),
            new UsageTelemetryOverviewCard(
                "longest-streak",
                "Longest Streak",
                HeatmapDisplayText.FormatDays(longestStreakDays)),
            new UsageTelemetryOverviewCard(
                "current-streak",
                "Current Streak",
                HeatmapDisplayText.FormatDays(currentStreakDays))
        };
    }

    private static UsageTelemetryApiCostEventEstimate EstimateEventApiCost(UsageEventRecord record) {
        var normalizedModel = NormalizePricingModelId(record.ProviderId, record.Model);
        var totalTokens = record.TotalTokens ?? 0L;
        if (!TryResolveApiPrice(normalizedModel, out var rate)) {
            return new UsageTelemetryApiCostEventEstimate(
                Model: normalizedModel,
                TotalTokens: totalTokens,
                EstimatedCostUsd: 0m,
                HasKnownPricing: false);
        }

        var inputTokens = Math.Max(0L, record.InputTokens ?? 0L);
        var cachedInputTokens = Math.Max(0L, record.CachedInputTokens ?? 0L);
        var outputTokens = Math.Max(0L, record.OutputTokens ?? 0L);
        var reasoningTokens = Math.Max(0L, record.ReasoningTokens ?? 0L);
        var effectiveOutputTokens = outputTokens + reasoningTokens;

        var estimatedCostUsd =
            ComputePerMillionCost(inputTokens, rate.InputUsdPerMillion) +
            ComputePerMillionCost(cachedInputTokens, rate.CachedInputUsdPerMillion ?? rate.InputUsdPerMillion) +
            ComputePerMillionCost(effectiveOutputTokens, rate.OutputUsdPerMillion);

        return new UsageTelemetryApiCostEventEstimate(
            Model: normalizedModel,
            TotalTokens: totalTokens,
            EstimatedCostUsd: estimatedCostUsd,
            HasKnownPricing: true);
    }

    private static decimal ComputePerMillionCost(long tokens, decimal usdPerMillion) {
        if (tokens <= 0L || usdPerMillion <= 0m) {
            return 0m;
        }

        return tokens / 1_000_000m * usdPerMillion;
    }

    private static string NormalizePricingModelId(string? providerId, string? model) {
        var provider = NormalizeOptional(providerId)?.ToLowerInvariant();
        if (provider is "codex" or "openai" or "ix") {
            return OpenAIModelCatalog.NormalizeModelId(model, "unknown-model").Trim().ToLowerInvariant();
        }

        return NormalizeOptional(model)?.ToLowerInvariant() ?? "unknown-model";
    }

    private static bool TryResolveApiPrice(string normalizedModelId, out UsageTelemetryApiPrice rate) {
        if (ApiPricingByModel.TryGetValue(normalizedModelId, out rate)) {
            return true;
        }

        if (normalizedModelId.StartsWith("claude-opus-4-6", StringComparison.OrdinalIgnoreCase)) {
            rate = new UsageTelemetryApiPrice(5m, null, 25m);
            return true;
        }
        if (normalizedModelId.StartsWith("claude-opus-4-5", StringComparison.OrdinalIgnoreCase)) {
            rate = new UsageTelemetryApiPrice(5m, null, 25m);
            return true;
        }
        if (normalizedModelId.StartsWith("claude-opus-4", StringComparison.OrdinalIgnoreCase)) {
            rate = new UsageTelemetryApiPrice(5m, null, 25m);
            return true;
        }

        rate = default;
        return false;
    }

    private static UsageTelemetryOverviewModelHighlight? BuildModelHighlight(
        IEnumerable<UsageEventRecord> events) {
        var candidate = events
            .GroupBy(static record => NormalizeOptional(record.Model) ?? "unknown-model", StringComparer.OrdinalIgnoreCase)
            .Select(group => new UsageTelemetryOverviewModelHighlight(
                group.Key,
                group.Sum(static record => record.TotalTokens ?? 0L)))
            .Where(static model => model.TotalTokens > 0L)
            .OrderByDescending(static model => model.TotalTokens)
            .ThenBy(static model => model.Model, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        return candidate;
    }

    private static IReadOnlyList<UsageEventRecord> FilterToRecentWindow(
        IReadOnlyList<UsageEventRecord> events,
        int windowDays) {
        if (events.Count == 0 || windowDays <= 0) {
            return Array.Empty<UsageEventRecord>();
        }

        var endDayUtc = events[events.Count - 1].TimestampUtc.UtcDateTime.Date;
        var startDayUtc = endDayUtc.AddDays(-(windowDays - 1));
        return events
            .Where(record => record.TimestampUtc.UtcDateTime.Date >= startDayUtc)
            .ToArray();
    }

    private static (int LongestStreakDays, int CurrentStreakDays) ComputeStreaks(
        IReadOnlyList<UsageEventRecord> events) {
        var activeDays = events
            .GroupBy(static record => record.TimestampUtc.UtcDateTime.Date)
            .Where(group => group.Sum(static record => record.TotalTokens ?? 0L) > 0L)
            .Select(static group => group.Key)
            .OrderBy(static day => day)
            .ToArray();

        if (activeDays.Length == 0) {
            return (0, 0);
        }

        var longest = 1;
        var current = 1;
        for (var i = 1; i < activeDays.Length; i++) {
            if ((activeDays[i] - activeDays[i - 1]).Days == 1) {
                current++;
            } else {
                if (current > longest) {
                    longest = current;
                }
                current = 1;
            }
        }

        if (current > longest) {
            longest = current;
        }

        var trailing = 1;
        for (var i = activeDays.Length - 1; i > 0; i--) {
            if ((activeDays[i] - activeDays[i - 1]).Days == 1) {
                trailing++;
            } else {
                break;
            }
        }

        var latestDayUtc = events[events.Count - 1].TimestampUtc.UtcDateTime.Date;
        var currentStreak = activeDays[activeDays.Length - 1] == latestDayUtc ? trailing : 0;
        return (longest, currentStreak);
    }

    private static string? BuildCoverageNote(IReadOnlyList<UsageEventRecord> events) {
        if (events.Count == 0) {
            return null;
        }

        var firstEventDayUtc = events[0].TimestampUtc.UtcDateTime.Date;
        var firstSplitDayUtc = events
            .Where(static record => (record.InputTokens ?? 0L) > 0L || (record.OutputTokens ?? 0L) > 0L)
            .Select(static record => record.TimestampUtc.UtcDateTime.Date)
            .OrderBy(static day => day)
            .FirstOrDefault();

        if (firstSplitDayUtc == default || firstSplitDayUtc <= firstEventDayUtc) {
            return null;
        }

        return "Full input/output token telemetry starts on "
               + firstSplitDayUtc.ToString("MMM d", CultureInfo.InvariantCulture)
               + "; earlier activity may be under-split.";
    }

    private static double? ComputeRatio(long value, long total) {
        if (value <= 0L || total <= 0L) {
            return 0d;
        }

        return Math.Min(1d, value / (double)total);
    }

    private static IEnumerable<UsageTelemetryOverviewCard> BuildCards(
        UsageSummarySnapshot summary,
        UsageSummaryMetric metric) {
        yield return new UsageTelemetryOverviewCard(
            "total",
            "Total " + ResolveUnitsLabel(metric),
            FormatMetricValue(summary.TotalValue, metric),
            BuildRangeLabel(summary.StartDayUtc, summary.EndDayUtc));

        yield return new UsageTelemetryOverviewCard(
            "active_days",
            "Active days",
            summary.ActiveDays.ToString(CultureInfo.InvariantCulture),
            summary.TotalDays > 0
                ? summary.TotalDays.ToString(CultureInfo.InvariantCulture) + " day range"
                : null);

        yield return new UsageTelemetryOverviewCard(
            "peak_day",
            "Peak day",
            summary.PeakDayUtc?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "n/a",
            summary.PeakDayUtc.HasValue
                ? FormatMetricValue(summary.PeakValue, metric) + " " + ResolveUnitsLabel(metric)
                : null);

        yield return new UsageTelemetryOverviewCard(
            "avg_active_day",
            "Avg / active day",
            FormatMetricValue(summary.AveragePerActiveDay, metric),
            ResolveUnitsLabel(metric));

        foreach (var window in summary.RollingWindows.OrderBy(static window => window.WindowDays)) {
            yield return new UsageTelemetryOverviewCard(
                "avg_" + window.WindowDays.ToString(CultureInfo.InvariantCulture) + "d",
                window.WindowDays.ToString(CultureInfo.InvariantCulture) + "d avg / day",
                FormatMetricValue(window.AveragePerCalendarDay, metric),
                FormatMetricValue(window.TotalValue, metric) + " " + ResolveUnitsLabel(metric));
        }
    }

    private static IEnumerable<UsageTelemetryOverviewHeatmap> BuildHeatmaps(
        IReadOnlyList<UsageEventRecord> events,
        UsageSummarySnapshot summary,
        string title,
        UsageTelemetryOverviewOptions options) {
        var builder = new UsageTelemetryHeatmapDocumentBuilder();
        foreach (var breakdown in NormalizeBreakdowns(options.HeatmapBreakdowns)) {
            if (!ShouldEmitHeatmap(summary, breakdown)) {
                continue;
            }

            var key = breakdown.ToString().ToLowerInvariant();
            var label = ResolveBreakdownLabel(breakdown);
            var document = builder.Build(
                events,
                new UsageTelemetryHeatmapOptions {
                    Metric = options.Metric,
                    Breakdown = breakdown,
                    Title = title + " - " + label,
                    Subtitle = ResolveBreakdownSubtitlePrefix(breakdown),
                    LegendLimit = options.LegendLimit,
                    BreakdownLabels = breakdown == UsageHeatmapBreakdownDimension.SourceRoot
                        ? options.SourceRootLabels
                        : null
                });

            yield return new UsageTelemetryOverviewHeatmap(key, label, document);
        }
    }

    private static bool ShouldEmitHeatmap(UsageSummarySnapshot summary, UsageHeatmapBreakdownDimension breakdown) {
        return breakdown switch {
            UsageHeatmapBreakdownDimension.Provider => summary.ProviderBreakdown.Count > 0,
            UsageHeatmapBreakdownDimension.Account => summary.AccountBreakdown.Count > 0,
            UsageHeatmapBreakdownDimension.Person => summary.PersonBreakdown.Count > 0,
            UsageHeatmapBreakdownDimension.Model => summary.ModelBreakdown.Count > 0,
            UsageHeatmapBreakdownDimension.SourceRoot => summary.SourceRootBreakdown.Count > 0,
            UsageHeatmapBreakdownDimension.Surface => summary.SurfaceBreakdown.Count > 0,
            _ => false
        };
    }

    private static IReadOnlyList<UsageHeatmapBreakdownDimension> NormalizeBreakdowns(
        IReadOnlyList<UsageHeatmapBreakdownDimension>? values) {
        if (values is null || values.Count == 0) {
            return Array.Empty<UsageHeatmapBreakdownDimension>();
        }

        var seen = new HashSet<UsageHeatmapBreakdownDimension>();
        var result = new List<UsageHeatmapBreakdownDimension>();
        foreach (var value in values) {
            if (seen.Add(value)) {
                result.Add(value);
            }
        }
        return result;
    }

    private static string ResolveBreakdownLabel(UsageHeatmapBreakdownDimension breakdown) {
        return breakdown switch {
            UsageHeatmapBreakdownDimension.Provider => "By telemetry source",
            UsageHeatmapBreakdownDimension.Account => "By account",
            UsageHeatmapBreakdownDimension.Person => "By person",
            UsageHeatmapBreakdownDimension.Model => "By model",
            UsageHeatmapBreakdownDimension.SourceRoot => "By source root",
            UsageHeatmapBreakdownDimension.Surface => "By surface",
            _ => "Heatmap"
        };
    }

    private static string ResolveBreakdownSubtitlePrefix(UsageHeatmapBreakdownDimension breakdown) {
        return breakdown switch {
            UsageHeatmapBreakdownDimension.Provider => "by telemetry source",
            UsageHeatmapBreakdownDimension.Account => "by account",
            UsageHeatmapBreakdownDimension.Person => "by person",
            UsageHeatmapBreakdownDimension.Model => "by model",
            UsageHeatmapBreakdownDimension.SourceRoot => "by source root",
            UsageHeatmapBreakdownDimension.Surface => "by surface",
            _ => "breakdown"
        };
    }

    private static string BuildOverviewSubtitle(UsageSummarySnapshot summary, UsageTelemetryOverviewOptions options) {
        var parts = new List<string>();
        var prefix = NormalizeOptional(options.Subtitle);
        if (!string.IsNullOrWhiteSpace(prefix)) {
            parts.Add(prefix!);
        }

        parts.Add(FormatMetricValue(summary.TotalValue, options.Metric) + " " + ResolveUnitsLabel(options.Metric));
        parts.Add(HeatmapDisplayText.FormatActiveDays(summary.ActiveDays));
        if (summary.PeakDayUtc.HasValue) {
            parts.Add("peak " + summary.PeakDayUtc.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                      + " (" + FormatMetricValue(summary.PeakValue, options.Metric) + ")");
        }

        return HeatmapDisplayText.JoinSummaryParts(parts.ToArray());
    }

    private static string BuildRangeLabel(DateTime? startDayUtc, DateTime? endDayUtc) {
        return HeatmapDisplayText.FormatDateRange(startDayUtc, endDayUtc);
    }

    private static string ResolveUnitsLabel(UsageSummaryMetric metric) {
        return metric switch {
            UsageSummaryMetric.CostUsd => "USD",
            UsageSummaryMetric.DurationMs => "ms",
            UsageSummaryMetric.EventCount => "events",
            _ => "tokens"
        };
    }

    private static string FormatMetricValue(decimal value, UsageSummaryMetric metric) {
        return metric switch {
            UsageSummaryMetric.CostUsd => value.ToString("0.##", CultureInfo.InvariantCulture),
            UsageSummaryMetric.DurationMs => value.ToString("0", CultureInfo.InvariantCulture),
            UsageSummaryMetric.EventCount => value.ToString("0", CultureInfo.InvariantCulture),
            _ => value.ToString("0", CultureInfo.InvariantCulture)
        };
    }

    private static string? NormalizeOptional(string? value) {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string ResolveProviderTitle(string providerId) {
        return UsageTelemetryProviderCatalog.ResolveSectionTitle(providerId);
    }

    private static HeatmapPalette ResolveProviderPalette(string providerId) {
        var appearance = UsageTelemetryProviderCatalog.ResolveAppearance(providerId);
        if (appearance.IntensityColors.Count == 0) {
            return HeatmapPalette.GitHubLight();
        }

        return new HeatmapPalette(
            backgroundColor: "#f2f2f2",
            panelColor: "#f2f2f2",
            textColor: "#162033",
            mutedTextColor: "#737373",
            emptyColor: "#e8e8e8",
            intensityColors: appearance.IntensityColors.ToArray());
    }

    private static ProviderAccentColors ResolveProviderAccentColors(string providerId) {
        var appearance = UsageTelemetryProviderCatalog.ResolveAppearance(providerId);
        return new ProviderAccentColors(appearance.Input, appearance.Output, appearance.Total, appearance.Other);
    }

    private static string FormatCompact(long value) {
        if (value <= 0L) {
            return "0";
        }

        return FormatCompact((double)value);
    }

    private static string FormatCompact(double value) {
        if (value >= 1_000_000_000d) {
            return (value / 1_000_000_000d).ToString(value >= 10_000_000_000d ? "0.#" : "0.##", CultureInfo.InvariantCulture) + "B";
        }
        if (value >= 1_000_000d) {
            return (value / 1_000_000d).ToString(value >= 10_000_000d ? "0.#" : "0.##", CultureInfo.InvariantCulture) + "M";
        }
        if (value >= 1_000d) {
            return (value / 1_000d).ToString(value >= 10_000d ? "0.#" : "0.##", CultureInfo.InvariantCulture) + "K";
        }
        return value.ToString("0", CultureInfo.InvariantCulture);
    }

    private static string FormatPercent(long value, long total) {
        if (value <= 0L || total <= 0L) {
            return "0%";
        }

        return (Math.Min(1d, value / (double)total) * 100d).ToString("0.#", CultureInfo.InvariantCulture) + "%";
    }

    private static string NormalizeKey(string value) {
        var chars = value
            .Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();
        return new string(chars).Trim('-');
    }

    private readonly record struct UsageTelemetryApiPrice(decimal InputUsdPerMillion, decimal? CachedInputUsdPerMillion, decimal OutputUsdPerMillion);

    private readonly record struct UsageTelemetryApiCostEventEstimate(string Model, long TotalTokens, decimal EstimatedCostUsd, bool HasKnownPricing);

    private readonly record struct ProviderAccentColors(string Input, string Output, string Total, string Other);
}
