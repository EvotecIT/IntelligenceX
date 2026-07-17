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

    public static SolidColorBrush AppBackground => Rgb(246, 248, 252);

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

    public static SolidColorBrush SuccessBorder => Rgb(134, 224, 176);

    public static SolidColorBrush SuccessBadge => Rgb(209, 250, 229);

    public static SolidColorBrush InfoSoft => Rgb(239, 246, 255);

    public static SolidColorBrush InfoBorder => Rgb(191, 219, 254);

    public static SolidColorBrush InfoText => Rgb(29, 78, 216);

    public static SolidColorBrush InfoBadge => Rgb(219, 234, 254);

    public static SolidColorBrush WarningSoft => Rgb(255, 251, 235);

    public static SolidColorBrush WarningBorder => Rgb(252, 211, 77);

    public static SolidColorBrush WarningText => Rgb(146, 64, 14);

    public static SolidColorBrush WarningBadge => Rgb(254, 243, 199);

    public static SolidColorBrush ErrorSoft => Rgb(254, 242, 242);

    public static SolidColorBrush ErrorBorder => Rgb(252, 165, 165);

    public static SolidColorBrush ErrorText => Rgb(185, 28, 28);

    public static SolidColorBrush ErrorBadge => Rgb(254, 226, 226);

    public static SolidColorBrush NeutralSoft => Rgb(248, 250, 252);

    public static SolidColorBrush NeutralBadge => Rgb(226, 232, 240);

    public static SolidColorBrush Quote => Rgb(124, 58, 237);

    public static SolidColorBrush QuoteSoft => Rgb(245, 243, 255);

    public static SolidColorBrush CodeBackground => Rgb(15, 23, 42);

    public static SolidColorBrush CodeHeader => Rgb(30, 41, 59);

    public static SolidColorBrush CodeBorder => Rgb(51, 65, 85);

    public static SolidColorBrush CodeText => Rgb(226, 232, 240);

    public static SolidColorBrush UserBubble => Rgb(241, 246, 255);

    public static SolidColorBrush UserBorder => Rgb(191, 210, 252);

    public static SolidColorBrush AssistantAccent => Rgb(13, 148, 136);

    public static SolidColorBrush AssistantText => Rgb(15, 118, 110);

    public static SolidColorBrush SystemAccent => Rgb(124, 58, 237);

    public static SolidColorBrush SystemText => Rgb(109, 40, 217);
}
