using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using IntelligenceX.Telemetry.Usage;

namespace IntelligenceX.Visualization.Heatmaps;

#pragma warning disable CS1591

public enum UsageHeatmapMetric {
    EventCount,
    InputTokens,
    CachedInputTokens,
    OutputTokens,
    ReasoningTokens,
    TotalTokens,
    TotalDurationMs,
    TotalCostUsd,
}

public enum UsageHeatmapBreakdownDimension {
    None,
    Provider,
    Account,
    Person,
    Model,
    Surface,
}

public sealed class UsageHeatmapLegendEntry {
    public UsageHeatmapLegendEntry(string key, string label, string color) {
        Key = string.IsNullOrWhiteSpace(key) ? throw new ArgumentException("Legend key is required.", nameof(key)) : key.Trim();
        Label = string.IsNullOrWhiteSpace(label) ? Key : label.Trim();
        Color = string.IsNullOrWhiteSpace(color) ? "#999999" : color.Trim();
    }

    public string Key { get; }
    public string Label { get; }
    public string Color { get; }
}

public sealed class UsageHeatmapDocumentOptions {
    public string Title { get; set; } = "Usage heatmap";
    public string? Subtitle { get; set; }
    public string? Units { get; set; } = "tokens";
    public UsageHeatmapMetric Metric { get; set; } = UsageHeatmapMetric.TotalTokens;
    public UsageHeatmapBreakdownDimension BreakdownDimension { get; set; } = UsageHeatmapBreakdownDimension.Surface;
    public HeatmapPalette Palette { get; set; } = HeatmapPalette.ChatGptDark();
    public DayOfWeek WeekStart { get; set; } = DayOfWeek.Sunday;
    public string LegendLowLabel { get; set; } = "Lower load";
    public string LegendHighLabel { get; set; } = "Higher load";
    public bool ShowIntensityLegend { get; set; } = true;
    public int TooltipBreakdownLimit { get; set; } = 4;
    public IReadOnlyList<UsageHeatmapLegendEntry>? LegendEntries { get; set; }
    public bool ShowDocumentHeader { get; set; } = true;
    public bool ShowSectionHeaders { get; set; } = true;
    public bool CompactWeekdayLabels { get; set; }
    public DateTime? RangeStartUtc { get; set; }
    public DateTime? RangeEndUtc { get; set; }
    public bool GroupSectionsByYear { get; set; } = true;
}

