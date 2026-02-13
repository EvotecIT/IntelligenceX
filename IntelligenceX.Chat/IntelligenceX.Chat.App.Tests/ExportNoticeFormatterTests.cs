using IntelligenceX.Chat.App.Conversation;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Tests for export notice rendering.
/// </summary>
public sealed class ExportNoticeFormatterTests {
    /// <summary>
    /// Ensures invalid-format failures render consistent status/system/dataview text.
    /// </summary>
    [Fact]
    public void InvalidFormat_Failure_RendersExpectedTexts() {
        var notice = ExportNotice.Failed(ExportNoticeKind.InvalidFormat, "");

        Assert.Equal("Export failed", ExportNoticeFormatter.Status(notice));
        Assert.Equal("Export failed: format is empty.", ExportNoticeFormatter.SystemText(notice));
        Assert.Equal("Export failed: format is empty.", ExportNoticeFormatter.DataViewText(notice));
        Assert.False(notice.Ok);
    }

    /// <summary>
    /// Ensures tool error failures include details in both outputs.
    /// </summary>
    [Fact]
    public void ToolError_Failure_IncludesDetail() {
        var notice = ExportNotice.Failed(ExportNoticeKind.ToolError, "excel", "permission denied");

        Assert.Equal("Export failed: permission denied", ExportNoticeFormatter.SystemText(notice));
        Assert.Equal("Export failed: permission denied", ExportNoticeFormatter.DataViewText(notice));
    }

    /// <summary>
    /// Ensures completed exports with file path show full path in system and filename in data-view.
    /// </summary>
    [Fact]
    public void Completed_WithFilePath_UsesPathAndFileName() {
        var notice = ExportNotice.Succeeded("excel", @"C:\temp\export.xlsx");

        Assert.Equal("Exported", ExportNoticeFormatter.Status(notice));
        Assert.Equal(@"Exported excel: C:\temp\export.xlsx", ExportNoticeFormatter.SystemText(notice));
        Assert.Equal("Exported excel: export.xlsx", ExportNoticeFormatter.DataViewText(notice));
        Assert.True(notice.Ok);
    }

    /// <summary>
    /// Ensures completed exports without path report generic completion.
    /// </summary>
    [Fact]
    public void Completed_WithoutFilePath_UsesCompletedText() {
        var notice = ExportNotice.Succeeded("word");

        Assert.Equal("Export completed.", ExportNoticeFormatter.SystemText(notice));
        Assert.Equal("Exported word.", ExportNoticeFormatter.DataViewText(notice));
    }
}
