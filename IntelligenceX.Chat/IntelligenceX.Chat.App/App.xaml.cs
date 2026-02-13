using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace IntelligenceX.Chat.App;

/// <summary>
/// WinUI 3 application root (code-only; no XAML).
/// </summary>
public sealed class App : Application {
    private Window? _window;

    /// <summary>
    /// Initializes the app.
    /// </summary>
    public App() {
        StartupLog.Write("App.ctor");
    }

    /// <inheritdoc />
    protected override void OnLaunched(LaunchActivatedEventArgs args) {
        StartupLog.Write("App.OnLaunched enter");
        if (IsTruthy(Environment.GetEnvironmentVariable("IXCHAT_MIN_WINDOW"))) {
            _window = new Window {
                Title = "IntelligenceX Chat (Min Window)",
                Content = new TextBlock {
                    Text = "Minimal window mode",
                    Margin = new Thickness(24)
                }
            };
            StartupLog.Write("Min window constructed");
        } else if (IsTruthy(Environment.GetEnvironmentVariable("IXCHAT_WEBVIEW_SMOKE"))) {
            _window = new WebViewSmokeWindow();
            StartupLog.Write("WebView smoke window constructed");
        } else {
            _window = new MainWindow();
            StartupLog.Write("MainWindow constructed");
        }

        _window.Activate();
        StartupLog.Write("Window activated");
    }

    private static bool IsTruthy(string? value) {
        if (string.IsNullOrWhiteSpace(value)) {
            return false;
        }

        var v = value.Trim();
        return string.Equals(v, "1", StringComparison.OrdinalIgnoreCase)
               || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase)
               || string.Equals(v, "yes", StringComparison.OrdinalIgnoreCase)
               || string.Equals(v, "on", StringComparison.OrdinalIgnoreCase);
    }
}
