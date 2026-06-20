using Microsoft.UI;
using Microsoft.UI.Xaml.Media;

namespace IntelligenceX.Chat.App.Native;

/// <summary>
/// Shared native chat brush helpers.
/// </summary>
internal static class NativeControlBrushes {
    public static SolidColorBrush Rgb(byte r, byte g, byte b) =>
        new(ColorHelper.FromArgb(255, r, g, b));

    public static SolidColorBrush Argb(byte a, byte r, byte g, byte b) =>
        new(ColorHelper.FromArgb(a, r, g, b));

    public static SolidColorBrush AppBackground => Rgb(244, 246, 249);

    public static SolidColorBrush Surface => Rgb(255, 255, 255);

    public static SolidColorBrush SurfaceMuted => Rgb(248, 250, 252);

    public static SolidColorBrush Border => Rgb(226, 232, 240);

    public static SolidColorBrush BorderStrong => Rgb(204, 213, 226);

    public static SolidColorBrush TextPrimary => Rgb(24, 33, 47);

    public static SolidColorBrush TextSecondary => Rgb(82, 96, 116);

    public static SolidColorBrush TextMuted => Rgb(112, 126, 148);

    public static SolidColorBrush Accent => Rgb(51, 100, 214);

    public static SolidColorBrush AccentSoft => Rgb(232, 240, 255);

    public static SolidColorBrush SuccessSoft => Rgb(229, 247, 237);

    public static SolidColorBrush Success => Rgb(22, 122, 72);
}
