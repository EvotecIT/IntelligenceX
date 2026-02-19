using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml.Packaging;
using System.Text.Json;
using OfficeIMO.Excel;
using OfficeIMO.Word;
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
            using (var workbook = ExcelDocument.Load(xlsxPath, readOnly: true)) {
                var sheet = workbook[0];
                Assert.True(sheet.TryGetCellText(1, 1, out var header));
                Assert.Equal("Name", header);
            }

            var docxPath = Path.Combine(root, "table.docx");
            LocalExportArtifactWriter.ExportTable(ExportPreferencesContract.FormatDocx, "sample", rows, docxPath);
            Assert.True(File.Exists(docxPath));
            using (var docx = WordDocument.Load(docxPath, readOnly: true)) {
                var table = docx.Tables.First();
                var headerTexts = table.Rows[0]
                    .Cells
                    .SelectMany(cell => cell.Paragraphs)
                    .Select(paragraph => paragraph.Text)
                    .ToArray();

                Assert.Contains("Name", headerTexts);
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
            using var docx = WordDocument.Load(docxPath, readOnly: true);
            var bodyText = string.Join("\n", docx.Paragraphs.Select(p => p.Text));
            Assert.Contains("Transcript", bodyText);
            Assert.Contains("item 1", bodyText);
        } finally {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Ensures transcript DOCX export does not leak literal strong delimiters on definition-like lines.
    /// </summary>
    [Fact]
    public void ExportTranscript_Docx_EscapesDefinitionLikeLinesBeforeConversion() {
        const string markdown = """
            # Transcript

            ### Assistant (10:15:54)
            Short answer: **no — nothing is failed** ✅
            """;

        var root = CreateTempDirectory();
        try {
            var docxPath = Path.Combine(root, "transcript-definition-line.docx");
            LocalExportArtifactWriter.ExportTranscript(ExportPreferencesContract.FormatDocx, "transcript", markdown, docxPath);
            Assert.True(File.Exists(docxPath));

            using var docx = WordDocument.Load(docxPath, readOnly: true);
            var paragraph = docx.Paragraphs.First(p => p.Text.Contains("Short answer", StringComparison.Ordinal));
            var combinedRuns = string.Concat(paragraph.GetRuns().Select(run => run.Text));

            Assert.DoesNotContain("**", combinedRuns, StringComparison.Ordinal);
            Assert.Contains("no — nothing is failed", combinedRuns, StringComparison.Ordinal);
        } finally {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Ensures fence content is not altered by the transcript DOCX pre-pass.
    /// </summary>
    [Fact]
    public void ExportTranscript_Docx_DoesNotEscapeDefinitionLikeLinesInsideCodeFence() {
        const string markdown = """
            # Transcript

            ```markdown
            Short answer: **no — nothing is failed** ✅
            ```

            Short answer: **outside fence still bold** ✅
            """;

        var root = CreateTempDirectory();
        try {
            var docxPath = Path.Combine(root, "transcript-definition-fence.docx");
            LocalExportArtifactWriter.ExportTranscript(ExportPreferencesContract.FormatDocx, "transcript", markdown, docxPath);
            Assert.True(File.Exists(docxPath));

            using var docx = WordDocument.Load(docxPath, readOnly: true);
            var bodyText = string.Join("\n", docx.Paragraphs.Select(p => p.Text));

            Assert.Contains("Short answer: **no — nothing is failed** ✅", bodyText, StringComparison.Ordinal);
            Assert.DoesNotContain("Short answer\\:", bodyText, StringComparison.Ordinal);
            Assert.Contains("outside fence still bold", bodyText, StringComparison.Ordinal);
        } finally {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Ensures longer outer fences are not closed by inner shorter fence markers during DOCX pre-pass.
    /// </summary>
    [Fact]
    public void ExportTranscript_Docx_PreservesContentInsideLongerOuterFence() {
        const string markdown = """
            # Transcript

            ````markdown
            ```powershell
            key: value
            ```
            ````
            """;

        var root = CreateTempDirectory();
        try {
            var docxPath = Path.Combine(root, "transcript-outer-fence.docx");
            LocalExportArtifactWriter.ExportTranscript(ExportPreferencesContract.FormatDocx, "transcript", markdown, docxPath);
            Assert.True(File.Exists(docxPath));

            using var docx = WordDocument.Load(docxPath, readOnly: true);
            var bodyText = string.Join("\n", docx.Paragraphs.Select(p => p.Text));

            Assert.Contains("key", bodyText, StringComparison.Ordinal);
            Assert.Contains("value", bodyText, StringComparison.Ordinal);
            Assert.DoesNotContain("key\\: value", bodyText, StringComparison.Ordinal);
        } finally {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Ensures separator detection ignores colon-space sequences inside inline code spans.
    /// </summary>
    [Fact]
    public void ExportTranscript_Docx_DoesNotEscapeSeparatorInsideInlineCode() {
        const string markdown = """
            # Transcript

            Use `key: value` syntax when defining pairs.
            """;

        var root = CreateTempDirectory();
        try {
            var docxPath = Path.Combine(root, "transcript-inline-code.docx");
            LocalExportArtifactWriter.ExportTranscript(ExportPreferencesContract.FormatDocx, "transcript", markdown, docxPath);
            Assert.True(File.Exists(docxPath));

            using var docx = WordDocument.Load(docxPath, readOnly: true);
            var bodyText = string.Join("\n", docx.Paragraphs.Select(p => p.Text));

            Assert.Contains("key", bodyText, StringComparison.Ordinal);
            Assert.Contains("value", bodyText, StringComparison.Ordinal);
            Assert.DoesNotContain("key\\: value", bodyText, StringComparison.Ordinal);
        } finally {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Ensures DOCX transcript export materializes supported visual fences into embedded images.
    /// </summary>
    [Fact]
    public void ExportTranscript_Docx_MaterializesVisualFencesIntoImages() {
        const string markdown = """
            # Transcript

            Mermaid snapshot:
            ```mermaid
            flowchart LR
            A[User] --> B[Group]
            ```
            Interpretation line.

            Chart snapshot:
            ```ix-chart
            {"type":"bar","data":{"labels":["A","B"],"datasets":[{"label":"Count","data":[3,5]}]}}
            ```
            Interpretation line.

            Network snapshot:
            ```ix-network
            {"nodes":[{"id":"A","label":"User"},{"id":"B","label":"Group"}],"edges":[{"from":"A","to":"B","label":"memberOf"}]}
            ```
            Interpretation line.
            """;

        var root = CreateTempDirectory();
        try {
            var docxPath = Path.Combine(root, "transcript-visuals.docx");
            LocalExportArtifactWriter.ExportTranscript(ExportPreferencesContract.FormatDocx, "transcript", markdown, docxPath);
            Assert.True(File.Exists(docxPath));

            using var docx = WordDocument.Load(docxPath, readOnly: true);
            var bodyText = string.Join("\n", docx.Paragraphs.Select(p => p.Text));
            Assert.Contains("Mermaid snapshot", bodyText, StringComparison.Ordinal);
            Assert.Contains("Chart snapshot", bodyText, StringComparison.Ordinal);
            Assert.Contains("Network snapshot", bodyText, StringComparison.Ordinal);

            var imageCount = CountMainDocumentImageParts(docxPath);
            Assert.True(imageCount >= 3, "Expected at least 3 embedded images, got " + imageCount.ToString());
        } finally {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Ensures invalid visual fences remain as raw code and are not materialized into images.
    /// </summary>
    [Fact]
    public void ExportTranscript_Docx_LeavesInvalidVisualFenceAsCode() {
        const string markdown = """
            # Transcript

            Invalid chart:
            ```ix-chart
            {"type":"bar","data":{"labels":["A"],"datasets":[{"label":"Broken","data":"not-array"}]}}
            ```
            """;

        var root = CreateTempDirectory();
        try {
            var docxPath = Path.Combine(root, "transcript-invalid-visual.docx");
            LocalExportArtifactWriter.ExportTranscript(ExportPreferencesContract.FormatDocx, "transcript", markdown, docxPath);
            Assert.True(File.Exists(docxPath));

            using var docx = WordDocument.Load(docxPath, readOnly: true);
            var bodyText = string.Join("\n", docx.Paragraphs.Select(p => p.Text));
            Assert.Contains("not-array", bodyText, StringComparison.Ordinal);

            var imageCount = CountMainDocumentImageParts(docxPath);
            Assert.Equal(0, imageCount);
        } finally {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTempDirectory() {
        var path = Path.Combine(Path.GetTempPath(), "ixchat-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static int CountMainDocumentImageParts(string docxPath) {
        using var package = WordprocessingDocument.Open(docxPath, false);
        return package.MainDocumentPart?.ImageParts.Count() ?? 0;
    }
}
