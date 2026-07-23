using System;
using System.Collections.Generic;
using System.Globalization;
using IntelligenceX.Chat.App.Theming;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace IntelligenceX.Chat.App.Native;

/// <summary>
/// Shared, live-updatable native chat palette.
/// </summary>
internal static class NativeControlBrushes {
    private static readonly SolidColorBrush AppBackgroundBrush = Rgb(246, 248, 252);
    private static readonly SolidColorBrush SurfaceBrush = Rgb(255, 255, 255);
    private static readonly SolidColorBrush SurfaceMutedBrush = Rgb(248, 250, 252);
    private static readonly SolidColorBrush BorderBrush = Rgb(226, 232, 240);
    private static readonly SolidColorBrush BorderStrongBrush = Rgb(204, 213, 226);
    private static readonly SolidColorBrush TextPrimaryBrush = Rgb(24, 33, 47);
    private static readonly SolidColorBrush TextSecondaryBrush = Rgb(82, 96, 116);
    private static readonly SolidColorBrush TextMutedBrush = Rgb(112, 126, 148);
    private static readonly SolidColorBrush AccentBrush = Rgb(51, 100, 214);
    private static readonly SolidColorBrush AccentSoftBrush = Rgb(232, 240, 255);
    private static readonly SolidColorBrush SuccessSoftBrush = Rgb(229, 247, 237);
    private static readonly SolidColorBrush SuccessBrush = Rgb(22, 122, 72);
    private static readonly SolidColorBrush SuccessBorderBrush = Rgb(134, 224, 176);
    private static readonly SolidColorBrush SuccessBadgeBrush = Rgb(209, 250, 229);
    private static readonly SolidColorBrush InfoSoftBrush = Rgb(239, 246, 255);
    private static readonly SolidColorBrush InfoBorderBrush = Rgb(191, 219, 254);
    private static readonly SolidColorBrush InfoTextBrush = Rgb(29, 78, 216);
    private static readonly SolidColorBrush InfoBadgeBrush = Rgb(219, 234, 254);
    private static readonly SolidColorBrush WarningSoftBrush = Rgb(255, 251, 235);
    private static readonly SolidColorBrush WarningBorderBrush = Rgb(252, 211, 77);
    private static readonly SolidColorBrush WarningTextBrush = Rgb(146, 64, 14);
    private static readonly SolidColorBrush WarningBadgeBrush = Rgb(254, 243, 199);
    private static readonly SolidColorBrush ErrorSoftBrush = Rgb(254, 242, 242);
    private static readonly SolidColorBrush ErrorBorderBrush = Rgb(252, 165, 165);
    private static readonly SolidColorBrush ErrorTextBrush = Rgb(185, 28, 28);
    private static readonly SolidColorBrush ErrorBadgeBrush = Rgb(254, 226, 226);
    private static readonly SolidColorBrush NeutralSoftBrush = Rgb(248, 250, 252);
    private static readonly SolidColorBrush NeutralBadgeBrush = Rgb(226, 232, 240);
    private static readonly SolidColorBrush QuoteBrush = Rgb(124, 58, 237);
    private static readonly SolidColorBrush QuoteSoftBrush = Rgb(245, 243, 255);
    private static readonly SolidColorBrush CodeBackgroundBrush = Rgb(15, 23, 42);
    private static readonly SolidColorBrush CodeHeaderBrush = Rgb(30, 41, 59);
    private static readonly SolidColorBrush CodeBorderBrush = Rgb(51, 65, 85);
    private static readonly SolidColorBrush CodeTextBrush = Rgb(226, 232, 240);
    private static readonly SolidColorBrush UserBubbleBrush = Rgb(241, 246, 255);
    private static readonly SolidColorBrush UserBorderBrush = Rgb(191, 210, 252);
    private static readonly SolidColorBrush AssistantAccentBrush = Rgb(13, 148, 136);
    private static readonly SolidColorBrush AssistantTextBrush = Rgb(15, 118, 110);
    private static readonly SolidColorBrush SystemAccentBrush = Rgb(124, 58, 237);
    private static readonly SolidColorBrush SystemTextBrush = Rgb(109, 40, 217);

    internal static ElementTheme RequestedTheme { get; private set; } = ElementTheme.Light;

    public static SolidColorBrush Rgb(byte r, byte g, byte b) =>
        new(ColorHelper.FromArgb(255, r, g, b));

    public static SolidColorBrush Argb(byte a, byte r, byte g, byte b) =>
        new(ColorHelper.FromArgb(a, r, g, b));

