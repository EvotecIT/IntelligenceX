using System;
using System.Globalization;
using System.Text;

namespace IntelligenceX.Visualization.Heatmaps;

internal static class UsageTelemetryGitHubWrappedHtmlFragments {
    public static void AppendStatCard(StringBuilder sb, string label, string value, string? copy) {
        sb.AppendLine("        <article class=\"stat-card wrapped-panel\">");
        sb.Append("          <div class=\"stat-label wrapped-label\">").Append(Html(label)).AppendLine("</div>");
        sb.Append("          <div class=\"stat-value\">").Append(Html(value)).AppendLine("</div>");
        if (!string.IsNullOrWhiteSpace(copy)) {
            sb.Append("          <div class=\"stat-copy wrapped-copy\">").Append(Html(copy!)).AppendLine("</div>");
        }
        sb.AppendLine("        </article>");
    }

    public static void AppendWrappedCardMetric(StringBuilder sb, string label, string value, string? copy) {
        sb.AppendLine("        <div class=\"metric wrapped-soft-panel\">");
        sb.Append("          <div class=\"metric-label wrapped-label\">").Append(Html(label)).AppendLine("</div>");
        sb.Append("          <div class=\"metric-value\">").Append(Html(value)).AppendLine("</div>");
        if (!string.IsNullOrWhiteSpace(copy)) {
            sb.Append("          <div class=\"metric-copy wrapped-copy\">").Append(Html(copy!)).AppendLine("</div>");
        }
        sb.AppendLine("        </div>");
    }

    public static void AppendWrappedCardStat(StringBuilder sb, string label, string value, string? copy) {
        sb.AppendLine("      <div class=\"stat wrapped-soft-panel\">");
        sb.Append("        <div class=\"stat-label wrapped-label\">").Append(Html(label)).AppendLine("</div>");
        sb.Append("        <div class=\"stat-value\">").Append(Html(value)).AppendLine("</div>");
        if (!string.IsNullOrWhiteSpace(copy)) {
            sb.Append("        <div class=\"stat-copy wrapped-copy\">").Append(Html(copy!)).AppendLine("</div>");
        }
        sb.AppendLine("      </div>");
    }

    public static void AppendWrappedCardFooter(StringBuilder sb, string label, string value, string? copy) {
        sb.AppendLine("      <div class=\"footer-card wrapped-soft-panel\">");
        sb.Append("        <div class=\"footer-title wrapped-label\">").Append(Html(label)).AppendLine("</div>");
        sb.Append("        <div class=\"footer-value\">").Append(Html(value)).AppendLine("</div>");
        if (!string.IsNullOrWhiteSpace(copy)) {
            sb.Append("        <div class=\"footer-copy wrapped-copy\">").Append(Html(copy!)).AppendLine("</div>");
        }
        sb.AppendLine("      </div>");
    }

    public static void AppendYearComparison(StringBuilder sb, UsageTelemetryOverviewInsightSection insight) {
        sb.AppendLine("      <div class=\"compare-grid\">");
        if (insight.Rows.Count > 0) {
            AppendCompareCard(sb, insight.Rows[0], false);
        }
        sb.AppendLine("        <div class=\"compare-arrow\">→</div>");
        if (insight.Rows.Count > 1) {
            AppendCompareCard(sb, insight.Rows[1], true);
        } else {
            AppendCompareCard(sb, new UsageTelemetryOverviewInsightRow("Current", insight.Headline ?? "n/a", insight.Note), true);
        }
        sb.AppendLine("      </div>");
    }

    public static void AppendMiniCard(StringBuilder sb, string label, string value, string? copy) {
        sb.AppendLine("        <article class=\"mini-card wrapped-soft-panel\">");
        sb.Append("          <div class=\"mini-label wrapped-label\">").Append(Html(label)).AppendLine("</div>");
        sb.Append("          <div class=\"mini-value\">").Append(Html(value)).AppendLine("</div>");
        if (!string.IsNullOrWhiteSpace(copy)) {
            sb.Append("          <div class=\"mini-copy wrapped-copy\">").Append(Html(copy!)).AppendLine("</div>");
        }
        sb.AppendLine("        </article>");
    }

