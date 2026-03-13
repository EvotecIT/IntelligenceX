using System;
using IntelligenceX.Chat.ExportArtifacts;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Guards the explicit OfficeIMO Word markdown runtime contract used by DOCX export flows.
/// </summary>
public sealed class OfficeImoWordMarkdownRuntimeContractTests {
    /// <summary>
    /// Verifies DOCX runtime diagnostics advertise the current minimum supported package version.
    /// </summary>
    [Fact]
    public void DescribeWordMarkdownContract_ReportsMinimumPublishedVersion() {
        var description = OfficeImoWordMarkdownRuntimeContract.DescribeWordMarkdownContract();

        Assert.Contains("OfficeIMO.Word.Markdown", description, StringComparison.Ordinal);
        Assert.Contains("expected>=1.0.7", description, StringComparison.Ordinal);
        Assert.Contains("status=", description, StringComparison.Ordinal);
    }
}
