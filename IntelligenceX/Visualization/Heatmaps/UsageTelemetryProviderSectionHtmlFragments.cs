using System;
using System.Globalization;
using System.Text;
using static IntelligenceX.Visualization.Heatmaps.UsageTelemetryOverviewHtmlFragments;

namespace IntelligenceX.Visualization.Heatmaps;

internal static class UsageTelemetryProviderSectionHtmlFragments {
    public static void AppendProviderMetric(StringBuilder sb, UsageTelemetryOverviewSectionMetric metric) {
        sb.AppendLine("          <div class=\"provider-metric\">");
        sb.Append("            <div class=\"metric-label\">").Append(Html(metric.Label.ToUpperInvariant())).AppendLine("</div>");
        sb.Append("            <div class=\"metric-value\">").Append(Html(metric.Value)).AppendLine("</div>");
        sb.Append("            <div class=\"metric-copy\">").Append(Html(metric.Subtitle ?? string.Empty)).AppendLine("</div>");
        sb.AppendLine("            <div class=\"metric-bar\">");
        sb.Append("              <div class=\"metric-fill\" style=\"width:").Append(Html(FormatRatioPercent(metric.Ratio))).Append("%; background:").Append(Html(metric.Color)).AppendLine(";\"></div>");
        sb.AppendLine("            </div>");
        sb.AppendLine("          </div>");
    }

    public static void AppendProviderComposition(StringBuilder sb, UsageTelemetryOverviewComposition composition) {
        sb.AppendLine("      <div class=\"provider-token-mix\">");
        sb.AppendLine("        <div class=\"provider-token-mix-header\">");
        sb.Append("          <div class=\"provider-token-mix-title\">").Append(Html(composition.Title)).AppendLine("</div>");
        sb.Append("          <div class=\"provider-token-mix-copy\">").Append(Html(composition.Copy)).AppendLine("</div>");
        sb.AppendLine("        </div>");
        sb.AppendLine("        <div class=\"provider-token-mix-bar\">");
        foreach (var item in composition.Items) {
            AppendProviderTokenSegment(sb, item);
        }
        sb.AppendLine("        </div>");
        sb.AppendLine("        <div class=\"provider-token-mix-legend\">");
        foreach (var item in composition.Items) {
            AppendProviderTokenMixItem(sb, item);
        }
        sb.AppendLine("        </div>");
        sb.AppendLine("      </div>");
    }

    public static void AppendProviderLegend(StringBuilder sb, string providerId) {
        var palette = ResolveLegendColors(providerId);
        sb.AppendLine("      <div class=\"provider-legend\">");
        sb.AppendLine("        <span class=\"legend-copy\">Less</span>");
        foreach (var color in palette) {
            sb.Append("        <span class=\"legend-swatch\" style=\"background:").Append(Html(color)).AppendLine("\"></span>");
        }
        sb.AppendLine("        <span class=\"legend-copy\">More</span>");
        sb.AppendLine("      </div>");
    }