    public static void AppendMonthlyContributionsPanel(StringBuilder sb, UsageTelemetryGitHubWrappedPageModel page) {
        var months = page.MonthlyUsage ?? Array.Empty<UsageTelemetryOverviewMonthlyUsage>();
        var maxValue = months.Count == 0 ? 0L : months.Max(static month => month.TotalValue);

        sb.AppendLine("      <article class=\"panel wrapped-panel\">");
        sb.AppendLine("        <h2 class=\"section-title\">Monthly contributions</h2>");
        sb.Append("        <p class=\"section-copy\">").Append(Html(months.Count.ToString(CultureInfo.InvariantCulture))).AppendLine(" month contribution window.</p>");
        sb.AppendLine("        <div class=\"month-grid\">");
        foreach (var month in months) {
            var height = maxValue <= 0L ? 6d : Math.Max(6d, month.TotalValue / (double)maxValue * 96d);
            sb.AppendLine("          <div class=\"month\">");
            sb.AppendLine("            <div class=\"month-bar-wrap\">");
            sb.Append("              <div class=\"month-bar\" style=\"height:")
                .Append(Html(height.ToString("0.##", CultureInfo.InvariantCulture)))
                .AppendLine("px;\"></div>");
            sb.AppendLine("            </div>");
            sb.Append("            <div class=\"month-label\">").Append(Html(month.Label)).AppendLine("</div>");
            sb.Append("            <div class=\"month-value\">").Append(Html(FormatCompact(month.TotalValue))).AppendLine("</div>");
            sb.AppendLine("          </div>");
        }
        sb.AppendLine("        </div>");
        sb.AppendLine("      </article>");
    }

    public static void AppendRecentRepositoriesPanel(StringBuilder sb, UsageTelemetryOverviewInsightSection insight) {
        sb.AppendLine("      <article class=\"panel wrapped-panel\">");
        sb.Append("        <h2 class=\"section-title\">").Append(Html(insight.Title)).AppendLine("</h2>");
        if (!string.IsNullOrWhiteSpace(insight.Note)) {
            sb.Append("        <p class=\"section-copy\">").Append(Html(insight.Note!)).AppendLine("</p>");
        }
        sb.AppendLine("        <div class=\"row-list\">");
        foreach (var row in insight.Rows.Take(6)) {
            AppendInsightRow(sb, row, includeBadge: true);
        }
        sb.AppendLine("        </div>");
        sb.AppendLine("      </article>");
    }

    public static void AppendInsightPanel(StringBuilder sb, UsageTelemetryOverviewInsightSection insight, string fallbackTitle) {
        sb.AppendLine("      <article class=\"panel wrapped-panel\">");
        sb.Append("        <h2 class=\"section-title\">").Append(Html(string.IsNullOrWhiteSpace(insight.Title) ? fallbackTitle : insight.Title)).AppendLine("</h2>");
        if (!string.IsNullOrWhiteSpace(insight.Note)) {
            sb.Append("        <p class=\"section-copy\">").Append(Html(insight.Note!)).AppendLine("</p>");
        }
        sb.AppendLine("        <div class=\"row-list\">");
        foreach (var row in insight.Rows.Take(6)) {
            AppendInsightRow(sb, row, includeBadge: false);
        }
        sb.AppendLine("        </div>");
        sb.AppendLine("      </article>");
    }

    public static string FindSpotlightValue(UsageTelemetryGitHubWrappedPageModel page, string key, string fallback) {
        return page.SpotlightCards.FirstOrDefault(card => string.Equals(card.Key, key, StringComparison.OrdinalIgnoreCase))?.Value
               ?? fallback;
    }

    public static string? FindSpotlightSubtitle(UsageTelemetryGitHubWrappedPageModel page, string key) {
        return page.SpotlightCards.FirstOrDefault(card => string.Equals(card.Key, key, StringComparison.OrdinalIgnoreCase))?.Subtitle;
    }

