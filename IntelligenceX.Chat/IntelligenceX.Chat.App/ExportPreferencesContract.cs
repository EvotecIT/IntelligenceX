using System;
using System.IO;

namespace IntelligenceX.Chat.App;

internal static class ExportPreferencesContract {
    public const string SaveModeAsk = "ask";
    public const string SaveModeRemember = "remember";
    public const string DefaultSaveMode = SaveModeAsk;

    public const string FormatCsv = "csv";
    public const string FormatXlsx = "xlsx";
    public const string FormatDocx = "docx";
    public const string FormatMarkdown = "md";
    public const string DefaultFormat = FormatXlsx;

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
