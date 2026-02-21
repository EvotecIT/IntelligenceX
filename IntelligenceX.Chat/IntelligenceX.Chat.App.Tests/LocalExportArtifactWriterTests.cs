using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml.Packaging;
using IntelligenceX.Chat.ExportArtifacts;
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
    /// Ensures DOCX transcript export normalizes compact signal-flow typography artifacts for readability.
    /// </summary>
    [Fact]
    public void ExportTranscript_Docx_NormalizesSignalFlowTypographyArtifacts() {
        const string markdown = """
            # Transcript

            - Signal **Only total count checked, not origin split -> **Why it matters:**external/custom rules can drift or disappear between hosts ->**Next action:**break down `rule_origin` (`builtin` vs `external`) and confirm expected external rules are present.**
            - Signal **Point-in-time snapshot only** -> Why it matters:trend evidence is missing -> Action:collect data every 15 minutes for 24h.
            - TestimoX rules available ****359****
            """;

        var normalizedMarkdown = OfficeImoArtifactWriter.NormalizeTranscriptMarkdownForDocx(markdown);
        Assert.Contains("**Only total count checked, not origin split** -> **Why it matters:** external/custom rules", normalizedMarkdown, StringComparison.Ordinal);
        Assert.Contains("**Next action:** break down", normalizedMarkdown, StringComparison.Ordinal);
        Assert.Contains("Why it matters: trend evidence is missing", normalizedMarkdown, StringComparison.Ordinal);
        Assert.Contains("Action: collect data every 15 minutes for 24h.", normalizedMarkdown, StringComparison.Ordinal);
        Assert.DoesNotContain("****359****", normalizedMarkdown, StringComparison.Ordinal);
        Assert.Contains("**359**", normalizedMarkdown, StringComparison.Ordinal);

        var root = CreateTempDirectory();
        try {
            var docxPath = Path.Combine(root, "transcript-typography-normalized.docx");
            LocalExportArtifactWriter.ExportTranscript(ExportPreferencesContract.FormatDocx, "transcript", markdown, docxPath);
            Assert.True(File.Exists(docxPath));

            using var docx = WordDocument.Load(docxPath, readOnly: true);
            var bodyText = string.Join("\n", docx.Paragraphs.Select(p => p.Text));
            Assert.Contains("Only total count checked, not origin split", bodyText, StringComparison.Ordinal);
            Assert.Contains("external/custom rules can drift or disappear between hosts", bodyText, StringComparison.Ordinal);
            Assert.Contains("359", bodyText, StringComparison.Ordinal);
        } finally {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Ensures clean transcript markdown is returned byte-for-byte when no normalization signal exists.
    /// </summary>
    [Fact]
    public void ExportTranscript_Docx_NormalizationLeavesCleanMarkdownUnchanged() {
        const string markdown = """
            # Transcript

            - Signal **Healthy baseline established** -> **Why it matters:** repeatability is preserved.
            - Next step: collect another sample tomorrow.
            """;

        var normalized = OfficeImoArtifactWriter.NormalizeTranscriptMarkdownForDocx(markdown);

        Assert.Equal(markdown, normalized);
    }

    /// <summary>
    /// Ensures malformed transcript normalization is idempotent after the first repair pass.
    /// </summary>
    [Fact]
    public void ExportTranscript_Docx_NormalizationIsIdempotentForMalformedSignalFlow() {
        const string markdown = """
            # Transcript

            - Signal **Only total count checked, not origin split -> **Why it matters:**external/custom rules can drift or disappear between hosts ->**Next action:**break down `rule_origin` (`builtin` vs `external`) and confirm expected external rules are present.**
            - TestimoX rules available ****359****
            """;

        var once = OfficeImoArtifactWriter.NormalizeTranscriptMarkdownForDocx(markdown);
        var twice = OfficeImoArtifactWriter.NormalizeTranscriptMarkdownForDocx(once);

        Assert.Equal(once, twice);
    }

    /// <summary>
    /// Ensures large adversarial transcript inputs do not trigger regex timeout failures.
    /// </summary>
    [Fact]
    public void ExportTranscript_Docx_NormalizationHandlesLargeAdversarialInput() {
        var repeatedStrongChunk = string.Concat(Enumerable.Repeat("**A**", 2200));
        var longStarRun = new string('*', 8000);
        var longTail = new string('x', 9000);
        var markdown =
            "# Transcript\n\n"
            + "- Signal **" + repeatedStrongChunk + " -> **Why it matters:**tight spacing persists -> **Action:**normalize quickly.**\n"
            + "- Signal **Probe " + longStarRun + " -> **Next action:**" + longTail + "**\n";

        var exception = Record.Exception(() => OfficeImoArtifactWriter.NormalizeTranscriptMarkdownForDocx(markdown));

        Assert.Null(exception);
    }

    /// <summary>
    /// Ensures local DOCX transcript export leaves visual fences as code when runtime materialization is not present.
    /// </summary>
    [Fact]
    public void ExportTranscript_Docx_LeavesVisualFencesAsCodeWhenNoRuntimeMaterialization() {
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
            Assert.Contains("flowchart LR", bodyText, StringComparison.Ordinal);
            Assert.Contains("\"datasets\"", bodyText, StringComparison.Ordinal);
            Assert.Contains("memberOf", bodyText, StringComparison.Ordinal);

            var imageCount = CountMainDocumentImageParts(docxPath);
            Assert.Equal(0, imageCount);
        } finally {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Ensures DOCX transcript export still embeds allow-listed local images after fallback removal.
    /// </summary>
    [Fact]
    public void WriteDocxTranscript_EmbedsAllowListedLocalImage() {
        var root = CreateTempDirectory();
        try {
            var imagesDirectory = Path.Combine(root, "images");
            Directory.CreateDirectory(imagesDirectory);
            var imagePath = Path.Combine(imagesDirectory, "dot.png");
            var imageBytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAANSURBVBhXY2Bg+P8fAAMCAf/Jsq3uAAAAAElFTkSuQmCC");
            File.WriteAllBytes(imagePath, imageBytes);

            var markdownImagePath = imagePath.Replace('\\', '/');
            var markdown = "# Transcript\n\n![dot](" + markdownImagePath + ")";
            var docxPath = Path.Combine(root, "transcript-allowlisted-image.docx");

            OfficeImoArtifactWriter.WriteDocxTranscript("transcript", markdown, docxPath, new[] { imagesDirectory });
            Assert.True(File.Exists(docxPath));

            var imageCount = CountMainDocumentImageParts(docxPath);
            Assert.Equal(1, imageCount);
        } finally {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Ensures DOCX transcript export does not embed local images outside allow-listed directories.
    /// </summary>
    [Fact]
    public void WriteDocxTranscript_DoesNotEmbedDisallowedLocalImage() {
        var root = CreateTempDirectory();
        try {
            var disallowedDirectory = Path.Combine(root, "disallowed");
            Directory.CreateDirectory(disallowedDirectory);
            var imagePath = Path.Combine(disallowedDirectory, "dot.png");
            var imageBytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAANSURBVBhXY2Bg+P8fAAMCAf/Jsq3uAAAAAElFTkSuQmCC");
            File.WriteAllBytes(imagePath, imageBytes);

            var allowedDirectory = Path.Combine(root, "allowed");
            Directory.CreateDirectory(allowedDirectory);

            var markdownImagePath = imagePath.Replace('\\', '/');
            var markdown = "# Transcript\n\n![blocked-image](" + markdownImagePath + ")";
            var docxPath = Path.Combine(root, "transcript-disallowed-image.docx");

            OfficeImoArtifactWriter.WriteDocxTranscript("transcript", markdown, docxPath, new[] { allowedDirectory });
            Assert.True(File.Exists(docxPath));

            using var docx = WordDocument.Load(docxPath, readOnly: true);
            var bodyText = string.Join("\n", docx.Paragraphs.Select(p => p.Text));
            Assert.Contains("blocked-image", bodyText, StringComparison.Ordinal);

            var imageCount = CountMainDocumentImageParts(docxPath);
            Assert.Equal(0, imageCount);
        } finally {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Ensures single-line "Label: value" content is rendered as narrative text, not definition-list separator runs.
    /// </summary>
    [Fact]
    public void WriteDocxTranscript_RendersSingleLineDefinitionsAsNarrativeText() {
        const string markdown = """
            # Transcript

            Status: **healthy**
            """;

        var root = CreateTempDirectory();
        try {
            var docxPath = Path.Combine(root, "transcript-single-line-definition.docx");
            OfficeImoArtifactWriter.WriteDocxTranscript("transcript", markdown, docxPath, additionalAllowedImageDirectories: null);
            Assert.True(File.Exists(docxPath));

            using var docx = WordDocument.Load(docxPath, readOnly: true);
            var paragraph = docx.Paragraphs.First(p => p.Text.Contains("Status:", StringComparison.Ordinal));
            var runTexts = paragraph.GetRuns().Select(run => run.Text ?? string.Empty).ToArray();

            Assert.Contains(runTexts, text => text.Contains("Status: ", StringComparison.Ordinal));
            Assert.DoesNotContain(runTexts, text => string.Equals(text, "Status", StringComparison.Ordinal));
            Assert.DoesNotContain(runTexts, text => string.Equals(text, ": ", StringComparison.Ordinal));
        } finally {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Ensures DOCX visual fence materialization uses caller-provided width hint for generated markdown images.
    /// </summary>
    [Fact]
    public void DocxVisualFenceMaterializer_UsesConfiguredWidthHint() {
        const string markdown = """
            ```mermaid
            flowchart LR
            A --> B
            ```
            """;

        using var materialized = DocxVisualFenceMaterializer.Materialize(markdown, 980);

        Assert.Contains("{width=980}", materialized.Markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("```mermaid", materialized.Markdown, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures DOCX visual fence materialization clamps width hints to supported bounds.
    /// </summary>
    [Theory]
    [InlineData(10, 320)]
    [InlineData(760, 760)]
    [InlineData(6000, 2000)]
    public void DocxVisualFenceMaterializer_ClampsWidthHint(int requestedWidth, int expectedWidth) {
        const string markdown = """
            ```mermaid
            graph TD
            X --> Y
            ```
            """;

        using var materialized = DocxVisualFenceMaterializer.Materialize(markdown, requestedWidth);

        Assert.Contains("{width=" + expectedWidth + "}", materialized.Markdown, StringComparison.Ordinal);
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
