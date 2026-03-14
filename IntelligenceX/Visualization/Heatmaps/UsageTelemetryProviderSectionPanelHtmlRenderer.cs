using System.Text;
using static IntelligenceX.Visualization.Heatmaps.UsageTelemetryOverviewHtmlFragments;
using static IntelligenceX.Visualization.Heatmaps.UsageTelemetryProviderSectionHtmlFragments;

namespace IntelligenceX.Visualization.Heatmaps;

internal static partial class UsageTelemetryProviderSectionHtmlRenderer {
    private static void AppendDatasetTabs(StringBuilder sb, UsageTelemetryOverviewSectionPageModel model) {
        sb.AppendLine("        <div class=\"provider-dataset-tabs\" role=\"tablist\" aria-label=\"Section datasets\">");
        foreach (var tab in model.DatasetTabs) {
            if (!string.IsNullOrWhiteSpace(tab.Href)) {
                sb.Append("          <a class=\"provider-dataset-tab provider-dataset-link\" href=\"")
                    .Append(Html(tab.Href!))
                    .Append("\" target=\"_blank\" rel=\"noopener\">")
                    .Append(Html(tab.Label))
                    .AppendLine("</a>");
                continue;
            }

            sb.Append("          <button type=\"button\" class=\"provider-dataset-tab");
            if (tab.IsDefault) {
                sb.Append(" active");
            }
            sb.Append("\" data-provider-panel=\"")
                .Append(Html(tab.Key))
                .Append("\" role=\"tab\" aria-selected=\"")
                .Append(tab.IsDefault ? "true" : "false")
                .Append("\">")
                .Append(Html(tab.Label))
                .AppendLine("</button>");
        }
        sb.AppendLine("        </div>");
    }

    private static void AppendSummaryPanel(StringBuilder sb, UsageTelemetryOverviewSectionPageModel model) {
        var section = model.Section;
        sb.AppendLine("        <div class=\"provider-panel active\" data-provider-panel-content=\"summary\">");
        sb.AppendLine("          <div class=\"provider-summary-stack\">");
        sb.AppendLine("            <div class=\"provider-spotlight\">");
        if (section.SpotlightCards.Count > 0) {
            foreach (var card in section.SpotlightCards) {
                AppendSpotlightCard(sb, card);
            }
        } else {
            AppendMiniCard(sb, "Most Used Model", section.MostUsedModel);
            AppendMiniCard(sb, "Recent Use (Last 30 Days)", section.RecentModel);
            AppendMiniMetricCard(sb, "Longest Streak", HeatmapDisplayText.FormatDays(section.LongestStreakDays));
            AppendMiniMetricCard(sb, "Current Streak", HeatmapDisplayText.FormatDays(section.CurrentStreakDays));
        }
        sb.AppendLine("            </div>");

        if (model.Flags.HasComposition && model.Flags.IsGitHub) {
            AppendProviderComposition(sb, section.Composition!);
        }
        if (model.Flags.HasMonthly && model.Flags.IsGitHub) {
            AppendProviderMonthlyUsage(sb, section, model.AccentColors.Total);
        }
        if (model.Flags.IsGitHub) {
            UsageTelemetryGitHubSectionHtmlRenderer.AppendSummaryStrip(sb, model.GitHub);
        }

        if (model.Flags.UseSummaryGrid) {
            AppendSummaryGrid(sb, model);
        } else if (model.Flags.HasPricing || model.Flags.HasModels || (model.Flags.HasAdditionalInsights && !model.Flags.IsGitHub)) {
            AppendSummaryInsights(sb, model);
        }

        sb.AppendLine("          </div>");
        sb.AppendLine("        </div>");
    }

    private static void AppendSummaryGrid(StringBuilder sb, UsageTelemetryOverviewSectionPageModel model) {
        var section = model.Section;
        sb.AppendLine("            <div class=\"provider-summary-grid\">");
        sb.AppendLine("              <div class=\"provider-summary-stack\">");
        if (model.Flags.HasComposition) {
            AppendProviderComposition(sb, section.Composition!);
        }
        if (model.Flags.HasMonthly) {
            AppendProviderMonthlyUsage(sb, section, model.AccentColors.Total);
        }
        sb.AppendLine("              </div>");
        sb.AppendLine("              <div class=\"provider-summary-stack\">");
        if (model.Flags.HasPricing) {
            sb.AppendLine("                <article class=\"insight-card\">");
            sb.AppendLine("                  <div class=\"insight-title\">Estimated API route</div>");
            AppendApiCostEstimate(sb, section.ApiCostEstimate);
            sb.AppendLine("                </article>");
        }
        if (model.Flags.HasModels) {
            sb.AppendLine("                <article class=\"insight-card\">");
            sb.AppendLine("                  <div class=\"insight-title\">Top models</div>");
            AppendTopModelsList(sb, section);
            sb.AppendLine("                </article>");
        }
        if (model.Flags.HasAdditionalInsights) {
            foreach (var insight in section.AdditionalInsights) {
                AppendInsightSection(sb, insight);
            }
        }
        sb.AppendLine("              </div>");
        sb.AppendLine("            </div>");
    }

