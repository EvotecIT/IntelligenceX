using IntelligenceX.Chat.Abstractions.Policy;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class StartupWarningPreviewFormatterTests {
    [Fact]
    public void BuildLines_TruncatesAndAddsFooterUsingSharedPreviewPolicy() {
        var lines = StartupWarningPreviewFormatter.BuildLines(
            new[] { "one", "two", "three", "four", "five" },
            static item => item,
            "Warnings:",
            "Found {0} warning(s):",
            "Inspect full details in diagnostics.");

        Assert.Equal("Warnings:", lines[0]);
        Assert.Contains("Found 5 warning(s):", lines);
        Assert.Contains("- one", lines);
        Assert.Contains("- four", lines);
        Assert.DoesNotContain("- five", lines);
        Assert.Contains("- +1 more", lines);
        Assert.Equal("Inspect full details in diagnostics.", lines[^1]);
    }

    [Fact]
    public void BuildLines_UsesDefaultPreviewLimitWhenConfiguredLimitIsInvalid() {
        var lines = StartupWarningPreviewFormatter.BuildLines(
            new[] { "one", "two", "three", "four", "five" },
            static item => item,
            "Warnings:",
            "Found {0} warning(s):",
            maxShown: 0);

        Assert.DoesNotContain("- five", lines);
        Assert.Contains("- +1 more", lines);
    }

    [Fact]
    public void BuildLines_SkipsEmptySectionWhenNoRenderedItemsRemain() {
        var lines = StartupWarningPreviewFormatter.BuildLines(
            new[] { " ", string.Empty, "\t" },
            static item => item,
            "Warnings:",
            "Found {0} warning(s):",
            "Inspect full details in diagnostics.");

        Assert.Empty(lines);
    }

    [Fact]
    public void BuildLines_CountsOnlyRenderedItemsWhenSomeInputsFormatToEmpty() {
        var lines = StartupWarningPreviewFormatter.BuildLines(
            new[] { " ", "one", "", "two", "three" },
            static item => item,
            "Warnings:",
            "Found {0} warning(s):",
            maxShown: 2);

        Assert.Contains("Found 3 warning(s):", lines);
        Assert.Contains("- one", lines);
        Assert.Contains("- two", lines);
        Assert.DoesNotContain("- three", lines);
        Assert.Contains("- +1 more", lines);
    }
}
