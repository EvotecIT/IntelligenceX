using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;

namespace IntelligenceX.Visualization.Heatmaps;

#pragma warning disable CS1591

public static class GitHubWrappedCardHtmlRenderer {
    public static string Render(UsageTelemetryOverviewProviderSection section) {
        if (section is null) {
            throw new ArgumentNullException(nameof(section));
        }

        var yearComparison = section.AdditionalInsights.FirstOrDefault(static insight =>
            string.Equals(insight.Key, "github-year-comparison", StringComparison.OrdinalIgnoreCase));
        var topRepositories = section.AdditionalInsights.FirstOrDefault(static insight =>
            string.Equals(insight.Key, "github-top-repositories", StringComparison.OrdinalIgnoreCase));
        var topLanguages = section.AdditionalInsights.FirstOrDefault(static insight =>
            string.Equals(insight.Key, "github-top-languages", StringComparison.OrdinalIgnoreCase));

        var sb = new StringBuilder(16 * 1024);
        sb.AppendLine("<!doctype html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("  <meta charset=\"utf-8\">");
        sb.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.Append("  <title>").Append(Html(section.Title)).AppendLine(" Wrapped Card</title>");
        sb.AppendLine("  <style>");
        sb.AppendLine("    :root { --bg:#060913; --panel:#101625; --line:#222c42; --ink:#f7f8fb; --muted:#a3afc3; --accent:#8b7cff; --accent2:#22c55e; --accent3:#38bdf8; }");
        sb.AppendLine("    * { box-sizing:border-box; }");
        sb.AppendLine("    body { margin:0; min-height:100vh; display:grid; place-items:center; padding:24px; background:radial-gradient(circle at top left, rgba(99,102,241,.28), transparent 35%), radial-gradient(circle at bottom right, rgba(16,185,129,.18), transparent 35%), linear-gradient(180deg, #05070d 0%, #0a1020 100%); color:var(--ink); font-family:\"IBM Plex Sans\",\"Aptos\",\"Segoe UI\",sans-serif; }");
        sb.AppendLine("    .card { width:min(920px,100%); background:linear-gradient(180deg, rgba(18,23,37,.98) 0%, rgba(13,18,28,.98) 100%); border:1px solid var(--line); border-radius:32px; padding:28px; box-shadow:0 26px 70px rgba(0,0,0,.35); }");
        sb.AppendLine("    .top { display:grid; grid-template-columns:1.15fr .85fr; gap:20px; align-items:start; }");
        sb.AppendLine("    .eyebrow { color:#c7bfff; font-size:13px; font-weight:700; letter-spacing:.1em; text-transform:uppercase; }");
        sb.AppendLine("    h1 { margin:8px 0 0; font-size:48px; line-height:.95; letter-spacing:-.05em; }");
        sb.AppendLine("    .subtitle { margin-top:10px; color:var(--muted); font-size:14px; }");
        sb.AppendLine("    .meta-grid { display:grid; grid-template-columns:repeat(2,minmax(0,1fr)); gap:12px; }");
        sb.AppendLine("    .metric { padding:16px; border-radius:20px; background:rgba(255,255,255,.03); border:1px solid var(--line); }");
        sb.AppendLine("    .metric-label { color:var(--muted); font-size:11px; font-weight:700; letter-spacing:.08em; text-transform:uppercase; }");
        sb.AppendLine("    .metric-value { margin-top:10px; font-size:30px; font-weight:800; letter-spacing:-.04em; }");
        sb.AppendLine("    .metric-copy { margin-top:8px; color:var(--muted); font-size:12px; }");
        sb.AppendLine("    .heatmap { margin-top:22px; padding:18px; border-radius:24px; background:rgba(255,255,255,.03); border:1px solid var(--line); }");
        sb.AppendLine("    .heatmap img { width:100%; height:auto; display:block; border-radius:18px; }");
        sb.AppendLine("    .stats { margin-top:18px; display:grid; grid-template-columns:repeat(4,minmax(0,1fr)); gap:14px; }");
        sb.AppendLine("    .stat { padding:16px; border-radius:20px; background:rgba(255,255,255,.03); border:1px solid var(--line); }");
        sb.AppendLine("    .stat-label { color:var(--muted); font-size:11px; font-weight:700; letter-spacing:.08em; text-transform:uppercase; }");
        sb.AppendLine("    .stat-value { margin-top:10px; font-size:28px; font-weight:800; letter-spacing:-.04em; }");
        sb.AppendLine("    .stat-copy { margin-top:8px; color:var(--muted); font-size:12px; line-height:1.4; }");
        sb.AppendLine("    .footer { margin-top:18px; display:grid; grid-template-columns:repeat(2,minmax(0,1fr)); gap:14px; }");
        sb.AppendLine("    .footer-card { padding:18px; border-radius:22px; background:rgba(255,255,255,.03); border:1px solid var(--line); }");
        sb.AppendLine("    .footer-title { color:var(--muted); font-size:12px; font-weight:700; letter-spacing:.08em; text-transform:uppercase; }");
        sb.AppendLine("    .footer-value { margin-top:12px; font-size:26px; font-weight:800; letter-spacing:-.04em; }");
        sb.AppendLine("    .footer-copy { margin-top:8px; color:var(--muted); font-size:12px; line-height:1.45; }");
        sb.AppendLine("    @media (max-width: 820px) { .top, .stats, .footer { grid-template-columns:1fr; } body { padding:14px; } h1 { font-size:40px; } }");
        sb.AppendLine("  </style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("  <article class=\"card\">");
        sb.AppendLine("    <div class=\"top\">");
        sb.AppendLine("      <div>");
        sb.AppendLine("        <div class=\"eyebrow\">GitHub Wrapped Card</div>");
        sb.Append("        <h1>").Append(Html(section.Title)).AppendLine("</h1>");
        sb.Append("        <div class=\"subtitle\">").Append(Html(section.Subtitle)).AppendLine("</div>");
        sb.AppendLine("      </div>");
        sb.AppendLine("      <div class=\"meta-grid\">");
        AppendMetric(sb, "Contributions", section.Metrics.ElementAtOrDefault(0)?.Value ?? "n/a", section.Metrics.ElementAtOrDefault(0)?.Subtitle);
        AppendMetric(sb, "Most active month", FindCard(section, "most-active-month")?.Value ?? "n/a", FindCard(section, "most-active-month")?.Subtitle);
        AppendMetric(sb, "Longest streak", section.LongestStreakDays.ToString(CultureInfo.InvariantCulture) + " days", FindCard(section, "longest-streak")?.Subtitle);
        AppendMetric(sb, "Current streak", section.CurrentStreakDays.ToString(CultureInfo.InvariantCulture) + " days", FindCard(section, "current-streak")?.Subtitle);
        sb.AppendLine("      </div>");
        sb.AppendLine("    </div>");
        sb.AppendLine("    <div class=\"heatmap\">");
        sb.AppendLine("      <img src=\"provider-github.dark.svg\" alt=\"GitHub activity heatmap\">");
        sb.AppendLine("    </div>");
        sb.AppendLine("    <div class=\"stats\">");
        AppendStat(sb, "Year over year", yearComparison?.Headline ?? "n/a", yearComparison?.Note);
        AppendStat(sb, "Top repository", topRepositories?.Headline ?? "n/a", topRepositories?.Rows.FirstOrDefault()?.Value);
        AppendStat(sb, "Top language", topLanguages?.Headline ?? "n/a", topLanguages?.Rows.FirstOrDefault()?.Value);
        AppendStat(sb, "Owner scope", FindOwnerScopeSummary(section), section.Note);
        sb.AppendLine("    </div>");
        sb.AppendLine("    <div class=\"footer\">");
        AppendFooter(sb, "Recent repo", FindInsightValue(section, "github-recent-repositories"), FindInsightSubtitle(section, "github-recent-repositories"));
        AppendFooter(sb, "Repository impact", FindInsightHeadline(section, "github-owner-impact") ?? "n/a", FindInsightNote(section, "github-owner-impact"));
        sb.AppendLine("    </div>");
        sb.AppendLine("  </article>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");
        return sb.ToString();
    }

