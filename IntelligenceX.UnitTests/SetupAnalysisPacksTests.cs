using System;
using System.Linq;
using IntelligenceX.Cli.Setup;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class SetupAnalysisPacksTests {
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(",")]
    [InlineData(" , , ")]
    public void TryNormalizeCsv_Empty_ReturnsDefaults(string? raw) {
        var ok = SetupAnalysisPacks.TryNormalizeCsv(raw, out var normalized, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Null(normalized);
    }

    [Fact]
    public void TryNormalizeCsv_TrimsAndDedupes() {
        var ok = SetupAnalysisPacks.TryNormalizeCsv(" all-50 , powershell-50,all-50 ", out var normalized, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal("all-50,powershell-50", normalized);
    }

    [Theory]
    [InlineData("--force")]
    [InlineData("-x")]
    [InlineData(" all 50 ")]
    [InlineData("all-50\npowershell-50")]
    [InlineData("\"all-50\"")]
    [InlineData("all-50, powershell-50;rm -rf")]
    [InlineData("all-50, powershell-50/extra")]
    [InlineData("all-50, _bad")]
    [InlineData("bad-")]
    public void TryNormalizeCsv_InvalidIds_Fails(string raw) {
        var ok = SetupAnalysisPacks.TryNormalizeCsv(raw, out var normalized, out var error);

        Assert.False(ok);
        Assert.Null(normalized);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void TryNormalizeCsv_TooManyIds_Fails() {
        var ids = Enumerable.Range(0, 101).Select(i => $"id{i}");
        var raw = string.Join(",", ids);

        var ok = SetupAnalysisPacks.TryNormalizeCsv(raw, out var normalized, out var error);

        Assert.False(ok);
        Assert.Null(normalized);
        Assert.Contains("Too many", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryNormalizeCsv_TooLong_Fails() {
        // 100 ids of length 30 -> >2048 when comma-joined
        var ids = Enumerable.Range(0, 100).Select(i => $"id{i:D3}_" + new string('a', 24));
        var raw = string.Join(",", ids);

        var ok = SetupAnalysisPacks.TryNormalizeCsv(raw, out var normalized, out var error);

        Assert.False(ok);
        Assert.Null(normalized);
        Assert.Contains("too long", error, StringComparison.OrdinalIgnoreCase);
    }
}

