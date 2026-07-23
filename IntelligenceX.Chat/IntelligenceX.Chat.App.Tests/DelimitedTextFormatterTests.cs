using System;
using Xunit;

namespace IntelligenceX.Chat.App.Tests;

/// <summary>
/// Guards spreadsheet-safe CSV and TSV projection for copied and exported tool results.
/// </summary>
public sealed class DelimitedTextFormatterTests {
    /// <summary>
    /// Ensures CSV cells cannot become formulas when opened by a spreadsheet application.
    /// </summary>
    [Fact]
    public void FormatCsv_NeutralizesSpreadsheetFormulaPrefixes() {
        var csv = DelimitedTextFormatter.FormatCsv(new[] {
            new[] { "=2+2", "@SUM(A1,A2)", " -42", "safe" }
        });

        Assert.Equal("'=2+2,\"'@SUM(A1,A2)\",' -42,safe", csv);
    }

    /// <summary>
    /// Ensures pasted TSV cells receive the same formula protection after whitespace cleanup.
    /// </summary>
    [Fact]
    public void FormatTsv_NeutralizesSpreadsheetFormulaPrefixes() {
        var tsv = DelimitedTextFormatter.FormatTsv(new[] {
            new[] { "=2+2", " +SUM(A1:A2)", "-42", "@cmd", "safe" }
        });

        Assert.Equal("'=2+2\t'+SUM(A1:A2)\t'-42\t'@cmd\tsafe", tsv);
    }
}
