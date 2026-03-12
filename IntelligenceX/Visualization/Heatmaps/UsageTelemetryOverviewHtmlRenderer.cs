using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using IntelligenceX.Telemetry.Usage;

namespace IntelligenceX.Visualization.Heatmaps;

#pragma warning disable CS1591

/// <summary>
/// Renders a bundled HTML report for telemetry usage overviews.
/// </summary>
public static class UsageTelemetryOverviewHtmlRenderer {
    public static string Render(UsageTelemetryOverviewDocument overview) {
        if (overview is null) {
            throw new ArgumentNullException(nameof(overview));
        }

        var sb = new StringBuilder(24 * 1024);
        sb.AppendLine("<!doctype html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("  <meta charset=\"utf-8\">");
        sb.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.Append("  <title>").Append(Html(overview.Title)).AppendLine("</title>");
        sb.AppendLine("  <style>");
        sb.AppendLine("    :root { --bg:#f2f2f2; --panel:#ffffff; --ink:#152038; --muted:#787878; --line:#e5e5e5; --soft:#ececec; }");
        sb.AppendLine("    * { box-sizing:border-box; }");
        sb.AppendLine("    body { margin:0; background:var(--bg); color:var(--ink); font-family:\"Aptos\",\"IBM Plex Sans\",\"Segoe UI\",sans-serif; }");
        sb.AppendLine("    .page { max-width:1460px; margin:0 auto; padding:36px 38px 48px; }");
        sb.AppendLine("    .hero { display:flex; justify-content:space-between; gap:24px; align-items:flex-end; margin-bottom:28px; }");
        sb.AppendLine("    .hero h1 { margin:0; font-size:34px; line-height:1; letter-spacing:-.03em; }");
        sb.AppendLine("    .hero p { margin:10px 0 0; color:var(--muted); max-width:70ch; font-size:14px; }");
        sb.AppendLine("    .hero-meta { display:grid; grid-template-columns:repeat(3,minmax(120px,1fr)); gap:18px; min-width:420px; }");
        sb.AppendLine("    .hero-stat { text-align:right; }");
        sb.AppendLine("    .hero-label, .mini-label, .metric-label, .legend-copy { color:var(--muted); font-size:12px; font-weight:700; letter-spacing:.08em; text-transform:uppercase; }");
        sb.AppendLine("    .hero-value { margin-top:4px; font-size:20px; font-weight:800; }");
        sb.AppendLine("    .provider-section { padding:6px 0 42px; border-top:1px solid transparent; }");
        sb.AppendLine("    .provider-header { display:flex; justify-content:space-between; gap:24px; align-items:flex-start; margin-bottom:18px; }");
        sb.AppendLine("    .provider-title { margin:0; font-size:40px; line-height:1; letter-spacing:-.04em; }");
        sb.AppendLine("    .provider-subtitle { margin-top:8px; color:var(--muted); font-size:14px; }");
        sb.AppendLine("    .provider-metrics { display:grid; grid-template-columns:repeat(3,minmax(120px,1fr)); gap:22px; min-width:420px; }");
        sb.AppendLine("    .provider-metric { text-align:right; }");
        sb.AppendLine("    .provider-metric .metric-value { margin-top:4px; font-size:26px; line-height:1; font-weight:800; letter-spacing:-.03em; }");
        sb.AppendLine("    .provider-heatmap { margin:0; }");
        sb.AppendLine("    .provider-heatmap img { width:100%; height:auto; display:block; }");
        sb.AppendLine("    .provider-note { margin:12px 0 0 72px; color:var(--muted); font-size:14px; }");
        sb.AppendLine("    .provider-legend { display:flex; align-items:center; gap:10px; margin:18px 0 0 48px; }");
        sb.AppendLine("    .legend-swatch { width:20px; height:20px; border-radius:6px; display:inline-block; background:var(--soft); }");
        sb.AppendLine("    .provider-footer { display:grid; grid-template-columns:repeat(4,minmax(180px,1fr)); gap:28px; margin-top:34px; }");
        sb.AppendLine("    .mini-card { min-height:72px; }");
        sb.AppendLine("    .mini-value { margin-top:6px; font-size:24px; line-height:1.15; font-weight:800; letter-spacing:-.03em; }");
        sb.AppendLine("    .mini-value span { color:var(--muted); font-weight:500; }");
        sb.AppendLine("    .divider { height:1px; background:var(--line); margin:8px 0 30px; }");
        sb.AppendLine("    .supporting { margin-top:24px; padding-top:22px; border-top:1px solid var(--line); }");
        sb.AppendLine("    .supporting h2 { margin:0 0 8px; font-size:18px; letter-spacing:-.02em; }");
        sb.AppendLine("    .supporting p { margin:0 0 18px; color:var(--muted); font-size:14px; }");
        sb.AppendLine("    .supporting-grid { display:grid; grid-template-columns:repeat(auto-fit,minmax(320px,1fr)); gap:18px; }");
        sb.AppendLine("    .supporting-card { background:var(--panel); border:1px solid var(--line); border-radius:18px; padding:16px; }");
        sb.AppendLine("    .supporting-card h3 { margin:0 0 8px; font-size:16px; letter-spacing:-.02em; }");
        sb.AppendLine("    .supporting-card p { margin:0 0 12px; color:var(--muted); font-size:13px; }");
        sb.AppendLine("    .supporting-card img { width:100%; display:block; border-radius:14px; background:var(--bg); }");
        sb.AppendLine("    .footnote { margin-top:24px; color:var(--muted); font-size:13px; }");
        sb.AppendLine("    @media (max-width: 1080px) { .hero, .provider-header { flex-direction:column; align-items:flex-start; } .hero-meta, .provider-metrics { min-width:0; width:100%; } .hero-stat, .provider-metric { text-align:left; } .provider-footer { grid-template-columns:repeat(2,minmax(180px,1fr)); } .provider-note, .provider-legend { margin-left:0; } }");
        sb.AppendLine("    @media (max-width: 680px) { .page { padding:22px 18px 32px; } .hero-meta, .provider-metrics, .provider-footer { grid-template-columns:1fr; gap:14px; } .provider-title { font-size:32px; } }");
        sb.AppendLine("  </style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("  <main class=\"page\">");
        AppendHero(sb, overview);

        foreach (var providerSection in overview.ProviderSections) {
            AppendProviderSection(sb, providerSection);
        }

        if (overview.Heatmaps.Count > 0) {
            sb.AppendLine("    <section class=\"supporting\">");
            sb.AppendLine("      <h2>Supporting Breakdowns</h2>");
            sb.AppendLine("      <p>These provider-neutral overlays still ride on the same telemetry ledger, so we can compare surfaces, accounts, people, and models when needed.</p>");
            sb.AppendLine("      <div class=\"supporting-grid\">");
            foreach (var heatmap in overview.Heatmaps) {
                sb.AppendLine("        <article class=\"supporting-card\">");
                sb.Append("          <h3>").Append(Html(heatmap.Label)).AppendLine("</h3>");
                if (!string.IsNullOrWhiteSpace(heatmap.Document.Subtitle)) {
                    sb.Append("          <p>").Append(Html(heatmap.Document.Subtitle!)).AppendLine("</p>");
                }
                sb.Append("          <img src=\"").Append(Html(heatmap.Key)).Append(".svg\" alt=\"").Append(Html(heatmap.Label)).AppendLine("\">");
                sb.AppendLine("        </article>");
            }
            sb.AppendLine("      </div>");
            sb.AppendLine("    </section>");
        }

        sb.AppendLine("    <div class=\"footnote\">Built from the provider-neutral telemetry ledger, so the same report format can work for Codex, Claude, IX-native usage, and future compatible providers.</div>");
        sb.AppendLine("  </main>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");
        return sb.ToString();
    }

