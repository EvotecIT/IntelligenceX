using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;

namespace IntelligenceX.Visualization.Heatmaps;

#pragma warning disable CS1591

internal static class GitHubWrappedHtmlRenderer {
    public static string Render(UsageTelemetryOverviewProviderSection section) {
        if (section is null) {
            throw new ArgumentNullException(nameof(section));
        }

        var yearComparison = FindInsight(section, "github-year-comparison");
        var scopeSplit = FindInsight(section, "github-scope-split");
        var ownerImpact = FindInsight(section, "github-owner-impact");
        var topLanguages = FindInsight(section, "github-top-languages");
        var recentRepositories = FindInsight(section, "github-recent-repositories");
        var topRepositories = FindInsight(section, "github-top-repositories");
        var ownerSections = section.AdditionalInsights
            .Where(static insight => insight.Key.StartsWith("github-owner-", StringComparison.OrdinalIgnoreCase)
                                     && !string.Equals(insight.Key, "github-owner-impact", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(static insight => ExtractOwnerStars(insight.Headline))
            .ThenBy(static insight => insight.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var sb = new StringBuilder(24 * 1024);
        sb.AppendLine("<!doctype html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("  <meta charset=\"utf-8\">");
        sb.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.Append("  <title>").Append(Html(section.Title)).AppendLine(" Wrapped</title>");
        sb.AppendLine("  <style>");
        sb.AppendLine("    :root { --bg:#090b12; --panel:#101521; --panel-soft:#141b29; --line:#232c3d; --ink:#f5f7fb; --muted:#9aa7bc; --accent:#8b7cff; --accent-2:#38bdf8; --accent-3:#34d399; --accent-4:#f59e0b; --accent-5:#f472b6; }");
        sb.AppendLine("    * { box-sizing:border-box; }");
        sb.AppendLine("    body { margin:0; min-height:100vh; color:var(--ink); background:radial-gradient(circle at top left, rgba(99,102,241,.22), transparent 35%), radial-gradient(circle at bottom right, rgba(236,72,153,.18), transparent 30%), linear-gradient(180deg, #070910 0%, #0b1020 100%); font-family:\"IBM Plex Sans\",\"Aptos\",\"Segoe UI\",sans-serif; }");
        sb.AppendLine("    .page { max-width:1200px; margin:0 auto; padding:36px 32px 48px; }");
        sb.AppendLine("    .hero { display:grid; grid-template-columns:1.15fr .85fr; gap:22px; align-items:start; margin-bottom:24px; }");
        sb.AppendLine("    .hero-card, .panel, .stat-card, .compare-card { background:linear-gradient(180deg, rgba(18,24,38,.92) 0%, rgba(13,18,29,.96) 100%); border:1px solid var(--line); border-radius:28px; box-shadow:0 20px 60px rgba(0,0,0,.28); }");
        sb.AppendLine("    .hero-card { padding:28px; }");
        sb.AppendLine("    .eyebrow { color:#b7a9ff; font-size:14px; font-weight:700; letter-spacing:.08em; text-transform:uppercase; }");
        sb.AppendLine("    h1 { margin:10px 0 0; font-size:52px; line-height:.95; letter-spacing:-.05em; }");
        sb.AppendLine("    .subtitle { margin:12px 0 0; color:var(--muted); font-size:15px; max-width:60ch; }");
        sb.AppendLine("    .hero-copy { margin:18px 0 0; color:var(--muted); font-size:14px; line-height:1.5; }");
        sb.AppendLine("    .hero-actions { margin-top:18px; display:flex; flex-wrap:wrap; gap:10px; }");
        sb.AppendLine("    .hero-action { display:inline-flex; align-items:center; justify-content:center; min-height:40px; padding:0 14px; border-radius:999px; background:rgba(139,124,255,.14); border:1px solid rgba(139,124,255,.32); color:var(--ink); text-decoration:none; font-size:13px; font-weight:700; }");
        sb.AppendLine("    .owner-switcher { margin-top:18px; display:flex; flex-wrap:wrap; gap:10px; }");
        sb.AppendLine("    .owner-chip { display:inline-flex; align-items:center; justify-content:center; min-height:36px; padding:0 12px; border-radius:999px; background:rgba(255,255,255,.04); border:1px solid var(--line); color:var(--ink); font-size:12px; font-weight:700; letter-spacing:.04em; text-transform:uppercase; cursor:pointer; }");
        sb.AppendLine("    .owner-chip.active { background:rgba(139,124,255,.18); border-color:rgba(139,124,255,.34); color:#fff; }");
        sb.AppendLine("    .hero-metrics { display:grid; grid-template-columns:repeat(2,minmax(0,1fr)); gap:14px; }");
        sb.AppendLine("    .stat-card { padding:18px; min-height:138px; }");
        sb.AppendLine("    .stat-label { color:var(--muted); font-size:12px; font-weight:700; letter-spacing:.08em; text-transform:uppercase; }");
        sb.AppendLine("    .stat-value { margin-top:10px; font-size:36px; line-height:1; font-weight:800; letter-spacing:-.05em; }");
        sb.AppendLine("    .stat-copy { margin-top:10px; color:var(--muted); font-size:13px; line-height:1.45; }");
        sb.AppendLine("    .section-grid { display:grid; gap:20px; }");
        sb.AppendLine("    .heatmap-panel { padding:22px; }");
        sb.AppendLine("    .heatmap-panel img { width:100%; height:auto; display:block; border-radius:20px; }");
        sb.AppendLine("    .section-title { margin:0 0 14px; font-size:28px; letter-spacing:-.04em; }");
        sb.AppendLine("    .section-copy { margin:0 0 18px; color:var(--muted); font-size:14px; }");
        sb.AppendLine("    .compare-grid { display:grid; grid-template-columns:1fr auto 1fr; gap:16px; align-items:center; }");
        sb.AppendLine("    .compare-card { padding:22px; }");
        sb.AppendLine("    .compare-card.highlight { background:linear-gradient(180deg, rgba(25,45,88,.65) 0%, rgba(15,28,56,.92) 100%); }");
        sb.AppendLine("    .compare-arrow { width:48px; height:48px; display:flex; align-items:center; justify-content:center; border-radius:999px; border:1px solid var(--line); background:rgba(255,255,255,.03); color:var(--muted); font-size:22px; font-weight:800; }");
        sb.AppendLine("    .compare-label { color:var(--muted); font-size:12px; font-weight:700; letter-spacing:.08em; text-transform:uppercase; }");
        sb.AppendLine("    .compare-value { margin-top:10px; font-size:32px; font-weight:800; letter-spacing:-.05em; }");
        sb.AppendLine("    .compare-subtitle { margin-top:8px; color:var(--muted); font-size:13px; line-height:1.45; }");
        sb.AppendLine("    .card-grid { display:grid; grid-template-columns:repeat(4,minmax(0,1fr)); gap:16px; }");
        sb.AppendLine("    .mini-card { padding:18px; min-height:132px; background:linear-gradient(180deg, rgba(20,27,41,.96) 0%, rgba(14,19,29,.98) 100%); border:1px solid var(--line); border-radius:22px; }");
        sb.AppendLine("    .mini-card .mini-label { color:var(--muted); font-size:12px; font-weight:700; letter-spacing:.08em; text-transform:uppercase; }");
        sb.AppendLine("    .mini-card .mini-value { margin-top:10px; font-size:30px; font-weight:800; letter-spacing:-.04em; line-height:1.05; }");
        sb.AppendLine("    .mini-card .mini-copy { margin-top:8px; color:var(--muted); font-size:13px; line-height:1.45; }");
        sb.AppendLine("    .split-grid { display:grid; grid-template-columns:1fr 1fr; gap:18px; }");
        sb.AppendLine("    .panel { padding:22px; }");
        sb.AppendLine("    .row-list { display:grid; gap:12px; }");
        sb.AppendLine("    .row { display:grid; gap:6px; }");
        sb.AppendLine("    .row-head { display:flex; justify-content:space-between; gap:12px; align-items:baseline; }");
        sb.AppendLine("    .row-label { min-width:0; font-size:15px; font-weight:700; letter-spacing:-.02em; }");
        sb.AppendLine("    .row-label a { color:inherit; text-decoration:none; }");
        sb.AppendLine("    .row-value { color:var(--muted); font-size:14px; white-space:nowrap; }");
        sb.AppendLine("    .row-copy { color:var(--muted); font-size:12px; line-height:1.45; }");
        sb.AppendLine("    .row-bar { width:100%; height:8px; border-radius:999px; overflow:hidden; background:rgba(255,255,255,.06); }");
        sb.AppendLine("    .row-fill { height:100%; border-radius:999px; background:linear-gradient(90deg, var(--accent-2) 0%, var(--accent) 100%); min-width:4px; }");
        sb.AppendLine("    .badge-row { display:flex; flex-wrap:wrap; gap:8px; margin-top:6px; }");
        sb.AppendLine("    .badge { display:inline-flex; align-items:center; justify-content:center; min-height:28px; padding:6px 10px; border-radius:999px; font-size:12px; font-weight:700; border:1px solid transparent; }");
        sb.AppendLine("    .badge.active { background:rgba(52,211,153,.16); color:#70f0c3; border-color:rgba(52,211,153,.28); }");
        sb.AppendLine("    .badge.rising { background:rgba(251,146,60,.16); color:#fbbf24; border-color:rgba(251,146,60,.30); }");
        sb.AppendLine("    .badge.established { background:rgba(96,165,250,.16); color:#93c5fd; border-color:rgba(96,165,250,.30); }");
        sb.AppendLine("    .badge.warm { background:rgba(245,158,11,.16); color:#fbbf24; border-color:rgba(245,158,11,.28); }");
        sb.AppendLine("    .badge.dormant { background:rgba(156,163,175,.16); color:#d1d5db; border-color:rgba(156,163,175,.24); }");
        sb.AppendLine("    .badge.scope { background:rgba(139,124,255,.14); color:#cfc7ff; border-color:rgba(139,124,255,.24); }");
        sb.AppendLine("    .owner-panels { display:grid; gap:18px; }");
        sb.AppendLine("    .owner-panel { display:none; }");
        sb.AppendLine("    .owner-panel.active { display:block; }");
        sb.AppendLine("    .month-grid { display:grid; grid-template-columns:repeat(auto-fit,minmax(52px,1fr)); gap:12px; align-items:end; min-height:170px; }");
        sb.AppendLine("    .month { display:flex; flex-direction:column; align-items:center; gap:8px; }");
        sb.AppendLine("    .month-bar-wrap { width:100%; height:92px; display:flex; align-items:flex-end; }");
        sb.AppendLine("    .month-bar { width:100%; min-height:6px; border-radius:12px 12px 6px 6px; background:linear-gradient(180deg, rgba(67,223,148,.92) 0%, rgba(34,197,94,.86) 100%); box-shadow:0 12px 24px rgba(34,197,94,.18); }");
        sb.AppendLine("    .month-label { color:var(--muted); font-size:12px; }");
        sb.AppendLine("    .month-value { color:var(--muted); font-size:11px; }");
        sb.AppendLine("    .footer-note { margin-top:18px; color:var(--muted); font-size:13px; }");
        sb.AppendLine("    @media (max-width: 1024px) { .hero, .split-grid, .compare-grid, .card-grid { grid-template-columns:1fr; } .hero-metrics { grid-template-columns:1fr 1fr; } .compare-arrow { margin:0 auto; } }");
        sb.AppendLine("    @media (max-width: 720px) { .page { padding:22px 18px 32px; } h1 { font-size:42px; } .hero-metrics { grid-template-columns:1fr; } }");
        sb.AppendLine("  </style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("  <main class=\"page\">");
        sb.AppendLine("    <section class=\"hero\">");
        sb.AppendLine("      <article class=\"hero-card\">");
        sb.AppendLine("        <div class=\"eyebrow\">GitHub Wrapped</div>");
        sb.Append("        <h1>").Append(Html(section.Title)).AppendLine("</h1>");
        sb.Append("        <div class=\"subtitle\">").Append(Html(section.Subtitle)).AppendLine("</div>");
        if (!string.IsNullOrWhiteSpace(section.Note)) {
            sb.Append("        <div class=\"hero-copy\">").Append(Html(section.Note!)).AppendLine("</div>");
        }
        sb.AppendLine("        <div class=\"hero-actions\">");
        sb.AppendLine("          <a class=\"hero-action\" href=\"provider-github.dark.svg\" target=\"_blank\" rel=\"noopener\">Open heatmap</a>");
        sb.AppendLine("          <a class=\"hero-action\" href=\"overview.json\" target=\"_blank\" rel=\"noopener\">Open bundle JSON</a>");
        sb.AppendLine("          <a class=\"hero-action\" href=\"github-wrapped-card.html\" target=\"_blank\" rel=\"noopener\">Open share card</a>");
        sb.AppendLine("        </div>");
        if (ownerSections.Length > 0) {
            sb.AppendLine("        <div class=\"owner-switcher\" role=\"tablist\" aria-label=\"GitHub owner scope\">");
            sb.AppendLine("          <button type=\"button\" class=\"owner-chip active\" data-owner-panel=\"all\">All scope</button>");
            foreach (var ownerSection in ownerSections) {
                sb.Append("          <button type=\"button\" class=\"owner-chip\" data-owner-panel=\"")
                    .Append(Html(ownerSection.Key))
                    .Append("\">")
                    .Append(Html(ownerSection.Title))
                    .AppendLine("</button>");
            }
            sb.AppendLine("        </div>");
        }
        sb.AppendLine("      </article>");
        sb.AppendLine("      <div class=\"hero-metrics\">");
        AppendStatCard(sb, section.Metrics.ElementAtOrDefault(0)?.Label ?? "Contributions", section.Metrics.ElementAtOrDefault(0)?.Value ?? FormatCompact(section.TotalTokens), section.Metrics.ElementAtOrDefault(0)?.Subtitle);
        AppendStatCard(sb, FindCardValue(section, "most-active-month", "Most Active Month"), FindCardValue(section, "most-active-month", null), FindCardSubtitle(section, "most-active-month"));
        AppendStatCard(sb, "Longest Streak", section.LongestStreakDays.ToString(CultureInfo.InvariantCulture) + " days", BuildCardCopy(section, "longest-streak"));
        AppendStatCard(sb, "Current Streak", section.CurrentStreakDays.ToString(CultureInfo.InvariantCulture) + " days", BuildCardCopy(section, "current-streak"));
        sb.AppendLine("      </div>");
        sb.AppendLine("    </section>");

        sb.AppendLine("    <section class=\"section-grid\">");
        if (yearComparison is not null) {
            AppendYearComparison(sb, yearComparison);
        }
        sb.AppendLine("      <article class=\"panel heatmap-panel\">");
        sb.AppendLine("        <h2 class=\"section-title\">Contribution Graph</h2>");
        sb.AppendLine("        <p class=\"section-copy\">Trailing-year contribution heatmap from the same GitHub section used in the main report.</p>");
        sb.AppendLine("        <img src=\"provider-github.dark.svg\" alt=\"GitHub activity heatmap\">");
        sb.AppendLine("      </article>");

        sb.AppendLine("      <div class=\"card-grid\">");
        AppendMiniCard(sb, "Profile vs Owner Scope", scopeSplit?.Headline ?? "Scope split", scopeSplit?.Note);
        AppendMiniCard(sb, "Owned Repository Impact", ownerImpact?.Headline ?? "No owned repository data", ownerImpact?.Note);
        AppendMiniCard(sb, "Top Language", topLanguages?.Headline ?? "n/a", topLanguages?.Rows.FirstOrDefault()?.Subtitle);
        AppendMiniCard(sb, "Top Repository", topRepositories?.Headline ?? "n/a", topRepositories?.Rows.FirstOrDefault()?.Value);
        sb.AppendLine("      </div>");

        sb.AppendLine("      <div class=\"split-grid\">");
        AppendMonthlyContributionsPanel(sb, section);
        if (recentRepositories is not null) {
            AppendRecentRepositoriesPanel(sb, recentRepositories);
        }
        sb.AppendLine("      </div>");

        sb.AppendLine("      <div class=\"owner-panels\">");
        sb.AppendLine("        <div class=\"owner-panel active\" data-owner-panel-content=\"all\">");
        sb.AppendLine("          <div class=\"split-grid\">");
        if (topRepositories is not null) {
            AppendInsightPanel(sb, topRepositories, "Top repositories");
        }
        if (topLanguages is not null) {
            AppendInsightPanel(sb, topLanguages, "Top languages");
        }
        sb.AppendLine("          </div>");
        sb.AppendLine("        </div>");
        foreach (var ownerSection in ownerSections) {
            sb.Append("        <div class=\"owner-panel\" data-owner-panel-content=\"").Append(Html(ownerSection.Key)).AppendLine("\">");
            sb.AppendLine("          <div class=\"split-grid\">");
            AppendInsightPanel(sb, ownerSection, ownerSection.Title + " repositories");
            if (topLanguages is not null) {
                AppendInsightPanel(sb, topLanguages, "Top languages");
            }
            sb.AppendLine("          </div>");
            sb.AppendLine("        </div>");
        }
        sb.AppendLine("      </div>");
        sb.AppendLine("    </section>");
        sb.AppendLine("    <div class=\"footer-note\">Built from the GitHub section inside the IntelligenceX telemetry report bundle, so this wrapped view stays consistent with the main contribution and owner-impact data.</div>");
        sb.AppendLine("  </main>");
        sb.AppendLine("  <script>");
        sb.AppendLine("    const ownerChips = document.querySelectorAll('.owner-chip');");
        sb.AppendLine("    const ownerPanels = document.querySelectorAll('.owner-panel');");
        sb.AppendLine("    ownerChips.forEach(chip => {");
        sb.AppendLine("      chip.addEventListener('click', () => {");
        sb.AppendLine("        const target = chip.getAttribute('data-owner-panel');");
        sb.AppendLine("        ownerChips.forEach(other => other.classList.toggle('active', other === chip));");
        sb.AppendLine("        ownerPanels.forEach(panel => panel.classList.toggle('active', panel.getAttribute('data-owner-panel-content') === target));");
        sb.AppendLine("      });");
        sb.AppendLine("    });");
        sb.AppendLine("  </script>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");
        return sb.ToString();
    }

    private static void AppendStatCard(StringBuilder sb, string label, string value, string? copy) {
        sb.AppendLine("        <article class=\"stat-card\">");
        sb.Append("          <div class=\"stat-label\">").Append(Html(label)).AppendLine("</div>");
        sb.Append("          <div class=\"stat-value\">").Append(Html(value)).AppendLine("</div>");
        if (!string.IsNullOrWhiteSpace(copy)) {
            sb.Append("          <div class=\"stat-copy\">").Append(Html(copy!)).AppendLine("</div>");
        }
        sb.AppendLine("        </article>");
    }

    private static void AppendYearComparison(StringBuilder sb, UsageTelemetryOverviewInsightSection insight) {
        sb.AppendLine("      <div class=\"compare-grid\">");
        if (insight.Rows.Count > 0) {
            AppendCompareCard(sb, insight.Rows[0], false);
        }
        sb.AppendLine("        <div class=\"compare-arrow\">→</div>");
        if (insight.Rows.Count > 1) {
            AppendCompareCard(sb, insight.Rows[1], true);
        } else {
            AppendCompareCard(sb, new UsageTelemetryOverviewInsightRow("Current", insight.Headline ?? "n/a", insight.Note), true);
        }
        sb.AppendLine("      </div>");
    }

    private static void AppendCompareCard(StringBuilder sb, UsageTelemetryOverviewInsightRow row, bool highlight) {
        sb.Append("      <article class=\"compare-card");
        if (highlight) {
            sb.Append(" highlight");
        }
        sb.AppendLine("\">");
        sb.Append("        <div class=\"compare-label\">").Append(Html(row.Label)).AppendLine("</div>");
        sb.Append("        <div class=\"compare-value\">").Append(Html(row.Value)).AppendLine("</div>");
        if (!string.IsNullOrWhiteSpace(row.Subtitle)) {
            sb.Append("        <div class=\"compare-subtitle\">").Append(Html(row.Subtitle!)).AppendLine("</div>");
        }
        sb.AppendLine("      </article>");
    }

    private static void AppendMiniCard(StringBuilder sb, string label, string value, string? copy) {
        sb.AppendLine("        <article class=\"mini-card\">");
        sb.Append("          <div class=\"mini-label\">").Append(Html(label)).AppendLine("</div>");
        sb.Append("          <div class=\"mini-value\">").Append(Html(value)).AppendLine("</div>");
        if (!string.IsNullOrWhiteSpace(copy)) {
            sb.Append("          <div class=\"mini-copy\">").Append(Html(copy!)).AppendLine("</div>");
        }
        sb.AppendLine("        </article>");
    }

    private static void AppendMonthlyContributionsPanel(StringBuilder sb, UsageTelemetryOverviewProviderSection section) {
        var months = section.MonthlyUsage ?? Array.Empty<UsageTelemetryOverviewMonthlyUsage>();
        var maxValue = months.Count == 0 ? 0L : months.Max(static month => month.TotalValue);

        sb.AppendLine("      <article class=\"panel\">");
        sb.Append("        <h2 class=\"section-title\">").Append(Html(section.MonthlyUsageTitle)).AppendLine("</h2>");
        sb.Append("        <p class=\"section-copy\">").Append(Html(months.Count.ToString(CultureInfo.InvariantCulture))).AppendLine(" month contribution window.</p>");
        sb.AppendLine("        <div class=\"month-grid\">");
        foreach (var month in months) {
            var height = maxValue <= 0L ? 6d : Math.Max(6d, month.TotalValue / (double)maxValue * 96d);
            sb.AppendLine("          <div class=\"month\">");
            sb.AppendLine("            <div class=\"month-bar-wrap\">");
            sb.Append("              <div class=\"month-bar\" style=\"height:")
                .Append(Html(height.ToString("0.##", CultureInfo.InvariantCulture)))
                .AppendLine("px;\"></div>");
            sb.AppendLine("            </div>");
            sb.Append("            <div class=\"month-label\">").Append(Html(month.Label)).AppendLine("</div>");
            sb.Append("            <div class=\"month-value\">").Append(Html(FormatCompact(month.TotalValue))).AppendLine("</div>");
            sb.AppendLine("          </div>");
        }
        sb.AppendLine("        </div>");
        sb.AppendLine("      </article>");
    }

    private static void AppendRecentRepositoriesPanel(StringBuilder sb, UsageTelemetryOverviewInsightSection insight) {
        sb.AppendLine("      <article class=\"panel\">");
        sb.Append("        <h2 class=\"section-title\">").Append(Html(insight.Title)).AppendLine("</h2>");
        if (!string.IsNullOrWhiteSpace(insight.Note)) {
            sb.Append("        <p class=\"section-copy\">").Append(Html(insight.Note!)).AppendLine("</p>");
        }
        sb.AppendLine("        <div class=\"row-list\">");
        foreach (var row in insight.Rows.Take(6)) {
            AppendInsightRow(sb, row, includeBadge: true);
        }
        sb.AppendLine("        </div>");
        sb.AppendLine("      </article>");
    }

    private static void AppendInsightPanel(StringBuilder sb, UsageTelemetryOverviewInsightSection insight, string fallbackTitle) {
        sb.AppendLine("      <article class=\"panel\">");
        sb.Append("        <h2 class=\"section-title\">").Append(Html(string.IsNullOrWhiteSpace(insight.Title) ? fallbackTitle : insight.Title)).AppendLine("</h2>");
        if (!string.IsNullOrWhiteSpace(insight.Note)) {
            sb.Append("        <p class=\"section-copy\">").Append(Html(insight.Note!)).AppendLine("</p>");
        }
        sb.AppendLine("        <div class=\"row-list\">");
        foreach (var row in insight.Rows.Take(6)) {
            AppendInsightRow(sb, row, includeBadge: false);
        }
        sb.AppendLine("        </div>");
        sb.AppendLine("      </article>");
    }

    private static void AppendInsightRow(StringBuilder sb, UsageTelemetryOverviewInsightRow row, bool includeBadge) {
        sb.AppendLine("          <div class=\"row\">");
        sb.AppendLine("            <div class=\"row-head\">");
        sb.Append("              <div class=\"row-label\">");
        if (!string.IsNullOrWhiteSpace(row.Href)) {
            sb.Append("<a href=\"").Append(Html(row.Href!)).Append("\" target=\"_blank\" rel=\"noopener\">")
                .Append(Html(row.Label))
                .Append("</a>");
        } else {
            sb.Append(Html(row.Label));
        }
        sb.AppendLine("</div>");
        sb.Append("              <div class=\"row-value\">").Append(Html(row.Value)).AppendLine("</div>");
        sb.AppendLine("            </div>");
        if (!string.IsNullOrWhiteSpace(row.Subtitle)) {
            sb.Append("            <div class=\"row-copy\">").Append(Html(row.Subtitle!)).AppendLine("</div>");
        }
        if (row.Ratio.HasValue && row.Ratio.Value > 0d) {
            sb.AppendLine("            <div class=\"row-bar\">");
            sb.Append("              <div class=\"row-fill\" style=\"width:")
                .Append(Html(FormatRatioPercent(row.Ratio)))
                .AppendLine("%;\"></div>");
            sb.AppendLine("            </div>");
        }
        if (includeBadge) {
            var (badgeClass, badgeLabel) = ResolveRepoHealthBadge(row.Subtitle);
            if (!string.IsNullOrWhiteSpace(badgeLabel)) {
                sb.AppendLine("            <div class=\"badge-row\">");
                sb.Append("              <span class=\"badge ").Append(Html(badgeClass)).Append("\">").Append(Html(badgeLabel)).AppendLine("</span>");
                sb.AppendLine("            </div>");
            }
        }
        sb.AppendLine("          </div>");
    }

    private static UsageTelemetryOverviewInsightSection? FindInsight(UsageTelemetryOverviewProviderSection section, string key) {
        return section.AdditionalInsights.FirstOrDefault(insight => string.Equals(insight.Key, key, StringComparison.OrdinalIgnoreCase));
    }

    private static string FindCardValue(UsageTelemetryOverviewProviderSection section, string key, string? fallback) {
        return section.SpotlightCards.FirstOrDefault(card => string.Equals(card.Key, key, StringComparison.OrdinalIgnoreCase))?.Value
               ?? fallback
               ?? "n/a";
    }

    private static string? FindCardSubtitle(UsageTelemetryOverviewProviderSection section, string key) {
        return section.SpotlightCards.FirstOrDefault(card => string.Equals(card.Key, key, StringComparison.OrdinalIgnoreCase))?.Subtitle;
    }

    private static string? BuildCardCopy(UsageTelemetryOverviewProviderSection section, string key) {
        return section.SpotlightCards.FirstOrDefault(card => string.Equals(card.Key, key, StringComparison.OrdinalIgnoreCase))?.Subtitle;
    }

    private static (string CssClass, string Label) ResolveRepoHealthBadge(string? subtitle) {
        var normalized = NormalizeOptional(subtitle);
        if (string.IsNullOrWhiteSpace(normalized)) {
            return ("dormant", string.Empty);
        }

        var text = normalized!;
        return text.StartsWith("Rising ·", StringComparison.OrdinalIgnoreCase)
            ? ("rising", "Rising")
            : text.StartsWith("Active ·", StringComparison.OrdinalIgnoreCase)
                ? ("active", "Active")
                : text.StartsWith("Established ·", StringComparison.OrdinalIgnoreCase)
                    ? ("established", "Established")
                    : text.StartsWith("Warm ·", StringComparison.OrdinalIgnoreCase)
                        ? ("warm", "Warm")
                        : text.StartsWith("Dormant ·", StringComparison.OrdinalIgnoreCase)
                            ? ("dormant", "Dormant")
                            : ("dormant", string.Empty);
    }

    private static string FormatCompact(long value) {
        if (value >= 1_000_000_000L) {
            return (value / 1_000_000_000d).ToString(value >= 10_000_000_000L ? "0.#" : "0.##", CultureInfo.InvariantCulture) + "B";
        }
        if (value >= 1_000_000L) {
            return (value / 1_000_000d).ToString(value >= 10_000_000L ? "0.#" : "0.##", CultureInfo.InvariantCulture) + "M";
        }
        if (value >= 1_000L) {
            return (value / 1_000d).ToString(value >= 10_000L ? "0.#" : "0.##", CultureInfo.InvariantCulture) + "K";
        }

        return value.ToString("0", CultureInfo.InvariantCulture);
    }

    private static string FormatRatioPercent(double? ratio) {
        if (!ratio.HasValue || ratio.Value <= 0d) {
            return "0";
        }

        return (Math.Max(0d, Math.Min(1d, ratio.Value)) * 100d).ToString("0.#", CultureInfo.InvariantCulture);
    }

    private static string Html(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    private static string? NormalizeOptional(string? value) {
        return string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
    }

    private static long ExtractOwnerStars(string? headline) {
        if (string.IsNullOrWhiteSpace(headline)) {
            return 0L;
        }

        var token = headline!.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return ParseCompactLong(token);
    }

    private static long ParseCompactLong(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return 0L;
        }

        var normalized = value!.Trim().ToUpperInvariant();
        var multiplier = 1d;
        if (normalized.EndsWith("B", StringComparison.Ordinal)) {
            multiplier = 1_000_000_000d;
            normalized = normalized.Substring(0, normalized.Length - 1);
        } else if (normalized.EndsWith("M", StringComparison.Ordinal)) {
            multiplier = 1_000_000d;
            normalized = normalized.Substring(0, normalized.Length - 1);
        } else if (normalized.EndsWith("K", StringComparison.Ordinal)) {
            multiplier = 1_000d;
            normalized = normalized.Substring(0, normalized.Length - 1);
        }

        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? (long)Math.Round(parsed * multiplier, MidpointRounding.AwayFromZero)
            : 0L;
    }
}
