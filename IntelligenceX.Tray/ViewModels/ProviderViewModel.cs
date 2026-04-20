using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using IntelligenceX.Json;
using IntelligenceX.Telemetry.Git;
using IntelligenceX.Telemetry.GitHub;
using IntelligenceX.Telemetry.Limits;
using IntelligenceX.Telemetry.Usage;
using IntelligenceX.Tray.Services;
using IntelligenceX.Visualization.Heatmaps;
using Microsoft.Win32;

namespace IntelligenceX.Tray.ViewModels;

public enum ProviderTimeRange {
    Today,
    Last7Days,
    Last30Days,
    AllTime
}

public enum ProviderEventSort {
    MostRecent,
    MostTokens,
    HighestCost,
    Model
}

public enum ProviderComparisonSort {
    Tokens,
    Cost,
    Events
}

internal static class ProviderFilterDefaults {
    public const string AllAccounts = "All accounts";
    public const string AllModels = "All models";
    public const string AllSurfaces = "All surfaces";
}

/// <summary>
/// Holds aggregated usage data for a single provider or the combined "All" view.
/// </summary>
public sealed class ProviderViewModel : ViewModelBase {
    private string _providerId = string.Empty;
    private string _displayName = string.Empty;
    private string _shortName = string.Empty;
    private string _iconKey = "";
    private System.Windows.Media.Geometry? _iconGeometry;
    private int _sortOrder;
    private bool _isFavorite;
    private int _newEventsSinceRefresh;
    private long _newTokensSinceRefresh;
    private Brush _accentBrush = Brushes.White;
    private Color _inputColor;
    private Color _outputColor;
    private Color _totalColor;

    // Today
    private long _todayTotalTokens;
    private long _todayInputTokens;
    private long _todayOutputTokens;
    private long _todayCachedTokens;
    private long _todayReasoningTokens;
    private decimal _todayCostUsd;
    private bool _todayCostUsesEstimate;
    private int _todayEventCount;

    // 7-day rolling
    private long _weeklyTotalTokens;
    private long _weeklyAvgPerDay;
    private decimal _weeklyCostUsd;
    private bool _weeklyCostUsesEstimate;

    // 30-day rolling
    private long _monthlyTotalTokens;
    private long _monthlyAvgPerDay;
    private decimal _monthlyCostUsd;
    private bool _monthlyCostUsesEstimate;