    private static void AppendHero(StringBuilder sb, UsageTelemetryOverviewDocument overview) {
        sb.AppendLine("    <section class=\"hero\">");
        sb.AppendLine("      <div>");
        sb.Append("        <h1>").Append(Html(overview.Title)).AppendLine("</h1>");
        if (!string.IsNullOrWhiteSpace(overview.Subtitle)) {
            sb.Append("        <p>").Append(Html(overview.Subtitle!)).AppendLine("</p>");
        }
        sb.AppendLine("      </div>");
        sb.AppendLine("      <div class=\"hero-meta\">");
        AppendHeroStat(sb, "Range", FormatRange(overview.Summary.StartDayUtc, overview.Summary.EndDayUtc));
        AppendHeroStat(sb, "Providers", overview.ProviderSections.Count.ToString(CultureInfo.InvariantCulture));
        AppendHeroStat(sb, "Total Tokens", FormatCompact(overview.Summary.TotalValue));
        sb.AppendLine("      </div>");
        sb.AppendLine("    </section>");
        sb.AppendLine("    <div class=\"divider\"></div>");
    }

    private static void AppendHeroStat(StringBuilder sb, string label, string value) {
        sb.AppendLine("        <div class=\"hero-stat\">");
        sb.Append("          <div class=\"hero-label\">").Append(Html(label.ToUpperInvariant())).AppendLine("</div>");
        sb.Append("          <div class=\"hero-value\">").Append(Html(value)).AppendLine("</div>");
        sb.AppendLine("        </div>");
    }

