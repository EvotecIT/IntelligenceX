using System.Windows.Media;

namespace IntelligenceX.Tray.ViewModels;

public sealed class CodeChurnBarViewModel : ViewModelBase {
    public DateTime DayUtc { get; set; }
    public string DayLabel { get; set; } = string.Empty;
    public string SummaryText { get; set; } = string.Empty;
    public double BarHeight { get; set; }
    public Brush BarBrush { get; set; } = Brushes.Gray;
}
