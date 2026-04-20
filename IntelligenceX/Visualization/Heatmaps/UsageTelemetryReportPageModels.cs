using System;
using System.Collections.Generic;

namespace IntelligenceX.Visualization.Heatmaps;

internal sealed record UsageTelemetryOverviewPageModel(
    string Title,
    string? Subtitle,
    IReadOnlyList<UsageTelemetryHeroStatModel> HeroStats,
    UsageTelemetryCodeChurnPageModel? CodeChurn,
    UsageTelemetryChurnUsageSignalPageModel? ChurnUsageCorrelation,
    UsageTelemetryGitHubLocalPulsePageModel? GitHubLocalAlignment,
    UsageTelemetryGitHubRepoClusterPageModel? GitHubRepoClusters,
    UsageTelemetryConversationPulsePageModel? ConversationPulse,
    IReadOnlyList<UsageTelemetrySectionSwitchModel> SectionSwitches,
    IReadOnlyList<UsageTelemetryOverviewSectionPageModel> Sections,
    IReadOnlyList<UsageTelemetrySupportingBreakdownModel> SupportingBreakdowns,
    UsageTelemetryReportDiagnosticsModel? Diagnostics,
    string BootstrapJson,
    string Footnote);

internal sealed record UsageTelemetryHeroStatModel(string Label, string Value);

internal sealed record UsageTelemetryCodeChurnPageModel(
    string Title,
    string Subtitle,
    string Headline,
    string? Note,
    IReadOnlyList<UsageTelemetryHeroStatModel> Stats,
    UsageTelemetryOverviewInsightSection DailyBreakdown);

internal sealed record UsageTelemetryChurnUsageSignalPageModel(
    string Title,
    string Subtitle,
    string Headline,
    string? Note,
    IReadOnlyList<UsageTelemetryHeroStatModel> Stats,
    UsageTelemetryOverviewInsightSection ProviderSignals);

internal sealed record UsageTelemetryGitHubLocalPulsePageModel(
    string Title,
    string Subtitle,
    string Headline,
    string? Note,
    IReadOnlyList<UsageTelemetryHeroStatModel> Stats,
    UsageTelemetryOverviewInsightSection Repositories);

internal sealed record UsageTelemetryGitHubRepoClusterPageModel(
    string Title,
    string Subtitle,
    string Headline,
    string? Note,
    IReadOnlyList<UsageTelemetryHeroStatModel> Stats,
    UsageTelemetryOverviewInsightSection Clusters);

internal sealed record UsageTelemetryConversationPulsePageModel(
    string Title,
    string Subtitle,
    string Headline,
    string? Note,
    IReadOnlyList<UsageTelemetryHeroStatModel> Stats,
    UsageTelemetryOverviewInsightSection Conversations,
    IReadOnlyList<UsageTelemetryConversationPulseRowPageModel> Rows);

internal sealed record UsageTelemetryConversationPulseRowPageModel(
    int Rank,
    string SessionLabel,
    string SessionCode,
    string TitleText,
    string? ContextText,
    string? RepositoryText,
    string? WorkspaceText,
    long TotalTokensRaw,
    long DurationMs,
    int TurnCountRaw,
    int CompactCountRaw,
    double CostUsdRaw,
    string TokenText,
    string ShareText,
    string StartedText,
    string SpanText,
    string? ActiveText,
    string TurnText,
    string? CompactText,
    string AccountText,
    string ModelText,
    string SurfaceText,
    string? CostText,
    double RatioPercent);

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
    UsageTelemetryReportDiagnosticsModel? Diagnostics,
    IReadOnlyList<string> HealthAccountLabels,
    IReadOnlyList<UsageTelemetryOverviewInsightSection> HealthInsights,
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
    string FileStem,
    string Label,
    string? Subtitle,
    bool IsDefault,
    UsageTelemetryBreakdownSummaryPageModel Summary);

internal sealed record UsageTelemetryBreakdownPageModel(
    string ReportTitle,
    string BreakdownKey,
    string FileStem,
    string BreakdownLabel,
    string SummaryHint,
    UsageTelemetryReportDiagnosticsModel? Diagnostics,
    string BootstrapJson,
    UsageTelemetryBreakdownSummaryPageModel Summary);

internal sealed record UsageTelemetryReportDiagnosticsModel(
    string Title,
    IReadOnlyList<UsageTelemetryReportDiagnosticsItemModel> Items,
    string? Note);

internal sealed record UsageTelemetryReportDiagnosticsItemModel(
    string Label,
    string Value,
    string? Copy);

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
    UsageTelemetryOverviewInsightSection? WatchedRepositories,
    UsageTelemetryOverviewInsightSection? WatchedCorrelations,
    UsageTelemetryOverviewInsightSection? WatchedStarCorrelations,
    UsageTelemetryOverviewInsightSection? WatchedRepoClusters,
    UsageTelemetryOverviewInsightSection? WatchedStargazerAudience,
    UsageTelemetryOverviewInsightSection? WatchedForkNetwork,
    UsageTelemetryOverviewInsightSection? WatchedForkMomentum,
    UsageTelemetryOverviewInsightSection? WatchedLocalAlignment,
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
    UsageTelemetryReportDiagnosticsModel? Diagnostics,
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
    UsageTelemetryOverviewInsightSection? WatchedRepositories,
    UsageTelemetryOverviewInsightSection? WatchedCorrelations,
    UsageTelemetryOverviewInsightSection? WatchedStarCorrelations,
    UsageTelemetryOverviewInsightSection? WatchedRepoClusters,
    UsageTelemetryOverviewInsightSection? WatchedStargazerAudience,
    UsageTelemetryOverviewInsightSection? WatchedForkNetwork,
    UsageTelemetryOverviewInsightSection? WatchedForkMomentum,
    UsageTelemetryOverviewInsightSection? WatchedLocalAlignment,
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
    UsageTelemetryReportDiagnosticsModel? Diagnostics,
    string BootstrapJson,
    IReadOnlyList<UsageTelemetryGitHubWrappedMetricModel> Metrics,
    IReadOnlyList<UsageTelemetryGitHubWrappedMetricModel> Stats,
    IReadOnlyList<UsageTelemetryGitHubWrappedMetricModel> FooterMetrics);

internal sealed record UsageTelemetryGitHubWrappedMetricModel(
    string Label,
    string Value,
    string? Copy);