    public static SolidColorBrush AppBackground => AppBackgroundBrush;
    public static SolidColorBrush Surface => SurfaceBrush;
    public static SolidColorBrush SurfaceMuted => SurfaceMutedBrush;
    public static SolidColorBrush Border => BorderBrush;
    public static SolidColorBrush BorderStrong => BorderStrongBrush;
    public static SolidColorBrush TextPrimary => TextPrimaryBrush;
    public static SolidColorBrush TextSecondary => TextSecondaryBrush;
    public static SolidColorBrush TextMuted => TextMutedBrush;
    public static SolidColorBrush Accent => AccentBrush;
    public static SolidColorBrush AccentSoft => AccentSoftBrush;
    public static SolidColorBrush SuccessSoft => SuccessSoftBrush;
    public static SolidColorBrush Success => SuccessBrush;
    public static SolidColorBrush SuccessBorder => SuccessBorderBrush;
    public static SolidColorBrush SuccessBadge => SuccessBadgeBrush;
    public static SolidColorBrush InfoSoft => InfoSoftBrush;
    public static SolidColorBrush InfoBorder => InfoBorderBrush;
    public static SolidColorBrush InfoText => InfoTextBrush;
    public static SolidColorBrush InfoBadge => InfoBadgeBrush;
    public static SolidColorBrush WarningSoft => WarningSoftBrush;
    public static SolidColorBrush WarningBorder => WarningBorderBrush;
    public static SolidColorBrush WarningText => WarningTextBrush;
    public static SolidColorBrush WarningBadge => WarningBadgeBrush;
    public static SolidColorBrush ErrorSoft => ErrorSoftBrush;
    public static SolidColorBrush ErrorBorder => ErrorBorderBrush;
    public static SolidColorBrush ErrorText => ErrorTextBrush;
    public static SolidColorBrush ErrorBadge => ErrorBadgeBrush;
    public static SolidColorBrush NeutralSoft => NeutralSoftBrush;
    public static SolidColorBrush NeutralBadge => NeutralBadgeBrush;
    public static SolidColorBrush Quote => QuoteBrush;
    public static SolidColorBrush QuoteSoft => QuoteSoftBrush;
    public static SolidColorBrush CodeBackground => CodeBackgroundBrush;
    public static SolidColorBrush CodeHeader => CodeHeaderBrush;
    public static SolidColorBrush CodeBorder => CodeBorderBrush;
    public static SolidColorBrush CodeText => CodeTextBrush;
    public static SolidColorBrush UserBubble => UserBubbleBrush;
    public static SolidColorBrush UserBorder => UserBorderBrush;
    public static SolidColorBrush AssistantAccent => AssistantAccentBrush;
    public static SolidColorBrush AssistantText => AssistantTextBrush;
    public static SolidColorBrush SystemAccent => SystemAccentBrush;
    public static SolidColorBrush SystemText => SystemTextBrush;

    internal static void ApplyTheme(string? presetName) {
        var preset = ThemeContract.Normalize(presetName) ?? ThemeContract.DefaultPreset;
        if (string.Equals(preset, ThemeContract.DefaultPreset, StringComparison.OrdinalIgnoreCase)
            || !ThemeRegistry.TryGetVariables(preset, out var variables)) {
            ApplyLightPalette();
            return;
        }

        ApplyDarkPalette(variables);
    }

    private static void ApplyLightPalette() {
        RequestedTheme = ElementTheme.Light;
        Set(AppBackgroundBrush, 246, 248, 252);
        Set(SurfaceBrush, 255, 255, 255);
        Set(SurfaceMutedBrush, 248, 250, 252);
        Set(BorderBrush, 226, 232, 240);
        Set(BorderStrongBrush, 204, 213, 226);
        Set(TextPrimaryBrush, 24, 33, 47);
        Set(TextSecondaryBrush, 82, 96, 116);
        Set(TextMutedBrush, 112, 126, 148);
        Set(AccentBrush, 51, 100, 214);
        Set(AccentSoftBrush, 232, 240, 255);
        Set(UserBubbleBrush, 241, 246, 255);
        Set(UserBorderBrush, 191, 210, 252);
        ApplySemanticPalette(dark: false);
    }

    private static void ApplyDarkPalette(IReadOnlyDictionary<string, string> variables) {
        RequestedTheme = ElementTheme.Dark;
        var background = ReadColor(variables, "--ix-bg-primary", ColorHelper.FromArgb(255, 13, 18, 28));
        var surface = ReadColor(variables, "--ix-bg-secondary", ColorHelper.FromArgb(255, 22, 29, 42));
        var elevated = ReadColor(variables, "--ix-bg-elevated", ColorHelper.FromArgb(255, 31, 41, 57));
        var accent = ReadColor(variables, "--ix-accent", ColorHelper.FromArgb(255, 96, 165, 250));
        var secondary = ReadColor(variables, "--ix-text-secondary", ColorHelper.FromArgb(255, 203, 213, 225));
        var muted = ReadColor(variables, "--ix-text-muted", ColorHelper.FromArgb(255, 148, 163, 184));
        Set(AppBackgroundBrush, background);
        Set(SurfaceBrush, surface);
        Set(SurfaceMutedBrush, elevated);
        Set(BorderBrush, Blend(surface, secondary, 0.28d));
        Set(BorderStrongBrush, Blend(surface, secondary, 0.45d));
        Set(TextPrimaryBrush, 241, 245, 249);
        Set(TextSecondaryBrush, secondary);
        Set(TextMutedBrush, muted);
        Set(AccentBrush, accent);
        Set(AccentSoftBrush, Blend(surface, accent, 0.22d));
        Set(UserBubbleBrush, Blend(surface, accent, 0.17d));
        Set(UserBorderBrush, Blend(surface, accent, 0.48d));
        ApplySemanticPalette(dark: true);
    }

