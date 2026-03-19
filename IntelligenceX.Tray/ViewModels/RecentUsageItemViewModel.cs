namespace IntelligenceX.Tray.ViewModels;

public sealed class RecentUsageItemViewModel {
    public string EventKey { get; set; } = string.Empty;
    public string TimestampText { get; set; } = string.Empty;
    public string TimestampLocalText { get; set; } = string.Empty;
    public string TimestampUtcText { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ModelText { get; set; } = string.Empty;
    public string SurfaceText { get; set; } = string.Empty;
    public string AccountText { get; set; } = string.Empty;
    public string TokensText { get; set; } = string.Empty;
    public string CostText { get; set; } = string.Empty;
    public string InputText { get; set; } = string.Empty;
    public string OutputText { get; set; } = string.Empty;
    public string CachedText { get; set; } = string.Empty;
    public string ReasoningText { get; set; } = string.Empty;
    public string MetricText { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public bool HasCost => !string.Equals(CostText, "--", StringComparison.Ordinal);
}
