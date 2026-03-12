using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using IntelligenceX.Visualization.Heatmaps;

namespace IntelligenceX.Cli.GitHub;

internal static class GitHubOverviewSectionBuilder {
    private const int TrailingDays = 365;
    private static readonly string[] LegendColors = { "#e8e8e8", "#d6ecd3", "#9be9a8", "#40c463", "#216e39" };

    public static async Task<UsageTelemetryOverviewProviderSection> BuildAsync(string login, IReadOnlyList<string>? repositoryOwners = null) {
        if (string.IsNullOrWhiteSpace(login)) {
            throw new InvalidOperationException("GitHub user login is required.");
        }

        var endUtc = DateTimeOffset.UtcNow.Date;
        var startUtc = endUtc.AddDays(-(TrailingDays - 1));
        var calendar = await new GitHubContributionCalendarClient()
            .GetUserContributionCalendarAsync(login.Trim(), startUtc, endUtc)
            .ConfigureAwait(false);
        var currentYearStartUtc = new DateTimeOffset(endUtc.Year, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var previousYearStartUtc = currentYearStartUtc.AddYears(-1);
        var previousYearEndUtc = CreateComparableYearEnd(previousYearStartUtc.Year, endUtc.Month, endUtc.Day);
        var previousYearCalendar = await new GitHubContributionCalendarClient()
            .GetUserContributionCalendarAsync(login.Trim(), previousYearStartUtc, previousYearEndUtc)
            .ConfigureAwait(false);
        var owners = ResolveRepositoryOwners(login, repositoryOwners);
        var repositoryImpact = owners.Count == 0
            ? null
            : await new GitHubRepositoryImpactClient().GetRepositoryImpactAsync(owners).ConfigureAwait(false);

        var days = calendar.Days
            .Where(day => day.Date >= startUtc.Date && day.Date <= endUtc.Date)
            .OrderBy(day => day.Date)
            .ToArray();

        var activeDays = days.Count(static day => day.ContributionCount > 0);
        var activeWeeks = days
            .Where(static day => day.ContributionCount > 0)
            .Select(static day => StartOfWeek(day.Date, DayOfWeek.Sunday))
            .Distinct()
            .Count();
        var weekCount = ((endUtc.Date - StartOfWeek(startUtc.Date, DayOfWeek.Sunday)).Days / 7) + 1;
        var totalContributions = days.Sum(static day => (long)day.ContributionCount);
        var peakDay = days
            .OrderByDescending(static day => day.ContributionCount)
            .ThenBy(static day => day.Date)
            .FirstOrDefault();
        var monthlyUsage = BuildMonthlyUsage(days, startUtc.Date, endUtc.Date);
        var mostActiveMonth = monthlyUsage
            .OrderByDescending(static month => month.TotalValue)
            .ThenBy(static month => month.MonthUtc)
            .FirstOrDefault();
        var recentThirtyDays = days
            .Where(day => day.Date >= endUtc.Date.AddDays(-29))
            .Sum(static day => (long)day.ContributionCount);
        var (longestStreakDays, currentStreakDays) = ComputeStreaks(days);
        var currentYearDays = calendar.Days
            .Where(day => day.Date >= currentYearStartUtc.Date && day.Date <= endUtc.Date)
            .OrderBy(day => day.Date)
            .ToArray();
        var previousYearDays = previousYearCalendar.Days
            .Where(day => day.Date >= previousYearStartUtc.Date && day.Date <= previousYearEndUtc.Date)
            .OrderBy(day => day.Date)
            .ToArray();

        var metrics = new[] {
            new UsageTelemetryOverviewSectionMetric(
                "contributions",
                "Total contributions",
                FormatCompact(totalContributions),
                activeDays.ToString(CultureInfo.InvariantCulture) + " active day(s)",
                totalContributions > 0 ? 1d : 0d,
                "#216e39"),
            new UsageTelemetryOverviewSectionMetric(
                "active-days",
                "Active days",
                activeDays.ToString(CultureInfo.InvariantCulture),
                FormatPercent(activeDays, TrailingDays) + " of trailing year",
                ComputeRatio(activeDays, TrailingDays),
                "#40c463"),
            new UsageTelemetryOverviewSectionMetric(
                repositoryImpact is not null && repositoryImpact.TotalStars > 0 ? "owned-stars" : "repositories",
                repositoryImpact is not null && repositoryImpact.TotalStars > 0 ? "Owned stars" : "Repositories",
                repositoryImpact is not null && repositoryImpact.TotalStars > 0
                    ? FormatCompact(repositoryImpact.TotalStars)
                    : FormatCompact(Math.Max(0, calendar.Summary.RepositoryContributions)),
                repositoryImpact is not null && repositoryImpact.TotalStars > 0
                    ? FormatCompact(repositoryImpact.TotalRepositories) + " public repo(s) across " + repositoryImpact.Owners.Count.ToString(CultureInfo.InvariantCulture) + " owner(s)"
                    : BuildRepositorySubtitle(calendar.Summary, activeWeeks, weekCount),
                repositoryImpact is not null && repositoryImpact.TotalStars > 0
                    ? 1d
                    : ComputeRatio(activeWeeks, weekCount),
                "#9be9a8")
        };

        var composition = BuildContributionComposition(calendar.Summary, totalContributions);
        var spotlight = new[] {
            new UsageTelemetryOverviewCard(
                "most-active-month",
                "Most Active Month",
                mostActiveMonth?.MonthUtc.ToString("MMMM yyyy", CultureInfo.InvariantCulture) ?? "n/a",
                mostActiveMonth is null ? null : FormatCompact(mostActiveMonth.TotalValue) + " contributions"),
            new UsageTelemetryOverviewCard(
                "peak-day",
                "Peak Day",
                peakDay is null ? "n/a" : peakDay.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                peakDay is null ? null : peakDay.ContributionCount.ToString(CultureInfo.InvariantCulture) + " contributions"),
            new UsageTelemetryOverviewCard(
                "recent-thirty-days",
                "Recent Use (Last 30 Days)",
                FormatCompact(recentThirtyDays),
                "contributions"),
            new UsageTelemetryOverviewCard(
                "repository-footprint",
                "Repository Footprint",
                repositoryImpact is not null && repositoryImpact.TotalRepositories > 0
                    ? FormatCompact(repositoryImpact.TotalRepositories)
                    : BuildRepositoryFootprintValue(calendar.Summary),
                repositoryImpact is not null && repositoryImpact.TotalStars > 0
                    ? FormatCompact(repositoryImpact.TotalStars) + " stars · " + FormatCompact(repositoryImpact.TotalForks) + " forks"
                    : BuildRepositoryFootprintSubtitle(calendar.Summary)),
            new UsageTelemetryOverviewCard(
                "longest-streak",
                "Longest Streak",
                longestStreakDays.ToString(CultureInfo.InvariantCulture) + " days",
                currentStreakDays > 0 ? "Current: " + currentStreakDays.ToString(CultureInfo.InvariantCulture) + " days" : null),
            new UsageTelemetryOverviewCard(
                "current-streak",
                "Current Streak",
                currentStreakDays.ToString(CultureInfo.InvariantCulture) + " days",
                currentStreakDays > 0 ? "Live streak through " + endUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "No active streak today")
        };
        var additionalInsights = BuildAdditionalInsights(
            login.Trim(),
            endUtc.Date,
            currentYearDays,
            previousYearDays,
            repositoryImpact);

        var heatmap = BuildHeatmap(calendar, startUtc.Date, endUtc.Date);
        var subtitle = "@" + calendar.Login + " · " + startUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " -> " + endUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        return new UsageTelemetryOverviewProviderSection(
            key: "provider-github",
            providerId: "github",
            title: "GitHub",
            subtitle: subtitle,
            heatmap: heatmap,
            metrics: metrics,
            composition: composition,
            spotlightCards: spotlight,
            inputTokens: 0L,
            outputTokens: 0L,
            totalTokens: 0L,
            monthlyUsageTitle: "Monthly contributions",
            monthlyUsageUnitsLabel: "contributions",
            monthlyUsage: monthlyUsage,
            additionalInsights: additionalInsights,
            topModels: Array.Empty<UsageTelemetryOverviewTopModel>(),
            apiCostEstimate: null,
            mostUsedModel: null,
            recentModel: null,
            longestStreakDays: longestStreakDays,
            currentStreakDays: currentStreakDays,
            note: BuildGitHubNote(calendar, repositoryImpact));
    }

    private static HeatmapDocument BuildHeatmap(GitHubContributionCalendar calendar, DateTime startUtc, DateTime endUtc) {
        var heatmapDays = calendar.Days
            .Where(day => day.Date >= startUtc && day.Date <= endUtc)
            .OrderBy(static day => day.Date)
            .Select(static day => new HeatmapDay(
                date: day.Date,
                value: day.ContributionCount,
                level: MapContributionLevel(day.ContributionLevel),
                fillColor: day.Color,
                tooltip: $"{day.Date:yyyy-MM-dd}\n{day.ContributionCount} contribution(s)",
                breakdown: new Dictionary<string, double> { ["contributions"] = day.ContributionCount }))
            .ToArray();

        return new HeatmapDocument(
            title: $"@{calendar.Login} on GitHub",
            subtitle: calendar.ProfileUrl,
            palette: HeatmapPalette.GitHubLight(),
            sections: new[] { new HeatmapSection(calendar.Login, null, heatmapDays) },
            units: "contributions",
            weekStart: DayOfWeek.Sunday,
            showIntensityLegend: true,
            legendLowLabel: "Less",
            legendHighLabel: "More");
    }

    private static UsageTelemetryOverviewComposition? BuildContributionComposition(
        GitHubContributionCollectionSummary summary,
        long totalContributions) {
        if (summary is null) {
            return null;
        }

        var totalTypedContributions =
            summary.CommitContributions +
            summary.IssueContributions +
            summary.PullRequestContributions +
            summary.PullRequestReviewContributions +
            summary.RestrictedContributions;

        if (totalTypedContributions <= 0) {
            return null;
        }

        var items = new[] {
            CreateCompositionItem("commits", "Commits", summary.CommitContributions, totalTypedContributions, "#9be9a8"),
            CreateCompositionItem("pull-requests", "Pull Requests", summary.PullRequestContributions, totalTypedContributions, "#40c463"),
            CreateCompositionItem("reviews", "Reviews", summary.PullRequestReviewContributions, totalTypedContributions, "#216e39"),
            CreateCompositionItem("issues", "Issues", summary.IssueContributions, totalTypedContributions, "#d6ecd3"),
            CreateCompositionItem("restricted", "Restricted", summary.RestrictedContributions, totalTypedContributions, "#95a3b8")
        }.Where(static item => item is not null)
            .Cast<UsageTelemetryOverviewCompositionItem>()
            .OrderByDescending(static item => item.Ratio ?? 0d)
            .ToArray();

        return items.Length == 0
            ? null
            : new UsageTelemetryOverviewComposition(
                "Contribution mix",
                FormatCompact(totalContributions) + " contributions across this GitHub window",
                items);
    }

    private static UsageTelemetryOverviewCompositionItem? CreateCompositionItem(
        string key,
        string label,
        int count,
        int total,
        string color) {
        if (count <= 0 || total <= 0) {
            return null;
        }

        return new UsageTelemetryOverviewCompositionItem(
            key: key,
            label: label,
            value: FormatCompact(count),
            subtitle: FormatPercent(count, total),
            ratio: ComputeRatio(count, total),
            color: color);
    }

    private static string BuildRepositorySubtitle(
        GitHubContributionCollectionSummary summary,
        int activeWeeks,
        int weekCount) {
        if (summary.RepositoryContributions > 0) {
            return BuildRepositoryFootprintSubtitle(summary) ?? FormatCompact(summary.RepositoryContributions) + " touched repo(s)";
        }

        return FormatPercent(activeWeeks, weekCount) + " of week columns";
    }

    private static string BuildRepositoryFootprintValue(GitHubContributionCollectionSummary summary) {
        if (summary.RepositoryContributions > 0) {
            return FormatCompact(summary.RepositoryContributions);
        }

        var fallback = new[] {
            summary.RepositoriesWithCommits,
            summary.RepositoriesWithIssues,
            summary.RepositoriesWithPullRequests,
            summary.RepositoriesWithPullRequestReviews
        }.Max();

        return fallback > 0 ? FormatCompact(fallback) : "n/a";
    }

    private static string? BuildRepositoryFootprintSubtitle(GitHubContributionCollectionSummary summary) {
        var parts = new List<string>();
        if (summary.RepositoriesWithCommits > 0) {
            parts.Add(summary.RepositoriesWithCommits.ToString(CultureInfo.InvariantCulture) + " commit repo(s)");
        }
        if (summary.RepositoriesWithPullRequests > 0) {
            parts.Add(summary.RepositoriesWithPullRequests.ToString(CultureInfo.InvariantCulture) + " PR repo(s)");
        }
        if (summary.RepositoriesWithPullRequestReviews > 0) {
            parts.Add(summary.RepositoriesWithPullRequestReviews.ToString(CultureInfo.InvariantCulture) + " review repo(s)");
        }
        if (summary.RepositoriesWithIssues > 0) {
            parts.Add(summary.RepositoriesWithIssues.ToString(CultureInfo.InvariantCulture) + " issue repo(s)");
        }

        return parts.Count == 0
            ? null
            : string.Join(" · ", parts.Take(3));
    }

    private static IReadOnlyList<UsageTelemetryOverviewInsightSection> BuildAdditionalInsights(
        string login,
        DateTime endUtc,
        IReadOnlyList<GitHubContributionDay> currentYearDays,
        IReadOnlyList<GitHubContributionDay> previousYearDays,
        GitHubRepositoryImpactSummary? repositoryImpact) {
        var sections = new List<UsageTelemetryOverviewInsightSection>();
        var yearComparison = BuildYearComparisonInsightSection(endUtc, currentYearDays, previousYearDays);
        if (yearComparison is not null) {
            sections.Add(yearComparison);
        }

        var repositoryImpactSections = BuildRepositoryImpactInsights(login, repositoryImpact);
        if (repositoryImpactSections.Count > 0) {
            sections.AddRange(repositoryImpactSections);
        }

        return sections;
    }

    private static UsageTelemetryOverviewInsightSection? BuildYearComparisonInsightSection(
        DateTime endUtc,
        IReadOnlyList<GitHubContributionDay> currentYearDays,
        IReadOnlyList<GitHubContributionDay> previousYearDays) {
        var currentSnapshot = BuildYearSnapshot(endUtc.Year, currentYearDays);
        var previousSnapshot = BuildYearSnapshot(endUtc.Year - 1, previousYearDays);
        if (currentSnapshot.TotalContributions <= 0 && previousSnapshot.TotalContributions <= 0) {
            return null;
        }

        var delta = previousSnapshot.TotalContributions <= 0
            ? (double?)null
            : ((currentSnapshot.TotalContributions - previousSnapshot.TotalContributions) / (double)previousSnapshot.TotalContributions) * 100d;
        var headline = delta.HasValue
            ? $"{endUtc.Year} YTD {FormatSignedPercent(delta.Value)} vs {endUtc.Year - 1}"
            : $"{endUtc.Year} YTD baseline";
        var note = $"Compared through {endUtc:MM-dd}; current year is year-to-date.";

        var peakBase = Math.Max(
            1L,
            Math.Max(currentSnapshot.PeakValue, previousSnapshot.PeakValue));
        var rows = new[] {
            new UsageTelemetryOverviewInsightRow(
                label: currentSnapshot.Label,
                value: FormatCompact(currentSnapshot.TotalContributions) + " contributions",
                subtitle: currentSnapshot.ActiveDays.ToString(CultureInfo.InvariantCulture) + " active day(s) · longest streak " + currentSnapshot.LongestStreakDays.ToString(CultureInfo.InvariantCulture) + " day(s)"
                          + (currentSnapshot.PeakDate is null ? string.Empty : " · peak " + currentSnapshot.PeakDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                ratio: currentSnapshot.TotalContributions / (double)peakBase),
            new UsageTelemetryOverviewInsightRow(
                label: previousSnapshot.Label,
                value: FormatCompact(previousSnapshot.TotalContributions) + " contributions",
                subtitle: previousSnapshot.ActiveDays.ToString(CultureInfo.InvariantCulture) + " active day(s) · longest streak " + previousSnapshot.LongestStreakDays.ToString(CultureInfo.InvariantCulture) + " day(s)"
                          + (previousSnapshot.PeakDate is null ? string.Empty : " · peak " + previousSnapshot.PeakDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                ratio: previousSnapshot.TotalContributions / (double)peakBase)
        };

        return new UsageTelemetryOverviewInsightSection(
            key: "github-year-comparison",
            title: "Year over year",
            headline: headline,
            note: note,
            rows: rows);
    }

    private static IReadOnlyList<UsageTelemetryOverviewInsightSection> BuildRepositoryImpactInsights(
        string login,
        GitHubRepositoryImpactSummary? repositoryImpact) {
        if (repositoryImpact is null || repositoryImpact.Owners.Count == 0) {
            return Array.Empty<UsageTelemetryOverviewInsightSection>();
        }

        var personalOwner = repositoryImpact.Owners.FirstOrDefault(owner =>
            string.Equals(owner.Owner, login, StringComparison.OrdinalIgnoreCase));
        var orgOwners = repositoryImpact.Owners
            .Where(owner => !string.Equals(owner.Owner, login, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var sections = new List<UsageTelemetryOverviewInsightSection>();

        if (personalOwner is not null || orgOwners.Length > 0) {
            var splitRows = new List<UsageTelemetryOverviewInsightRow>();
            var totalStars = Math.Max(1, repositoryImpact.TotalStars);
            if (personalOwner is not null) {
                splitRows.Add(new UsageTelemetryOverviewInsightRow(
                    label: "Personal scope",
                    value: FormatCompact(personalOwner.TotalStars) + " stars",
                    subtitle: personalOwner.Owner + " · " + FormatCompact(personalOwner.RepositoryCount) + " repo(s) · " + FormatCompact(personalOwner.TotalForks) + " forks",
                    ratio: personalOwner.TotalStars / (double)totalStars));
            }

            if (orgOwners.Length > 0) {
                var orgStars = orgOwners.Sum(static owner => owner.TotalStars);
                var orgRepos = orgOwners.Sum(static owner => owner.RepositoryCount);
                var orgForks = orgOwners.Sum(static owner => owner.TotalForks);
                splitRows.Add(new UsageTelemetryOverviewInsightRow(
                    label: "Org / owner scope",
                    value: FormatCompact(orgStars) + " stars",
                    subtitle: string.Join(", ", orgOwners.Select(static owner => owner.Owner)) + " · " + FormatCompact(orgRepos) + " repo(s) · " + FormatCompact(orgForks) + " forks",
                    ratio: orgStars / (double)totalStars));
            }

            sections.Add(new UsageTelemetryOverviewInsightSection(
                key: "github-scope-split",
                title: "Profile vs owner scope",
                headline: FormatCompact(repositoryImpact.TotalStars) + " stars across selected scope",
                note: "Personal profile activity and owned-repository impact are tracked separately here.",
                rows: splitRows));
        }

        var ownerTotal = Math.Max(1, repositoryImpact.TotalStars);
        var ownerRows = repositoryImpact.Owners
            .OrderByDescending(static owner => owner.TotalStars)
            .ThenBy(static owner => owner.Owner, StringComparer.OrdinalIgnoreCase)
            .Select(owner => new UsageTelemetryOverviewInsightRow(
                label: owner.Owner,
                value: FormatCompact(owner.TotalStars) + " stars",
                subtitle: FormatCompact(owner.RepositoryCount) + " repo(s) · " + FormatCompact(owner.TotalForks) + " forks" +
                          (owner.TopRepository is null ? string.Empty : " · Top: " + owner.TopRepository.NameWithOwner),
                ratio: owner.TotalStars / (double)ownerTotal))
            .ToArray();

        sections.Add(new UsageTelemetryOverviewInsightSection(
            key: "github-owner-impact",
            title: "Owned repository impact",
            headline: FormatCompact(repositoryImpact.TotalStars) + " stars · " + FormatCompact(repositoryImpact.TotalForks) + " forks",
            note: FormatCompact(repositoryImpact.TotalRepositories) + " public repo(s) across " + repositoryImpact.Owners.Count.ToString(CultureInfo.InvariantCulture) + " owner scope(s)",
            rows: ownerRows));

        foreach (var owner in repositoryImpact.Owners
                     .OrderByDescending(static owner => owner.TotalStars)
                     .ThenBy(static owner => owner.Owner, StringComparer.OrdinalIgnoreCase)) {
            var ownerTopRepositoryTotal = Math.Max(1, owner.TopRepository?.Stars ?? 0);
            var ownerRepositoryRows = owner.Repositories
                .OrderByDescending(static repo => repo.Stars)
                .ThenByDescending(static repo => repo.Forks)
                .ThenBy(static repo => repo.NameWithOwner, StringComparer.OrdinalIgnoreCase)
                .Take(6)
                .Select(repo => new UsageTelemetryOverviewInsightRow(
                    label: repo.NameWithOwner,
                    value: FormatCompact(repo.Stars) + " stars",
                    subtitle: FormatCompact(repo.Forks) + " forks" +
                              (string.IsNullOrWhiteSpace(repo.PrimaryLanguage) ? string.Empty : " · " + repo.PrimaryLanguage) +
                              (TryParseGitHubTimestamp(repo.PushedAt) is { } pushedAt
                                  ? " · pushed " + pushedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                                  : string.Empty),
                    ratio: repo.Stars / (double)ownerTopRepositoryTotal,
                    href: repo.Url))
                .ToArray();

            sections.Add(new UsageTelemetryOverviewInsightSection(
                key: "github-owner-" + SanitizeKey(owner.Owner),
                title: owner.Owner,
                headline: FormatCompact(owner.TotalStars) + " stars · " + FormatCompact(owner.TotalForks) + " forks",
                note: FormatCompact(owner.RepositoryCount) + " public repo(s) in this owner scope",
                rows: ownerRepositoryRows));
        }

        var languageRows = repositoryImpact.Owners
            .SelectMany(static owner => owner.Repositories)
            .GroupBy(static repo => string.IsNullOrWhiteSpace(repo.PrimaryLanguage) ? "Unknown" : repo.PrimaryLanguage!, StringComparer.OrdinalIgnoreCase)
            .Select(group => new {
                Language = group.Key,
                RepositoryCount = group.Count(),
                Stars = group.Sum(static repo => repo.Stars),
                Forks = group.Sum(static repo => repo.Forks)
            })
            .OrderByDescending(static entry => entry.Stars)
            .ThenByDescending(static entry => entry.RepositoryCount)
            .ThenBy(static entry => entry.Language, StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToArray();

        if (languageRows.Length > 0) {
            var topLanguageTotal = Math.Max(1, languageRows[0].Stars);
            sections.Add(new UsageTelemetryOverviewInsightSection(
                key: "github-top-languages",
                title: "Top languages",
                headline: languageRows[0].Language,
                note: "Ranked by stars across the selected owner scope.",
                rows: languageRows.Select(entry => new UsageTelemetryOverviewInsightRow(
                    label: entry.Language,
                    value: FormatCompact(entry.Stars) + " stars",
                    subtitle: FormatCompact(entry.RepositoryCount) + " repo(s) · " + FormatCompact(entry.Forks) + " forks",
                    ratio: entry.Stars / (double)topLanguageTotal)).ToArray()));
        }

        var recentRepositories = repositoryImpact.Owners
            .SelectMany(static owner => owner.Repositories)
            .Select(repo => new {
                Repository = repo,
                ParsedPushedAt = TryParseGitHubTimestamp(repo.PushedAt)
            })
            .Where(static entry => entry.ParsedPushedAt.HasValue)
            .OrderByDescending(static entry => entry.ParsedPushedAt)
            .ThenByDescending(static entry => entry.Repository.Stars)
            .ThenBy(static entry => entry.Repository.NameWithOwner, StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToArray();

        if (recentRepositories.Length > 0) {
            sections.Add(new UsageTelemetryOverviewInsightSection(
                key: "github-recent-repositories",
                title: "Recent repository activity",
                headline: recentRepositories[0].Repository.NameWithOwner,
                note: "Public repositories sorted by latest push timestamp across the selected owner scope.",
                rows: recentRepositories.Select(entry => new UsageTelemetryOverviewInsightRow(
                    label: entry.Repository.NameWithOwner,
                    value: entry.ParsedPushedAt!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    subtitle: BuildRecentRepositorySubtitle(entry.Repository, entry.ParsedPushedAt) +
                              " · " + FormatCompact(entry.Repository.Stars) + " stars · " + FormatCompact(entry.Repository.Forks) + " forks" +
                              (string.IsNullOrWhiteSpace(entry.Repository.PrimaryLanguage) ? string.Empty : " · " + entry.Repository.PrimaryLanguage),
                    ratio: recentRepositories.Length <= 1 ? 1d : 1d - (Array.IndexOf(recentRepositories, entry) / (double)Math.Max(1, recentRepositories.Length - 1)),
                    href: entry.Repository.Url)).ToArray()));
        }

        var topRepositoryTotal = Math.Max(1, repositoryImpact.TopRepositories.FirstOrDefault()?.Stars ?? 0);
        var topRepositoryRows = repositoryImpact.TopRepositories
            .Select(repo => new UsageTelemetryOverviewInsightRow(
                label: repo.NameWithOwner,
                value: FormatCompact(repo.Stars) + " stars",
                subtitle: FormatCompact(repo.Forks) + " forks" +
                          (string.IsNullOrWhiteSpace(repo.PrimaryLanguage) ? string.Empty : " · " + repo.PrimaryLanguage),
                ratio: repo.Stars / (double)topRepositoryTotal,
                href: repo.Url))
            .ToArray();

        sections.Add(new UsageTelemetryOverviewInsightSection(
            key: "github-top-repositories",
            title: "Top repositories",
            headline: repositoryImpact.TopRepositories.Count == 0 ? null : repositoryImpact.TopRepositories[0].NameWithOwner,
            note: repositoryImpact.TopRepositories.Count == 0 ? null : "Ranked by stars across the selected owner scope",
            rows: topRepositoryRows));

        var topRepositoriesByForks = repositoryImpact.Owners
            .SelectMany(static owner => owner.Repositories)
            .OrderByDescending(static repo => repo.Forks)
            .ThenByDescending(static repo => repo.Stars)
            .ThenBy(static repo => repo.NameWithOwner, StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToArray();

        if (topRepositoriesByForks.Length > 0) {
            var maxForks = Math.Max(1, topRepositoriesByForks[0].Forks);
            sections.Add(new UsageTelemetryOverviewInsightSection(
                key: "github-top-repositories-forks",
                title: "Top repositories by forks",
                headline: topRepositoriesByForks[0].NameWithOwner,
                note: "Ranked by forks across the selected owner scope",
                rows: topRepositoriesByForks.Select(repo => new UsageTelemetryOverviewInsightRow(
                    label: repo.NameWithOwner,
                    value: FormatCompact(repo.Forks) + " forks",
                    subtitle: FormatCompact(repo.Stars) + " stars" +
                              (string.IsNullOrWhiteSpace(repo.PrimaryLanguage) ? string.Empty : " · " + repo.PrimaryLanguage) +
                              (TryParseGitHubTimestamp(repo.PushedAt) is { } pushedAt ? " · pushed " + pushedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : string.Empty),
                    ratio: repo.Forks / (double)maxForks,
                    href: repo.Url)).ToArray()));
        }

        var topRepositoriesByHealth = repositoryImpact.Owners
            .SelectMany(static owner => owner.Repositories)
            .Select(repo => new {
                Repository = repo,
                ParsedPushedAt = TryParseGitHubTimestamp(repo.PushedAt),
                HealthScore = ComputeRepositoryHealthScore(repo, TryParseGitHubTimestamp(repo.PushedAt))
            })
            .OrderByDescending(static entry => entry.HealthScore)
            .ThenByDescending(static entry => entry.Repository.Stars)
            .ThenBy(static entry => entry.Repository.NameWithOwner, StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToArray();

        if (topRepositoriesByHealth.Length > 0) {
            var maxHealth = Math.Max(1d, topRepositoriesByHealth[0].HealthScore);
            sections.Add(new UsageTelemetryOverviewInsightSection(
                key: "github-top-repositories-health",
                title: "Top repositories by health",
                headline: topRepositoriesByHealth[0].Repository.NameWithOwner,
                note: "Ranked by recency plus repository impact across the selected owner scope",
                rows: topRepositoriesByHealth.Select(entry => new UsageTelemetryOverviewInsightRow(
                    label: entry.Repository.NameWithOwner,
                    value: BuildRecentRepositorySubtitle(entry.Repository, entry.ParsedPushedAt),
                    subtitle: FormatCompact(entry.Repository.Stars) + " stars · " + FormatCompact(entry.Repository.Forks) + " forks" +
                              (entry.ParsedPushedAt.HasValue ? " · pushed " + entry.ParsedPushedAt.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : string.Empty),
                    ratio: entry.HealthScore / maxHealth,
                    href: entry.Repository.Url)).ToArray()));
        }

        return sections;
    }

    private static string BuildRecentRepositorySubtitle(
        GitHubRepositoryImpactRepository repository,
        DateTimeOffset? pushedAtUtc) {
        return ClassifyRepositoryHealth(repository, pushedAtUtc) switch {
            GitHubRepositoryHealth.Rising => "Rising",
            GitHubRepositoryHealth.Active => "Active",
            GitHubRepositoryHealth.Established => "Established",
            GitHubRepositoryHealth.Warm => "Warm",
            GitHubRepositoryHealth.Dormant => "Dormant",
            _ => "Unknown"
        };
    }

    private static double ComputeRepositoryHealthScore(
        GitHubRepositoryImpactRepository repository,
        DateTimeOffset? pushedAtUtc) {
        var impactScore = repository.Stars + (repository.Forks * 2);
        if (!pushedAtUtc.HasValue) {
            return impactScore;
        }

        var daysOld = Math.Max(0, (DateTimeOffset.UtcNow.Date - pushedAtUtc.Value.Date).Days);
        var recencyBoost = daysOld switch {
            <= 14 => 1200d,
            <= 30 => 800d,
            <= 90 => 400d,
            <= 180 => 180d,
            <= 365 => 80d,
            _ => 0d
        };

        return impactScore + recencyBoost;
    }

    private static GitHubRepositoryHealth ClassifyRepositoryHealth(
        GitHubRepositoryImpactRepository repository,
        DateTimeOffset? pushedAtUtc) {
        if (!pushedAtUtc.HasValue) {
            return GitHubRepositoryHealth.Unknown;
        }

        var daysOld = Math.Max(0, (DateTimeOffset.UtcNow.Date - pushedAtUtc.Value.Date).Days);
        var impactScore = repository.Stars + (repository.Forks * 2);

        if (daysOld <= 14 && impactScore >= 100) {
            return GitHubRepositoryHealth.Rising;
        }

        if (daysOld <= 14) {
            return GitHubRepositoryHealth.Active;
        }

        if (impactScore >= 500 && daysOld <= 180) {
            return GitHubRepositoryHealth.Established;
        }

        if (daysOld <= 90) {
            return GitHubRepositoryHealth.Warm;
        }

        if (impactScore >= 1000 && daysOld <= 365) {
            return GitHubRepositoryHealth.Established;
        }

        return GitHubRepositoryHealth.Dormant;
    }

    private static DateTimeOffset? TryParseGitHubTimestamp(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return null;
        }

        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;
    }

    private static string? BuildGitHubNote(GitHubContributionCalendar calendar, GitHubRepositoryImpactSummary? repositoryImpact) {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(calendar.ProfileUrl)) {
            parts.Add(calendar.ProfileUrl!);
        }

        if (repositoryImpact is not null && repositoryImpact.Owners.Count > 0) {
            parts.Add("Owner scope: " + string.Join(", ", repositoryImpact.Owners.Select(static owner => owner.Owner)));
            parts.Add(FormatCompact(repositoryImpact.TotalStars) + " stars across " + FormatCompact(repositoryImpact.TotalRepositories) + " public repo(s)");
        }

        var repositoryFootprint = BuildRepositoryFootprintSubtitle(calendar.Summary);
        if (!string.IsNullOrWhiteSpace(repositoryFootprint)) {
            parts.Add("Repository footprint: " + repositoryFootprint);
        }

        if (calendar.Summary.RestrictedContributions > 0) {
            parts.Add(FormatCompact(calendar.Summary.RestrictedContributions) + " restricted contribution(s)");
        }

        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    private static IReadOnlyList<string> ResolveRepositoryOwners(string login, IReadOnlyList<string>? repositoryOwners) {
        return (repositoryOwners ?? Array.Empty<string>())
            .Append(login)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<UsageTelemetryOverviewMonthlyUsage> BuildMonthlyUsage(
        IReadOnlyList<GitHubContributionDay> days,
        DateTime startUtc,
        DateTime endUtc) {
        var monthLookup = days
            .GroupBy(day => new DateTime(day.Date.Year, day.Date.Month, 1, 0, 0, 0, DateTimeKind.Utc))
            .ToDictionary(
                static group => group.Key,
                group => new UsageTelemetryOverviewMonthlyUsage(
                    group.Key,
                    group.Sum(static day => (long)day.ContributionCount),
                    group.Count(static day => day.ContributionCount > 0)));

        var values = new List<UsageTelemetryOverviewMonthlyUsage>();
        var cursor = new DateTime(startUtc.Year, startUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var endMonth = new DateTime(endUtc.Year, endUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        while (cursor <= endMonth) {
            values.Add(monthLookup.TryGetValue(cursor, out var month)
                ? month
                : new UsageTelemetryOverviewMonthlyUsage(cursor, 0L, 0));
            cursor = cursor.AddMonths(1);
        }

        return values;
    }

    private static (int LongestStreakDays, int CurrentStreakDays) ComputeStreaks(IReadOnlyList<GitHubContributionDay> days) {
        var activeDates = days
            .Where(static day => day.ContributionCount > 0)
            .Select(static day => day.Date.Date)
            .OrderBy(static date => date)
            .Distinct()
            .ToArray();

        if (activeDates.Length == 0) {
            return (0, 0);
        }

        var longest = 1;
        var current = 1;
        for (var i = 1; i < activeDates.Length; i++) {
            if ((activeDates[i] - activeDates[i - 1]).Days == 1) {
                current++;
            } else {
                if (current > longest) {
                    longest = current;
                }
                current = 1;
            }
        }

        if (current > longest) {
            longest = current;
        }

        var trailing = 1;
        for (var i = activeDates.Length - 1; i > 0; i--) {
            if ((activeDates[i] - activeDates[i - 1]).Days == 1) {
                trailing++;
            } else {
                break;
            }
        }

        var latestActiveDay = activeDates[activeDates.Length - 1];
        var currentStreak = latestActiveDay == DateTime.UtcNow.Date ? trailing : 0;
        return (longest, currentStreak);
    }

    private static DateTime StartOfWeek(DateTime date, DayOfWeek weekStart) {
        var diff = (7 + (date.DayOfWeek - weekStart)) % 7;
        return date.AddDays(-diff).Date;
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

    private static string FormatPercent(int value, int total) {
        if (value <= 0 || total <= 0) {
            return "0%";
        }

        return (Math.Min(1d, value / (double)total) * 100d).ToString("0.#", CultureInfo.InvariantCulture) + "%";
    }

    private static double? ComputeRatio(int value, int total) {
        if (value <= 0 || total <= 0) {
            return 0d;
        }

        return Math.Min(1d, value / (double)total);
    }

    private static int MapContributionLevel(string? level) {
        return level switch {
            "FIRST_QUARTILE" => 1,
            "SECOND_QUARTILE" => 2,
            "THIRD_QUARTILE" => 3,
            "FOURTH_QUARTILE" => 4,
            _ => 0
        };
    }

    private static DateTimeOffset CreateComparableYearEnd(int year, int month, int day) {
        var clampedDay = Math.Min(day, DateTime.DaysInMonth(year, month));
        return new DateTimeOffset(year, month, clampedDay, 0, 0, 0, TimeSpan.Zero);
    }

    private static GitHubYearSnapshot BuildYearSnapshot(int year, IReadOnlyList<GitHubContributionDay> days) {
        var total = days.Sum(static day => (long)day.ContributionCount);
        var activeDays = days.Count(static day => day.ContributionCount > 0);
        var peakDay = days
            .OrderByDescending(static day => day.ContributionCount)
            .ThenBy(static day => day.Date)
            .FirstOrDefault();
        var (longestStreakDays, _) = ComputeStreaks(days);
        return new GitHubYearSnapshot(
            year + (DateTime.UtcNow.Year == year ? " YTD" : string.Empty),
            total,
            activeDays,
            longestStreakDays,
            peakDay?.Date,
            peakDay?.ContributionCount ?? 0);
    }

    private static string FormatSignedPercent(double value) {
        var prefix = value >= 0d ? "+" : string.Empty;
        return prefix + value.ToString("0.#", CultureInfo.InvariantCulture) + "%";
    }

    private static string SanitizeKey(string value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return "unknown";
        }

        var chars = value.Trim()
            .ToLowerInvariant()
            .Select(static ch => char.IsLetterOrDigit(ch) ? ch : '-')
            .ToArray();
        return string.Join(string.Empty, new string(chars).Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries));
    }

    private sealed record GitHubYearSnapshot(
        string Label,
        long TotalContributions,
        int ActiveDays,
        int LongestStreakDays,
        DateTime? PeakDate,
        int PeakValue);

    private enum GitHubRepositoryHealth {
        Unknown,
        Rising,
        Active,
        Established,
        Warm,
        Dormant
    }
}