    public static void AppendProviderMonthlyUsage(StringBuilder sb, UsageTelemetryOverviewProviderSection section, string accentColor) {
        var months = section.MonthlyUsage ?? Array.Empty<UsageTelemetryOverviewMonthlyUsage>();
        if (months.Count == 0) {
            return;
        }

        var maxTokens = 0L;
        foreach (var month in months) {
            if (month.TotalValue > maxTokens) {
                maxTokens = month.TotalValue;
            }
        }
        sb.AppendLine("      <div class=\"provider-monthly\">");
        sb.AppendLine("        <div class=\"provider-monthly-header\">");
        sb.Append("          <div class=\"provider-monthly-title\">").Append(Html(section.MonthlyUsageTitle)).AppendLine("</div>");
        sb.Append("          <div class=\"provider-monthly-copy\">").Append(Html(months.Count.ToString(CultureInfo.InvariantCulture))).AppendLine(" month window</div>");
        sb.AppendLine("        </div>");
        sb.AppendLine("        <div class=\"provider-monthly-grid\">");
        foreach (var month in months) {
            var height = maxTokens <= 0L ? 4d : Math.Max(4d, month.TotalValue / (double)maxTokens * 90d);
            var alpha = month.TotalValue <= 0L ? "33" : Math.Max(64, Math.Min(255, (int)Math.Round(month.TotalValue / (double)Math.Max(1L, maxTokens) * 255d))).ToString("X2", CultureInfo.InvariantCulture);
            var monthColor = month.TotalValue <= 0L ? "#dfdfdf" : accentColor + alpha;
            var title = $"{month.Key}: {FormatCompact(month.TotalValue)} {section.MonthlyUsageUnitsLabel}";
            if (month.ActiveDays > 0) {
                title += $" across {month.ActiveDays} active day(s)";
            }

            sb.Append("          <div class=\"provider-month\" title=\"").Append(Html(title)).AppendLine("\">");
            sb.AppendLine("            <div class=\"provider-month-bar-wrap\">");
            sb.Append("              <div class=\"provider-month-bar\" style=\"height:")
                .Append(Html(height.ToString("0.##", CultureInfo.InvariantCulture)))
                .Append("px; background:")
                .Append(Html(monthColor))
                .AppendLine(";\"></div>");
            sb.AppendLine("            </div>");
            sb.Append("            <div class=\"provider-month-label\">").Append(Html(month.Label)).AppendLine("</div>");
            sb.Append("            <div class=\"provider-month-value\">").Append(Html(FormatCompact(month.TotalValue))).AppendLine("</div>");
            sb.AppendLine("          </div>");
        }
        sb.AppendLine("        </div>");
        sb.AppendLine("      </div>");
    }

    public static void AppendTopModelsList(StringBuilder sb, UsageTelemetryOverviewProviderSection section) {
        if (section.TopModels.Count == 0) {
            sb.AppendLine("          <div class=\"estimate-note\">No model breakdown available.</div>");
            return;
        }

        sb.AppendLine("          <div class=\"rank-list\">");
        var rank = 1;
        foreach (var model in section.TopModels) {
            sb.AppendLine("            <div class=\"rank-row\">");
            sb.Append("              <div class=\"rank-index\">").Append(rank.ToString(CultureInfo.InvariantCulture)).AppendLine(".</div>");
            sb.Append("              <div class=\"rank-label\">").Append(Html(model.Model)).AppendLine("</div>");
            sb.Append("              <div class=\"rank-value\">")
                .Append(Html(FormatCompact(model.TotalTokens)))
                .Append(" (")
                .Append(Html(model.SharePercent.ToString("0.#", CultureInfo.InvariantCulture)))
                .AppendLine("%)</div>");
            sb.AppendLine("            </div>");
            rank++;
        }
        sb.AppendLine("          </div>");
    }

    public static void AppendApiCostEstimate(StringBuilder sb, UsageTelemetryOverviewApiCostEstimate? estimate) {
        if (estimate is null) {
            sb.AppendLine("          <div class=\"estimate-note\">No model pricing coverage available for this section yet.</div>");
            return;
        }

        sb.AppendLine("          <div class=\"estimate-total\">");
        sb.Append("            <div class=\"estimate-value\">$").Append(Html(FormatCurrencyCompact(estimate.TotalEstimatedCostUsd))).AppendLine("</div>");
        sb.Append("            <div class=\"estimate-copy\">Estimated from exact token telemetry using current public API rates.</div>");
        sb.AppendLine("          </div>");
        if (estimate.TopDrivers.Count > 0) {
            sb.AppendLine("          <div class=\"rank-list\">");
            foreach (var driver in estimate.TopDrivers) {
                sb.AppendLine("            <div class=\"rank-row\">");
                sb.AppendLine("              <div class=\"rank-index\">$</div>");
                sb.Append("              <div class=\"rank-label\">").Append(Html(driver.Model)).AppendLine("</div>");
                sb.Append("              <div class=\"rank-value\">$")
                    .Append(Html(FormatCurrencyCompact(driver.EstimatedCostUsd)))
                    .Append(" (")
                    .Append(Html(driver.SharePercent.ToString("0.#", CultureInfo.InvariantCulture)))
                    .AppendLine("%)</div>");
                sb.AppendLine("            </div>");
            }
            sb.AppendLine("          </div>");
        }

        var totalTokens = estimate.CoveredTokens + estimate.UncoveredTokens;
        var coveredPercent = totalTokens <= 0L ? 0d : estimate.CoveredTokens / (double)totalTokens * 100d;
        sb.Append("          <div class=\"estimate-note\">Priced coverage: ")
            .Append(Html(coveredPercent.ToString("0.#", CultureInfo.InvariantCulture)))
            .Append("% of tokens");
        if (estimate.UncoveredTokens > 0L) {
            sb.Append(" (")
                .Append(Html(FormatCompact(estimate.UncoveredTokens)))
                .Append(" unpriced)");
        }
        sb.AppendLine(".</div>");
    }

