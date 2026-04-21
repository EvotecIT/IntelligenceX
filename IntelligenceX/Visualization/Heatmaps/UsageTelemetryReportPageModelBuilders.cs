using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using IntelligenceX.Json;
using IntelligenceX.Telemetry.Git;
using IntelligenceX.Telemetry.GitHub;
using IntelligenceX.Telemetry.Usage;

namespace IntelligenceX.Visualization.Heatmaps;

internal static class UsageTelemetryReportPageModelBuilders {
    public static UsageTelemetryOverviewPageModel BuildOverview(
        UsageTelemetryOverviewDocument overview,
        GitHubObservabilitySummaryData? gitHubObservabilitySummary = null,
        GitCodeChurnSummaryData? gitCodeChurnSummary = null) {
        if (overview is null) {
            throw new ArgumentNullException(nameof(overview));
        }

        var providerDailySeries = BuildProviderDailySeries(overview);
        var churnUsageCorrelation = BuildCodeUsageCorrelationSection(
            GitCodeUsageCorrelationSummaryBuilder.BuildFromDailySeries(
                gitCodeChurnSummary,
                providerDailySeries,
                overview.Units));
        var gitHubLocalAlignmentSummary = GitHubLocalActivityCorrelationSummaryBuilder.BuildFromDailySeries(
            gitCodeChurnSummary,
            BuildProviderDailySeries(overview, excludeGitHub: true),
            gitHubObservabilitySummary);
        var gitHubLocalAlignment = BuildGitHubWatchedLocalAlignmentInsight(gitHubLocalAlignmentSummary);
        var gitHubRepoClusterSummary = GitHubRepositoryClusterSummaryBuilder.Build(gitHubObservabilitySummary, gitHubLocalAlignmentSummary);
        var gitHubRepoClusters = BuildGitHubWatchedRepoClusterInsight(gitHubRepoClusterSummary);
        var conversationPulse = BuildConversationPulseSection(overview.Metadata);

        var sectionSwitches = new List<UsageTelemetrySectionSwitchModel>();
        if (overview.ProviderSections.Count > 1) {
            sectionSwitches.Add(new UsageTelemetrySectionSwitchModel("all", "All sections"));
            sectionSwitches.AddRange(overview.ProviderSections.Select(static section =>
                new UsageTelemetrySectionSwitchModel(section.ProviderId, section.Title)));
        }

        var sections = overview.ProviderSections
            .Select(section => BuildSection(section, gitHubObservabilitySummary, gitHubLocalAlignmentSummary))
            .ToArray();

        var supportingBreakdowns = overview.Heatmaps
            .Select((heatmap, index) => new UsageTelemetrySupportingBreakdownModel(
                heatmap.Key,
                UsageTelemetryBreakdownFileNames.ResolveFileStem(heatmap.Key, heatmap.Label),
                heatmap.Label,
                UsageTelemetryBreakdownDisplayText.FormatSummaryHint(heatmap.Label, heatmap.Document.Subtitle),
                index == 0,
                BuildBreakdownSummary(
                    heatmap.Key,
                    UsageTelemetryBreakdownDisplayText.FormatSummaryHint(heatmap.Label, heatmap.Document.Subtitle),
                    heatmap.Document)))
            .ToArray();

        var bootstrap = UsageTelemetryReportAppearanceDefaults.AddBootstrap(new JsonObject())
            .Add("defaultSectionTarget", "all")
            .Add("defaultSupportingMode", "preview");

        return new UsageTelemetryOverviewPageModel(
            Title: overview.Title,
            Subtitle: overview.Subtitle,
            HeroStats: new[] {
                new UsageTelemetryHeroStatModel("Range", FormatRange(overview.Summary.StartDayUtc, overview.Summary.EndDayUtc)),
                new UsageTelemetryHeroStatModel("Sections", overview.ProviderSections.Count.ToString(CultureInfo.InvariantCulture)),
                new UsageTelemetryHeroStatModel("Telemetry Tokens", FormatCompact(overview.Summary.TotalValue))
            },
            CodeChurn: BuildCodeChurnSection(gitCodeChurnSummary),
            ChurnUsageCorrelation: churnUsageCorrelation,
            GitHubLocalAlignment: BuildGitHubLocalAlignmentSection(gitHubLocalAlignmentSummary, gitHubLocalAlignment),
            GitHubRepoClusters: BuildGitHubRepoClusterSection(gitHubRepoClusterSummary, gitHubRepoClusters),
            ConversationPulse: conversationPulse,
            SectionSwitches: sectionSwitches,
            Sections: sections,
            SupportingBreakdowns: supportingBreakdowns,
            Diagnostics: UsageTelemetryReportDiagnosticsBuilder.Build(overview.Summary, overview.Metadata, overview.ProviderSections.Count),
            BootstrapJson: JsonLite.Serialize(JsonValue.From(bootstrap)),
            Footnote: "Built from the provider-neutral telemetry ledger, so the same report format can work for Codex, Claude, IX-native usage, and future compatible providers.");
    }

    public static UsageTelemetryBreakdownPageModel BuildBreakdown(
        string reportTitle,
        string breakdownKey,
        string breakdownLabel,
        string? subtitle,
        HeatmapDocument document,
        UsageSummarySnapshot? summary = null,
        JsonObject? metadata = null,
        int providerSectionsCount = 0) {
        var safeTitle = string.IsNullOrWhiteSpace(reportTitle) ? "Usage Overview" : reportTitle.Trim();
        var safeLabel = string.IsNullOrWhiteSpace(breakdownLabel) ? "Breakdown" : breakdownLabel.Trim();
        var safeKey = string.IsNullOrWhiteSpace(breakdownKey) ? "breakdown" : breakdownKey.Trim();
        var summaryHint = UsageTelemetryBreakdownDisplayText.FormatSummaryHint(safeLabel, subtitle);

        var bootstrap = UsageTelemetryReportAppearanceDefaults.AddBootstrap(new JsonObject());

        return new UsageTelemetryBreakdownPageModel(
            ReportTitle: safeTitle,
            BreakdownKey: safeKey,
            FileStem: UsageTelemetryBreakdownFileNames.ResolveFileStem(safeKey, safeLabel),
            BreakdownLabel: safeLabel,
            SummaryHint: summaryHint,
            Diagnostics: summary is null ? null : UsageTelemetryReportDiagnosticsBuilder.Build(summary, metadata, providerSectionsCount),
            BootstrapJson: JsonLite.Serialize(JsonValue.From(bootstrap)),
            Summary: BuildBreakdownSummary(safeKey, summaryHint, document));
    }

    private static UsageTelemetryOverviewSectionPageModel BuildSection(
        UsageTelemetryOverviewProviderSection section,
        GitHubObservabilitySummaryData? gitHubObservabilitySummary,
        GitHubLocalActivityCorrelationSummaryData? gitHubLocalAlignmentSummary) {
        var isGitHub = IsGitHubSection(section);
        var flags = new UsageTelemetryOverviewSectionFlags(
            IsGitHub: isGitHub,
            HasActivity: HasActivityData(section),
            HasMonthly: section.MonthlyUsage.Count > 0,
            HasModels: section.MostUsedModel is not null || section.RecentModel is not null || section.TopModels.Count > 0,
            HasPricing: section.ApiCostEstimate is not null,
            HasComposition: section.Composition is not null && section.Composition.Items.Count > 0,
            HasAdditionalInsights: section.AdditionalInsights.Count > 0,
            UseSummaryGrid: !isGitHub && (section.Composition is not null && section.Composition.Items.Count > 0 || section.MonthlyUsage.Count > 0 || section.ApiCostEstimate is not null || section.MostUsedModel is not null || section.RecentModel is not null || section.TopModels.Count > 0 || section.AdditionalInsights.Count > 0));
        var datasetTabs = BuildDatasetTabs(flags);

        return new UsageTelemetryOverviewSectionPageModel(
            ProviderSectionId: "provider-section-" + section.ProviderId.Trim().ToLowerInvariant(),
            ProviderId: section.ProviderId,
            Title: section.Title,
            Subtitle: section.Subtitle,
            Section: section,
            Flags: flags,
            DatasetTabs: datasetTabs,
            AccentColors: ResolveProviderAccentColors(section.ProviderId),
            Diagnostics: UsageTelemetryReportDiagnosticsBuilder.BuildForProviderSection(section),
            HealthAccountLabels: section.AccountLabels,
            HealthInsights: BuildProviderHealthInsights(section),
            GitHub: isGitHub ? BuildGitHubSection(section, gitHubObservabilitySummary, gitHubLocalAlignmentSummary) : null);
    }

    private static bool HasActivityData(UsageTelemetryOverviewProviderSection section) {
        return section.Heatmap.Sections.Any(static entry => entry.Days.Count > 0);
    }

    private static bool IsGitHubSection(UsageTelemetryOverviewProviderSection section) {
        return string.Equals(section.ProviderId, "github", StringComparison.OrdinalIgnoreCase);
    }

    private static UsageTelemetryProviderAccentColors ResolveProviderAccentColors(string providerId) {
        var appearance = UsageTelemetryProviderCatalog.ResolveAppearance(providerId);
        return new UsageTelemetryProviderAccentColors(appearance.Input, appearance.Output, appearance.Total, appearance.Other);
    }

