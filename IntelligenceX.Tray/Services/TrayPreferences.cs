namespace IntelligenceX.Tray.Services;

public sealed class TrayPreferences {
    public string? SelectedProviderId { get; set; }
    public string GitHubUsername { get; set; } = string.Empty;
    public string ThemeMode { get; set; } = TrayThemeService.SystemMode;
    public string AccentPreset { get; set; } = TrayThemeService.DefaultAccentPreset;
    public int AutoRefreshIntervalSeconds { get; set; } = 120;
    public bool NotificationsEnabled { get; set; } = true;
    public List<string> FavoriteProviderIds { get; set; } = [];
    public Dictionary<string, ProviderExplorerPreferences> Providers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ProviderExplorerPreferences {
    public string? SelectedRange { get; set; }
    public string? SelectedEventSort { get; set; }
    public string? SelectedProviderComparisonSort { get; set; }
    public string? SelectedAccountFilter { get; set; }
    public string? SelectedModelFilter { get; set; }
    public string? SelectedSurfaceFilter { get; set; }
}
