using IntelligenceX.Tools;
using Xunit;

namespace IntelligenceX.UnitTests;

public sealed class ToolRoutingTaxonomyTests {

    [Theory]
    [InlineData("scope:domain", "scope", "domain")]
    [InlineData("operation:search", "operation", "search")]
    [InlineData("entity:user", "entity", "user")]
    [InlineData("risk:high", "risk", "high")]
    [InlineData("routing:explicit", "routing", "explicit")]
    [InlineData("  RISK:LOW  ", "risk", "LOW")]
    public void TryGetTagKeyValue_ShouldReturnExpectedKeyAndValue_ForValidTags(
        string tag,
        string expectedKey,
        string expectedValue) {
        var ok = ToolRoutingTaxonomy.TryGetTagKeyValue(tag, out var key, out var value);

        Assert.True(ok);
        Assert.Equal(expectedKey, key);
        Assert.Equal(expectedValue, value);
    }

    [Theory]
    [InlineData("scope:")]
    [InlineData("operation:   ")]
    [InlineData("entity:")]
    [InlineData("risk:")]
    [InlineData("routing:")]
    [InlineData("not_a_taxonomy_tag")]
    [InlineData("")]
    [InlineData("   ")]
    public void TryGetTagKeyValue_ShouldReturnFalse_ForMalformedOrNonTaxonomyTags(string tag) {
        var ok = ToolRoutingTaxonomy.TryGetTagKeyValue(tag, out var key, out var value);

        Assert.False(ok);
        Assert.Equal(string.Empty, key);
        Assert.Equal(string.Empty, value);
    }

    [Theory]
    [InlineData(ToolRoutingTaxonomy.RoleOperational)]
    [InlineData(ToolRoutingTaxonomy.RolePackInfo)]
    [InlineData(ToolRoutingTaxonomy.RoleEnvironmentDiscover)]
    [InlineData(ToolRoutingTaxonomy.RoleResolver)]
    [InlineData(ToolRoutingTaxonomy.RoleDiagnostic)]
    public void IsAllowedRole_ShouldReturnTrue_ForSupportedRoles(string role) {
        Assert.True(ToolRoutingTaxonomy.IsAllowedRole(role));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("unknown")]
    [InlineData("packinfo")]
    public void IsAllowedRole_ShouldReturnFalse_ForUnsupportedRoles(string role) {
        Assert.False(ToolRoutingTaxonomy.IsAllowedRole(role));
    }

    [Theory]
    [InlineData(ToolRoutingTaxonomy.SourceExplicit)]
    [InlineData(ToolRoutingTaxonomy.SourceInferred)]
    public void IsAllowedSource_ShouldReturnTrue_ForSupportedSources(string source) {
        Assert.True(ToolRoutingTaxonomy.IsAllowedSource(source));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("unknown")]
    [InlineData("explicitly")]
    public void IsAllowedSource_ShouldReturnFalse_ForUnsupportedSources(string source) {
        Assert.False(ToolRoutingTaxonomy.IsAllowedSource(source));
    }
}
