using System.Collections.ObjectModel;
using System.Windows.Media;
using IntelligenceX.Tray.Services;

namespace IntelligenceX.Tray.ViewModels;

/// <summary>
/// Holds aggregated usage data for a single provider or the combined "All" view.
/// </summary>
public sealed class ProviderViewModel : ViewModelBase {
    private string _providerId = string.Empty;
    private string _displayName = string.Empty;
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

    public string ProviderId {
        get => _providerId;
        set => SetProperty(ref _providerId, value);
    }

    public string DisplayName {
        get => _displayName;
        set => SetProperty(ref _displayName, value);
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
    public long TodayTotalTokens {
        get => _todayTotalTokens;
        set {
            if (SetProperty(ref _todayTotalTokens, value)) {
                OnPropertyChanged(nameof(TodayTotalTokensFormatted));
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

    public ObservableCollection<ModelUsageViewModel> ModelBreakdown { get; } = [];

    public void ApplyProviderInfo(ProviderInfo info) {
        ProviderId = info.Id;
        DisplayName = info.DisplayName;
        SortOrder = info.SortOrder;
        InputColor = info.InputColor;
        OutputColor = info.OutputColor;
        TotalColor = info.TotalColor;
        AccentBrush = new SolidColorBrush(info.TotalColor);
    }

    private static string FormatTokens(long tokens) {
        return tokens switch {
            >= 1_000_000_000L => $"{tokens / 1_000_000_000.0:F1}B",
            >= 1_000_000L => $"{tokens / 1_000_000.0:F1}M",
            >= 1_000L => $"{tokens / 1_000.0:F1}K",
            _ => tokens.ToString("N0")
        };
    }
}
