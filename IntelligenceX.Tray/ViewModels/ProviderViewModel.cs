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
    private string _todayLabel = "Today";
    private string _weeklyLabel = "7 days";
    private string _monthlyLabel = "30 days";
    private readonly List<UsageEventRecord> _usageEvents = [];
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
        RecentActivity.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasRecentActivity));
        ProviderComparison.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasProviderComparison));
    }

    public string ProviderId {
        get => _providerId;
        set {
            if (SetProperty(ref _providerId, value)) {
                OnPropertyChanged(nameof(IsCombinedProvider));
                OnPropertyChanged(nameof(HasProviderComparison));
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

            var summary = "+" + NewEventsSinceRefresh.ToString("N0", CultureInfo.CurrentCulture) + " new events";
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
        set => SetProperty(ref _todayEventCount, value);
    }

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

    public bool HasLimitSummary => !string.IsNullOrWhiteSpace(LimitSummary);
    public bool HasLimitStatusMessage => !string.IsNullOrWhiteSpace(LimitStatusMessage);
    public bool HasRecommendedLimitAccount => !string.IsNullOrWhiteSpace(RecommendedLimitAccountLabel);
    public bool HasLiveLimitData => LimitWindows.Count > 0;
    public bool HasLimitAccounts => LimitAccounts.Count > 0;
    public bool HasMultipleLimitAccounts => LimitAccounts.Count > 1;
    public bool ShowSharedLimitWindows => HasLiveLimitData && !HasMultipleLimitAccounts;
    public bool HasAccountBreakdown => AccountBreakdown.Count > 0;
    public bool HasSurfaceBreakdown => SurfaceBreakdown.Count > 0;
    public bool HasRecentActivity => RecentActivity.Count > 0;
    public bool IsCombinedProvider => string.Equals(ProviderId, "__all__", StringComparison.Ordinal);
    public bool HasProviderComparison => IsCombinedProvider && ProviderComparison.Count > 0;
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
    public ObservableCollection<DailyBarViewModel> DailyBars { get; } = [];
    public ObservableCollection<ProviderLimitWindowViewModel> LimitWindows { get; } = [];
    public ObservableCollection<ProviderLimitAccountViewModel> LimitAccounts { get; } = [];
    public ObservableCollection<UsageBreakdownEntryViewModel> AccountBreakdown { get; } = [];
    public ObservableCollection<UsageBreakdownEntryViewModel> SurfaceBreakdown { get; } = [];
    public ObservableCollection<ProviderComparisonEntryViewModel> ProviderComparison { get; } = [];
    public ObservableCollection<RecentUsageItemViewModel> RecentActivity { get; } = [];
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

    public string FilteredEventCountLabel => FilteredEventCount.ToString("N0", CultureInfo.CurrentCulture) + " matching events";

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
        RebuildFilterOptions();
        RebuildStaticWindows();
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
            return;
        }

        LimitPlanLabel = snapshot.PlanLabel;
        LimitAccountLabel = snapshot.AccountLabel;
        LimitSummary = snapshot.Summary;
        LimitSourceLabel = snapshot.SourceLabel;
        LimitStatusMessage = snapshot.DetailMessage;
        RecommendedLimitAccountLabel = null;
        RecommendedLimitAccountSummary = null;
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
                WindowSummaryText = accountSnapshot is { Windows.Count: > 0 }
                    ? accountSnapshot.Windows.Count.ToString(CultureInfo.InvariantCulture) + " tracked windows"
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

        if (snapshot.Accounts.Count <= 1) {
            PopulateLimitWindows(LimitWindows, snapshot.Windows, forecasts);
        }
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
        PopulateUsageBreakdown(
            AccountBreakdown,
            BuildBreakdown(rangeEvents, e => NormalizeAccountLabel(e.AccountLabel, e.ProviderAccountId)),
            OutputColor);
        PopulateUsageBreakdown(
            SurfaceBreakdown,
            BuildBreakdown(rangeEvents, e => NormalizeSurfaceLabel(e.Surface)),
            InputColor);
        PopulateProviderComparison(rangeEvents);
        PopulateRecentActivity(rangeEvents);

        ActionStatusMessage = string.Empty;
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

    private List<UsageEventRecord> FilterByWindow(DateTime startDay, DateTime endDay) {
        return _usageEvents
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
                EventCountText = group.Events.ToString("N0", CultureInfo.CurrentCulture) + " events",
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
            ActionStatusMessage = "Select an event first.";
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
            ActionStatusMessage = "The selected event does not include a usable " + dimensionLabel + ".";
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
                $"Events: {TodayEventCount}",
                $"Tokens: {TodayTotalTokensFormatted}",
                $"Input: {TodayInputTokensFormatted}",
                $"Output: {TodayOutputTokensFormatted}",
                $"Cached: {TodayCachedTokensFormatted}",
                $"Reasoning: {TodayReasoningTokensFormatted}",
                $"Cost: {TodayCostFormatted}"
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

            Clipboard.SetText(string.Join(Environment.NewLine, lines));
            ActionStatusMessage = "Copied selected range summary to clipboard.";
        } catch (Exception ex) {
            ActionStatusMessage = "Copy failed: " + ex.Message;
        }

        return Task.CompletedTask;
    }

    private Task CopySelectedEventAsync() {
        if (SelectedEvent is null) {
            ActionStatusMessage = "Select an event first.";
            return Task.CompletedTask;
        }

        try {
            var lines = new List<string> {
                $"{DisplayName} event details",
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
                $"Cost: {SelectedEvent.CostText}"
            };

            Clipboard.SetText(string.Join(Environment.NewLine, lines));
            ActionStatusMessage = "Copied selected event details to clipboard.";
        } catch (Exception ex) {
            ActionStatusMessage = "Copy failed: " + ex.Message;
        }

        return Task.CompletedTask;
    }

    private Task CopySelectedEventJsonAsync() {
        if (SelectedEvent is null) {
            ActionStatusMessage = "Select an event first.";
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
                cost = SelectedEvent.CostText
            };

            Clipboard.SetText(JsonSerializer.Serialize(payload, new JsonSerializerOptions {
                WriteIndented = true
            }));
            ActionStatusMessage = "Copied selected event JSON to clipboard.";
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
                    events = TodayEventCount,
                    totalTokens = TodayTotalTokens,
                    inputTokens = TodayInputTokens,
                    outputTokens = TodayOutputTokens,
                    cachedTokens = TodayCachedTokens,
                    reasoningTokens = TodayReasoningTokens,
                    costUsd = TodayCostUsd,
                    costApproximate = TodayCostUsesEstimate
                },
                events = events.Select(e => {
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
                        costUsd = displayCost.TotalCostUsd,
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
            builder.AppendLine("timestamp_local,timestamp_utc,account,model,surface,input_tokens,output_tokens,cached_tokens,reasoning_tokens,total_tokens,cost_usd");
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
                ActionStatusMessage = "No events are available for the current range and filters.";
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
                    Subtitle = "Tray explorer: " + TodayLabel + " | " + BuildFilterSummary()
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

        return parts.Count == 0 ? "No token or cost data" : string.Join(" • ", parts);
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

    private static string? NormalizeOptional(string? value) {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
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

        return (displayCost.UsesEstimatedFallback ? "~" : string.Empty)
               + displayCost.TotalCostUsd.ToString("0.####", CultureInfo.InvariantCulture);
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
