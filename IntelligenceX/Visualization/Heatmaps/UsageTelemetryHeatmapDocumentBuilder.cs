using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using IntelligenceX.Telemetry.Usage;

namespace IntelligenceX.Visualization.Heatmaps;

/// <summary>
/// Configures telemetry-backed heatmap document generation.
/// </summary>
public sealed class UsageTelemetryHeatmapOptions {
    /// <summary>
    /// Gets or sets the metric rendered by the heatmap.
    /// </summary>
    public UsageSummaryMetric Metric { get; set; } = UsageSummaryMetric.TotalTokens;

    /// <summary>
    /// Gets or sets the breakdown dimension used for dominant cell coloring.
    /// </summary>
    public UsageHeatmapBreakdownDimension Breakdown { get; set; } = UsageHeatmapBreakdownDimension.Surface;

    /// <summary>
    /// Gets or sets the heatmap title.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets an optional subtitle prefix to prepend before the generated summary.
    /// </summary>
    public string? Subtitle { get; set; }

    /// <summary>
    /// Gets or sets the palette used for the rendered document.
    /// </summary>
    public HeatmapPalette? Palette { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of legend items to emit.
    /// </summary>
    public int LegendLimit { get; set; } = 5;
}

/// <summary>
/// Builds reusable heatmap documents from canonical usage events.
/// </summary>
public sealed class UsageTelemetryHeatmapDocumentBuilder {
    private static readonly UsageHeatmapLegendEntry[] SurfaceLegendEntries = {
        new("reviewer", "Reviewer", "#8b5cf6"),
        new("chat", "Chat", "#06b6d4"),
        new("cli", "CLI", "#f25ca7"),
        new("desktop_app", "Desktop App", "#ef4444"),
        new("github_code_review", "GitHub Code Review", "#8ccf1f"),
        new("web", "Web", "#38bdf8")
    };

    private static readonly UsageHeatmapLegendEntry[] ProviderLegendEntries = {
        new("ix", "IntelligenceX", "#06b6d4"),
        new("codex", "Codex", "#f25ca7"),
        new("claude", "Claude", "#f59e0b"),
        new("chatgpt", "ChatGPT", "#22c55e"),
        new("github", "GitHub", "#2563eb"),
        new("lmstudio", "LM Studio", "#a855f7"),
        new("ollama", "Ollama", "#14b8a6")
    };

    /// <summary>
    /// Builds a telemetry heatmap document from canonical usage events.
    /// </summary>
    public HeatmapDocument Build(
        IEnumerable<UsageEventRecord> events,
        UsageTelemetryHeatmapOptions? options = null) {
        if (events is null) {
            throw new ArgumentNullException(nameof(events));
        }

        var effectiveOptions = options ?? new UsageTelemetryHeatmapOptions();
        var aggregateBuilder = new UsageDailyAggregateBuilder();
        var aggregates = aggregateBuilder.Build(
            events,
            new UsageDailyAggregateOptions {
                Dimensions = ResolveDimensions(effectiveOptions.Breakdown)
            });

        return BuildFromAggregates(aggregates, effectiveOptions);
    }

    /// <summary>
    /// Builds a telemetry heatmap document from canonical daily aggregates.
    /// </summary>
    public HeatmapDocument BuildFromAggregates(
        IEnumerable<UsageDailyAggregateRecord> aggregates,
        UsageTelemetryHeatmapOptions? options = null) {
        if (aggregates is null) {
            throw new ArgumentNullException(nameof(aggregates));
        }

        var effectiveOptions = options ?? new UsageTelemetryHeatmapOptions();
        var aggregateList = aggregates
            .Where(static aggregate => aggregate is not null)
            .OrderBy(static aggregate => aggregate.DayUtc)
            .ToArray();

        if (aggregateList.Length == 0) {
            throw new InvalidOperationException("No telemetry usage aggregates were available for heatmap rendering.");
        }

        var summary = new UsageSummaryBuilder().Build(
            aggregateList,
            new UsageSummaryOptions {
                Metric = effectiveOptions.Metric,
                BreakdownLimit = Math.Max(1, effectiveOptions.LegendLimit),
                RollingWindowDays = new[] { 7, 30 }
            });

        var heatmapBuilder = new UsageHeatmapDocumentBuilder();
        var document = heatmapBuilder.Build(
            aggregateList,
            new UsageHeatmapDocumentOptions {
                Title = NormalizeOptional(effectiveOptions.Title) ?? "Usage Heatmap",
                Subtitle = BuildSubtitle(summary, effectiveOptions),
                Units = ResolveUnitsLabel(effectiveOptions.Metric),
                Metric = ResolveHeatmapMetric(effectiveOptions.Metric),
                BreakdownDimension = effectiveOptions.Breakdown,
                Palette = effectiveOptions.Palette ?? HeatmapPalette.ChatGptDark(),
                LegendLowLabel = "Lower load",
                LegendHighLabel = "Higher load",
                ShowIntensityLegend = true,
                TooltipBreakdownLimit = 4,
                LegendEntries = ResolveLegendEntries(summary, effectiveOptions)
            });

        return document;
    }

    private static UsageAggregateDimensions ResolveDimensions(UsageHeatmapBreakdownDimension breakdown) {
        return breakdown switch {
            UsageHeatmapBreakdownDimension.Provider => UsageAggregateDimensions.Provider,
            UsageHeatmapBreakdownDimension.Account => UsageAggregateDimensions.Account,
            UsageHeatmapBreakdownDimension.Person => UsageAggregateDimensions.Person,
            UsageHeatmapBreakdownDimension.Model => UsageAggregateDimensions.Model,
            UsageHeatmapBreakdownDimension.Surface => UsageAggregateDimensions.Surface,
            _ => UsageAggregateDimensions.None
        };
    }

