using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using S = DocumentFormat.OpenXml.Spreadsheet;
using W = DocumentFormat.OpenXml.Wordprocessing;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Verifies local export fallback writers used by the chat desktop app.
/// </summary>
public sealed class LocalExportArtifactWriterTests {
    /// <summary>
    /// Ensures row parsing normalizes ragged arrays to a rectangular matrix.
    /// </summary>
    [Fact]
    public void TryReadRows_NormalizesRaggedRows() {
        using var doc = JsonDocument.Parse("""
            [
              ["A","B"],
              ["1"],
              ["x","y","z"]
            ]
            """);

        var ok = LocalExportArtifactWriter.TryReadRows(doc.RootElement, out var rows);

        Assert.True(ok);
        Assert.Equal(3, rows.Count);
        Assert.Equal(3, rows[0].Length);
        Assert.Equal("A", rows[0][0]);
        Assert.Equal(string.Empty, rows[1][2]);
        Assert.Equal("z", rows[2][2]);
    }

    /// <summary>
    /// Ensures CSV/XLSX/DOCX writers persist tabular data to disk.
    /// </summary>
    [Fact]
    public void ExportTable_WritesAllFormats() {
        var rows = new List<string[]> {
            new[] { "Name", "Status" },
            new[] { "AD1", "Healthy" },
            new[] { "AD2", "Warning" }
        };

        var root = CreateTempDirectory();
        try {
            var csvPath = Path.Combine(root, "table.csv");
            LocalExportArtifactWriter.ExportTable(ExportPreferencesContract.FormatCsv, "sample", rows, csvPath);
            Assert.True(File.Exists(csvPath));
            Assert.Contains("Name,Status", File.ReadAllText(csvPath));

            var xlsxPath = Path.Combine(root, "table.xlsx");
            LocalExportArtifactWriter.ExportTable(ExportPreferencesContract.FormatXlsx, "sample", rows, xlsxPath);
            Assert.True(File.Exists(xlsxPath));
            using (var sheet = SpreadsheetDocument.Open(xlsxPath, false)) {
                var workbookPart = sheet.WorkbookPart!;
                var firstSheet = workbookPart.Workbook.Sheets!.Elements<S.Sheet>().First();
                var worksheetPart = (WorksheetPart)workbookPart.GetPartById(firstSheet.Id!);
                var firstRow = worksheetPart.Worksheet.GetFirstChild<S.SheetData>()!.Elements<S.Row>().First();
                var firstCell = firstRow.Elements<S.Cell>().First();
                Assert.Equal("Name", ReadSpreadsheetCellText(firstCell));
            }

            var docxPath = Path.Combine(root, "table.docx");
            LocalExportArtifactWriter.ExportTable(ExportPreferencesContract.FormatDocx, "sample", rows, docxPath);
            Assert.True(File.Exists(docxPath));
            using (var docx = WordprocessingDocument.Open(docxPath, false)) {
                var table = docx.MainDocumentPart!.Document.Body!.Elements<W.Table>().First();
                var headerCell = table.Elements<W.TableRow>().First().Elements<W.TableCell>().First();
                Assert.Equal("Name", headerCell.InnerText);
            }
        } finally {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Ensures transcript export supports both markdown and Word formats.
    /// </summary>
    [Fact]
    public void ExportTranscript_WritesMarkdownAndDocx() {
        const string markdown = """
            # Transcript
            - item 1
            - item 2
            """;

        var root = CreateTempDirectory();
        try {
            var markdownPath = Path.Combine(root, "transcript.md");
            LocalExportArtifactWriter.ExportTranscript("md", "transcript", markdown, markdownPath);
            Assert.True(File.Exists(markdownPath));
            Assert.Contains("# Transcript", File.ReadAllText(markdownPath));

            var docxPath = Path.Combine(root, "transcript.docx");
            LocalExportArtifactWriter.ExportTranscript(ExportPreferencesContract.FormatDocx, "transcript", markdown, docxPath);
            Assert.True(File.Exists(docxPath));
            using var docx = WordprocessingDocument.Open(docxPath, false);
            var bodyText = docx.MainDocumentPart!.Document.Body!.InnerText;
            Assert.Contains("Source: Markdown", bodyText);
            Assert.Contains("- item 1", bodyText);
        } finally {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string ReadSpreadsheetCellText(S.Cell cell) {
        if (cell.InlineString?.Text is not null) {
            return cell.InlineString.Text.Text ?? string.Empty;
        }

        if (cell.CellValue is not null) {
            return cell.CellValue.Text ?? string.Empty;
        }

        return cell.InnerText ?? string.Empty;
    }

    private static string CreateTempDirectory() {
        var path = Path.Combine(Path.GetTempPath(), "ixchat-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
