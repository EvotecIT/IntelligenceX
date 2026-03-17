using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Media;
using IntelligenceX.Telemetry.Limits;
using IntelligenceX.Tray.Services;

namespace IntelligenceX.Tray.ViewModels;

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
    private int _todayEventCount;

    // 7-day rolling
    private long _weeklyTotalTokens;
    private long _weeklyAvgPerDay;
    private decimal _weeklyCostUsd;

    // 30-day rolling
    private long _monthlyTotalTokens;
    private long _monthlyAvgPerDay;
    private decimal _monthlyCostUsd;

    private DateTimeOffset _lastUpdated;
    private string? _limitPlanLabel;
    private string? _limitAccountLabel;
    private string? _limitSummary;
    private string? _limitSourceLabel;
    private string? _limitStatusMessage;

    public ProviderViewModel() {
        LimitWindows.CollectionChanged += (_, _) => {
            OnPropertyChanged(nameof(HasLiveLimitData));
            OnPropertyChanged(nameof(HasLimitSection));
        };
    }

    public string ProviderId {
        get => _providerId;
        set => SetProperty(ref _providerId, value);
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
            } else {
                IconGeometry = null;
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

    public int SortOrder {
        get => _sortOrder;
        set => SetProperty(ref _sortOrder, value);
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

    public string TodayCostFormatted => TodayCostUsd > 0 ? $"${TodayCostUsd:F4}" : "--";
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

    public string WeeklyCostFormatted => WeeklyCostUsd > 0 ? $"${WeeklyCostUsd:F2}" : "--";
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

    public string MonthlyCostFormatted => MonthlyCostUsd > 0 ? $"${MonthlyCostUsd:F2}" : "--";
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

    public bool HasLimitSummary => !string.IsNullOrWhiteSpace(LimitSummary);
    public bool HasLimitStatusMessage => !string.IsNullOrWhiteSpace(LimitStatusMessage);
    public bool HasLiveLimitData => LimitWindows.Count > 0;
    public bool HasLimitSection =>
        HasLiveLimitData
        || HasLimitStatusMessage
        || !string.IsNullOrWhiteSpace(LimitPlanLabel)
        || !string.IsNullOrWhiteSpace(LimitAccountLabel)
        || HasLimitSummary
        || !string.IsNullOrWhiteSpace(LimitSourceLabel);

    public ObservableCollection<ModelUsageViewModel> ModelBreakdown { get; } = [];
    public ObservableCollection<DailyBarViewModel> DailyBars { get; } = [];
    public ObservableCollection<ProviderLimitWindowViewModel> LimitWindows { get; } = [];

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
        if (snapshot is null) {
            LimitPlanLabel = null;
            LimitAccountLabel = null;
            LimitSummary = null;
            LimitSourceLabel = null;
            LimitStatusMessage = null;
            return;
        }

        LimitPlanLabel = snapshot.PlanLabel;
        LimitAccountLabel = snapshot.AccountLabel;
        LimitSummary = snapshot.Summary;
        LimitSourceLabel = snapshot.SourceLabel;
        LimitStatusMessage = snapshot.DetailMessage;

        foreach (var window in snapshot.Windows) {
            LimitWindows.Add(new ProviderLimitWindowViewModel {
                Label = window.Label,
                UsedPercent = window.UsedPercent,
                UsedPercentFormatted = window.UsedPercent.HasValue
                    ? window.UsedPercent.Value.ToString("0.#", CultureInfo.InvariantCulture) + "%"
                    : "--",
                ResetText = FormatResetText(window.ResetsAt),
                Detail = window.Detail,
                Proportion = window.UsedPercent.HasValue
                    ? Math.Min(1d, Math.Max(0d, window.UsedPercent.Value / 100d))
                    : 0d,
                BarBrush = FrozenBrush(OutputColor)
            });
        }
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

    private static Brush FrozenBrush(Color color) {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
