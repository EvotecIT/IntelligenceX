using System;
using IntelligenceX.Chat.Tooling;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class ToolPackBootstrapMetadataTests {
    [Fact]
    public void NormalizeSourceKind_Throws_WhenSourceKindMissing() {
        Assert.Throws<ArgumentException>(() => ToolPackBootstrap.NormalizeSourceKind(sourceKind: null, descriptorId: "system"));
    }

    [Theory]
    [InlineData("open_source", "open_source")]
    [InlineData("public", "open_source")]
    [InlineData("closed_source", "closed_source")]
    [InlineData("private", "closed_source")]
    [InlineData("builtin", "builtin")]
    public void NormalizeSourceKind_NormalizesKnownValues(string input, string expected) {
        var normalized = ToolPackBootstrap.NormalizeSourceKind(input);
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("computerx", "system")]
    [InlineData("adplayground", "ad")]
    [InlineData("reviewer_setup_pack", "reviewersetup")]
    [InlineData("IX.FileSystem", "fs")]
    public void NormalizePackId_UsesCanonicalAliases(string input, string expected) {
        var normalized = ToolPackBootstrap.NormalizePackId(input);
        Assert.Equal(expected, normalized);
    }
}
