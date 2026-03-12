using IntelligenceX.Chat.Abstractions.Policy;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class ToolPackMetadataNormalizerTests {
    [Theory]
    [InlineData("AD", "active_directory")]
    [InlineData("Active Directory", "active_directory")]
    [InlineData("ad-playground", "active_directory")]
    [InlineData("ComputerX", "system")]
    [InlineData("fs", "filesystem")]
    [InlineData("Reviewer Setup", "reviewer_setup")]
    public void NormalizePackId_UsesCanonicalChatContractAliases(string input, string expected) {
        var normalized = ToolPackMetadataNormalizer.NormalizePackId(input);

        Assert.Equal(expected, normalized);
    }

    [Fact]
    public void ResolveDisplayName_PrefersExplicitName_AndFallsBackToCanonicalId() {
        Assert.Equal("Active Directory", ToolPackMetadataNormalizer.ResolveDisplayName("ad", " Active Directory "));
        Assert.Equal("active_directory", ToolPackMetadataNormalizer.ResolveDisplayName("ad", null));
    }

    [Theory]
    [InlineData("builtin", ToolPackSourceKind.Builtin)]
    [InlineData("public", ToolPackSourceKind.OpenSource)]
    [InlineData("internal", ToolPackSourceKind.ClosedSource)]
    [InlineData(null, ToolPackSourceKind.OpenSource)]
    public void ResolveSourceKind_NormalizesExpectedContractValues(string? input, ToolPackSourceKind expected) {
        var sourceKind = ToolPackMetadataNormalizer.ResolveSourceKind(input, "eventlog");

        Assert.Equal(expected, sourceKind);
    }
}
