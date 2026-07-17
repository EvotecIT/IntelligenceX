using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace IntelligenceX.Chat.App.Native;

/// <summary>
/// Owns resizable native artifact windows and their lifetime.
/// </summary>
internal static class NativeArtifactWindow {
    private static readonly HashSet<Window> OpenWindows = new();

    public static void ApplyCurrentTheme() {
        foreach (var window in new List<Window>(OpenWindows)) {
            if (window.Content is not FrameworkElement root) continue;
            root.RequestedTheme = NativeControlBrushes.RequestedTheme;
            if (root is Grid grid) grid.Background = NativeControlBrushes.AppBackground;
        }
    }

    public static void Show(string title, Func<FrameworkElement> contentFactory, int width, int height) {
        if (contentFactory == null) throw new ArgumentNullException(nameof(contentFactory));
        var content = contentFactory();
        var root = new Grid {
            Padding = new Thickness(18),
            Background = NativeControlBrushes.AppBackground,
            RequestedTheme = NativeControlBrushes.RequestedTheme,
            Children = { content }
        };
        var window = new Window {
            Title = string.IsNullOrWhiteSpace(title) ? "IntelligenceX artifact" : title.Trim(),
            Content = root,
            SystemBackdrop = new MicaBackdrop()
        };
        window.Closed += (_, _) => OpenWindows.Remove(window);
        OpenWindows.Add(window);
        window.AppWindow.Resize(new SizeInt32(Math.Max(640, width), Math.Max(480, height)));
        window.Activate();
    }
}
