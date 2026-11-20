using Avalonia;
using Avalonia.Media.Imaging;

namespace IntelligenceX.Ledger.Tray;

/// <summary>
/// Writes the details page in light and dark, and every icon state, to PNG files. Everything is laid out and drawn
/// offscreen, so no window appears on the desktop. The page is drawn at the window's default size and at full length.
/// </summary>
internal static class Snapshots {
    public const double WindowHeight = 720;

    public static void Save(string folder, TrayView view, DateTimeOffset at) {
        Directory.CreateDirectory(folder);
        foreach (var (theme, palette) in new[] { ("light", Palette.Light), ("dark", Palette.Dark) }) {
            var page = DetailsView.Build(view, at, palette);
            page.Measure(new Size(DetailsView.Width, double.PositiveInfinity));
            page.Arrange(new Rect(0, 0, DetailsView.Width, page.DesiredSize.Height));
            Draw(page, page.DesiredSize.Height, Path.Combine(folder, "details-" + theme + ".png"));
            Draw(page, Math.Min(WindowHeight, page.DesiredSize.Height), Path.Combine(folder, "details-" + theme + "-window.png"));
        }
        foreach (var state in Enum.GetValues<TrayState>())
            foreach (var dark in new[] { false, true }) {
                using var icon = TrayArt.Render(state, dark, 64);
                icon.Save(Path.Combine(folder, "icon-" + state.ToString().ToLowerInvariant() + (dark ? "-dark-bar" : "-light-bar") + ".png"), new PngBitmapEncoderOptions());
            }
        File.WriteAllText(Path.Combine(folder, "menu.txt"), view.Text());
    }

    private static void Draw(Visual page, double height, string path) {
        const double scale = 2;
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)(DetailsView.Width * scale), (int)(height * scale)), new Vector(96 * scale, 96 * scale));
        bitmap.Render(page);
        bitmap.Save(path, new PngBitmapEncoderOptions());
    }
}
