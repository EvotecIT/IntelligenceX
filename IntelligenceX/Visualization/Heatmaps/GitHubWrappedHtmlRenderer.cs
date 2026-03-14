using System;
using System.Text;
using static IntelligenceX.Visualization.Heatmaps.UsageTelemetryGitHubWrappedHtmlFragments;

namespace IntelligenceX.Visualization.Heatmaps;

#pragma warning disable CS1591

internal static class GitHubWrappedHtmlRenderer {
    public static string Render(UsageTelemetryOverviewProviderSection section) {
        if (section is null) {
            throw new ArgumentNullException(nameof(section));
        }

        var page = UsageTelemetryReportPageModelBuilders.BuildGitHubWrapped(section);

        var sb = new StringBuilder(24 * 1024);
        AppendHero(sb, page);
        AppendBody(sb, page);
        sb.AppendLine("    <div class=\"footer-note\">Built from the GitHub section inside the IntelligenceX telemetry report bundle, so this wrapped view stays consistent with the main contribution and owner-impact data.</div>");

        return UsageTelemetryReportStaticAssets.RenderPage(
            "github-wrapped.html",
            page.Title + " Wrapped",
            sb.ToString(),
            page.BootstrapJson);
    }

    private static void AppendHero(StringBuilder sb, UsageTelemetryGitHubWrappedPageModel page) {
        sb.AppendLine("    <section class=\"hero\">");
        sb.AppendLine("      <article class=\"hero-card wrapped-panel\">");
        sb.AppendLine("        <div class=\"eyebrow wrapped-label\">GitHub Wrapped</div>");
        sb.Append("        <h1>").Append(Html(page.Title)).AppendLine("</h1>");
        sb.Append("        <div class=\"subtitle\">").Append(Html(page.Subtitle)).AppendLine("</div>");
        AppendHeroNote(sb, page.Note);
        sb.AppendLine("        <div class=\"hero-actions\">");
        sb.AppendLine("          <a class=\"hero-action wrapped-action\" href=\"provider-github.dark.svg\" target=\"_blank\" rel=\"noopener\">Open heatmap</a>");
        sb.AppendLine("          <a class=\"hero-action wrapped-action\" href=\"overview.json\" target=\"_blank\" rel=\"noopener\">Open bundle JSON</a>");
        sb.AppendLine("          <a class=\"hero-action wrapped-action\" href=\"github-wrapped-card.html\" target=\"_blank\" rel=\"noopener\">Open share card</a>");
        sb.AppendLine("        </div>");
        if (page.OwnerPanels.Count > 0) {
            sb.AppendLine("        <div class=\"owner-switcher\" role=\"tablist\" aria-label=\"GitHub owner scope\">");
            sb.AppendLine("          <button type=\"button\" class=\"owner-chip wrapped-action active\" data-owner-panel=\"all\">All scope</button>");
            foreach (var ownerSection in page.OwnerPanels) {
                sb.Append("          <button type=\"button\" class=\"owner-chip wrapped-action\" data-owner-panel=\"")
                    .Append(Html(ownerSection.Key))
                    .Append("\">")
                    .Append(Html(ownerSection.Label))
                    .AppendLine("</button>");
            }
            sb.AppendLine("        </div>");
        }
        sb.AppendLine("      </article>");
        sb.AppendLine("      <div class=\"hero-metrics\">");
        AppendStatCard(sb, page.Metrics.ElementAtOrDefault(0)?.Label ?? "Contributions", page.Metrics.ElementAtOrDefault(0)?.Value ?? "n/a", page.Metrics.ElementAtOrDefault(0)?.Subtitle);
        AppendStatCard(sb, "Most active month", FindSpotlightValue(page, "most-active-month", "n/a"), FindSpotlightSubtitle(page, "most-active-month"));
        AppendStatCard(sb, "Longest Streak", HeatmapDisplayText.FormatDays(page.LongestStreakDays), FindSpotlightSubtitle(page, "longest-streak"));
        AppendStatCard(sb, "Current Streak", HeatmapDisplayText.FormatDays(page.CurrentStreakDays), FindSpotlightSubtitle(page, "current-streak"));
        sb.AppendLine("      </div>");
        sb.AppendLine("    </section>");
    }

