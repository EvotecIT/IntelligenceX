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
    public void ResolveDisplayName_PrefersExplicitName_AndFallsBackToHumanFriendlyCanonicalLabel() {
        Assert.Equal("Active Directory", ToolPackMetadataNormalizer.ResolveDisplayName("ad", " Active Directory "));
        Assert.Equal("Active Directory", ToolPackMetadataNormalizer.ResolveDisplayName("ad", null));
        Assert.Equal("Custom Pack", ToolPackMetadataNormalizer.ResolveDisplayName("custom_pack", null));
    }

    [Theory]
    [InlineData("builtin", ToolPackSourceKind.Builtin)]
    [InlineData("public", ToolPackSourceKind.OpenSource)]
    [InlineData("internal", ToolPackSourceKind.ClosedSource)]
    [InlineData(null, ToolPackSourceKind.OpenSource)]
    public void ResolveSourceKind_NormalizesExpectedContractValues(string? input, ToolPackSourceKind expected) {
        var sourceKind = ToolPackMetadataNormalizer.ResolveSourceKind(input);

        Assert.Equal(expected, sourceKind);
    }
}