    private static void AppendMetric(StringBuilder sb, string label, string value, string? copy) {
        sb.AppendLine("        <div class=\"metric\">");
        sb.Append("          <div class=\"metric-label\">").Append(Html(label)).AppendLine("</div>");
        sb.Append("          <div class=\"metric-value\">").Append(Html(value)).AppendLine("</div>");
        if (!string.IsNullOrWhiteSpace(copy)) {
            sb.Append("          <div class=\"metric-copy\">").Append(Html(copy!)).AppendLine("</div>");
        }
        sb.AppendLine("        </div>");
    }

    private static void AppendStat(StringBuilder sb, string label, string value, string? copy) {
        sb.AppendLine("      <div class=\"stat\">");
        sb.Append("        <div class=\"stat-label\">").Append(Html(label)).AppendLine("</div>");
        sb.Append("        <div class=\"stat-value\">").Append(Html(value)).AppendLine("</div>");
        if (!string.IsNullOrWhiteSpace(copy)) {
            sb.Append("        <div class=\"stat-copy\">").Append(Html(copy!)).AppendLine("</div>");
        }
        sb.AppendLine("      </div>");
    }

    private static void AppendFooter(StringBuilder sb, string label, string value, string? copy) {
        sb.AppendLine("      <div class=\"footer-card\">");
        sb.Append("        <div class=\"footer-title\">").Append(Html(label)).AppendLine("</div>");
        sb.Append("        <div class=\"footer-value\">").Append(Html(value)).AppendLine("</div>");
        if (!string.IsNullOrWhiteSpace(copy)) {
            sb.Append("        <div class=\"footer-copy\">").Append(Html(copy!)).AppendLine("</div>");
        }
        sb.AppendLine("      </div>");
    }

    private static UsageTelemetryOverviewCard? FindCard(UsageTelemetryOverviewProviderSection section, string key) {
        return section.SpotlightCards.FirstOrDefault(card => string.Equals(card.Key, key, StringComparison.OrdinalIgnoreCase));
    }

    private static string FindInsightValue(UsageTelemetryOverviewProviderSection section, string key) {
        return FindInsight(section, key)?.Rows.FirstOrDefault()?.Label ?? "n/a";
    }

    private static string? FindInsightSubtitle(UsageTelemetryOverviewProviderSection section, string key) {
        return FindInsight(section, key)?.Rows.FirstOrDefault()?.Subtitle;
    }

    private static string? FindInsightHeadline(UsageTelemetryOverviewProviderSection section, string key) {
        return FindInsight(section, key)?.Headline;
    }

    private static string? FindInsightNote(UsageTelemetryOverviewProviderSection section, string key) {
        return FindInsight(section, key)?.Note;
    }

    private static string FindOwnerScopeSummary(UsageTelemetryOverviewProviderSection section) {
        var scope = FindInsight(section, "github-scope-split");
        return scope?.Rows.Count > 1
            ? scope.Rows[1].Value
            : scope?.Headline ?? "n/a";
    }

    private static UsageTelemetryOverviewInsightSection? FindInsight(UsageTelemetryOverviewProviderSection section, string key) {
        return section.AdditionalInsights.FirstOrDefault(insight => string.Equals(insight.Key, key, StringComparison.OrdinalIgnoreCase));
    }

    private static string Html(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
