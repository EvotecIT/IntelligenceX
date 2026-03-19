using IntelligenceX.Json;
using IntelligenceX.Telemetry.Usage;

namespace IntelligenceX.Visualization.Heatmaps;

#pragma warning disable CS1591

internal static class UsageTelemetryBreakdownHtmlRenderer {
    public static string Render(
        string reportTitle,
        string breakdownKey,
        string breakdownLabel,
        string? subtitle,
        HeatmapDocument document,
        UsageSummarySnapshot? summary = null,
        JsonObject? metadata = null,
        int providerSectionsCount = 0) {
        var page = UsageTelemetryReportPageModelBuilders.BuildBreakdown(
            reportTitle,
            breakdownKey,
            breakdownLabel,
            subtitle,
            document,
            summary,
            metadata,
            providerSectionsCount);
        return UsageTelemetryReportStaticAssets.RenderPage(
            "breakdown.html",
            page.ReportTitle + " · " + page.BreakdownLabel,
            UsageTelemetryBreakdownPageHtmlRenderer.RenderBody(page),
            page.BootstrapJson);
    }
}