    public static (string CssClass, string Label) ResolveRepoHealthBadge(string? subtitle) {
        var normalized = NormalizeOptional(subtitle);
        if (string.IsNullOrWhiteSpace(normalized)) {
            return ("dormant", string.Empty);
        }

        var text = normalized!;
        return text.StartsWith("Rising ·", StringComparison.OrdinalIgnoreCase)
            ? ("rising", "Rising")
            : text.StartsWith("Active ·", StringComparison.OrdinalIgnoreCase)
                ? ("active", "Active")
                : text.StartsWith("Established ·", StringComparison.OrdinalIgnoreCase)
                    ? ("established", "Established")
                    : text.StartsWith("Warm ·", StringComparison.OrdinalIgnoreCase)
                        ? ("warm", "Warm")
                        : text.StartsWith("Dormant ·", StringComparison.OrdinalIgnoreCase)
                            ? ("dormant", "Dormant")
                            : ("dormant", string.Empty);
    }

    private static void AppendCompareCard(StringBuilder sb, UsageTelemetryOverviewInsightRow row, bool highlight) {
        sb.Append("      <article class=\"compare-card wrapped-panel");
        if (highlight) {
            sb.Append(" highlight");
        }
        sb.AppendLine("\">");
        sb.Append("        <div class=\"compare-label wrapped-label\">").Append(Html(row.Label)).AppendLine("</div>");
        sb.Append("        <div class=\"compare-value\">").Append(Html(row.Value)).AppendLine("</div>");
        if (!string.IsNullOrWhiteSpace(row.Subtitle)) {
            sb.Append("        <div class=\"compare-subtitle wrapped-copy\">").Append(Html(row.Subtitle!)).AppendLine("</div>");
        }
        sb.AppendLine("      </article>");
    }

    private static void AppendInsightRow(StringBuilder sb, UsageTelemetryOverviewInsightRow row, bool includeBadge) {
        sb.AppendLine("          <div class=\"row\">");
        sb.AppendLine("            <div class=\"row-head\">");
        sb.Append("              <div class=\"row-label\">");
        if (!string.IsNullOrWhiteSpace(row.Href)) {
            sb.Append("<a href=\"").Append(Html(row.Href!)).Append("\" target=\"_blank\" rel=\"noopener\">")
                .Append(Html(row.Label))
                .Append("</a>");
        } else {
            sb.Append(Html(row.Label));
        }
        sb.AppendLine("</div>");
        sb.Append("              <div class=\"row-value\">").Append(Html(row.Value)).AppendLine("</div>");
        sb.AppendLine("            </div>");
        if (!string.IsNullOrWhiteSpace(row.Subtitle)) {
            sb.Append("            <div class=\"row-copy wrapped-copy\">").Append(Html(row.Subtitle!)).AppendLine("</div>");
        }
        if (row.Ratio.HasValue && row.Ratio.Value > 0d) {
            sb.AppendLine("            <div class=\"row-bar\">");
            sb.Append("              <div class=\"row-fill\" style=\"width:")
                .Append(Html(FormatRatioPercent(row.Ratio)))
                .AppendLine("%;\"></div>");
            sb.AppendLine("            </div>");
        }
        if (includeBadge) {
            var (badgeClass, badgeLabel) = ResolveRepoHealthBadge(row.Subtitle);
            if (!string.IsNullOrWhiteSpace(badgeLabel)) {
                sb.AppendLine("            <div class=\"badge-row\">");
                sb.Append("              <span class=\"badge ").Append(Html(badgeClass)).Append("\">").Append(Html(badgeLabel)).AppendLine("</span>");
                sb.AppendLine("            </div>");
            }
        }
        sb.AppendLine("          </div>");
    }

    private static string? NormalizeOptional(string? value) {
        return string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
    }

    private static string Html(string value) => UsageTelemetryOverviewHtmlFragments.Html(value);
    private static string FormatCompact(long value) => UsageTelemetryOverviewHtmlFragments.FormatCompact(value);
    private static string FormatRatioPercent(double? ratio) => UsageTelemetryOverviewHtmlFragments.FormatRatioPercent(ratio);
}
