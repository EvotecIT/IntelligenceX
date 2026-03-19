using System.Net;
using System.Text;

namespace IntelligenceX.Visualization.Heatmaps;

internal static class UsageTelemetryBreakdownPageHtmlRenderer {
    public static string RenderBody(UsageTelemetryBreakdownPageModel page) {
        var sb = new StringBuilder(16 * 1024);
        UsageTelemetryReportPageShellHtmlRenderer.AppendBreakdownHeader(sb, page);
        UsageTelemetryReportDiagnosticsHtmlRenderer.Append(sb, page.Diagnostics);
        sb.AppendLine("    <section class=\"panel\">");
        sb.AppendLine("      <div class=\"panel-toolbar\">");
        sb.AppendLine("        <div class=\"mode-switcher\" role=\"tablist\" aria-label=\"Breakdown display mode\">");
        sb.AppendLine("          <button type=\"button\" class=\"mode-button active\" data-mode=\"preview\" role=\"tab\" aria-selected=\"true\">Preview</button>");
        sb.AppendLine("          <button type=\"button\" class=\"mode-button\" data-mode=\"summary\" role=\"tab\" aria-selected=\"false\">Summary</button>");
        sb.AppendLine("        </div>");
        sb.AppendLine("      </div>");
        sb.AppendLine("      <div class=\"preview\">");
        sb.Append("        <img src=\"").Append(Html(page.FileStem)).Append(".light.svg\" data-light-src=\"").Append(Html(page.FileStem)).Append(".light.svg\" data-dark-src=\"").Append(Html(page.FileStem)).Append(".dark.svg\" alt=\"").Append(Html(page.BreakdownLabel)).AppendLine(" heatmap\">");
        sb.AppendLine("      </div>");
        UsageTelemetryBreakdownSummaryHtmlRenderer.AppendSummary(sb, page.Summary, baseIndentLevel: 3);
        sb.AppendLine("    </section>");
        return sb.ToString();
    }

    private static string Html(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
