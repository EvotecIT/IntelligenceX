using System;
using System.Globalization;
using System.Net;
using System.Text;

namespace IntelligenceX.Visualization.Heatmaps;

internal static class UsageTelemetryBreakdownSummaryHtmlRenderer {
    public static void AppendSummary(StringBuilder sb, UsageTelemetryBreakdownSummaryPageModel summary, int baseIndentLevel = 0) {
        if (sb is null) {
            throw new ArgumentNullException(nameof(sb));
        }
        if (summary is null) {
            throw new ArgumentNullException(nameof(summary));
        }

        AppendLine(sb, baseIndentLevel, "<div class=\"summary\">");
        AppendLine(sb, baseIndentLevel + 1, "<div class=\"summary-stats\">");
        foreach (var stat in summary.Stats) {
            AppendLine(sb, baseIndentLevel + 2, "<div class=\"summary-stat\">");
            AppendTextElement(sb, baseIndentLevel + 3, "div", "summary-stat-label", stat.Label);
            AppendTextElement(sb, baseIndentLevel + 3, "div", "summary-stat-value", stat.Value);
            AppendLine(sb, baseIndentLevel + 2, "</div>");
        }
        AppendLine(sb, baseIndentLevel + 1, "</div>");
        AppendLine(sb, baseIndentLevel + 1, "<div class=\"summary-columns\">");
        AppendSummaryCardWithNotes(sb, summary.OverviewTitle, summary.OverviewNotes, baseIndentLevel + 2);
        AppendSummaryCardWithRows(sb, summary.TopRowsTitle, summary.TopRows, "No active breakdown totals available.", baseIndentLevel + 2);
        AppendSummaryCardWithRows(sb, summary.SecondaryRowsTitle, summary.SecondaryRows, summary.IsSourceRoot ? "No source-family totals available." : "No active sections available.", baseIndentLevel + 2);
        AppendLegendCard(sb, summary.LegendTitle, summary.LegendItems, baseIndentLevel + 2);
        AppendLine(sb, baseIndentLevel + 1, "</div>");
        AppendLine(sb, baseIndentLevel, "</div>");
    }

    private static void AppendSummaryCardWithNotes(StringBuilder sb, string title, IReadOnlyList<string> notes, int indentLevel) {
        AppendLine(sb, indentLevel, "<article class=\"summary-card\">");
        AppendTextElement(sb, indentLevel + 1, "h4", null, title);
        foreach (var note in notes) {
            AppendTextElement(sb, indentLevel + 1, "div", "note", note);
        }
        AppendLine(sb, indentLevel, "</article>");
    }

    private static void AppendSummaryCardWithRows(StringBuilder sb, string title, IReadOnlyList<UsageTelemetryBreakdownRowModel> rows, string emptyMessage, int indentLevel) {
        AppendLine(sb, indentLevel, "<article class=\"summary-card\">");
        AppendTextElement(sb, indentLevel + 1, "h4", null, title);
        if (rows.Count == 0) {
            AppendTextElement(sb, indentLevel + 1, "div", "empty", emptyMessage);
            AppendLine(sb, indentLevel, "</article>");
            return;
        }

        AppendLine(sb, indentLevel + 1, "<div class=\"summary-list\">");
        foreach (var row in rows) {
            var safeWidth = Math.Max(row.RatioPercent, row.RatioPercent > 0d ? 2d : 0d);
            AppendLine(sb, indentLevel + 2, "<div class=\"summary-row\">");
            AppendLine(sb, indentLevel + 3, "<div class=\"summary-row-head\">");
            AppendTextElement(sb, indentLevel + 4, "div", "summary-row-label", row.Label);
            AppendTextElement(sb, indentLevel + 4, "div", "summary-row-value", row.Value);
            AppendLine(sb, indentLevel + 3, "</div>");
            if (!string.IsNullOrWhiteSpace(row.Meta)) {
                AppendTextElement(sb, indentLevel + 3, "div", "summary-row-meta", row.Meta);
            }
            AppendLine(sb, indentLevel + 3, "<div class=\"summary-row-bar\">");
            AppendLine(
                sb,
                indentLevel + 4,
                "<div class=\"summary-row-fill\" style=\"width:" +
                Html(safeWidth.ToString("0.##", CultureInfo.InvariantCulture)) +
                "%\"></div>");
            AppendLine(sb, indentLevel + 3, "</div>");
            AppendLine(sb, indentLevel + 2, "</div>");
        }
        AppendLine(sb, indentLevel + 1, "</div>");
        AppendLine(sb, indentLevel, "</article>");
    }

    private static void AppendLegendCard(StringBuilder sb, string title, IReadOnlyList<UsageTelemetryBreakdownLegendItemModel> items, int indentLevel) {
        AppendLine(sb, indentLevel, "<article class=\"summary-card\">");
        AppendTextElement(sb, indentLevel + 1, "h4", null, title);
        if (items.Count == 0) {
            AppendTextElement(sb, indentLevel + 1, "div", "empty", "No legend categories defined for this breakdown.");
            AppendLine(sb, indentLevel, "</article>");
            return;
        }

        AppendLine(sb, indentLevel + 1, "<div class=\"legend\">");
        foreach (var item in items) {
            AppendLine(
                sb,
                indentLevel + 2,
                "<span class=\"legend-item\"><span class=\"legend-swatch\" style=\"background:" +
                Html(item.Color) +
                "\"></span>" +
                Html(item.Label) +
                "</span>");
        }
        AppendLine(sb, indentLevel + 1, "</div>");
        AppendLine(sb, indentLevel, "</article>");
    }

    private static void AppendTextElement(StringBuilder sb, int indentLevel, string tagName, string? className, string? text) {
        if (string.IsNullOrWhiteSpace(className)) {
            AppendLine(sb, indentLevel, $"<{tagName}>{Html(text)}</{tagName}>");
            return;
        }

        AppendLine(sb, indentLevel, $"<{tagName} class=\"{Html(className)}\">{Html(text)}</{tagName}>");
    }

    private static void AppendLine(StringBuilder sb, int indentLevel, string text) {
        sb.Append(' ', indentLevel * 2).AppendLine(text);
    }

    private static string Html(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
