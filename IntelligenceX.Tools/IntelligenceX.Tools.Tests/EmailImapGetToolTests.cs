using IntelligenceX.Tools.Email;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public sealed class EmailImapGetToolTests {
    [Fact]
    public void CreateSummaryPreview_WhenTextBodyIsNull_ShouldReturnEmptyString() {
        var preview = EmailImapGetTool.CreateSummaryPreview(textBody: null);

        Assert.Equal(string.Empty, preview);
    }

    [Fact]
    public void CreateSummaryPreview_WhenTextBodyExceedsLimit_ShouldTruncate() {
        var preview = EmailImapGetTool.CreateSummaryPreview(new string('a', 12), previewMax: 5);

        Assert.Equal("aaaaa...", preview);
    }
}
