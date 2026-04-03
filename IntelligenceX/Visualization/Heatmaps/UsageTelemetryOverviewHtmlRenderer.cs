using System.Text;
using IntelligenceX.Telemetry.Git;
using IntelligenceX.Telemetry.GitHub;
using static IntelligenceX.Visualization.Heatmaps.UsageTelemetryOverviewHtmlFragments;
namespace IntelligenceX.Visualization.Heatmaps;

#pragma warning disable CS1591

/// <summary>
/// Renders a bundled HTML report for telemetry usage overviews.
/// </summary>
internal static class UsageTelemetryOverviewHtmlRenderer {
    public static string Render(
        UsageTelemetryOverviewDocument overview,
        GitHubObservabilitySummaryData? gitHubObservabilitySummary = null,
        GitCodeChurnSummaryData? gitCodeChurnSummary = null) {
        if (overview is null) {
            throw new ArgumentNullException(nameof(overview));
        }

        var page = UsageTelemetryReportPageModelBuilders.BuildOverview(overview, gitHubObservabilitySummary, gitCodeChurnSummary);
        var sb = new StringBuilder(24 * 1024);
        UsageTelemetryReportPageShellHtmlRenderer.AppendOverviewHeader(sb, page);
        UsageTelemetryReportDiagnosticsHtmlRenderer.Append(sb, page.Diagnostics);
        if (page.CodeChurn is not null) {
            AppendCodeChurnSection(sb, page.CodeChurn);
        }
        if (page.ChurnUsageCorrelation is not null) {
            AppendChurnUsageCorrelationSection(sb, page.ChurnUsageCorrelation);
        }
        if (page.GitHubLocalAlignment is not null) {
            AppendGitHubLocalAlignmentSection(sb, page.GitHubLocalAlignment);
        }
        if (page.GitHubRepoClusters is not null) {
            AppendGitHubRepoClusterSection(sb, page.GitHubRepoClusters);
        }

        foreach (var providerSection in page.Sections) {
            AppendProviderSection(sb, providerSection);
        }

        UsageTelemetrySupportingBreakdownHtmlRenderer.AppendSection(sb, page.SupportingBreakdowns);
        UsageTelemetryReportPageShellHtmlRenderer.AppendFootnote(sb, page.Footnote);
        return UsageTelemetryReportStaticAssets.RenderOverviewPage(
            page.Title,
            sb.ToString(),
            page.BootstrapJson);
    }

    private static void AppendProviderSection(StringBuilder sb, UsageTelemetryOverviewSectionPageModel model) {
        UsageTelemetryProviderSectionHtmlRenderer.AppendSection(sb, model);
    }

    private static void AppendCodeChurnSection(StringBuilder sb, UsageTelemetryCodeChurnPageModel model) {
        sb.AppendLine("    <section class=\"provider-section\" id=\"code-churn\">");
        sb.AppendLine("      <div class=\"provider-shell\">");
        sb.AppendLine("        <div class=\"provider-header\">");
        sb.AppendLine("          <div>");
        sb.Append("            <h2 class=\"provider-title\">").Append(Html(model.Title)).AppendLine("</h2>");
        sb.Append("            <div class=\"provider-subtitle\">").Append(Html(model.Subtitle)).AppendLine("</div>");
        sb.AppendLine("          </div>");
        sb.AppendLine("          <div class=\"provider-metrics\">");
        foreach (var stat in model.Stats) {
            sb.AppendLine("            <div class=\"provider-metric\">");
            sb.Append("              <div class=\"metric-label\">").Append(Html(stat.Label)).AppendLine("</div>");
            sb.Append("              <div class=\"metric-value\">").Append(Html(stat.Value)).AppendLine("</div>");
            sb.AppendLine("            </div>");
        }
        sb.AppendLine("          </div>");
        sb.AppendLine("        </div>");
        sb.AppendLine("        <div class=\"provider-insights tight\">");
        sb.AppendLine("          <article class=\"provider-feature-card\">");
        sb.AppendLine("            <div class=\"provider-feature-kicker\">Local repository</div>");
        sb.Append("            <div class=\"provider-feature-headline\">").Append(Html(model.Headline)).AppendLine("</div>");
        if (!string.IsNullOrWhiteSpace(model.Note)) {
            sb.Append("            <div class=\"provider-feature-copy\">").Append(Html(model.Note!)).AppendLine("</div>");
        }
        sb.AppendLine("          </article>");
        AppendInsightSection(sb, model.DailyBreakdown);
        sb.AppendLine("        </div>");
        sb.AppendLine("      </div>");
        sb.AppendLine("    </section>");
    }

