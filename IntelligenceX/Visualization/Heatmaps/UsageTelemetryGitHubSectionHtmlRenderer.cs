using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using static IntelligenceX.Visualization.Heatmaps.UsageTelemetryOverviewHtmlFragments;

namespace IntelligenceX.Visualization.Heatmaps;

internal static class UsageTelemetryGitHubSectionHtmlRenderer {
    public static void AppendSummaryStrip(StringBuilder sb, UsageTelemetryGitHubSectionPageModel? model) {
        if (model is null) {
            return;
        }

        var yearComparison = model.YearComparison;
        var scopeSplit = model.ScopeSplit;
        var watchedRepositories = model.WatchedRepositories;
        var watchedCorrelations = model.WatchedCorrelations;
        var watchedStarCorrelations = model.WatchedStarCorrelations;
        var watchedRepoClusters = model.WatchedRepoClusters;
        var watchedStargazerAudience = model.WatchedStargazerAudience;
        var watchedForkNetwork = model.WatchedForkNetwork;
        var watchedForkMomentum = model.WatchedForkMomentum;
        var watchedLocalAlignment = model.WatchedLocalAlignment;
        var recentRepositories = model.RecentRepositories;
        var ownerImpact = model.OwnerImpact;
        var ownerSections = model.OwnerSections;
        if (yearComparison is null && scopeSplit is null && watchedRepositories is null && watchedCorrelations is null && watchedStarCorrelations is null && watchedRepoClusters is null && watchedStargazerAudience is null && watchedForkNetwork is null && watchedForkMomentum is null && watchedLocalAlignment is null && recentRepositories is null && ownerImpact is null) {
            return;
        }

        sb.AppendLine("            <div class=\"provider-feature-grid\">");
        if (yearComparison is not null) {
            AppendComparisonCard(sb, yearComparison);
        }
        if (scopeSplit is not null) {
            AppendScopeCard(sb, scopeSplit);
        }
        if (recentRepositories is not null) {
            AppendRecentRepositoriesCard(sb, recentRepositories);
        }
        if (watchedRepositories is not null) {
            AppendFeatureCard(sb, watchedRepositories);
        }
        if (watchedCorrelations is not null) {
            AppendFeatureCard(sb, watchedCorrelations);
        }
        if (watchedStarCorrelations is not null) {
            AppendFeatureCard(sb, watchedStarCorrelations);
        }
        if (watchedRepoClusters is not null) {
            AppendFeatureCard(sb, watchedRepoClusters);
        }
        if (watchedStargazerAudience is not null) {
            AppendFeatureCard(sb, watchedStargazerAudience);
        }
        if (watchedForkNetwork is not null) {
            AppendFeatureCard(sb, watchedForkNetwork);
        }
        if (watchedForkMomentum is not null) {
            AppendFeatureCard(sb, watchedForkMomentum);
        }
        if (watchedLocalAlignment is not null) {
            AppendFeatureCard(sb, watchedLocalAlignment);
        }
        sb.AppendLine("            </div>");
        if (ownerSections.Count > 0) {
            sb.AppendLine("            <div class=\"github-scope-pills\">");
            sb.AppendLine("              <span class=\"provider-badge scope\">Owner lenses available in Impact</span>");
            foreach (var ownerSection in ownerSections) {
                sb.Append("              <span class=\"provider-badge scope\">")
                    .Append(Html(ownerSection.Title))
                    .AppendLine("</span>");
            }
            sb.AppendLine("            </div>");
            AppendSummaryOwnerExplorer(sb, ownerImpact, scopeSplit, ownerSections, model.OwnerScopes);
        }
    }

