using System.IO;
using System.Text.Json;

namespace IntelligenceX.Tray.Services;

public sealed class TrayPreferencesStore {
    private static readonly JsonSerializerOptions SerializerOptions = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public string PreferencesPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IntelligenceX",
        "Tray",
        "tray-settings.json");

    public TrayPreferences Load() {
        try {
            if (!File.Exists(PreferencesPath)) {
                return new TrayPreferences();
            }

            var json = File.ReadAllText(PreferencesPath);
            var preferences = JsonSerializer.Deserialize<TrayPreferences>(json, SerializerOptions) ?? new TrayPreferences();
            preferences.Providers ??= new Dictionary<string, ProviderExplorerPreferences>(StringComparer.OrdinalIgnoreCase);
            preferences.ThemeMode = TrayThemeService.NormalizeThemeMode(preferences.ThemeMode);
            preferences.AccentPreset = TrayThemeService.NormalizeAccentPreset(preferences.AccentPreset);
            return preferences;
        } catch {
            return new TrayPreferences();
        }
    }

    public void Save(TrayPreferences preferences) {
        ArgumentNullException.ThrowIfNull(preferences);

        var directory = Path.GetDirectoryName(PreferencesPath);
        if (!string.IsNullOrWhiteSpace(directory)) {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(preferences, SerializerOptions);
        File.WriteAllText(PreferencesPath, json);
    }
}
