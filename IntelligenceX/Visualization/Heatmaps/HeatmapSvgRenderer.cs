using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace IntelligenceX.Visualization.Heatmaps;

#pragma warning disable CS1591

/// <summary>
/// Renders heatmap documents as standalone SVG.
/// </summary>
public static class HeatmapSvgRenderer {
    private const int CellSize = 12;
    private const int CellGap = 3;
    private const int HeaderHeight = 86;
    private const int SectionTitleHeight = 22;
    private const int MonthLabelHeight = 18;
    private const int SectionBottomGap = 28;
    private const int LegendHeight = 38;
    private const int OuterPadding = 24;
    private const int GridLeft = 70;
    private const int WeekdayLabelOffset = 18;
    private const int CornerRadius = 2;
    private const int PanelRadius = 18;
    private const int MinWidth = 760;

    public static string Render(HeatmapDocument document) {
        if (document is null) {
            throw new ArgumentNullException(nameof(document));
        }

        var sectionLayouts = document.Sections
            .Select(section => BuildSectionLayout(section, document.WeekStart))
            .ToArray();
        var widestGrid = sectionLayouts.Length == 0 ? 52 : sectionLayouts.Max(static layout => layout.WeekColumns);
        var contentWidth = GridLeft + (widestGrid * (CellSize + CellGap)) + 48;
        var width = Math.Max(MinWidth, OuterPadding * 2 + contentWidth);

        var headerHeight = document.ShowDocumentHeader ? HeaderHeight : 24;
        var bodyHeight = 0;
        foreach (var layout in sectionLayouts) {
            var sectionHeaderHeight = document.ShowSectionHeaders ? SectionTitleHeight : 0;
            bodyHeight += sectionHeaderHeight + MonthLabelHeight + layout.GridHeight + SectionBottomGap;
        }
        var legendHeight = document.ShowIntensityLegend || document.LegendItems.Count > 0 ? LegendHeight : 0;
        var height = OuterPadding + headerHeight + bodyHeight + legendHeight + OuterPadding;

        var panelX = OuterPadding;
        var panelY = OuterPadding;
        var panelWidth = width - (OuterPadding * 2);
        var panelHeight = height - (OuterPadding * 2);

        var sb = new StringBuilder();
        sb.AppendLine($"""
<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}" fill="none">
  <rect width="{width}" height="{height}" fill="{Escape(document.Palette.BackgroundColor)}" />
  <rect x="{panelX}" y="{panelY}" width="{panelWidth}" height="{panelHeight}" rx="{PanelRadius}" fill="{Escape(document.Palette.PanelColor)}" stroke="{Escape(document.Palette.EmptyColor)}" stroke-opacity="0.45" />
""");

        var currentY = OuterPadding + headerHeight;
        if (document.ShowDocumentHeader) {
            var titleX = OuterPadding + 20;
            var headerY = OuterPadding + 34;
            sb.AppendLine(
                $"  <text x=\"{titleX}\" y=\"{headerY}\" fill=\"{Escape(document.Palette.TextColor)}\" font-family=\"Consolas, 'SFMono-Regular', Menlo, monospace\" font-size=\"28\" font-weight=\"700\">{Escape(document.Title)}</text>");
            headerY += 26;
            if (!string.IsNullOrWhiteSpace(document.Subtitle)) {
                sb.AppendLine(
                    $"  <text x=\"{titleX}\" y=\"{headerY}\" fill=\"{Escape(document.Palette.MutedTextColor)}\" font-family=\"Consolas, 'SFMono-Regular', Menlo, monospace\" font-size=\"13\">{Escape(document.Subtitle!)}</text>");
            }
        }

        foreach (var layout in sectionLayouts) {
            AppendSection(sb, document, layout, currentY);
            currentY += (document.ShowSectionHeaders ? SectionTitleHeight : 0) + MonthLabelHeight + layout.GridHeight + SectionBottomGap;
        }

        if (document.ShowIntensityLegend || document.LegendItems.Count > 0) {
            AppendLegend(sb, document, width, height - OuterPadding - 14);
        }

        sb.AppendLine("</svg>");
        return sb.ToString();
    }

