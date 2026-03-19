using System.Text;

namespace IntelligenceX.Visualization.Heatmaps;

internal static class UsageTelemetryReportDiagnosticsHtmlRenderer {
    public static void Append(StringBuilder sb, UsageTelemetryReportDiagnosticsModel? diagnostics, int indentLevel = 2) {
        if (sb is null || diagnostics is null || diagnostics.Items.Count == 0) {
            return;
        }

        var indent = new string(' ', indentLevel * 2);
        if (!string.IsNullOrWhiteSpace(diagnostics.Title)) {
            sb.Append(indent).Append("  <div class=\"diagnostics-title\">")
                .Append(UsageTelemetryOverviewHtmlFragments.Html(diagnostics.Title))
                .AppendLine("</div>");
        }
        sb.Append(indent).AppendLine("<section class=\"diagnostics-strip\" aria-label=\"Data health\">");
        foreach (var item in diagnostics.Items) {
            sb.Append(indent).AppendLine("  <article class=\"diagnostics-card\">");
            sb.Append(indent).Append("    <div class=\"diagnostics-kicker\">")
                .Append(UsageTelemetryOverviewHtmlFragments.Html(item.Label))
                .AppendLine("</div>");
            sb.Append(indent).Append("    <div class=\"diagnostics-value\">")
                .Append(UsageTelemetryOverviewHtmlFragments.Html(item.Value))
                .AppendLine("</div>");
            if (!string.IsNullOrWhiteSpace(item.Copy)) {
                var itemCopy = item.Copy!;
                sb.Append(indent).Append("    <div class=\"diagnostics-copy\">")
                    .Append(UsageTelemetryOverviewHtmlFragments.Html(itemCopy))
                    .AppendLine("</div>");
            }
            sb.Append(indent).AppendLine("  </article>");
        }
        sb.Append(indent).AppendLine("</section>");
        if (!string.IsNullOrWhiteSpace(diagnostics.Note)) {
            var diagnosticsNote = diagnostics.Note!;
            sb.Append(indent).Append("  <div class=\"diagnostics-note\">")
                .Append(UsageTelemetryOverviewHtmlFragments.Html(diagnosticsNote))
                .AppendLine("</div>");
        }
    }
}