    private static UsageHeatmapMetric ResolveHeatmapMetric(UsageSummaryMetric metric) {
        return metric switch {
            UsageSummaryMetric.CostUsd => UsageHeatmapMetric.TotalCostUsd,
            UsageSummaryMetric.DurationMs => UsageHeatmapMetric.TotalDurationMs,
            UsageSummaryMetric.EventCount => UsageHeatmapMetric.EventCount,
            _ => UsageHeatmapMetric.TotalTokens
        };
    }

    private static string BuildSubtitle(UsageSummarySnapshot summary, UsageTelemetryHeatmapOptions options) {
        var parts = new List<string>();
        var subtitlePrefix = NormalizeOptional(options.Subtitle);
        if (!string.IsNullOrWhiteSpace(subtitlePrefix)) {
            parts.Add(subtitlePrefix!);
        }

        parts.Add($"{FormatMetricValue(summary.TotalValue, options.Metric)} {ResolveUnitsLabel(options.Metric)}");
        parts.Add($"{summary.ActiveDays} active day(s)");
        if (summary.PeakDayUtc.HasValue) {
            parts.Add($"peak {summary.PeakDayUtc.Value:yyyy-MM-dd} ({FormatMetricValue(summary.PeakValue, options.Metric)})");
        }

        return string.Join(" | ", parts);
    }

    private static IReadOnlyList<UsageHeatmapLegendEntry> ResolveLegendEntries(
        UsageSummarySnapshot summary,
        UsageTelemetryHeatmapOptions options) {
        IReadOnlyList<UsageSummaryBreakdownEntry> breakdownEntries = options.Breakdown switch {
            UsageHeatmapBreakdownDimension.Provider => summary.ProviderBreakdown,
            UsageHeatmapBreakdownDimension.Account => summary.AccountBreakdown,
            UsageHeatmapBreakdownDimension.Person => summary.PersonBreakdown,
            UsageHeatmapBreakdownDimension.Model => summary.ModelBreakdown,
            UsageHeatmapBreakdownDimension.Surface => summary.SurfaceBreakdown,
            _ => Array.Empty<UsageSummaryBreakdownEntry>()
        };

        var topKeys = new HashSet<string>(
            breakdownEntries
                .Take(Math.Max(1, options.LegendLimit))
                .Select(static entry => entry.Key),
            StringComparer.OrdinalIgnoreCase);

        var seedEntries = options.Breakdown switch {
            UsageHeatmapBreakdownDimension.Provider => ProviderLegendEntries,
            UsageHeatmapBreakdownDimension.Surface => SurfaceLegendEntries,
            _ => Array.Empty<UsageHeatmapLegendEntry>()
        };

        var result = new List<UsageHeatmapLegendEntry>();
        foreach (var entry in seedEntries) {
            if (topKeys.Contains(entry.Key)) {
                result.Add(entry);
            }
        }

        foreach (var key in topKeys) {
            if (result.Any(existing => existing.Key.Equals(key, StringComparison.OrdinalIgnoreCase))) {
                continue;
            }

            result.Add(new UsageHeatmapLegendEntry(
                key,
                key,
                BuildStableColor(key, options.Breakdown)));
        }

        return result;
    }

    private static string BuildStableColor(string key, UsageHeatmapBreakdownDimension breakdown) {
        var hash = UsageTelemetryIdentity.ComputeStableHash(key, 4);
        var hueRaw = uint.Parse(hash.Substring(0, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var hue = (int)(hueRaw % 360u);
        var saturation = breakdown switch {
            UsageHeatmapBreakdownDimension.Account => 0.48d,
            UsageHeatmapBreakdownDimension.Person => 0.52d,
            UsageHeatmapBreakdownDimension.Model => 0.58d,
            UsageHeatmapBreakdownDimension.Provider => 0.68d,
            _ => 0.62d
        };
        var lightness = breakdown switch {
            UsageHeatmapBreakdownDimension.Person => 0.54d,
            UsageHeatmapBreakdownDimension.Model => 0.53d,
            UsageHeatmapBreakdownDimension.Provider => 0.55d,
            _ => 0.56d
        };
        return HslToHex(hue / 360d, saturation, lightness);
    }

    private static string HslToHex(double h, double s, double l) {
        var r = l;
        var g = l;
        var b = l;

        if (s > 0d) {
            var q = l < 0.5d ? l * (1d + s) : l + s - (l * s);
            var p = (2d * l) - q;
            r = HueToRgb(p, q, h + (1d / 3d));
            g = HueToRgb(p, q, h);
            b = HueToRgb(p, q, h - (1d / 3d));
        }

        return $"#{(int)Math.Round(r * 255d):X2}{(int)Math.Round(g * 255d):X2}{(int)Math.Round(b * 255d):X2}";
    }

    private static double HueToRgb(double p, double q, double t) {
        if (t < 0d) {
            t += 1d;
        }
        if (t > 1d) {
            t -= 1d;
        }
        if (t < 1d / 6d) {
            return p + ((q - p) * 6d * t);
        }
        if (t < 1d / 2d) {
            return q;
        }
        if (t < 2d / 3d) {
            return p + ((q - p) * ((2d / 3d) - t) * 6d);
        }
        return p;
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
