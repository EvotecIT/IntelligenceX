using IntelligenceX.Chat.Abstractions.Policy;
using Xunit;

namespace IntelligenceX.Chat.Tests;

public sealed class StartupToolHealthWarningFormatterTests {
    [Fact]
    public void BuildDisplayParts_NormalizesAliasPackIdsAndFormatsStructuredWarning() {
        var parts = StartupToolHealthWarningFormatter.BuildDisplayParts(
            "[tool health][open_source][ADPlayground] ad_pack_info failed (smoke_not_configured): Select a domain before running the startup probe.",
            normalizedPackId => string.Equals(normalizedPackId, "active_directory", System.StringComparison.OrdinalIgnoreCase)
                ? "Active Directory"
                : null);

        Assert.NotNull(parts);
        Assert.Equal("Active Directory (Open)", parts.Value.Title);
        Assert.Equal("startup smoke check is not configured: Select a domain before running the startup probe.", parts.Value.Summary);
    }

    [Theory]
    [InlineData("dnsclientx", "DnsClientX")]
    [InlineData("ADPlayground", "Active Directory")]
    [InlineData("reviewer_setup", "Reviewer Setup")]
    public void ResolvePackDisplayLabel_HumanizesKnownCanonicalPackIds(string packId, string expectedLabel) {
        var label = StartupToolHealthWarningFormatter.ResolvePackDisplayLabel(packId, fallbackName: null);

        Assert.Equal(expectedLabel, label);
    }
}
