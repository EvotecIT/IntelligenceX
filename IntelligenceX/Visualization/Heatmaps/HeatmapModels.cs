using System;
using System.Collections.Generic;
using System.Linq;
using IntelligenceX.Json;

namespace IntelligenceX.Visualization.Heatmaps;

#pragma warning disable CS1591

/// <summary>
/// Represents a reusable calendar-style heatmap document.
/// </summary>
public sealed class HeatmapDocument {
    public HeatmapDocument(
        string title,
        string? subtitle,
        HeatmapPalette palette,
        IReadOnlyList<HeatmapSection> sections,
        string? units = null,
        DayOfWeek weekStart = DayOfWeek.Sunday,
        bool showIntensityLegend = true,
        string? legendLowLabel = null,
        string? legendHighLabel = null,
        IReadOnlyList<HeatmapLegendItem>? legendItems = null,
        bool showDocumentHeader = true,
        bool showSectionHeaders = true,
        bool compactWeekdayLabels = false) {
        Title = string.IsNullOrWhiteSpace(title) ? "Heatmap" : title.Trim();
        Subtitle = HeatmapText.NormalizeOptionalText(subtitle);
        Units = HeatmapText.NormalizeOptionalText(units);
        Palette = palette ?? HeatmapPalette.GitHubLight();
        Sections = sections ?? Array.Empty<HeatmapSection>();
        WeekStart = weekStart;
        ShowIntensityLegend = showIntensityLegend;
        LegendLowLabel = string.IsNullOrWhiteSpace(legendLowLabel) ? "Less" : HeatmapText.NormalizeOptionalText(legendLowLabel) ?? "Less";
        LegendHighLabel = string.IsNullOrWhiteSpace(legendHighLabel) ? "More" : HeatmapText.NormalizeOptionalText(legendHighLabel) ?? "More";
        LegendItems = legendItems ?? Array.Empty<HeatmapLegendItem>();
        ShowDocumentHeader = showDocumentHeader;
        ShowSectionHeaders = showSectionHeaders;
        CompactWeekdayLabels = compactWeekdayLabels;
    }

    public string Title { get; }
    public string? Subtitle { get; }
    public string? Units { get; }
    public HeatmapPalette Palette { get; }
    public IReadOnlyList<HeatmapSection> Sections { get; }
    public DayOfWeek WeekStart { get; }
    public bool ShowIntensityLegend { get; }
    public string LegendLowLabel { get; }
    public string LegendHighLabel { get; }
    public IReadOnlyList<HeatmapLegendItem> LegendItems { get; }
    public bool ShowDocumentHeader { get; }
    public bool ShowSectionHeaders { get; }
    public bool CompactWeekdayLabels { get; }

    public JsonObject ToJson() {
        var obj = new JsonObject()
            .Add("title", Title)
            .Add("palette", Palette.ToJson())
            .Add("week_start", WeekStart.ToString())
            .Add("show_intensity_legend", ShowIntensityLegend)
            .Add("legend_low_label", LegendLowLabel)
            .Add("legend_high_label", LegendHighLabel)
            .Add("show_document_header", ShowDocumentHeader)
            .Add("show_section_headers", ShowSectionHeaders)
            .Add("compact_weekday_labels", CompactWeekdayLabels);

        if (!string.IsNullOrWhiteSpace(Subtitle)) {
            obj.Add("subtitle", Subtitle);
        }
        if (!string.IsNullOrWhiteSpace(Units)) {
            obj.Add("units", Units);
        }

        var sections = new JsonArray();
        foreach (var section in Sections) {
            sections.Add(JsonValue.From(section.ToJson()));
        }
        obj.Add("sections", sections);

        if (LegendItems.Count > 0) {
            var legend = new JsonArray();
            foreach (var item in LegendItems) {
                legend.Add(JsonValue.From(item.ToJson()));
            }
            obj.Add("legend_items", legend);
        }

        return obj;
    }
}

/// <summary>
/// Represents one logical section in a heatmap document.
/// </summary>
public sealed class HeatmapSection {
    public HeatmapSection(string title, string? subtitle, IReadOnlyList<HeatmapDay> days) {
        Title = string.IsNullOrWhiteSpace(title) ? "Section" : title.Trim();
        Subtitle = HeatmapText.NormalizeOptionalText(subtitle);
        Days = days?
            .OrderBy(static day => day.Date)
            .ToArray() ?? Array.Empty<HeatmapDay>();
    }

    public string Title { get; }
    public string? Subtitle { get; }
    public IReadOnlyList<HeatmapDay> Days { get; }

    public JsonObject ToJson() {
        var obj = new JsonObject().Add("title", Title);
        if (!string.IsNullOrWhiteSpace(Subtitle)) {
            obj.Add("subtitle", Subtitle);
        }

        var days = new JsonArray();
        foreach (var day in Days) {
            days.Add(JsonValue.From(day.ToJson()));
        }
        obj.Add("days", days);
        return obj;
    }
}

