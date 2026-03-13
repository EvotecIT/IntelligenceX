using System;
using System.Collections.Generic;

namespace IntelligenceX.Visualization.Heatmaps;

internal sealed record UsageTelemetryOverviewPageModel(
    string Title,
    string? Subtitle,
    IReadOnlyList<UsageTelemetryHeroStatModel> HeroStats,
    IReadOnlyList<UsageTelemetrySectionSwitchModel> SectionSwitches,
    IReadOnlyList<UsageTelemetryOverviewSectionPageModel> Sections,
    IReadOnlyList<UsageTelemetrySupportingBreakdownModel> SupportingBreakdowns,
    string BootstrapJson,
    string Footnote);

internal sealed record UsageTelemetryHeroStatModel(string Label, string Value);

internal sealed record UsageTelemetrySectionSwitchModel(string Key, string Label);

internal sealed record UsageTelemetryOverviewSectionPageModel(
    string ProviderSectionId,
    string ProviderId,
    string Title,
    string Subtitle,
    UsageTelemetryOverviewProviderSection Section,
    UsageTelemetryOverviewSectionFlags Flags,
    IReadOnlyList<UsageTelemetryToggleOptionModel> DatasetTabs,
    UsageTelemetryProviderAccentColors AccentColors,
    UsageTelemetryGitHubSectionPageModel? GitHub);

internal sealed record UsageTelemetryOverviewSectionFlags(
    bool IsGitHub,
    bool HasActivity,
    bool HasMonthly,
    bool HasModels,
    bool HasPricing,
    bool HasComposition,
    bool HasAdditionalInsights,
    bool UseSummaryGrid);

internal sealed record UsageTelemetrySupportingBreakdownModel(
    string Key,
    string Label,
    string? Subtitle,
    bool IsDefault,
    UsageTelemetryBreakdownSummaryPageModel Summary);

internal sealed record UsageTelemetryBreakdownPageModel(
    string ReportTitle,
    string BreakdownKey,
    string BreakdownLabel,
    string SummaryHint,
    string BootstrapJson,
    UsageTelemetryBreakdownSummaryPageModel Summary);

internal sealed record UsageTelemetryBreakdownSummaryPageModel(
    bool IsSourceRoot,
    IReadOnlyList<UsageTelemetryHeroStatModel> Stats,
    string OverviewTitle,
    IReadOnlyList<string> OverviewNotes,
    IReadOnlyList<UsageTelemetryBreakdownRowModel> TopRows,
    string TopRowsTitle,
    IReadOnlyList<UsageTelemetryBreakdownRowModel> SecondaryRows,
    string SecondaryRowsTitle,
    IReadOnlyList<UsageTelemetryBreakdownLegendItemModel> LegendItems,
    string LegendTitle);

internal sealed record UsageTelemetryBreakdownRowModel(
    string Label,
    string Value,
    string? Meta,
    double RatioPercent);

internal sealed record UsageTelemetryBreakdownLegendItemModel(
    string Label,
    string Color);

internal sealed record UsageTelemetryProviderAccentColors(string Input, string Output, string Total, string Other);

internal sealed record UsageTelemetryToggleOptionModel(
    string Key,
    string Label,
    bool IsDefault,
    string? Href = null);

internal sealed record UsageTelemetryGitHubSectionPageModel(
    IReadOnlyList<UsageTelemetryToggleOptionModel> Lenses,
    IReadOnlyList<UsageTelemetryToggleOptionModel> RepoSortModes,
    IReadOnlyList<UsageTelemetryToggleOptionModel> OwnerScopes,
    UsageTelemetryOverviewInsightSection? YearComparison,
    UsageTelemetryOverviewInsightSection? ScopeSplit,
    UsageTelemetryOverviewInsightSection? RecentRepositories,
    UsageTelemetryOverviewInsightSection? OwnerImpact,
    UsageTelemetryOverviewInsightSection? TopRepositories,
    UsageTelemetryOverviewInsightSection? TopRepositoriesByForks,
    UsageTelemetryOverviewInsightSection? TopRepositoriesByHealth,
    UsageTelemetryOverviewInsightSection? TopLanguages,
    IReadOnlyList<UsageTelemetryOverviewInsightSection> OwnerSections);

internal sealed record UsageTelemetryGitHubWrappedPageModel(
    string Title,
    string Subtitle,
    string? Note,
    string BootstrapJson,
    IReadOnlyList<UsageTelemetryOverviewSectionMetric> Metrics,
    IReadOnlyList<UsageTelemetryOverviewCard> SpotlightCards,
    IReadOnlyList<UsageTelemetryOverviewMonthlyUsage> MonthlyUsage,
    int LongestStreakDays,
    int CurrentStreakDays,
    UsageTelemetryOverviewInsightSection? YearComparison,
    UsageTelemetryOverviewInsightSection? ScopeSplit,
    UsageTelemetryOverviewInsightSection? OwnerImpact,
    UsageTelemetryOverviewInsightSection? TopLanguages,
    UsageTelemetryOverviewInsightSection? RecentRepositories,
    UsageTelemetryOverviewInsightSection? TopRepositories,
    UsageTelemetryOverviewInsightSection? TopRepositoriesByForks,
    UsageTelemetryOverviewInsightSection? TopRepositoriesByHealth,
    IReadOnlyList<UsageTelemetryGitHubWrappedOwnerPanelModel> OwnerPanels);

internal sealed record UsageTelemetryGitHubWrappedOwnerPanelModel(
    string Key,
    string Label,
    UsageTelemetryOverviewInsightSection Section);

internal sealed record UsageTelemetryGitHubWrappedCardPageModel(
    string Title,
    string Subtitle,
    string BootstrapJson,
    IReadOnlyList<UsageTelemetryGitHubWrappedMetricModel> Metrics,
    IReadOnlyList<UsageTelemetryGitHubWrappedMetricModel> Stats,
    IReadOnlyList<UsageTelemetryGitHubWrappedMetricModel> FooterMetrics);

internal sealed record UsageTelemetryGitHubWrappedMetricModel(
    string Label,
    string Value,
    string? Copy);
