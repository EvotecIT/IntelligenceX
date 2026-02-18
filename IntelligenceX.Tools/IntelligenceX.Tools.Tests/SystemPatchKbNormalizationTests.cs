using IntelligenceX.Tools.System;
using Xunit;

namespace IntelligenceX.Tools.Tests;

public class SystemPatchKbNormalizationTests {
    [Fact]
    public void NormalizeDistinct_ShouldCanonicalizeAndDeduplicate() {
        var normalized = SystemPatchKbNormalization.NormalizeDistinct(new[] {
            "KB5034441",
            "kb 5034441",
            "Security update KB5034442 for Windows",
            "5034443"
        });

        Assert.Equal(new[] { "KB5034441", "KB5034442", "KB5034443" }, normalized);
    }

    [Fact]
    public void MatchesContainsFilter_ShouldMatchSpacedKbFilter() {
        var matched = SystemPatchKbNormalization.MatchesContainsFilter(
            values: new[] { "KB5034441" },
            filter: "KB 5034441");

        Assert.True(matched);
    }

    [Fact]
    public void MatchesContainsFilter_ShouldMatchNumericKbFilter() {
        var matched = SystemPatchKbNormalization.MatchesContainsFilter(
            values: new[] { "Cumulative Update (KB5034441)" },
            filter: "5034441");

        Assert.True(matched);
    }

    [Fact]
    public void MatchesContainsFilter_ShouldReturnFalseWhenNoKbMatch() {
        var matched = SystemPatchKbNormalization.MatchesContainsFilter(
            values: new[] { "KB5034441" },
            filter: "KB9999999");

        Assert.False(matched);
    }
}
