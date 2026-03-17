using System.Windows.Media;

namespace IntelligenceX.Tray.ViewModels;

public sealed class ProviderLimitWindowViewModel {
    public string Label { get; set; } = string.Empty;
    public double? UsedPercent { get; set; }
    public string UsedPercentFormatted { get; set; } = "--";
    public string ResetText { get; set; } = "Reset unknown";
    public string? Detail { get; set; }
    public double Proportion { get; set; }
    public Brush BarBrush { get; set; } = Brushes.White;

    public bool HasDetail => !string.IsNullOrWhiteSpace(Detail);
}
