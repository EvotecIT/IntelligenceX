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
}