    public static void AppendImpactExplorer(StringBuilder sb, UsageTelemetryGitHubSectionPageModel? model) {
        if (model is null) {
            return;
        }

        var topRepositories = model.TopRepositories;
        var topRepositoriesByForks = model.TopRepositoriesByForks;
        var topRepositoriesByHealth = model.TopRepositoriesByHealth;
        var watchedRepositories = model.WatchedRepositories;
        var watchedCorrelations = model.WatchedCorrelations;
        var watchedStarCorrelations = model.WatchedStarCorrelations;
        var watchedRepoClusters = model.WatchedRepoClusters;
        var watchedStargazerAudience = model.WatchedStargazerAudience;
        var watchedForkNetwork = model.WatchedForkNetwork;
        var watchedForkMomentum = model.WatchedForkMomentum;
        var watchedLocalAlignment = model.WatchedLocalAlignment;
        var recentRepositories = model.RecentRepositories;
        var topLanguages = model.TopLanguages;
        var ownerImpact = model.OwnerImpact;
        var scopeSplit = model.ScopeSplit;
        var ownerSections = model.OwnerSections;
        var lenses = model.Lenses;
        var repoSortModes = model.RepoSortModes;
        var ownerScopes = model.OwnerScopes;

        sb.AppendLine("          <div class=\"github-impact-shell\">");
        sb.AppendLine("            <div class=\"github-lens-switcher\" role=\"tablist\" aria-label=\"GitHub impact lenses\">");
        foreach (var lens in lenses) {
            sb.Append("              <button type=\"button\" class=\"github-lens-tab");
            if (lens.IsDefault) {
                sb.Append(" active");
            }
            sb.Append("\" data-github-lens=\"")
                .Append(Html(lens.Key))
                .Append("\">")
                .Append(Html(lens.Label))
                .AppendLine("</button>");
        }
        sb.AppendLine("            </div>");

        sb.AppendLine("            <div class=\"github-lens-panel active\" data-github-lens-content=\"impact\">");
        sb.AppendLine("              <div class=\"github-impact-toolbar\">");
        sb.AppendLine("                <div class=\"github-repo-sorter\">");
        sb.AppendLine("                  <div class=\"github-repo-sort-kicker\">Repository ranking</div>");
        sb.AppendLine("                  <div class=\"github-repo-sort-tabs\" role=\"tablist\" aria-label=\"GitHub repository ranking\">");
        foreach (var repoSortMode in repoSortModes) {
            sb.Append("                    <button type=\"button\" class=\"github-repo-sort-tab");
            if (repoSortMode.IsDefault) {
                sb.Append(" active");
            }
            sb.Append("\" data-github-repo-sort=\"")
                .Append(Html(repoSortMode.Key))
                .Append("\">")
                .Append(Html(repoSortMode.Label))
                .AppendLine("</button>");
        }
        sb.AppendLine("                  </div>");
        sb.AppendLine("                </div>");
        sb.AppendLine("              </div>");
        sb.AppendLine("              <div class=\"provider-insights tight\">");
        if (topRepositories is not null && topRepositoriesByHealth is not null) {
            AppendRepositoryComparisonCard(sb, topRepositories, topRepositoriesByHealth);
        }
        sb.AppendLine("                <div class=\"github-repo-sort-panel active\" data-github-repo-sort-content=\"stars\">");
        if (topRepositories is not null) {
            AppendInsightSection(sb, topRepositories);
        }
        sb.AppendLine("                </div>");
        if (topRepositoriesByForks is not null) {
            sb.AppendLine("                <div class=\"github-repo-sort-panel\" data-github-repo-sort-content=\"forks\">");
            AppendInsightSection(sb, topRepositoriesByForks);
            sb.AppendLine("                </div>");
        }
        if (topRepositoriesByHealth is not null) {
            sb.AppendLine("                <div class=\"github-repo-sort-panel\" data-github-repo-sort-content=\"health\">");
            AppendInsightSection(sb, topRepositoriesByHealth);
            sb.AppendLine("                </div>");
        }
        if (ownerImpact is not null) {
            AppendInsightSection(sb, ownerImpact);
        }
        if (scopeSplit is not null) {
            AppendInsightSection(sb, scopeSplit);
        }
        sb.AppendLine("              </div>");
        sb.AppendLine("            </div>");

        sb.AppendLine("            <div class=\"github-lens-panel\" data-github-lens-content=\"recent\">");
        sb.AppendLine("              <div class=\"provider-feature-grid\">");
        if (recentRepositories is not null) {
            AppendRecentRepositoriesCard(sb, recentRepositories);
        }
        if (watchedRepositories is not null) {
            AppendFeatureCard(sb, watchedRepositories);
        }
        if (watchedCorrelations is not null) {
            AppendFeatureCard(sb, watchedCorrelations);
        }
        if (watchedStarCorrelations is not null) {
            AppendFeatureCard(sb, watchedStarCorrelations);
        }
        if (watchedRepoClusters is not null) {
            AppendFeatureCard(sb, watchedRepoClusters);
        }
        if (watchedStargazerAudience is not null) {
            AppendFeatureCard(sb, watchedStargazerAudience);
        }
        if (watchedForkNetwork is not null) {
            AppendFeatureCard(sb, watchedForkNetwork);
        }
        if (watchedForkMomentum is not null) {
            AppendFeatureCard(sb, watchedForkMomentum);
        }
        if (watchedLocalAlignment is not null) {
            AppendFeatureCard(sb, watchedLocalAlignment);
        }
        if (scopeSplit is not null) {
            AppendScopeCard(sb, scopeSplit);
        }
        if (topRepositories is not null) {
            AppendFeatureCard(sb, topRepositories);
        }
        sb.AppendLine("              </div>");
        sb.AppendLine("            </div>");

        if (watchedRepositories is not null) {
            sb.AppendLine("            <div class=\"github-lens-panel\" data-github-lens-content=\"watched\">");
            sb.AppendLine("              <div class=\"provider-insights tight\">");
            AppendInsightSection(sb, watchedRepositories);
            if (watchedCorrelations is not null) {
                AppendInsightSection(sb, watchedCorrelations);
            }
            if (watchedStarCorrelations is not null) {
                AppendInsightSection(sb, watchedStarCorrelations);
            }
            if (watchedRepoClusters is not null) {
                AppendInsightSection(sb, watchedRepoClusters);
            }
            if (watchedStargazerAudience is not null) {
                AppendInsightSection(sb, watchedStargazerAudience);
            }
            if (watchedForkNetwork is not null) {
                AppendInsightSection(sb, watchedForkNetwork);
            }
            if (watchedForkMomentum is not null) {
                AppendInsightSection(sb, watchedForkMomentum);
            }
            if (watchedLocalAlignment is not null) {
                AppendInsightSection(sb, watchedLocalAlignment);
            }
            if (recentRepositories is not null) {
                AppendRecentRepositoriesCard(sb, recentRepositories);
            }
            sb.AppendLine("              </div>");
            sb.AppendLine("            </div>");
        }

        if (ownerSections.Count > 0) {
            sb.AppendLine("            <div class=\"github-lens-panel\" data-github-lens-content=\"owners\">");
            sb.AppendLine("              <div class=\"github-owner-explorer\">");
            sb.AppendLine("                <div class=\"github-owner-switcher\" role=\"tablist\" aria-label=\"GitHub owner scope\">");
            foreach (var ownerScope in ownerScopes) {
                sb.Append("                  <button type=\"button\" class=\"github-owner-chip");
                if (ownerScope.IsDefault) {
                    sb.Append(" active");
                }
                sb.Append("\" data-github-owner=\"")
                    .Append(Html(ownerScope.Key))
                    .Append("\">")
                    .Append(Html(ownerScope.Label))
                    .AppendLine("</button>");
            }
            sb.AppendLine("                </div>");
            sb.AppendLine("                <div class=\"github-owner-panel active\" data-github-owner-content=\"all\">");
            sb.AppendLine("                  <div class=\"github-impact-compact\">");
            if (ownerImpact is not null) {
                AppendInsightSection(sb, ownerImpact);
            }
            if (topRepositories is not null) {
                AppendInsightSection(sb, topRepositories);
            }
            sb.AppendLine("                  </div>");
            sb.AppendLine("                </div>");
            foreach (var ownerSection in ownerSections) {
                sb.Append("                <div class=\"github-owner-panel\" data-github-owner-content=\"")
                    .Append(Html(ownerSection.Key))
                    .AppendLine("\">");
                sb.AppendLine("                  <div class=\"github-impact-compact\">");
                AppendInsightSection(sb, ownerSection);
                if (topLanguages is not null) {
                    AppendInsightSection(sb, topLanguages);
                }
                sb.AppendLine("                  </div>");
                sb.AppendLine("                </div>");
            }
            sb.AppendLine("              </div>");
            sb.AppendLine("            </div>");
        }

        if (topLanguages is not null) {
            sb.AppendLine("            <div class=\"github-lens-panel\" data-github-lens-content=\"languages\">");
            sb.AppendLine("              <div class=\"provider-insights tight\">");
            AppendInsightSection(sb, topLanguages);
            if (ownerImpact is not null) {
                AppendInsightSection(sb, ownerImpact);
            }
            sb.AppendLine("              </div>");
            sb.AppendLine("            </div>");
        }

        sb.AppendLine("          </div>");
    }