    private static UsageTelemetryGitHubSectionPageModel BuildGitHubSection(
        UsageTelemetryOverviewProviderSection section,
        GitHubObservabilitySummaryData? gitHubObservabilitySummary,
        GitHubLocalActivityCorrelationSummaryData? gitHubLocalAlignmentSummary) {
        var ownerSections = section.AdditionalInsights
            .Where(static insight =>
                insight.Key.StartsWith("github-owner-", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(insight.Key, "github-owner-impact", StringComparison.OrdinalIgnoreCase))
            .OrderBy(static insight => insight.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var topRepositoriesByForks = FindInsight(section, "github-top-repositories-forks");
        var topRepositoriesByHealth = FindInsight(section, "github-top-repositories-health");
        var topLanguages = FindInsight(section, "github-top-languages");
        var watchedRepositories = BuildGitHubWatchedRepositoriesInsight(gitHubObservabilitySummary);
        var watchedCorrelations = BuildGitHubWatchedCorrelationInsight(gitHubObservabilitySummary);
        var watchedStarCorrelations = BuildGitHubWatchedStarCorrelationInsight(gitHubObservabilitySummary);
        var watchedRepoClusters = BuildGitHubWatchedRepoClusterInsight(
            GitHubRepositoryClusterSummaryBuilder.Build(gitHubObservabilitySummary, gitHubLocalAlignmentSummary));
        var watchedStargazerAudience = BuildGitHubWatchedStargazerAudienceInsight(gitHubObservabilitySummary);
        var watchedForkNetwork = BuildGitHubWatchedForkNetworkInsight(gitHubObservabilitySummary);
        var watchedForkMomentum = BuildGitHubWatchedForkMomentumInsight(gitHubObservabilitySummary);
        var gitHubLocalAlignment = BuildGitHubWatchedLocalAlignmentInsight(gitHubLocalAlignmentSummary);

        return new UsageTelemetryGitHubSectionPageModel(
            Lenses: BuildGitHubLenses(
                ownerSections,
                topLanguages,
                watchedRepositories,
                watchedCorrelations,
                watchedStarCorrelations,
                watchedRepoClusters,
                watchedStargazerAudience,
                watchedForkNetwork,
                watchedForkMomentum,
                gitHubLocalAlignment),
            RepoSortModes: BuildGitHubRepoSortModes(topRepositoriesByForks, topRepositoriesByHealth),
            OwnerScopes: BuildGitHubOwnerScopes(ownerSections),
            YearComparison: FindInsight(section, "github-year-comparison"),
            ScopeSplit: FindInsight(section, "github-scope-split"),
            WatchedRepositories: watchedRepositories,
            WatchedCorrelations: watchedCorrelations,
            WatchedStarCorrelations: watchedStarCorrelations,
            WatchedRepoClusters: watchedRepoClusters,
            WatchedStargazerAudience: watchedStargazerAudience,
            WatchedForkNetwork: watchedForkNetwork,
            WatchedForkMomentum: watchedForkMomentum,
            WatchedLocalAlignment: gitHubLocalAlignment,
            RecentRepositories: FindInsight(section, "github-recent-repositories"),
            OwnerImpact: FindInsight(section, "github-owner-impact"),
            TopRepositories: FindInsight(section, "github-top-repositories"),
            TopRepositoriesByForks: topRepositoriesByForks,
            TopRepositoriesByHealth: topRepositoriesByHealth,
            TopLanguages: topLanguages,
            OwnerSections: ownerSections);
    }

    private static UsageTelemetryConversationPulsePageModel? BuildConversationPulseSection(JsonObject? metadata) {
        var conversations = metadata?.GetObject("conversations");
        var items = conversations?.GetArray("items");
        if (items is null || items.Count == 0) {
            return null;
        }

        var itemObjects = items
            .Select(static value => value.AsObject())
            .Where(static value => value is not null)
            .Cast<JsonObject>()
            .ToArray();
        if (itemObjects.Length == 0) {
            return null;
        }

        var totalCount = conversations?.GetInt64("totalCount") ?? itemObjects.Length;
        var shownCount = conversations?.GetInt64("shownCount") ?? itemObjects.Length;
        var tokenTotal = conversations?.GetInt64("tokenTotal") ?? itemObjects.Sum(static item => item.GetInt64("totalTokens") ?? 0L);
        var turnCount = conversations?.GetInt64("turnCount") ?? itemObjects.Sum(static item => item.GetInt64("turnCount") ?? 0L);
        var compactCount = conversations?.GetInt64("compactCount") ?? itemObjects.Sum(static item => item.GetInt64("compactCount") ?? 0L);
        var maxTokens = Math.Max(1L, itemObjects.Max(static item => item.GetInt64("totalTokens") ?? 0L));
        var displayItems = itemObjects.Take(12).ToArray();
        var displayTokenTotal = displayItems.Sum(static item => item.GetInt64("totalTokens") ?? 0L);
        var rowModels = displayItems
            .Select((item, index) => BuildConversationPulseRowModel(item, index + 1, maxTokens, Math.Max(1L, displayTokenTotal)))
            .ToArray();
        var rows = rowModels
            .Select(static row => new UsageTelemetryOverviewInsightRow(
                row.SessionCode,
                row.TokenText + " tokens",
                BuildConversationRowSubtitle(row),
                row.RatioPercent / 100d))
            .ToArray();
        var displayCount = Math.Min(displayItems.Length, shownCount);
        var note = displayCount < totalCount
            ? "Showing the top " + displayCount.ToString(CultureInfo.InvariantCulture) + " conversations here; JSON and CSV exports keep every conversation."
            : "Built from raw session rows before provider rollups are aggregated.";
        var topShare = tokenTotal <= 0
            ? "0%"
            : FormatPercent(displayTokenTotal / (double)Math.Max(1L, tokenTotal));

        return new UsageTelemetryConversationPulsePageModel(
            Title: "Conversation usage",
            Subtitle: "Raw sessions behind this tray report",
            Headline: "Top " + displayCount.ToString(CultureInfo.InvariantCulture) + " cover " + topShare + " of conversation tokens",
            Note: note,
            Stats: new[] {
                new UsageTelemetryHeroStatModel("Conversations", totalCount.ToString(CultureInfo.InvariantCulture)),
                new UsageTelemetryHeroStatModel("Tokens", FormatCompact((double)tokenTotal)),
                new UsageTelemetryHeroStatModel("Turns", turnCount.ToString(CultureInfo.InvariantCulture)),
                new UsageTelemetryHeroStatModel("Compacts", compactCount.ToString(CultureInfo.InvariantCulture))
            },
            Conversations: new UsageTelemetryOverviewInsightSection(
                key: "conversation-usage",
                title: "Top conversations",
                headline: "Largest session: " + FormatCompact((double)maxTokens) + " tokens",
                note: "Ranked by token volume. Duration is wall time; active time appears only when captured.",
                rows: rows),
            Rows: rowModels);
    }

    private static UsageTelemetryConversationPulseRowPageModel BuildConversationPulseRowModel(
        JsonObject item,
        int rank,
        long maxTokens,
        long tokenTotal) {
        var totalTokens = item.GetInt64("totalTokens") ?? 0L;
        var label = NormalizeOptional(item.GetString("label"))
                    ?? NormalizeOptional(item.GetString("sessionId"))
                    ?? "Conversation";
        var title = NormalizeOptional(item.GetString("title"));
        var repository = NormalizeOptional(item.GetString("repository"));
        var workspace = NormalizeOptional(item.GetString("workspace"));
        var context = repository ?? workspace;
        var duration = NormalizeOptional(item.GetString("duration"));
        var durationMs = item.GetInt64("durationMs") ?? 0L;
        var activeDurationMs = item.GetInt64("activeDurationMs");
        var activeDuration = NormalizeOptional(item.GetString("activeDuration"));
        var turnCount = item.GetInt64("turnCount") ?? 0L;
        var compactCount = item.GetInt64("compactCount") ?? 0L;
        var cost = item.GetDouble("apiEquivalentCostUsd") ?? 0d;
        string? costText = null;
        if (cost > 0d) {
            var approximate = item.GetBoolean("costApproximate");
            costText = (approximate ? "~$" : "$") + cost.ToString(cost >= 100d ? "0" : "0.##", CultureInfo.InvariantCulture);
        }

        var provider = NormalizeOptional(item.GetString("provider")) ?? NormalizeOptional(item.GetString("providerId")) ?? "Provider";
        var account = NormalizeConversationAccount(item.GetString("account"), provider);
        var models = BuildConversationList(item.GetArray("models")) ?? "model n/a";
        var surfaces = BuildConversationList(item.GetArray("surfaces")) ?? "surface n/a";
        return new UsageTelemetryConversationPulseRowPageModel(
            Rank: rank,
            SessionLabel: "Session " + rank.ToString(CultureInfo.InvariantCulture),
            SessionCode: label,
            TitleText: title ?? "Session " + rank.ToString(CultureInfo.InvariantCulture),
            ContextText: context,
            RepositoryText: repository,
            WorkspaceText: workspace,
            TotalTokensRaw: totalTokens,
            DurationMs: Math.Max(0L, durationMs),
            TurnCountRaw: (int)Math.Max(0L, turnCount),
            CompactCountRaw: (int)Math.Max(0L, compactCount),
            CostUsdRaw: Math.Max(0d, cost),
            TokenText: FormatCompact((double)totalTokens),
            ShareText: FormatPercent(totalTokens / (double)Math.Max(1L, tokenTotal)) + " of shown total",
            StartedText: NormalizeOptional(item.GetString("startedLocal")) ?? "time n/a",
            SpanText: string.IsNullOrWhiteSpace(duration) ? "span n/a" : "span " + duration,
            ActiveText: (activeDurationMs is null || activeDurationMs > 0) && !string.IsNullOrWhiteSpace(activeDuration)
                ? "active " + activeDuration
                : null,
            TurnText: turnCount > 0
                ? turnCount.ToString(CultureInfo.InvariantCulture) + " turns"
                : "turns n/a",
            CompactText: compactCount > 0
                ? compactCount.ToString(CultureInfo.InvariantCulture) + " compacts"
                : null,
            AccountText: account,
            ModelText: models,
            SurfaceText: surfaces,
            CostText: costText,
            RatioPercent: totalTokens <= 0 ? 0d : Math.Min(100d, totalTokens / (double)Math.Max(1L, maxTokens) * 100d));
    }

    private static string BuildConversationRowSubtitle(UsageTelemetryConversationPulseRowPageModel row) {
        var parts = new List<string> {
            row.StartedText,
            row.SpanText,
            row.TurnText,
            row.AccountText,
            row.ModelText,
            row.SurfaceText
        };
        if (!string.IsNullOrWhiteSpace(row.ContextText)) {
            parts.Insert(1, row.ContextText!);
        }
        if (!string.IsNullOrWhiteSpace(row.ActiveText)) {
            parts.Insert(2, row.ActiveText!);
        }
        if (!string.IsNullOrWhiteSpace(row.CompactText)) {
            parts.Insert(3, row.CompactText!);
        }
        if (!string.IsNullOrWhiteSpace(row.CostText)) {
            parts.Add(row.CostText!);
        }

        return string.Join(" • ", parts);
    }

    private static string? BuildConversationList(JsonArray? values) {
        if (values is null || values.Count == 0) {
            return null;
        }

        var labels = values
            .Select(static value => NormalizeOptional(value.AsString()))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .Take(3)
            .ToArray();
        return labels.Length == 0 ? null : string.Join(", ", labels);
    }

    private static string NormalizeConversationAccount(string? value, string provider) {
        var normalized = NormalizeOptional(value);
        if (string.IsNullOrWhiteSpace(normalized) ||
            string.Equals(normalized, "unknown-account", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "Unknown account", StringComparison.OrdinalIgnoreCase)) {
            return provider + " account";
        }

        var account = normalized!;
        if (Guid.TryParse(account, out _)) {
            return provider + " account " + account.Substring(0, Math.Min(8, account.Length));
        }

        if (account.Length > 36 && account.Count(static ch => ch == '-') >= 4) {
            return provider + " account " + account.Substring(0, Math.Min(8, account.Length));
        }

        return account;
    }

    private static string FormatPercent(double ratio) {
        var percent = Math.Max(0d, Math.Min(100d, ratio * 100d));
        return percent >= 10d
            ? percent.ToString("0", CultureInfo.InvariantCulture) + "%"
            : percent.ToString("0.#", CultureInfo.InvariantCulture) + "%";
    }

    private static IReadOnlyList<UsageTelemetryOverviewInsightSection> BuildProviderHealthInsights(
        UsageTelemetryOverviewProviderSection section) {
        return section.AdditionalInsights
            .Where(static insight =>
                string.Equals(insight.Key, "source-roots", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(insight.Key, "quick-scan-dedupe", StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private static UsageTelemetryToggleOptionModel[] BuildDatasetTabs(UsageTelemetryOverviewSectionFlags flags) {
        var tabs = new List<UsageTelemetryToggleOptionModel> {
            new("summary", "Summary", IsDefault: true)
        };

        if (flags.HasActivity) {
            tabs.Add(new("activity", "Activity", IsDefault: false));
        }
        if (flags.HasModels) {
            tabs.Add(new("models", "Models", IsDefault: false));
        }
        if (flags.HasPricing) {
            tabs.Add(new("pricing", "Pricing", IsDefault: false));
        }
        if (flags.HasAdditionalInsights) {
            tabs.Add(new("impact", "Impact", IsDefault: false));
        }
        if (flags.IsGitHub && flags.HasActivity) {
            tabs.Add(new("wrapped", "Wrapped", IsDefault: false, Href: "github-wrapped.html"));
        }

        return tabs.ToArray();
    }

    private static UsageTelemetryToggleOptionModel[] BuildGitHubLenses(
        IReadOnlyList<UsageTelemetryOverviewInsightSection> ownerSections,
        UsageTelemetryOverviewInsightSection? topLanguages,
        UsageTelemetryOverviewInsightSection? watchedRepositories,
        UsageTelemetryOverviewInsightSection? watchedCorrelations,
        UsageTelemetryOverviewInsightSection? watchedStarCorrelations,
        UsageTelemetryOverviewInsightSection? watchedRepoClusters,
        UsageTelemetryOverviewInsightSection? watchedStargazerAudience,
        UsageTelemetryOverviewInsightSection? watchedForkNetwork,
        UsageTelemetryOverviewInsightSection? watchedForkMomentum,
        UsageTelemetryOverviewInsightSection? watchedLocalAlignment) {
        var tabs = new List<UsageTelemetryToggleOptionModel> {
            new("impact", "Impact", IsDefault: true),
            new("recent", "Recent", IsDefault: false)
        };
        if (watchedRepositories is not null
            || watchedCorrelations is not null
            || watchedStarCorrelations is not null
            || watchedRepoClusters is not null
            || watchedStargazerAudience is not null
            || watchedForkNetwork is not null
            || watchedForkMomentum is not null
            || watchedLocalAlignment is not null) {
            tabs.Add(new("watched", "Watched", IsDefault: false));
        }
        if (ownerSections.Count > 0) {
            tabs.Add(new("owners", "Owners", IsDefault: false));
        }
        if (topLanguages is not null) {
            tabs.Add(new("languages", "Languages", IsDefault: false));
        }

        return tabs.ToArray();
    }

    private static UsageTelemetryToggleOptionModel[] BuildGitHubRepoSortModes(
        UsageTelemetryOverviewInsightSection? topRepositoriesByForks,
        UsageTelemetryOverviewInsightSection? topRepositoriesByHealth) {
        var tabs = new List<UsageTelemetryToggleOptionModel> {
            new("stars", "Top by stars", IsDefault: true)
        };
        if (topRepositoriesByForks is not null) {
            tabs.Add(new("forks", "Top by forks", IsDefault: false));
        }
        if (topRepositoriesByHealth is not null) {
            tabs.Add(new("health", "Top by health", IsDefault: false));
        }

        return tabs.ToArray();
    }

    private static UsageTelemetryToggleOptionModel[] BuildGitHubOwnerScopes(
        IReadOnlyList<UsageTelemetryOverviewInsightSection> ownerSections) {
        if (ownerSections.Count == 0) {
            return Array.Empty<UsageTelemetryToggleOptionModel>();
        }

        var scopes = new List<UsageTelemetryToggleOptionModel> {
            new("all", "All scope", IsDefault: true)
        };
        scopes.AddRange(ownerSections.Select(static ownerSection =>
            new UsageTelemetryToggleOptionModel(ownerSection.Key, ownerSection.Title, IsDefault: false)));
        return scopes.ToArray();
    }

    public static UsageTelemetryGitHubWrappedPageModel BuildGitHubWrapped(
        UsageTelemetryOverviewProviderSection section,
        UsageSummarySnapshot? summary = null,
        JsonObject? metadata = null,
        int providerSectionsCount = 0,
        GitHubObservabilitySummaryData? gitHubObservabilitySummary = null,
        GitHubLocalActivityCorrelationSummaryData? gitHubLocalAlignmentSummary = null) {
        if (section is null) {
            throw new ArgumentNullException(nameof(section));
        }

        var github = BuildGitHubSection(section, gitHubObservabilitySummary, gitHubLocalAlignmentSummary);
        var ownerPanels = github.OwnerSections
            .OrderByDescending(static insight => ExtractOwnerMagnitude(insight.Headline))
            .ThenBy(static insight => insight.Title, StringComparer.OrdinalIgnoreCase)
            .Select(static insight => new UsageTelemetryGitHubWrappedOwnerPanelModel(
                insight.Key,
                insight.Title,
                insight))
            .ToArray();

        var bootstrap = UsageTelemetryReportAppearanceDefaults.AddBootstrap(new JsonObject())
            .Add("defaultOwnerPanel", "all");

        return new UsageTelemetryGitHubWrappedPageModel(
            Title: section.Title,
            Subtitle: section.Subtitle,
            Note: section.Note,
            Diagnostics: summary is null ? null : UsageTelemetryReportDiagnosticsBuilder.Build(summary, metadata, providerSectionsCount),
            BootstrapJson: JsonLite.Serialize(JsonValue.From(bootstrap)),
            Metrics: section.Metrics,
            SpotlightCards: section.SpotlightCards,
            MonthlyUsage: section.MonthlyUsage,
            LongestStreakDays: section.LongestStreakDays,
            CurrentStreakDays: section.CurrentStreakDays,
            YearComparison: github.YearComparison,
            ScopeSplit: github.ScopeSplit,
            OwnerImpact: github.OwnerImpact,
            TopLanguages: github.TopLanguages,
            WatchedRepositories: github.WatchedRepositories,
            WatchedCorrelations: github.WatchedCorrelations,
            WatchedStarCorrelations: github.WatchedStarCorrelations,
            WatchedRepoClusters: github.WatchedRepoClusters,
            WatchedStargazerAudience: github.WatchedStargazerAudience,
            WatchedForkNetwork: github.WatchedForkNetwork,
            WatchedForkMomentum: github.WatchedForkMomentum,
            WatchedLocalAlignment: github.WatchedLocalAlignment,
            RecentRepositories: github.RecentRepositories,
            TopRepositories: github.TopRepositories,
            TopRepositoriesByForks: github.TopRepositoriesByForks,
            TopRepositoriesByHealth: github.TopRepositoriesByHealth,
            OwnerPanels: ownerPanels);
    }

    public static UsageTelemetryGitHubWrappedCardPageModel BuildGitHubWrappedCard(
        UsageTelemetryOverviewProviderSection section,
        UsageSummarySnapshot? summary = null,
        JsonObject? metadata = null,
        int providerSectionsCount = 0,
        GitHubObservabilitySummaryData? gitHubObservabilitySummary = null,
        GitHubLocalActivityCorrelationSummaryData? gitHubLocalAlignmentSummary = null) {
        if (section is null) {
            throw new ArgumentNullException(nameof(section));
        }

        var github = BuildGitHubSection(section, gitHubObservabilitySummary, gitHubLocalAlignmentSummary);
        var yearComparisonHeadline = github.YearComparison?.Headline ?? "n/a";
        var topRepositoryHeadline = github.TopRepositories?.Headline ?? "n/a";
        var topRepositoryValue = github.TopRepositories?.Rows.FirstOrDefault()?.Value;
        var topLanguageHeadline = github.TopLanguages?.Headline ?? "n/a";
        var topLanguageValue = github.TopLanguages?.Rows.FirstOrDefault()?.Value;
        var ownerScopeValue = github.ScopeSplit?.Rows.Count > 1
            ? github.ScopeSplit.Rows[1].Value
            : github.ScopeSplit?.Headline ?? "n/a";

        return new UsageTelemetryGitHubWrappedCardPageModel(
            Title: section.Title,
            Subtitle: section.Subtitle,
            Diagnostics: summary is null ? null : UsageTelemetryReportDiagnosticsBuilder.Build(summary, metadata, providerSectionsCount),
            BootstrapJson: JsonLite.Serialize(JsonValue.From(
                UsageTelemetryReportAppearanceDefaults.AddBootstrap(new JsonObject()))),
            Metrics: new[] {
                new UsageTelemetryGitHubWrappedMetricModel(
                    section.Metrics.ElementAtOrDefault(0)?.Label ?? "Contributions",
                    section.Metrics.ElementAtOrDefault(0)?.Value ?? "n/a",
                    section.Metrics.ElementAtOrDefault(0)?.Subtitle),
                new UsageTelemetryGitHubWrappedMetricModel(
                    "Most active month",
                    FindSpotlightCardValue(section, "most-active-month", "n/a"),
                    FindSpotlightCardSubtitle(section, "most-active-month")),
                new UsageTelemetryGitHubWrappedMetricModel(
                    "Longest streak",
                    HeatmapDisplayText.FormatDays(section.LongestStreakDays),
                    FindSpotlightCardSubtitle(section, "longest-streak")),
                new UsageTelemetryGitHubWrappedMetricModel(
                    "Current streak",
                    HeatmapDisplayText.FormatDays(section.CurrentStreakDays),
                    FindSpotlightCardSubtitle(section, "current-streak"))
            },
            Stats: new[] {
                new UsageTelemetryGitHubWrappedMetricModel("Year over year", yearComparisonHeadline, github.YearComparison?.Note),
                new UsageTelemetryGitHubWrappedMetricModel("Top repository", topRepositoryHeadline, topRepositoryValue),
                new UsageTelemetryGitHubWrappedMetricModel("Top language", topLanguageHeadline, topLanguageValue),
                new UsageTelemetryGitHubWrappedMetricModel("Owner scope", ownerScopeValue, section.Note)
            },
            FooterMetrics: BuildGitHubWrappedFooterMetrics(github));
    }

    private static UsageTelemetryOverviewInsightSection? BuildGitHubWatchedRepositoriesInsight(
        GitHubObservabilitySummaryData? summary) {
        if (summary is null) {
            return null;
        }
        var featuredRepositories = summary.FeaturedRepositories;
        if (featuredRepositories.Count == 0) {
            return null;
        }

        var maxScore = Math.Max(
            1d,
            featuredRepositories
                .Select(static repository => ComputeWatchedRepositoryScore(repository))
                .DefaultIfEmpty(1d)
                .Max());

        var rows = featuredRepositories
            .Select(repository => new UsageTelemetryOverviewInsightRow(
                label: repository.RepositoryNameWithOwner,
                value: BuildWatchedRepositoryValue(repository),
                subtitle: BuildWatchedRepositorySubtitle(repository),
                ratio: Math.Min(1d, ComputeWatchedRepositoryScore(repository) / maxScore),
                href: "https://github.com/" + repository.RepositoryNameWithOwner))
            .ToArray();

        return new UsageTelemetryOverviewInsightSection(
            key: "github-watched-repositories",
            title: "Watched repo momentum",
            headline: summary.ChangedRepositoryCount.ToString(CultureInfo.InvariantCulture) + " repos moved • +" + summary.PositiveStarDelta.ToString(CultureInfo.InvariantCulture) + " stars",
            note: summary.LatestCaptureAtUtc.HasValue
                ? "Latest watch snapshot " + summary.LatestCaptureAtUtc.Value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture) + " across "
                  + summary.SnapshotRepositoryCount.ToString(CultureInfo.InvariantCulture) + " tracked repositories."
                : "Watched repository momentum appears after local watch snapshots are synced.",
            rows: rows);
    }

    private static UsageTelemetryOverviewInsightSection? BuildGitHubWatchedCorrelationInsight(
        GitHubObservabilitySummaryData? summary) {
        if (summary is null || summary.Correlations.Count == 0) {
            return null;
        }

        var strongestPositive = summary.StrongestPositiveCorrelation;
        var strongestNegative = summary.StrongestNegativeCorrelation;
        var fallbackCorrelation = summary.Correlations[0];
        var rows = summary.Correlations
            .Select(static correlation => new UsageTelemetryOverviewInsightRow(
                label: correlation.RepositoryANameWithOwner + " ↔ " + correlation.RepositoryBNameWithOwner,
                value: BuildWatchedCorrelationValue(correlation),
                subtitle: BuildWatchedCorrelationSubtitle(correlation),
                ratio: Math.Abs(correlation.Correlation)))
            .ToArray();

        var primaryCorrelation = strongestPositive ?? strongestNegative ?? fallbackCorrelation;
        var headline = primaryCorrelation.Correlation >= 0d
            ? "Strongest sync • " + FormatCorrelationValue(primaryCorrelation.Correlation)
            : "Strongest divergence • " + FormatCorrelationValue(primaryCorrelation.Correlation);
        var note = strongestNegative is not null && !ReferenceEquals(primaryCorrelation, strongestNegative)
            ? strongestNegative.RepositoryANameWithOwner + " ↔ " + strongestNegative.RepositoryBNameWithOwner
              + " diverges most at " + FormatCorrelationValue(strongestNegative.Correlation) + "."
            : "Based on shared daily movement across the recent watched-repo pulse window.";

        return new UsageTelemetryOverviewInsightSection(
            key: "github-watched-correlations",
            title: "Watched repo correlation",
            headline: headline,
            note: note,
            rows: rows);
    }

    private static UsageTelemetryOverviewInsightSection? BuildGitHubWatchedStarCorrelationInsight(
        GitHubObservabilitySummaryData? summary) {
        if (summary is null || summary.StarCorrelations.Count == 0) {
            return null;
        }

        var strongestPositive = summary.StrongestPositiveStarCorrelation;
        var strongestNegative = summary.StrongestNegativeStarCorrelation;
        var fallbackCorrelation = summary.StarCorrelations[0];
        var rows = summary.StarCorrelations
            .Select(static correlation => new UsageTelemetryOverviewInsightRow(
                label: correlation.RepositoryANameWithOwner + " ↔ " + correlation.RepositoryBNameWithOwner,
                value: correlation.Correlation >= 0d
                    ? "Star sync " + FormatCorrelationValue(correlation.Correlation)
                    : "Star divergence " + FormatCorrelationValue(correlation.Correlation),
                subtitle: correlation.OverlapDays.ToString(CultureInfo.InvariantCulture) + " shared days • "
                          + FormatSigned(correlation.RepositoryARecentStarChange, "stars") + " / "
                          + FormatSigned(correlation.RepositoryBRecentStarChange, "stars") + " • "
                          + (correlation.Correlation >= 0d
                              ? correlation.SharedGainDays.ToString(CultureInfo.InvariantCulture) + " gain-together days"
                              : correlation.OpposingDays.ToString(CultureInfo.InvariantCulture) + " opposing star days"),
                ratio: Math.Abs(correlation.Correlation)))
            .ToArray();

        var primaryCorrelation = strongestPositive ?? strongestNegative ?? fallbackCorrelation;
        var headline = primaryCorrelation.Correlation >= 0d
            ? "Strongest star sync • " + FormatCorrelationValue(primaryCorrelation.Correlation)
            : "Strongest star divergence • " + FormatCorrelationValue(primaryCorrelation.Correlation);
        var note = strongestNegative is not null && !ReferenceEquals(primaryCorrelation, strongestNegative)
            ? strongestNegative.RepositoryANameWithOwner + " ↔ " + strongestNegative.RepositoryBNameWithOwner
              + " diverges most at " + FormatCorrelationValue(strongestNegative.Correlation) + "."
            : "Based only on shared daily star changes across the recent watched-repo window.";

        return new UsageTelemetryOverviewInsightSection(
            key: "github-watched-star-correlations",
            title: "Watched repo star sync",
            headline: headline,
            note: note,
            rows: rows);
    }

    private static UsageTelemetryOverviewInsightSection? BuildGitHubWatchedRepoClusterInsight(
        GitHubRepositoryClusterSummaryData? summary) {
        if (summary is null || !summary.HasSignals) {
            return null;
        }

        var strongest = summary.StrongestCluster ?? summary.Clusters[0];
        var rows = summary.Clusters
            .Select(static cluster => new UsageTelemetryOverviewInsightRow(
                label: cluster.RepositoryANameWithOwner + " ↔ " + cluster.RepositoryBNameWithOwner,
                value: cluster.SupportingSignalCount.ToString(CultureInfo.InvariantCulture) + " signals • score " + cluster.CompositeScore.ToString("0.00", CultureInfo.InvariantCulture),
                subtitle: "Star sync " + FormatCorrelationValue(cluster.StarCorrelation)
                          + " • "
                          + cluster.SharedStargazerCount.ToString(CultureInfo.InvariantCulture) + " shared stargazers"
                          + " • "
                          + cluster.SharedForkOwnerCount.ToString(CultureInfo.InvariantCulture) + " shared forkers"
                          + (cluster.LocallyAlignedRepositoryCount == 2
                              ? " • both local " + FormatCorrelationValue(cluster.LocalAlignmentAverageCorrelation)
                              : string.Empty)
                          + (cluster.SampleSharedStargazers.Count > 0
                              ? " • " + string.Join(", ", cluster.SampleSharedStargazers)
                              : string.Empty)
                          + (cluster.SampleSharedForkOwners.Count > 0
                              ? " • " + string.Join(", ", cluster.SampleSharedForkOwners)
                              : string.Empty),
                ratio: cluster.CompositeScore))
            .ToArray();

        return new UsageTelemetryOverviewInsightSection(
            key: "github-watched-repo-clusters",
            title: "Related watched repos",
            headline: "Strongest cluster • "
                      + BuildShortRepositoryLabel(strongest.RepositoryANameWithOwner)
                      + " ↔ "
                      + BuildShortRepositoryLabel(strongest.RepositoryBNameWithOwner),
            note: strongest.SupportingSignalCount.ToString(CultureInfo.InvariantCulture)
                  + " signals aligned • "
                  + strongest.SharedStargazerCount.ToString(CultureInfo.InvariantCulture)
                  + " shared stargazers • "
                  + summary.LocallyAlignedRepositoryCount.ToString(CultureInfo.InvariantCulture)
                  + " repos aligned with local pulse",
            rows: rows);
    }

    private static UsageTelemetryOverviewInsightSection? BuildGitHubWatchedLocalAlignmentInsight(
        GitHubLocalActivityCorrelationSummaryData? summary) {
        if (summary is null || !summary.HasSignals) {
            return null;
        }

        var strongestPositive = summary.StrongestPositiveCorrelation;
        var strongestNegative = summary.StrongestNegativeCorrelation;
        var fallbackCorrelation = summary.RepositoryCorrelations[0];
        var rows = summary.RepositoryCorrelations
            .Select(static correlation => new UsageTelemetryOverviewInsightRow(
                label: correlation.RepositoryNameWithOwner,
                value: correlation.Correlation >= 0d
                    ? "Local sync " + FormatCorrelationValue(correlation.Correlation)
                    : "Local divergence " + FormatCorrelationValue(correlation.Correlation),
                subtitle: FormatSigned(correlation.StarDelta, "stars")
                          + " • " + FormatSigned(correlation.ForkDelta, "forks")
                          + " • " + FormatSigned(correlation.WatcherDelta, "watchers")
                          + " • " + correlation.OverlapDays.ToString(CultureInfo.InvariantCulture) + " overlap days",
                ratio: Math.Abs(correlation.Correlation),
                href: "https://github.com/" + correlation.RepositoryNameWithOwner))
            .ToArray();

        var primaryCorrelation = strongestPositive ?? strongestNegative ?? fallbackCorrelation;
        var headline = primaryCorrelation.Correlation >= 0d
            ? "Strongest local sync • " + FormatCorrelationValue(primaryCorrelation.Correlation)
            : "Strongest local divergence • " + FormatCorrelationValue(primaryCorrelation.Correlation);
        var noteParts = new List<string> {
            FormatCompact((double)summary.RecentChurnVolume) + " churn lines",
            FormatCompact(summary.RecentUsageTotal) + " recent usage",
            summary.ActiveLocalDays.ToString(CultureInfo.InvariantCulture) + " active local days"
        };
        if (strongestNegative is not null && !ReferenceEquals(primaryCorrelation, strongestNegative)) {
            noteParts.Add("Diverging repo: " + strongestNegative.RepositoryNameWithOwner + " " + FormatCorrelationValue(strongestNegative.Correlation));
        }

        return new UsageTelemetryOverviewInsightSection(
            key: "github-watched-local-alignment",
            title: "Watched repo vs local pulse",
            headline: headline,
            note: string.Join(" • ", noteParts),
            rows: rows);
    }

    private static UsageTelemetryOverviewInsightSection? BuildGitHubWatchedForkNetworkInsight(
        GitHubObservabilitySummaryData? summary) {
        if (summary is null || summary.EnabledWatchCount == 0) {
            return null;
        }

        var strongest = summary.StrongestForkNetworkOverlap;
        UsageTelemetryOverviewInsightRow[] rows;
        string headline;
        if (summary.ForkNetworkOverlaps.Count > 0 && strongest is not null) {
            rows = summary.ForkNetworkOverlaps
                .Select(static overlap => new UsageTelemetryOverviewInsightRow(
                    label: overlap.RepositoryANameWithOwner + " ↔ " + overlap.RepositoryBNameWithOwner,
                    value: overlap.SharedForkOwnerCount.ToString(CultureInfo.InvariantCulture) + " shared fork owners",
                    subtitle: overlap.RepositoryAForkOwnerCount.ToString(CultureInfo.InvariantCulture)
                              + "/" + overlap.RepositoryBForkOwnerCount.ToString(CultureInfo.InvariantCulture)
                              + " observed owners • "
                              + overlap.OverlapRatio.ToString("0%", CultureInfo.InvariantCulture)
                              + " smaller-set overlap"
                              + (overlap.SampleSharedForkOwners.Count > 0
                                  ? " • " + string.Join(", ", overlap.SampleSharedForkOwners)
                                  : string.Empty),
                    ratio: overlap.OverlapRatio))
                .ToArray();
            headline = "Strongest shared forkers • "
                       + BuildShortRepositoryLabel(strongest.RepositoryANameWithOwner)
                       + " ↔ "
                       + BuildShortRepositoryLabel(strongest.RepositoryBNameWithOwner);
        } else {
            rows = new[] {
                new UsageTelemetryOverviewInsightRow(
                    label: "Coverage",
                    value: summary.ForkSnapshotRepositoryCount.ToString(CultureInfo.InvariantCulture)
                           + "/"
                           + summary.EnabledWatchCount.ToString(CultureInfo.InvariantCulture)
                           + " watched repos",
                    subtitle: BuildGitHubForkCoverageNote(summary),
                    ratio: summary.EnabledWatchCount <= 0
                        ? 0d
                        : Math.Min(1d, summary.ForkSnapshotRepositoryCount / (double)summary.EnabledWatchCount))
            };
            headline = summary.HasAnyForkSnapshots
                ? "Fork capture is active, but no shared fork-owner overlap is confirmed yet"
                : "Fork network capture is still pending";
        }

        return new UsageTelemetryOverviewInsightSection(
            key: "github-watched-fork-network",
            title: "Shared fork network",
            headline: headline,
            note: BuildGitHubForkNetworkNote(summary, strongest),
            rows: rows);
    }

    private static UsageTelemetryOverviewInsightSection? BuildGitHubWatchedForkMomentumInsight(
        GitHubObservabilitySummaryData? summary) {
        if (summary is null || summary.EnabledWatchCount == 0) {
            return null;
        }

        var strongest = summary.StrongestForkChange;
        UsageTelemetryOverviewInsightRow[] rows;
        string headline;
        if (summary.ForkChanges.Count > 0 && strongest is not null) {
            var maxRatio = Math.Max(
                1d,
                summary.ForkChanges
                    .Select(static change => Math.Max(Math.Abs(change.ScoreDelta), change.Score))
                    .DefaultIfEmpty(1d)
                    .Max());
            rows = summary.ForkChanges
                .Select(change => new UsageTelemetryOverviewInsightRow(
                    label: change.ForkRepositoryNameWithOwner,
                    value: change.Status + " • " + change.Score.ToString("0.##", CultureInfo.InvariantCulture) + " score",
                    subtitle: change.ParentRepositoryNameWithOwner
                              + " • "
                              + change.ScoreDelta.ToString("+0.##;-0.##;0", CultureInfo.InvariantCulture)
                              + " score delta • "
                              + FormatSigned(change.StarDelta, "stars")
                              + " • "
                              + FormatSigned(change.WatcherDelta, "watchers"),
                    ratio: Math.Min(1d, Math.Max(Math.Abs(change.ScoreDelta), change.Score) / maxRatio),
                    href: "https://github.com/" + change.ForkRepositoryNameWithOwner))
                .ToArray();
            headline = "Top fork mover • " + BuildShortRepositoryLabel(strongest.ForkRepositoryNameWithOwner);
        } else {
            rows = new[] {
                new UsageTelemetryOverviewInsightRow(
                    label: "Coverage",
                    value: summary.ForkSnapshotRepositoryCount.ToString(CultureInfo.InvariantCulture)
                           + "/"
                           + summary.EnabledWatchCount.ToString(CultureInfo.InvariantCulture)
                           + " watched repos",
                    subtitle: BuildGitHubForkCoverageNote(summary),
                    ratio: summary.EnabledWatchCount <= 0
                        ? 0d
                        : Math.Min(1d, summary.ForkSnapshotRepositoryCount / (double)summary.EnabledWatchCount))
            };
            headline = summary.HasAnyForkSnapshots
                ? "Fork capture is active, but no strong fork mover stands out yet"
                : "Fork mover capture is still pending";
        }

        return new UsageTelemetryOverviewInsightSection(
            key: "github-watched-fork-momentum",
            title: "Rising forks",
            headline: headline,
            note: BuildGitHubForkMomentumNote(summary, strongest),
            rows: rows);
    }

    private static UsageTelemetryOverviewInsightSection? BuildGitHubWatchedStargazerAudienceInsight(
        GitHubObservabilitySummaryData? summary) {
        if (summary is null || summary.EnabledWatchCount == 0) {
            return null;
        }

        var strongest = summary.StrongestStargazerAudienceOverlap;
        UsageTelemetryOverviewInsightRow[] rows;
        string headline;
        if (summary.StargazerAudienceOverlaps.Count > 0 && strongest is not null) {
            rows = summary.StargazerAudienceOverlaps
                .Select(static overlap => new UsageTelemetryOverviewInsightRow(
                    label: overlap.RepositoryANameWithOwner + " ↔ " + overlap.RepositoryBNameWithOwner,
                    value: overlap.SharedStargazerCount.ToString(CultureInfo.InvariantCulture) + " shared stargazers",
                    subtitle: overlap.RepositoryAStargazerCount.ToString(CultureInfo.InvariantCulture)
                              + "/" + overlap.RepositoryBStargazerCount.ToString(CultureInfo.InvariantCulture)
                              + " observed stargazers • "
                              + overlap.OverlapRatio.ToString("0%", CultureInfo.InvariantCulture)
                              + " smaller-set overlap"
                              + (overlap.SampleSharedStargazers.Count > 0
                                  ? " • " + string.Join(", ", overlap.SampleSharedStargazers)
                                  : string.Empty),
                    ratio: overlap.OverlapRatio))
                .ToArray();
            headline = "Strongest shared stargazers • "
                       + BuildShortRepositoryLabel(strongest.RepositoryANameWithOwner)
                       + " ↔ "
                       + BuildShortRepositoryLabel(strongest.RepositoryBNameWithOwner);
        } else {
            rows = new[] {
                new UsageTelemetryOverviewInsightRow(
                    label: "Coverage",
                    value: summary.StargazerSnapshotRepositoryCount.ToString(CultureInfo.InvariantCulture)
                           + "/"
                           + summary.EnabledWatchCount.ToString(CultureInfo.InvariantCulture)
                           + " watched repos",
                    subtitle: BuildGitHubStargazerCoverageNote(summary),
                    ratio: summary.EnabledWatchCount <= 0
                        ? 0d
                        : Math.Min(1d, summary.StargazerSnapshotRepositoryCount / (double)summary.EnabledWatchCount))
            };
            headline = summary.HasAnyStargazerSnapshots
                ? "Audience capture is active, but no shared stargazer overlap is confirmed yet"
                : "Stargazer audience capture is still pending";
        }

        return new UsageTelemetryOverviewInsightSection(
            key: "github-watched-stargazer-audience",
            title: "Shared stargazer audience",
            headline: headline,
            note: BuildGitHubStargazerAudienceNote(summary, strongest),
            rows: rows);
    }

    private static string BuildGitHubStargazerAudienceNote(
        GitHubObservabilitySummaryData summary,
        GitHubObservedStargazerAudienceOverlapData? strongest) {
        var parts = new List<string>();
        if (strongest is not null) {
            parts.Add(strongest.SharedStargazerCount.ToString(CultureInfo.InvariantCulture) + " shared stargazers");
        }
        if (summary.ObservedStargazerCount > 0) {
            parts.Add(summary.ObservedStargazerCount.ToString(CultureInfo.InvariantCulture) + " distinct observed stargazers");
        }
        parts.Add(BuildGitHubStargazerCoverageNote(summary));
        return string.Join(" • ", parts.Where(static part => !string.IsNullOrWhiteSpace(part)));
    }

    private static string BuildGitHubStargazerCoverageNote(GitHubObservabilitySummaryData summary) {
        var parts = new List<string> {
            summary.StargazerSnapshotRepositoryCount.ToString(CultureInfo.InvariantCulture)
            + "/"
            + summary.EnabledWatchCount.ToString(CultureInfo.InvariantCulture)
            + " watched repos captured"
        };
        if (summary.MissingStargazerSnapshotRepositoryCount > 0) {
            parts.Add(summary.MissingStargazerSnapshotRepositoryCount.ToString(CultureInfo.InvariantCulture) + " missing audience snapshots");
        }
        if (summary.LaggingStargazerRepositoryCount > 0) {
            parts.Add(summary.LaggingStargazerRepositoryCount.ToString(CultureInfo.InvariantCulture) + " behind latest repo sync");
        } else if (summary.HasFreshStargazerCoverage) {
            parts.Add("aligned with latest repo sync");
        }
        if (summary.LatestStargazerCaptureAtUtc.HasValue) {
            parts.Add("last audience sync " + summary.LatestStargazerCaptureAtUtc.Value.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture));
        }

        return string.Join(" • ", parts);
    }

    private static string BuildGitHubForkNetworkNote(
        GitHubObservabilitySummaryData summary,
        GitHubObservedForkNetworkOverlapData? strongest) {
        var parts = new List<string>();
        if (strongest is not null) {
            parts.Add(strongest.SharedForkOwnerCount.ToString(CultureInfo.InvariantCulture) + " shared fork owners");
        }
        if (summary.ObservedForkOwnerCount > 0) {
            parts.Add(summary.ObservedForkOwnerCount.ToString(CultureInfo.InvariantCulture) + " distinct observed fork owners");
        }
        parts.Add(BuildGitHubForkCoverageNote(summary));
        return string.Join(" • ", parts.Where(static part => !string.IsNullOrWhiteSpace(part)));
    }

    private static string BuildGitHubForkMomentumNote(
        GitHubObservabilitySummaryData summary,
        GitHubRepositoryForkChange? strongest) {
        var parts = new List<string>();
        if (strongest is not null) {
            parts.Add(strongest.ParentRepositoryNameWithOwner);
            parts.Add(strongest.Status + " • " + strongest.Tier + " tier");
        }
        parts.Add(BuildGitHubForkCoverageNote(summary));
        return string.Join(" • ", parts.Where(static part => !string.IsNullOrWhiteSpace(part)));
    }

    private static string BuildGitHubForkCoverageNote(GitHubObservabilitySummaryData summary) {
        var parts = new List<string> {
            summary.ForkSnapshotRepositoryCount.ToString(CultureInfo.InvariantCulture)
            + "/"
            + summary.EnabledWatchCount.ToString(CultureInfo.InvariantCulture)
            + " watched repos captured"
        };
        if (summary.MissingForkSnapshotRepositoryCount > 0) {
            parts.Add(summary.MissingForkSnapshotRepositoryCount.ToString(CultureInfo.InvariantCulture) + " missing fork snapshots");
        }
        if (summary.LaggingForkRepositoryCount > 0) {
            parts.Add(summary.LaggingForkRepositoryCount.ToString(CultureInfo.InvariantCulture) + " behind latest repo sync");
        } else if (summary.HasFreshForkCoverage) {
            parts.Add("aligned with latest repo sync");
        }
        if (summary.LatestForkCaptureAtUtc.HasValue) {
            parts.Add("last fork sync " + summary.LatestForkCaptureAtUtc.Value.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture));
        }

        return string.Join(" • ", parts);
    }

    private static UsageTelemetryGitHubLocalPulsePageModel? BuildGitHubLocalAlignmentSection(
        GitHubLocalActivityCorrelationSummaryData? summary,
        UsageTelemetryOverviewInsightSection? insight) {
        if (summary is null || !summary.HasData || insight is null) {
            return null;
        }

        return new UsageTelemetryGitHubLocalPulsePageModel(
            Title: "Watched repo sync",
            Subtitle: "Repositories whose recent GitHub momentum most closely matches the same local churn and usage pulse.",
            Headline: BuildGitHubLocalAlignmentHeadline(summary),
            Note: BuildGitHubLocalAlignmentNote(summary),
            Stats: new[] {
                new UsageTelemetryHeroStatModel("Watched repos", summary.WatchedRepositoryCount.ToString(CultureInfo.InvariantCulture)),
                new UsageTelemetryHeroStatModel("Linked movers", summary.RepositoryCorrelations.Count.ToString(CultureInfo.InvariantCulture)),
                new UsageTelemetryHeroStatModel("Active local days", summary.ActiveLocalDays.ToString(CultureInfo.InvariantCulture))
            },
            Repositories: insight);
    }

    private static UsageTelemetryGitHubRepoClusterPageModel? BuildGitHubRepoClusterSection(
        GitHubRepositoryClusterSummaryData? summary,
        UsageTelemetryOverviewInsightSection? insight) {
        if (summary is null || !summary.HasData || insight is null) {
            return null;
        }

        return new UsageTelemetryGitHubRepoClusterPageModel(
            Title: "Related repo clusters",
            Subtitle: "Watched repo pairs where audience overlap, star momentum, and local pulse start telling the same story.",
            Headline: BuildGitHubRepoClusterHeadline(summary),
            Note: BuildGitHubRepoClusterNote(summary),
            Stats: new[] {
                new UsageTelemetryHeroStatModel("Watched repos", summary.WatchedRepositoryCount.ToString(CultureInfo.InvariantCulture)),
                new UsageTelemetryHeroStatModel("Clusters", summary.Clusters.Count.ToString(CultureInfo.InvariantCulture)),
                new UsageTelemetryHeroStatModel("Local overlaps", summary.LocallyAlignedRepositoryCount.ToString(CultureInfo.InvariantCulture))
            },
            Clusters: insight);
    }

    private static string BuildWatchedRepositoryValue(GitHubObservedRepositoryTrendData repository) {
        if (!repository.PreviousCapturedAtUtc.HasValue) {
            return "Baseline";
        }

        return FormatSigned(repository.StarDelta, "stars")
               + " · "
               + FormatSigned(repository.ForkDelta, "forks")
               + " · "
               + FormatSigned(repository.WatcherDelta, "watchers");
    }

    private static string BuildWatchedRepositorySubtitle(GitHubObservedRepositoryTrendData repository) {
        var recentPoints = repository.TrendPoints
            .Where(static point => point.DayUtc != default)
            .ToArray();
        var trendWindow = recentPoints.Length == 0
            ? "daily trend pending"
            : recentPoints.First().DayUtc.ToString("MMM d", CultureInfo.InvariantCulture)
              + " to "
              + recentPoints.Last().DayUtc.ToString("MMM d", CultureInfo.InvariantCulture);
        return FormatCompact((double)repository.Stars) + " stars · "
               + FormatCompact((double)repository.Forks) + " forks · "
               + FormatCompact((double)repository.Watchers) + " watchers · "
               + trendWindow;
    }

    private static double ComputeWatchedRepositoryScore(GitHubObservedRepositoryTrendData repository) {
        return Math.Abs(repository.StarDelta * 6d)
               + Math.Abs(repository.ForkDelta * 8d)
               + Math.Abs(repository.WatcherDelta * 3d)
               + Math.Max(0, repository.OpenIssueDelta);
    }

    private static string BuildWatchedCorrelationValue(GitHubObservedCorrelationData correlation) {
        return correlation.Correlation >= 0d
            ? "Sync " + FormatCorrelationValue(correlation.Correlation)
            : "Diverges " + FormatCorrelationValue(correlation.Correlation);
    }

    private static string BuildWatchedCorrelationSubtitle(GitHubObservedCorrelationData correlation) {
        var lead = correlation.Correlation >= 0d
            ? correlation.SharedUpDays.ToString(CultureInfo.InvariantCulture) + " up together"
            : correlation.OpposingDays.ToString(CultureInfo.InvariantCulture) + " opposing days";
        var trailing = correlation.Correlation >= 0d
            ? correlation.SharedDownDays.ToString(CultureInfo.InvariantCulture) + " down together"
            : correlation.SharedUpDays.ToString(CultureInfo.InvariantCulture) + " up together";
        return correlation.OverlapDays.ToString(CultureInfo.InvariantCulture) + " shared days • "
               + lead
               + " • "
               + trailing;
    }

    private static IReadOnlyList<UsageTelemetryGitHubWrappedMetricModel> BuildGitHubWrappedFooterMetrics(
        UsageTelemetryGitHubSectionPageModel github) {
        var metrics = new List<UsageTelemetryGitHubWrappedMetricModel> {
            new(
                "Recent repo",
                github.RecentRepositories?.Rows.FirstOrDefault()?.Label ?? "n/a",
                github.RecentRepositories?.Rows.FirstOrDefault()?.Subtitle),
            new(
                "Repository impact",
                github.OwnerImpact?.Headline ?? "n/a",
                github.OwnerImpact?.Note)
        };
        if (github.WatchedRepositories is not null) {
            var watchedRepositories = github.WatchedRepositories;
            metrics.Add(new UsageTelemetryGitHubWrappedMetricModel(
                "Watched momentum",
                watchedRepositories.Headline ?? "n/a",
                watchedRepositories.Note));
        }
        if (github.WatchedCorrelations is not null) {
            var watchedCorrelations = github.WatchedCorrelations;
            metrics.Add(new UsageTelemetryGitHubWrappedMetricModel(
                "Linked movers",
                watchedCorrelations.Rows.FirstOrDefault()?.Label ?? watchedCorrelations.Headline ?? "n/a",
                watchedCorrelations.Rows.FirstOrDefault()?.Value ?? watchedCorrelations.Note));
        }
        if (github.WatchedStarCorrelations is not null) {
            var watchedStarCorrelations = github.WatchedStarCorrelations;
            metrics.Add(new UsageTelemetryGitHubWrappedMetricModel(
                "Star sync",
                watchedStarCorrelations.Rows.FirstOrDefault()?.Label ?? watchedStarCorrelations.Headline ?? "n/a",
                watchedStarCorrelations.Rows.FirstOrDefault()?.Value ?? watchedStarCorrelations.Note));
        }
        if (github.WatchedRepoClusters is not null) {
            var watchedRepoClusters = github.WatchedRepoClusters;
            metrics.Add(new UsageTelemetryGitHubWrappedMetricModel(
                "Related repos",
                watchedRepoClusters.Rows.FirstOrDefault()?.Label ?? watchedRepoClusters.Headline ?? "n/a",
                watchedRepoClusters.Rows.FirstOrDefault()?.Value ?? watchedRepoClusters.Note));
        }
        if (github.WatchedStargazerAudience is not null) {
            var watchedStargazerAudience = github.WatchedStargazerAudience;
            metrics.Add(new UsageTelemetryGitHubWrappedMetricModel(
                "Shared stargazers",
                watchedStargazerAudience.Rows.FirstOrDefault()?.Label ?? watchedStargazerAudience.Headline ?? "n/a",
                watchedStargazerAudience.Rows.FirstOrDefault()?.Value ?? watchedStargazerAudience.Note));
        }
        if (github.WatchedForkNetwork is not null) {
            var watchedForkNetwork = github.WatchedForkNetwork;
            metrics.Add(new UsageTelemetryGitHubWrappedMetricModel(
                "Shared forkers",
                watchedForkNetwork.Rows.FirstOrDefault()?.Label ?? watchedForkNetwork.Headline ?? "n/a",
                watchedForkNetwork.Rows.FirstOrDefault()?.Value ?? watchedForkNetwork.Note));
        }
        if (github.WatchedForkMomentum is not null) {
            var watchedForkMomentum = github.WatchedForkMomentum;
            metrics.Add(new UsageTelemetryGitHubWrappedMetricModel(
                "Rising forks",
                watchedForkMomentum.Rows.FirstOrDefault()?.Label ?? watchedForkMomentum.Headline ?? "n/a",
                watchedForkMomentum.Rows.FirstOrDefault()?.Value ?? watchedForkMomentum.Note));
        }
        if (github.WatchedLocalAlignment is not null) {
            var watchedLocalAlignment = github.WatchedLocalAlignment;
            metrics.Add(new UsageTelemetryGitHubWrappedMetricModel(
                "Local sync",
                watchedLocalAlignment.Rows.FirstOrDefault()?.Label ?? watchedLocalAlignment.Headline ?? "n/a",
                watchedLocalAlignment.Rows.FirstOrDefault()?.Value ?? watchedLocalAlignment.Note));
        }

        return metrics;
    }

    private static string FormatSigned(int value, string suffix) {
        return (value >= 0 ? "+" : "-")
               + Math.Abs(value).ToString(CultureInfo.InvariantCulture)
               + " " + suffix;
    }

    private static string FormatCorrelationValue(double correlation) {
        return correlation.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture);
    }

    private static UsageTelemetryOverviewInsightSection? FindInsight(
        UsageTelemetryOverviewProviderSection section,
        string key) {
        return section.AdditionalInsights.FirstOrDefault(insight =>
            string.Equals(insight.Key, key, StringComparison.OrdinalIgnoreCase));
    }

    private static UsageTelemetryCodeChurnPageModel? BuildCodeChurnSection(GitCodeChurnSummaryData? summary) {
        if (summary is null || !summary.HasData) {
            return null;
        }

        var maxChangedLines = Math.Max(
            1d,
            summary.TrendDays.Count == 0
                ? 1d
                : summary.TrendDays.Max(static day => (double)day.TotalChangedLines));
        var rows = summary.TrendDays
            .Select(day => new UsageTelemetryOverviewInsightRow(
                label: day.DayUtc.ToString("ddd, MMM d", CultureInfo.InvariantCulture),
                value: "+" + FormatCompact((double)day.AddedLines) + " / -" + FormatCompact((double)day.DeletedLines),
                subtitle: day.HasActivity
                    ? day.FilesModified.ToString(CultureInfo.InvariantCulture) + " files • "
                      + day.CommitCount.ToString(CultureInfo.InvariantCulture) + " commits • net "
                      + FormatSigned(day.NetLines, "lines")
                    : "No commits recorded.",
                ratio: Math.Min(1d, day.TotalChangedLines / maxChangedLines)))
            .ToArray();

        var peakDay = summary.PeakRecentDay;
        var note = peakDay is null
            ? "Recent git churn appears after local commits land in this repository."
            : "Peak day " + peakDay.DayUtc.ToString("MMM d", CultureInfo.InvariantCulture)
              + " • +" + FormatCompact((double)peakDay.AddedLines)
              + " / -" + FormatCompact((double)peakDay.DeletedLines)
              + " • " + peakDay.FilesModified.ToString(CultureInfo.InvariantCulture) + " files.";

        return new UsageTelemetryCodeChurnPageModel(
            Title: "Code churn",
            Subtitle: (summary.RepositoryName ?? "Local repository")
                      + " • "
                      + summary.Last30DaysActiveDayCount.ToString(CultureInfo.InvariantCulture)
                      + " active days in the last 30",
            Headline: "+" + FormatCompact((double)summary.RecentAddedLines)
                      + " / -" + FormatCompact((double)summary.RecentDeletedLines)
                      + " over the last 7 days",
            Note: note + " Previous 7d: +" + FormatCompact((double)summary.PreviousAddedLines)
                  + " / -" + FormatCompact((double)summary.PreviousDeletedLines)
                  + " across " + summary.PreviousFilesModified.ToString(CultureInfo.InvariantCulture)
                  + " files and " + summary.PreviousCommitCount.ToString(CultureInfo.InvariantCulture) + " commits.",
            Stats: new[] {
                new UsageTelemetryHeroStatModel("Added", "+" + FormatCompact((double)summary.RecentAddedLines)),
                new UsageTelemetryHeroStatModel("Deleted", "-" + FormatCompact((double)summary.RecentDeletedLines)),
                new UsageTelemetryHeroStatModel("Files", summary.RecentFilesModified.ToString(CultureInfo.InvariantCulture)),
                new UsageTelemetryHeroStatModel("Commits", summary.RecentCommitCount.ToString(CultureInfo.InvariantCulture))
            },
            DailyBreakdown: new UsageTelemetryOverviewInsightSection(
                key: "git-code-churn",
                title: "Daily movement",
                headline: summary.RecentActiveDayCount.ToString(CultureInfo.InvariantCulture) + " active days in the recent window",
                note: summary.LatestCommitAtUtc.HasValue
                    ? "Latest commit " + summary.LatestCommitAtUtc.Value.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture)
                    : "Latest commit time unavailable.",
                rows: rows));
    }

    private static UsageTelemetryChurnUsageSignalPageModel? BuildCodeUsageCorrelationSection(
        GitCodeUsageCorrelationSummaryData? summary) {
        if (summary is null || !summary.HasData) {
            return null;
        }

        var rows = summary.ProviderCorrelations
            .Take(4)
            .Select(correlation => new UsageTelemetryOverviewInsightRow(
                label: correlation.ProviderDisplayName,
                value: FormatCorrelationValue(correlation.Correlation),
                subtitle: FormatCompact(correlation.RecentActivityValue)
                          + " recent "
                          + summary.ActivityUnitsLabel
                          + " • "
                          + correlation.SharedActiveDays.ToString(CultureInfo.InvariantCulture)
                          + "/" + correlation.OverlapDays.ToString(CultureInfo.InvariantCulture)
                          + " shared active days",
                ratio: Math.Min(1d, Math.Abs(correlation.Correlation))))
            .ToArray();

        var noteParts = new List<string> {
            "Recent " + summary.ActivityUnitsLabel + ": " + FormatCompact(summary.RecentActivityTotal),
            "Previous: " + FormatCompact(summary.PreviousActivityTotal),
            "Recent churn: " + FormatCompact((double)summary.RecentChurnVolume) + " lines",
            "Previous churn: " + FormatCompact((double)summary.PreviousChurnVolume) + " lines"
        };
        if (summary.StrongestPositiveCorrelation is not null) {
            noteParts.Add("Aligned: " + summary.StrongestPositiveCorrelation.ProviderDisplayName + " " + FormatCorrelationValue(summary.StrongestPositiveCorrelation.Correlation));
        }
        if (summary.StrongestNegativeCorrelation is not null) {
            noteParts.Add("Diverging: " + summary.StrongestNegativeCorrelation.ProviderDisplayName + " " + FormatCorrelationValue(summary.StrongestNegativeCorrelation.Correlation));
        }

        return new UsageTelemetryChurnUsageSignalPageModel(
            Title: "Churn x usage",
            Subtitle: (summary.RepositoryName ?? "Local repository") + " • recent telemetry alignment",
            Headline: BuildCodeUsageCorrelationHeadline(summary),
            Note: string.Join(" • ", noteParts),
            Stats: new[] {
                new UsageTelemetryHeroStatModel("Recent " + summary.ActivityUnitsLabel, FormatCompact(summary.RecentActivityTotal)),
                new UsageTelemetryHeroStatModel("Previous " + summary.ActivityUnitsLabel, FormatCompact(summary.PreviousActivityTotal)),
                new UsageTelemetryHeroStatModel("Recent churn", FormatCompact((double)summary.RecentChurnVolume) + " lines"),
                new UsageTelemetryHeroStatModel("Active days", summary.RecentActivityDays.ToString(CultureInfo.InvariantCulture))
            },
            ProviderSignals: new UsageTelemetryOverviewInsightSection(
                key: "git-code-usage-correlation",
                title: "Provider signals",
                headline: summary.HasCorrelationSignals
                    ? summary.ProviderCorrelations.Count.ToString(CultureInfo.InvariantCulture) + " provider alignment signals"
                    : "No provider alignment signals yet",
                note: "Correlation is computed from the recent 7-day churn window against provider activity in the same dates.",
                rows: rows));
    }

    private static string BuildCodeUsageCorrelationHeadline(GitCodeUsageCorrelationSummaryData summary) {
        var activityDelta = summary.ActivityDeltaRatio;
        var churnDelta = summary.ChurnDeltaRatio;
        var activityMoving = Math.Abs(activityDelta) >= 0.10d;
        var churnMoving = Math.Abs(churnDelta) >= 0.10d;
        if (!activityMoving && !churnMoving) {
            return "Usage and churn held steady across the recent 7-day pulse.";
        }

        if (activityDelta >= 0d && churnDelta >= 0d) {
            return "Usage and churn rose together across the recent 7-day pulse.";
        }

        if (activityDelta <= 0d && churnDelta <= 0d) {
            return "Usage and churn cooled together across the recent 7-day pulse.";
        }

        return churnDelta > activityDelta
            ? "Churn rose while usage cooled across the recent 7-day pulse."
            : "Usage rose while churn cooled across the recent 7-day pulse.";
    }

    private static string BuildGitHubLocalAlignmentHeadline(GitHubLocalActivityCorrelationSummaryData summary) {
        var strongestPositive = summary.StrongestPositiveCorrelation;
        var strongestNegative = summary.StrongestNegativeCorrelation;
        if (strongestPositive is not null && strongestNegative is not null) {
            return "Strongest local sync: "
                   + BuildShortRepositoryLabel(strongestPositive.RepositoryNameWithOwner)
                   + " "
                   + FormatCorrelation(strongestPositive.Correlation)
                   + " • divergence: "
                   + BuildShortRepositoryLabel(strongestNegative.RepositoryNameWithOwner)
                   + " "
                   + FormatCorrelation(strongestNegative.Correlation);
        }

        if (strongestPositive is not null) {
            return "Strongest local sync: "
                   + BuildShortRepositoryLabel(strongestPositive.RepositoryNameWithOwner)
                   + " "
                   + FormatCorrelation(strongestPositive.Correlation);
        }

        if (strongestNegative is not null) {
            return "Strongest local divergence: "
                   + BuildShortRepositoryLabel(strongestNegative.RepositoryNameWithOwner)
                   + " "
                   + FormatCorrelation(strongestNegative.Correlation);
        }

        return "Watched repo sync appears once GitHub momentum overlaps with the same local pulse window.";
    }

    private static string BuildGitHubLocalAlignmentNote(GitHubLocalActivityCorrelationSummaryData summary) {
        return "7d local pulse "
               + FormatCompact((double)summary.RecentChurnVolume)
               + " changed lines • "
               + FormatCompact(summary.RecentUsageTotal)
               + " recent usage units"
               + (summary.RepositoryName is { Length: > 0 } repositoryName
                   ? " • repo " + repositoryName
                   : string.Empty);
    }

    private static string BuildGitHubRepoClusterHeadline(GitHubRepositoryClusterSummaryData summary) {
        if (!summary.HasSignals || summary.StrongestCluster is null) {
            return "Related repo clusters appear once watched repos share more than one supporting signal.";
        }

        return BuildShortRepositoryLabel(summary.StrongestCluster.RepositoryANameWithOwner)
               + " ↔ "
               + BuildShortRepositoryLabel(summary.StrongestCluster.RepositoryBNameWithOwner)
               + " leads at score "
               + summary.StrongestCluster.CompositeScore.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private static string BuildGitHubRepoClusterNote(GitHubRepositoryClusterSummaryData summary) {
        if (!summary.HasSignals) {
            return "Clusters appear after watched repos overlap on star sync, shared stargazers, shared forkers, or the same local pulse.";
        }

        var strongest = summary.StrongestCluster!;
        var parts = new List<string> {
            strongest.SupportingSignalCount.ToString(CultureInfo.InvariantCulture) + " aligned signals"
        };
        if (strongest.SharedStargazerCount > 0) {
            parts.Add(strongest.SharedStargazerCount.ToString(CultureInfo.InvariantCulture) + " shared stargazers");
        }
        if (strongest.SharedForkOwnerCount > 0) {
            parts.Add(strongest.SharedForkOwnerCount.ToString(CultureInfo.InvariantCulture) + " shared forkers");
        }
        if (strongest.LocallyAlignedRepositoryCount == 2) {
            parts.Add("both local " + FormatCorrelationValue(strongest.LocalAlignmentAverageCorrelation));
        }

        return string.Join(" • ", parts);
    }

    private static GitCodeUsageProviderSeriesData[] BuildProviderDailySeries(
        UsageTelemetryOverviewDocument overview,
        bool excludeGitHub = false) {
        return overview.ProviderSections
            .Where(section => !excludeGitHub || !string.Equals(section.ProviderId, "github", StringComparison.OrdinalIgnoreCase))
            .Where(static section => section.Heatmap.Sections.Any(static heatmapSection => heatmapSection.Days.Count > 0))
            .Select(section => new GitCodeUsageProviderSeriesData(
                section.ProviderId,
                section.Title,
                section.Heatmap.Sections
                    .SelectMany(static heatmapSection => heatmapSection.Days)
                    .GroupBy(static day => day.Date.Date)
                    .Select(static group => new GitCodeUsageDailyValueData(
                        group.Key,
                        group.Sum(static day => day.Value),
                        0))
                    .ToArray()))
            .ToArray();
    }

    private static string FormatRange(DateTime? startDayUtc, DateTime? endDayUtc) {
        if (!startDayUtc.HasValue || !endDayUtc.HasValue) {
            return "n/a";
        }

        return startDayUtc.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " to " + endDayUtc.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static string? NormalizeOptional(string? value) {
        return HeatmapText.NormalizeOptionalText(value);
    }

    private static string FormatCompact(decimal value) {
        if (value <= 0m) {
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

    private static string BuildShortRepositoryLabel(string repositoryNameWithOwner) {
        if (string.IsNullOrWhiteSpace(repositoryNameWithOwner)) {
            return "Repository";
        }

        var normalized = repositoryNameWithOwner.Trim();
        var separatorIndex = normalized.LastIndexOf('/');
        return separatorIndex >= 0 && separatorIndex < normalized.Length - 1
            ? normalized.Substring(separatorIndex + 1)
            : normalized;
    }

    private static string FormatCorrelation(double value) {
        return value.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture);
    }

    private static string FindSpotlightCardValue(
        UsageTelemetryOverviewProviderSection section,
        string key,
        string fallback) {
        return section.SpotlightCards.FirstOrDefault(card =>
                   string.Equals(card.Key, key, StringComparison.OrdinalIgnoreCase))?.Value
               ?? fallback;
    }

    private static string? FindSpotlightCardSubtitle(
        UsageTelemetryOverviewProviderSection section,
        string key) {
        return section.SpotlightCards.FirstOrDefault(card =>
            string.Equals(card.Key, key, StringComparison.OrdinalIgnoreCase))?.Subtitle;
    }

    private static long ExtractOwnerMagnitude(string? headline) {
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

    private static UsageTelemetryBreakdownSummaryPageModel BuildBreakdownSummary(
        string breakdownKey,
        string summaryHint,
        HeatmapDocument document) {
        var sections = document.Sections ?? Array.Empty<HeatmapSection>();
        var days = sections.SelectMany(static section => section.Days).OrderBy(static day => day.Date).ToArray();
        var activeDays = days.Where(static day => day.Value > 0d).ToArray();
        var totals = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var day in activeDays) {
            foreach (var pair in day.Breakdown) {
                totals[pair.Key] = totals.TryGetValue(pair.Key, out var existing)
                    ? existing + pair.Value
                    : pair.Value;
            }
        }

        var totalValue = activeDays.Sum(static day => day.Value);
        var isSourceRoot = string.Equals(breakdownKey, "sourceroot", StringComparison.OrdinalIgnoreCase);
        var firstDate = days.Length > 0 ? days[0].Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "n/a";
        var lastDate = days.Length > 0 ? days[days.Length - 1].Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "n/a";
        var peak = activeDays.Length > 0
            ? activeDays.OrderByDescending(static day => day.Value).First()
            : null;

        var stats = new[] {
            new UsageTelemetryHeroStatModel("Range", firstDate + " to " + lastDate),
            new UsageTelemetryHeroStatModel("Active days", activeDays.Length.ToString(CultureInfo.InvariantCulture)),
            new UsageTelemetryHeroStatModel("Total", FormatCompact(totalValue)),
            new UsageTelemetryHeroStatModel("Peak day", peak is null ? "n/a" : peak.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " (" + FormatCompact(peak.Value) + ")"),
            new UsageTelemetryHeroStatModel("Categories", totals.Count.ToString(CultureInfo.InvariantCulture))
        };

        var legendLabelMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in document.LegendItems) {
            if (!string.IsNullOrWhiteSpace(item.Key)) {
                legendLabelMap[item.Key] = item.Label;
            }
            if (!string.IsNullOrWhiteSpace(item.Label)) {
                legendLabelMap[item.Label] = item.Label;
            }
        }

        var topRows = totals
            .OrderByDescending(static pair => pair.Value)
            .Take(10)
            .Select(pair => new UsageTelemetryBreakdownRowModel(
                ResolveLegendLabel(legendLabelMap, pair.Key),
                FormatCompact(pair.Value) + " (" + FormatPercentValue(pair.Value, totalValue) + "%)",
                FormatPercentValue(pair.Value, totalValue) + "% of visible total",
                ComputePercentage(pair.Value, totalValue)))
            .ToArray();

        var sectionRows = sections
            .Select(section => {
                var sectionActive = section.Days.Where(static day => day.Value > 0d).ToArray();
                var sectionTotal = sectionActive.Sum(static day => day.Value);
                return new {
                    section.Title,
                    SectionTotal = sectionTotal,
                    ActiveDays = sectionActive.Length
                };
            })
            .Where(static row => row.SectionTotal > 0d)
            .OrderByDescending(static row => row.SectionTotal)
            .Select(row => new UsageTelemetryBreakdownRowModel(
                row.Title,
                FormatCompact(row.SectionTotal) + " (" + FormatPercentValue(row.SectionTotal, totalValue) + "%)",
                HeatmapDisplayText.FormatActiveDays(row.ActiveDays),
                ComputePercentage(row.SectionTotal, totalValue)))
            .ToArray();

        var secondaryRows = isSourceRoot
            ? BuildSourceFamilyRows(totals, totalValue)
            : sectionRows;

        var overviewNotes = new List<string> { summaryHint };
        if (isSourceRoot) {
            overviewNotes.Add(HeatmapDisplayText.FormatCount(totals.Count, "distinct source root", "distinct source roots") + ", with labels derived from current roots, Windows.old, and future imported sources like WSL or macOS backups.");
            overviewNotes.Add(HeatmapDisplayText.FormatCount(secondaryRows.Count, "source family", "source families") + " currently visible in this comparison.");
        }

        var legendItems = document.LegendItems
            .Select(static item => new UsageTelemetryBreakdownLegendItemModel(item.Label, item.Color))
            .ToArray();

        return new UsageTelemetryBreakdownSummaryPageModel(
            IsSourceRoot: isSourceRoot,
            Stats: stats,
            OverviewTitle: isSourceRoot ? "Source coverage" : "Overview",
            OverviewNotes: overviewNotes,
            TopRows: topRows,
            TopRowsTitle: isSourceRoot ? "Top source roots" : "Top categories",
            SecondaryRows: secondaryRows,
            SecondaryRowsTitle: isSourceRoot ? "Source families" : "Section activity",
            LegendItems: legendItems,
            LegendTitle: "Legend");
    }

    private static IReadOnlyList<UsageTelemetryBreakdownRowModel> BuildSourceFamilyRows(
        IReadOnlyDictionary<string, double> totals,
        double totalValue) {
        var families = new Dictionary<string, SourceFamilySummary>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in totals) {
            var bucket = ResolveSourceFamily(pair.Key);
            if (!families.TryGetValue(bucket, out var existing)) {
                existing = new SourceFamilySummary();
                families[bucket] = existing;
            }

            existing.TotalValue += pair.Value;
            existing.RootLabels.Add(pair.Key);

            var provider = ResolveSourceFamilyProvider(pair.Key);
            if (!string.IsNullOrWhiteSpace(provider)) {
                existing.Providers.Add(provider!);
            }
        }

        return families
            .OrderByDescending(static pair => pair.Value.TotalValue)
            .Select(pair => new UsageTelemetryBreakdownRowModel(
                pair.Key,
                FormatCompact(pair.Value.TotalValue) + " (" + FormatPercentValue(pair.Value.TotalValue, totalValue) + "%)",
                BuildSourceFamilyMeta(pair.Value, totalValue),
                ComputePercentage(pair.Value.TotalValue, totalValue)))
            .ToArray();
    }

    private static string ResolveSourceFamily(string label) {
        if (label.IndexOf("internal", StringComparison.OrdinalIgnoreCase) >= 0) {
            return "Internal runtime";
        }
        if (label.IndexOf("windows.old", StringComparison.OrdinalIgnoreCase) >= 0) {
            return "Windows.old";
        }
        if (label.IndexOf("current", StringComparison.OrdinalIgnoreCase) >= 0) {
            return "Current machine";
        }
        if (label.IndexOf("wsl", StringComparison.OrdinalIgnoreCase) >= 0) {
            return "WSL";
        }
        if (label.IndexOf("mac", StringComparison.OrdinalIgnoreCase) >= 0) {
            return "macOS";
        }

        return "Imported / other";
    }

    private static string BuildSourceFamilyMeta(SourceFamilySummary summary, double totalValue) {
        var parts = new List<string> {
            HeatmapDisplayText.FormatCount(summary.RootLabels.Count, "root", "roots"),
            FormatPercentValue(summary.TotalValue, totalValue) + "% of visible total"
        };

        if (summary.Providers.Count > 0) {
            parts.Add(string.Join(", ", summary.Providers.OrderBy(static provider => provider, StringComparer.OrdinalIgnoreCase)));
        }

        return string.Join(" · ", parts);
    }

    private static string? ResolveSourceFamilyProvider(string label) {
        if (string.IsNullOrWhiteSpace(label)) {
            return null;
        }

        var separatorIndex = label.IndexOf('·');
        var provider = separatorIndex >= 0
            ? label.Substring(0, separatorIndex)
            : label;

        var trimmed = provider.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string ResolveLegendLabel(
        IReadOnlyDictionary<string, string> labelMap,
        string label) {
        return labelMap.TryGetValue(label, out var resolved)
            ? resolved
            : label;
    }

    private static string FormatPercentValue(double numerator, double denominator) {
        return ComputePercentage(numerator, denominator).ToString("0.#", CultureInfo.InvariantCulture);
    }

    private static double ComputePercentage(double numerator, double denominator) {
        if (numerator <= 0d || denominator <= 0d) {
            return 0d;
        }

        return numerator / denominator * 100d;
    }

    private sealed class SourceFamilySummary {
        public double TotalValue { get; set; }
        public HashSet<string> RootLabels { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Providers { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
