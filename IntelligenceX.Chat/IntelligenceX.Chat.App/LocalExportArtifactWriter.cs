using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using S = DocumentFormat.OpenXml.Spreadsheet;
using W = DocumentFormat.OpenXml.Wordprocessing;

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
                WriteXlsx(title, rows, outputPath);
                break;
            case ExportPreferencesContract.FormatDocx:
                WriteDocxTable(title, rows, outputPath);
                break;
            default:
                throw new InvalidOperationException("Unsupported export format: " + normalizedFormat);
        }
    }

    public static void ExportTranscript(string format, string title, string markdown, string outputPath) {
        var normalizedFormat = (format ?? string.Empty).Trim().ToLowerInvariant();
        switch (normalizedFormat) {
            case "md":
                File.WriteAllText(outputPath, markdown ?? string.Empty, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
                break;
            case ExportPreferencesContract.FormatDocx:
                WriteDocxTranscript(title, markdown ?? string.Empty, outputPath);
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

    private static void WriteXlsx(string title, IReadOnlyList<string[]> rows, string outputPath) {
        using var document = SpreadsheetDocument.Create(outputPath, SpreadsheetDocumentType.Workbook);
        var workbookPart = document.AddWorkbookPart();
        workbookPart.Workbook = new S.Workbook();

        var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        var sheetData = new S.SheetData();
        worksheetPart.Worksheet = new S.Worksheet(sheetData);

        for (int r = 0; r < rows.Count; r++) {
            var rowIndex = (uint)(r + 1);
            var row = new S.Row { RowIndex = rowIndex };
            var values = rows[r];
            for (int c = 0; c < values.Length; c++) {
                row.Append(CreateSpreadsheetTextCell(c + 1, rowIndex, values[c] ?? string.Empty));
            }
            sheetData.Append(row);
        }

        var sheets = workbookPart.Workbook.AppendChild(new S.Sheets());
        var sheetName = SanitizeSheetName(title);
        sheets.Append(new S.Sheet {
            Id = workbookPart.GetIdOfPart(worksheetPart),
            SheetId = 1U,
            Name = sheetName
        });

        workbookPart.Workbook.Save();
    }

    private static S.Cell CreateSpreadsheetTextCell(int columnIndexOneBased, uint rowIndex, string value) {
        return new S.Cell {
            CellReference = BuildSpreadsheetCellReference(columnIndexOneBased, rowIndex),
            DataType = S.CellValues.InlineString,
            InlineString = new S.InlineString(new S.Text(value) { Space = SpaceProcessingModeValues.Preserve })
        };
    }

    private static string BuildSpreadsheetCellReference(int columnIndexOneBased, uint rowIndex) {
        return BuildSpreadsheetColumnName(columnIndexOneBased) + rowIndex.ToString();
    }

    private static string BuildSpreadsheetColumnName(int columnIndexOneBased) {
        if (columnIndexOneBased < 1) {
            throw new ArgumentOutOfRangeException(nameof(columnIndexOneBased));
        }

        var columnName = string.Empty;
        var column = columnIndexOneBased;
        while (column > 0) {
            var remainder = (column - 1) % 26;
            columnName = (char)('A' + remainder) + columnName;
            column = (column - 1) / 26;
        }
        return columnName;
    }

    private static void WriteDocxTable(string title, IReadOnlyList<string[]> rows, string outputPath) {
        using var document = WordprocessingDocument.Create(outputPath, WordprocessingDocumentType.Document);
        var mainPart = document.AddMainDocumentPart();
        mainPart.Document = new W.Document(new W.Body());

        var body = mainPart.Document.Body!;
        body.Append(CreateHeadingParagraph(string.IsNullOrWhiteSpace(title) ? "Data Export" : title.Trim()));
        body.Append(new W.Paragraph(new W.Run(new W.Text(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")))));

        var table = new W.Table();
        table.AppendChild(new W.TableProperties(
            new W.TableStyle { Val = "TableGrid" },
            new W.TableWidth { Width = "0", Type = W.TableWidthUnitValues.Auto },
            new W.TableBorders(
                new W.TopBorder { Val = W.BorderValues.Single, Size = 8 },
                new W.BottomBorder { Val = W.BorderValues.Single, Size = 8 },
                new W.LeftBorder { Val = W.BorderValues.Single, Size = 8 },
                new W.RightBorder { Val = W.BorderValues.Single, Size = 8 },
                new W.InsideHorizontalBorder { Val = W.BorderValues.Single, Size = 6 },
                new W.InsideVerticalBorder { Val = W.BorderValues.Single, Size = 6 })));

        for (int r = 0; r < rows.Count; r++) {
            var row = new W.TableRow();
            var values = rows[r];
            for (int c = 0; c < values.Length; c++) {
                row.Append(CreateTableCell(values[c] ?? string.Empty, bold: r == 0));
            }
            table.Append(row);
        }

        body.Append(table);
        mainPart.Document.Save();
    }

    private static void WriteDocxTranscript(string title, string markdown, string outputPath) {
        using var document = WordprocessingDocument.Create(outputPath, WordprocessingDocumentType.Document);
        var mainPart = document.AddMainDocumentPart();
        mainPart.Document = new W.Document(new W.Body());

        var body = mainPart.Document.Body!;
        body.Append(CreateHeadingParagraph(string.IsNullOrWhiteSpace(title) ? "Transcript" : title.Trim()));
        body.Append(new W.Paragraph(new W.Run(new W.Text("Source: Markdown"))));
        body.Append(new W.Paragraph());

        var normalized = (markdown ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n');
        for (int i = 0; i < lines.Length; i++) {
            var line = lines[i];
            if (line.Length == 0) {
                body.Append(new W.Paragraph());
                continue;
            }

            body.Append(new W.Paragraph(new W.Run(new W.Text(line) { Space = SpaceProcessingModeValues.Preserve })));
        }

        mainPart.Document.Save();
    }

    private static W.Paragraph CreateHeadingParagraph(string text) {
        return new W.Paragraph(
            new W.ParagraphProperties(new W.ParagraphStyleId { Val = "Heading1" }),
            new W.Run(new W.Text(text) { Space = SpaceProcessingModeValues.Preserve }));
    }

    private static W.TableCell CreateTableCell(string value, bool bold) {
        var run = new W.Run(new W.Text(value) { Space = SpaceProcessingModeValues.Preserve });
        if (bold) {
            run.RunProperties = new W.RunProperties(new W.Bold());
        }

        var paragraph = new W.Paragraph(run);
        return new W.TableCell(
            paragraph,
            new W.TableCellProperties(new W.TableCellVerticalAlignment { Val = W.TableVerticalAlignmentValues.Top }));
    }

    private static string GetFileExtension(string format) {
        return (format ?? string.Empty).Trim().ToLowerInvariant() switch {
            ExportPreferencesContract.FormatXlsx => ".xlsx",
            ExportPreferencesContract.FormatDocx => ".docx",
            "md" => ".md",
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

    private static string SanitizeSheetName(string title) {
        var raw = (title ?? string.Empty).Trim();
        if (raw.Length == 0) {
            raw = "Sheet1";
        }

        raw = raw
            .Replace(":", string.Empty, StringComparison.Ordinal)
            .Replace("\\", string.Empty, StringComparison.Ordinal)
            .Replace("/", string.Empty, StringComparison.Ordinal)
            .Replace("?", string.Empty, StringComparison.Ordinal)
            .Replace("*", string.Empty, StringComparison.Ordinal)
            .Replace("[", string.Empty, StringComparison.Ordinal)
            .Replace("]", string.Empty, StringComparison.Ordinal);

        if (raw.Length > 31) {
            raw = raw[..31];
        }

        return raw.Length == 0 ? "Sheet1" : raw;
    }
}