    private static void AppendRepositoryComparisonCard(
        StringBuilder sb,
        UsageTelemetryOverviewInsightSection topRepositories,
        UsageTelemetryOverviewInsightSection topRepositoriesByHealth) {
        var starsRow = topRepositories.Rows.FirstOrDefault();
        var healthRow = topRepositoriesByHealth.Rows.FirstOrDefault();
        if (starsRow is null || healthRow is null) {
            return;
        }

        sb.AppendLine("                <article class=\"provider-compare-card\">");
        sb.AppendLine("                  <div class=\"provider-feature-kicker\">Repository comparison</div>");
        sb.AppendLine("                  <div class=\"provider-feature-headline\">Impact vs momentum</div>");
        sb.AppendLine("                  <div class=\"provider-feature-copy\">Compare the repository leading on raw stars with the repository currently leading on health across the selected owner scope.</div>");
        sb.AppendLine("                  <div class=\"provider-compare-grid\">");
        AppendCompareSide(sb, new UsageTelemetryOverviewInsightRow(
            "Top by stars",
            starsRow.Label,
            starsRow.Value + (string.IsNullOrWhiteSpace(starsRow.Subtitle) ? string.Empty : " · " + starsRow.Subtitle),
            starsRow.Ratio,
            starsRow.Href), false);
        sb.AppendLine("                    <div class=\"provider-compare-arrow\">⇄</div>");
        AppendCompareSide(sb, new UsageTelemetryOverviewInsightRow(
            "Top by health",
            healthRow.Label,
            healthRow.Value + (string.IsNullOrWhiteSpace(healthRow.Subtitle) ? string.Empty : " · " + healthRow.Subtitle),
            healthRow.Ratio,
            healthRow.Href), true);
        sb.AppendLine("                  </div>");
        sb.AppendLine("                </article>");
    }