    public static void AppendMiniCard(StringBuilder sb, string label, UsageTelemetryOverviewModelHighlight? highlight) {
        sb.AppendLine("        <article class=\"mini-card\">");
        sb.Append("          <div class=\"mini-label\">").Append(Html(label.ToUpperInvariant())).AppendLine("</div>");
        if (highlight is null) {
            sb.AppendLine("          <div class=\"mini-value\">n/a</div>");
        } else {
            sb.Append("          <div class=\"mini-value\">").Append(Html(highlight.Model)).Append(" <span>(").Append(Html(FormatCompact(highlight.TotalTokens))).AppendLine(")</span></div>");
        }
        sb.AppendLine("        </article>");
    }

    public static void AppendMiniMetricCard(StringBuilder sb, string label, string value) {
        sb.AppendLine("        <article class=\"mini-card\">");
        sb.Append("          <div class=\"mini-label\">").Append(Html(label.ToUpperInvariant())).AppendLine("</div>");
        sb.Append("          <div class=\"mini-value\">").Append(Html(value)).AppendLine("</div>");
        sb.AppendLine("        </article>");
    }

    public static void AppendSpotlightCard(StringBuilder sb, UsageTelemetryOverviewCard card) {
        sb.AppendLine("        <article class=\"mini-card\">");
        sb.Append("          <div class=\"mini-label\">").Append(Html(card.Label.ToUpperInvariant())).AppendLine("</div>");
        sb.Append("          <div class=\"mini-value\">").Append(Html(card.Value)).AppendLine("</div>");
        if (!string.IsNullOrWhiteSpace(card.Subtitle)) {
            sb.Append("          <div class=\"mini-copy\">").Append(Html(card.Subtitle!)).AppendLine("</div>");
        }
        sb.AppendLine("        </article>");
    }

    private static void AppendProviderTokenSegment(StringBuilder sb, UsageTelemetryOverviewCompositionItem item) {
        if (!item.Ratio.HasValue || item.Ratio.Value <= 0d) {
            return;
        }

        sb.Append("          <span class=\"provider-token-segment\" style=\"width:")
            .Append(Html(FormatRatioPercent(item.Ratio)))
            .Append("%; background:")
            .Append(Html(item.Color))
            .AppendLine(";\"></span>");
    }

    private static void AppendProviderTokenMixItem(StringBuilder sb, UsageTelemetryOverviewCompositionItem item) {
        sb.Append("          <div class=\"provider-token-mix-item\"><span class=\"provider-token-dot\" style=\"background:")
            .Append(Html(item.Color))
            .Append("\"></span>")
            .Append(Html(item.Label))
            .Append(": <strong>")
            .Append(Html(item.Value))
            .Append("</strong>");
        if (!string.IsNullOrWhiteSpace(item.Subtitle)) {
            sb.Append(" <span>(")
                .Append(Html(item.Subtitle!))
                .Append(")</span>");
        }
        sb.AppendLine("</div>");
    }
}