    private static void ApplySemanticPalette(bool dark) {
        if (!dark) {
            Set(SuccessSoftBrush, 229, 247, 237); Set(SuccessBrush, 22, 122, 72);
            Set(SuccessBorderBrush, 134, 224, 176); Set(SuccessBadgeBrush, 209, 250, 229);
            Set(InfoSoftBrush, 239, 246, 255); Set(InfoBorderBrush, 191, 219, 254);
            Set(InfoTextBrush, 29, 78, 216); Set(InfoBadgeBrush, 219, 234, 254);
            Set(WarningSoftBrush, 255, 251, 235); Set(WarningBorderBrush, 252, 211, 77);
            Set(WarningTextBrush, 146, 64, 14); Set(WarningBadgeBrush, 254, 243, 199);
            Set(ErrorSoftBrush, 254, 242, 242); Set(ErrorBorderBrush, 252, 165, 165);
            Set(ErrorTextBrush, 185, 28, 28); Set(ErrorBadgeBrush, 254, 226, 226);
            Set(NeutralSoftBrush, 248, 250, 252); Set(NeutralBadgeBrush, 226, 232, 240);
            Set(QuoteBrush, 124, 58, 237); Set(QuoteSoftBrush, 245, 243, 255);
            Set(AssistantAccentBrush, 13, 148, 136); Set(AssistantTextBrush, 15, 118, 110);
            Set(SystemAccentBrush, 124, 58, 237); Set(SystemTextBrush, 109, 40, 217);
            return;
        }

        Set(SuccessSoftBrush, 18, 56, 43); Set(SuccessBrush, 110, 231, 183);
        Set(SuccessBorderBrush, 36, 129, 91); Set(SuccessBadgeBrush, 24, 78, 58);
        Set(InfoSoftBrush, 23, 48, 82); Set(InfoBorderBrush, 54, 104, 174);
        Set(InfoTextBrush, 147, 197, 253); Set(InfoBadgeBrush, 30, 64, 112);
        Set(WarningSoftBrush, 67, 49, 17); Set(WarningBorderBrush, 180, 126, 25);
        Set(WarningTextBrush, 253, 224, 135); Set(WarningBadgeBrush, 92, 67, 20);
        Set(ErrorSoftBrush, 69, 30, 34); Set(ErrorBorderBrush, 165, 65, 73);
        Set(ErrorTextBrush, 254, 202, 202); Set(ErrorBadgeBrush, 94, 38, 43);
        Set(NeutralSoftBrush, SurfaceMutedBrush.Color); Set(NeutralBadgeBrush, BorderBrush.Color);
        Set(QuoteBrush, 196, 181, 253); Set(QuoteSoftBrush, 51, 39, 85);
        Set(AssistantAccentBrush, 94, 234, 212); Set(AssistantTextBrush, 153, 246, 228);
        Set(SystemAccentBrush, 196, 181, 253); Set(SystemTextBrush, 221, 214, 254);
    }

    private static Color ReadColor(
        IReadOnlyDictionary<string, string> variables,
        string key,
        Color fallback) {
        if (!variables.TryGetValue(key, out var value)) {
            return fallback;
        }
        var text = value.Trim();
        if (text.Length != 7 || text[0] != '#'
            || !uint.TryParse(text[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)) {
            return fallback;
        }
        return ColorHelper.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    }

    private static Color Blend(Color background, Color foreground, double amount) {
        var ratio = Math.Clamp(amount, 0d, 1d);
        return ColorHelper.FromArgb(
            255,
            (byte)Math.Round(background.R + ((foreground.R - background.R) * ratio)),
            (byte)Math.Round(background.G + ((foreground.G - background.G) * ratio)),
            (byte)Math.Round(background.B + ((foreground.B - background.B) * ratio)));
    }

    private static void Set(SolidColorBrush brush, byte red, byte green, byte blue) =>
        brush.Color = ColorHelper.FromArgb(255, red, green, blue);

    private static void Set(SolidColorBrush brush, Color color) => brush.Color = color;
}
