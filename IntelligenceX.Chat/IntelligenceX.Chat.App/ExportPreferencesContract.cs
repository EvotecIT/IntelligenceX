using System;
using System.Globalization;
using System.IO;

namespace IntelligenceX.Chat.App;

internal static class ExportPreferencesContract {
    public const string SaveModeAsk = "ask";
    public const string SaveModeRemember = "remember";
    public const string DefaultSaveMode = SaveModeAsk;

    public const string VisualThemeModePreserveUi = "preserve_ui_theme";
    public const string VisualThemeModePrintFriendly = "print_friendly";
    public const string DefaultVisualThemeMode = VisualThemeModePreserveUi;

    public const string FormatCsv = "csv";
    public const string FormatXlsx = "xlsx";
    public const string FormatDocx = "docx";
    public const string FormatMarkdown = "md";
    public const string DefaultFormat = FormatXlsx;
    public const int MinDocxVisualMaxWidthPx = 320;
    public const int MaxDocxVisualMaxWidthPx = 2000;
    public const int DefaultDocxVisualMaxWidthPx = 760;

    public static string NormalizeSaveMode(string? value) {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch {
            SaveModeRemember or "last" or "auto" => SaveModeRemember,
            _ => SaveModeAsk
        };
    }

    public static string NormalizeFormat(string? value) {
        return TryNormalizeFormat(value, out var format) ? format : DefaultFormat;
    }

    public static string NormalizeVisualThemeMode(string? value) {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch {
            VisualThemeModePrintFriendly or "print" or "light" => VisualThemeModePrintFriendly,
            VisualThemeModePreserveUi or "preserve" or "theme" => VisualThemeModePreserveUi,
            _ => DefaultVisualThemeMode
        };
    }

    public static bool TryNormalizeFormat(string? value, out string format) {
        var normalized = (value ?? string.Empty).Trim().ToLowerInvariant();
        format = normalized switch {
            "excel" => FormatXlsx,
            "word" => FormatDocx,
            "markdown" => FormatMarkdown,
            FormatMarkdown => FormatMarkdown,
            FormatCsv => FormatCsv,
            FormatXlsx => FormatXlsx,
            FormatDocx => FormatDocx,
            _ => string.Empty
        };

        return format.Length > 0;
    }

    public static int NormalizeDocxVisualMaxWidthPx(int? value) {
        if (!value.HasValue) {
            return DefaultDocxVisualMaxWidthPx;
        }

        var normalized = value.Value;
        if (normalized < MinDocxVisualMaxWidthPx) {
            return MinDocxVisualMaxWidthPx;
        }
        if (normalized > MaxDocxVisualMaxWidthPx) {
            return MaxDocxVisualMaxWidthPx;
        }

        return normalized;
    }

    public static int NormalizeDocxVisualMaxWidthPx(string? value) {
        var normalized = (value ?? string.Empty).Trim();
        if (!int.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)) {
            return DefaultDocxVisualMaxWidthPx;
        }

        return NormalizeDocxVisualMaxWidthPx(parsed);
    }

    public static string? NormalizeDirectory(string? path) {
        var normalized = (path ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return null;
        }

        try {
            normalized = Path.GetFullPath(normalized);
        } catch {
            return null;
        }

        return Directory.Exists(normalized) ? normalized : null;
    }

    public static string? NormalizeFromFilePath(string? path) {
        var normalized = (path ?? string.Empty).Trim();
        if (normalized.Length == 0) {
            return null;
        }

        try {
            var full = Path.GetFullPath(normalized);
            var dir = Path.GetDirectoryName(full);
            return NormalizeDirectory(dir);
        } catch {
            return null;
        }
    }
}