    private static void AppendSummaryOwnerExplorer(
        StringBuilder sb,
        UsageTelemetryOverviewInsightSection? ownerImpact,
        UsageTelemetryOverviewInsightSection? scopeSplit,
        IReadOnlyList<UsageTelemetryOverviewInsightSection> ownerSections,
        IReadOnlyList<UsageTelemetryToggleOptionModel> ownerScopes) {
        if (ownerSections.Count == 0) {
            return;
        }

        sb.AppendLine("            <div class=\"github-summary-owner-shell github-owner-explorer\">");
        sb.AppendLine("              <div class=\"github-owner-switcher\" role=\"tablist\" aria-label=\"GitHub owner scope summary\">");
        foreach (var ownerScope in ownerScopes) {
            sb.Append("                <button type=\"button\" class=\"github-owner-chip");
            if (ownerScope.IsDefault) {
                sb.Append(" active");
            }
            sb.Append("\" data-github-owner=\"")
                .Append(Html(ownerScope.Key))
                .Append("\">")
                .Append(Html(ownerScope.Label))
                .AppendLine("</button>");
        }
        sb.AppendLine("              </div>");
        sb.AppendLine("              <div class=\"github-owner-panel active\" data-github-owner-content=\"all\">");
        sb.AppendLine("                <div class=\"github-impact-compact\">");
        if (scopeSplit is not null) {
            AppendFeatureCard(sb, scopeSplit);
        }
        if (ownerImpact is not null) {
            AppendInsightSection(sb, ownerImpact);
        }
        sb.AppendLine("                </div>");
        sb.AppendLine("              </div>");

        foreach (var ownerSection in ownerSections) {
            sb.Append("              <div class=\"github-owner-panel\" data-github-owner-content=\"")
                .Append(Html(ownerSection.Key))
                .AppendLine("\">");
            sb.AppendLine("                <div class=\"github-impact-compact\">");
            AppendInsightSection(sb, ownerSection);
            sb.AppendLine("                </div>");
            sb.AppendLine("              </div>");
        }

        sb.AppendLine("            </div>");
    }

