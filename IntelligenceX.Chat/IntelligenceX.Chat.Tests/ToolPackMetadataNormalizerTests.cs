using IntelligenceX.Chat.Abstractions.Policy;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class ToolPackMetadataNormalizerTests {
    [Theory]
    [InlineData("AD", "active_directory")]
    [InlineData("Active Directory", "active_directory")]
    [InlineData("ad-playground", "active_directory")]
    [InlineData("ComputerX", "system")]
    [InlineData("EventViewerX", "eventlog")]
    [InlineData("fs", "filesystem")]
    [InlineData("Mailozaurr", "email")]
    [InlineData("Reviewer Setup", "reviewer_setup")]
    public void NormalizePackId_UsesCanonicalChatContractAliases(string input, string expected) {
        var normalized = ToolPackMetadataNormalizer.NormalizePackId(input);

        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("ad", " Active Directory ", "Active Directory")]
    [InlineData("ad", null, "Active Directory")]
    [InlineData("eventviewerx", null, "Event Log")]
    [InlineData("mailozaurr", null, "Email")]
    [InlineData("testimox_analytics", null, "TestimoX Analytics")]
    [InlineData("custom_pack", null, "Custom Pack")]
    public void ResolveDisplayName_PrefersExplicitName_AndFallsBackToHumanFriendlyCanonicalLabel(
        string descriptorId,
        string? fallbackName,
        string expected) {
        var displayName = ToolPackMetadataNormalizer.ResolveDisplayName(descriptorId, fallbackName);

        Assert.Equal(expected, displayName);
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
