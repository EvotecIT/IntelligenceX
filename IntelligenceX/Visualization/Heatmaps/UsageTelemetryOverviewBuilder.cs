using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using IntelligenceX.Json;
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
        UsageHeatmapBreakdownDimension.Account,
        UsageHeatmapBreakdownDimension.Person
    };

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
        IReadOnlyList<UsageTelemetryOverviewHeatmap> heatmaps) {
        Title = string.IsNullOrWhiteSpace(title) ? "Usage Overview" : title.Trim();
        Subtitle = HeatmapText.NormalizeOptionalText(subtitle);
        Metric = metric;
        Units = string.IsNullOrWhiteSpace(units) ? "tokens" : units.Trim();
        Summary = summary ?? throw new ArgumentNullException(nameof(summary));
        Cards = cards ?? Array.Empty<UsageTelemetryOverviewCard>();
        Heatmaps = heatmaps ?? Array.Empty<UsageTelemetryOverviewHeatmap>();
    }

    public string Title { get; }
    public string? Subtitle { get; }
    public UsageSummaryMetric Metric { get; }
    public string Units { get; }
    public UsageSummarySnapshot Summary { get; }
    public IReadOnlyList<UsageTelemetryOverviewCard> Cards { get; }
    public IReadOnlyList<UsageTelemetryOverviewHeatmap> Heatmaps { get; }

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

        return new UsageTelemetryOverviewDocument(
            title,
            subtitle,
            effectiveOptions.Metric,
            units,
            summary,
            cards,
            heatmaps);
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
                    Subtitle = "by " + key,
                    LegendLimit = options.LegendLimit
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
            UsageHeatmapBreakdownDimension.Provider => "By provider",
            UsageHeatmapBreakdownDimension.Account => "By account",
            UsageHeatmapBreakdownDimension.Person => "By person",
            UsageHeatmapBreakdownDimension.Model => "By model",
            UsageHeatmapBreakdownDimension.Surface => "By surface",
            _ => "Heatmap"
        };
    }

    private static string BuildOverviewSubtitle(UsageSummarySnapshot summary, UsageTelemetryOverviewOptions options) {
        var parts = new List<string>();
        var prefix = NormalizeOptional(options.Subtitle);
        if (!string.IsNullOrWhiteSpace(prefix)) {
            parts.Add(prefix!);
        }

        parts.Add(FormatMetricValue(summary.TotalValue, options.Metric) + " " + ResolveUnitsLabel(options.Metric));
        parts.Add(summary.ActiveDays.ToString(CultureInfo.InvariantCulture) + " active day(s)");
        if (summary.PeakDayUtc.HasValue) {
            parts.Add("peak " + summary.PeakDayUtc.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                      + " (" + FormatMetricValue(summary.PeakValue, options.Metric) + ")");
        }

        return string.Join(" | ", parts);
    }

    private static string BuildRangeLabel(DateTime? startDayUtc, DateTime? endDayUtc) {
        if (!startDayUtc.HasValue || !endDayUtc.HasValue) {
            return "No range";
        }

        return startDayUtc.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
               + " -> "
               + endDayUtc.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
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
}
