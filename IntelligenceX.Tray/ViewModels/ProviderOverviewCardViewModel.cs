using System.Windows.Media;

namespace IntelligenceX.Tray.ViewModels;

public sealed class ProviderOverviewCardViewModel {
    public string Title { get; set; } = string.Empty;
    public string MetricText { get; set; } = string.Empty;
    public string DetailText { get; set; } = string.Empty;
    public Brush AccentBrush { get; set; } = Brushes.White;
}
