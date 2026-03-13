using System.Net;
using System.Text;

namespace IntelligenceX.Visualization.Heatmaps;

internal static class UsageTelemetrySupportingBreakdownHtmlRenderer {
    public static void AppendSection(StringBuilder sb, IReadOnlyList<UsageTelemetrySupportingBreakdownModel> heatmaps) {
        if (heatmaps.Count == 0) {
            return;
        }

        sb.AppendLine("    <section class=\"supporting\">");
        sb.AppendLine("      <h2>Supporting Breakdowns</h2>");
        sb.AppendLine("      <p>These cross-section overlays still ride on the same telemetry ledger, so they help compare telemetry-backed sections like Codex and Claude across surfaces, source roots, accounts, people, and models.</p>");
        sb.AppendLine("      <div class=\"supporting-tabs\" role=\"tablist\" aria-label=\"Supporting breakdowns\">");
        foreach (var heatmap in heatmaps) {
            sb.Append("        <button type=\"button\" class=\"supporting-tab");
            if (heatmap.IsDefault) {
                sb.Append(" active");
            }

            sb.Append("\" data-target=\"").Append(Html(heatmap.Key)).Append("\" role=\"tab\" aria-selected=\"")
                .Append(heatmap.IsDefault ? "true" : "false")
                .Append("\">")
                .Append(Html(heatmap.Label))
                .AppendLine("</button>");
        }

        sb.AppendLine("      </div>");
        sb.AppendLine("      <div class=\"supporting-viewer\">");
        sb.AppendLine("        <div class=\"supporting-toolbar\">");
        sb.AppendLine("          <div class=\"supporting-modes\" role=\"tablist\" aria-label=\"Breakdown display mode\">");
        sb.AppendLine("            <button type=\"button\" class=\"supporting-mode active\" data-mode=\"preview\" role=\"tab\" aria-selected=\"true\">Preview</button>");
        sb.AppendLine("            <button type=\"button\" class=\"supporting-mode\" data-mode=\"summary\" role=\"tab\" aria-selected=\"false\">Summary</button>");
        sb.AppendLine("          </div>");
        sb.AppendLine("        </div>");

        foreach (var heatmap in heatmaps) {
            AppendPanel(sb, heatmap);
        }

        sb.AppendLine("      </div>");
        sb.AppendLine("    </section>");
    }

    private static void AppendPanel(StringBuilder sb, UsageTelemetrySupportingBreakdownModel heatmap) {
        sb.Append("        <section class=\"supporting-panel");
        if (heatmap.IsDefault) {
            sb.Append(" active");
        }

        sb.Append("\" id=\"panel-").Append(Html(heatmap.Key)).Append("\" data-key=\"").Append(Html(heatmap.Key)).Append("\" role=\"tabpanel\">").AppendLine();
        sb.AppendLine("          <div class=\"supporting-header\">");
        sb.AppendLine("            <div>");
        sb.Append("              <h3 class=\"supporting-title\">").Append(Html(heatmap.Label)).AppendLine("</h3>");
        if (!string.IsNullOrWhiteSpace(heatmap.Subtitle)) {
            sb.Append("              <p class=\"supporting-copy\">").Append(Html(heatmap.Subtitle!)).AppendLine("</p>");
        }

        sb.AppendLine("            </div>");
        sb.AppendLine("            <div class=\"supporting-links\">");
        sb.Append("              <a class=\"supporting-link\" href=\"").Append(Html(heatmap.Key)).Append(".html\">Open detail</a>").AppendLine();
        sb.Append("              <a class=\"supporting-link\" data-light-href=\"").Append(Html(heatmap.Key)).Append(".light.svg\" data-dark-href=\"").Append(Html(heatmap.Key)).Append(".dark.svg\" href=\"").Append(Html(heatmap.Key)).Append(".light.svg\" target=\"_blank\" rel=\"noopener\">Open SVG</a>").AppendLine();
        sb.Append("              <a class=\"supporting-link\" href=\"").Append(Html(heatmap.Key)).Append(".json\" target=\"_blank\" rel=\"noopener\">Open JSON</a>").AppendLine();
        sb.AppendLine("            </div>");
        sb.AppendLine("          </div>");
        sb.AppendLine("          <div class=\"supporting-preview\">");
        sb.Append("            <img src=\"").Append(Html(heatmap.Key)).Append(".light.svg\" data-light-src=\"").Append(Html(heatmap.Key)).Append(".light.svg\" data-dark-src=\"").Append(Html(heatmap.Key)).Append(".dark.svg\" alt=\"").Append(Html(heatmap.Label)).AppendLine("\">");
        sb.AppendLine("          </div>");
        sb.AppendLine("          <div class=\"supporting-summary\">");
        UsageTelemetryBreakdownSummaryHtmlRenderer.AppendSummary(sb, heatmap.Summary, baseIndentLevel: 6);
        sb.AppendLine("          </div>");
        sb.AppendLine("        </section>");
    }

    private static string Html(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
