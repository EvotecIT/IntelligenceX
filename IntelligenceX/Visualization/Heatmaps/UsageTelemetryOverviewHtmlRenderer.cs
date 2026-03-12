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
        sb.AppendLine("    .provider-shell { background:linear-gradient(180deg,#ffffff 0%, #fbfbfb 100%); border:1px solid var(--line); border-radius:26px; padding:28px 28px 30px; box-shadow:0 12px 34px rgba(18,24,38,.04); }");
        sb.AppendLine("    .provider-header { display:flex; justify-content:space-between; gap:24px; align-items:flex-start; margin-bottom:18px; }");
        sb.AppendLine("    .provider-title { margin:0; font-size:40px; line-height:1; letter-spacing:-.04em; }");
        sb.AppendLine("    .provider-subtitle { margin-top:8px; color:var(--muted); font-size:14px; }");
        sb.AppendLine("    .provider-metrics { display:grid; grid-template-columns:repeat(3,minmax(120px,1fr)); gap:22px; min-width:420px; }");
        sb.AppendLine("    .provider-metric { text-align:right; background:#f8f8f8; border:1px solid var(--line); border-radius:18px; padding:16px 16px 14px; }");
        sb.AppendLine("    .provider-metric .metric-value { margin-top:4px; font-size:26px; line-height:1; font-weight:800; letter-spacing:-.03em; }");
        sb.AppendLine("    .provider-metric .metric-copy { margin-top:8px; color:var(--muted); font-size:12px; }");
        sb.AppendLine("    .metric-bar { margin-top:12px; height:10px; width:100%; background:#e7e7e7; border-radius:999px; overflow:hidden; }");
        sb.AppendLine("    .metric-fill { height:100%; border-radius:999px; min-width:10px; }");
        sb.AppendLine("    .provider-token-mix { margin:18px 0 18px; padding:16px 18px; border-radius:20px; background:#f8f8f8; border:1px solid var(--line); }");
        sb.AppendLine("    .provider-token-mix-header { display:flex; justify-content:space-between; gap:16px; align-items:baseline; margin-bottom:12px; }");
        sb.AppendLine("    .provider-token-mix-title { font-size:12px; font-weight:700; letter-spacing:.08em; text-transform:uppercase; color:var(--muted); }");
        sb.AppendLine("    .provider-token-mix-copy { font-size:13px; color:var(--muted); }");
        sb.AppendLine("    .provider-token-mix-bar { display:flex; width:100%; height:14px; border-radius:999px; overflow:hidden; background:#ececec; }");
        sb.AppendLine("    .provider-token-segment { min-width:2px; }");
        sb.AppendLine("    .provider-token-mix-legend { display:flex; flex-wrap:wrap; gap:14px; margin-top:12px; }");
        sb.AppendLine("    .provider-token-mix-item { display:flex; align-items:center; gap:8px; color:var(--muted); font-size:12px; }");
        sb.AppendLine("    .provider-token-mix-item strong { color:var(--ink); font-size:13px; }");
        sb.AppendLine("    .provider-token-dot { width:10px; height:10px; border-radius:999px; display:inline-block; }");
        sb.AppendLine("    .provider-monthly { margin:18px 0 18px; padding:16px 18px 14px; border-radius:20px; background:#f8f8f8; border:1px solid var(--line); }");
        sb.AppendLine("    .provider-monthly-header { display:flex; justify-content:space-between; gap:16px; align-items:baseline; margin-bottom:14px; }");
        sb.AppendLine("    .provider-monthly-title { font-size:12px; font-weight:700; letter-spacing:.08em; text-transform:uppercase; color:var(--muted); }");
        sb.AppendLine("    .provider-monthly-copy { font-size:13px; color:var(--muted); }");
        sb.AppendLine("    .provider-monthly-grid { display:grid; grid-template-columns:repeat(auto-fit,minmax(42px,1fr)); gap:10px; align-items:end; min-height:126px; }");
        sb.AppendLine("    .provider-month { display:flex; flex-direction:column; gap:8px; align-items:center; }");
        sb.AppendLine("    .provider-month-bar-wrap { width:100%; height:90px; display:flex; align-items:flex-end; }");
        sb.AppendLine("    .provider-month-bar { width:100%; min-height:4px; border-radius:10px 10px 4px 4px; background:#d7dcff; box-shadow:inset 0 -1px 0 rgba(0,0,0,.08); }");
        sb.AppendLine("    .provider-month-label { font-size:12px; color:var(--muted); }");
        sb.AppendLine("    .provider-month-value { font-size:11px; color:var(--muted); }");
        sb.AppendLine("    .provider-heatmap { margin:0; background:#f6f6f6; border:1px solid var(--line); border-radius:24px; padding:14px; }");
        sb.AppendLine("    .provider-heatmap img { width:100%; height:auto; display:block; }");
        sb.AppendLine("    .provider-note { margin:14px 0 0; color:var(--muted); font-size:14px; }");
        sb.AppendLine("    .provider-legend { display:flex; align-items:center; gap:10px; margin:18px 0 0; }");
        sb.AppendLine("    .legend-swatch { width:20px; height:20px; border-radius:6px; display:inline-block; background:var(--soft); }");
        sb.AppendLine("    .provider-footer { display:grid; grid-template-columns:repeat(4,minmax(180px,1fr)); gap:28px; margin-top:34px; }");
        sb.AppendLine("    .mini-card { min-height:72px; padding:14px 16px; border:1px solid var(--line); border-radius:18px; background:#fafafa; }");
        sb.AppendLine("    .mini-value { margin-top:6px; font-size:24px; line-height:1.15; font-weight:800; letter-spacing:-.03em; }");
        sb.AppendLine("    .mini-value span { color:var(--muted); font-weight:500; }");
        sb.AppendLine("    .provider-insights { display:grid; grid-template-columns:1.15fr .85fr; gap:22px; margin-top:24px; }");
        sb.AppendLine("    .insight-card { min-height:160px; padding:18px 18px 16px; border:1px solid var(--line); border-radius:20px; background:#fafafa; }");
        sb.AppendLine("    .insight-title { color:var(--muted); font-size:12px; font-weight:700; letter-spacing:.08em; text-transform:uppercase; margin-bottom:12px; }");
        sb.AppendLine("    .rank-list { display:grid; gap:10px; }");
        sb.AppendLine("    .rank-row { display:grid; grid-template-columns:32px 1fr auto; gap:12px; align-items:baseline; }");
        sb.AppendLine("    .rank-index { color:var(--muted); font-size:15px; font-weight:700; }");
        sb.AppendLine("    .rank-label { font-size:18px; font-weight:700; letter-spacing:-.02em; }");
        sb.AppendLine("    .rank-value { color:var(--muted); font-size:16px; white-space:nowrap; }");
        sb.AppendLine("    .estimate-total { display:flex; justify-content:space-between; gap:18px; align-items:flex-end; margin-bottom:12px; }");
        sb.AppendLine("    .estimate-value { font-size:34px; font-weight:800; letter-spacing:-.04em; line-height:1; }");
        sb.AppendLine("    .estimate-copy { color:var(--muted); font-size:13px; max-width:28ch; text-align:right; }");
        sb.AppendLine("    .estimate-note { color:var(--muted); font-size:13px; margin-top:10px; }");
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
        sb.AppendLine("    @media (max-width: 1080px) { .hero, .provider-header, .provider-token-mix-header { flex-direction:column; align-items:flex-start; } .hero-meta, .provider-metrics { min-width:0; width:100%; } .hero-stat, .provider-metric { text-align:left; } .provider-footer { grid-template-columns:repeat(2,minmax(180px,1fr)); } .provider-insights { grid-template-columns:1fr; } .provider-note, .provider-legend { margin-left:0; } }");
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
        var accentColors = ResolveProviderAccentColors(section.ProviderId);
        sb.AppendLine("    <section class=\"provider-section\">");
        sb.AppendLine("      <div class=\"provider-shell\">");
        sb.AppendLine("      <div class=\"provider-header\">");
        sb.AppendLine("        <div>");
        sb.Append("          <h2 class=\"provider-title\">").Append(Html(section.Title)).AppendLine("</h2>");
        sb.Append("          <div class=\"provider-subtitle\">").Append(Html(section.Subtitle)).AppendLine("</div>");
        sb.AppendLine("        </div>");
        sb.AppendLine("        <div class=\"provider-metrics\">");
        AppendProviderMetric(sb, "Input Tokens", FormatCompact(section.InputTokens), section.InputTokens, section.TotalTokens, accentColors.Input);
        AppendProviderMetric(sb, "Output Tokens", FormatCompact(section.OutputTokens), section.OutputTokens, section.TotalTokens, accentColors.Output);
        AppendProviderMetric(sb, "Total Tokens", FormatCompact(section.TotalTokens), section.TotalTokens, section.TotalTokens, accentColors.Total);
        sb.AppendLine("        </div>");
        sb.AppendLine("      </div>");
        AppendProviderTokenMix(sb, section, accentColors);
        AppendProviderMonthlyUsage(sb, section, accentColors.Total);
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
        AppendProviderInsights(sb, section);
        sb.AppendLine("      </div>");
        sb.AppendLine("    </section>");
    }

    private static void AppendProviderMetric(StringBuilder sb, string label, string value, long metricValue, long totalValue, string fillColor) {
        sb.AppendLine("          <div class=\"provider-metric\">");
        sb.Append("            <div class=\"metric-label\">").Append(Html(label.ToUpperInvariant())).AppendLine("</div>");
        sb.Append("            <div class=\"metric-value\">").Append(Html(value)).AppendLine("</div>");
        sb.Append("            <div class=\"metric-copy\">").Append(Html(FormatPercent(metricValue, totalValue))).AppendLine(" of section total</div>");
        sb.AppendLine("            <div class=\"metric-bar\">");
        sb.Append("              <div class=\"metric-fill\" style=\"width:").Append(Html(FormatPercentValue(metricValue, totalValue))).Append("%; background:").Append(Html(fillColor)).AppendLine(";\"></div>");
        sb.AppendLine("            </div>");
        sb.AppendLine("          </div>");
    }

    private static void AppendProviderTokenMix(StringBuilder sb, UsageTelemetryOverviewProviderSection section, ProviderAccentColors colors) {
        var otherTokens = Math.Max(0L, section.TotalTokens - section.InputTokens - section.OutputTokens);
        sb.AppendLine("      <div class=\"provider-token-mix\">");
        sb.AppendLine("        <div class=\"provider-token-mix-header\">");
        sb.AppendLine("          <div class=\"provider-token-mix-title\">Token mix</div>");
        sb.Append("          <div class=\"provider-token-mix-copy\">").Append(Html(FormatCompact(section.TotalTokens))).AppendLine(" total tokens across this provider section</div>");
        sb.AppendLine("        </div>");
        sb.AppendLine("        <div class=\"provider-token-mix-bar\">");
        AppendProviderTokenSegment(sb, section.InputTokens, section.TotalTokens, colors.Input);
        AppendProviderTokenSegment(sb, section.OutputTokens, section.TotalTokens, colors.Output);
        if (otherTokens > 0) {
            AppendProviderTokenSegment(sb, otherTokens, section.TotalTokens, colors.Other);
        }
        sb.AppendLine("        </div>");
        sb.AppendLine("        <div class=\"provider-token-mix-legend\">");
        AppendProviderTokenMixItem(sb, "Input", section.InputTokens, section.TotalTokens, colors.Input);
        AppendProviderTokenMixItem(sb, "Output", section.OutputTokens, section.TotalTokens, colors.Output);
        if (otherTokens > 0) {
            AppendProviderTokenMixItem(sb, "Other", otherTokens, section.TotalTokens, colors.Other);
        }
        sb.AppendLine("        </div>");
        sb.AppendLine("      </div>");
    }

    private static void AppendProviderTokenSegment(StringBuilder sb, long value, long totalValue, string color) {
        if (value <= 0 || totalValue <= 0) {
            return;
        }

        sb.Append("          <span class=\"provider-token-segment\" style=\"width:")
            .Append(Html(FormatPercentValue(value, totalValue)))
            .Append("%; background:")
            .Append(Html(color))
            .AppendLine(";\"></span>");
    }

    private static void AppendProviderTokenMixItem(StringBuilder sb, string label, long value, long totalValue, string color) {
        sb.Append("          <div class=\"provider-token-mix-item\"><span class=\"provider-token-dot\" style=\"background:")
            .Append(Html(color))
            .Append("\"></span>")
            .Append(Html(label))
            .Append(": <strong>")
            .Append(Html(FormatCompact(value)))
            .Append("</strong> <span>(")
            .Append(Html(FormatPercent(value, totalValue)))
            .AppendLine(")</span></div>");
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

    private static void AppendProviderMonthlyUsage(StringBuilder sb, UsageTelemetryOverviewProviderSection section, string accentColor) {
        var months = section.MonthlyUsage ?? Array.Empty<UsageTelemetryOverviewMonthlyUsage>();
        if (months.Count == 0) {
            return;
        }

        var maxTokens = months.Max(static month => month.TotalTokens);
        sb.AppendLine("      <div class=\"provider-monthly\">");
        sb.AppendLine("        <div class=\"provider-monthly-header\">");
        sb.AppendLine("          <div class=\"provider-monthly-title\">Monthly usage</div>");
        sb.Append("          <div class=\"provider-monthly-copy\">").Append(Html(months.Count.ToString(CultureInfo.InvariantCulture))).AppendLine(" month window</div>");
        sb.AppendLine("        </div>");
        sb.AppendLine("        <div class=\"provider-monthly-grid\">");
        foreach (var month in months) {
            var height = maxTokens <= 0L ? 4d : Math.Max(4d, month.TotalTokens / (double)maxTokens * 90d);
            var alpha = month.TotalTokens <= 0L ? "33" : Math.Max(64, Math.Min(255, (int)Math.Round(month.TotalTokens / (double)Math.Max(1L, maxTokens) * 255d))).ToString("X2", CultureInfo.InvariantCulture);
            var monthColor = month.TotalTokens <= 0L ? "#dfdfdf" : accentColor + alpha;
            var title = $"{month.Key}: {FormatCompact(month.TotalTokens)} tokens";
            if (month.ActiveDays > 0) {
                title += $" across {month.ActiveDays} active day(s)";
            }

            sb.Append("          <div class=\"provider-month\" title=\"").Append(Html(title)).AppendLine("\">");
            sb.AppendLine("            <div class=\"provider-month-bar-wrap\">");
            sb.Append("              <div class=\"provider-month-bar\" style=\"height:")
                .Append(Html(height.ToString("0.##", CultureInfo.InvariantCulture)))
                .Append("px; background:")
                .Append(Html(monthColor))
                .AppendLine(";\"></div>");
            sb.AppendLine("            </div>");
            sb.Append("            <div class=\"provider-month-label\">").Append(Html(month.Label)).AppendLine("</div>");
            sb.Append("            <div class=\"provider-month-value\">").Append(Html(FormatCompact(month.TotalTokens))).AppendLine("</div>");
            sb.AppendLine("          </div>");
        }
        sb.AppendLine("        </div>");
        sb.AppendLine("      </div>");
    }

    private static void AppendProviderInsights(StringBuilder sb, UsageTelemetryOverviewProviderSection section) {
        var hasTopModels = section.TopModels.Count > 0;
        var hasEstimate = section.ApiCostEstimate is not null;
        if (!hasTopModels && !hasEstimate) {
            return;
        }

        sb.AppendLine("      <div class=\"provider-insights\">");
        sb.AppendLine("        <article class=\"insight-card\">");
        sb.AppendLine("          <div class=\"insight-title\">Top models</div>");
        if (hasTopModels) {
            sb.AppendLine("          <div class=\"rank-list\">");
            var rank = 1;
            foreach (var model in section.TopModels) {
                sb.AppendLine("            <div class=\"rank-row\">");
                sb.Append("              <div class=\"rank-index\">").Append(rank.ToString(CultureInfo.InvariantCulture)).AppendLine(".</div>");
                sb.Append("              <div class=\"rank-label\">").Append(Html(model.Model)).AppendLine("</div>");
                sb.Append("              <div class=\"rank-value\">")
                    .Append(Html(FormatCompact(model.TotalTokens)))
                    .Append(" (")
                    .Append(Html(model.SharePercent.ToString("0.#", CultureInfo.InvariantCulture)))
                    .AppendLine("%)</div>");
                sb.AppendLine("            </div>");
                rank++;
            }
            sb.AppendLine("          </div>");
        } else {
            sb.AppendLine("          <div class=\"estimate-note\">No model breakdown available.</div>");
        }
        sb.AppendLine("        </article>");

        sb.AppendLine("        <article class=\"insight-card\">");
        sb.AppendLine("          <div class=\"insight-title\">Estimated API route</div>");
        AppendApiCostEstimate(sb, section.ApiCostEstimate);
        sb.AppendLine("        </article>");
        sb.AppendLine("      </div>");
    }

    private static void AppendApiCostEstimate(StringBuilder sb, UsageTelemetryOverviewApiCostEstimate? estimate) {
        if (estimate is null) {
            sb.AppendLine("          <div class=\"estimate-note\">No model pricing coverage available for this section yet.</div>");
            return;
        }

        sb.AppendLine("          <div class=\"estimate-total\">");
        sb.Append("            <div class=\"estimate-value\">$").Append(Html(FormatCurrencyCompact(estimate.TotalEstimatedCostUsd))).AppendLine("</div>");
        sb.Append("            <div class=\"estimate-copy\">Estimated from exact token telemetry using current public API rates.</div>");
        sb.AppendLine("          </div>");
        if (estimate.TopDrivers.Count > 0) {
            sb.AppendLine("          <div class=\"rank-list\">");
            foreach (var driver in estimate.TopDrivers) {
                sb.AppendLine("            <div class=\"rank-row\">");
                sb.AppendLine("              <div class=\"rank-index\">$</div>");
                sb.Append("              <div class=\"rank-label\">").Append(Html(driver.Model)).AppendLine("</div>");
                sb.Append("              <div class=\"rank-value\">$")
                    .Append(Html(FormatCurrencyCompact(driver.EstimatedCostUsd)))
                    .Append(" (")
                    .Append(Html(driver.SharePercent.ToString("0.#", CultureInfo.InvariantCulture)))
                    .AppendLine("%)</div>");
                sb.AppendLine("            </div>");
            }
            sb.AppendLine("          </div>");
        }

        var totalTokens = estimate.CoveredTokens + estimate.UncoveredTokens;
        var coveredPercent = totalTokens <= 0L ? 0d : estimate.CoveredTokens / (double)totalTokens * 100d;
        sb.Append("          <div class=\"estimate-note\">Priced coverage: ")
            .Append(Html(coveredPercent.ToString("0.#", CultureInfo.InvariantCulture)))
            .Append("% of tokens");
        if (estimate.UncoveredTokens > 0L) {
            sb.Append(" (")
                .Append(Html(FormatCompact(estimate.UncoveredTokens)))
                .Append(" unpriced)");
        }
        sb.AppendLine(".</div>");
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

    private static ProviderAccentColors ResolveProviderAccentColors(string providerId) {
        return providerId.Trim().ToLowerInvariant() switch {
            "claude" => new ProviderAccentColors("#f3ba73", "#fb8c1d", "#c65102", "#e9c89e"),
            "codex" => new ProviderAccentColors("#98a8ff", "#6268f1", "#2f2a93", "#bcc5ff"),
            _ => new ProviderAccentColors("#9be9a8", "#40c463", "#216e39", "#cfe8d2")
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

    private static string FormatCurrencyCompact(decimal value) {
        if (value >= 1000m) {
            return (value / 1000m).ToString(value >= 10000m ? "0.#" : "0.##", CultureInfo.InvariantCulture) + "K";
        }

        return value.ToString(value >= 100m ? "0" : "0.##", CultureInfo.InvariantCulture);
    }

    private static string FormatPercent(long value, long total) {
        if (value <= 0 || total <= 0) {
            return "0%";
        }

        return (Math.Min(1d, value / (double)total) * 100d).ToString("0.#", CultureInfo.InvariantCulture) + "%";
    }

    private static string FormatPercentValue(long value, long total) {
        if (value <= 0 || total <= 0) {
            return "0";
        }

        return (Math.Min(1d, value / (double)total) * 100d).ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string Html(string value) {
        return WebUtility.HtmlEncode(value ?? string.Empty);
    }

    private sealed record ProviderAccentColors(string Input, string Output, string Total, string Other);
}