    private static void AppendProviderSection(StringBuilder sb, UsageTelemetryOverviewProviderSection section) {
        sb.AppendLine("    <section class=\"provider-section\">");
        sb.AppendLine("      <div class=\"provider-header\">");
        sb.AppendLine("        <div>");
        sb.Append("          <h2 class=\"provider-title\">").Append(Html(section.Title)).AppendLine("</h2>");
        sb.Append("          <div class=\"provider-subtitle\">").Append(Html(section.Subtitle)).AppendLine("</div>");
        sb.AppendLine("        </div>");
        sb.AppendLine("        <div class=\"provider-metrics\">");
        AppendProviderMetric(sb, "Input Tokens", FormatCompact(section.InputTokens));
        AppendProviderMetric(sb, "Output Tokens", FormatCompact(section.OutputTokens));
        AppendProviderMetric(sb, "Total Tokens", FormatCompact(section.TotalTokens));
        sb.AppendLine("        </div>");
        sb.AppendLine("      </div>");
        sb.AppendLine("      <figure class=\"provider-heatmap\">");
        sb.Append("        <img src=\"").Append(Html(section.Key)).Append(".svg\" alt=\"").Append(Html(section.Title)).AppendLine(" usage heatmap\">");
        sb.AppendLine("      </figure>");
        if (!string.IsNullOrWhiteSpace(section.Note)) {
            sb.Append("      <div class=\"provider-note\">").Append(Html(section.Note!)).AppendLine("</div>");
        }
        AppendProviderLegend(sb, section.ProviderId);
        sb.AppendLine("      <div class=\"provider-footer\">");
        AppendMiniCard(sb, "Most Used Model", section.MostUsedModel);
        AppendMiniCard(sb, "Recent Use (Last 30 Days)", section.RecentModel);
        AppendMiniMetricCard(sb, "Longest Streak", section.LongestStreakDays + " days");
        AppendMiniMetricCard(sb, "Current Streak", section.CurrentStreakDays + " days");
        sb.AppendLine("      </div>");
        sb.AppendLine("    </section>");
    }

