using System.Windows.Media;

namespace IntelligenceX.Tray.ViewModels;

public sealed class ProviderComparisonDeltaInfo {
    public string SummaryText { get; set; } = string.Empty;
    public Brush SummaryBrush { get; set; } = Brushes.White;
    public long TokenDelta { get; set; }
    public int EventDelta { get; set; }
}
