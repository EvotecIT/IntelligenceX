using IntelligenceX.Json;

namespace IntelligenceX.Visualization.Heatmaps;

internal static class UsageTelemetryReportAppearanceDefaults {
    public const string ThemeStorageKey = "ix-usage-report-theme";
    public const string DefaultTheme = "system";
    public const string AccentStorageKey = "ix-usage-report-accent";
    public const string DefaultAccent = "violet";

    public static JsonObject AddBootstrap(JsonObject bootstrap) {
        return bootstrap
            .Add("themeKey", ThemeStorageKey)
            .Add("defaultTheme", DefaultTheme)
            .Add("accentKey", AccentStorageKey)
            .Add("defaultAccent", DefaultAccent);
    }

    public static string BuildInitialAppearanceScript() {
        return $@"<script>
  (function () {{
    var themeKey = '{ThemeStorageKey}';
    var defaultTheme = '{DefaultTheme}';
    var accentKey = '{AccentStorageKey}';
    var defaultAccent = '{DefaultAccent}';
    var root = document.documentElement;
    var theme = defaultTheme;
    var accent = defaultAccent;

    try {{
      theme = localStorage.getItem(themeKey) || defaultTheme;
    }} catch (_) {{
      theme = defaultTheme;
    }}

    try {{
      accent = localStorage.getItem(accentKey) || defaultAccent;
    }} catch (_) {{
      accent = defaultAccent;
    }}

    var resolvedTheme = theme === 'light' || theme === 'dark'
      ? theme
      : (window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light');

    root.setAttribute('data-theme', resolvedTheme);
    root.setAttribute('data-accent', accent);

    var palettes = {{
      violet: {{
        dark: {{
          accent: '#9da9ff',
          accentStrong: '#6268f1',
          toneCurrentBg: 'rgba(129,140,248,.18)',
          toneCurrentBorder: 'rgba(129,140,248,.28)',
          toneCurrentInk: '#c7d2fe'
        }},
        light: {{
          accent: '#6268f1',
          accentStrong: '#4740d1',
          toneCurrentBg: 'rgba(98,104,241,.12)',
          toneCurrentBorder: 'rgba(98,104,241,.2)',
          toneCurrentInk: '#4740d1'
        }}
      }},
      ocean: {{
        dark: {{
          accent: '#72b7ff',
          accentStrong: '#2f86ff',
          toneCurrentBg: 'rgba(47,134,255,.18)',
          toneCurrentBorder: 'rgba(47,134,255,.28)',
          toneCurrentInk: '#c9e3ff'
        }},
        light: {{
          accent: '#4b9bff',
          accentStrong: '#1d62d8',
          toneCurrentBg: 'rgba(47,134,255,.12)',
          toneCurrentBorder: 'rgba(47,134,255,.22)',
          toneCurrentInk: '#1d62d8'
        }}
      }},
      forest: {{
        dark: {{
          accent: '#63c78f',
          accentStrong: '#2aa06d',
          toneCurrentBg: 'rgba(42,160,109,.18)',
          toneCurrentBorder: 'rgba(42,160,109,.28)',
          toneCurrentInk: '#caf6de'
        }},
        light: {{
          accent: '#46b57d',
          accentStrong: '#1f7f56',
          toneCurrentBg: 'rgba(42,160,109,.12)',
          toneCurrentBorder: 'rgba(42,160,109,.22)',
          toneCurrentInk: '#1f7f56'
        }}
      }},
      sunset: {{
        dark: {{
          accent: '#f2a06e',
          accentStrong: '#dc6a3c',
          toneCurrentBg: 'rgba(220,106,60,.18)',
          toneCurrentBorder: 'rgba(220,106,60,.28)',
          toneCurrentInk: '#ffd9c6'
        }},
        light: {{
          accent: '#ee8554',
          accentStrong: '#b44a1f',
          toneCurrentBg: 'rgba(220,106,60,.12)',
          toneCurrentBorder: 'rgba(220,106,60,.22)',
          toneCurrentInk: '#b44a1f'
        }}
      }}
    }};

    if (!Object.prototype.hasOwnProperty.call(palettes, accent)) {{
      accent = defaultAccent;
    }}

    var paletteGroup = palettes[accent] || palettes[defaultAccent];
    var palette = resolvedTheme === 'dark' ? paletteGroup.dark : paletteGroup.light;
    root.style.setProperty('--accent', palette.accent);
    root.style.setProperty('--accent-strong', palette.accentStrong);
    root.style.setProperty('--tone-current-bg', palette.toneCurrentBg);
    root.style.setProperty('--tone-current-border', palette.toneCurrentBorder);
    root.style.setProperty('--tone-current-ink', palette.toneCurrentInk);
  }}());
</script>";
    }
}
