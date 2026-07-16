using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.XamlTypeInfo;
using IntelligenceX.Chat.App.Native;

namespace IntelligenceX.Chat.App;

/// <summary>
/// WinUI 3 application root (code-only; no XAML).
/// </summary>
public sealed class App : Application, IXamlMetadataProvider {
    private readonly XamlControlsXamlMetaDataProvider _xamlMetadataProvider = new();
    private Window? _window;
    private bool _resourcesInitialized;

    /// <summary>
    /// Initializes the app.
    /// </summary>
    public App() {
        StartupLog.Write("App.ctor");
        UnhandledException += OnUnhandledException;
    }

    /// <inheritdoc />
    public IXamlType GetXamlType(Type type) => _xamlMetadataProvider.GetXamlType(type);

    /// <inheritdoc />
    public IXamlType GetXamlType(string fullName) => _xamlMetadataProvider.GetXamlType(fullName);

    /// <inheritdoc />
    public XmlnsDefinition[] GetXmlnsDefinitions() => _xamlMetadataProvider.GetXmlnsDefinitions();

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs args) {
        StartupLog.Write("App unhandled exception: " + args.Exception);
    }

    /// <inheritdoc />
    protected override void OnLaunched(LaunchActivatedEventArgs args) {
        StartupLog.Write("App.OnLaunched enter");
        EnsureXamlResources();
        _window = CreateLaunchWindow(ChatAppLaunchModeResolver.Resolve(Environment.GetEnvironmentVariable));

        _window.Activate();
        StartupLog.Write("Window activated");
    }

    private static Window CreateLaunchWindow(ChatAppLaunchMode launchMode) =>
        launchMode switch {
            ChatAppLaunchMode.MinimalWindow => CreateMinimalWindow(),
            ChatAppLaunchMode.WebViewSmoke => CreateLoggedWindow(new WebViewSmokeWindow(), "WebView smoke window constructed"),
            ChatAppLaunchMode.LegacyWebView => CreateLoggedWindow(new MainWindow(), "Legacy WebView MainWindow constructed"),
            _ => CreateLoggedWindow(new NativeChatWindow(), "Native WinUI chat window constructed")
        };

    private static Window CreateMinimalWindow() {
        var window = new Window {
            Title = "IntelligenceX Chat (Min Window)",
            Content = new TextBlock {
                Text = "Minimal window mode",
                Margin = new Thickness(24)
            }
        };
        StartupLog.Write("Min window constructed");
        return window;
    }

    private static TWindow CreateLoggedWindow<TWindow>(TWindow window, string message)
        where TWindow : Window {
        StartupLog.Write(message);
        return window;
    }

    private void EnsureXamlResources() {
        if (_resourcesInitialized) {
            return;
        }

        Resources ??= new ResourceDictionary();
        Resources.MergedDictionaries.Add(new XamlControlsResources());
        _resourcesInitialized = true;
        StartupLog.Write("App resources initialized");
    }

}