    private static void AppendFeatureCard(StringBuilder sb, UsageTelemetryOverviewInsightSection insight) {
        sb.AppendLine("              <article class=\"provider-feature-card\">");
        sb.Append("                <div class=\"provider-feature-kicker\">").Append(Html(insight.Title)).AppendLine("</div>");
        if (!string.IsNullOrWhiteSpace(insight.Headline)) {
            sb.Append("                <div class=\"provider-feature-headline\">").Append(Html(insight.Headline!)).AppendLine("</div>");
        }
        if (!string.IsNullOrWhiteSpace(insight.Note)) {
            sb.Append("                <div class=\"provider-feature-copy\">").Append(Html(insight.Note!)).AppendLine("</div>");
        }

        if (insight.Rows.Count > 0) {
            sb.AppendLine("                <div class=\"provider-feature-rows\">");
            foreach (var row in insight.Rows.Take(4)) {
                sb.AppendLine("                  <div class=\"provider-feature-row\">");
                sb.AppendLine("                    <div class=\"provider-feature-row-head\">");
                sb.Append("                      <div class=\"provider-feature-row-label\">");
                if (!string.IsNullOrWhiteSpace(row.Href)) {
                    sb.Append("<a class=\"inline-link\" href=\"").Append(Html(row.Href!)).Append("\" target=\"_blank\" rel=\"noopener\">")
                        .Append(Html(row.Label))
                        .Append("</a>");
                } else {
                    sb.Append(Html(row.Label));
                }
                sb.AppendLine("</div>");
                sb.Append("                      <div class=\"provider-feature-row-value\">").Append(Html(row.Value)).AppendLine("</div>");
                sb.AppendLine("                    </div>");
                if (!string.IsNullOrWhiteSpace(row.Subtitle)) {
                    sb.Append("                    <div class=\"provider-feature-row-copy\">").Append(Html(row.Subtitle!)).AppendLine("</div>");
                }
                if (row.Ratio.HasValue && row.Ratio.Value > 0d) {
                    sb.AppendLine("                    <div class=\"provider-feature-row-bar\">");
                    sb.Append("                      <div class=\"provider-feature-row-fill\" style=\"width:")
                        .Append(Html(FormatRatioPercent(row.Ratio)))
                        .AppendLine("%;\"></div>");
                    sb.AppendLine("                    </div>");
                }
                sb.AppendLine("                  </div>");
            }
            sb.AppendLine("                </div>");
        }

        sb.AppendLine("              </article>");
    }

    private static void AppendComparisonCard(StringBuilder sb, UsageTelemetryOverviewInsightSection insight) {
        sb.AppendLine("              <article class=\"provider-compare-card\">");
        sb.Append("                <div class=\"provider-feature-kicker\">").Append(Html(insight.Title)).AppendLine("</div>");
        if (!string.IsNullOrWhiteSpace(insight.Headline)) {
            sb.Append("                <div class=\"provider-feature-headline\">").Append(Html(insight.Headline!)).AppendLine("</div>");
        }
        if (!string.IsNullOrWhiteSpace(insight.Note)) {
            sb.Append("                <div class=\"provider-feature-copy\">").Append(Html(insight.Note!)).AppendLine("</div>");
        }

        if (insight.Rows.Count >= 2) {
            var left = insight.Rows[0];
            var right = insight.Rows[1];
            sb.AppendLine("                <div class=\"provider-compare-grid\">");
            AppendCompareSide(sb, left, false);
            sb.AppendLine("                  <div class=\"provider-compare-arrow\">→</div>");
            AppendCompareSide(sb, right, true);
            sb.AppendLine("                </div>");
        } else if (insight.Rows.Count == 1) {
            var row = insight.Rows[0];
            sb.AppendLine("                <div class=\"provider-feature-rows\">");
            sb.AppendLine("                  <div class=\"provider-feature-row\">");
            sb.AppendLine("                    <div class=\"provider-feature-row-head\">");
            sb.Append("                      <div class=\"provider-feature-row-label\">").Append(Html(row.Label)).AppendLine("</div>");
            sb.Append("                      <div class=\"provider-feature-row-value\">").Append(Html(row.Value)).AppendLine("</div>");
            sb.AppendLine("                    </div>");
            if (!string.IsNullOrWhiteSpace(row.Subtitle)) {
                sb.Append("                    <div class=\"provider-feature-row-copy\">").Append(Html(row.Subtitle!)).AppendLine("</div>");
            }
            sb.AppendLine("                  </div>");
            sb.AppendLine("                </div>");
        }

        sb.AppendLine("              </article>");
    }