    private static void AppendBody(StringBuilder sb, UsageTelemetryGitHubWrappedPageModel page) {
        sb.AppendLine("    <section class=\"section-grid\">");
        if (page.YearComparison is not null) {
            AppendYearComparison(sb, page.YearComparison);
        }

        sb.AppendLine("      <article class=\"panel heatmap-panel wrapped-panel\">");
        sb.AppendLine("        <h2 class=\"section-title\">Contribution Graph</h2>");
        sb.AppendLine("        <p class=\"section-copy\">Trailing-year contribution heatmap from the same GitHub section used in the main report.</p>");
        sb.AppendLine("        <img src=\"provider-github.dark.svg\" alt=\"GitHub activity heatmap\">");
        sb.AppendLine("      </article>");

        sb.AppendLine("      <div class=\"card-grid\">");
        AppendMiniCard(sb, "Profile vs Correlated Scope", page.ScopeSplit?.Headline ?? "Scope split", page.ScopeSplit?.Note);
        AppendMiniCard(sb, "Owned Repository Impact", page.OwnerImpact?.Headline ?? "No owned repository data", page.OwnerImpact?.Note);
        AppendMiniCard(sb, "Top Language", page.TopLanguages?.Headline ?? "n/a", page.TopLanguages?.Rows.FirstOrDefault()?.Subtitle);
        AppendMiniCard(sb, "Top Repository", page.TopRepositories?.Headline ?? "n/a", page.TopRepositories?.Rows.FirstOrDefault()?.Value);
        sb.AppendLine("      </div>");

        sb.AppendLine("      <div class=\"split-grid\">");
        AppendMonthlyContributionsPanel(sb, page);
        if (page.RecentRepositories is not null) {
            AppendRecentRepositoriesPanel(sb, page.RecentRepositories);
        }
        sb.AppendLine("      </div>");

        sb.AppendLine("      <div class=\"owner-panels\">");
        AppendOwnerPanel(sb, "all", page.TopRepositories, page.TopLanguages, null);
        foreach (var ownerSection in page.OwnerPanels) {
            AppendOwnerPanel(sb, ownerSection.Key, ownerSection.Section, page.TopLanguages, ownerSection.Label);
        }
        sb.AppendLine("      </div>");
        sb.AppendLine("    </section>");
    }

    private static void AppendOwnerPanel(
        StringBuilder sb,
        string key,
        UsageTelemetryOverviewInsightSection? primarySection,
        UsageTelemetryOverviewInsightSection? languages,
        string? ownerLabel) {
        sb.Append("        <div class=\"owner-panel");
        if (string.Equals(key, "all", StringComparison.OrdinalIgnoreCase)) {
            sb.Append(" active");
        }
        sb.Append("\" data-owner-panel-content=\"").Append(Html(key)).AppendLine("\">");
        sb.AppendLine("          <div class=\"split-grid\">");
        if (primarySection is not null) {
            AppendInsightPanel(sb, primarySection, string.IsNullOrWhiteSpace(ownerLabel) ? "Top repositories" : ownerLabel + " repositories");
        }
        if (languages is not null) {
            AppendInsightPanel(sb, languages, "Top languages");
        }
        sb.AppendLine("          </div>");
        sb.AppendLine("        </div>");
    }

    private static void AppendHeroNote(StringBuilder sb, string? note) {
        if (string.IsNullOrWhiteSpace(note)) {
            return;
        }

        var parts = note!
            .Split(new[] { " · " }, StringSplitOptions.RemoveEmptyEntries)
            .Select(static part => part.Trim())
            .Where(static part => part.Length > 0)
            .ToArray();

        if (parts.Length <= 1) {
            sb.Append("        <div class=\"hero-copy\">").Append(Html(note!)).AppendLine("</div>");
            return;
        }

        sb.AppendLine("        <div class=\"hero-facts\" aria-label=\"GitHub wrapped scope summary\">");
        foreach (var part in parts) {
            sb.Append("          <span class=\"hero-fact wrapped-copy\">").Append(Html(part)).AppendLine("</span>");
        }
        sb.AppendLine("        </div>");
    }

    private static string Html(string value) => UsageTelemetryOverviewHtmlFragments.Html(value);
}
