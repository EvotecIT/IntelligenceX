using System.Text;
using static IntelligenceX.Visualization.Heatmaps.UsageTelemetryOverviewHtmlFragments;
namespace IntelligenceX.Visualization.Heatmaps;

#pragma warning disable CS1591

/// <summary>
/// Renders a bundled HTML report for telemetry usage overviews.
/// </summary>
internal static class UsageTelemetryOverviewHtmlRenderer {
    public static string Render(UsageTelemetryOverviewDocument overview) {
        if (overview is null) {
            throw new ArgumentNullException(nameof(overview));
        }

        var page = UsageTelemetryReportPageModelBuilders.BuildOverview(overview);
        var sb = new StringBuilder(24 * 1024);
        UsageTelemetryReportPageShellHtmlRenderer.AppendOverviewHeader(sb, page);

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
}
