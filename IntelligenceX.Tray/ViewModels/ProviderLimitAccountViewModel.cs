using System.Collections.ObjectModel;

namespace IntelligenceX.Tray.ViewModels;

public sealed class ProviderLimitAccountViewModel : ViewModelBase {
    private bool _isExpanded;

    public ProviderLimitAccountViewModel() {
        Windows.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasWindows));
    }

    public string Label { get; set; } = string.Empty;
    public string? PlanLabel { get; set; }
    public string? StatusLabel { get; set; }
    public string Summary { get; set; } = string.Empty;
    public string? BadgeText { get; set; }
    public string? WindowSummaryText { get; set; }
    public ObservableCollection<ProviderLimitWindowViewModel> Windows { get; } = [];

    public bool IsExpanded {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public bool HasPlanLabel => !string.IsNullOrWhiteSpace(PlanLabel);
    public bool HasStatusLabel => !string.IsNullOrWhiteSpace(StatusLabel);
    public bool HasBadge => !string.IsNullOrWhiteSpace(BadgeText);
    public bool HasWindowSummary => !string.IsNullOrWhiteSpace(WindowSummaryText);
    public bool HasWindows => Windows.Count > 0;
}
