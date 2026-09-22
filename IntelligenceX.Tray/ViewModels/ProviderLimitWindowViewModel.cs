using System.Windows.Media;
using System;
using System.Globalization;

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

    /// <summary>Capacity is shown only when the provider reports a finite usage percentage.</summary>
    public bool HasUsage => UsedPercent.HasValue && UsedPercent.Value >= 0d && !double.IsNaN(UsedPercent.Value) && !double.IsInfinity(UsedPercent.Value);
    public double RemainingPercent => HasUsage ? 100d - Math.Clamp(UsedPercent!.Value, 0d, 100d) : 0d;
    public string RemainingText => HasUsage ? RemainingPercent.ToString("0.#", CultureInfo.CurrentCulture) + "% left" : "Not reported";
    public string CompactLabel => Label.StartsWith("Global ", StringComparison.Ordinal) ? Label.Substring(7) : Label;
    public bool IsCapacityLow => HasUsage && RemainingPercent <= 20d;
}
