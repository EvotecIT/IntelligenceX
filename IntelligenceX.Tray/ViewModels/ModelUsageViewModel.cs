using System.Windows.Media;

namespace IntelligenceX.Tray.ViewModels;

/// <summary>
/// Represents a single model's usage within a provider.
/// </summary>
public sealed class ModelUsageViewModel : ViewModelBase {
    private string _modelName = string.Empty;
    private long _totalTokens;
    private double _proportion;
    private Brush _barBrush = Brushes.Gray;

    public string ModelName {
        get => _modelName;
        set => SetProperty(ref _modelName, value);
    }

    public long TotalTokens {
        get => _totalTokens;
        set {
            if (SetProperty(ref _totalTokens, value)) {
                OnPropertyChanged(nameof(TotalTokensFormatted));
            }
        }
    }

    public string TotalTokensFormatted => FormatTokens(TotalTokens);

    public double Proportion {
        get => _proportion;
        set => SetProperty(ref _proportion, value);
    }

    public Brush BarBrush {
        get => _barBrush;
        set => SetProperty(ref _barBrush, value);
    }

    private static string FormatTokens(long tokens) {
        return tokens switch {
            >= 1_000_000_000L => $"{tokens / 1_000_000_000.0:F1}B",
            >= 1_000_000L => $"{tokens / 1_000_000.0:F1}M",
            >= 1_000L => $"{tokens / 1_000.0:F1}K",
            _ => tokens.ToString("N0")
        };
    }
}