    private static void AppendSection(StringBuilder sb, HeatmapDocument document, SectionLayout layout, int y) {
        var sectionHeaderOffset = 0;
        if (document.ShowSectionHeaders) {
            var titleY = y;
            sb.AppendLine(
                $"  <text x=\"{OuterPadding + 20}\" y=\"{titleY}\" fill=\"{Escape(document.Palette.TextColor)}\" font-family=\"Consolas, 'SFMono-Regular', Menlo, monospace\" font-size=\"16\" font-weight=\"700\">{Escape(layout.Section.Title)}</text>");
            if (!string.IsNullOrWhiteSpace(layout.Section.Subtitle)) {
                sb.AppendLine(
                    $"  <text x=\"{OuterPadding + 20 + 90}\" y=\"{titleY}\" fill=\"{Escape(document.Palette.MutedTextColor)}\" font-family=\"Consolas, 'SFMono-Regular', Menlo, monospace\" font-size=\"13\">{Escape(layout.Section.Subtitle!)}</text>");
            }
            sectionHeaderOffset = SectionTitleHeight;
        }

        var monthLabelY = y + sectionHeaderOffset + MonthLabelHeight;
        foreach (var label in layout.MonthLabels) {
            var x = OuterPadding + GridLeft + (label.WeekIndex * (CellSize + CellGap));
            sb.AppendLine(
                $"  <text x=\"{x}\" y=\"{monthLabelY}\" fill=\"{Escape(document.Palette.MutedTextColor)}\" font-family=\"Consolas, 'SFMono-Regular', Menlo, monospace\" font-size=\"11\">{Escape(label.Label)}</text>");
        }

        var gridTop = y + sectionHeaderOffset + MonthLabelHeight + 8;
        AppendWeekdayLabels(sb, document, gridTop);

        var dayLookup = new Dictionary<DateTime, HeatmapDay>();
        foreach (var day in layout.Section.Days) {
            dayLookup[day.Date] = day;
        }

        var cursor = layout.AlignedStart;
        while (cursor <= layout.AlignedEnd) {
            var weekIndex = (int)((cursor - layout.AlignedStart).TotalDays / 7);
            var row = ResolveRow(cursor.DayOfWeek, document.WeekStart);
            var x = OuterPadding + GridLeft + (weekIndex * (CellSize + CellGap));
            var cellY = gridTop + (row * (CellSize + CellGap));
            var fill = document.Palette.EmptyColor;
            string? tooltip = null;
            if (dayLookup.TryGetValue(cursor, out var day)) {
                fill = string.IsNullOrWhiteSpace(day.FillColor)
                    ? document.Palette.GetIntensityColor(day.Level)
                    : day.FillColor!;
                tooltip = day.Tooltip;
            }

            sb.Append(
                $"  <rect x=\"{x}\" y=\"{cellY}\" width=\"{CellSize}\" height=\"{CellSize}\" rx=\"{CornerRadius}\" fill=\"{Escape(fill)}\"");
            if (!string.IsNullOrWhiteSpace(tooltip)) {
                sb.Append('>');
                sb.Append("<title>");
                sb.Append(Escape(tooltip!));
                sb.AppendLine("</title></rect>");
            } else {
                sb.AppendLine(" />");
            }

            cursor = cursor.AddDays(1);
        }
    }

    private static void AppendWeekdayLabels(StringBuilder sb, HeatmapDocument document, int gridTop) {
        var labels = document.CompactWeekdayLabels
            ? BuildCompactWeekdayLabels(document.WeekStart)
            : new[] {
                (Day: OffsetWeekday(document.WeekStart, 1), Label: "Mon"),
                (Day: OffsetWeekday(document.WeekStart, 3), Label: "Wed"),
                (Day: OffsetWeekday(document.WeekStart, 5), Label: "Fri")
            };
        foreach (var label in labels) {
            var row = ResolveRow(label.Day, document.WeekStart);
            var y = gridTop + (row * (CellSize + CellGap)) + WeekdayLabelOffset;
            sb.AppendLine(
                $"  <text x=\"{OuterPadding + 16}\" y=\"{y}\" fill=\"{Escape(document.Palette.MutedTextColor)}\" font-family=\"Consolas, 'SFMono-Regular', Menlo, monospace\" font-size=\"11\">{label.Label}</text>");
        }
    }

    private static (DayOfWeek Day, string Label)[] BuildCompactWeekdayLabels(DayOfWeek weekStart) {
        if (weekStart == DayOfWeek.Monday) {
            return new[] {
                (DayOfWeek.Monday, "Mon"),
                (DayOfWeek.Sunday, "Sun")
            };
        }

        return new[] {
            (weekStart, weekStart.ToString().Substring(0, 3)),
            (OffsetWeekday(weekStart, 6), OffsetWeekday(weekStart, 6).ToString().Substring(0, 3))
        };
    }

