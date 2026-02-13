using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace IntelligenceX.Chat.App;

internal sealed class WebViewSmokeWindow : Window {
    private readonly TextBlock _status;

    public WebViewSmokeWindow() {
        Title = "WebView Smoke";

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        _status = new TextBlock {
            Margin = new Thickness(8),
            Text = "Initializing WebView2..."
        };
        grid.Children.Add(_status);

        var web = new WebView2();
        Grid.SetRow(web, 1);
        grid.Children.Add(web);

        Content = grid;

        Activated += async (_, _) => {
            StartupLog.Write("WebViewSmokeWindow.Activated");
            try {
                await web.EnsureCoreWebView2Async();
                web.NavigateToString("<html><body><h1>WebView2 OK</h1></body></html>");
                _status.Text = "WebView2 initialized.";
                StartupLog.Write("WebViewSmokeWindow.EnsureCoreWebView2Async ok");
            } catch (System.Exception ex) {
                _status.Text = ex.GetType().Name + ": " + ex.Message;
                StartupLog.Write("WebViewSmokeWindow.EnsureCoreWebView2Async failed: " + ex);
            }
        };
    }
}