    private static void AppendSummaryInsights(StringBuilder sb, UsageTelemetryOverviewSectionPageModel model) {
        var section = model.Section;
        sb.AppendLine("            <div class=\"provider-insights\">");
        if (model.Flags.HasPricing) {
            sb.AppendLine("              <article class=\"insight-card\">");
            sb.AppendLine("                <div class=\"insight-title\">Estimated API route</div>");
            AppendApiCostEstimate(sb, section.ApiCostEstimate);
            sb.AppendLine("              </article>");
        }
        if (model.Flags.HasModels) {
            sb.AppendLine("              <article class=\"insight-card\">");
            sb.AppendLine("                <div class=\"insight-title\">Top models</div>");
            AppendTopModelsList(sb, section);
            sb.AppendLine("              </article>");
        }
        if (model.Flags.HasAdditionalInsights && !model.Flags.IsGitHub) {
            foreach (var insight in section.AdditionalInsights) {
                AppendInsightSection(sb, insight);
            }
        }
        sb.AppendLine("            </div>");
    }

    private static void AppendActivityPanel(StringBuilder sb, UsageTelemetryOverviewSectionPageModel model) {
        var section = model.Section;
        sb.AppendLine("        <div class=\"provider-panel\" data-provider-panel-content=\"activity\">");
        if (model.Flags.HasMonthly) {
            AppendProviderMonthlyUsage(sb, section, model.AccentColors.Total);
        }
        sb.AppendLine("          <figure class=\"provider-heatmap\">");
        sb.Append("            <img src=\"").Append(Html(section.Key)).Append(".light.svg\" data-light-src=\"").Append(Html(section.Key)).Append(".light.svg\" data-dark-src=\"").Append(Html(section.Key)).Append(".dark.svg\" alt=\"").Append(Html(model.Title)).AppendLine(" usage heatmap\">");
        sb.AppendLine("          </figure>");
        if (!string.IsNullOrWhiteSpace(section.Note)) {
            AppendProviderNote(sb, section.Note!);
        }
        AppendProviderLegend(sb, section.ProviderId);
        sb.AppendLine("        </div>");
    }

    private static void AppendModelsPanel(StringBuilder sb, UsageTelemetryOverviewSectionPageModel model) {
        var section = model.Section;
        sb.AppendLine("        <div class=\"provider-panel\" data-provider-panel-content=\"models\">");
        sb.AppendLine("          <div class=\"provider-spotlight\">");
        AppendMiniCard(sb, "Most Used Model", section.MostUsedModel);
        AppendMiniCard(sb, "Recent Use (Last 30 Days)", section.RecentModel);
        sb.AppendLine("          </div>");
        sb.AppendLine("          <div class=\"provider-models-stack\">");
        sb.AppendLine("            <article class=\"insight-card\">");
        sb.AppendLine("              <div class=\"insight-title\">Top models</div>");
        AppendTopModelsList(sb, section);
        sb.AppendLine("            </article>");
        sb.AppendLine("          </div>");
        sb.AppendLine("        </div>");
    }

    private static void AppendPricingPanel(StringBuilder sb, UsageTelemetryOverviewSectionPageModel model) {
        sb.AppendLine("        <div class=\"provider-panel\" data-provider-panel-content=\"pricing\">");
        sb.AppendLine("          <article class=\"insight-card\">");
        sb.AppendLine("            <div class=\"insight-title\">Estimated API route</div>");
        AppendApiCostEstimate(sb, model.Section.ApiCostEstimate);
        sb.AppendLine("          </article>");
        sb.AppendLine("        </div>");
    }

    private static void AppendImpactPanel(StringBuilder sb, UsageTelemetryOverviewSectionPageModel model) {
        sb.AppendLine("        <div class=\"provider-panel\" data-provider-panel-content=\"impact\">");
        if (model.Flags.IsGitHub) {
            UsageTelemetryGitHubSectionHtmlRenderer.AppendImpactExplorer(sb, model.GitHub);
        } else {
            sb.AppendLine("          <div class=\"provider-insights\">");
            foreach (var insight in model.Section.AdditionalInsights) {
                AppendInsightSection(sb, insight);
            }
            sb.AppendLine("          </div>");
        }
        sb.AppendLine("        </div>");
    }

    private static void AppendProviderNote(StringBuilder sb, string note) {
        var segments = note
            .Split(new[] { " · " }, System.StringSplitOptions.RemoveEmptyEntries)
            .Select(static part => part.Trim())
            .Where(static part => !string.IsNullOrWhiteSpace(part))
            .ToArray();

        if (segments.Length <= 1) {
            sb.Append("          <div class=\"provider-note\">").Append(Html(note)).AppendLine("</div>");
            return;
        }

        sb.AppendLine("          <div class=\"provider-note provider-note-chips\">");
        foreach (var segment in segments) {
            sb.Append("            <span class=\"provider-note-chip\">")
                .Append(Html(segment))
                .AppendLine("</span>");
        }
        sb.AppendLine("          </div>");
    }
}
