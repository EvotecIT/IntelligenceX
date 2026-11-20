using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Microsoft.Win32;

namespace IntelligenceX.Ledger.Tray;

/// <summary>
/// The icon, drawn rather than shipped: a ledger page with three ruled lines and a status dot in the corner. Green is
/// working, amber needs a look, red is down and grey is still reading. The page is drawn light on a dark taskbar or
/// menu bar and dark on a light one.
/// </summary>
internal static class TrayArt {
    public static Color StateColor(TrayState state) => state switch {
        TrayState.Healthy => Color.FromRgb(0x2D, 0xA4, 0x4E),
        TrayState.Degraded => Color.FromRgb(0xE3, 0xA0, 0x08),
        TrayState.Down => Color.FromRgb(0xE5, 0x48, 0x4D),
        _ => Color.FromRgb(0x8C, 0x95, 0x9F)
    };

    public static WindowIcon Draw(TrayState state, bool darkBar) {
        using var bitmap = Render(state, darkBar);
        using var stream = new MemoryStream();
        bitmap.Save(stream, new PngBitmapEncoderOptions());
        stream.Position = 0;
        return new WindowIcon(stream);
    }

    public static RenderTargetBitmap Render(TrayState state, bool darkBar, int size = 64) {
        var ink = darkBar ? Color.FromRgb(0xF2, 0xF4, 0xF7) : Color.FromRgb(0x1F, 0x23, 0x28);
        var scale = size / 64.0;
        var pen = new Pen(new SolidColorBrush(ink), 5 * scale, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        var bitmap = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
        using (var context = bitmap.CreateDrawingContext()) {
            context.DrawRectangle(null, pen, new Rect(8 * scale, 5 * scale, 40 * scale, 52 * scale), 6 * scale, 6 * scale);
            foreach (var y in new[] { 19, 29, 39 })
                context.DrawLine(pen, new Point(17 * scale, y * scale), new Point((y == 39 ? 30 : 39) * scale, y * scale));
            var badge = new SolidColorBrush(StateColor(state));
            var ring = new Pen(new SolidColorBrush(darkBar ? Color.FromRgb(0x1F, 0x23, 0x28) : Colors.White), 4 * scale);
            context.DrawEllipse(badge, ring, new Point(46 * scale, 46 * scale), 15 * scale, 15 * scale);
        }
        return bitmap;
    }

    /// <summary>Whether the taskbar or menu bar is dark. Windows keeps the taskbar's setting apart from apps'.</summary>
    public static bool DarkBar(Application? application) {
        if (OperatingSystem.IsWindows()) return WindowsTaskbarDark();
        return application?.PlatformSettings?.GetColorValues().ThemeVariant == Avalonia.Platform.PlatformThemeVariant.Dark;
    }

    [SupportedOSPlatform("windows")]
    private static bool WindowsTaskbarDark() {
        try {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is not 1;
        } catch (System.Security.SecurityException) { return true; }
    }
}
