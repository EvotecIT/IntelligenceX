using System;
using System.Text;
using static IntelligenceX.Visualization.Heatmaps.UsageTelemetryOverviewHtmlFragments;
using static IntelligenceX.Visualization.Heatmaps.UsageTelemetryProviderSectionHtmlFragments;

namespace IntelligenceX.Visualization.Heatmaps;

internal static partial class UsageTelemetryProviderSectionHtmlRenderer {
    public static void AppendSection(StringBuilder sb, UsageTelemetryOverviewSectionPageModel model) {
        var section = model.Section;
        var accentColors = model.AccentColors;
        var hasMonthly = model.Flags.HasMonthly;
        var hasModels = model.Flags.HasModels;
        var hasPricing = model.Flags.HasPricing;
        var hasComposition = model.Flags.HasComposition;
        var hasAdditionalInsights = model.Flags.HasAdditionalInsights;
        var hasActivity = model.Flags.HasActivity;
        var providerSectionId = model.ProviderSectionId;
        sb.Append("    <section class=\"provider-section\" id=\"")
            .Append(Html(providerSectionId))
            .Append("\" data-provider=\"")
            .Append(Html(model.ProviderId))
            .AppendLine("\">");
        sb.AppendLine("      <div class=\"provider-shell\">");
        sb.AppendLine("      <div class=\"provider-header\">");
        sb.AppendLine("        <div>");
        sb.Append("          <h2 class=\"provider-title\">").Append(Html(model.Title)).AppendLine("</h2>");
        sb.Append("          <div class=\"provider-subtitle\">").Append(Html(model.Subtitle)).AppendLine("</div>");
        sb.AppendLine("        </div>");
        sb.AppendLine("        <div class=\"provider-metrics\">");
        if (section.Metrics.Count > 0) {
            foreach (var metric in section.Metrics) {
                AppendProviderMetric(sb, metric);
            }
        } else {
            AppendProviderMetric(sb, new UsageTelemetryOverviewSectionMetric("input", "Input Tokens", FormatCompact(section.InputTokens), FormatPercent(section.InputTokens, section.TotalTokens) + " of section total", ComputeRatio(section.InputTokens, section.TotalTokens), accentColors.Input));
            AppendProviderMetric(sb, new UsageTelemetryOverviewSectionMetric("output", "Output Tokens", FormatCompact(section.OutputTokens), FormatPercent(section.OutputTokens, section.TotalTokens) + " of section total", ComputeRatio(section.OutputTokens, section.TotalTokens), accentColors.Output));
            AppendProviderMetric(sb, new UsageTelemetryOverviewSectionMetric("total", "Total Tokens", FormatCompact(section.TotalTokens), "100% of section total", section.TotalTokens > 0 ? 1d : 0d, accentColors.Total));
        }
        sb.AppendLine("        </div>");
        sb.AppendLine("      </div>");
        UsageTelemetryReportDiagnosticsHtmlRenderer.Append(sb, model.Diagnostics, indentLevel: 3);
        sb.AppendLine("      <div class=\"provider-datasets\">");
        AppendDatasetTabs(sb, model);
        AppendSummaryPanel(sb, model);
        if (hasActivity) {
            AppendActivityPanel(sb, model);
        }
        if (hasModels) {
            AppendModelsPanel(sb, model);
        }
        if (hasPricing) {
            AppendPricingPanel(sb, model);
        }
        if (hasAdditionalInsights) {
            AppendImpactPanel(sb, model);
        }
        sb.AppendLine("      </div>");
        sb.AppendLine("      </div>");
        sb.AppendLine("    </section>");
    }
}