    private static void AppendChurnUsageCorrelationSection(StringBuilder sb, UsageTelemetryChurnUsageSignalPageModel model) {
        sb.AppendLine("    <section class=\"provider-section\" id=\"churn-usage-correlation\">");
        sb.AppendLine("      <div class=\"provider-shell\">");
        sb.AppendLine("        <div class=\"provider-header\">");
        sb.AppendLine("          <div>");
        sb.Append("            <h2 class=\"provider-title\">").Append(Html(model.Title)).AppendLine("</h2>");
        sb.Append("            <div class=\"provider-subtitle\">").Append(Html(model.Subtitle)).AppendLine("</div>");
        sb.AppendLine("          </div>");
        sb.AppendLine("          <div class=\"provider-metrics\">");
        foreach (var stat in model.Stats) {
            sb.AppendLine("            <div class=\"provider-metric\">");
            sb.Append("              <div class=\"metric-label\">").Append(Html(stat.Label)).AppendLine("</div>");
            sb.Append("              <div class=\"metric-value\">").Append(Html(stat.Value)).AppendLine("</div>");
            sb.AppendLine("            </div>");
        }
        sb.AppendLine("          </div>");
        sb.AppendLine("        </div>");
        sb.AppendLine("        <div class=\"provider-insights tight\">");
        sb.AppendLine("          <article class=\"provider-feature-card\">");
        sb.AppendLine("            <div class=\"provider-feature-kicker\">Recent window</div>");
        sb.Append("            <div class=\"provider-feature-headline\">").Append(Html(model.Headline)).AppendLine("</div>");
        if (!string.IsNullOrWhiteSpace(model.Note)) {
            sb.Append("            <div class=\"provider-feature-copy\">").Append(Html(model.Note!)).AppendLine("</div>");
        }
        sb.AppendLine("          </article>");
        AppendInsightSection(sb, model.ProviderSignals);
        sb.AppendLine("        </div>");
        sb.AppendLine("      </div>");
        sb.AppendLine("    </section>");
    }

    private static void AppendGitHubLocalAlignmentSection(StringBuilder sb, UsageTelemetryGitHubLocalPulsePageModel model) {
        sb.AppendLine("    <section class=\"provider-section\" id=\"github-local-alignment\">");
        sb.AppendLine("      <div class=\"provider-shell\">");
        sb.AppendLine("        <div class=\"provider-header\">");
        sb.AppendLine("          <div>");
        sb.Append("            <h2 class=\"provider-title\">").Append(Html(model.Title)).AppendLine("</h2>");
        sb.Append("            <div class=\"provider-subtitle\">").Append(Html(model.Subtitle)).AppendLine("</div>");
        sb.AppendLine("          </div>");
        sb.AppendLine("          <div class=\"provider-metrics\">");
        foreach (var stat in model.Stats) {
            sb.AppendLine("            <div class=\"provider-metric\">");
            sb.Append("              <div class=\"metric-label\">").Append(Html(stat.Label)).AppendLine("</div>");
            sb.Append("              <div class=\"metric-value\">").Append(Html(stat.Value)).AppendLine("</div>");
            sb.AppendLine("            </div>");
        }
        sb.AppendLine("          </div>");
        sb.AppendLine("        </div>");
        sb.AppendLine("        <div class=\"provider-insights tight\">");
        sb.AppendLine("          <article class=\"provider-feature-card\">");
        sb.AppendLine("            <div class=\"provider-feature-kicker\">Recent window</div>");
        sb.Append("            <div class=\"provider-feature-headline\">").Append(Html(model.Headline)).AppendLine("</div>");
        if (!string.IsNullOrWhiteSpace(model.Note)) {
            sb.Append("            <div class=\"provider-feature-copy\">").Append(Html(model.Note!)).AppendLine("</div>");
        }
        sb.AppendLine("          </article>");
        AppendInsightSection(sb, model.Repositories);
        sb.AppendLine("        </div>");
        sb.AppendLine("      </div>");
        sb.AppendLine("    </section>");
    }

    private static void AppendGitHubRepoClusterSection(StringBuilder sb, UsageTelemetryGitHubRepoClusterPageModel model) {
        sb.AppendLine("    <section class=\"provider-section\" id=\"github-repo-clusters\">");
        sb.AppendLine("      <div class=\"provider-shell\">");
        sb.AppendLine("        <div class=\"provider-header\">");
        sb.AppendLine("          <div>");
        sb.Append("            <h2 class=\"provider-title\">").Append(Html(model.Title)).AppendLine("</h2>");
        sb.Append("            <div class=\"provider-subtitle\">").Append(Html(model.Subtitle)).AppendLine("</div>");
        sb.AppendLine("          </div>");
        sb.AppendLine("          <div class=\"provider-metrics\">");
        foreach (var stat in model.Stats) {
            sb.AppendLine("            <div class=\"provider-metric\">");
            sb.Append("              <div class=\"metric-label\">").Append(Html(stat.Label)).AppendLine("</div>");
            sb.Append("              <div class=\"metric-value\">").Append(Html(stat.Value)).AppendLine("</div>");
            sb.AppendLine("            </div>");
        }
        sb.AppendLine("          </div>");
        sb.AppendLine("        </div>");
        sb.AppendLine("        <div class=\"provider-insights tight\">");
        sb.AppendLine("          <article class=\"provider-feature-card\">");
        sb.AppendLine("            <div class=\"provider-feature-kicker\">Recent window</div>");
        sb.Append("            <div class=\"provider-feature-headline\">").Append(Html(model.Headline)).AppendLine("</div>");
        if (!string.IsNullOrWhiteSpace(model.Note)) {
            sb.Append("            <div class=\"provider-feature-copy\">").Append(Html(model.Note!)).AppendLine("</div>");
        }
        sb.AppendLine("          </article>");
        AppendInsightSection(sb, model.Clusters);
        sb.AppendLine("        </div>");
        sb.AppendLine("      </div>");
        sb.AppendLine("    </section>");
    }
}
