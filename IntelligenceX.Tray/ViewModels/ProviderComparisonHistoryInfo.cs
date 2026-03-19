using System.Windows.Media;

namespace IntelligenceX.Tray.ViewModels;

public sealed class ProviderComparisonHistoryInfo {
    public string SummaryText { get; set; } = string.Empty;
    public Brush SummaryBrush { get; set; } = Brushes.White;
}
