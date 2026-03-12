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
/// Represents one provider-specific usage section in the overview.
/// </summary>
public sealed class UsageTelemetryOverviewProviderSection {
    public UsageTelemetryOverviewProviderSection(
        string key,
        string providerId,
        string title,
        string subtitle,
        HeatmapDocument heatmap,
        long inputTokens,
        long outputTokens,
        long totalTokens,
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
        InputTokens = Math.Max(0L, inputTokens);
        OutputTokens = Math.Max(0L, outputTokens);
        TotalTokens = Math.Max(0L, totalTokens);
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
    public long InputTokens { get; }
    public long OutputTokens { get; }
    public long TotalTokens { get; }
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
            .Add("inputTokens", InputTokens)
            .Add("outputTokens", OutputTokens)
            .Add("totalTokens", TotalTokens)
            .Add("longestStreakDays", LongestStreakDays)
            .Add("currentStreakDays", CurrentStreakDays)
            .Add("heatmap", Heatmap.ToJson());

        if (MostUsedModel is not null) {
            obj.Add("mostUsedModel", MostUsedModel.ToJson());
        }
        if (RecentModel is not null) {
            obj.Add("recentModel", RecentModel.ToJson());
        }
        if (!string.IsNullOrWhiteSpace(Note)) {
            obj.Add("note", Note);
        }

        return obj;
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
        var rangeStartUtc = latestDayUtc.AddDays(-364);
        var events = allEvents
            .Where(record => record.TimestampUtc.UtcDateTime.Date >= rangeStartUtc
                             && record.TimestampUtc.UtcDateTime.Date <= rangeEndUtc)
            .ToArray();

        var title = ResolveProviderTitle(providerId);
        var subtitle = BuildRangeLabel(rangeStartUtc, rangeEndUtc);
        var inputTokens = events.Sum(static record => record.InputTokens ?? 0L);
        var outputTokens = events.Sum(static record => record.OutputTokens ?? 0L);
        var totalTokens = events.Sum(static record => record.TotalTokens ?? 0L);
        var mostUsedModel = BuildModelHighlight(events);
        var recentModel = BuildModelHighlight(FilterToRecentWindow(events, 30));
        var (longestStreakDays, currentStreakDays) = ComputeStreaks(events);
        var note = BuildCoverageNote(events);

        var heatmap = BuildProviderHeatmap(title, providerId, events, rangeStartUtc, rangeEndUtc);

        return new UsageTelemetryOverviewProviderSection(
            key: "provider-" + NormalizeKey(providerId),
            providerId: providerId,
            title: title,
            subtitle: subtitle,
            heatmap: heatmap,
            inputTokens: inputTokens,
            outputTokens: outputTokens,
            totalTokens: totalTokens,
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

    private static string ResolveProviderTitle(string providerId) {
        return providerId.Trim().ToLowerInvariant() switch {
            "claude" => "Claude Code",
            "codex" => "Codex",
            "ix" => "IntelligenceX",
            "chatgpt" => "ChatGPT",
            "github" => "GitHub",
            "lmstudio" => "LM Studio",
            "ollama" => "Ollama",
            _ => providerId
        };
    }

    private static HeatmapPalette ResolveProviderPalette(string providerId) {
        return providerId.Trim().ToLowerInvariant() switch {
            "claude" => new HeatmapPalette(
                backgroundColor: "#f2f2f2",
                panelColor: "#f2f2f2",
                textColor: "#162033",
                mutedTextColor: "#737373",
                emptyColor: "#e8e8e8",
                intensityColors: new[] { "#f5d8b0", "#f3ba73", "#fb8c1d", "#c65102" }),
            "codex" => new HeatmapPalette(
                backgroundColor: "#f2f2f2",
                panelColor: "#f2f2f2",
                textColor: "#162033",
                mutedTextColor: "#737373",
                emptyColor: "#e8e8e8",
                intensityColors: new[] { "#cfd6ff", "#98a8ff", "#6268f1", "#2f2a93" }),
            _ => HeatmapPalette.GitHubLight()
        };
    }

    private static string NormalizeKey(string value) {
        var chars = value
            .Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();
        return new string(chars).Trim('-');
    }
}
