using System.Windows.Media;

namespace IntelligenceX.Tray.ViewModels;

public sealed class DailyBarViewModel : ViewModelBase {
    public DateTime DayUtc { get; set; }
    public string DayLabel { get; set; } = "";
    public long TotalTokens { get; set; }
    public double BarHeight { get; set; }
    public Brush BarBrush { get; set; } = Brushes.Gray;

    public string TokensFormatted => TotalTokens switch {
        >= 1_000_000_000L => $"{TotalTokens / 1_000_000_000.0:F1}B",
        >= 1_000_000L => $"{TotalTokens / 1_000_000.0:F1}M",
        >= 1_000L => $"{TotalTokens / 1_000.0:F1}K",
        0 => "-",
        _ => TotalTokens.ToString("N0")
    };
}
