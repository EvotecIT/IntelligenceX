using System;
using System.IO;
using System.Linq;
using IntelligenceX.Chat.ExportArtifacts;
using OfficeIMO.Word;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Verifies DOCX-specific transcript rendering and runtime option behaviors.
/// </summary>
public sealed partial class LocalExportArtifactWriterTests {
    /// <summary>
    /// Ensures DOCX transcript export still embeds allow-listed local images after fallback removal.
    /// </summary>
    [Fact]
    public void WriteDocxTranscript_EmbedsAllowListedLocalImage() {
        var root = TempPathTestHelper.CreateTempDirectory("ixchat-tests");
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

            using var docx = WordDocument.Load(docxPath, readOnly: true);
            Assert.Single(docx.Images);
        } finally {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Ensures DOCX transcript export does not embed local images outside allow-listed directories.
    /// </summary>
    [Fact]
    public void WriteDocxTranscript_DoesNotEmbedDisallowedLocalImage() {
        var root = TempPathTestHelper.CreateTempDirectory("ixchat-tests");
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
            Assert.Empty(docx.Images);
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

        var root = TempPathTestHelper.CreateTempDirectory("ixchat-tests");
        try {
            var docxPath = Path.Combine(root, "transcript-single-line-definition.docx");
            OfficeImoArtifactWriter.WriteDocxTranscript("transcript", markdown, docxPath, additionalAllowedImageDirectories: null);
            Assert.True(File.Exists(docxPath));

            using var docx = WordDocument.Load(docxPath, readOnly: true);
            var bodyText = string.Join("\n", docx.Paragraphs.Select(p => p.Text));
            Assert.Contains("Status", bodyText, StringComparison.Ordinal);
            Assert.Contains("healthy", bodyText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Status\\:", bodyText, StringComparison.Ordinal);
        } finally {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Ensures grouped label-value blocks remain distinct paragraphs instead of collapsing into one narrative line.
    /// </summary>
    [Fact]
    public void WriteDocxTranscript_PreservesGroupedDefinitionLikeBlocks() {
        const string markdown = """
            # Transcript

            Status: healthy
            Impact: none
            """;

        var root = TempPathTestHelper.CreateTempDirectory("ixchat-tests");
        try {
            var docxPath = Path.Combine(root, "transcript-grouped-definition-lines.docx");
            OfficeImoArtifactWriter.WriteDocxTranscript("transcript", markdown, docxPath, additionalAllowedImageDirectories: null);
            Assert.True(File.Exists(docxPath));

            using var docx = WordDocument.Load(docxPath, readOnly: true);
            var bodyParagraphs = docx.Paragraphs
                .Select(p => string.Concat(p.GetRuns().Select(run => run.Text)))
                .Where(text => !string.IsNullOrWhiteSpace(text) && !string.Equals(text, "Transcript", StringComparison.Ordinal))
                .ToList();

            Assert.Contains("Status: healthy", bodyParagraphs);
            Assert.Contains("Impact: none", bodyParagraphs);
        } finally {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Ensures the DOCX runtime contract owns the baseline transcript converter options used by the writer.
    /// </summary>
    [Fact]
    public void CreateTranscriptMarkdownToWordOptions_ConfiguresNarrativeAndImageDefaults() {
        var options = OfficeImoWordMarkdownRuntimeContract.CreateTranscriptMarkdownToWordOptions(
            ["C:\\allowed-a", "C:\\allowed-b"],
            2500);

        Assert.Equal("Calibri", options.FontFamily);
        Assert.True(options.AllowLocalImages);
        Assert.True(options.PreferNarrativeSingleLineDefinitions);
        Assert.Equal(100d, options.MaxImageWidthPercentOfContent);
        Assert.Equal(2000, options.MaxImageWidthPixels);
        Assert.Contains("C:\\allowed-a", options.AllowedImageDirectories);
        Assert.Contains("C:\\allowed-b", options.AllowedImageDirectories);

        var readerOptionsProperty = options.GetType().GetProperty("ReaderOptions");
        if (readerOptionsProperty == null) {
            return;
        }

        var readerOptions = readerOptionsProperty.GetValue(options);
        Assert.NotNull(readerOptions);
        Assert.True((bool?)readerOptions!.GetType().GetProperty("PreferNarrativeSingleLineDefinitions")?.GetValue(readerOptions) ?? false);
        Assert.True((bool?)readerOptions.GetType().GetProperty("Callouts")?.GetValue(readerOptions) ?? false);
        Assert.True((bool?)readerOptions.GetType().GetProperty("DefinitionLists")?.GetValue(readerOptions) ?? false);
        Assert.NotNull(readerOptions.GetType().GetProperty("InputNormalization")?.GetValue(readerOptions));
    }

    /// <summary>
    /// Ensures invalid visual fences remain as raw code and are not materialized into images.
    /// </summary>
    [Fact]
    public void ExportTranscript_Docx_LeavesInvalidVisualFenceAsCode() {
        const string markdown = """
            # Transcript

            Invalid chart:
            ```chart
            {"type":"bar","data":{"labels":["A"],"datasets":[{"label":"Broken","data":"not-array"}]}}
            ```
            """;

        var root = TempPathTestHelper.CreateTempDirectory("ixchat-tests");
        try {
            var docxPath = Path.Combine(root, "transcript-invalid-visual.docx");
            LocalExportArtifactWriter.ExportTranscript(ExportPreferencesContract.FormatDocx, "transcript", markdown, docxPath);
            Assert.True(File.Exists(docxPath));

            using var docx = WordDocument.Load(docxPath, readOnly: true);
            var bodyText = string.Join("\n", docx.Paragraphs.Select(p => p.Text));
            Assert.Contains("not-array", bodyText, StringComparison.Ordinal);
            Assert.Empty(docx.Images);
        } finally {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Ensures plain colon-prefixed narrative lines do not gain visible backslash escapes in DOCX output.
    /// </summary>
    [Fact]
    public void ExportTranscript_Docx_DoesNotEscapePlainInterpretationNarrativeLine() {
        const string markdown = """
            # Transcript

            Interpretation: topology looks clean in this sample.
            """;

        var root = TempPathTestHelper.CreateTempDirectory("ixchat-tests");
        try {
            var docxPath = Path.Combine(root, "transcript-plain-colon-line.docx");
            LocalExportArtifactWriter.ExportTranscript(ExportPreferencesContract.FormatDocx, "transcript", markdown, docxPath);
            Assert.True(File.Exists(docxPath));

            using var docx = WordDocument.Load(docxPath, readOnly: true);
            var bodyText = string.Join("\n", docx.Paragraphs.Select(p => p.Text));
            Assert.Contains("Interpretation", bodyText, StringComparison.Ordinal);
            Assert.Contains("topology looks clean in this sample.", bodyText, StringComparison.Ordinal);
            Assert.DoesNotContain("Interpretation\\:", bodyText, StringComparison.Ordinal);
        } finally {
            Directory.Delete(root, recursive: true);
        }
    }
}
