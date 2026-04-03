using System;
using System.Text;
using IntelligenceX.Json;
using IntelligenceX.Telemetry.GitHub;
using IntelligenceX.Telemetry.Usage;
using static IntelligenceX.Visualization.Heatmaps.UsageTelemetryGitHubWrappedHtmlFragments;

namespace IntelligenceX.Visualization.Heatmaps;

#pragma warning disable CS1591

internal static class GitHubWrappedCardHtmlRenderer {
    public static string Render(
        UsageTelemetryOverviewProviderSection section,
        UsageSummarySnapshot? summary = null,
        JsonObject? metadata = null,
        int providerSectionsCount = 0,
        GitHubObservabilitySummaryData? gitHubObservabilitySummary = null,
        GitHubLocalActivityCorrelationSummaryData? gitHubLocalAlignmentSummary = null) {
        if (section is null) {
            throw new ArgumentNullException(nameof(section));
        }

        var page = UsageTelemetryReportPageModelBuilders.BuildGitHubWrappedCard(
            section,
            summary,
            metadata,
            providerSectionsCount,
            gitHubObservabilitySummary,
            gitHubLocalAlignmentSummary);

        var sb = new StringBuilder(16 * 1024);
        sb.AppendLine("  <article class=\"card wrapped-panel\">");
        sb.AppendLine("    <div class=\"top\">");
        sb.AppendLine("      <div>");
        sb.AppendLine("        <div class=\"eyebrow\">GitHub Wrapped Card</div>");
        sb.Append("        <h1>").Append(Html(page.Title)).AppendLine("</h1>");
        sb.Append("        <div class=\"subtitle\">").Append(Html(page.Subtitle)).AppendLine("</div>");
        sb.AppendLine("      </div>");
        sb.AppendLine("      <div class=\"meta-grid\">");
        foreach (var metric in page.Metrics) {
            AppendWrappedCardMetric(sb, metric.Label, metric.Value, metric.Copy);
        }
        sb.AppendLine("      </div>");
        sb.AppendLine("    </div>");
        UsageTelemetryReportDiagnosticsHtmlRenderer.Append(sb, page.Diagnostics, indentLevel: 2);
        sb.AppendLine("    <div class=\"heatmap\">");
        sb.AppendLine("      <img src=\"provider-github.light.svg\" data-light-src=\"provider-github.light.svg\" data-dark-src=\"provider-github.dark.svg\" alt=\"GitHub activity heatmap\">");
        sb.AppendLine("    </div>");
        sb.AppendLine("    <div class=\"stats\">");
        foreach (var stat in page.Stats) {
            AppendWrappedCardStat(sb, stat.Label, stat.Value, stat.Copy);
        }
        sb.AppendLine("    </div>");
        sb.AppendLine("    <div class=\"footer\">");
        foreach (var footer in page.FooterMetrics) {
            AppendWrappedCardFooter(sb, footer.Label, footer.Value, footer.Copy);
        }
        sb.AppendLine("    </div>");
        sb.AppendLine("  </article>");

        return UsageTelemetryReportStaticAssets.RenderPage(
            "github-wrapped-card.html",
            page.Title + " Wrapped Card",
            sb.ToString(),
            page.BootstrapJson);
    }

    private static string Html(string value) => UsageTelemetryOverviewHtmlFragments.Html(value);
}