/// <summary>
/// Builds reusable heatmap documents from provider-neutral daily aggregates.
/// </summary>
public sealed class UsageHeatmapDocumentBuilder {
    public HeatmapDocument Build(
        IEnumerable<UsageDailyAggregateRecord> aggregates,
        UsageHeatmapDocumentOptions? options = null) {
        if (aggregates is null) {
            throw new ArgumentNullException(nameof(aggregates));
        }

        var effectiveOptions = options ?? new UsageHeatmapDocumentOptions();
        var aggregateList = aggregates
            .Where(static aggregate => aggregate is not null)
            .OrderBy(static aggregate => aggregate.DayUtc)
            .ToArray();

        var legendByKey = (effectiveOptions.LegendEntries ?? Array.Empty<UsageHeatmapLegendEntry>())
            .GroupBy(static entry => entry.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);

        if (aggregateList.Length == 0) {
            return new HeatmapDocument(
                title: effectiveOptions.Title,
                subtitle: effectiveOptions.Subtitle,
                palette: effectiveOptions.Palette,
                sections: Array.Empty<HeatmapSection>(),
                units: effectiveOptions.Units,
                weekStart: effectiveOptions.WeekStart,
                showIntensityLegend: effectiveOptions.ShowIntensityLegend,
                legendLowLabel: effectiveOptions.LegendLowLabel,
                legendHighLabel: effectiveOptions.LegendHighLabel,
                legendItems: Array.Empty<HeatmapLegendItem>(),
                showDocumentHeader: effectiveOptions.ShowDocumentHeader,
                showSectionHeaders: effectiveOptions.ShowSectionHeaders,
                compactWeekdayLabels: effectiveOptions.CompactWeekdayLabels);
        }

        var sourceDays = aggregateList
            .GroupBy(static aggregate => aggregate.DayUtc.Date)
            .Select(group => BuildDay(group.Key, group, effectiveOptions, legendByKey))
            .OrderBy(static day => day.Date)
            .ToArray();

        var maxValue = sourceDays.Max(static day => day.Total);
        var renderedDays = sourceDays
            .Select(day => day.ToHeatmapDay(maxValue, effectiveOptions.Palette, legendByKey))
            .ToArray();
        var rangeStartUtc = NormalizeRangeBoundary(effectiveOptions.RangeStartUtc, renderedDays[0].Date);
        var rangeEndUtc = NormalizeRangeBoundary(effectiveOptions.RangeEndUtc, renderedDays[renderedDays.Length - 1].Date);
        if (rangeEndUtc < rangeStartUtc) {
            var swap = rangeStartUtc;
            rangeStartUtc = rangeEndUtc;
            rangeEndUtc = swap;
        }

        var renderedDayLookup = renderedDays.ToDictionary(static day => day.Date, static day => day);
        var heatmapDays = ExpandRange(renderedDayLookup, rangeStartUtc, rangeEndUtc, effectiveOptions.Palette.EmptyColor);

        var sections = effectiveOptions.GroupSectionsByYear
            ? heatmapDays
                .GroupBy(static day => day.Date.Year)
                .OrderByDescending(static group => group.Key)
                .Select(group => {
                    var yearDays = group.OrderBy(static day => day.Date).ToArray();
                    var activeDays = yearDays.Count(static day => day.Value > 0d);
                    var peak = yearDays.Max(static day => day.Value);
                    var subtitle = $"{activeDays} active day(s), peak {FormatMetric(peak, effectiveOptions.Metric)} {ResolveUnitsLabel(effectiveOptions.Units)}";
                    return new HeatmapSection(group.Key.ToString(CultureInfo.InvariantCulture), subtitle, yearDays);
                })
                .Cast<HeatmapSection>()
                .ToArray()
            : new[] {
                BuildSingleRangeSection(
                    heatmapDays,
                    rangeStartUtc,
                    rangeEndUtc,
                    effectiveOptions)
            };

        var legendItems = sourceDays
            .SelectMany(static day => day.BreakdownKeys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(key => legendByKey.TryGetValue(key!, out var entry)
                ? new HeatmapLegendItem(entry.Label, entry.Color)
                : null)
            .Where(static item => item is not null)
            .Cast<HeatmapLegendItem>()
            .ToArray();

        return new HeatmapDocument(
            title: effectiveOptions.Title,
            subtitle: effectiveOptions.Subtitle,
            palette: effectiveOptions.Palette,
            sections: sections,
            units: effectiveOptions.Units,
            weekStart: effectiveOptions.WeekStart,
            showIntensityLegend: effectiveOptions.ShowIntensityLegend,
            legendLowLabel: effectiveOptions.LegendLowLabel,
            legendHighLabel: effectiveOptions.LegendHighLabel,
            legendItems: legendItems,
            showDocumentHeader: effectiveOptions.ShowDocumentHeader,
            showSectionHeaders: effectiveOptions.ShowSectionHeaders,
            compactWeekdayLabels: effectiveOptions.CompactWeekdayLabels);
    }

    private static DateTime NormalizeRangeBoundary(DateTime? value, DateTime fallback) {
        return value?.Date ?? fallback.Date;
    }

    private static HeatmapSection BuildSingleRangeSection(
        IReadOnlyList<HeatmapDay> heatmapDays,
        DateTime rangeStartUtc,
        DateTime rangeEndUtc,
        UsageHeatmapDocumentOptions options) {
        var activeDays = heatmapDays.Count(static day => day.Value > 0d);
        var peak = heatmapDays.Max(static day => day.Value);
        var subtitle = $"{activeDays} active day(s), peak {FormatMetric(peak, options.Metric)} {ResolveUnitsLabel(options.Units)}";
        var title = rangeStartUtc == rangeEndUtc
            ? rangeStartUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : $"{rangeStartUtc:yyyy-MM-dd} -> {rangeEndUtc:yyyy-MM-dd}";
        return new HeatmapSection(title, subtitle, heatmapDays);
    }

    private static HeatmapDay[] ExpandRange(
        IReadOnlyDictionary<DateTime, HeatmapDay> renderedDayLookup,
        DateTime rangeStartUtc,
        DateTime rangeEndUtc,
        string emptyColor) {
        var days = new List<HeatmapDay>();
        for (var cursor = rangeStartUtc.Date; cursor <= rangeEndUtc.Date; cursor = cursor.AddDays(1)) {
            if (renderedDayLookup.TryGetValue(cursor, out var existing)) {
                days.Add(existing);
                continue;
            }

            days.Add(new HeatmapDay(
                cursor,
                0d,
                level: 0,
                fillColor: emptyColor,
                tooltip: cursor.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        }

        return days.ToArray();
    }

    private static UsageHeatmapSourceDay BuildDay(
        DateTime dayUtc,
        IEnumerable<UsageDailyAggregateRecord> records,
        UsageHeatmapDocumentOptions options,
        IReadOnlyDictionary<string, UsageHeatmapLegendEntry> legendByKey) {
        var breakdown = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var dominantBreakdownKey = default(string);
        var total = 0d;

        foreach (var record in records) {
            var value = SelectMetric(record, options.Metric);
            total += value;

            var breakdownKey = ResolveBreakdownKey(record, options.BreakdownDimension);
            if (string.IsNullOrWhiteSpace(breakdownKey)) {
                continue;
            }

            if (!breakdown.TryGetValue(breakdownKey!, out var existing)) {
                existing = 0d;
            }
            breakdown[breakdownKey!] = existing + value;
        }

        if (breakdown.Count > 0) {
            dominantBreakdownKey = breakdown
                .OrderByDescending(static pair => pair.Value)
                .ThenBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .First()
                .Key;
        }

        var tooltip = BuildTooltip(dayUtc, total, breakdown, options, legendByKey);
        var displayBreakdown = breakdown
            .OrderByDescending(static pair => pair.Value)
            .ThenBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                pair => ResolveLegendLabel(pair.Key, legendByKey),
                static pair => pair.Value,
                StringComparer.OrdinalIgnoreCase);

        return new UsageHeatmapSourceDay(
            dayUtc,
            total,
            dominantBreakdownKey,
            breakdown.Keys.OrderBy(static key => key, StringComparer.OrdinalIgnoreCase).ToArray(),
            displayBreakdown,
            tooltip);
    }

    private static string BuildTooltip(
        DateTime dayUtc,
        double total,
        IReadOnlyDictionary<string, double> breakdown,
        UsageHeatmapDocumentOptions options,
        IReadOnlyDictionary<string, UsageHeatmapLegendEntry> legendByKey) {
        var tooltip = new List<string> {
            dayUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            $"Total: {FormatMetric(total, options.Metric)} {ResolveUnitsLabel(options.Units)}"
        };

        if (options.BreakdownDimension != UsageHeatmapBreakdownDimension.None) {
            foreach (var pair in breakdown
                         .OrderByDescending(static value => value.Value)
                         .ThenBy(static value => value.Key, StringComparer.OrdinalIgnoreCase)
                         .Take(Math.Max(1, options.TooltipBreakdownLimit))) {
                tooltip.Add($"{ResolveLegendLabel(pair.Key, legendByKey)}: {FormatMetric(pair.Value, options.Metric)}");
            }
        }

        return string.Join("\n", tooltip);
    }

    private static double SelectMetric(UsageDailyAggregateRecord record, UsageHeatmapMetric metric) {
        return metric switch {
            UsageHeatmapMetric.EventCount => record.EventCount,
            UsageHeatmapMetric.InputTokens => record.InputTokens ?? 0,
            UsageHeatmapMetric.CachedInputTokens => record.CachedInputTokens ?? 0,
            UsageHeatmapMetric.OutputTokens => record.OutputTokens ?? 0,
            UsageHeatmapMetric.ReasoningTokens => record.ReasoningTokens ?? 0,
            UsageHeatmapMetric.TotalTokens => record.TotalTokens ?? 0,
            UsageHeatmapMetric.TotalDurationMs => record.TotalDurationMs ?? 0,
            UsageHeatmapMetric.TotalCostUsd => (double)(record.TotalCostUsd ?? 0m),
            _ => record.TotalTokens ?? 0
        };
    }

    private static string? ResolveBreakdownKey(UsageDailyAggregateRecord record, UsageHeatmapBreakdownDimension dimension) {
        return dimension switch {
            UsageHeatmapBreakdownDimension.Provider => NormalizeOptional(record.ProviderId),
            UsageHeatmapBreakdownDimension.Account => NormalizeOptional(record.AccountLabel) ??
                                                      NormalizeOptional(record.AccountKey) ??
                                                      NormalizeOptional(record.ProviderAccountId),
            UsageHeatmapBreakdownDimension.Person => NormalizeOptional(record.PersonLabel),
            UsageHeatmapBreakdownDimension.Model => NormalizeOptional(record.Model),
            UsageHeatmapBreakdownDimension.Surface => NormalizeOptional(record.Surface),
            _ => null
        };
    }

    private static string ResolveLegendLabel(string key, IReadOnlyDictionary<string, UsageHeatmapLegendEntry> legendByKey) {
        if (legendByKey.TryGetValue(key, out var entry)) {
            return entry.Label;
        }

        return key;
    }

    private static string ResolveDayFillColor(
        string? dominantBreakdownKey,
        double total,
        double maxValue,
        HeatmapPalette palette,
        IReadOnlyDictionary<string, UsageHeatmapLegendEntry> legendByKey) {
        if (total <= 0d) {
            return palette.EmptyColor;
        }

        if (!string.IsNullOrWhiteSpace(dominantBreakdownKey) &&
            legendByKey.TryGetValue(dominantBreakdownKey!, out var entry)) {
            var normalized = maxValue <= 0d ? 0d : Math.Sqrt(Math.Min(1d, total / maxValue));
            return BlendColor(palette.EmptyColor, entry.Color, 0.22d + (0.78d * normalized));
        }

        return palette.GetIntensityColor(QuantizeLevel(total, maxValue));
    }

    private static int QuantizeLevel(double value, double maxValue) {
        if (value <= 0d || maxValue <= 0d) {
            return 0;
        }

        var normalized = Math.Sqrt(Math.Min(1d, value / maxValue));
        return 1 + (int)Math.Floor(normalized * 3.999d);
    }

    private static string FormatMetric(double value, UsageHeatmapMetric metric) {
        return metric switch {
            UsageHeatmapMetric.TotalCostUsd => value.ToString("0.00", CultureInfo.InvariantCulture),
            _ => Math.Abs(value % 1d) < 0.00001d
                ? value.ToString("0", CultureInfo.InvariantCulture)
                : value.ToString("0.##", CultureInfo.InvariantCulture)
        };
    }

    private static string ResolveUnitsLabel(string? units) {
        return string.IsNullOrWhiteSpace(units) ? "units" : units?.Trim() ?? "units";
    }

    private static string? NormalizeOptional(string? value) {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string BlendColor(string from, string to, double factor) {
        var start = ParseColor(from);
        var end = ParseColor(to);
        var clamped = Math.Max(0d, Math.Min(1d, factor));
        var r = (int)Math.Round(start.R + ((end.R - start.R) * clamped));
        var g = (int)Math.Round(start.G + ((end.G - start.G) * clamped));
        var b = (int)Math.Round(start.B + ((end.B - start.B) * clamped));
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    private static (int R, int G, int B) ParseColor(string value) {
        var normalized = value?.Trim() ?? string.Empty;
        if (!normalized.StartsWith("#", StringComparison.Ordinal) || normalized.Length != 7) {
            return (153, 153, 153);
        }

        return (
            ParseHexComponent(normalized, 1),
            ParseHexComponent(normalized, 3),
            ParseHexComponent(normalized, 5));
    }

    private static int ParseHexComponent(string value, int index) {
        return int.TryParse(value.Substring(index, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 153;
    }

    private sealed class UsageHeatmapSourceDay {
        public UsageHeatmapSourceDay(
            DateTime date,
            double total,
            string? dominantBreakdownKey,
            IReadOnlyList<string> rawBreakdownKeys,
            IReadOnlyDictionary<string, double> displayBreakdown,
            string tooltip) {
            Date = date.Date;
            Total = total;
            DominantBreakdownKey = dominantBreakdownKey;
            RawBreakdownKeys = rawBreakdownKeys;
            DisplayBreakdown = displayBreakdown;
            Tooltip = tooltip;
        }

        public DateTime Date { get; }
        public double Total { get; }
        public string? DominantBreakdownKey { get; }
        public IReadOnlyList<string> RawBreakdownKeys { get; }
        public IReadOnlyDictionary<string, double> DisplayBreakdown { get; }
        public IEnumerable<string> BreakdownKeys => RawBreakdownKeys;
        public string Tooltip { get; }

        public HeatmapDay ToHeatmapDay(
            double maxValue,
            HeatmapPalette palette,
            IReadOnlyDictionary<string, UsageHeatmapLegendEntry> legendByKey) {
            return new HeatmapDay(
                date: Date,
                value: Total,
                level: QuantizeLevel(Total, maxValue),
                fillColor: ResolveDayFillColor(DominantBreakdownKey, Total, maxValue, palette, legendByKey),
                tooltip: Tooltip,
                breakdown: DisplayBreakdown);
        }
    }
}
