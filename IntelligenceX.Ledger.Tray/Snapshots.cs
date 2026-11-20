using Avalonia;
using Avalonia.Media.Imaging;

namespace IntelligenceX.Ledger.Tray;

/// <summary>Writes the details window and every icon state to PNG files, so screenshots don't need a screen capture.</summary>
internal static class Snapshots {
    public static void Save(string folder, DetailsWindow window, TrayView view, bool darkBar) {
        Directory.CreateDirectory(folder);
        var theme = window.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark ? "dark" : "light";
        var scale = window.RenderScaling;
        var size = new PixelSize((int)(window.Bounds.Width * scale), (int)(window.Bounds.Height * scale));
        using (var bitmap = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale))) {
            bitmap.Render(window);
            bitmap.Save(Path.Combine(folder, "details-" + theme + ".png"), new PngBitmapEncoderOptions());
        }
        foreach (var state in Enum.GetValues<TrayState>())
            foreach (var dark in new[] { false, true }) {
                using var icon = TrayArt.Render(state, dark, 64);
                icon.Save(Path.Combine(folder, "icon-" + state.ToString().ToLowerInvariant() + (dark ? "-dark-bar" : "-light-bar") + ".png"), new PngBitmapEncoderOptions());
            }
        File.WriteAllText(Path.Combine(folder, "menu-" + theme + ".txt"), view.Text());
    }
}
