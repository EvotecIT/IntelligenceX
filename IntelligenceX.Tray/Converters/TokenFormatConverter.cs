using System.Globalization;
using System.Windows.Data;

namespace IntelligenceX.Tray.Converters;

/// <summary>
/// Formats a long token count into a human-readable string with K/M/B suffixes.
/// </summary>
public sealed class TokenFormatConverter : IValueConverter {
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
        if (value is long tokens) {
            return tokens switch {
                >= 1_000_000_000L => $"{tokens / 1_000_000_000.0:F1}B",
                >= 1_000_000L => $"{tokens / 1_000_000.0:F1}M",
                >= 1_000L => $"{tokens / 1_000.0:F1}K",
                _ => tokens.ToString("N0")
            };
        }

        return value?.ToString() ?? "0";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
        throw new NotSupportedException();
    }
}

/// <summary>
/// Converts a boolean to a Visibility. True = Visible, False = Collapsed.
/// </summary>
public sealed class BoolToVisibilityConverter : IValueConverter {
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
        return value is true
            ? System.Windows.Visibility.Visible
            : System.Windows.Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
        throw new NotSupportedException();
    }
}

/// <summary>
/// Multiplies a proportion (0..1) by a max width to produce a bar width.
/// </summary>
public sealed class ProportionToWidthConverter : IValueConverter {
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
        if (value is double proportion && parameter is string maxStr && double.TryParse(maxStr, out var max)) {
            return Math.Max(2, proportion * max);
        }

        return 2.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) {
        throw new NotSupportedException();
    }
}
