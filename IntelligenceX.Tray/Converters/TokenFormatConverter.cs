using System.Globalization;
using System.Windows.Data;

namespace IntelligenceX.Tray.Converters;

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

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class BoolToVisibilityConverter : IValueConverter {
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class InverseBoolToVisibilityConverter : IValueConverter {
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class ProportionToWidthConverter : IValueConverter {
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) {
        if (value is double proportion && parameter is string maxStr && double.TryParse(maxStr, out var max)) {
            return Math.Max(2, proportion * max);
        }
        return 2.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class ProportionAndWidthToWidthConverter : IMultiValueConverter {
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) {
        if (values.Length < 2
            || values[0] is not double proportion
            || values[1] is not double availableWidth
            || double.IsNaN(availableWidth)
            || double.IsInfinity(availableWidth)
            || availableWidth <= 0d) {
            return 2.0;
        }

        proportion = Math.Max(0d, Math.Min(1d, proportion));
        return Math.Max(2d, proportion * availableWidth);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