    private static void AppendCompareSide(StringBuilder sb, UsageTelemetryOverviewInsightRow row, bool emphasize) {
        sb.Append("                  <div class=\"provider-compare-side");
        if (emphasize) {
            sb.Append(" right");
        }
        sb.AppendLine("\">");
        sb.Append("                    <div class=\"provider-compare-label\">").Append(Html(row.Label)).AppendLine("</div>");
        sb.Append("                    <div class=\"provider-compare-value\">").Append(Html(row.Value)).AppendLine("</div>");
        if (!string.IsNullOrWhiteSpace(row.Subtitle)) {
            sb.Append("                    <div class=\"provider-compare-subtitle\">").Append(Html(row.Subtitle!)).AppendLine("</div>");
        }
        sb.AppendLine("                  </div>");
    }

    private static void AppendScopeCard(StringBuilder sb, UsageTelemetryOverviewInsightSection insight) {
        sb.AppendLine("              <article class=\"provider-feature-card\">");
        sb.Append("                <div class=\"provider-feature-kicker\">").Append(Html(insight.Title)).AppendLine("</div>");
        if (!string.IsNullOrWhiteSpace(insight.Headline)) {
            sb.Append("                <div class=\"provider-feature-headline\">").Append(Html(insight.Headline!)).AppendLine("</div>");
        }
        if (!string.IsNullOrWhiteSpace(insight.Note)) {
            sb.Append("                <div class=\"provider-feature-copy\">").Append(Html(insight.Note!)).AppendLine("</div>");
        }
        sb.AppendLine("                <div class=\"provider-feature-rows\">");
        foreach (var row in insight.Rows.Take(2)) {
            sb.AppendLine("                  <div class=\"provider-feature-row\">");
            sb.AppendLine("                    <div class=\"provider-feature-row-head\">");
            sb.Append("                      <div class=\"provider-feature-row-label\">").Append(Html(row.Label)).AppendLine("</div>");
            sb.Append("                      <div class=\"provider-feature-row-value\">").Append(Html(row.Value)).AppendLine("</div>");
            sb.AppendLine("                    </div>");
            if (!string.IsNullOrWhiteSpace(row.Subtitle)) {
                sb.Append("                    <div class=\"provider-feature-row-copy\">").Append(Html(row.Subtitle!)).AppendLine("</div>");
            }
            if (row.Ratio.HasValue) {
                sb.AppendLine("                    <div class=\"provider-feature-row-bar\">");
                sb.Append("                      <div class=\"provider-feature-row-fill\" style=\"width:")
                    .Append(Html(FormatRatioPercent(row.Ratio)))
                    .AppendLine("%;\"></div>");
                sb.AppendLine("                    </div>");
            }
            sb.AppendLine("                  </div>");
        }
        sb.AppendLine("                </div>");
        sb.AppendLine("                <div class=\"provider-badge-row\">");
        sb.AppendLine("                  <span class=\"provider-badge scope\">Personal activity</span>");
        sb.AppendLine("                  <span class=\"provider-badge scope\">Org repository impact</span>");
        sb.AppendLine("                </div>");
        sb.AppendLine("              </article>");
    }

