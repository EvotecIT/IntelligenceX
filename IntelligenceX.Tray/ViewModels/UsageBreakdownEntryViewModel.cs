using System.Windows.Media;

namespace IntelligenceX.Tray.ViewModels;

public sealed class UsageBreakdownEntryViewModel {
    public string Label { get; set; } = string.Empty;
    public string ValueText { get; set; } = string.Empty;
    public double Proportion { get; set; }
    public Brush BarBrush { get; set; } = Brushes.Gray;
}