    private DateTimeOffset _lastUpdated;
    private string? _limitPlanLabel;
    private string? _limitAccountLabel;
    private string? _limitSummary;
    private string? _limitSourceLabel;
    private string? _limitStatusMessage;
    private string? _recommendedLimitAccountLabel;
    private string? _recommendedLimitAccountSummary;
    private string? _limitAccountsOverviewText;
    private string? _usageHealthSummary;
    private string? _usageHealthDetail;
    private string? _usageHealthAccountsText;
    private string? _scopeLocalText;
    private string? _scopeOnlineText;
    private string? _scopeDifferenceText;
    private string _todayLabel = "Today";
    private string _weeklyLabel = "7 days";
    private string _monthlyLabel = "30 days";
    private readonly List<UsageEventRecord> _usageEvents = [];
    private readonly List<UsageEventRecord> _conversationEvents = [];
    private readonly List<UsageConversationSummary> _conversationSummaryData = [];
    private readonly List<ConversationUsageViewModel> _conversationSummaries = [];
    private ProviderTimeRange _selectedRange = ProviderTimeRange.Today;
    private string _actionStatusMessage = string.Empty;
    private string _selectedAccountFilter = ProviderFilterDefaults.AllAccounts;
    private string _selectedModelFilter = ProviderFilterDefaults.AllModels;
    private string _selectedSurfaceFilter = ProviderFilterDefaults.AllSurfaces;
    private int _filteredEventCount;
    private ProviderEventSort _selectedEventSort = ProviderEventSort.MostRecent;
    private ProviderComparisonSort _selectedProviderComparisonSort = ProviderComparisonSort.Tokens;
    private bool _isApplyingExplorerPreferences;
    private RecentUsageItemViewModel? _selectedEvent;
    private IReadOnlyDictionary<string, ProviderComparisonHealthInfo> _providerComparisonHealth = new Dictionary<string, ProviderComparisonHealthInfo>(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyDictionary<string, ProviderComparisonDeltaInfo> _providerComparisonDelta = new Dictionary<string, ProviderComparisonDeltaInfo>(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyDictionary<string, ProviderComparisonHistoryInfo> _providerComparisonHistory = new Dictionary<string, ProviderComparisonHistoryInfo>(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _providerComparisonFavorites = new(StringComparer.OrdinalIgnoreCase);
    private GitCodeChurnSummaryData _codeChurnSummary = GitCodeChurnSummaryData.Empty;
    private GitCodeUsageCorrelationSummaryData _codeUsageCorrelationSummary = GitCodeUsageCorrelationSummaryData.Empty;
    private GitHubLocalActivityCorrelationSummaryData _gitHubLocalActivityCorrelationSummary = GitHubLocalActivityCorrelationSummaryData.Empty;
    private GitHubRepositoryClusterSummaryData _gitHubRepositoryClusterSummary = GitHubRepositoryClusterSummaryData.Empty;

    public ProviderViewModel() {
        CopySummaryCommand = new RelayCommand(CopySummaryAsync);
        CopySelectedEventCommand = new RelayCommand(CopySelectedEventAsync, () => SelectedEvent is not null);
        CopySelectedEventJsonCommand = new RelayCommand(CopySelectedEventJsonAsync, () => SelectedEvent is not null);
        FilterToSelectedModelCommand = new RelayCommand(FilterToSelectedModelAsync, () => SelectedEvent is not null);
        FilterToSelectedSurfaceCommand = new RelayCommand(FilterToSelectedSurfaceAsync, () => SelectedEvent is not null);
        FilterToSelectedAccountCommand = new RelayCommand(FilterToSelectedAccountAsync, () => SelectedEvent is not null);
        ClearFiltersCommand = new RelayCommand(ClearFiltersAsync, () => HasActiveFilters);
        ExportJsonCommand = new RelayCommand(ExportJsonAsync);
        ExportCsvCommand = new RelayCommand(ExportCsvAsync);
        OpenDetailedReportCommand = new RelayCommand(OpenDetailedReportAsync);
        LimitWindows.CollectionChanged += (_, _) => {
            OnPropertyChanged(nameof(HasLiveLimitData));
            OnPropertyChanged(nameof(ShowSharedLimitWindows));
            OnPropertyChanged(nameof(HasLimitSection));
        };
        LimitAccounts.CollectionChanged += (_, _) => {
            OnPropertyChanged(nameof(HasLimitAccounts));
            OnPropertyChanged(nameof(HasMultipleLimitAccounts));
            OnPropertyChanged(nameof(ShowSharedLimitWindows));
            OnPropertyChanged(nameof(HasLimitSection));
        };
        AccountBreakdown.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasAccountBreakdown));
        SurfaceBreakdown.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasSurfaceBreakdown));
        ModelDaySummaries.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasModelDaySummaries));
        RecentActivity.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasRecentActivity));
        Conversations.CollectionChanged += (_, _) => {
            OnPropertyChanged(nameof(HasConversationStats));
            OnPropertyChanged(nameof(ConversationStatsSummaryText));
        };
        ProviderComparison.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasProviderComparison));
        CombinedOverviewCards.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasCombinedOverviewCards));
        CodeChurnBars.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasCodeChurnBars));
    }

    public string ProviderId {
        get => _providerId;
        set {
            if (SetProperty(ref _providerId, value)) {
                OnPropertyChanged(nameof(IsCombinedProvider));
                OnPropertyChanged(nameof(HasProviderComparison));
                OnPropertyChanged(nameof(HasCombinedOverviewCards));
                OnPropertyChanged(nameof(HasCodeChurn));
                OnPropertyChanged(nameof(HasCodeChurnBars));
                OnPropertyChanged(nameof(HasCodeUsageCorrelation));
                OnPropertyChanged(nameof(HasPositiveCodeUsageCorrelation));
                OnPropertyChanged(nameof(HasNegativeCodeUsageCorrelation));
            }
        }
    }

    public string DisplayName {
        get => _displayName;
        set => SetProperty(ref _displayName, value);
    }

    public string ShortName {
        get => _shortName;
        set => SetProperty(ref _shortName, value);
    }

    public string IconKey {
        get => _iconKey;
        set {
            if (!SetProperty(ref _iconKey, value)) {
                return;
            }

            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher?.CheckAccess() == true) {
                IconGeometry = ResolveIconGeometry(value);
            }
        }
    }

    public System.Windows.Media.Geometry? IconGeometry {
        get => _iconGeometry;
        private set => SetProperty(ref _iconGeometry, value);
    }

    private static System.Windows.Media.Geometry? ResolveIconGeometry(string? key) {
        if (string.IsNullOrWhiteSpace(key)) return null;
        return System.Windows.Application.Current?.TryFindResource(key) as System.Windows.Media.Geometry;
    }

    public void RefreshIconGeometry() {
        IconGeometry = ResolveIconGeometry(IconKey);
    }

    public void ApplyRefreshDelta(long tokenDelta, int eventDelta) {
        NewTokensSinceRefresh = Math.Max(0L, tokenDelta);
        NewEventsSinceRefresh = Math.Max(0, eventDelta);
    }

    public void ClearRefreshBadge() {
        NewTokensSinceRefresh = 0L;
        NewEventsSinceRefresh = 0;
    }

    public int SortOrder {
        get => _sortOrder;
        set => SetProperty(ref _sortOrder, value);
    }

    public bool IsFavorite {
        get => _isFavorite;
        set => SetProperty(ref _isFavorite, value);
    }

    public int NewEventsSinceRefresh {
        get => _newEventsSinceRefresh;
        private set {
            if (SetProperty(ref _newEventsSinceRefresh, value)) {
                OnPropertyChanged(nameof(HasRefreshBadge));
                OnPropertyChanged(nameof(RefreshBadgeText));
                OnPropertyChanged(nameof(RefreshBadgeToolTip));
            }
        }
    }

    public long NewTokensSinceRefresh {
        get => _newTokensSinceRefresh;
        private set {
            if (SetProperty(ref _newTokensSinceRefresh, value)) {
                OnPropertyChanged(nameof(RefreshBadgeToolTip));
            }
        }
    }

    public bool HasRefreshBadge => NewEventsSinceRefresh > 0;

    public string RefreshBadgeText => NewEventsSinceRefresh switch {
        > 99 => "99+",
        > 0 => "+" + NewEventsSinceRefresh.ToString("N0", CultureInfo.CurrentCulture),
        _ => string.Empty
    };

    public string RefreshBadgeToolTip {
        get {
            if (NewEventsSinceRefresh <= 0) {
                return "No new activity since the previous refresh.";
            }

            var summary = "+" + NewEventsSinceRefresh.ToString("N0", CultureInfo.CurrentCulture) + " new rollups";
            if (NewTokensSinceRefresh > 0) {
                summary += " • +" + FormatTokens(NewTokensSinceRefresh) + " tokens";
            }

            return summary + " since the previous refresh.";
        }
    }

    public Brush AccentBrush {
        get => _accentBrush;
        set => SetProperty(ref _accentBrush, value);
    }

    public Color InputColor {
        get => _inputColor;
        set => SetProperty(ref _inputColor, value);
    }

    public Color OutputColor {
        get => _outputColor;
        set => SetProperty(ref _outputColor, value);
    }

    public Color TotalColor {
        get => _totalColor;
        set => SetProperty(ref _totalColor, value);
    }

    // -- Today --
    // Bar widths for the token distribution bar (max ~310px)
    public double InputBarWidth => TodayTotalTokens > 0
        ? Math.Max(2, 310.0 * TodayInputTokens / TodayTotalTokens)
        : 2;
    public double OutputBarWidth => TodayTotalTokens > 0
        ? Math.Max(2, 310.0 * TodayOutputTokens / TodayTotalTokens)
        : 2;

    public long TodayTotalTokens {
        get => _todayTotalTokens;
        set {
            if (SetProperty(ref _todayTotalTokens, value)) {
                OnPropertyChanged(nameof(TodayTotalTokensFormatted));
                OnPropertyChanged(nameof(InputBarWidth));
                OnPropertyChanged(nameof(OutputBarWidth));
            }
        }
    }

    public string TodayTotalTokensFormatted => FormatTokens(TodayTotalTokens);

    public string TodayLabel {
        get => _todayLabel;
        set => SetProperty(ref _todayLabel, value);
    }

    public ProviderTimeRange SelectedRange {
        get => _selectedRange;
        set {
            if (!SetProperty(ref _selectedRange, value)) {
                return;
            }

            OnPropertyChanged(nameof(IsTodayRangeSelected));
            OnPropertyChanged(nameof(IsLast7DaysRangeSelected));
            OnPropertyChanged(nameof(IsLast30DaysRangeSelected));
            OnPropertyChanged(nameof(IsAllTimeRangeSelected));
            if (!_isApplyingExplorerPreferences) {
                RebuildSelectedRangeViews();
            }
        }
    }

    public bool IsTodayRangeSelected => SelectedRange == ProviderTimeRange.Today;
    public bool IsLast7DaysRangeSelected => SelectedRange == ProviderTimeRange.Last7Days;
    public bool IsLast30DaysRangeSelected => SelectedRange == ProviderTimeRange.Last30Days;
    public bool IsAllTimeRangeSelected => SelectedRange == ProviderTimeRange.AllTime;

    public string SelectedAccountFilter {
        get => _selectedAccountFilter;
        set {
            if (SetProperty(ref _selectedAccountFilter, value)) {
                OnPropertyChanged(nameof(HasActiveFilters));
                ClearFiltersCommand.RaiseCanExecuteChanged();
                if (!_isApplyingExplorerPreferences) {
                    RebuildSelectedRangeViews();
                }
            }
        }
    }

    public string SelectedModelFilter {
        get => _selectedModelFilter;
        set {
            if (SetProperty(ref _selectedModelFilter, value)) {
                OnPropertyChanged(nameof(HasActiveFilters));
                ClearFiltersCommand.RaiseCanExecuteChanged();
                if (!_isApplyingExplorerPreferences) {
                    RebuildSelectedRangeViews();
                }
            }
        }
    }

    public string SelectedSurfaceFilter {
        get => _selectedSurfaceFilter;
        set {
            if (SetProperty(ref _selectedSurfaceFilter, value)) {
                OnPropertyChanged(nameof(HasActiveFilters));
                ClearFiltersCommand.RaiseCanExecuteChanged();
                if (!_isApplyingExplorerPreferences) {
                    RebuildSelectedRangeViews();
                }
            }
        }
    }

    public ProviderEventSort SelectedEventSort {
        get => _selectedEventSort;
        set {
            if (!SetProperty(ref _selectedEventSort, value)) {
                return;
            }

            OnPropertyChanged(nameof(IsMostRecentSortSelected));
            OnPropertyChanged(nameof(IsMostTokensSortSelected));
            OnPropertyChanged(nameof(IsHighestCostSortSelected));
            OnPropertyChanged(nameof(IsModelSortSelected));
            if (!_isApplyingExplorerPreferences) {
                RebuildSelectedRangeViews();
            }
        }
    }

    public bool IsMostRecentSortSelected => SelectedEventSort == ProviderEventSort.MostRecent;
    public bool IsMostTokensSortSelected => SelectedEventSort == ProviderEventSort.MostTokens;
    public bool IsHighestCostSortSelected => SelectedEventSort == ProviderEventSort.HighestCost;
    public bool IsModelSortSelected => SelectedEventSort == ProviderEventSort.Model;

    public ProviderComparisonSort SelectedProviderComparisonSort {
        get => _selectedProviderComparisonSort;
        set {
            if (!SetProperty(ref _selectedProviderComparisonSort, value)) {
                return;
            }

            OnPropertyChanged(nameof(IsProviderComparisonTokensSortSelected));
            OnPropertyChanged(nameof(IsProviderComparisonCostSortSelected));
            OnPropertyChanged(nameof(IsProviderComparisonEventsSortSelected));
            if (!_isApplyingExplorerPreferences) {
                RebuildSelectedRangeViews();
            }
        }
    }

    public bool IsProviderComparisonTokensSortSelected => SelectedProviderComparisonSort == ProviderComparisonSort.Tokens;
    public bool IsProviderComparisonCostSortSelected => SelectedProviderComparisonSort == ProviderComparisonSort.Cost;
    public bool IsProviderComparisonEventsSortSelected => SelectedProviderComparisonSort == ProviderComparisonSort.Events;

    public long TodayInputTokens {
        get => _todayInputTokens;
        set {
            if (SetProperty(ref _todayInputTokens, value)) {
                OnPropertyChanged(nameof(TodayInputTokensFormatted));
            }
        }
    }

    public string TodayInputTokensFormatted => FormatTokens(TodayInputTokens);

    public long TodayOutputTokens {
        get => _todayOutputTokens;
        set {
            if (SetProperty(ref _todayOutputTokens, value)) {
                OnPropertyChanged(nameof(TodayOutputTokensFormatted));
            }
        }
    }

    public string TodayOutputTokensFormatted => FormatTokens(TodayOutputTokens);

    public long TodayCachedTokens {
        get => _todayCachedTokens;
        set {
            if (SetProperty(ref _todayCachedTokens, value)) {
                OnPropertyChanged(nameof(TodayCachedTokensFormatted));
            }
        }
    }

    public string TodayCachedTokensFormatted => FormatTokens(TodayCachedTokens);

    public long TodayReasoningTokens {
        get => _todayReasoningTokens;
        set {
            if (SetProperty(ref _todayReasoningTokens, value)) {
                OnPropertyChanged(nameof(TodayReasoningTokensFormatted));
            }
        }
    }

    public string TodayReasoningTokensFormatted => FormatTokens(TodayReasoningTokens);

    public decimal TodayCostUsd {
        get => _todayCostUsd;
        set {
            if (SetProperty(ref _todayCostUsd, value)) {
                OnPropertyChanged(nameof(TodayCostFormatted));
                OnPropertyChanged(nameof(HasTodayCost));
            }
        }
    }

    public bool TodayCostUsesEstimate {
        get => _todayCostUsesEstimate;
        set {
            if (SetProperty(ref _todayCostUsesEstimate, value)) {
                OnPropertyChanged(nameof(TodayCostFormatted));
            }
        }
    }

    public string TodayCostFormatted => FormatCostDisplay(TodayCostUsd, TodayCostUsesEstimate);
    public bool HasTodayCost => TodayCostUsd > 0;

    public int TodayEventCount {
        get => _todayEventCount;
        set {
            if (SetProperty(ref _todayEventCount, value)) {
                OnPropertyChanged(nameof(TodayRollupCountText));
            }
        }
    }
    public string TodayRollupCountText => FormatCountLabel(TodayEventCount, "rollup", "rollups");

    // -- 7-day --
    public long WeeklyTotalTokens {
        get => _weeklyTotalTokens;
        set {
            if (SetProperty(ref _weeklyTotalTokens, value)) {
                OnPropertyChanged(nameof(WeeklyTotalTokensFormatted));
            }
        }
    }

    public string WeeklyTotalTokensFormatted => FormatTokens(WeeklyTotalTokens);

    public string WeeklyLabel {
        get => _weeklyLabel;
        set => SetProperty(ref _weeklyLabel, value);
    }

    public long WeeklyAvgPerDay {
        get => _weeklyAvgPerDay;
        set {
            if (SetProperty(ref _weeklyAvgPerDay, value)) {
                OnPropertyChanged(nameof(WeeklyAvgPerDayFormatted));
            }
        }
    }

    public string WeeklyAvgPerDayFormatted => FormatTokens(WeeklyAvgPerDay) + "/day";

    public decimal WeeklyCostUsd {
        get => _weeklyCostUsd;
        set {
            if (SetProperty(ref _weeklyCostUsd, value)) {
                OnPropertyChanged(nameof(WeeklyCostFormatted));
                OnPropertyChanged(nameof(HasWeeklyCost));
            }
        }
    }

    public bool WeeklyCostUsesEstimate {
        get => _weeklyCostUsesEstimate;
        set {
            if (SetProperty(ref _weeklyCostUsesEstimate, value)) {
                OnPropertyChanged(nameof(WeeklyCostFormatted));
            }
        }
    }

    public string WeeklyCostFormatted => FormatCostDisplay(WeeklyCostUsd, WeeklyCostUsesEstimate);
    public bool HasWeeklyCost => WeeklyCostUsd > 0;

    // -- 30-day --
    public long MonthlyTotalTokens {
        get => _monthlyTotalTokens;
        set {
            if (SetProperty(ref _monthlyTotalTokens, value)) {
                OnPropertyChanged(nameof(MonthlyTotalTokensFormatted));
            }
        }
    }

    public string MonthlyTotalTokensFormatted => FormatTokens(MonthlyTotalTokens);

    public string MonthlyLabel {
        get => _monthlyLabel;
        set => SetProperty(ref _monthlyLabel, value);
    }

    public long MonthlyAvgPerDay {
        get => _monthlyAvgPerDay;
        set {
            if (SetProperty(ref _monthlyAvgPerDay, value)) {
                OnPropertyChanged(nameof(MonthlyAvgPerDayFormatted));
            }
        }
    }

    public string MonthlyAvgPerDayFormatted => FormatTokens(MonthlyAvgPerDay) + "/day";

    public decimal MonthlyCostUsd {
        get => _monthlyCostUsd;
        set {
            if (SetProperty(ref _monthlyCostUsd, value)) {
                OnPropertyChanged(nameof(MonthlyCostFormatted));
                OnPropertyChanged(nameof(HasMonthlyCost));
            }
        }
    }

    public bool MonthlyCostUsesEstimate {
        get => _monthlyCostUsesEstimate;
        set {
            if (SetProperty(ref _monthlyCostUsesEstimate, value)) {
                OnPropertyChanged(nameof(MonthlyCostFormatted));
            }
        }
    }

    public string MonthlyCostFormatted => FormatCostDisplay(MonthlyCostUsd, MonthlyCostUsesEstimate);
    public bool HasMonthlyCost => MonthlyCostUsd > 0;

    public DateTimeOffset LastUpdated {
        get => _lastUpdated;
        set {
            if (SetProperty(ref _lastUpdated, value)) {
                OnPropertyChanged(nameof(LastUpdatedFormatted));
            }
        }
    }

    public string LastUpdatedFormatted => LastUpdated == default
        ? "Never"
        : LastUpdated.ToLocalTime().ToString("HH:mm:ss");

    public string? LimitPlanLabel {
        get => _limitPlanLabel;
        set {
            if (SetProperty(ref _limitPlanLabel, value)) {
                OnPropertyChanged(nameof(HasLimitSection));
            }
        }
    }

    public string? LimitAccountLabel {
        get => _limitAccountLabel;
        set {
            if (SetProperty(ref _limitAccountLabel, value)) {
                OnPropertyChanged(nameof(HasLimitSection));
            }
        }
    }

    public string? LimitSummary {
        get => _limitSummary;
        set {
            if (SetProperty(ref _limitSummary, value)) {
                OnPropertyChanged(nameof(HasLimitSummary));
                OnPropertyChanged(nameof(HasLimitSection));
            }
        }
    }

    public string? LimitSourceLabel {
        get => _limitSourceLabel;
        set {
            if (SetProperty(ref _limitSourceLabel, value)) {
                OnPropertyChanged(nameof(HasLimitSection));
            }
        }
    }

    public string? LimitStatusMessage {
        get => _limitStatusMessage;
        set {
            if (SetProperty(ref _limitStatusMessage, value)) {
                OnPropertyChanged(nameof(HasLimitStatusMessage));
                OnPropertyChanged(nameof(HasLimitSection));
            }
        }
    }

    public string? RecommendedLimitAccountLabel {
        get => _recommendedLimitAccountLabel;
        set {
            if (SetProperty(ref _recommendedLimitAccountLabel, value)) {
                OnPropertyChanged(nameof(HasRecommendedLimitAccount));
            }
        }
    }

    public string? RecommendedLimitAccountSummary {
        get => _recommendedLimitAccountSummary;
        set {
            if (SetProperty(ref _recommendedLimitAccountSummary, value)) {
                OnPropertyChanged(nameof(HasRecommendedLimitAccount));
            }
        }
    }

    public string? LimitAccountsOverviewText {
        get => _limitAccountsOverviewText;
        set {
            if (SetProperty(ref _limitAccountsOverviewText, value)) {
                OnPropertyChanged(nameof(HasLimitAccountsOverview));
                OnPropertyChanged(nameof(HasLimitSection));
            }
        }
    }

    public string? UsageHealthSummary {
        get => _usageHealthSummary;
        set {
            if (SetProperty(ref _usageHealthSummary, value)) {
                OnPropertyChanged(nameof(HasUsageHealthSummary));
                OnPropertyChanged(nameof(HasUsageHealthSection));
            }
        }
    }

    public string? UsageHealthDetail {
        get => _usageHealthDetail;
        set {
            if (SetProperty(ref _usageHealthDetail, value)) {
                OnPropertyChanged(nameof(HasUsageHealthDetail));
                OnPropertyChanged(nameof(HasUsageHealthSection));
            }
        }
    }

    public string? UsageHealthAccountsText {
        get => _usageHealthAccountsText;
        set {
            if (SetProperty(ref _usageHealthAccountsText, value)) {
                OnPropertyChanged(nameof(HasUsageHealthAccounts));
                OnPropertyChanged(nameof(HasUsageHealthSection));
            }
        }
    }

    public string? ScopeLocalText {
        get => _scopeLocalText;
        set {
            if (SetProperty(ref _scopeLocalText, value)) {
                OnPropertyChanged(nameof(HasScopeLocalText));
                OnPropertyChanged(nameof(HasDataScopeSection));
            }
        }
    }

    public string? ScopeOnlineText {
        get => _scopeOnlineText;
        set {
            if (SetProperty(ref _scopeOnlineText, value)) {
                OnPropertyChanged(nameof(HasScopeOnlineText));
                OnPropertyChanged(nameof(HasDataScopeSection));
            }
        }
    }

    public string? ScopeDifferenceText {
        get => _scopeDifferenceText;
        set {
            if (SetProperty(ref _scopeDifferenceText, value)) {
                OnPropertyChanged(nameof(HasScopeDifferenceText));
                OnPropertyChanged(nameof(HasDataScopeSection));
            }
        }
    }

    public bool HasLimitSummary => !string.IsNullOrWhiteSpace(LimitSummary);
    public bool HasLimitStatusMessage => !string.IsNullOrWhiteSpace(LimitStatusMessage);
    public bool HasRecommendedLimitAccount => !string.IsNullOrWhiteSpace(RecommendedLimitAccountLabel);
    public bool HasLimitAccountsOverview => !string.IsNullOrWhiteSpace(LimitAccountsOverviewText);
    public bool HasUsageHealthSummary => !string.IsNullOrWhiteSpace(UsageHealthSummary);
    public bool HasUsageHealthDetail => !string.IsNullOrWhiteSpace(UsageHealthDetail);
    public bool HasUsageHealthAccounts => !string.IsNullOrWhiteSpace(UsageHealthAccountsText);
    public bool HasScopeLocalText => !string.IsNullOrWhiteSpace(ScopeLocalText);
    public bool HasScopeOnlineText => !string.IsNullOrWhiteSpace(ScopeOnlineText);
    public bool HasScopeDifferenceText => !string.IsNullOrWhiteSpace(ScopeDifferenceText);
    public bool HasUsageHealthSection => HasUsageHealthSummary || HasUsageHealthDetail || HasUsageHealthAccounts;
    public bool HasDataScopeSection => HasScopeLocalText || HasScopeOnlineText || HasScopeDifferenceText;
    public bool HasLiveLimitData => LimitWindows.Count > 0;
    public bool HasLimitAccounts => LimitAccounts.Count > 0;
    public bool HasMultipleLimitAccounts => LimitAccounts.Count > 1;
    public bool ShowSharedLimitWindows => HasLiveLimitData && !HasMultipleLimitAccounts;
    public bool HasAccountBreakdown => AccountBreakdown.Count > 0;
    public bool HasSurfaceBreakdown => SurfaceBreakdown.Count > 0;
    public bool HasModelDaySummaries => ModelDaySummaries.Count > 0;
    public bool HasRecentActivity => RecentActivity.Count > 0;
    public bool HasConversationStats => _conversationSummaryData.Count > 0;
    public bool IsCombinedProvider => string.Equals(ProviderId, "__all__", StringComparison.Ordinal);
    public bool HasProviderComparison => IsCombinedProvider && ProviderComparison.Count > 0;
    public bool HasCombinedOverviewCards => IsCombinedProvider && CombinedOverviewCards.Count > 0;
    public bool HasCodeChurn => IsCombinedProvider && _codeChurnSummary.HasData;
    public bool HasCodeChurnBars => HasCodeChurn && CodeChurnBars.Count > 0;
    public bool HasCodeUsageCorrelation => IsCombinedProvider && _codeUsageCorrelationSummary.HasData;
    public bool HasPositiveCodeUsageCorrelation => HasCodeUsageCorrelation && _codeUsageCorrelationSummary.StrongestPositiveCorrelation is not null;
    public bool HasNegativeCodeUsageCorrelation => HasCodeUsageCorrelation && _codeUsageCorrelationSummary.StrongestNegativeCorrelation is not null;
    public bool HasSelectedEvent => SelectedEvent is not null;
    public bool HasActionStatusMessage => !string.IsNullOrWhiteSpace(ActionStatusMessage);
    public bool HasActiveFilters =>
        !string.Equals(SelectedAccountFilter, ProviderFilterDefaults.AllAccounts, StringComparison.Ordinal)
        || !string.Equals(SelectedModelFilter, ProviderFilterDefaults.AllModels, StringComparison.Ordinal)
        || !string.Equals(SelectedSurfaceFilter, ProviderFilterDefaults.AllSurfaces, StringComparison.Ordinal);
    public bool HasLimitSection =>
        HasLiveLimitData
        || HasLimitStatusMessage
        || !string.IsNullOrWhiteSpace(LimitPlanLabel)
        || !string.IsNullOrWhiteSpace(LimitAccountLabel)
        || HasLimitSummary
        || !string.IsNullOrWhiteSpace(LimitSourceLabel)
        || HasLimitAccounts;

    public ObservableCollection<ModelUsageViewModel> ModelBreakdown { get; } = [];
    public ObservableCollection<ModelDaySummaryViewModel> ModelDaySummaries { get; } = [];
    public ObservableCollection<DailyBarViewModel> DailyBars { get; } = [];
    public ObservableCollection<CodeChurnBarViewModel> CodeChurnBars { get; } = [];
    public ObservableCollection<ProviderLimitWindowViewModel> LimitWindows { get; } = [];
    public ObservableCollection<ProviderLimitAccountViewModel> LimitAccounts { get; } = [];
    public ObservableCollection<UsageBreakdownEntryViewModel> AccountBreakdown { get; } = [];
    public ObservableCollection<UsageBreakdownEntryViewModel> SurfaceBreakdown { get; } = [];
    public ObservableCollection<ProviderComparisonEntryViewModel> ProviderComparison { get; } = [];
    public ObservableCollection<ProviderOverviewCardViewModel> CombinedOverviewCards { get; } = [];
    public ObservableCollection<RecentUsageItemViewModel> RecentActivity { get; } = [];
    public ObservableCollection<ConversationUsageViewModel> Conversations { get; } = [];
    public string ConversationStatsSummaryText => BuildConversationStatsSummaryText();
    public string CodeChurnRepositoryText => NormalizeOptional(_codeChurnSummary.RepositoryName) ?? "Local repository";
    public string CodeChurnAddedText => "+" + FormatTokens(_codeChurnSummary.RecentAddedLines);
    public string CodeChurnDeletedText => "-" + FormatTokens(_codeChurnSummary.RecentDeletedLines);
    public string CodeChurnFilesText => _codeChurnSummary.RecentFilesModified.ToString("N0", CultureInfo.CurrentCulture);
    public string CodeChurnCommitsText => _codeChurnSummary.RecentCommitCount.ToString("N0", CultureInfo.CurrentCulture);
    public string CodeChurnSummaryText => BuildCodeChurnSummaryText();
    public string CodeChurnPeakDayText => BuildCodeChurnPeakDayText();
    public string CodeUsageCorrelationHeadlineText => BuildCodeUsageCorrelationHeadlineText();
    public string CodeUsageCorrelationSummaryText => BuildCodeUsageCorrelationSummaryText();
    public string CodeUsagePositiveProviderText => BuildCodeUsagePositiveProviderText();
    public string CodeUsagePositiveSummaryText => BuildCodeUsagePositiveSummaryText();
    public string CodeUsageNegativeProviderText => BuildCodeUsageNegativeProviderText();
    public string CodeUsageNegativeSummaryText => BuildCodeUsageNegativeSummaryText();
    public RelayCommand CopySummaryCommand { get; }
    public RelayCommand CopySelectedEventCommand { get; }
    public RelayCommand CopySelectedEventJsonCommand { get; }
    public RelayCommand FilterToSelectedModelCommand { get; }
    public RelayCommand FilterToSelectedSurfaceCommand { get; }
    public RelayCommand FilterToSelectedAccountCommand { get; }
    public RelayCommand ClearFiltersCommand { get; }
    public RelayCommand ExportJsonCommand { get; }
    public RelayCommand ExportCsvCommand { get; }
    public RelayCommand OpenDetailedReportCommand { get; }
    public ObservableCollection<string> AccountFilterOptions { get; } = [];
    public ObservableCollection<string> ModelFilterOptions { get; } = [];
    public ObservableCollection<string> SurfaceFilterOptions { get; } = [];

    public string ActionStatusMessage {
        get => _actionStatusMessage;
        set {
            if (SetProperty(ref _actionStatusMessage, value)) {
                OnPropertyChanged(nameof(HasActionStatusMessage));
            }
        }
    }

    public int FilteredEventCount {
        get => _filteredEventCount;
        set {
            if (SetProperty(ref _filteredEventCount, value)) {
                OnPropertyChanged(nameof(FilteredEventCountLabel));
            }
        }
    }

    public string FilteredEventCountLabel => FormatCountLabel(FilteredEventCount, "matching rollup", "matching rollups");

    public RecentUsageItemViewModel? SelectedEvent {
        get => _selectedEvent;
        set {
            if (SetProperty(ref _selectedEvent, value)) {
                OnPropertyChanged(nameof(HasSelectedEvent));
                CopySelectedEventCommand.RaiseCanExecuteChanged();
                CopySelectedEventJsonCommand.RaiseCanExecuteChanged();
                FilterToSelectedModelCommand.RaiseCanExecuteChanged();
                FilterToSelectedSurfaceCommand.RaiseCanExecuteChanged();
                FilterToSelectedAccountCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public ProviderExplorerPreferences CaptureExplorerPreferences() {
        return new ProviderExplorerPreferences {
            SelectedRange = SelectedRange.ToString(),
            SelectedEventSort = SelectedEventSort.ToString(),
            SelectedProviderComparisonSort = SelectedProviderComparisonSort.ToString(),
            SelectedAccountFilter = SelectedAccountFilter,
            SelectedModelFilter = SelectedModelFilter,
            SelectedSurfaceFilter = SelectedSurfaceFilter
        };
    }

    public void ApplyExplorerPreferences(ProviderExplorerPreferences? preferences) {
        if (preferences is null) {
            return;
        }

        _isApplyingExplorerPreferences = true;
        try {
            if (Enum.TryParse<ProviderTimeRange>(preferences.SelectedRange, ignoreCase: true, out var range)) {
                SelectedRange = range;
            }

            if (Enum.TryParse<ProviderEventSort>(preferences.SelectedEventSort, ignoreCase: true, out var sort)) {
                SelectedEventSort = sort;
            }

            if (Enum.TryParse<ProviderComparisonSort>(preferences.SelectedProviderComparisonSort, ignoreCase: true, out var providerComparisonSort)) {
                SelectedProviderComparisonSort = providerComparisonSort;
            }

            SelectedAccountFilter = NormalizeSelectedFilter(
                preferences.SelectedAccountFilter ?? SelectedAccountFilter,
                AccountFilterOptions,
                ProviderFilterDefaults.AllAccounts);
            SelectedModelFilter = NormalizeSelectedFilter(
                preferences.SelectedModelFilter ?? SelectedModelFilter,
                ModelFilterOptions,
                ProviderFilterDefaults.AllModels);
            SelectedSurfaceFilter = NormalizeSelectedFilter(
                preferences.SelectedSurfaceFilter ?? SelectedSurfaceFilter,
                SurfaceFilterOptions,
                ProviderFilterDefaults.AllSurfaces);
        } finally {
            _isApplyingExplorerPreferences = false;
        }

        OnPropertyChanged(nameof(HasActiveFilters));
        ClearFiltersCommand.RaiseCanExecuteChanged();
        RebuildSelectedRangeViews();
    }

    public void ApplyUsageEvents(IEnumerable<UsageEventRecord> events) {
        _usageEvents.Clear();
        _usageEvents.AddRange(events ?? Array.Empty<UsageEventRecord>());
        if (_conversationEvents.Count == 0) {
            _conversationEvents.AddRange(_usageEvents);
        }
        RebuildFilterOptions();
        RebuildStaticWindows();
        RebuildSelectedRangeViews();
    }

    public void ApplyConversationEvents(IEnumerable<UsageEventRecord> events) {
        _conversationEvents.Clear();
        _conversationEvents.AddRange(events ?? Array.Empty<UsageEventRecord>());
        RebuildSelectedRangeViews();
    }

    public void SetSelectedRange(ProviderTimeRange range) {
        SelectedRange = range;
    }

    public void SetEventSort(ProviderEventSort sort) {
        SelectedEventSort = sort;
    }

    public void SetProviderComparisonSort(ProviderComparisonSort sort) {
        SelectedProviderComparisonSort = sort;
    }

    public void SetProviderComparisonHealth(IReadOnlyDictionary<string, ProviderComparisonHealthInfo>? healthByProviderId) {
        _providerComparisonHealth = healthByProviderId is null
            ? new Dictionary<string, ProviderComparisonHealthInfo>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, ProviderComparisonHealthInfo>(healthByProviderId, StringComparer.OrdinalIgnoreCase);

        if (IsCombinedProvider) {
            RebuildSelectedRangeViews();
        }
    }

    public void SetProviderComparisonDelta(IReadOnlyDictionary<string, ProviderComparisonDeltaInfo>? deltaByProviderId) {
        _providerComparisonDelta = deltaByProviderId is null
            ? new Dictionary<string, ProviderComparisonDeltaInfo>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, ProviderComparisonDeltaInfo>(deltaByProviderId, StringComparer.OrdinalIgnoreCase);

        if (IsCombinedProvider) {
            RebuildSelectedRangeViews();
        }
    }

    public void SetProviderComparisonHistory(IReadOnlyDictionary<string, ProviderComparisonHistoryInfo>? historyByProviderId) {
        _providerComparisonHistory = historyByProviderId is null
            ? new Dictionary<string, ProviderComparisonHistoryInfo>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, ProviderComparisonHistoryInfo>(historyByProviderId, StringComparer.OrdinalIgnoreCase);

        if (IsCombinedProvider) {
            RebuildSelectedRangeViews();
        }
    }

    public void SetProviderComparisonFavorites(IEnumerable<string>? favoriteProviderIds) {
        _providerComparisonFavorites = favoriteProviderIds is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(favoriteProviderIds.Where(static providerId => !string.IsNullOrWhiteSpace(providerId)), StringComparer.OrdinalIgnoreCase);

        if (IsCombinedProvider) {
            RebuildSelectedRangeViews();
        }
    }

    internal void ApplyCodeChurnSummary(GitCodeChurnSummaryData? summary) {
        _codeChurnSummary = summary ?? GitCodeChurnSummaryData.Empty;
        RebuildCodeChurnBars();
        OnPropertyChanged(nameof(HasCodeChurn));
        OnPropertyChanged(nameof(HasCodeChurnBars));
        OnPropertyChanged(nameof(CodeChurnRepositoryText));
        OnPropertyChanged(nameof(CodeChurnAddedText));
        OnPropertyChanged(nameof(CodeChurnDeletedText));
        OnPropertyChanged(nameof(CodeChurnFilesText));
        OnPropertyChanged(nameof(CodeChurnCommitsText));
        OnPropertyChanged(nameof(CodeChurnSummaryText));
        OnPropertyChanged(nameof(CodeChurnPeakDayText));

        if (IsCombinedProvider) {
            RebuildSelectedRangeViews();
        }
    }

    internal void ApplyGitHubLocalActivityCorrelationSummary(GitHubLocalActivityCorrelationSummaryData? summary) {
        _gitHubLocalActivityCorrelationSummary = summary ?? GitHubLocalActivityCorrelationSummaryData.Empty;
        if (IsCombinedProvider) {
            RebuildSelectedRangeViews();
        }
    }

    internal void ApplyGitHubRepositoryClusterSummary(GitHubRepositoryClusterSummaryData? summary) {
        _gitHubRepositoryClusterSummary = summary ?? GitHubRepositoryClusterSummaryData.Empty;
        if (IsCombinedProvider) {
            RebuildSelectedRangeViews();
        }
    }

    public void ApplyProviderInfo(ProviderInfo info) {
        ProviderId = info.Id;
        DisplayName = info.DisplayName;
        ShortName = info.ShortName;
        IconKey = info.Icon;
        SortOrder = info.SortOrder;
        InputColor = info.InputColor;
        OutputColor = info.OutputColor;
        TotalColor = info.TotalColor;
        var brush = new SolidColorBrush(info.TotalColor);
        brush.Freeze();
        AccentBrush = brush;
    }

    public void ApplyUsageScopeSummary(UsageTelemetryScopeSummary? summary) {
        if (summary is null || !summary.HasAnyText) {
            ScopeLocalText = null;
            ScopeOnlineText = null;
            ScopeDifferenceText = null;
            return;
        }

        ScopeLocalText = summary.LocalScopeText;
        ScopeOnlineText = summary.OnlineScopeText;
        ScopeDifferenceText = summary.DifferenceText;
    }

    public void ApplyLimitSnapshot(ProviderLimitSnapshot? snapshot) {
        LimitWindows.Clear();
        LimitAccounts.Clear();
        if (snapshot is null) {
            LimitPlanLabel = null;
            LimitAccountLabel = null;
            LimitSummary = null;
            LimitSourceLabel = null;
            LimitStatusMessage = null;
            RecommendedLimitAccountLabel = null;
            RecommendedLimitAccountSummary = null;
            LimitAccountsOverviewText = null;
            return;
        }

        LimitPlanLabel = snapshot.PlanLabel;
        LimitAccountLabel = snapshot.AccountLabel;
        LimitSummary = snapshot.Summary;
        LimitSourceLabel = snapshot.SourceLabel;
        LimitStatusMessage = snapshot.DetailMessage;
        RecommendedLimitAccountLabel = null;
        RecommendedLimitAccountSummary = null;
        LimitAccountsOverviewText = null;
        var forecasts = ProviderLimitForecasting.BuildForecasts(snapshot);
        var advisories = ProviderLimitForecasting.BuildAccountAdvisories(snapshot);
        var accountSnapshots = snapshot.Accounts.Count > 0
            ? snapshot.Accounts
            : new[] {
                new ProviderLimitAccountSnapshot(
                    accountId: null,
                    accountLabel: snapshot.AccountLabel,
                    planLabel: snapshot.PlanLabel,
                    windows: snapshot.Windows,
                    summary: snapshot.Summary,
                    detailMessage: snapshot.DetailMessage,
                    retrievedAtUtc: snapshot.RetrievedAtUtc,
                    isSelected: true)
            };

        foreach (var advisory in advisories) {
            var accountSnapshot = FindAccountSnapshot(accountSnapshots, advisory);
            var accountViewModel = new ProviderLimitAccountViewModel {
                Label = advisory.DisplayLabel,
                PlanLabel = advisory.PlanLabel,
                StatusLabel = advisory.StatusLabel,
                Summary = advisory.Summary ?? "No live limit windows",
                DetailText = NormalizeLimitAccountDetail(accountSnapshot?.DetailMessage, advisory.Summary),
                WindowSummaryText = accountSnapshot is { Windows.Count: > 0 }
                    ? BuildLimitWindowSummaryText(accountSnapshot.Windows)
                    : accountSnapshot is { IsAvailable: false }
                        ? "Live limits unavailable"
                    : null,
                IsExpanded = advisory.IsRecommended || advisory.IsSelected,
                BadgeText = advisory.IsRecommended
                    ? (advisory.IsSelected ? "Best current" : "Recommended")
                    : (advisory.IsSelected ? "Current" : null)
            };
            PopulateLimitWindows(
                accountViewModel.Windows,
                accountSnapshot?.Windows,
                forecasts: null,
                forecastNowUtc: accountSnapshot?.RetrievedAtUtc ?? snapshot.RetrievedAtUtc);
            LimitAccounts.Add(accountViewModel);
        }

        if (advisories.FirstOrDefault(static advisory => advisory.IsRecommended) is { } recommended) {
            RecommendedLimitAccountLabel = recommended.IsSelected
                ? "Best current choice: " + recommended.DisplayLabel
                : "Recommended next: " + recommended.DisplayLabel;
            RecommendedLimitAccountSummary = recommended.Summary;
        }

        if (accountSnapshots.Count > 1) {
            LimitAccountsOverviewText = BuildLimitAccountsOverviewText(accountSnapshots);
        }

        if (snapshot.Accounts.Count <= 1) {
            PopulateLimitWindows(LimitWindows, snapshot.Windows, forecasts);
        }
    }

    private static string? NormalizeLimitAccountDetail(string? detail, string? summary) {
        if (string.IsNullOrWhiteSpace(detail)) {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(summary)
            && string.Equals(detail.Trim(), summary.Trim(), StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        return detail.Trim();
    }

    private static string BuildLimitAccountsOverviewText(IReadOnlyList<ProviderLimitAccountSnapshot> accounts) {
        var detectedCount = accounts.Count;
        var liveCount = accounts.Count(static account => account.IsAvailable);
        var unavailableCount = detectedCount - liveCount;
        var selected = accounts.FirstOrDefault(static account => account.IsSelected);

        var parts = new List<string> {
            detectedCount.ToString(CultureInfo.InvariantCulture) + " detected locally",
            liveCount.ToString(CultureInfo.InvariantCulture) + " live limit accounts"
        };
        if (unavailableCount > 0) {
            parts.Add(unavailableCount.ToString(CultureInfo.InvariantCulture) + " unavailable");
        }
        if (selected is not null) {
            var label = selected.AccountLabel ?? selected.AccountId;
            if (!string.IsNullOrWhiteSpace(label)) {
                parts.Add("current " + label.Trim());
            }
        }

        return string.Join(" • ", parts);
    }

    private static string BuildLimitWindowSummaryText(IReadOnlyList<ProviderLimitWindow> windows) {
        if (windows.Count == 0) {
            return "No live windows";
        }

        return windows.Count.ToString(CultureInfo.InvariantCulture)
               + (windows.Count == 1 ? " live window" : " live windows");
    }

    private void PopulateLimitWindows(
        ObservableCollection<ProviderLimitWindowViewModel> target,
        IReadOnlyList<ProviderLimitWindow>? windows,
        IReadOnlyDictionary<string, ProviderLimitWindowForecast>? forecasts = null,
        DateTimeOffset? forecastNowUtc = null) {
        if (windows is null || windows.Count == 0) {
            return;
        }

        foreach (var window in windows) {
            ProviderLimitWindowForecast? forecast = null;
            if (forecasts is not null) {
                forecasts.TryGetValue(window.Key, out forecast);
            } else if (forecastNowUtc.HasValue) {
                forecast = ProviderLimitForecasting.BuildForecast(window, forecastNowUtc.Value);
            }

            target.Add(CreateLimitWindowViewModel(window, forecast));
        }
    }

    private ProviderLimitWindowViewModel CreateLimitWindowViewModel(
        ProviderLimitWindow window,
        ProviderLimitWindowForecast? forecast) {
        var detail = window.Detail;
        if (!string.IsNullOrWhiteSpace(forecast?.Summary)) {
            detail = string.IsNullOrWhiteSpace(detail)
                ? forecast!.Summary
                : detail + " • " + forecast.Summary;
        }

        return new ProviderLimitWindowViewModel {
            Label = window.Label,
            UsedPercent = window.UsedPercent,
            UsedPercentFormatted = window.UsedPercent.HasValue
                ? window.UsedPercent.Value.ToString("0.#", CultureInfo.InvariantCulture) + "%"
                : "--",
            ResetText = FormatResetText(window.ResetsAt),
            Detail = detail,
            Proportion = window.UsedPercent.HasValue
                ? Math.Min(1d, Math.Max(0d, window.UsedPercent.Value / 100d))
                : 0d,
            BarBrush = FrozenBrush(OutputColor)
        };
    }

    private static ProviderLimitAccountSnapshot? FindAccountSnapshot(
        IReadOnlyList<ProviderLimitAccountSnapshot> accounts,
        ProviderLimitAccountAdvisory advisory) {
        var advisoryKey = BuildLimitAccountKey(advisory.AccountId, advisory.DisplayLabel);
        foreach (var account in accounts) {
            if (string.Equals(
                    advisoryKey,
                    BuildLimitAccountKey(account.AccountId, account.AccountLabel),
                    StringComparison.OrdinalIgnoreCase)) {
                return account;
            }
        }

        return null;
    }

    private static string BuildLimitAccountKey(string? accountId, string? accountLabel) {
        return string.IsNullOrWhiteSpace(accountId)
            ? accountLabel?.Trim() ?? string.Empty
            : accountId.Trim();
    }

    private static string FormatTokens(long tokens) {
        return tokens switch {
            >= 1_000_000_000L => $"{tokens / 1_000_000_000.0:F1}B",
            >= 1_000_000L => $"{tokens / 1_000_000.0:F1}M",
            >= 1_000L => $"{tokens / 1_000.0:F1}K",
            _ => tokens.ToString("N0")
        };
    }

    private static string FormatResetText(DateTimeOffset? resetsAt) {
        if (!resetsAt.HasValue) {
            return "Reset unknown";
        }

        var local = resetsAt.Value.ToLocalTime();
        var now = DateTimeOffset.Now;
        var remaining = local - now;
        if (remaining.TotalMinutes > 0 && remaining.TotalHours < 24) {
            if (remaining.TotalHours >= 1) {
                return "Resets in "
                       + Math.Floor(remaining.TotalHours).ToString(CultureInfo.InvariantCulture)
                       + "h " + remaining.Minutes.ToString(CultureInfo.InvariantCulture) + "m";
            }

            return "Resets in " + Math.Max(1, remaining.Minutes).ToString(CultureInfo.InvariantCulture) + "m";
        }

        return "Resets " + local.ToString("MMM d HH:mm", CultureInfo.CurrentCulture);
    }

    private void RebuildStaticWindows() {
        var today = DateTime.Now.Date;
        WeeklyLabel = "Last 7 days";
        MonthlyLabel = "Last 30 days";

        var weeklyEvents = FilterByWindow(today.AddDays(-6), today);
        WeeklyTotalTokens = weeklyEvents.Sum(e => e.TotalTokens ?? 0L);
        WeeklyAvgPerDay = WeeklyTotalTokens > 0 ? WeeklyTotalTokens / 7 : 0;
        ApplyDisplayCost(
            UsageTelemetryApiPricing.BuildDisplayCost(weeklyEvents),
            value => WeeklyCostUsd = value,
            value => WeeklyCostUsesEstimate = value);

        var monthlyEvents = FilterByWindow(today.AddDays(-29), today);
        MonthlyTotalTokens = monthlyEvents.Sum(e => e.TotalTokens ?? 0L);
        MonthlyAvgPerDay = MonthlyTotalTokens > 0 ? MonthlyTotalTokens / 30 : 0;
        ApplyDisplayCost(
            UsageTelemetryApiPricing.BuildDisplayCost(monthlyEvents),
            value => MonthlyCostUsd = value,
            value => MonthlyCostUsesEstimate = value);

        var dailyTotals = new List<(DateTime Day, long Tokens)>();
        for (var i = 6; i >= 0; i--) {
            var day = today.AddDays(-i);
            var tokens = _usageEvents
                .Where(e => e.TimestampUtc.ToLocalTime().Date == day)
                .Sum(e => e.TotalTokens ?? 0L);
            dailyTotals.Add((day, tokens));
        }

        var baseBrush = FrozenBrush(OutputColor);
        var todayBrush = FrozenBrush(InputColor);
        var maxDaily = dailyTotals.Count > 0 ? dailyTotals.Max(static value => value.Tokens) : 0L;
        DailyBars.Clear();
        foreach (var (day, tokens) in dailyTotals) {
            DailyBars.Add(new DailyBarViewModel {
                DayUtc = day,
                DayLabel = day == today ? "Today" : day.ToString("ddd", CultureInfo.CurrentCulture),
                TotalTokens = tokens,
                BarHeight = maxDaily > 0 ? Math.Max(2, 48d * tokens / maxDaily) : 2,
                BarBrush = day == today ? todayBrush : baseBrush
            });
        }
    }

    private void RebuildFilterOptions() {
        ResetFilterOptions(
            AccountFilterOptions,
            ProviderFilterDefaults.AllAccounts,
            _usageEvents
                .Select(static e => NormalizeAccountLabel(e.AccountLabel, e.ProviderAccountId))
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static value => value, StringComparer.CurrentCultureIgnoreCase)
                .Cast<string>()
                .ToList());
        ResetFilterOptions(
            ModelFilterOptions,
            ProviderFilterDefaults.AllModels,
            _usageEvents
                .Select(static e => NormalizeOptional(e.Model))
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static value => value, StringComparer.CurrentCultureIgnoreCase)
                .Cast<string>()
                .ToList());
        ResetFilterOptions(
            SurfaceFilterOptions,
            ProviderFilterDefaults.AllSurfaces,
            _usageEvents
                .Select(static e => NormalizeSurfaceLabel(e.Surface))
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static value => value, StringComparer.CurrentCultureIgnoreCase)
                .Cast<string>()
                .ToList());

        SelectedAccountFilter = NormalizeSelectedFilter(SelectedAccountFilter, AccountFilterOptions, ProviderFilterDefaults.AllAccounts);
        SelectedModelFilter = NormalizeSelectedFilter(SelectedModelFilter, ModelFilterOptions, ProviderFilterDefaults.AllModels);
        SelectedSurfaceFilter = NormalizeSelectedFilter(SelectedSurfaceFilter, SurfaceFilterOptions, ProviderFilterDefaults.AllSurfaces);
        OnPropertyChanged(nameof(HasActiveFilters));
    }

    private void RebuildSelectedRangeViews() {
        var rangeEvents = ApplyFilters(GetRangeEvents(SelectedRange));
        ApplyCodeUsageCorrelationSummary(BuildCodeUsageCorrelationSummary());
        TodayLabel = SelectedRange switch {
            ProviderTimeRange.Today => "Today (local)",
            ProviderTimeRange.Last7Days => "Selected range: 7 days",
            ProviderTimeRange.Last30Days => "Selected range: 30 days",
            ProviderTimeRange.AllTime => "Selected range: all time",
            _ => "Selected range"
        };

        FilteredEventCount = rangeEvents.Count;
        TodayTotalTokens = rangeEvents.Sum(e => e.TotalTokens ?? 0L);
        TodayInputTokens = rangeEvents.Sum(e => e.InputTokens ?? 0L);
        TodayOutputTokens = rangeEvents.Sum(e => e.OutputTokens ?? 0L);
        TodayCachedTokens = rangeEvents.Sum(e => e.CachedInputTokens ?? 0L);
        TodayReasoningTokens = rangeEvents.Sum(e => e.ReasoningTokens ?? 0L);
        ApplyDisplayCost(
            UsageTelemetryApiPricing.BuildDisplayCost(rangeEvents),
            value => TodayCostUsd = value,
            value => TodayCostUsesEstimate = value);
        TodayEventCount = rangeEvents.Count;

        PopulateModelBreakdown(rangeEvents);
        PopulateModelDaySummaries(rangeEvents);
        PopulateUsageBreakdown(
            AccountBreakdown,
            BuildBreakdown(rangeEvents, e => NormalizeAccountLabel(e.AccountLabel, e.ProviderAccountId)),
            OutputColor);
        PopulateUsageBreakdown(
            SurfaceBreakdown,
            BuildBreakdown(rangeEvents, e => NormalizeSurfaceLabel(e.Surface)),
            InputColor);
        PopulateProviderComparison(rangeEvents);
        PopulateCombinedOverview(rangeEvents);
        PopulateConversationStats(ApplyFilters(GetConversationRangeEvents(SelectedRange)));
        PopulateRecentActivity(rangeEvents);

        ActionStatusMessage = string.Empty;
    }

    private void RebuildCodeChurnBars() {
        CodeChurnBars.Clear();
        if (!_codeChurnSummary.HasData) {
            return;
        }

        var trendDays = _codeChurnSummary.TrendDays;
        var maxChangedLines = trendDays.Count > 0 ? trendDays.Max(static day => day.TotalChangedLines) : 0;
        foreach (var day in trendDays) {
            CodeChurnBars.Add(new CodeChurnBarViewModel {
                DayUtc = day.DayUtc,
                DayLabel = day.DayUtc == DateTime.Now.Date ? "Today" : day.DayUtc.ToString("ddd", CultureInfo.CurrentCulture),
                SummaryText = day.HasActivity
                    ? "+" + FormatTokens(day.AddedLines) + " / -" + FormatTokens(day.DeletedLines)
                    : "-",
                BarHeight = maxChangedLines > 0
                    ? Math.Max(2d, 48d * day.TotalChangedLines / maxChangedLines)
                    : 2d,
                BarBrush = FrozenBrush(day.NetLines >= 0 ? Color.FromRgb(144, 208, 160) : Color.FromRgb(240, 192, 64))
            });
        }
    }

    private string BuildCodeChurnSummaryText() {
        if (!_codeChurnSummary.HasData) {
            return "No recent git churn detected for the local repository.";
        }

        return "7d • "
               + _codeChurnSummary.RecentFilesModified.ToString("N0", CultureInfo.CurrentCulture)
               + " files • "
               + _codeChurnSummary.RecentCommitCount.ToString("N0", CultureInfo.CurrentCulture)
               + " commits • prev "
               + "+" + FormatTokens(_codeChurnSummary.PreviousAddedLines)
               + " / -" + FormatTokens(_codeChurnSummary.PreviousDeletedLines);
    }

    private string BuildCodeChurnPeakDayText() {
        if (_codeChurnSummary.PeakRecentDay is not { } peakDay) {
            return "Peak day appears once there is local git activity in the current pulse window.";
        }

        return "Peak "
               + peakDay.DayUtc.ToString("MMM d", CultureInfo.CurrentCulture)
               + " • +" + FormatTokens(peakDay.AddedLines)
               + " / -" + FormatTokens(peakDay.DeletedLines)
               + " • "
               + peakDay.FilesModified.ToString("N0", CultureInfo.CurrentCulture)
               + " files";
    }

    private GitCodeUsageCorrelationSummaryData BuildCodeUsageCorrelationSummary() {
        if (!IsCombinedProvider || !_codeChurnSummary.HasData) {
            return GitCodeUsageCorrelationSummaryData.Empty;
        }

        var filteredEvents = ApplyFilters(_usageEvents);
        return GitCodeUsageCorrelationSummaryBuilder.Build(
            _codeChurnSummary,
            filteredEvents,
            static providerId => ProviderMetadata.Resolve(providerId).DisplayName,
            activityUnitsLabel: "usage");
    }

    private void ApplyCodeUsageCorrelationSummary(GitCodeUsageCorrelationSummaryData? summary) {
        _codeUsageCorrelationSummary = summary ?? GitCodeUsageCorrelationSummaryData.Empty;
        OnPropertyChanged(nameof(HasCodeUsageCorrelation));
        OnPropertyChanged(nameof(HasPositiveCodeUsageCorrelation));
        OnPropertyChanged(nameof(HasNegativeCodeUsageCorrelation));
        OnPropertyChanged(nameof(CodeUsageCorrelationHeadlineText));
        OnPropertyChanged(nameof(CodeUsageCorrelationSummaryText));
        OnPropertyChanged(nameof(CodeUsagePositiveProviderText));
        OnPropertyChanged(nameof(CodeUsagePositiveSummaryText));
        OnPropertyChanged(nameof(CodeUsageNegativeProviderText));
        OnPropertyChanged(nameof(CodeUsageNegativeSummaryText));
    }

    private string BuildCodeUsageCorrelationHeadlineText() {
        if (!_codeUsageCorrelationSummary.HasData) {
            return "Usage and churn correlation appears once both telemetry and local git activity overlap.";
        }

        var activityDelta = _codeUsageCorrelationSummary.ActivityDeltaRatio;
        var churnDelta = _codeUsageCorrelationSummary.ChurnDeltaRatio;
        var activityMoving = Math.Abs(activityDelta) >= 0.10d;
        var churnMoving = Math.Abs(churnDelta) >= 0.10d;
        if (!activityMoving && !churnMoving) {
            return "Usage and churn held steady in the recent window.";
        }

        if (activityDelta >= 0d && churnDelta >= 0d) {
            return "Usage and churn rose together across the recent 7-day pulse.";
        }

        if (activityDelta <= 0d && churnDelta <= 0d) {
            return "Usage and churn cooled together across the recent 7-day pulse.";
        }

        return churnDelta > activityDelta
            ? "Churn rose while usage cooled in the recent 7-day pulse."
            : "Usage rose while churn cooled in the recent 7-day pulse.";
    }

    private string BuildCodeUsageCorrelationSummaryText() {
        if (!_codeUsageCorrelationSummary.HasData) {
            return "Correlation needs both local git churn and recent telemetry activity.";
        }

        return "7d usage "
               + FormatTokens((long)Math.Round(_codeUsageCorrelationSummary.RecentActivityTotal, MidpointRounding.AwayFromZero))
               + " vs prev "
               + FormatTokens((long)Math.Round(_codeUsageCorrelationSummary.PreviousActivityTotal, MidpointRounding.AwayFromZero))
               + " • churn "
               + FormatShortSignedDelta(_codeUsageCorrelationSummary.RecentChurnVolume - _codeUsageCorrelationSummary.PreviousChurnVolume)
               + " lines • "
               + _codeUsageCorrelationSummary.RecentActivityDays.ToString("N0", CultureInfo.CurrentCulture)
               + " active usage days";
    }

    private string BuildCodeUsagePositiveProviderText() {
        return _codeUsageCorrelationSummary.StrongestPositiveCorrelation is { } correlation
            ? correlation.ProviderDisplayName + " • " + FormatCorrelationValue(correlation.Correlation)
            : "No aligned provider signal yet";
    }

    private string BuildCodeUsagePositiveSummaryText() {
        if (_codeUsageCorrelationSummary.StrongestPositiveCorrelation is not { } correlation) {
            return "Aligned providers appear once a repo and one provider move together for several days.";
        }

        return FormatTokens((long)Math.Round(correlation.RecentActivityValue, MidpointRounding.AwayFromZero))
               + " recent usage • "
               + correlation.SharedActiveDays.ToString("N0", CultureInfo.CurrentCulture)
               + "/" + correlation.OverlapDays.ToString("N0", CultureInfo.CurrentCulture)
               + " shared active days";
    }

    private string BuildCodeUsageNegativeProviderText() {
        return _codeUsageCorrelationSummary.StrongestNegativeCorrelation is { } correlation
            ? correlation.ProviderDisplayName + " • " + FormatCorrelationValue(correlation.Correlation)
            : "No diverging provider signal yet";
    }

    private string BuildCodeUsageNegativeSummaryText() {
        if (_codeUsageCorrelationSummary.StrongestNegativeCorrelation is not { } correlation) {
            return "Diverging providers appear when local churn and one provider decouple across the same week.";
        }

        return FormatTokens((long)Math.Round(correlation.RecentActivityValue, MidpointRounding.AwayFromZero))
               + " recent usage • "
               + correlation.ProviderActiveDays.ToString("N0", CultureInfo.CurrentCulture)
               + "/" + correlation.OverlapDays.ToString("N0", CultureInfo.CurrentCulture)
               + " provider-active days";
    }

    private List<UsageEventRecord> GetRangeEvents(ProviderTimeRange range) {
        var today = DateTime.Now.Date;
        return range switch {
            ProviderTimeRange.Today => FilterByWindow(today, today),
            ProviderTimeRange.Last7Days => FilterByWindow(today.AddDays(-6), today),
            ProviderTimeRange.Last30Days => FilterByWindow(today.AddDays(-29), today),
            ProviderTimeRange.AllTime => _usageEvents.OrderByDescending(static e => e.TimestampUtc).ToList(),
            _ => _usageEvents.OrderByDescending(static e => e.TimestampUtc).ToList()
        };
    }

    private List<UsageEventRecord> GetConversationRangeEvents(ProviderTimeRange range) {
        var today = DateTime.Now.Date;
        return range switch {
            ProviderTimeRange.Today => FilterConversationByWindow(today, today),
            ProviderTimeRange.Last7Days => FilterConversationByWindow(today.AddDays(-6), today),
            ProviderTimeRange.Last30Days => FilterConversationByWindow(today.AddDays(-29), today),
            ProviderTimeRange.AllTime => _conversationEvents.OrderByDescending(static e => e.TimestampUtc).ToList(),
            _ => _conversationEvents.OrderByDescending(static e => e.TimestampUtc).ToList()
        };
    }

    private List<UsageEventRecord> FilterByWindow(DateTime startDay, DateTime endDay) {
        return _usageEvents
            .Where(e => {
                var localDay = e.TimestampUtc.ToLocalTime().Date;
                return localDay >= startDay && localDay <= endDay;
            })
            .OrderByDescending(static e => e.TimestampUtc)
            .ToList();
    }

    private List<UsageEventRecord> FilterConversationByWindow(DateTime startDay, DateTime endDay) {
        return _conversationEvents
            .Where(e => {
                var localDay = e.TimestampUtc.ToLocalTime().Date;
                return localDay >= startDay && localDay <= endDay;
            })
            .OrderByDescending(static e => e.TimestampUtc)
            .ToList();
    }

    private List<UsageEventRecord> ApplyFilters(IEnumerable<UsageEventRecord> events) {
        return events
            .Where(e => MatchesFilter(SelectedAccountFilter, NormalizeAccountLabel(e.AccountLabel, e.ProviderAccountId), ProviderFilterDefaults.AllAccounts))
            .Where(e => MatchesFilter(SelectedModelFilter, NormalizeOptional(e.Model), ProviderFilterDefaults.AllModels))
            .Where(e => MatchesFilter(SelectedSurfaceFilter, NormalizeSurfaceLabel(e.Surface), ProviderFilterDefaults.AllSurfaces))
            .OrderByDescending(static e => e.TimestampUtc)
            .ToList();
    }

    private void PopulateModelBreakdown(IReadOnlyList<UsageEventRecord> events) {
        ModelBreakdown.Clear();
        var modelGroups = events
            .Where(static e => !string.IsNullOrWhiteSpace(e.Model))
            .GroupBy(static e => e.Model!.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(static group => new {
                Model = group.Key,
                Total = group.Sum(e => e.TotalTokens ?? 0L)
            })
            .Where(static group => group.Total > 0)
            .OrderByDescending(static group => group.Total)
            .Take(8)
            .ToList();

        var max = modelGroups.Count > 0 ? modelGroups.Max(static group => group.Total) : 0L;
        foreach (var group in modelGroups) {
            ModelBreakdown.Add(new ModelUsageViewModel {
                ModelName = group.Model,
                TotalTokens = group.Total,
                Proportion = max > 0 ? (double)group.Total / max : 0d,
                BarBrush = FrozenBrush(OutputColor)
            });
        }
    }

    private void PopulateModelDaySummaries(IReadOnlyList<UsageEventRecord> events) {
        ModelDaySummaries.Clear();
        var groupedDays = events
            .GroupBy(static usageEvent => usageEvent.TimestampUtc.ToLocalTime().Date)
            .OrderByDescending(static group => group.Key)
            .Take(7)
            .ToList();

        foreach (var dayGroup in groupedDays) {
            var topModels = dayGroup
                .Where(static usageEvent => !string.IsNullOrWhiteSpace(usageEvent.Model))
                .GroupBy(static usageEvent => usageEvent.Model!.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(static group => new {
                    Model = group.Key,
                    Tokens = group.Sum(usageEvent => usageEvent.TotalTokens ?? 0L)
                })
                .OrderByDescending(static group => group.Tokens)
                .ThenBy(static group => group.Model, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var visibleModels = topModels
                .Take(3)
                .Select(group => group.Model + " (" + FormatTokens(group.Tokens) + ")")
                .ToList();
            var moreCount = topModels.Count - visibleModels.Count;
            if (moreCount > 0) {
                visibleModels.Add("+" + moreCount.ToString(CultureInfo.InvariantCulture) + " more");
            }

            ModelDaySummaries.Add(new ModelDaySummaryViewModel {
                DayLabel = dayGroup.Key == DateTime.Now.Date
                    ? "Today"
                    : dayGroup.Key.ToString("ddd, MMM d", CultureInfo.CurrentCulture),
                TotalTokensText = FormatTokens(dayGroup.Sum(static usageEvent => usageEvent.TotalTokens ?? 0L)),
                ModelsText = visibleModels.Count > 0
                    ? string.Join(" • ", visibleModels)
                    : "No model ids in local logs"
            });
        }
    }

    private static List<(string Label, decimal Value)> BuildBreakdown(
        IReadOnlyList<UsageEventRecord> events,
        Func<UsageEventRecord, string?> labelSelector) {
        return events
            .Select(eventRecord => new {
                Label = labelSelector(eventRecord),
                Value = (decimal)(eventRecord.TotalTokens ?? 0L)
            })
            .Where(static item => !string.IsNullOrWhiteSpace(item.Label) && item.Value > 0m)
            .GroupBy(static item => item.Label!, StringComparer.OrdinalIgnoreCase)
            .Select(static group => (group.Key, group.Sum(item => item.Value)))
            .OrderByDescending(static group => group.Item2)
            .ThenBy(static group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToList();
    }

    private void PopulateUsageBreakdown(
        ObservableCollection<UsageBreakdownEntryViewModel> target,
        IReadOnlyList<(string Label, decimal Value)> items,
        Color color) {
        target.Clear();
        var max = items.Count > 0 ? items.Max(static item => item.Value) : 0m;
        foreach (var item in items) {
            target.Add(new UsageBreakdownEntryViewModel {
                Label = item.Label,
                ValueText = FormatMetricValue(item.Value),
                Proportion = max > 0m ? (double)(item.Value / max) : 0d,
                BarBrush = FrozenBrush(color)
            });
        }
    }

    private void PopulateRecentActivity(IReadOnlyList<UsageEventRecord> events) {
        var previousKey = SelectedEvent?.EventKey;
        RecentActivity.Clear();
        foreach (var usageEvent in OrderEventsForDisplay(events).Take(14)) {
            var localTime = usageEvent.TimestampUtc.ToLocalTime();
            var subtitleParts = new List<string>();
            var surface = NormalizeSurfaceLabel(usageEvent.Surface);
            if (!string.IsNullOrWhiteSpace(surface)) {
                subtitleParts.Add(surface);
            }

            var account = NormalizeAccountLabel(usageEvent.AccountLabel, usageEvent.ProviderAccountId);
            var displayCost = UsageTelemetryApiPricing.BuildDisplayCost(usageEvent);
            if (!string.IsNullOrWhiteSpace(account)) {
                subtitleParts.Add(account);
            }

            RecentActivity.Add(new RecentUsageItemViewModel {
                EventKey = BuildEventKey(usageEvent),
                TimestampText = localTime.Date == DateTime.Now.Date
                    ? localTime.ToString("HH:mm", CultureInfo.CurrentCulture)
                    : localTime.ToString("MMM d HH:mm", CultureInfo.CurrentCulture),
                TimestampLocalText = localTime.ToString("dddd, MMM d yyyy HH:mm:ss", CultureInfo.CurrentCulture),
                TimestampUtcText = usageEvent.TimestampUtc.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture),
                Title = NormalizeOptional(usageEvent.Model) ?? "Unknown model",
                ModelText = NormalizeOptional(usageEvent.Model) ?? "Unknown model",
                SurfaceText = surface ?? "Unknown surface",
                AccountText = account ?? "Unknown account",
                TokensText = FormatTokens(usageEvent.TotalTokens ?? 0L),
                CostText = FormatCostDisplay(displayCost.TotalCostUsd, displayCost.UsesEstimatedFallback),
                InputText = FormatTokens(usageEvent.InputTokens ?? 0L),
                OutputText = FormatTokens(usageEvent.OutputTokens ?? 0L),
                CachedText = FormatTokens(usageEvent.CachedInputTokens ?? 0L),
                ReasoningText = FormatTokens(usageEvent.ReasoningTokens ?? 0L),
                Subtitle = subtitleParts.Count == 0 ? "No surface metadata" : string.Join(" • ", subtitleParts),
                MetricText = BuildRecentMetric(usageEvent)
            });
        }

        SelectedEvent = previousKey is not null
            ? RecentActivity.FirstOrDefault(item => string.Equals(item.EventKey, previousKey, StringComparison.Ordinal))
            : RecentActivity.FirstOrDefault();
    }

    private void PopulateConversationStats(IReadOnlyList<UsageEventRecord> events) {
        Conversations.Clear();
        _conversationSummaryData.Clear();
        _conversationSummaries.Clear();
        var summaries = UsageConversationSummaryBuilder.Build(events).ToList();
        var viewModels = summaries
            .Select(static summary => BuildConversationViewModel(summary))
            .ToList();

        _conversationSummaryData.AddRange(summaries);
        _conversationSummaries.AddRange(viewModels);
        foreach (var item in viewModels.Take(10)) {
            Conversations.Add(item);
        }

        OnPropertyChanged(nameof(HasConversationStats));
        OnPropertyChanged(nameof(ConversationStatsSummaryText));
    }

    private void PopulateProviderComparison(IReadOnlyList<UsageEventRecord> events) {
        ProviderComparison.Clear();
        if (!IsCombinedProvider) {
            return;
        }

        var providerGroups = events
            .GroupBy(static e => e.ProviderId?.Trim()?.ToLowerInvariant() ?? "unknown")
            .Where(static group => !string.IsNullOrWhiteSpace(group.Key))
            .Select(group => new {
                Info = ProviderMetadata.Resolve(group.Key),
                Events = group.Count(),
                Tokens = group.Sum(eventRecord => eventRecord.TotalTokens ?? 0L),
                CostRollup = UsageTelemetryApiPricing.BuildDisplayCost(group)
            });

        var orderedGroups = SelectedProviderComparisonSort switch {
            ProviderComparisonSort.Cost => providerGroups
                .OrderByDescending(static group => group.CostRollup.TotalCostUsd)
                .ThenByDescending(static group => group.Tokens)
                .ThenByDescending(static group => group.Events),
            ProviderComparisonSort.Events => providerGroups
                .OrderByDescending(static group => group.Events)
                .ThenByDescending(static group => group.Tokens)
                .ThenByDescending(static group => group.CostRollup.TotalCostUsd),
            _ => providerGroups
                .OrderByDescending(static group => group.Tokens)
                .ThenByDescending(static group => group.Events)
                .ThenByDescending(static group => group.CostRollup.TotalCostUsd)
        };

        var topGroups = orderedGroups.Take(8).ToList();
        var maxTokens = topGroups.Count > 0 ? topGroups.Max(static group => group.Tokens) : 0L;
        foreach (var group in topGroups) {
            var healthInfo = _providerComparisonHealth.TryGetValue(group.Info.Id, out var providerHealth)
                ? providerHealth
                : null;
            var deltaInfo = _providerComparisonDelta.TryGetValue(group.Info.Id, out var providerDelta)
                ? providerDelta
                : null;
            var historyInfo = _providerComparisonHistory.TryGetValue(group.Info.Id, out var providerHistory)
                ? providerHistory
                : null;
            ProviderComparison.Add(new ProviderComparisonEntryViewModel {
                ProviderId = group.Info.Id,
                DisplayName = group.Info.DisplayName,
                ShortName = group.Info.ShortName,
                TokensText = FormatTokens(group.Tokens),
                CostText = FormatCostDisplay(group.CostRollup.TotalCostUsd, group.CostRollup.UsesEstimatedFallback),
                EventCountText = FormatCountLabel(group.Events, "rollup", "rollups"),
                HealthText = healthInfo?.SummaryText ?? "Usage snapshot only",
                HealthBrush = healthInfo?.SummaryBrush ?? FrozenBrush(Color.FromRgb(144, 144, 184)),
                DeltaText = deltaInfo?.SummaryText ?? "No previous refresh baseline",
                DeltaBrush = deltaInfo?.SummaryBrush ?? FrozenBrush(Color.FromRgb(96, 96, 136)),
                HistoryText = historyInfo?.SummaryText ?? "Trend building...",
                HistoryBrush = historyInfo?.SummaryBrush ?? FrozenBrush(Color.FromRgb(96, 96, 136)),
                IsFavorite = _providerComparisonFavorites.Contains(group.Info.Id),
                Proportion = maxTokens > 0 ? (double)group.Tokens / maxTokens : 0d,
                BarBrush = FrozenBrush(group.Info.TotalColor)
            });
        }
    }

    private void PopulateCombinedOverview(IReadOnlyList<UsageEventRecord> events) {
        CombinedOverviewCards.Clear();
        if (!IsCombinedProvider) {
            return;
        }

        var providerGroups = events
            .GroupBy(static e => e.ProviderId?.Trim()?.ToLowerInvariant() ?? "unknown")
            .Where(static group => !string.IsNullOrWhiteSpace(group.Key))
            .Select(group => new {
                Info = ProviderMetadata.Resolve(group.Key),
                Events = group.Count(),
                Tokens = group.Sum(eventRecord => eventRecord.TotalTokens ?? 0L),
                CostRollup = UsageTelemetryApiPricing.BuildDisplayCost(group)
            })
            .OrderByDescending(static group => group.Tokens)
            .ThenByDescending(static group => group.Events)
            .ThenBy(static group => group.Info.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var leadingProvider = providerGroups.FirstOrDefault();
        CombinedOverviewCards.Add(new ProviderOverviewCardViewModel {
            Title = "Providers",
            MetricText = providerGroups.Count.ToString("N0", CultureInfo.CurrentCulture),
            DetailText = leadingProvider is null
                ? "No provider activity in the current range."
                : "Top: "
                  + leadingProvider.Info.DisplayName
                  + " • " + FormatTokens(leadingProvider.Tokens)
                  + " • " + FormatCountLabel(leadingProvider.Events, "rollup", "rollups"),
            AccentBrush = FrozenBrush(leadingProvider is null ? TotalColor : leadingProvider.Info.TotalColor)
        });

        CombinedOverviewCards.Add(new ProviderOverviewCardViewModel {
            Title = "Refresh Pulse",
            MetricText = HasRefreshBadge ? RefreshBadgeText : "0",
            DetailText = HasRefreshBadge
                ? RefreshBadgeToolTip
                : "No new activity since the previous refresh.",
            AccentBrush = HasRefreshBadge ? FrozenBrush(Color.FromRgb(144, 208, 160)) : FrozenBrush(Color.FromRgb(96, 96, 136))
        });

        if (_codeChurnSummary.HasData) {
            CombinedOverviewCards.Add(new ProviderOverviewCardViewModel {
                Title = "Code Churn",
                MetricText = CodeChurnAddedText + " / " + CodeChurnDeletedText,
                DetailText = CodeChurnSummaryText,
                AccentBrush = FrozenBrush(_codeChurnSummary.RecentNetLines >= 0
                    ? Color.FromRgb(144, 208, 160)
                    : Color.FromRgb(240, 192, 64))
            });
        }

        if (_codeUsageCorrelationSummary.HasData) {
            CombinedOverviewCards.Add(new ProviderOverviewCardViewModel {
                Title = "Churn x Usage",
                MetricText = BuildCombinedCorrelationMetricText(),
                DetailText = BuildCombinedCorrelationDetailText(),
                AccentBrush = FrozenBrush(BuildCombinedCorrelationAccentColor())
            });
        }

        if (_gitHubLocalActivityCorrelationSummary.HasData) {
            CombinedOverviewCards.Add(new ProviderOverviewCardViewModel {
                Title = "Repo Sync",
                MetricText = BuildCombinedGitHubLocalAlignmentMetricText(),
                DetailText = BuildCombinedGitHubLocalAlignmentDetailText(),
                AccentBrush = FrozenBrush(BuildCombinedGitHubLocalAlignmentAccentColor())
            });
        }

        if (_gitHubRepositoryClusterSummary.HasData) {
            CombinedOverviewCards.Add(new ProviderOverviewCardViewModel {
                Title = "Repo Cluster",
                MetricText = BuildCombinedGitHubClusterMetricText(),
                DetailText = BuildCombinedGitHubClusterDetailText(),
                AccentBrush = FrozenBrush(BuildCombinedGitHubClusterAccentColor())
            });
        }

        var (healthMetric, healthDetail, healthBrush) = BuildCombinedHealthOverview();
        CombinedOverviewCards.Add(new ProviderOverviewCardViewModel {
            Title = "Data Health",
            MetricText = healthMetric,
            DetailText = healthDetail,
            AccentBrush = healthBrush
        });

        var limitCandidate = SelectCombinedLimitCandidate();
        CombinedOverviewCards.Add(new ProviderOverviewCardViewModel {
            Title = "Live Limits",
            MetricText = limitCandidate?.DisplayName ?? "No live data",
            DetailText = limitCandidate?.HealthText ?? "No live limit data detected across providers.",
            AccentBrush = limitCandidate?.HealthBrush ?? FrozenBrush(Color.FromRgb(96, 96, 136))
        });
    }

    private (string Metric, string Detail, Brush AccentBrush) BuildCombinedHealthOverview() {
        var summary = NormalizeOptional(UsageHealthSummary);
        var detail = NormalizeOptional(UsageHealthDetail) ?? NormalizeOptional(UsageHealthAccountsText);
        var combinedText = string.Join(" ", new[] { summary, detail }.Where(static value => !string.IsNullOrWhiteSpace(value))).ToLowerInvariant();

        if (combinedText.Contains("partial", StringComparison.Ordinal) ||
            combinedText.Contains("missing", StringComparison.Ordinal) ||
            combinedText.Contains("stale", StringComparison.Ordinal)) {
            return (
                "Partial",
                summary ?? detail ?? "Some provider roots or artifacts were incomplete during the last scan.",
                FrozenBrush(Color.FromRgb(240, 192, 64)));
        }

        if (combinedText.Contains("cached", StringComparison.Ordinal)) {
            return (
                "Cached",
                summary ?? detail ?? "The tray is currently using persisted snapshot data.",
                FrozenBrush(Color.FromRgb(144, 144, 184)));
        }

        if (!string.IsNullOrWhiteSpace(summary) || !string.IsNullOrWhiteSpace(detail)) {
            return (
                "Checked",
                summary ?? detail ?? "Usage health data is available for this refresh.",
                FrozenBrush(Color.FromRgb(144, 208, 160)));
        }

        return (
            "Local",
            "No health warnings were recorded for the current explorer state.",
            FrozenBrush(Color.FromRgb(96, 96, 136)));
    }

    private ProviderComparisonEntryViewModel? SelectCombinedLimitCandidate() {
        return ProviderComparison
            .Select(entry => new {
                Entry = entry,
                Score = BuildLimitHealthScore(entry.HealthText)
            })
            .Where(static item => item.Score > 0d)
            .OrderByDescending(static item => item.Score)
            .ThenBy(static item => item.Entry.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .Select(static item => item.Entry)
            .FirstOrDefault();
    }

    private static double BuildLimitHealthScore(string? healthText) {
        if (string.IsNullOrWhiteSpace(healthText)) {
            return 0d;
        }

        var normalized = healthText.Trim();
        if (normalized.Contains("No live limit data", StringComparison.OrdinalIgnoreCase)) {
            return 0d;
        }

        var percentIndex = normalized.IndexOf('%');
        if (percentIndex > 0) {
            var start = percentIndex - 1;
            while (start >= 0 && (char.IsDigit(normalized[start]) || normalized[start] == '.')) {
                start--;
            }

            var numberText = normalized.Substring(start + 1, percentIndex - start - 1);
            if (double.TryParse(numberText, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent)) {
                return percent;
            }
        }

        return 1d;
    }

    private string BuildCombinedCorrelationMetricText() {
        var activityDelta = _codeUsageCorrelationSummary.ActivityDeltaRatio;
        var churnDelta = _codeUsageCorrelationSummary.ChurnDeltaRatio;
        var movingTogether = Math.Sign(activityDelta) == Math.Sign(churnDelta) && (Math.Abs(activityDelta) >= 0.10d || Math.Abs(churnDelta) >= 0.10d);
        if (movingTogether) {
            return "Moving together";
        }

        if (Math.Abs(activityDelta) < 0.10d && Math.Abs(churnDelta) < 0.10d) {
            return "Steady";
        }

        return "Diverging";
    }

    private string BuildCombinedCorrelationDetailText() {
        var strongest = _codeUsageCorrelationSummary.StrongestPositiveCorrelation;
        if (strongest is null) {
            return CodeUsageCorrelationSummaryText;
        }

        return CodeUsageCorrelationSummaryText
               + " • aligned: "
               + strongest.ProviderDisplayName
               + " "
               + FormatCorrelationValue(strongest.Correlation);
    }

    private Color BuildCombinedCorrelationAccentColor() {
        var strongestPositive = _codeUsageCorrelationSummary.StrongestPositiveCorrelation;
        if (strongestPositive is not null && strongestPositive.Correlation >= 0.55d) {
            return Color.FromRgb(144, 208, 160);
        }

        var strongestNegative = _codeUsageCorrelationSummary.StrongestNegativeCorrelation;
        if (strongestNegative is not null && strongestNegative.Correlation <= -0.55d) {
            return Color.FromRgb(240, 192, 64);
        }

        return Color.FromRgb(144, 144, 184);
    }

    private string BuildCombinedGitHubLocalAlignmentMetricText() {
        var strongestPositive = _gitHubLocalActivityCorrelationSummary.StrongestPositiveCorrelation;
        if (strongestPositive is not null) {
            return BuildShortRepositoryLabel(strongestPositive.RepositoryNameWithOwner)
                   + " "
                   + FormatCorrelationValue(strongestPositive.Correlation);
        }

        var strongestNegative = _gitHubLocalActivityCorrelationSummary.StrongestNegativeCorrelation;
        if (strongestNegative is not null) {
            return BuildShortRepositoryLabel(strongestNegative.RepositoryNameWithOwner)
                   + " "
                   + FormatCorrelationValue(strongestNegative.Correlation);
        }

        return _gitHubLocalActivityCorrelationSummary.WatchedRepositoryCount.ToString("N0", CultureInfo.CurrentCulture)
               + " watched";
    }

    private string BuildCombinedGitHubLocalAlignmentDetailText() {
        if (!_gitHubLocalActivityCorrelationSummary.HasSignals) {
            return _gitHubLocalActivityCorrelationSummary.WatchedRepositoryCount.ToString("N0", CultureInfo.CurrentCulture)
                   + " watched repos • waiting for overlapping local pulse data";
        }

        var parts = new List<string> {
            _gitHubLocalActivityCorrelationSummary.RepositoryCorrelations.Count.ToString("N0", CultureInfo.CurrentCulture)
            + " linked movers"
        };

        if (_gitHubLocalActivityCorrelationSummary.StrongestPositiveCorrelation is { } strongestPositive) {
            parts.Add("sync: "
                      + BuildShortRepositoryLabel(strongestPositive.RepositoryNameWithOwner)
                      + " "
                      + FormatCorrelationValue(strongestPositive.Correlation));
        }

        if (_gitHubLocalActivityCorrelationSummary.StrongestNegativeCorrelation is { } strongestNegative) {
            parts.Add("diverge: "
                      + BuildShortRepositoryLabel(strongestNegative.RepositoryNameWithOwner)
                      + " "
                      + FormatCorrelationValue(strongestNegative.Correlation));
        }

        parts.Add(_gitHubLocalActivityCorrelationSummary.ActiveLocalDays.ToString("N0", CultureInfo.CurrentCulture)
                  + " active local days");

        return string.Join(" • ", parts);
    }

    private Color BuildCombinedGitHubLocalAlignmentAccentColor() {
        var strongestPositive = _gitHubLocalActivityCorrelationSummary.StrongestPositiveCorrelation;
        if (strongestPositive is not null && strongestPositive.Correlation >= 0.55d) {
            return Color.FromRgb(144, 208, 160);
        }

        var strongestNegative = _gitHubLocalActivityCorrelationSummary.StrongestNegativeCorrelation;
        if (strongestNegative is not null && strongestNegative.Correlation <= -0.55d) {
            return Color.FromRgb(240, 192, 64);
        }

        return Color.FromRgb(144, 144, 184);
    }

    private string BuildCombinedGitHubClusterMetricText() {
        var strongest = _gitHubRepositoryClusterSummary.StrongestCluster;
        if (strongest is null) {
            return _gitHubRepositoryClusterSummary.WatchedRepositoryCount.ToString("N0", CultureInfo.CurrentCulture)
                   + " watched";
        }

        return BuildShortRepositoryLabel(strongest.RepositoryANameWithOwner)
               + " x "
               + BuildShortRepositoryLabel(strongest.RepositoryBNameWithOwner);
    }

    private string BuildCombinedGitHubClusterDetailText() {
        if (!_gitHubRepositoryClusterSummary.HasSignals) {
            return _gitHubRepositoryClusterSummary.WatchedRepositoryCount.ToString("N0", CultureInfo.CurrentCulture)
                   + " watched repos • waiting for shared momentum and audience overlap";
        }

        var strongest = _gitHubRepositoryClusterSummary.StrongestCluster!;
        var parts = new List<string> {
            strongest.SupportingSignalCount.ToString("N0", CultureInfo.CurrentCulture) + " signals",
            "star sync " + FormatCorrelationValue(strongest.StarCorrelation)
        };

        if (strongest.SharedStargazerCount > 0) {
            parts.Add(strongest.SharedStargazerCount.ToString("N0", CultureInfo.CurrentCulture) + " shared stargazers");
        }
        if (strongest.SharedForkOwnerCount > 0) {
            parts.Add(strongest.SharedForkOwnerCount.ToString("N0", CultureInfo.CurrentCulture) + " shared forkers");
        }
        if (strongest.LocallyAlignedRepositoryCount == 2) {
            parts.Add("both local " + FormatCorrelationValue(strongest.LocalAlignmentAverageCorrelation));
        }

        return string.Join(" • ", parts);
    }

    private Color BuildCombinedGitHubClusterAccentColor() {
        var strongest = _gitHubRepositoryClusterSummary.StrongestCluster;
        if (strongest is null) {
            return Color.FromRgb(144, 144, 184);
        }
        if (strongest.CompositeScore >= 0.70d) {
            return Color.FromRgb(144, 208, 160);
        }
        if (strongest.CompositeScore >= 0.50d) {
            return Color.FromRgb(240, 192, 64);
        }

        return Color.FromRgb(144, 144, 184);
    }

    private static string BuildShortRepositoryLabel(string repositoryNameWithOwner) {
        var normalized = NormalizeOptional(repositoryNameWithOwner);
        if (string.IsNullOrWhiteSpace(normalized)) {
            return "Repository";
        }

        var separatorIndex = normalized.LastIndexOf('/');
        return separatorIndex >= 0 && separatorIndex < normalized.Length - 1
            ? normalized.Substring(separatorIndex + 1)
            : normalized;
    }

    private static string FormatCorrelationValue(double correlation) {
        return correlation.ToString("+0.00;-0.00;0.00", CultureInfo.CurrentCulture);
    }

    private static string FormatShortSignedDelta(int value) {
        return value switch {
            > 0 => "+" + value.ToString("N0", CultureInfo.CurrentCulture),
            < 0 => "-" + Math.Abs(value).ToString("N0", CultureInfo.CurrentCulture),
            _ => "0"
        };
    }

    private Task FilterToSelectedModelAsync() {
        return ApplySelectedEventFilterAsync(FilterDimension.Model);
    }

    private Task FilterToSelectedSurfaceAsync() {
        return ApplySelectedEventFilterAsync(FilterDimension.Surface);
    }

    private Task FilterToSelectedAccountAsync() {
        return ApplySelectedEventFilterAsync(FilterDimension.Account);
    }

    private Task ApplySelectedEventFilterAsync(FilterDimension dimension) {
        if (SelectedEvent is null) {
            ActionStatusMessage = "Select a rollup first.";
            return Task.CompletedTask;
        }

        var (value, fallbackLabel, setter, dimensionLabel) = dimension switch {
            FilterDimension.Model => (
                NormalizeOptional(SelectedEvent.ModelText),
                ProviderFilterDefaults.AllModels,
                new Action<string>(value => SelectedModelFilter = value),
                "model"),
            FilterDimension.Surface => (
                NormalizeOptional(SelectedEvent.SurfaceText),
                ProviderFilterDefaults.AllSurfaces,
                new Action<string>(value => SelectedSurfaceFilter = value),
                "surface"),
            _ => (
                NormalizeOptional(SelectedEvent.AccountText),
                ProviderFilterDefaults.AllAccounts,
                new Action<string>(value => SelectedAccountFilter = value),
                "account")
        };

        if (string.IsNullOrWhiteSpace(value) || value.StartsWith("Unknown ", StringComparison.OrdinalIgnoreCase)) {
            ActionStatusMessage = "The selected rollup does not include a usable " + dimensionLabel + ".";
            return Task.CompletedTask;
        }

        setter(value);
        ActionStatusMessage = "Filtered to " + dimensionLabel + ": " + value;
        return Task.CompletedTask;
    }

    private Task ClearFiltersAsync() {
        SelectedAccountFilter = ProviderFilterDefaults.AllAccounts;
        SelectedModelFilter = ProviderFilterDefaults.AllModels;
        SelectedSurfaceFilter = ProviderFilterDefaults.AllSurfaces;
        ActionStatusMessage = "Cleared account, model, and surface filters.";
        return Task.CompletedTask;
    }

    private Task CopySummaryAsync() {
        try {
            var lines = new List<string> {
                $"{DisplayName} usage summary",
                $"Range: {TodayLabel}",
                $"Filters: {BuildFilterSummary()}",
                $"Rollups: {TodayEventCount}",
                $"Tokens: {TodayTotalTokensFormatted}",
                $"Input: {TodayInputTokensFormatted}",
                $"Output: {TodayOutputTokensFormatted}",
                $"Cached: {TodayCachedTokensFormatted}",
                $"Reasoning: {TodayReasoningTokensFormatted}",
                $"API equivalent: {TodayCostFormatted}"
            };

            if (HasSurfaceBreakdown) {
                lines.Add("Top surfaces:");
                lines.AddRange(SurfaceBreakdown.Select(entry => $"  {entry.Label}: {entry.ValueText}"));
            }

            if (HasAccountBreakdown) {
                lines.Add("Top accounts:");
                lines.AddRange(AccountBreakdown.Select(entry => $"  {entry.Label}: {entry.ValueText}"));
            }

            if (ModelBreakdown.Count > 0) {
                lines.Add("Top models:");
                lines.AddRange(ModelBreakdown.Select(entry => $"  {entry.ModelName}: {entry.TotalTokensFormatted}"));
            }

            if (HasModelDaySummaries) {
                lines.Add("Local models by day:");
                lines.AddRange(ModelDaySummaries.Select(entry => $"  {entry.DayLabel}: {entry.TotalTokensText} • {entry.ModelsText}"));
            }

            if (_conversationSummaryData.Count > 0) {
                var compactCount = _conversationSummaryData.Sum(static conversation => conversation.CompactCount);
                lines.Add("Conversations:");
                lines.Add("  " + FormatCountLabel(_conversationSummaryData.Count, "conversation", "conversations"));
                lines.Add("  " + FormatCountLabel(_conversationSummaryData.Sum(static conversation => conversation.TurnCount), "turn", "turns"));
                if (compactCount > 0) {
                    lines.Add("  " + FormatCountLabel(compactCount, "compact", "compacts"));
                }

                lines.Add("Top conversations:");
                lines.AddRange(_conversationSummaryData
                    .Take(5)
                    .Select(static conversation => {
                        var parts = new List<string> {
                            FormatTokens(conversation.TotalTokens),
                            "span " + HeatmapDisplayText.FormatDuration(conversation.Duration),
                            "active " + HeatmapDisplayText.FormatDuration(conversation.ActiveDuration),
                            FormatCountLabel(conversation.TurnCount, "turn", "turns")
                        };
                        if (conversation.CompactCount > 0) {
                            parts.Add(FormatCountLabel(conversation.CompactCount, "compact", "compacts"));
                        }
                        if (conversation.Models.Count > 0) {
                            parts.Add(string.Join(", ", conversation.Models));
                        }
                        var contextLabel = conversation.RepositoryName ?? conversation.WorkspaceName;
                        if (!string.IsNullOrWhiteSpace(contextLabel)) {
                            parts.Add(contextLabel);
                        }

                        var title = conversation.ConversationTitle ?? FormatSessionSnippet(conversation.SessionId);
                        return "  " + title + ": " + string.Join(" • ", parts);
                    }));
            }

            if (HasDataScopeSection) {
                lines.Add("Data scope:");
                if (HasScopeLocalText) {
                    lines.Add("  Local: " + ScopeLocalText);
                }
                if (HasScopeOnlineText) {
                    lines.Add("  Online: " + ScopeOnlineText);
                }
                if (HasScopeDifferenceText) {
                    lines.Add("  Why they differ: " + ScopeDifferenceText);
                }
            }

            if (HasLimitSection) {
                lines.Add("Live limits:");
                if (!string.IsNullOrWhiteSpace(LimitSourceLabel)) {
                    lines.Add("  Source: " + LimitSourceLabel);
                }
                if (!string.IsNullOrWhiteSpace(LimitPlanLabel)) {
                    lines.Add("  Plan: " + LimitPlanLabel);
                }
                if (!string.IsNullOrWhiteSpace(LimitAccountLabel)) {
                    lines.Add("  Account: " + LimitAccountLabel);
                }
                if (!string.IsNullOrWhiteSpace(LimitSummary)) {
                    lines.Add("  Summary: " + LimitSummary);
                }
                if (!string.IsNullOrWhiteSpace(LimitStatusMessage)) {
                    lines.Add("  Detail: " + LimitStatusMessage);
                }
            }

            Clipboard.SetText(string.Join(Environment.NewLine, lines));
            ActionStatusMessage = "Copied selected range summary to clipboard.";
        } catch (Exception ex) {
            ActionStatusMessage = "Copy failed: " + ex.Message;
        }

        return Task.CompletedTask;
    }

    private Task CopySelectedEventAsync() {
        if (SelectedEvent is null) {
            ActionStatusMessage = "Select a rollup first.";
            return Task.CompletedTask;
        }

        try {
            var lines = new List<string> {
                $"{DisplayName} rollup details",
                $"Model: {SelectedEvent.ModelText}",
                $"Surface: {SelectedEvent.SurfaceText}",
                $"Account: {SelectedEvent.AccountText}",
                $"Local: {SelectedEvent.TimestampLocalText}",
                $"UTC: {SelectedEvent.TimestampUtcText}",
                $"Tokens: {SelectedEvent.TokensText}",
                $"Input: {SelectedEvent.InputText}",
                $"Output: {SelectedEvent.OutputText}",
                $"Cached: {SelectedEvent.CachedText}",
                $"Reasoning: {SelectedEvent.ReasoningText}",
                $"API equivalent: {SelectedEvent.CostText}"
            };

            Clipboard.SetText(string.Join(Environment.NewLine, lines));
            ActionStatusMessage = "Copied selected rollup details to clipboard.";
        } catch (Exception ex) {
            ActionStatusMessage = "Copy failed: " + ex.Message;
        }

        return Task.CompletedTask;
    }

    private Task CopySelectedEventJsonAsync() {
        if (SelectedEvent is null) {
            ActionStatusMessage = "Select a rollup first.";
            return Task.CompletedTask;
        }

        try {
            var payload = new {
                provider = DisplayName,
                model = SelectedEvent.ModelText,
                surface = SelectedEvent.SurfaceText,
                account = SelectedEvent.AccountText,
                timestampLocal = SelectedEvent.TimestampLocalText,
                timestampUtc = SelectedEvent.TimestampUtcText,
                tokens = SelectedEvent.TokensText,
                inputTokens = SelectedEvent.InputText,
                outputTokens = SelectedEvent.OutputText,
                cachedTokens = SelectedEvent.CachedText,
                reasoningTokens = SelectedEvent.ReasoningText,
                apiEquivalentCost = SelectedEvent.CostText
            };

            Clipboard.SetText(JsonSerializer.Serialize(payload, new JsonSerializerOptions {
                WriteIndented = true
            }));
            ActionStatusMessage = "Copied selected rollup JSON to clipboard.";
        } catch (Exception ex) {
            ActionStatusMessage = "JSON copy failed: " + ex.Message;
        }

        return Task.CompletedTask;
    }

    private async Task ExportJsonAsync() {
        try {
            var dialog = new SaveFileDialog {
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                FileName = BuildExportFileName("json")
            };
            if (dialog.ShowDialog() != true) {
                return;
            }

            var events = ApplyFilters(GetRangeEvents(SelectedRange));
            var payload = new {
                provider = DisplayName,
                providerId = ProviderId,
                range = TodayLabel,
                filters = new {
                    account = SelectedAccountFilter,
                    model = SelectedModelFilter,
                    surface = SelectedSurfaceFilter
                },
                summary = new {
                    rollups = TodayEventCount,
                    totalTokens = TodayTotalTokens,
                    inputTokens = TodayInputTokens,
                    outputTokens = TodayOutputTokens,
                    cachedTokens = TodayCachedTokens,
                    reasoningTokens = TodayReasoningTokens,
                    apiEquivalentCostUsd = TodayCostUsd,
                    costApproximate = TodayCostUsesEstimate
                },
                dataScope = HasDataScopeSection
                    ? new {
                        local = ScopeLocalText,
                        online = ScopeOnlineText,
                        whyDifferent = ScopeDifferenceText
                    }
                    : null,
                liveLimits = HasLimitSection
                    ? new {
                        source = LimitSourceLabel,
                        plan = LimitPlanLabel,
                        account = LimitAccountLabel,
                        summary = LimitSummary,
                        detail = LimitStatusMessage,
                        windows = LimitWindows.Select(window => new {
                            label = window.Label,
                            usedPercent = window.UsedPercent,
                            reset = window.ResetText,
                            detail = window.Detail
                        }),
                        accounts = LimitAccounts.Select(account => new {
                            label = account.Label,
                            plan = account.PlanLabel,
                            status = account.StatusLabel,
                            summary = account.Summary,
                            detail = account.DetailText,
                            windows = account.Windows.Select(window => new {
                                label = window.Label,
                                usedPercent = window.UsedPercent,
                                reset = window.ResetText,
                                detail = window.Detail
                            })
                        })
                    }
                    : null,
                modelsByDay = ModelDaySummaries.Select(entry => new {
                    day = entry.DayLabel,
                    totalTokens = entry.TotalTokensText,
                    models = entry.ModelsText
                }),
                conversations = _conversationSummaryData.Select(conversation => new {
                    conversationKey = conversation.ConversationKey,
                    providerId = conversation.ProviderId,
                    provider = UsageTelemetryProviderCatalog.ResolveDisplayTitle(conversation.ProviderId),
                    providerAccountId = conversation.ProviderAccountId,
                    account = conversation.AccountLabel,
                    sessionId = conversation.SessionId,
                    title = conversation.ConversationTitle,
                    workspacePath = conversation.WorkspacePath,
                    workspace = conversation.WorkspaceName,
                    repository = conversation.RepositoryName,
                    startedLocal = conversation.StartedUtc.ToLocalTime().ToString("O", CultureInfo.InvariantCulture),
                    startedUtc = conversation.StartedUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                    lastSeenLocal = conversation.LastSeenUtc.ToLocalTime().ToString("O", CultureInfo.InvariantCulture),
                    lastSeenUtc = conversation.LastSeenUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                    durationMs = (long)conversation.Duration.TotalMilliseconds,
                    duration = HeatmapDisplayText.FormatDuration(conversation.Duration),
                    activeDurationMs = (long)conversation.ActiveDuration.TotalMilliseconds,
                    activeDuration = HeatmapDisplayText.FormatDuration(conversation.ActiveDuration),
                    turnCount = conversation.TurnCount,
                    compactCount = conversation.CompactCount,
                    models = conversation.Models,
                    surfaces = conversation.Surfaces,
                    inputTokens = conversation.InputTokens,
                    outputTokens = conversation.OutputTokens,
                    cachedTokens = conversation.CachedInputTokens,
                    reasoningTokens = conversation.ReasoningTokens,
                    totalTokens = conversation.TotalTokens,
                    apiEquivalentCostUsd = conversation.CostUsd,
                    costApproximate = conversation.CostUsesEstimatedFallback
                }),
                rollups = events.Select(e => {
                    var displayCost = UsageTelemetryApiPricing.BuildDisplayCost(e);
                    return new {
                        timestampLocal = e.TimestampUtc.ToLocalTime().ToString("O", CultureInfo.InvariantCulture),
                        timestampUtc = e.TimestampUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                        account = NormalizeAccountLabel(e.AccountLabel, e.ProviderAccountId),
                        model = NormalizeOptional(e.Model),
                        surface = NormalizeSurfaceLabel(e.Surface),
                        inputTokens = e.InputTokens,
                        outputTokens = e.OutputTokens,
                        cachedTokens = e.CachedInputTokens,
                        reasoningTokens = e.ReasoningTokens,
                        totalTokens = e.TotalTokens,
                        apiEquivalentCostUsd = displayCost.TotalCostUsd,
                        costApproximate = displayCost.UsesEstimatedFallback
                    };
                })
            };

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions {
                WriteIndented = true
            });
            await File.WriteAllTextAsync(dialog.FileName, json).ConfigureAwait(true);
            ActionStatusMessage = "Exported JSON to " + dialog.FileName;
        } catch (Exception ex) {
            ActionStatusMessage = "JSON export failed: " + ex.Message;
        }
    }

    private async Task ExportCsvAsync() {
        try {
            var dialog = new SaveFileDialog {
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                FileName = BuildExportFileName("csv")
            };
            if (dialog.ShowDialog() != true) {
                return;
            }

            var builder = new StringBuilder();
            builder.AppendLine("timestamp_local,timestamp_utc,account,model,surface,input_tokens,output_tokens,cached_tokens,reasoning_tokens,total_tokens,api_equivalent_cost_usd");
            foreach (var usageEvent in ApplyFilters(GetRangeEvents(SelectedRange))) {
                builder.AppendLine(string.Join(",",
                    EscapeCsv(usageEvent.TimestampUtc.ToLocalTime().ToString("O", CultureInfo.InvariantCulture)),
                    EscapeCsv(usageEvent.TimestampUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)),
                    EscapeCsv(NormalizeAccountLabel(usageEvent.AccountLabel, usageEvent.ProviderAccountId)),
                    EscapeCsv(NormalizeOptional(usageEvent.Model)),
                    EscapeCsv(NormalizeSurfaceLabel(usageEvent.Surface)),
                    EscapeCsv(usageEvent.InputTokens),
                    EscapeCsv(usageEvent.OutputTokens),
                    EscapeCsv(usageEvent.CachedInputTokens),
                    EscapeCsv(usageEvent.ReasoningTokens),
                    EscapeCsv(usageEvent.TotalTokens),
                    EscapeCsv(FormatExportCost(usageEvent))));
            }

            if (_conversationSummaryData.Count > 0) {
                builder.AppendLine();
                builder.AppendLine("conversation_key,provider,provider_id,account,provider_account_id,session_id,title,workspace_path,workspace,repository,started_local,started_utc,last_seen_local,last_seen_utc,duration_ms,active_duration_ms,turn_count,compact_count,models,surfaces,input_tokens,output_tokens,cached_tokens,reasoning_tokens,total_tokens,api_equivalent_cost_usd,cost_approximate");
                foreach (var conversation in _conversationSummaryData) {
                    builder.AppendLine(string.Join(",",
                        EscapeCsv(conversation.ConversationKey),
                        EscapeCsv(UsageTelemetryProviderCatalog.ResolveDisplayTitle(conversation.ProviderId)),
                        EscapeCsv(conversation.ProviderId),
                        EscapeCsv(conversation.AccountLabel),
                        EscapeCsv(conversation.ProviderAccountId),
                        EscapeCsv(conversation.SessionId),
                        EscapeCsv(conversation.ConversationTitle),
                        EscapeCsv(conversation.WorkspacePath),
                        EscapeCsv(conversation.WorkspaceName),
                        EscapeCsv(conversation.RepositoryName),
                        EscapeCsv(conversation.StartedUtc.ToLocalTime().ToString("O", CultureInfo.InvariantCulture)),
                        EscapeCsv(conversation.StartedUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)),
                        EscapeCsv(conversation.LastSeenUtc.ToLocalTime().ToString("O", CultureInfo.InvariantCulture)),
                        EscapeCsv(conversation.LastSeenUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)),
                        EscapeCsv((long)conversation.Duration.TotalMilliseconds),
                        EscapeCsv((long)conversation.ActiveDuration.TotalMilliseconds),
                        EscapeCsv(conversation.TurnCount),
                        EscapeCsv(conversation.CompactCount),
                        EscapeCsv(string.Join(" | ", conversation.Models)),
                        EscapeCsv(string.Join(" | ", conversation.Surfaces)),
                        EscapeCsv(conversation.InputTokens),
                        EscapeCsv(conversation.OutputTokens),
                        EscapeCsv(conversation.CachedInputTokens),
                        EscapeCsv(conversation.ReasoningTokens),
                        EscapeCsv(conversation.TotalTokens),
                        EscapeCsv(FormatExportCost(conversation.CostUsd, conversation.CostUsesEstimatedFallback)),
                        EscapeCsv(conversation.CostUsesEstimatedFallback)));
                }
            }

            await File.WriteAllTextAsync(dialog.FileName, builder.ToString()).ConfigureAwait(true);
            ActionStatusMessage = "Exported CSV to " + dialog.FileName;
        } catch (Exception ex) {
            ActionStatusMessage = "CSV export failed: " + ex.Message;
        }
    }

    private async Task OpenDetailedReportAsync() {
        try {
            var events = ApplyFilters(GetRangeEvents(SelectedRange));
            if (events.Count == 0) {
                ActionStatusMessage = "No rollups are available for the current range and filters.";
                return;
            }

            var outputDirectory = Path.Combine(
                Path.GetTempPath(),
                "IntelligenceX",
                "TrayReports",
                SanitizeFileSegment(DisplayName) + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));

            var title = $"{DisplayName} usage report";
            if (HasActiveFilters) {
                title += " (filtered)";
            }

            var overview = await Task.Run(() => new UsageTelemetryOverviewBuilder().Build(
                events.OrderBy(static e => e.TimestampUtc).ToArray(),
                new UsageTelemetryOverviewOptions {
                    Metric = UsageSummaryMetric.TotalTokens,
                    Title = title,
                    Subtitle = "Tray explorer: " + TodayLabel + " | " + BuildFilterSummary(),
                    Metadata = BuildReportMetadata()
                })).ConfigureAwait(true);
            var reportPath = await Task.Run(() => UsageTelemetryOverviewReportExporter.WriteBundle(overview, outputDirectory)).ConfigureAwait(true);

            Process.Start(new ProcessStartInfo {
                FileName = reportPath,
                UseShellExecute = true
            });
            ActionStatusMessage = "Opened detailed report from " + outputDirectory;
        } catch (Exception ex) {
            ActionStatusMessage = "Report generation failed: " + ex.Message;
        }
    }

    private static string BuildRecentMetric(UsageEventRecord usageEvent) {
        var parts = new List<string>();
        if (usageEvent.TotalTokens.HasValue && usageEvent.TotalTokens.Value > 0L) {
            parts.Add(FormatMetricValue(usageEvent.TotalTokens.Value));
        }

        var displayCost = UsageTelemetryApiPricing.BuildDisplayCost(usageEvent);
        if (displayCost.HasAnyCost) {
            parts.Add(FormatCostDisplay(displayCost.TotalCostUsd, displayCost.UsesEstimatedFallback));
        }

        return parts.Count == 0 ? "No token or API-equivalent cost data" : string.Join(" • ", parts);
    }

    private static string BuildEventKey(UsageEventRecord usageEvent) {
        return string.Join("|",
            usageEvent.TimestampUtc.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture),
            NormalizeOptional(usageEvent.Model) ?? "unknown-model",
            NormalizeSurfaceLabel(usageEvent.Surface) ?? "unknown-surface",
            NormalizeAccountLabel(usageEvent.AccountLabel, usageEvent.ProviderAccountId) ?? "unknown-account",
            (usageEvent.TotalTokens ?? 0L).ToString(CultureInfo.InvariantCulture),
            FormatExportCost(usageEvent));
    }

    private static string FormatSessionSnippet(string value) {
        var normalized = NormalizeOptional(value) ?? "unknown-session";
        if (normalized.Length <= 18) {
            return normalized;
        }

        return normalized[..8] + "..." + normalized[^6..];
    }

    private static ConversationUsageViewModel BuildConversationViewModel(UsageConversationSummary summary) {
        var models = summary.Models.Count == 0 ? "Unknown model" : string.Join(", ", summary.Models);
        var surfaces = summary.Surfaces.Count == 0 ? "Unknown surface" : string.Join(", ", summary.Surfaces);

        return new ConversationUsageViewModel {
            ConversationKey = summary.ConversationKey,
            ProviderText = UsageTelemetryProviderCatalog.ResolveDisplayTitle(summary.ProviderId),
            SessionText = FormatSessionSnippet(summary.SessionId),
            TitleText = summary.ConversationTitle ?? "Untitled session",
            WorkspaceText = summary.WorkspaceName ?? "Unknown workspace",
            RepositoryText = summary.RepositoryName ?? summary.WorkspaceName ?? "Unknown repo",
            AccountText = summary.AccountLabel ?? summary.ProviderAccountId ?? "Unknown account",
            StartedText = summary.StartedUtc.ToLocalTime().ToString("MMM d HH:mm", CultureInfo.CurrentCulture),
            LastSeenText = summary.LastSeenUtc.ToLocalTime().ToString("MMM d HH:mm", CultureInfo.CurrentCulture),
            DurationText = HeatmapDisplayText.FormatDuration(summary.Duration),
            ActiveDurationText = HeatmapDisplayText.FormatDuration(summary.ActiveDuration),
            TurnsText = FormatCountLabel(summary.TurnCount, "turn", "turns"),
            TokensText = FormatTokens(summary.TotalTokens),
            InputText = FormatTokens(summary.InputTokens),
            OutputText = FormatTokens(summary.OutputTokens),
            CachedText = FormatTokens(summary.CachedInputTokens),
            ReasoningText = FormatTokens(summary.ReasoningTokens),
            CostText = FormatCostDisplay(summary.CostUsd, summary.CostUsesEstimatedFallback),
            ModelsText = models,
            SurfaceText = surfaces,
            CompactCountText = summary.CompactCount > 0
                ? summary.CompactCount.ToString("N0", CultureInfo.CurrentCulture)
                : "0"
        };
    }

    private string BuildConversationStatsSummaryText() {
        if (_conversationSummaryData.Count == 0) {
            return "No per-conversation rows are available for this range.";
        }

        var turnCount = _conversationSummaryData.Sum(static conversation => conversation.TurnCount);
        var compactCount = _conversationSummaryData.Sum(static conversation => conversation.CompactCount);
        var tokenCount = _conversationSummaryData.Sum(static conversation => conversation.TotalTokens);
        var summaryParts = new List<string> {
            FormatTokens(tokenCount),
            FormatCountLabel(turnCount, "turn", "turns")
        };
        if (compactCount > 0) {
            summaryParts.Add(FormatCountLabel(compactCount, "compact", "compacts"));
        }

        var summarySuffix = " • " + string.Join(" • ", summaryParts);
        if (_conversationSummaryData.Count == Conversations.Count) {
            return FormatCountLabel(Conversations.Count, "conversation", "conversations")
                   + summarySuffix;
        }

        return Conversations.Count.ToString("N0", CultureInfo.CurrentCulture)
               + " of "
               + FormatCountLabel(_conversationSummaryData.Count, "conversation", "conversations")
               + summarySuffix
               + "; exports include all.";
    }

    private static string? NormalizeOptional(string? value) {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private JsonObject? BuildReportMetadata() {
        var hasConversationSummary = _conversationSummaryData.Count > 0;
        if (!HasUsageHealthSection && !HasDataScopeSection && !hasConversationSummary) {
            return null;
        }

        var metadata = new JsonObject();
        if (HasUsageHealthSection) {
            var reportHealth = new JsonObject()
                .Add("source", "tray-explorer");
            if (!string.IsNullOrWhiteSpace(UsageHealthSummary)) {
                reportHealth.Add("summary", UsageHealthSummary);
            }
            if (!string.IsNullOrWhiteSpace(UsageHealthDetail)) {
                reportHealth.Add("detail", UsageHealthDetail);
            }
            if (!string.IsNullOrWhiteSpace(UsageHealthAccountsText)) {
                reportHealth.Add("accountsText", UsageHealthAccountsText);
            }
            reportHealth.Add("generatedAtLocal", LastUpdated.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture));
            metadata.Add("reportHealth", reportHealth);
        }

        if (HasDataScopeSection) {
            var dataScope = new JsonObject();
            if (!string.IsNullOrWhiteSpace(ScopeLocalText)) {
                dataScope.Add("local", ScopeLocalText);
            }
            if (!string.IsNullOrWhiteSpace(ScopeOnlineText)) {
                dataScope.Add("online", ScopeOnlineText);
            }
            if (!string.IsNullOrWhiteSpace(ScopeDifferenceText)) {
                dataScope.Add("whyDifferent", ScopeDifferenceText);
            }
            metadata.Add("dataScope", dataScope);
        }

        if (hasConversationSummary) {
            metadata.Add("conversations", BuildReportConversationMetadata());
        }

        return metadata;
    }

    private JsonObject BuildReportConversationMetadata() {
        var conversations = _conversationSummaryData;
        var items = new JsonArray();
        foreach (var conversation in conversations.Take(25)) {
            var models = new JsonArray();
            foreach (var model in conversation.Models) {
                models.Add(model);
            }

            var surfaces = new JsonArray();
            foreach (var surface in conversation.Surfaces) {
                surfaces.Add(surface);
            }

            var account = conversation.AccountLabel ?? conversation.ProviderAccountId ?? "Unknown account";
            var label = FormatSessionSnippet(conversation.SessionId);
            items.Add(new JsonObject()
                .Add("conversationKey", conversation.ConversationKey)
                .Add("providerId", conversation.ProviderId)
                .Add("provider", UsageTelemetryProviderCatalog.ResolveDisplayTitle(conversation.ProviderId))
                .Add("providerAccountId", conversation.ProviderAccountId)
                .Add("account", account)
                .Add("sessionId", conversation.SessionId)
                .Add("title", conversation.ConversationTitle)
                .Add("workspacePath", conversation.WorkspacePath)
                .Add("workspace", conversation.WorkspaceName)
                .Add("repository", conversation.RepositoryName)
                .Add("label", label)
                .Add("startedLocal", conversation.StartedUtc.ToLocalTime().ToString("MMM d HH:mm", CultureInfo.CurrentCulture))
                .Add("startedUtc", conversation.StartedUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))
                .Add("lastSeenLocal", conversation.LastSeenUtc.ToLocalTime().ToString("MMM d HH:mm", CultureInfo.CurrentCulture))
                .Add("lastSeenUtc", conversation.LastSeenUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))
                .Add("durationMs", (long)conversation.Duration.TotalMilliseconds)
                .Add("duration", HeatmapDisplayText.FormatDuration(conversation.Duration))
                .Add("activeDurationMs", (long)conversation.ActiveDuration.TotalMilliseconds)
                .Add("activeDuration", HeatmapDisplayText.FormatDuration(conversation.ActiveDuration))
                .Add("turnCount", conversation.TurnCount)
                .Add("compactCount", conversation.CompactCount)
                .Add("inputTokens", conversation.InputTokens)
                .Add("outputTokens", conversation.OutputTokens)
                .Add("cachedTokens", conversation.CachedInputTokens)
                .Add("reasoningTokens", conversation.ReasoningTokens)
                .Add("totalTokens", conversation.TotalTokens)
                .Add("apiEquivalentCostUsd", (double)conversation.CostUsd)
                .Add("costApproximate", conversation.CostUsesEstimatedFallback)
                .Add("models", models)
                .Add("surfaces", surfaces));
        }

        return new JsonObject()
            .Add("totalCount", conversations.Count)
            .Add("shownCount", items.Count)
            .Add("tokenTotal", conversations.Sum(static conversation => conversation.TotalTokens))
            .Add("turnCount", conversations.Sum(static conversation => (long)conversation.TurnCount))
            .Add("compactCount", conversations.Sum(static conversation => (long)conversation.CompactCount))
            .Add("items", items);
    }

    private static string? NormalizeAccountLabel(string? value, string? providerAccountId = null) {
        var normalized = NormalizeOptional(value);
        if (normalized is null || string.Equals(normalized, "unknown-account", StringComparison.OrdinalIgnoreCase)) {
            normalized = NormalizeOptional(providerAccountId);
            if (normalized is null || string.Equals(normalized, "unknown-account", StringComparison.OrdinalIgnoreCase)) {
                return null;
            }
        }

        if (normalized.StartsWith("acct:", StringComparison.OrdinalIgnoreCase)) {
            return normalized["acct:".Length..];
        }

        if (normalized.StartsWith("label:", StringComparison.OrdinalIgnoreCase)) {
            return normalized["label:".Length..];
        }

        return normalized;
    }

    private static string? NormalizeSurfaceLabel(string? value) {
        var normalized = NormalizeOptional(value);
        if (normalized is null || string.Equals(normalized, "unknown-surface", StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        return normalized;
    }

    private static string FormatMetricValue(decimal value) {
        if (value >= 1_000_000_000m) {
            return (value / 1_000_000_000m).ToString("0.0", CultureInfo.InvariantCulture) + "B";
        }
        if (value >= 1_000_000m) {
            return (value / 1_000_000m).ToString("0.0", CultureInfo.InvariantCulture) + "M";
        }
        if (value >= 1_000m) {
            return (value / 1_000m).ToString("0.0", CultureInfo.InvariantCulture) + "K";
        }

        return decimal.Truncate(value) == value
            ? value.ToString("N0", CultureInfo.InvariantCulture)
            : value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static string FormatCountLabel(int count, string singular, string plural) {
        return count.ToString("N0", CultureInfo.CurrentCulture)
               + " "
               + (count == 1 ? singular : plural);
    }

    private static void ResetFilterOptions(ObservableCollection<string> target, string allLabel, IReadOnlyList<string> values) {
        target.Clear();
        target.Add(allLabel);
        foreach (var value in values) {
            target.Add(value);
        }
    }

    private static string NormalizeSelectedFilter(string currentValue, ObservableCollection<string> options, string fallback) {
        return options.Any(option => string.Equals(option, currentValue, StringComparison.OrdinalIgnoreCase))
            ? options.First(option => string.Equals(option, currentValue, StringComparison.OrdinalIgnoreCase))
            : fallback;
    }

    private static bool MatchesFilter(string filterValue, string? actualValue, string allLabel) {
        return string.Equals(filterValue, allLabel, StringComparison.Ordinal)
               || string.Equals(filterValue, actualValue, StringComparison.OrdinalIgnoreCase);
    }

    private string BuildFilterSummary() {
        var filters = new List<string>();
        if (!string.Equals(SelectedAccountFilter, ProviderFilterDefaults.AllAccounts, StringComparison.Ordinal)) {
            filters.Add("account=" + SelectedAccountFilter);
        }
        if (!string.Equals(SelectedModelFilter, ProviderFilterDefaults.AllModels, StringComparison.Ordinal)) {
            filters.Add("model=" + SelectedModelFilter);
        }
        if (!string.Equals(SelectedSurfaceFilter, ProviderFilterDefaults.AllSurfaces, StringComparison.Ordinal)) {
            filters.Add("surface=" + SelectedSurfaceFilter);
        }

        return filters.Count == 0 ? "none" : string.Join(", ", filters);
    }

    private string BuildExportFileName(string extension) {
        var provider = SanitizeFileSegment(DisplayName);
        var range = SelectedRange switch {
            ProviderTimeRange.Today => "today",
            ProviderTimeRange.Last7Days => "7days",
            ProviderTimeRange.Last30Days => "30days",
            ProviderTimeRange.AllTime => "alltime",
            _ => "range"
        };
        return $"{provider}-{range}.{extension}";
    }

    private static string SanitizeFileSegment(string? value) {
        var normalized = NormalizeOptional(value) ?? "usage";
        var invalid = Path.GetInvalidFileNameChars();
        var chars = normalized.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray();
        return new string(chars);
    }

    private static string EscapeCsv(object? value) {
        var text = value?.ToString() ?? string.Empty;
        if (text.Contains('"')) {
            text = text.Replace("\"", "\"\"");
        }

        if (text.Contains(',') || text.Contains('"') || text.Contains('\n') || text.Contains('\r')) {
            return "\"" + text + "\"";
        }

        return text;
    }

    private IEnumerable<UsageEventRecord> OrderEventsForDisplay(IEnumerable<UsageEventRecord> events) {
        return SelectedEventSort switch {
            ProviderEventSort.MostTokens => events
                .OrderByDescending(static e => e.TotalTokens ?? 0L)
                .ThenByDescending(static e => e.TimestampUtc),
            ProviderEventSort.HighestCost => events
                .OrderByDescending(static e => UsageTelemetryApiPricing.BuildDisplayCost(e).TotalCostUsd)
                .ThenByDescending(static e => e.TotalTokens ?? 0L)
                .ThenByDescending(static e => e.TimestampUtc),
            ProviderEventSort.Model => events
                .OrderBy(static e => NormalizeOptional(e.Model) ?? "unknown-model", StringComparer.CurrentCultureIgnoreCase)
                .ThenByDescending(static e => e.TimestampUtc),
            _ => events.OrderByDescending(static e => e.TimestampUtc)
        };
    }

    private static void ApplyDisplayCost(UsageTelemetryDisplayCost cost, Action<decimal> assignValue, Action<bool> assignApproximate) {
        assignValue(cost.TotalCostUsd);
        assignApproximate(cost.UsesEstimatedFallback);
    }

    private static string FormatExportCost(UsageEventRecord usageEvent) {
        var displayCost = UsageTelemetryApiPricing.BuildDisplayCost(usageEvent);
        if (!displayCost.HasAnyCost) {
            return string.Empty;
        }

        return FormatExportCost(displayCost.TotalCostUsd, displayCost.UsesEstimatedFallback);
    }

    private static string FormatExportCost(decimal costUsd, bool approximate) {
        if (costUsd <= 0m) {
            return string.Empty;
        }

        return (approximate ? "~" : string.Empty)
               + costUsd.ToString("0.####", CultureInfo.InvariantCulture);
    }

    private static string FormatCostDisplay(decimal costUsd, bool approximate) {
        if (costUsd <= 0m) {
            return "--";
        }

        string value;
        if (costUsd >= 1_000_000m) {
            value = (costUsd / 1_000_000m).ToString("0.0", CultureInfo.InvariantCulture) + "M";
        } else if (costUsd >= 1_000m) {
            value = (costUsd / 1_000m).ToString("0.0", CultureInfo.InvariantCulture) + "K";
        } else if (costUsd >= 1m) {
            value = costUsd.ToString("0.##", CultureInfo.InvariantCulture);
        } else {
            value = costUsd.ToString("0.####", CultureInfo.InvariantCulture);
        }

        return (approximate ? "~$" : "$") + value;
    }

    private static Brush FrozenBrush(Color color) {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private enum FilterDimension {
        Model,
        Surface,
        Account
    }
}
