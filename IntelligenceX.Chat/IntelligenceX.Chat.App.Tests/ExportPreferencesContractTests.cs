using System;
using System.IO;
using IntelligenceX.Chat.App;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Tests normalization rules for persisted export preferences.
/// </summary>
public sealed class ExportPreferencesContractTests {
    /// <summary>
    /// Ensures save-mode normalization accepts known aliases and defaults safely.
    /// </summary>
    [Theory]
    [InlineData(null, ExportPreferencesContract.SaveModeAsk)]
    [InlineData("", ExportPreferencesContract.SaveModeAsk)]
    [InlineData("ask", ExportPreferencesContract.SaveModeAsk)]
    [InlineData("remember", ExportPreferencesContract.SaveModeRemember)]
    [InlineData("last", ExportPreferencesContract.SaveModeRemember)]
    [InlineData("auto", ExportPreferencesContract.SaveModeRemember)]
    [InlineData("unexpected", ExportPreferencesContract.SaveModeAsk)]
    public void NormalizeSaveMode_ReturnsExpected(string? input, string expected) {
        var actual = ExportPreferencesContract.NormalizeSaveMode(input);

        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Ensures visual-theme-mode normalization accepts aliases and defaults safely.
    /// </summary>
    [Theory]
    [InlineData(null, ExportPreferencesContract.VisualThemeModePreserveUi)]
    [InlineData("", ExportPreferencesContract.VisualThemeModePreserveUi)]
    [InlineData("preserve_ui_theme", ExportPreferencesContract.VisualThemeModePreserveUi)]
    [InlineData("preserve", ExportPreferencesContract.VisualThemeModePreserveUi)]
    [InlineData("print_friendly", ExportPreferencesContract.VisualThemeModePrintFriendly)]
    [InlineData("print", ExportPreferencesContract.VisualThemeModePrintFriendly)]
    [InlineData("light", ExportPreferencesContract.VisualThemeModePrintFriendly)]
    [InlineData("unexpected", ExportPreferencesContract.VisualThemeModePreserveUi)]
    public void NormalizeVisualThemeMode_ReturnsExpected(string? input, string expected) {
        var actual = ExportPreferencesContract.NormalizeVisualThemeMode(input);

        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Ensures format parsing accepts canonical and alias values.
    /// </summary>
    [Theory]
    [InlineData("csv", true, ExportPreferencesContract.FormatCsv)]
    [InlineData("xlsx", true, ExportPreferencesContract.FormatXlsx)]
    [InlineData("docx", true, ExportPreferencesContract.FormatDocx)]
    [InlineData("md", true, ExportPreferencesContract.FormatMarkdown)]
    [InlineData("markdown", true, ExportPreferencesContract.FormatMarkdown)]
    [InlineData("excel", true, ExportPreferencesContract.FormatXlsx)]
    [InlineData("word", true, ExportPreferencesContract.FormatDocx)]
    [InlineData("unknown", false, "")]
    [InlineData("", false, "")]
    public void TryNormalizeFormat_ReturnsExpected(string input, bool expectedOk, string expectedFormat) {
        var ok = ExportPreferencesContract.TryNormalizeFormat(input, out var normalized);

        Assert.Equal(expectedOk, ok);
        Assert.Equal(expectedFormat, normalized);
    }

    /// <summary>
    /// Ensures last-directory extraction validates directory existence.
    /// </summary>
    [Fact]
    public void NormalizeFromFilePath_ReturnsExistingDirectory() {
        var root = Path.Combine(Path.GetTempPath(), "ixchat-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try {
            var filePath = Path.Combine(root, "export.xlsx");
            var normalized = ExportPreferencesContract.NormalizeFromFilePath(filePath);

            Assert.Equal(root, normalized);
        } finally {
            Directory.Delete(root, recursive: true);
        }
    }
}