    private static void AppendProviderMetric(StringBuilder sb, string label, string value) {
        sb.AppendLine("          <div class=\"provider-metric\">");
        sb.Append("            <div class=\"metric-label\">").Append(Html(label.ToUpperInvariant())).AppendLine("</div>");
        sb.Append("            <div class=\"metric-value\">").Append(Html(value)).AppendLine("</div>");
        sb.AppendLine("          </div>");
    }

    private static void AppendProviderLegend(StringBuilder sb, string providerId) {
        var palette = ResolveLegendColors(providerId);
        sb.AppendLine("      <div class=\"provider-legend\">");
        sb.AppendLine("        <span class=\"legend-copy\">Less</span>");
        foreach (var color in palette) {
            sb.Append("        <span class=\"legend-swatch\" style=\"background:").Append(Html(color)).AppendLine("\"></span>");
        }
        sb.AppendLine("        <span class=\"legend-copy\">More</span>");
        sb.AppendLine("      </div>");
    }

    private static void AppendMiniCard(StringBuilder sb, string label, UsageTelemetryOverviewModelHighlight? highlight) {
        sb.AppendLine("        <article class=\"mini-card\">");
        sb.Append("          <div class=\"mini-label\">").Append(Html(label.ToUpperInvariant())).AppendLine("</div>");
        if (highlight is null) {
            sb.AppendLine("          <div class=\"mini-value\">n/a</div>");
        } else {
            sb.Append("          <div class=\"mini-value\">").Append(Html(highlight.Model)).Append(" <span>(").Append(Html(FormatCompact(highlight.TotalTokens))).AppendLine(")</span></div>");
        }
        sb.AppendLine("        </article>");
    }

    private static void AppendMiniMetricCard(StringBuilder sb, string label, string value) {
        sb.AppendLine("        <article class=\"mini-card\">");
        sb.Append("          <div class=\"mini-label\">").Append(Html(label.ToUpperInvariant())).AppendLine("</div>");
        sb.Append("          <div class=\"mini-value\">").Append(Html(value)).AppendLine("</div>");
        sb.AppendLine("        </article>");
    }

    private static string[] ResolveLegendColors(string providerId) {
        return providerId.Trim().ToLowerInvariant() switch {
            "claude" => new[] { "#e8e8e8", "#f5d8b0", "#f3ba73", "#fb8c1d", "#c65102" },
            "codex" => new[] { "#e8e8e8", "#cfd6ff", "#98a8ff", "#6268f1", "#2f2a93" },
            _ => new[] { "#e8e8e8", "#d6ecd3", "#9be9a8", "#40c463", "#216e39" }
        };
    }

    private static string FormatRange(DateTime? startDayUtc, DateTime? endDayUtc) {
        if (!startDayUtc.HasValue || !endDayUtc.HasValue) {
            return "n/a";
        }

        return startDayUtc.Value.ToString("yyyy-MM-dd") + " to " + endDayUtc.Value.ToString("yyyy-MM-dd");
    }

    private static string FormatCompact(decimal value) {
        if (value <= 0m) {
            return "0";
        }

        return FormatCompact((double)value);
    }

    private static string FormatCompact(long value) {
        if (value <= 0L) {
            return "0";
        }

        return FormatCompact((double)value);
    }

    private static string FormatCompact(double value) {
        if (value >= 1_000_000_000d) {
            return (value / 1_000_000_000d).ToString(value >= 10_000_000_000d ? "0.#" : "0.##", CultureInfo.InvariantCulture) + "B";
        }
        if (value >= 1_000_000d) {
            return (value / 1_000_000d).ToString(value >= 10_000_000d ? "0.#" : "0.##", CultureInfo.InvariantCulture) + "M";
        }
        if (value >= 1_000d) {
            return (value / 1_000d).ToString(value >= 10_000d ? "0.#" : "0.##", CultureInfo.InvariantCulture) + "K";
        }
        return value.ToString("0", CultureInfo.InvariantCulture);
    }

    private static string Html(string value) {
        return WebUtility.HtmlEncode(value ?? string.Empty);
    }
}
