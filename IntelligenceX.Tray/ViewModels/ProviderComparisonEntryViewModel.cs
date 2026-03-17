using System.Windows.Media;

namespace IntelligenceX.Tray.ViewModels;

public sealed class ProviderComparisonEntryViewModel {
    public string ProviderId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string ShortName { get; set; } = string.Empty;
    public string TokensText { get; set; } = string.Empty;
    public string CostText { get; set; } = string.Empty;
    public string EventCountText { get; set; } = string.Empty;
    public string HealthText { get; set; } = string.Empty;
    public Brush HealthBrush { get; set; } = Brushes.White;
    public string DeltaText { get; set; } = string.Empty;
    public Brush DeltaBrush { get; set; } = Brushes.White;
    public string HistoryText { get; set; } = string.Empty;
    public Brush HistoryBrush { get; set; } = Brushes.White;
    public bool IsFavorite { get; set; }
    public string FavoriteActionText => IsFavorite ? "Pinned" : "Pin";
    public double Proportion { get; set; }
    public Brush BarBrush { get; set; } = Brushes.White;
}
