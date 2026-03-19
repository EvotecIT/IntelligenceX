using Microsoft.Win32;
using System.Windows;
using System.Windows.Media;

namespace IntelligenceX.Tray.Services;

public sealed class TrayThemeService : IDisposable {
    public const string SystemMode = "system";
    public const string DarkMode = "dark";
    public const string LightMode = "light";

    public const string DefaultAccentPreset = "violet";
    public const string OceanAccentPreset = "ocean";
    public const string ForestAccentPreset = "forest";
    public const string SunsetAccentPreset = "sunset";

    private static readonly Uri DarkThemeUri = new("Themes/DarkTheme.xaml", UriKind.Relative);
    private static readonly Uri LightThemeUri = new("Themes/LightTheme.xaml", UriKind.Relative);
    private readonly Application _application;

    public TrayThemeService(Application application) {
        _application = application ?? throw new ArgumentNullException(nameof(application));
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    public event EventHandler? ThemeChanged;

    public string RequestedMode { get; private set; } = SystemMode;
    public string EffectiveMode { get; private set; } = DarkMode;
    public string RequestedAccentPreset { get; private set; } = DefaultAccentPreset;
    public string EffectiveAccentPreset { get; private set; } = DefaultAccentPreset;

    public void ApplyAppearance(string? requestedMode, string? accentPreset) {
        RequestedMode = NormalizeThemeMode(requestedMode);
        RequestedAccentPreset = NormalizeAccentPreset(accentPreset);

        var effectiveMode = ResolveEffectiveThemeMode(RequestedMode);
        var effectiveAccentPreset = RequestedAccentPreset;

        ReplaceThemeDictionary(effectiveMode);
        ApplyAccentResources(effectiveAccentPreset);

        var changed = !string.Equals(EffectiveMode, effectiveMode, StringComparison.Ordinal)
                      || !string.Equals(EffectiveAccentPreset, effectiveAccentPreset, StringComparison.Ordinal);

        EffectiveMode = effectiveMode;
        EffectiveAccentPreset = effectiveAccentPreset;
        if (changed) {
            ThemeChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void ApplyTheme(string? requestedMode) {
        ApplyAppearance(requestedMode, RequestedAccentPreset);
    }

    public void ApplyAccentPreset(string? accentPreset) {
        ApplyAppearance(RequestedMode, accentPreset);
    }

    public static string NormalizeThemeMode(string? mode) {
        return mode?.Trim().ToLowerInvariant() switch {
            LightMode => LightMode,
            DarkMode => DarkMode,
            _ => SystemMode
        };
    }

    public static string NormalizeAccentPreset(string? accentPreset) {
        return accentPreset?.Trim().ToLowerInvariant() switch {
            OceanAccentPreset => OceanAccentPreset,
            ForestAccentPreset => ForestAccentPreset,
            SunsetAccentPreset => SunsetAccentPreset,
            _ => DefaultAccentPreset
        };
    }

    public static string ResolveEffectiveThemeMode(string? requestedMode) {
        var normalizedMode = NormalizeThemeMode(requestedMode);
        return normalizedMode switch {
            LightMode => LightMode,
            DarkMode => DarkMode,
            _ => IsSystemLightTheme() ? LightMode : DarkMode
        };
    }

    public static string GetDisplayName(string? mode) {
        return NormalizeThemeMode(mode) switch {
            LightMode => "Light",
            DarkMode => "Dark",
            _ => "Auto"
        };
    }

    public static string GetAccentDisplayName(string? accentPreset) {
        return NormalizeAccentPreset(accentPreset) switch {
            OceanAccentPreset => "Ocean",
            ForestAccentPreset => "Forest",
            SunsetAccentPreset => "Sunset",
            _ => "Violet"
        };
    }

    public void Dispose() {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e) {
        if (!string.Equals(RequestedMode, SystemMode, StringComparison.Ordinal) ||
            e.Category is not UserPreferenceCategory.General and not UserPreferenceCategory.Color) {
            return;
        }

        var effectiveMode = ResolveEffectiveThemeMode(RequestedMode);
        if (string.Equals(EffectiveMode, effectiveMode, StringComparison.Ordinal)) {
            return;
        }

        ReplaceThemeDictionary(effectiveMode);
        ApplyAccentResources(EffectiveAccentPreset);
        EffectiveMode = effectiveMode;
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ReplaceThemeDictionary(string effectiveMode) {
        var dictionaries = _application.Resources.MergedDictionaries;
        for (var index = dictionaries.Count - 1; index >= 0; index--) {
            var originalString = dictionaries[index].Source?.OriginalString;
            if (string.Equals(originalString, DarkThemeUri.OriginalString, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(originalString, LightThemeUri.OriginalString, StringComparison.OrdinalIgnoreCase)) {
                dictionaries.RemoveAt(index);
            }
        }

        var themeDictionary = new ResourceDictionary {
            Source = string.Equals(effectiveMode, LightMode, StringComparison.Ordinal)
                ? LightThemeUri
                : DarkThemeUri
        };

        dictionaries.Insert(0, themeDictionary);
    }

    private void ApplyAccentResources(string accentPreset) {
        var palette = GetAccentPalette(accentPreset);
        ApplyColorResource("AccentColor", palette.AccentColor);
        ApplyColorResource("AccentDimColor", palette.AccentDimColor);
        ApplyBrushResource("AccentBrush", palette.AccentColor);
        ApplyBrushResource("AccentDimBrush", palette.AccentDimColor);
        ApplyColorResource("BrandStartColor", palette.BrandStartColor);
        ApplyColorResource("BrandEndColor", palette.BrandEndColor);
    }

    private void ApplyColorResource(string key, Color color) {
        _application.Resources[key] = color;
    }

    private void ApplyBrushResource(string key, Color color) {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        _application.Resources[key] = brush;
    }

    private static AccentPalette GetAccentPalette(string accentPreset) {
        return NormalizeAccentPreset(accentPreset) switch {
            OceanAccentPreset => new AccentPalette(
                AccentColor: ParseColor("#2f86ff"),
                AccentDimColor: ParseColor("#1d62d8"),
                BrandStartColor: ParseColor("#2f86ff"),
                BrandEndColor: ParseColor("#27c7b8")),
            ForestAccentPreset => new AccentPalette(
                AccentColor: ParseColor("#2aa06d"),
                AccentDimColor: ParseColor("#1f7f56"),
                BrandStartColor: ParseColor("#2aa06d"),
                BrandEndColor: ParseColor("#84c441")),
            SunsetAccentPreset => new AccentPalette(
                AccentColor: ParseColor("#dc6a3c"),
                AccentDimColor: ParseColor("#b44a1f"),
                BrandStartColor: ParseColor("#dc6a3c"),
                BrandEndColor: ParseColor("#f0b24a")),
            _ => new AccentPalette(
                AccentColor: ParseColor("#7c6ff5"),
                AccentDimColor: ParseColor("#5a50d0"),
                BrandStartColor: ParseColor("#7c6ff5"),
                BrandEndColor: ParseColor("#50d880"))
        };
    }

    private static Color ParseColor(string value) {
        return (Color)ColorConverter.ConvertFromString(value)!;
    }

    private static bool IsSystemLightTheme() {
        const string personalizeKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        try {
            using var personalizeKey = Registry.CurrentUser.OpenSubKey(personalizeKeyPath);
            if (personalizeKey?.GetValue("AppsUseLightTheme") is int lightThemeValue) {
                return lightThemeValue > 0;
            }
        } catch {
            // Fall back to dark if the theme registry cannot be read.
        }

        return false;
    }

    private sealed record AccentPalette(
        Color AccentColor,
        Color AccentDimColor,
        Color BrandStartColor,
        Color BrandEndColor);
}
