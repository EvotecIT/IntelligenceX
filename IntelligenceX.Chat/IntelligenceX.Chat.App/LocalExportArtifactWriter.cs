using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using IntelligenceX.Chat.ExportArtifacts;

namespace IntelligenceX.Chat.App;

/// <summary>
/// Local export writer used by the desktop app when service-side export tooling is unavailable.
/// </summary>
internal static class LocalExportArtifactWriter {
    public static bool TryReadRows(JsonElement rowsElement, out List<string[]> rows) {
        rows = new List<string[]>();
        if (rowsElement.ValueKind != JsonValueKind.Array) {
            return false;
        }

        int maxColumns = 0;
        foreach (var rowElement in rowsElement.EnumerateArray()) {
            string[] row;
            if (rowElement.ValueKind == JsonValueKind.Array) {
                var values = new List<string>();
                foreach (var cell in rowElement.EnumerateArray()) {
                    values.Add(ReadCellAsText(cell));
                }
                row = values.ToArray();
            } else {
                row = [ReadCellAsText(rowElement)];
            }

            if (row.Length > maxColumns) {
                maxColumns = row.Length;
            }

            rows.Add(row);
        }

        if (rows.Count == 0 || maxColumns == 0) {
            rows.Clear();
            return false;
        }

        for (int i = 0; i < rows.Count; i++) {
            if (rows[i].Length == maxColumns) {
                continue;
            }

            var expanded = new string[maxColumns];
            Array.Copy(rows[i], expanded, rows[i].Length);
            for (int c = rows[i].Length; c < expanded.Length; c++) {
                expanded[c] = string.Empty;
            }
            rows[i] = expanded;
        }

        return true;
    }

    public static string ResolveOutputPath(string format, string? title, string? outputPath, string defaultPrefix = "dataset") {
        var extension = GetFileExtension(format);
        var normalizedPath = (outputPath ?? string.Empty).Trim();

        if (normalizedPath.Length == 0) {
            var dir = Path.Combine(Path.GetTempPath(), "IntelligenceX.Chat", "exports");
            Directory.CreateDirectory(dir);
            var stem = SanitizeFileStem(title, defaultPrefix);
            var ts = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            return Path.Combine(dir, stem + "-" + ts + extension);
        }

        var fullPath = Path.GetFullPath(normalizedPath);
        var currentExtension = Path.GetExtension(fullPath);
        if (!string.Equals(currentExtension, extension, StringComparison.OrdinalIgnoreCase)) {
            fullPath = Path.ChangeExtension(fullPath, extension);
        }

        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory)) {
            Directory.CreateDirectory(directory);
        }

        return fullPath;
    }

    public static void ExportTable(string format, string title, IReadOnlyList<string[]> rows, string outputPath) {
        if (rows is null || rows.Count == 0) {
            throw new InvalidOperationException("No rows were provided for export.");
        }

        var normalizedFormat = (format ?? string.Empty).Trim().ToLowerInvariant();
        switch (normalizedFormat) {
            case ExportPreferencesContract.FormatCsv:
                WriteCsv(rows, outputPath);
                break;
            case ExportPreferencesContract.FormatXlsx:
                OfficeImoArtifactWriter.WriteXlsx(title, rows, outputPath);
                break;
            case ExportPreferencesContract.FormatDocx:
                OfficeImoArtifactWriter.WriteDocxTable(title, rows, outputPath);
                break;
            default:
                throw new InvalidOperationException("Unsupported export format: " + normalizedFormat);
        }
    }

    public static void ExportTranscript(string format, string title, string markdown, string outputPath) {
        var normalizedFormat = (format ?? string.Empty).Trim().ToLowerInvariant();
        switch (normalizedFormat) {
            case ExportPreferencesContract.FormatMarkdown:
                File.WriteAllText(outputPath, markdown ?? string.Empty, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
                break;
            case ExportPreferencesContract.FormatDocx:
                OfficeImoArtifactWriter.WriteDocxTranscript(title, markdown ?? string.Empty, outputPath);
                break;
            default:
                throw new InvalidOperationException("Unsupported transcript export format: " + normalizedFormat);
        }
    }

    private static string ReadCellAsText(JsonElement cell) {
        return cell.ValueKind switch {
            JsonValueKind.String => cell.GetString() ?? string.Empty,
            JsonValueKind.Null => string.Empty,
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Number => cell.GetRawText(),
            JsonValueKind.Object => cell.GetRawText(),
            JsonValueKind.Array => cell.GetRawText(),
            _ => cell.ToString()
        };
    }

    private static void WriteCsv(IReadOnlyList<string[]> rows, string outputPath) {
        using var writer = new StreamWriter(outputPath, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        for (int r = 0; r < rows.Count; r++) {
            var row = rows[r];
            for (int c = 0; c < row.Length; c++) {
                if (c > 0) {
                    writer.Write(',');
                }
                writer.Write(EscapeCsv(row[c] ?? string.Empty));
            }

            writer.WriteLine();
        }
    }

    private static string EscapeCsv(string value) {
        if (value.IndexOfAny([',', '"', '\r', '\n']) < 0) {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private static string GetFileExtension(string format) {
        return (format ?? string.Empty).Trim().ToLowerInvariant() switch {
            ExportPreferencesContract.FormatXlsx => ".xlsx",
            ExportPreferencesContract.FormatDocx => ".docx",
            ExportPreferencesContract.FormatMarkdown => ".md",
            _ => ".csv"
        };
    }

    private static string SanitizeFileStem(string? title, string fallback) {
        var stem = (title ?? string.Empty).Trim();
        if (stem.Length == 0) {
            stem = fallback;
        }

        foreach (var ch in Path.GetInvalidFileNameChars()) {
            stem = stem.Replace(ch, '_');
        }

        if (stem.Length > 80) {
            stem = stem[..80].TrimEnd();
        }

        return stem.Length == 0 ? fallback : stem;
    }
}