    private static void AppendLegend(StringBuilder sb, HeatmapDocument document, int width, int baselineY) {
        var x = width - OuterPadding - 18;
        if (document.LegendItems.Count > 0) {
            for (var i = document.LegendItems.Count - 1; i >= 0; i--) {
                var item = document.LegendItems[i];
                var labelWidth = Math.Max(46, item.Label.Length * 8);
                x -= labelWidth;
                sb.AppendLine(
                    $"  <text x=\"{x}\" y=\"{baselineY}\" fill=\"{Escape(document.Palette.MutedTextColor)}\" font-family=\"Consolas, 'SFMono-Regular', Menlo, monospace\" font-size=\"11\">{Escape(item.Label)}</text>");
                x -= 16;
                sb.AppendLine(
                    $"  <rect x=\"{x}\" y=\"{baselineY - 10}\" width=\"10\" height=\"10\" rx=\"2\" fill=\"{Escape(item.Color)}\" />");
                x -= 14;
            }
        }

        if (!document.ShowIntensityLegend) {
            return;
        }

        var intensityColors = new[] { document.Palette.EmptyColor }
            .Concat(document.Palette.IntensityColors)
            .ToArray();

        x -= 8;
        sb.AppendLine(
            $"  <text x=\"{x}\" y=\"{baselineY}\" fill=\"{Escape(document.Palette.MutedTextColor)}\" font-family=\"Consolas, 'SFMono-Regular', Menlo, monospace\" font-size=\"11\" text-anchor=\"end\">{Escape(document.LegendHighLabel)}</text>");
        x -= 62;
        for (var i = intensityColors.Length - 1; i >= 0; i--) {
            x -= 14;
            sb.AppendLine(
                $"  <rect x=\"{x}\" y=\"{baselineY - 10}\" width=\"10\" height=\"10\" rx=\"2\" fill=\"{Escape(intensityColors[i])}\" />");
        }
        x -= 10;
        sb.AppendLine(
            $"  <text x=\"{x}\" y=\"{baselineY}\" fill=\"{Escape(document.Palette.MutedTextColor)}\" font-family=\"Consolas, 'SFMono-Regular', Menlo, monospace\" font-size=\"11\" text-anchor=\"end\">{Escape(document.LegendLowLabel)}</text>");
    }

    private static SectionLayout BuildSectionLayout(HeatmapSection section, DayOfWeek weekStart) {
        if (section.Days.Count == 0) {
            var today = DateTime.UtcNow.Date;
            return new SectionLayout(section, today, today, 1, Array.Empty<MonthLabel>());
        }

        var start = section.Days[0].Date.Date;
        var end = section.Days[section.Days.Count - 1].Date.Date;
        var alignedStart = AlignToWeekStart(start, weekStart);
        var alignedEnd = AlignToWeekEnd(end, weekStart);
        var weekColumns = ((alignedEnd - alignedStart).Days / 7) + 1;
        var monthLabels = BuildMonthLabels(start, end, alignedStart);
        return new SectionLayout(section, alignedStart, alignedEnd, weekColumns, monthLabels);
    }

    private static IReadOnlyList<MonthLabel> BuildMonthLabels(DateTime start, DateTime end, DateTime alignedStart) {
        var labels = new List<MonthLabel>();
        var monthCursor = new DateTime(start.Year, start.Month, 1);
        while (monthCursor <= end) {
            var weekIndex = Math.Max(0, (monthCursor - alignedStart).Days / 7);
            labels.Add(new MonthLabel(monthCursor.ToString("MMM", CultureInfo.InvariantCulture), weekIndex));
            monthCursor = monthCursor.AddMonths(1);
        }
        return labels;
    }

    private static DateTime AlignToWeekStart(DateTime date, DayOfWeek weekStart) {
        var diff = ((int)date.DayOfWeek - (int)weekStart + 7) % 7;
        return date.AddDays(-diff).Date;
    }

    private static DateTime AlignToWeekEnd(DateTime date, DayOfWeek weekStart) {
        var start = AlignToWeekStart(date, weekStart);
        return start.AddDays(6).Date;
    }

    private static int ResolveRow(DayOfWeek day, DayOfWeek weekStart) {
        return ((int)day - (int)weekStart + 7) % 7;
    }

    private static DayOfWeek OffsetWeekday(DayOfWeek start, int offset) {
        return (DayOfWeek)(((int)start + offset) % 7);
    }

    private static string Escape(string value) {
        if (string.IsNullOrEmpty(value)) {
            return string.Empty;
        }
        return value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");
    }

    private sealed class SectionLayout {
        public SectionLayout(
            HeatmapSection section,
            DateTime alignedStart,
            DateTime alignedEnd,
            int weekColumns,
            IReadOnlyList<MonthLabel> monthLabels) {
            Section = section;
            AlignedStart = alignedStart;
            AlignedEnd = alignedEnd;
            WeekColumns = weekColumns;
            MonthLabels = monthLabels;
        }

        public HeatmapSection Section { get; }
        public DateTime AlignedStart { get; }
        public DateTime AlignedEnd { get; }
        public int WeekColumns { get; }
        public int GridHeight => (CellSize * 7) + (CellGap * 6);
        public IReadOnlyList<MonthLabel> MonthLabels { get; }
    }

    private sealed class MonthLabel {
        public MonthLabel(string label, int weekIndex) {
            Label = label;
            WeekIndex = weekIndex;
        }

        public string Label { get; }
        public int WeekIndex { get; }
    }
}
