using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using IntelligenceX.Chat.App.Markdown;
using IntelligenceX.Chat.ExportArtifacts;
using System.Text.Json;
using OfficeIMO.Excel;
using OfficeIMO.Word;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Verifies local export fallback writers used by the chat desktop app.
/// </summary>
public sealed partial class LocalExportArtifactWriterTests {
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

        var root = TempPathTestHelper.CreateTempDirectory("ixchat-tests");
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

        var root = TempPathTestHelper.CreateTempDirectory("ixchat-tests");
        try {
            var markdownPath = Path.Combine(root, "transcript.md");
            var markdownResult = LocalExportArtifactWriter.ExportTranscript("md", "transcript", markdown, markdownPath);
            Assert.True(markdownResult.Succeeded);
            Assert.Equal(TranscriptExportOutcomeKind.Succeeded, markdownResult.OutcomeKind);
            Assert.Equal(ExportPreferencesContract.FormatMarkdown, markdownResult.ActualFormat);
            Assert.Equal(markdownPath, markdownResult.OutputPath);
            Assert.True(File.Exists(markdownPath));
            Assert.Contains("# Transcript", File.ReadAllText(markdownPath));

            var docxPath = Path.Combine(root, "transcript.docx");
            var docxResult = LocalExportArtifactWriter.ExportTranscript(ExportPreferencesContract.FormatDocx, "transcript", markdown, docxPath);
            Assert.True(docxResult.Succeeded);
            Assert.Equal(TranscriptExportOutcomeKind.Succeeded, docxResult.OutcomeKind);
            Assert.Equal(ExportPreferencesContract.FormatDocx, docxResult.ActualFormat);
            Assert.Equal(docxPath, docxResult.OutputPath);
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
    /// Ensures markdown transcript export strips internal cached-evidence transport markers.
    /// </summary>
    [Fact]
    public void ExportTranscript_Markdown_StripsInternalCachedEvidenceMarkers() {
        const string markdown = """
            # Transcript

            [Cached evidence fallback]
            ix:cached-tool-evidence:v1

            Recent evidence:
            #### ad_environment_discover
            ### Active Directory: Environment Discovery
            """;

        var root = TempPathTestHelper.CreateTempDirectory("ixchat-tests");
        try {
            var markdownPath = Path.Combine(root, "transcript.md");
            var result = LocalExportArtifactWriter.ExportTranscript("md", "transcript", markdown, markdownPath);

            Assert.True(result.Succeeded);
            var written = File.ReadAllText(markdownPath);
            Assert.DoesNotContain("ix:cached-tool-evidence:v1", written, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("#### ad_environment_discover", written, StringComparison.Ordinal);
            Assert.Contains("Cached evidence fallback", written, StringComparison.Ordinal);
            Assert.Contains("Active Directory: Environment Discovery", written, StringComparison.Ordinal);
        } finally {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Ensures DOCX transcript export receives sanitized markdown without internal cached-evidence transport markers.
    /// </summary>
    [Fact]
    public void ExportTranscript_Docx_SanitizesCachedEvidenceMarkersBeforeWriter() {
        const string markdown = """
            # Transcript

            [Cached evidence fallback]
            ix:cached-tool-evidence:v1

            Recent evidence:
            #### ad_environment_discover
            ### Active Directory: Environment Discovery
            """;

        string? capturedMarkdown = null;
        var root = TempPathTestHelper.CreateTempDirectory("ixchat-tests");
        try {
            var docxPath = Path.Combine(root, "transcript.docx");
            var result = LocalExportArtifactWriter.ExportTranscript(
                ExportPreferencesContract.FormatDocx,
                "transcript",
                markdown,
                docxPath,
                additionalAllowedImageDirectories: null,
                docxVisualMaxWidthPx: null,
                allowMarkdownFallback: false,
                markdownWriter: static (_, _) => throw new InvalidOperationException("markdown fallback should not run"),
                docxWriter: (_, docxMarkdown, _, _, _) => capturedMarkdown = docxMarkdown);

            Assert.True(result.Succeeded);
            Assert.NotNull(capturedMarkdown);
            Assert.DoesNotContain("ix:cached-tool-evidence:v1", capturedMarkdown, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("#### ad_environment_discover", capturedMarkdown, StringComparison.Ordinal);
            Assert.Contains("Cached evidence fallback", capturedMarkdown, StringComparison.Ordinal);
        } finally {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Ensures export applies the shared IX transcript contract to legacy cached-evidence headings and overwrapped strong spans.
    /// </summary>
    [Fact]
    public void ExportTranscript_Markdown_NormalizesLegacyHistoryArtifacts() {
        const string markdown = """
            # Transcript

            [Cached evidence fallback]
            ix:cached-tool-evidence:v1

            Recent evidence:
            - eventlog_top_events: ### Top 30 recent events (preview)

            ### Forest replication health
            - Overall health ****healthy****
            """;

        var root = TempPathTestHelper.CreateTempDirectory("ixchat-tests");
        try {
            var markdownPath = Path.Combine(root, "transcript.md");
            var result = LocalExportArtifactWriter.ExportTranscript("md", "transcript", markdown, markdownPath);

            Assert.True(result.Succeeded);
            var written = File.ReadAllText(markdownPath);
            Assert.DoesNotContain("ix:cached-tool-evidence:v1", written, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("- eventlog_top_events:", written, StringComparison.Ordinal);
            Assert.Contains("Top 30 recent events", written, StringComparison.Ordinal);
            Assert.Contains("- Overall health **healthy**", written, StringComparison.Ordinal);
        } finally {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Ensures markdown transcript export prefers portable generic visual fence languages for downstream consumers.
    /// </summary>
    [Fact]
    public void ExportTranscript_Markdown_UsesPortableGenericVisualFenceLanguages() {
        const string markdown = """
            # Transcript

            ix:cached-tool-evidence:v1

            ```json
            {"type":"bar","data":{"labels":["A"],"datasets":[{"label":"Count","data":[1]}]}}
            ```

            ```json
            {"nodes":[{"id":"A","label":"Forest: ad.evotec.xyz"}],"edges":[{"source":"A","target":"B","label":"contains"}]}
            ```
            """;

        var root = TempPathTestHelper.CreateTempDirectory("ixchat-tests");
        try {
            var markdownPath = Path.Combine(root, "transcript.md");
            var result = LocalExportArtifactWriter.ExportTranscript("md", "transcript", markdown, markdownPath);

            Assert.True(result.Succeeded);
            var written = File.ReadAllText(markdownPath);
            Assert.DoesNotContain("ix:cached-tool-evidence:v1", written, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("```chart", written, StringComparison.Ordinal);
            Assert.Contains("```network", written, StringComparison.Ordinal);
            Assert.DoesNotContain("```ix-chart", written, StringComparison.Ordinal);
            Assert.DoesNotContain("```ix-network", written, StringComparison.Ordinal);
        } finally {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Ensures markdown transcript export writes a stable portable artifact for downstream markdown/crawl consumers.
    /// </summary>
    [Fact]
    public void ExportTranscript_Markdown_PortableArtifactMatchesExpectedSnapshot() {
        var markdown = CreatePortableVisualExportMarkdown();

        var root = TempPathTestHelper.CreateTempDirectory("ixchat-tests");
        try {
            var markdownPath = Path.Combine(root, "transcript.md");
            var result = LocalExportArtifactWriter.ExportTranscript("md", "transcript", markdown, markdownPath);

            Assert.True(result.Succeeded);
            Assert.Equal(
                NormalizeSnapshotText(LoadExpectedSnapshot("local-export-portable-transcript-markdown.snapshot.md")),
                NormalizeSnapshotText(File.ReadAllText(markdownPath, Encoding.UTF8)));
        } finally {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Ensures successful DOCX transcript export stays on the IX compatibility lane for the markdown handed to the DOCX writer.
    /// </summary>
    [Fact]
    public void ExportTranscript_Docx_PassesIxCompatibilityMarkdownToWriter() {
        var markdown = CreatePortableVisualExportMarkdown();

        string? capturedMarkdown = null;
        var root = TempPathTestHelper.CreateTempDirectory("ixchat-tests");
        try {
            var docxPath = Path.Combine(root, "transcript.docx");
            var result = LocalExportArtifactWriter.ExportTranscript(
                ExportPreferencesContract.FormatDocx,
                "transcript",
                markdown,
                docxPath,
                additionalAllowedImageDirectories: null,
                docxVisualMaxWidthPx: null,
                allowMarkdownFallback: false,
                markdownWriter: static (_, _) => throw new InvalidOperationException("markdown fallback should not run"),
                docxWriter: (_, docxMarkdown, _, _, _) => capturedMarkdown = docxMarkdown);

            Assert.True(result.Succeeded);
            Assert.NotNull(capturedMarkdown);
            Assert.Equal(
                NormalizeSnapshotText(TranscriptMarkdownPreparation.PrepareTranscriptMarkdownForExport(markdown)),
                NormalizeSnapshotText(capturedMarkdown));
            Assert.Contains("```ix-chart", capturedMarkdown, StringComparison.Ordinal);
            Assert.Contains("```ix-network", capturedMarkdown, StringComparison.Ordinal);
            Assert.Contains("```ix-dataview", capturedMarkdown, StringComparison.Ordinal);
            Assert.DoesNotContain("```chart", capturedMarkdown, StringComparison.Ordinal);
            Assert.DoesNotContain("```network", capturedMarkdown, StringComparison.Ordinal);
            Assert.DoesNotContain("```dataview", capturedMarkdown, StringComparison.Ordinal);
        } finally {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Ensures full-transcript export normalization preserves the shared adjacent ordered-list spacing repair.
    /// </summary>
    [Fact]
    public void NormalizeTranscriptMarkdownForExport_InsertsBlankLineBetweenAdjacentOrderedItems() {
        const string markdown = """
            ### Assistant (10:22:14)
            1. First check
            2. Second check
            """;

        var normalized = TranscriptMarkdownPreparation.PrepareTranscriptMarkdownForExport(markdown)
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains("1. First check\n\n2. Second check", normalized, StringComparison.Ordinal);
    }

    /// <summary>
    /// Ensures requested DOCX transcript export falls back to the same portable markdown artifact contract when the DOCX writer fails.
    /// </summary>
    [Fact]
    public void ExportTranscript_Docx_FallsBackToMarkdown_WhenDocxWriterFails() {
        var markdown = CreatePortableVisualExportMarkdown();

        var root = TempPathTestHelper.CreateTempDirectory("ixchat-tests");
        try {
            var requestedDocxPath = Path.Combine(root, "transcript.docx");
            var result = LocalExportArtifactWriter.ExportTranscript(
                ExportPreferencesContract.FormatDocx,
                "transcript",
                markdown,
                requestedDocxPath,
                additionalAllowedImageDirectories: null,
                docxVisualMaxWidthPx: null,
                allowMarkdownFallback: true,
                markdownWriter: static (path, text) => File.WriteAllText(path, text),
                docxWriter: static (_, _, _, _, _) => throw new InvalidOperationException("docx write boom"));

            var fallbackPath = Path.Combine(root, "transcript.md");
            Assert.True(result.Succeeded);
            Assert.Equal(TranscriptExportOutcomeKind.SucceededWithFallback, result.OutcomeKind);
            Assert.Equal(ExportPreferencesContract.FormatDocx, result.RequestedFormat);
            Assert.Equal(ExportPreferencesContract.FormatMarkdown, result.ActualFormat);
            Assert.Equal(fallbackPath, result.OutputPath);
            Assert.Equal(TranscriptExportFallbackKind.Markdown, result.Fallback?.Kind);
            Assert.Equal(fallbackPath, result.Fallback?.OutputPath);
            Assert.Equal(TranscriptExportStage.DocxWrite, result.Fallback?.Cause.Stage);
            Assert.Contains("docx write boom", result.Fallback?.Cause.Message, StringComparison.Ordinal);
            Assert.True(File.Exists(fallbackPath));
            Assert.False(File.Exists(requestedDocxPath));
            Assert.Equal(
                NormalizeSnapshotText(LoadExpectedSnapshot("local-export-portable-transcript-markdown.snapshot.md")),
                NormalizeSnapshotText(File.ReadAllText(fallbackPath, Encoding.UTF8)));
        } finally {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Ensures the typed result stays failed when DOCX export is retried later and markdown fallback is intentionally disabled.
    /// </summary>
    [Fact]
    public void ExportTranscript_Docx_ReturnsFailedResult_WhenFallbackIsDisabled() {
        var root = TempPathTestHelper.CreateTempDirectory("ixchat-tests");
        try {
            var requestedDocxPath = Path.Combine(root, "transcript.docx");
            var result = LocalExportArtifactWriter.ExportTranscript(
                ExportPreferencesContract.FormatDocx,
                "transcript",
                "# Transcript",
                requestedDocxPath,
                additionalAllowedImageDirectories: null,
                docxVisualMaxWidthPx: null,
                allowMarkdownFallback: false,
                markdownWriter: static (_, _) => throw new InvalidOperationException("markdown fallback should not run"),
                docxWriter: static (_, _, _, _, _) => throw new InvalidOperationException("docx write boom"));

            Assert.False(result.Succeeded);
            Assert.Equal(TranscriptExportOutcomeKind.Failed, result.OutcomeKind);
            Assert.Equal(TranscriptExportStage.DocxWrite, result.Failure?.Stage);
            Assert.Contains("docx write boom", result.Failure?.Message, StringComparison.Ordinal);
            Assert.Null(result.Fallback);
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

        var root = TempPathTestHelper.CreateTempDirectory("ixchat-tests");
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

        var root = TempPathTestHelper.CreateTempDirectory("ixchat-tests");
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

        var root = TempPathTestHelper.CreateTempDirectory("ixchat-tests");
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

        var root = TempPathTestHelper.CreateTempDirectory("ixchat-tests");
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

        var root = TempPathTestHelper.CreateTempDirectory("ixchat-tests");
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

        var root = TempPathTestHelper.CreateTempDirectory("ixchat-tests");
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

            Assert.Empty(docx.Images);
        } finally {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string LoadExpectedSnapshot(string name) {
        return File.ReadAllText(Path.Combine(GetTestsProjectRoot(), "Fixtures", "Expected", name), Encoding.UTF8);
    }

    private static string CreatePortableVisualExportMarkdown() {
        return """
            # Transcript

            [Cached evidence fallback]
            ix:cached-tool-evidence:v1

            Recent evidence:
            - eventlog_top_events: ### Top 30 recent events (preview)

            Chart preview:
            ```json
            {"type":"bar","data":{"labels":["A"],"datasets":[{"label":"Count","data":[1]}]}}
            ```

            Network preview:
            ```json
            {"nodes":[{"id":"A","label":"Forest: ad.evotec.xyz"}],"edges":[{"source":"A","target":"B","label":"contains"}]}
            ```

            Dataview preview:
            ```json
            {"kind":"ix_tool_dataview_v1","rows":[["Server","Fails"],["AD0","0"]]}
            ```
            """;
    }

    private static string GetTestsProjectRoot() {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null) {
            var candidate = Path.Combine(dir.FullName, "IntelligenceX.Chat.App.Tests.csproj");
            if (File.Exists(candidate)) {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate IntelligenceX.Chat.App.Tests project root from test runtime base directory.");
    }

    private static string NormalizeSnapshotText(string text) {
        return (text ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
    }
}