/// <summary>
/// Represents one heatmap cell/day.
/// </summary>
public sealed class HeatmapDay {
    public HeatmapDay(
        DateTime date,
        double value,
        int level = 0,
        string? fillColor = null,
        string? tooltip = null,
        IReadOnlyDictionary<string, double>? breakdown = null) {
        Date = date.Date;
        Value = value;
        Level = Math.Max(0, level);
        FillColor = HeatmapText.NormalizeOptionalText(fillColor);
        Tooltip = HeatmapText.NormalizeOptionalText(tooltip);
        Breakdown = breakdown ?? new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
    }

    public DateTime Date { get; }
    public double Value { get; }
    public int Level { get; }
    public string? FillColor { get; }
    public string? Tooltip { get; }
    public IReadOnlyDictionary<string, double> Breakdown { get; }

    public JsonObject ToJson() {
        var obj = new JsonObject()
            .Add("date", Date.ToString("yyyy-MM-dd"))
            .Add("value", Value)
            .Add("level", Level);

        if (!string.IsNullOrWhiteSpace(FillColor)) {
            obj.Add("fill_color", FillColor);
        }
        if (!string.IsNullOrWhiteSpace(Tooltip)) {
            obj.Add("tooltip", Tooltip);
        }
        if (Breakdown.Count > 0) {
            var breakdown = new JsonObject();
            foreach (var pair in Breakdown.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)) {
                breakdown.Add(pair.Key, pair.Value);
            }
            obj.Add("breakdown", breakdown);
        }
        return obj;
    }
}

/// <summary>
/// Represents a named legend swatch.
/// </summary>
public sealed class HeatmapLegendItem {
    public HeatmapLegendItem(string label, string color)
        : this(label, label, color) {
    }

    public HeatmapLegendItem(string key, string label, string color) {
        Key = string.IsNullOrWhiteSpace(key) ? "Item" : key.Trim();
        Label = string.IsNullOrWhiteSpace(label) ? "Item" : label.Trim();
        Color = string.IsNullOrWhiteSpace(color) ? "#999999" : HeatmapText.NormalizeOptionalText(color) ?? "#999999";
    }

    public string Key { get; }
    public string Label { get; }
    public string Color { get; }

    public JsonObject ToJson() {
        return new JsonObject()
            .Add("key", Key)
            .Add("label", Label)
            .Add("color", Color);
    }
}

/// <summary>
/// Represents palette settings used by the shared SVG renderer.
/// </summary>
public sealed class HeatmapPalette {
    public HeatmapPalette(
        string backgroundColor,
        string panelColor,
        string textColor,
        string mutedTextColor,
        string emptyColor,
        IReadOnlyList<string> intensityColors) {
        BackgroundColor = NormalizeColor(backgroundColor, "#ffffff");
        PanelColor = NormalizeColor(panelColor, "#ffffff");
        TextColor = NormalizeColor(textColor, "#111111");
        MutedTextColor = NormalizeColor(mutedTextColor, "#666666");
        EmptyColor = NormalizeColor(emptyColor, "#ebedf0");
        IntensityColors = intensityColors?
            .Select(static color => NormalizeColor(color, "#9be9a8"))
            .ToArray() ?? Array.Empty<string>();
    }

    public string BackgroundColor { get; }
    public string PanelColor { get; }
    public string TextColor { get; }
    public string MutedTextColor { get; }
    public string EmptyColor { get; }
    public IReadOnlyList<string> IntensityColors { get; }

    public string GetIntensityColor(int level) {
        if (level <= 0 || IntensityColors.Count == 0) {
            return EmptyColor;
        }

        var index = Math.Min(IntensityColors.Count - 1, level - 1);
        return IntensityColors[index];
    }

    public JsonObject ToJson() {
        var obj = new JsonObject()
            .Add("background_color", BackgroundColor)
            .Add("panel_color", PanelColor)
            .Add("text_color", TextColor)
            .Add("muted_text_color", MutedTextColor)
            .Add("empty_color", EmptyColor);

        var colors = new JsonArray();
        foreach (var color in IntensityColors) {
            colors.Add(color);
        }
        obj.Add("intensity_colors", colors);
        return obj;
    }

    public static HeatmapPalette GitHubLight() {
        return new HeatmapPalette(
            backgroundColor: "#f6f8fa",
            panelColor: "#ffffff",
            textColor: "#24292f",
            mutedTextColor: "#57606a",
            emptyColor: "#ebedf0",
            intensityColors: new[] { "#9be9a8", "#40c463", "#30a14e", "#216e39" });
    }

    public static HeatmapPalette ChatGptDark() {
        return new HeatmapPalette(
            backgroundColor: "#0f1115",
            panelColor: "#171b22",
            textColor: "#f5f7fa",
            mutedTextColor: "#9aa4b2",
            emptyColor: "#252b34",
            intensityColors: new[] { "#4f7cff", "#38bdf8", "#34d399", "#bef264" });
    }

    private static string NormalizeColor(string? value, string fallback) {
        if (string.IsNullOrWhiteSpace(value)) {
            return fallback;
        }
        return HeatmapText.NormalizeOptionalText(value) ?? fallback;
    }
}

internal static class HeatmapText {
    internal static string? NormalizeOptionalText(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }
        return (value ?? string.Empty).Trim();
    }
}
