using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using IntelligenceX.Chat.App.Markdown;
using IntelligenceX.Chat.App.Rendering;
using IntelligenceX.Chat.ExportArtifacts;

namespace IntelligenceX.Chat.App;

/// <summary>
/// Local export writer used by the desktop app when service-side export tooling is unavailable.
/// </summary>
internal static class LocalExportArtifactWriter {
    internal delegate void MarkdownTranscriptWriter(string outputPath, string markdown);
    internal delegate void DocxTranscriptWriter(
        string title,
        string markdown,
        string outputPath,
        IReadOnlyList<string>? additionalAllowedImageDirectories,
        int? docxVisualMaxWidthPx);

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

    public static TranscriptExportResult ExportTranscript(
        string format,
        string title,
        string markdown,
        string outputPath,
        IReadOnlyList<string>? additionalAllowedImageDirectories = null,
        int? docxVisualMaxWidthPx = null,
        bool allowMarkdownFallback = true) {
        return ExportTranscript(
            format,
            title,
            markdown,
            outputPath,
            additionalAllowedImageDirectories,
            docxVisualMaxWidthPx,
            allowMarkdownFallback,
            static (path, text) => File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)),
            static (docxTitle, docxMarkdown, docxPath, allowedImageDirectories, maxWidthPx) =>
                OfficeImoArtifactWriter.WriteDocxTranscript(docxTitle, docxMarkdown, docxPath, allowedImageDirectories, maxWidthPx));
    }

    internal static TranscriptExportResult ExportTranscript(
        string format,
        string title,
        string markdown,
        string outputPath,
        IReadOnlyList<string>? additionalAllowedImageDirectories,
        int? docxVisualMaxWidthPx,
        bool allowMarkdownFallback,
        MarkdownTranscriptWriter markdownWriter,
        DocxTranscriptWriter docxWriter) {
        var normalizedFormat = (format ?? string.Empty).Trim().ToLowerInvariant();
        var safeMarkdown = NormalizeTranscriptMarkdownForExport(markdown ?? string.Empty);
        switch (normalizedFormat) {
            case ExportPreferencesContract.FormatMarkdown:
                try {
                    markdownWriter(outputPath, safeMarkdown);
                    return TranscriptExportResult.Success(normalizedFormat, ExportPreferencesContract.FormatMarkdown, outputPath);
                } catch (Exception ex) {
                    return TranscriptExportResult.Failed(
                        normalizedFormat,
                        outputPath,
                        new TranscriptExportFailure(TranscriptExportStage.MarkdownWrite, ex.Message));
                }
            case ExportPreferencesContract.FormatDocx:
                try {
                    docxWriter(title, safeMarkdown, outputPath, additionalAllowedImageDirectories, docxVisualMaxWidthPx);
                    return TranscriptExportResult.Success(normalizedFormat, ExportPreferencesContract.FormatDocx, outputPath);
                } catch (Exception ex) {
                    var docxFailure = new TranscriptExportFailure(TranscriptExportStage.DocxWrite, ex.Message);
                    if (!allowMarkdownFallback) {
                        return TranscriptExportResult.Failed(normalizedFormat, outputPath, docxFailure);
                    }

                    var fallbackPath = ResolveOutputPath(
                        ExportPreferencesContract.FormatMarkdown,
                        title,
                        outputPath,
                        defaultPrefix: "transcript");
                    try {
                        markdownWriter(fallbackPath, safeMarkdown);
                        return TranscriptExportResult.SuccessWithFallback(
                            normalizedFormat,
                            ExportPreferencesContract.FormatMarkdown,
                            fallbackPath,
                            new TranscriptExportFallback(
                                TranscriptExportFallbackKind.Markdown,
                                fallbackPath,
                                docxFailure));
                    } catch (Exception fallbackEx) {
                        return TranscriptExportResult.Failed(
                            normalizedFormat,
                            outputPath,
                            new TranscriptExportFailure(TranscriptExportStage.MarkdownFallbackWrite, fallbackEx.Message),
                            new TranscriptExportFallback(
                                TranscriptExportFallbackKind.Markdown,
                                fallbackPath,
                                docxFailure));
                    }
                }
            default:
                throw new InvalidOperationException("Unsupported transcript export format: " + normalizedFormat);
        }
    }

    internal static string NormalizeTranscriptMarkdownForExport(string markdown) {
        return TranscriptMarkdownPreparation.PrepareTranscriptMarkdownForExport(markdown);
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
