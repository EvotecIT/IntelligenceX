namespace IntelligenceX.Visualization.Heatmaps;

#pragma warning disable CS1591

internal static class UsageTelemetryBreakdownHtmlRenderer {
    public static string Render(string reportTitle, string breakdownKey, string breakdownLabel, string? subtitle, HeatmapDocument document) {
        var page = UsageTelemetryReportPageModelBuilders.BuildBreakdown(reportTitle, breakdownKey, breakdownLabel, subtitle, document);
        return UsageTelemetryReportStaticAssets.RenderPage(
            "breakdown.html",
            page.ReportTitle + " · " + page.BreakdownLabel,
            UsageTelemetryBreakdownPageHtmlRenderer.RenderBody(page),
            page.BootstrapJson);
    }
}
