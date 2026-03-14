using System.Net;
using System.Text;
using System.Collections.Generic;
using System.Linq;

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
        var guide = ResolveBreakdownGuide(heatmap.Key);

        sb.Append("        <section class=\"supporting-panel");
        if (heatmap.IsDefault) {
            sb.Append(" active");
        }

        sb.Append("\" id=\"panel-").Append(Html(heatmap.Key)).Append("\" data-key=\"").Append(Html(heatmap.Key)).Append("\" role=\"tabpanel\">").AppendLine();
        sb.AppendLine("          <div class=\"supporting-header\">");
        sb.AppendLine("            <div class=\"supporting-header-copy\">");
        sb.Append("              <h3 class=\"supporting-title\">").Append(Html(heatmap.Label)).AppendLine("</h3>");
        if (!string.IsNullOrWhiteSpace(guide)) {
            sb.Append("              <p class=\"supporting-guide\">").Append(Html(guide)).AppendLine("</p>");
        }
        if (!string.IsNullOrWhiteSpace(heatmap.Subtitle)) {
            sb.Append("              <p class=\"supporting-copy\">").Append(Html(heatmap.Subtitle!)).AppendLine("</p>");
        }
        AppendSourceFamilyChips(sb, heatmap);

        sb.AppendLine("            </div>");
        sb.AppendLine("            <div class=\"supporting-links\">");
        sb.Append("              <a class=\"supporting-link\" href=\"").Append(Html(heatmap.FileStem)).Append(".html\">Breakdown page</a>").AppendLine();
        sb.Append("              <a class=\"supporting-link\" data-light-href=\"").Append(Html(heatmap.FileStem)).Append(".light.svg\" data-dark-href=\"").Append(Html(heatmap.FileStem)).Append(".dark.svg\" href=\"").Append(Html(heatmap.FileStem)).Append(".light.svg\" target=\"_blank\" rel=\"noopener\">Chart SVG</a>").AppendLine();
        sb.Append("              <a class=\"supporting-link\" href=\"").Append(Html(heatmap.FileStem)).Append(".json\" target=\"_blank\" rel=\"noopener\">Data JSON</a>").AppendLine();
        sb.AppendLine("            </div>");
        sb.AppendLine("          </div>");
        sb.AppendLine("          <div class=\"supporting-preview\">");
        sb.Append("            <img src=\"").Append(Html(heatmap.FileStem)).Append(".light.svg\" data-light-src=\"").Append(Html(heatmap.FileStem)).Append(".light.svg\" data-dark-src=\"").Append(Html(heatmap.FileStem)).Append(".dark.svg\" alt=\"").Append(Html(heatmap.Label)).AppendLine("\">");
        sb.AppendLine("          </div>");
        sb.AppendLine("          <div class=\"supporting-summary\">");
        UsageTelemetryBreakdownSummaryHtmlRenderer.AppendSummary(sb, heatmap.Summary, baseIndentLevel: 6);
        sb.AppendLine("          </div>");
        sb.AppendLine("        </section>");
    }

    private static void AppendSourceFamilyChips(StringBuilder sb, UsageTelemetrySupportingBreakdownModel heatmap) {
        if (!heatmap.Summary.IsSourceRoot || heatmap.Summary.SecondaryRows.Count == 0) {
            return;
        }

        var chips = heatmap.Summary.SecondaryRows
            .Select(static row => row.Label)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(label => UsageTelemetrySourceFamilyBadges.TryResolve(label, out var tone, out var text)
                ? new SourceFamilyChip(tone, text)
                : null)
            .Where(static chip => chip is not null)
            .Cast<SourceFamilyChip>()
            .ToArray();

        if (chips.Length == 0) {
            return;
        }

        sb.AppendLine("              <div class=\"supporting-family-chips\" aria-label=\"Source families in this breakdown\">");
        foreach (var chip in chips) {
            sb.Append("                <span class=\"summary-row-badge supporting-family-chip ")
                .Append(Html(chip.Tone))
                .Append("\">")
                .Append(Html(chip.Text))
                .AppendLine("</span>");
        }
        sb.AppendLine("              </div>");
    }

    private sealed record SourceFamilyChip(string Tone, string Text);

    private static string? ResolveBreakdownGuide(string? breakdownKey) {
        var normalized = (breakdownKey ?? string.Empty).Trim().ToLowerInvariant();
        if (normalized.Length == 0) {
            return null;
        }

        return normalized switch {
            "surface" => "Compare where work happened across CLI sessions, chat flows, reviewer runs, and other recorded surfaces.",
            "provider" => "Compare usage across telemetry sources such as Codex, Claude, LM Studio, and future compatible providers.",
            "model" => "Spot which models dominated the selected window across all imported providers.",
            "sourceroot" => "Trace activity back to current machines, recovered Windows.old profiles, WSL homes, and imported source roots.",
            "account" => "Compare normalized provider accounts after account bindings and alias cleanup are applied.",
            "person" => "Roll up multiple accounts into shared people labels for cross-account reporting.",
            _ => null
        };
    }

    private static string Html(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