    private static void AppendRecentRepositoriesCard(StringBuilder sb, UsageTelemetryOverviewInsightSection insight) {
        sb.AppendLine("              <article class=\"provider-feature-card\">");
        sb.Append("                <div class=\"provider-feature-kicker\">").Append(Html(insight.Title)).AppendLine("</div>");
        if (!string.IsNullOrWhiteSpace(insight.Headline)) {
            sb.Append("                <div class=\"provider-feature-headline\">").Append(Html(insight.Headline!)).AppendLine("</div>");
        }
        if (!string.IsNullOrWhiteSpace(insight.Note)) {
            sb.Append("                <div class=\"provider-feature-copy\">").Append(Html(insight.Note!)).AppendLine("</div>");
        }
        sb.AppendLine("                <div class=\"provider-feature-rows\">");
        foreach (var row in insight.Rows.Take(4)) {
            var (badgeClass, badgeLabel) = ResolveRepoHealthBadge(row.Value, row.Subtitle);
            sb.AppendLine("                  <div class=\"provider-feature-row\">");
            sb.AppendLine("                    <div class=\"provider-feature-row-head\">");
            sb.Append("                      <div class=\"provider-feature-row-label\">");
            if (!string.IsNullOrWhiteSpace(row.Href)) {
                sb.Append("<a class=\"inline-link\" href=\"").Append(Html(row.Href!)).Append("\" target=\"_blank\" rel=\"noopener\">")
                    .Append(Html(row.Label))
                    .Append("</a>");
            } else {
                sb.Append(Html(row.Label));
            }
            sb.AppendLine("</div>");
            sb.Append("                      <div class=\"provider-feature-row-value\">").Append(Html(row.Value)).AppendLine("</div>");
            sb.AppendLine("                    </div>");
            if (!string.IsNullOrWhiteSpace(row.Subtitle)) {
                sb.Append("                    <div class=\"provider-feature-row-copy\">").Append(Html(row.Subtitle!)).AppendLine("</div>");
            }
            sb.AppendLine("                    <div class=\"provider-badge-row\">");
            sb.Append("                      <span class=\"provider-badge ").Append(Html(badgeClass)).Append("\">").Append(Html(badgeLabel)).AppendLine("</span>");
            sb.AppendLine("                    </div>");
            sb.AppendLine("                  </div>");
        }
        sb.AppendLine("                </div>");
        sb.AppendLine("              </article>");
    }

    private static (string CssClass, string Label) ResolveRepoHealthBadge(string? yyyyMmDd, string? subtitle) {
        var hinted = ResolveRepoHealthBadgeFromSubtitle(subtitle);
        if (hinted.HasValue) {
            return hinted.Value;
        }

        if (DateTime.TryParseExact(
                yyyyMmDd,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed)) {
            var daysOld = (DateTime.UtcNow.Date - parsed.Date).Days;
            if (daysOld <= 14) {
                return ("active", "Active");
            }

            if (daysOld <= 60) {
                return ("warm", "Warm");
            }

            if (daysOld <= 365) {
                return ("established", "Established");
            }

            return ("dormant", "Dormant");
        }

        return ("dormant", "Unknown");
    }

    private static (string CssClass, string Label)? ResolveRepoHealthBadgeFromSubtitle(string? subtitle) {
        var normalized = HeatmapText.NormalizeOptionalText(subtitle);
        if (string.IsNullOrWhiteSpace(normalized)) {
            return null;
        }

        var text = normalized!;
        if (text.StartsWith("Rising ·", StringComparison.OrdinalIgnoreCase)) {
            return ("rising", "Rising");
        }

        if (text.StartsWith("Active ·", StringComparison.OrdinalIgnoreCase)) {
            return ("active", "Active");
        }

        if (text.StartsWith("Established ·", StringComparison.OrdinalIgnoreCase)) {
            return ("established", "Established");
        }

        if (text.StartsWith("Warm ·", StringComparison.OrdinalIgnoreCase)) {
            return ("warm", "Warm");
        }

        if (text.StartsWith("Dormant ·", StringComparison.OrdinalIgnoreCase)) {
            return ("dormant", "Dormant");
        }

        return null;
    }
}
